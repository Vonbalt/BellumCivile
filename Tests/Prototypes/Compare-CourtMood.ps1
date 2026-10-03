$ErrorActionPreference='Stop'
# Pure arithmetic proposal, not the production mood model. Bloc order: Nobility, Glory, Liberty.
function Land([int[]]$holdings) {
    $n=$holdings.Count; $total=($holdings | Measure-Object -Sum).Sum
    if($n -lt 3 -or $total -le 0){return 0}
    $k=[int][Math]::Ceiling($n/3.0)
    $top=($holdings | Sort-Object -Descending | Select-Object -First $k | Measure-Object -Sum).Sum
    if(5*$n*$top -le 6*$k*$total){return -10}
    if(5*$n*$top -ge 9*$k*$total){return 10}
    return 0
}
function Legality([int]$mismatches,[int]$assessed) {
    if($assessed -le 0){return 0}
    if($mismatches -eq 0){return 10}
    if(4*$mismatches -ge $assessed){return -10}
    return 0
}
function Welfare([int]$starving,[int]$towns,[double]$prosperity) {
    if($towns -le 0){return 0}
    if(4*$starving -ge $towns){return -15}
    if($prosperity -gt 5000){return 10}
    return 0
}
function Mood($world,[int[]]$seats,[int]$central,[int[]]$policy,[double[]]$advisor) {
    $filled=($seats | Measure-Object -Sum).Sum
    $raw=@(0..2 | ForEach-Object { $world[$_]+10*$seats[$_]-5*$filled-5*$central+$policy[$_]+$advisor[$_] })
    return [pscustomobject]@{raw=$raw;clamped=@($raw | ForEach-Object {[Math]::Max(-100,[Math]::Min(100,$_))})}
}
foreach($n in 3..30){if((Land ([int[]]@(1)*$n)) -ne -10){throw 'Equal distribution failed'}}
if((Land @(3,1,1)) -ne 10 -or (Land @(8,1,1)) -ne 10 -or (Land @(0,0,0)) -ne 0 -or (Land @(9,1)) -ne 0){throw 'Land boundary failed'}
if((Legality 0 0) -ne 0 -or (Legality 0 4) -ne 10 -or (Legality 1 4) -ne -10 -or (Legality 1 5) -ne 0){throw 'Legal boundary failed'}
if((Welfare 0 0 9000) -ne 0 -or (Welfare 1 4 9000) -ne -15 -or (Welfare 1 5 9000) -ne 10 -or (Welfare 0 5 5000) -ne 0){throw 'Welfare boundary failed'}
$landReport=& powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot/Inspect-CourtLand.ps1" | ConvertFrom-Json
if($LASTEXITCODE -ne 0){throw 'Land survey failed'}
$land=@($landReport.realms | ForEach-Object {[pscustomobject]@{realm=$_.realm;nobility=(Land $_.holdings);liberty=-(Land $_.holdings)}})
$worlds=@(
    @{name='Peace, lawful, no treaties, ordinary welfare';scores=@(10,-10,0)},
    @{name='Peace, lawful, alliance/trade, prosperous';scores=@(30,-10,30)},
    @{name='War, lawful, alliance/trade, prosperous, receiving tribute';scores=@(30,20,10)},
    @{name='War, internal conflict, unlawful, no treaties, hunger, paying tribute';scores=@(-35,0,-35)}
)
# World scores assume neutral land concentration and matured war/peace counters.
$scenarios=@()
foreach($world in $worlds){foreach($central in @(0,2,4,6)){
    $scores=Mood $world.scores @(2,2,2) $central @(0,0,0) @(0,0,0)
    $scenarios += [pscustomobject]@{world=$world.name;central=$central;scores=$scores.raw}
}}
$councils=@(@(2,2,2),@(4,1,1),@(6,0,0))
$councilResults=@($councils | ForEach-Object {$seats=$_; [pscustomobject]@{seats=$seats;result=(Mood @(0,0,0) $seats 0 @(0,0,0) @(0,0,0)).raw}})
$sweep=@()
foreach($world in $worlds){
    $cases=0;$unhappy=@(0,0,0);$coalition=@(0,0,0);$clamped=@(0,0,0)
    for($n=0;$n -le 6;$n++){for($g=0;$g -le 6-$n;$g++){
        $seats=@($n,$g,(6-$n-$g))
        foreach($central in 0..6){foreach($policyNet in @(-20,0,20)){
            $m=Mood $world.scores $seats $central @($policyNet,$policyNet,$policyNet) @(0,0,0)
            $cases++
            for($b=0;$b -lt 3;$b++){
                if($m.clamped[$b] -le -20){$unhappy[$b]++}
                if($m.clamped[$b] -le -60){$coalition[$b]++}
                if($m.raw[$b] -ne $m.clamped[$b]){$clamped[$b]++}
            }
        }}
    }}
    $sweep += [pscustomobject]@{world=$world.name;cases=$cases;at_or_below_minus20=$unhappy;at_or_below_minus60=$coalition;clamp_cases=$clamped}
}
[pscustomobject]@{
    scope='Offline sensitivity, not campaign predictions. XML-backed land only. World conditions, policy net balances and councils hypothetical. Centralization burden is additional to ideological policy net. Six filled eligible bloc-affiliated seats; no absent-bloc assumptions tested.'
    tests='Equal holdings for 3..30 houses; land, legality and hunger boundaries passed'
    land=$land;balanced_council_scenarios=$scenarios;councils=$councilResults;sweep=$sweep
    quarter_thresholds=@(1..12 | ForEach-Object {[pscustomobject]@{assessed=$_;required=[int][Math]::Ceiling($_/4.0)}})
} | ConvertTo-Json -Depth 8
