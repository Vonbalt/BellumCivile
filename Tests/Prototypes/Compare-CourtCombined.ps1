param([string]$ModuleData)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../GamePath.ps1"
$ModuleData = Resolve-BellumModuleData $ModuleData
$p = & "$PSScriptRoot/Compare-CourtPersonality.ps1" -ModuleData $ModuleData | ConvertFrom-Json
$s = & powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot/Inspect-CourtSkills.ps1" -ModuleData $ModuleData | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Skill survey failed.' }
$owners=($p.reports | Where-Object candidate -eq 'C_standardized_strengths').owner_scores
$skills=@{}; foreach($row in $s.owner_skills) { $skills[$row.hero]=$row.skills }
$settings=New-Object System.Xml.XmlReaderSettings
$settings.DtdProcessing=[System.Xml.DtdProcessing]::Prohibit
$reader=[System.Xml.XmlReader]::Create((Join-Path $ModuleData 'spclans.xml'),$settings)
try { $clans=New-Object System.Xml.XmlDocument; $clans.Load($reader) } finally { $reader.Dispose() }
$tiers=@{}; foreach($node in $clans.SelectNodes('/Factions/Faction')) { if($node.HasAttribute('tier')) { $tiers[$node.GetAttribute('id')]=[int]$node.GetAttribute('tier') } }
function Competence([double]$value) {
    if($value -le 60) { return 0.0 }
    if($value -lt 100) { return ($value-60)/40 }
    return [Math]::Min(4,1+($value-100)/50)
}
function Training($v) {
    $weapon=(@('OneHanded','TwoHanded','Polearm','Bow','Crossbow','Throwing') | ForEach-Object { [double]$v.$_ } | Measure-Object -Maximum).Maximum
    $raw=@(
        ((Competence $v.Steward)+(Competence $v.Leadership))/2
        ((Competence $v.Tactics)+(Competence $weapon))/2
        ((Competence $v.Charm)+(Competence $v.Trade))/2
    )
    $min=($raw | Measure-Object -Minimum).Minimum
    return @($raw | ForEach-Object { $_-$min })
}
function Winners($scores) {
    $max=($scores | Measure-Object -Maximum).Maximum
    return @(0..2 | Where-Object { [Math]::Abs($scores[$_]-$max) -lt 0.000001 })
}
function Switches([double]$gap,[double]$threshold) { return $gap -ge $threshold }
$anchors=@(0,60,80,100,150,200,250,300); $expected=@(0,0,0.5,1,2,3,4,4)
for($i=0;$i -lt $anchors.Count;$i++) { if((Competence $anchors[$i]) -ne $expected[$i]) { throw 'Skill anchor failed.' } }
if((Switches 5.99 6) -or -not (Switches 6 6) -or (Switches 9.99 10) -or -not (Switches 10 10)) { throw 'Switch boundary failed.' }
$generalist=@{}; foreach($id in @('Steward','Leadership','Tactics','OneHanded','Charm','Trade')) { $generalist[$id]=200 }
if(@(Training $generalist | Where-Object {$_ -ne 0}).Count) { throw 'Generalist bias.' }
$trained=@{}; foreach($owner in $owners) {
    if(-not $tiers.ContainsKey($owner.clan) -or -not $skills.ContainsKey($owner.hero)) { throw 'Unresolved owner.' }
    $trained[$owner.hero]=@(Training $skills[$owner.hero])
    foreach($bonus in $trained[$owner.hero]) { if($bonus -lt 0 -or $bonus -gt 4) { throw 'Training cap failed.' } }
}
$rankResults=@(); $switchResults=@()
$scenarios=@(
    @{name='Friend 80';pull=3}, @{name='One marriage';pull=2},
    @{name='Immediate liege both trees';pull=4}, @{name='Friend 80 plus marriage';pull=5},
    @{name='Friend 100 plus marriage';pull=6},
    @{name='Friend 100 plus immediate liege';pull=8},
    @{name='Friend 100 marriage and liege';pull=10}, @{name='Maximum ties';pull=12}
)
foreach($rank in 0..4) {
    $wins=@(0,0,0); $ties=0; $changed=0; $comparable=0; $trainingChanged=0
    $baselines=@()
    foreach($owner in $owners) {
        $political=@((2*$rank),[Math]::Min(8,[Math]::Max(0,2*($tiers[$owner.clan]-2))),(8-2*$rank))
        $without=@(0..2 | ForEach-Object { $owner.scores[$_]+$political[$_] })
        $total=@(0..2 | ForEach-Object { $without[$_]+$trained[$owner.hero][$_] })
        $winner=@(Winners $total); $old=@(Winners $owner.scores); $noTraining=@(Winners $without)
        if($winner.Count -eq 1) { $wins[$winner[0]]++ } else { $ties++ }
        if($winner.Count -eq 1 -and $old.Count -eq 1) { $comparable++; if($winner[0] -ne $old[0]) { $changed++ } }
        if(($winner -join ',') -ne ($noTraining -join ',')) { $trainingChanged++ }
        if($winner.Count -eq 1) { $baselines += [pscustomobject]@{scores=$total;current=$winner[0]} }
    }
    $rankResults += [pscustomobject]@{rank=@('None','Barony','County','Duchy','Kingdom/Empire')[$rank];winners=$wins;ties=$ties;unique_comparisons=$comparable;changed_from_personality=$changed;training_changed_winner_set=$trainingChanged}
    foreach($scenario in $scenarios) {
        $tested=0; $newPreferences=0; $member=0; $leader=0; $atFour=0; $atEight=0
        foreach($base in $baselines) {
            foreach($target in 0..2) {
                if($target -eq $base.current) { continue }
                $tested++
                $gap=$base.scores[$target]+$scenario.pull-$base.scores[$base.current]
                if($gap -gt 0.000001) { $newPreferences++ }
                if(Switches $gap 6) { $member++ }
                if(Switches $gap 10) { $leader++ }
                if(Switches $gap 4) { $atFour++ }
                if(Switches $gap 8) { $atEight++ }
            }
        }
        $switchResults += [pscustomobject]@{rank=$rank;scenario=$scenario.name;pull=$scenario.pull;comparisons=$tested;new_preferences=$newPreferences;member_switches=$member;leader_switches=$leader;switches_at_4=$atFour;switches_at_8=$atEight}
    }
}
[pscustomobject]@{
    scope='73 SandBox XML owners swept through five hypothetical title categories. Not a campaign population. Social scenarios add ties to one alternative from a unique combined baseline with zero initial ties. Tied baselines excluded from switching. No runtime skill noise, DLC, temporal simulation or political changes during switching.'
    tests='Skill anchors, generalist neutrality, training caps, threshold boundaries passed'
    source_hashes=$s.source_hashes; ranks=$rankResults; switching=$switchResults
    training_means=@(0..2 | ForEach-Object { $b=$_; [Math]::Round(($owners | ForEach-Object {$trained[$_.hero][$b]} | Measure-Object -Average).Average,3) })
} | ConvertTo-Json -Depth 8
