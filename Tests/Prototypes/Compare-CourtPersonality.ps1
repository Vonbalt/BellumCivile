param(
    [string]$ModuleData,
    [switch]$SyntheticOnly
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../GamePath.ps1"
if (-not $SyntheticOnly) { $ModuleData = Resolve-BellumModuleData $ModuleData }
$configPath = Join-Path $PSScriptRoot 'court-personality-candidates.json'
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
if (($config.axes -join ',') -ne 'Honor,Generosity,Mercy,Valor,Calculating' -or $config.blocs.Count -ne 3) {
    throw 'Unexpected prototype axes or bloc count.'
}

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
public sealed class CourtPersonalityPrototype {
 public int[] Wins = new int[3];
 public double[] TieSplitWins = new double[3];
 public int Ties, Count, SmallMargins, ClearMargins;
 public int[] Min = {int.MaxValue,int.MaxValue,int.MaxValue};
 public int[] Max = {int.MinValue,int.MinValue,int.MinValue};
 public double[] Totals = new double[3];
 public double MarginTotal;
 public static int[] Score(int[] traits,int[] weights) {
  if(traits.Length!=5 || weights.Length!=15)throw new Exception("Invalid dimensions");
  var score=new int[3];
  for(int a=0;a<5;a++) {
   if(traits[a]<-2 || traits[a]>2)throw new Exception("Trait outside -2..2");
   for(int b=0;b<3;b++)score[b]+=traits[a]*weights[a*3+b];
  }
  return score;
 }
 public void Add(int[] traits,int[] weights) {
  int[] score=Score(traits,weights);Count++;
  int maximum=Math.Max(score[0],Math.Max(score[1],score[2])), tied=0;
  for(int b=0;b<3;b++) {
   Min[b]=Math.Min(Min[b],score[b]);Max[b]=Math.Max(Max[b],score[b]);Totals[b]+=score[b];
   if(score[b]==maximum)tied++;
  }
  if(tied>1)Ties++;
  for(int b=0;b<3;b++)if(score[b]==maximum) {TieSplitWins[b]+=1.0/tied;if(tied==1)Wins[b]++;}
  Array.Sort(score);int margin=score[2]-score[1];MarginTotal+=margin;
  if(margin<10)SmallMargins++;if(margin>=20)ClearMargins++;
 }
}
'@

function Read-Xml([string]$Path) {
    $settings = New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $reader = [System.Xml.XmlReader]::Create($Path, $settings)
    try { $doc = New-Object System.Xml.XmlDocument; $doc.Load($reader); return ,$doc }
    finally { $reader.Dispose() }
}

$owners = @()
$hashes = @()
$skipped = @()
if (-not $SyntheticOnly) {
    foreach ($name in @('lords.xml', 'heroes.xml', 'spclans.xml')) {
        $path = Join-Path $ModuleData $name
        if (-not (Test-Path -LiteralPath $path)) { throw "Missing $path. Use -SyntheticOnly to omit XML input." }
        $hashes += [pscustomobject]@{ file = $name; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    }
    $lords = Read-Xml (Join-Path $ModuleData 'lords.xml')
    $heroes = Read-Xml (Join-Path $ModuleData 'heroes.xml')
    $clans = Read-Xml (Join-Path $ModuleData 'spclans.xml')
    $characters = @{}
    foreach ($node in $lords.SelectNodes('/NPCCharacters/NPCCharacter')) { $characters[$node.GetAttribute('id')] = $node }
    $heroRecords = @{}
    foreach ($node in $heroes.SelectNodes('/Heroes/Hero')) { $heroRecords[$node.GetAttribute('id')] = $node }
    foreach ($clan in $clans.SelectNodes('/Factions/Faction')) {
        if ($clan.GetAttribute('id') -eq 'player_faction' -or $clan.GetAttribute('is_minor_faction') -eq 'true' -or
            $clan.GetAttribute('is_bandit') -eq 'true' -or [string]::IsNullOrEmpty($clan.GetAttribute('super_faction'))) { continue }
        $ownerRef = $clan.GetAttribute('owner')
        if (-not $ownerRef.StartsWith('Hero.')) { $skipped += $clan.GetAttribute('id'); continue }
        $id = $ownerRef.Substring(5)
        $character = $characters[$id]
        $hero = $heroRecords[$id]
        if ($null -eq $character -or $null -eq $hero -or $hero.GetAttribute('alive') -eq 'false') {
            $skipped += $clan.GetAttribute('id'); continue
        }
        $traits = New-Object int[] 5
        $declared = New-Object bool[] 5
        foreach ($trait in $character.SelectNodes('Traits/Trait')) {
            $index = [Array]::IndexOf([string[]]$config.axes, $trait.GetAttribute('id'))
            if ($index -ge 0) { $traits[$index] = [int]$trait.GetAttribute('value'); $declared[$index] = $true }
        }
        $owners += [pscustomobject]@{ clan=$clan.GetAttribute('id'); hero=$id; realm=$clan.GetAttribute('super_faction'); traits=$traits; declared=$declared }
    }
    if ($owners.Count -eq 0) { throw 'No declared noble owners resolved; check XML schema.' }
}

function Summarize($stats) {
    $n = [Math]::Max(1, $stats.Count)
    [pscustomobject]@{
        count=$stats.Count; unique_winners=$stats.Wins; tied_cases=$stats.Ties
        tie_split_percent=@($stats.TieSplitWins | ForEach-Object { [Math]::Round(100 * $_ / $n, 2) })
        min=$stats.Min; max=$stats.Max
        mean_score=@($stats.Totals | ForEach-Object { [Math]::Round($_ / $n, 2) })
        mean_margin=[Math]::Round($stats.MarginTotal / $n, 2)
        margin_below_10=$stats.SmallMargins; margin_at_least_20=$stats.ClearMargins
    }
}

$reports = @()
foreach ($candidate in $config.candidates) {
    if ($candidate.weights.Count -ne 5) { throw 'Expected five rows.' }
    $weights = @()
    foreach ($row in $candidate.weights) {
        if ($row.Count -ne 3) { throw 'Expected three bloc weights.' }
        $weights += @($row | ForEach-Object { [int]$_ })
    }
    $weights = [int[]]$weights
    if ($candidate.allowed_weights) {
        foreach ($weight in $weights) {
            if ($weight -notin $candidate.allowed_weights) { throw "Nonstandard weight in $($candidate.id): $weight" }
        }
    }
    $synthetic = New-Object CourtPersonalityPrototype
    for ($i=0; $i -lt 3125; $i++) {
        $traits = New-Object int[] 5
        $remaining = $i
        for ($axis=0; $axis -lt 5; $axis++) { $traits[$axis] = ($remaining % 5) - 2; $remaining = [Math]::Floor($remaining / 5) }
        $synthetic.Add($traits, $weights)
        $s = [CourtPersonalityPrototype]::Score($traits, $weights)
        $inverse = [CourtPersonalityPrototype]::Score([int[]]@($traits | ForEach-Object { -$_ }), $weights)
        for ($b=0; $b -lt 3; $b++) { if ($s[$b] -ne -$inverse[$b]) { throw 'Signed reversal failed.' } }
    }
    for ($b=0; $b -lt 3; $b++) {
        $bound = 0
        for ($a=0; $a -lt 5; $a++) { $bound += 2 * [Math]::Abs($weights[$a*3+$b]) }
        if ($synthetic.Min[$b] -ne -$bound -or $synthetic.Max[$b] -ne $bound -or $synthetic.Totals[$b] -ne 0) { throw 'Range/symmetry test failed.' }
    }
    $all = New-Object CourtPersonalityPrototype
    $byRealm = @{}
    $ownerScores = @()
    foreach ($owner in $owners) {
        $all.Add($owner.traits, $weights)
        if (-not $byRealm.ContainsKey($owner.realm)) { $byRealm[$owner.realm] = New-Object CourtPersonalityPrototype }
        $byRealm[$owner.realm].Add($owner.traits, $weights)
        $ownerScores += [pscustomobject]@{clan=$owner.clan; hero=$owner.hero; realm=$owner.realm; traits=$owner.traits; scores=[CourtPersonalityPrototype]::Score($owner.traits,$weights)}
    }
    $examples = @($config.examples | ForEach-Object { [pscustomobject]@{ name=$_.name; traits=$_.traits; scores=[CourtPersonalityPrototype]::Score([int[]]$_.traits,$weights) } })
    if (($examples[0].scores | Where-Object { $_ -ne 0 }).Count -gt 0) { throw 'Neutral bias.' }
    $reports += [pscustomobject]@{
        candidate=$candidate.id; synthetic=(Summarize $synthetic)
        declared_owners=$(if ($owners.Count) { Summarize $all } else { $null })
        realms=@($byRealm.Keys | Sort-Object | ForEach-Object { [pscustomobject]@{realm=$_; results=(Summarize $byRealm[$_])} })
        examples=$examples; owner_scores=$ownerScores
    }
}
$frequencies = @()
for ($axis=0; $axis -lt 5; $axis++) {
    $bins = @(0,0,0,0,0); $missing=0
    foreach ($owner in $owners) { $bins[$owner.traits[$axis]+2]++; if (-not $owner.declared[$axis]) { $missing++ } }
    $frequencies += [pscustomobject]@{ axis=$config.axes[$axis]; levels_minus2_to_plus2=$bins; unspecified_assumed_zero=$missing }
}
[pscustomobject]@{
    scope='Offline prototype. XML-declared SandBox noble clan owners only; no NavalDLC, mod patches, random generation, active save, political/social scores, or election behavior.'
    tie_policy='Report ties separately; tie-split percentages are descriptive fractional allocation, not actual faction assignment.'
    bloc_order=$config.blocs; files=$hashes; skipped_clans=$skipped; trait_frequencies=$frequencies; reports=$reports
} | ConvertTo-Json -Depth 12
