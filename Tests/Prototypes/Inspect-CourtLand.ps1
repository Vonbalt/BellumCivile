param([string]$ModuleData)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/../GamePath.ps1"
$ModuleData = Resolve-BellumModuleData $ModuleData
function Read-Document($path) {
    $settings=New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing=[System.Xml.DtdProcessing]::Prohibit
    $reader=[System.Xml.XmlReader]::Create($path,$settings)
    try { $doc=New-Object System.Xml.XmlDocument; $doc.Load($reader); return ,$doc }
    finally { $reader.Dispose() }
}
function Assess([int[]]$holdings) {
    $n=$holdings.Count; $total=($holdings | Measure-Object -Sum).Sum
    $topCount=[int][Math]::Ceiling($n/3.0)
    $top=($holdings | Sort-Object -Descending | Select-Object -First $topCount | Measure-Object -Sum).Sum
    $mood=0
    if($n -ge 3 -and $total -gt 0) {
        if(5*$top -le 2*$total) { $mood=-10 }
        elseif(5*$top -ge 3*$total) { $mood=10 }
    }
    [pscustomobject]@{houses=$n;fiefs=$total;largest_houses=$topCount;largest_fiefs=$top;share=$(if($total -gt 0){[Math]::Round(100*$top/$total,2)}else{0});nobility=$mood;liberty=-$mood}
}
if((Assess @(1,1,1)).liberty -ne 10 -or (Assess @(3,1,1)).nobility -ne 10 -or (Assess @(5,0)).nobility -ne 0 -or (Assess @(0,0,0)).nobility -ne 0) { throw 'Boundary test failed.' }
$clans=Read-Document (Join-Path $ModuleData 'spclans.xml')
$kingdoms=Read-Document (Join-Path $ModuleData 'spkingdoms.xml')
$settlements=Read-Document (Join-Path $ModuleData 'settlements.xml')
$counts=@{}
foreach($node in $settlements.SelectNodes('/Settlements/Settlement[Components/Town]')) {
    $owner=$node.GetAttribute('owner')
    if(-not $owner.StartsWith('Faction.')) { throw "Unresolved owner $owner" }
    $id=$owner.Substring(8); $counts[$id]=1+[int]$counts[$id]
}
$results=@()
foreach($realm in $kingdoms.SelectNodes('/Kingdoms/Kingdom')) {
    $id=$realm.GetAttribute('id'); $ruler=$realm.GetAttribute('owner')
    $members=@($clans.SelectNodes('/Factions/Faction') | Where-Object {$_.GetAttribute('super_faction') -eq "Kingdom.$id" -and $_.GetAttribute('is_minor_faction') -ne 'true' -and $_.GetAttribute('is_bandit') -ne 'true'})
    $rulers=@($members | Where-Object {$_.GetAttribute('owner') -eq $ruler})
    if($rulers.Count -ne 1) { throw "Unresolved ruling clan: $id" }
    $vassals=@($members | Where-Object {$_ -ne $rulers[0]})
    $holdings=@($vassals | ForEach-Object {[int]$counts[$_.GetAttribute('id')]})
    $results += [pscustomobject]@{realm=$id;ruler_fiefs=[int]$counts[$rulers[0].GetAttribute('id')];holdings=@($holdings | Sort-Object -Descending);landless=@($holdings | Where-Object {$_ -eq 0}).Count;assessment=(Assess $holdings)}
}
[pscustomobject]@{
    scope='Base SandBox XML ownership only; no NavalDLC, mod transformations, actual save or council appointments. Excludes ruler, includes landless vassals, counts towns and castles equally. Largest third rounds UP.'
    tests='Inclusive 40/60 thresholds, tiny court and zero holdings passed'
    realms=$results
    equal_holdings_rounding=@(3..10 | ForEach-Object { Assess ([int[]]@(1)*$_) })
} | ConvertTo-Json -Depth 7
