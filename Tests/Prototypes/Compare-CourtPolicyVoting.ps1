param([switch]$LibraryOnly)
$ErrorActionPreference = 'Stop'

# Offline proposal, not live policy AI. Mirrors the audited 1.4.8 binary election
# for symmetric outcome merits, explicit likelihood and no cost-modifying perks.
function Clamp([double]$v, [double]$lo, [double]$hi) { return [Math]::Max($lo, [Math]::Min($hi, $v)) }
function Merit([int]$stance, [int]$honor, [double]$stake, [double]$relation,
    [double]$mood, [double]$leader, [bool]$rulerMotion, [bool]$isRuler, [bool]$repeal) {
    $honorMultiplier = 1.0
    if ($honor -gt 0) { $honorMultiplier = 1.25 }
    if ($honor -lt 0) { $honorMultiplier = 0.75 }
    $substance = 40 * $stance * $honorMultiplier + (Clamp $stake -30 30)
    if ($repeal) { $substance = -$substance }
    $social = 0.0
    if (-not $isRuler) {
        $social = Clamp ($relation * 0.2) -20 20
        if ($rulerMotion) { $social += Clamp ($mood * 0.15) -15 15 }
        $social += Clamp $leader -10 10
    }
    return $substance + $social
}
function Commitment([double]$score, [double]$influence, [double]$reserve,
    [double]$likelihood, [double]$sponsorRelation) {
    if ([Math]::Abs($score) -lt 0.000001) {
        return [pscustomobject]@{side='Abstain';enthusiasm=0;vanilla_cost=0;cost=0;points=0}
    }
    # Best merit minus half the negative worst merit, then vanilla spending rules.
    $willing = 1.5 * [Math]::Abs($score)
    if ($influence -lt 2 * $willing) {
        $willing = [Math]::Min($willing * 0.5, $influence * 0.7)
    } elseif ($influence -gt 10 * $willing) { $willing *= 1.5 }
    if ($likelihood -gt 0.65) { $willing *= 1.6 * (1.2 - $likelihood) }
    # Native integer conversion of (100 - clamped relation).
    $distance = [Math]::Truncate(100 - (Clamp $sponsorRelation -100 100))
    $willing *= 0.2 + 1.6 * (1 - $distance / 200)
    $cost = 0; $points = 0; $vanillaCost = 0
    foreach ($level in @(@(20,1), @(60,2), @(150,3))) {
        if ($willing -gt $level[0] -and $influence -ge $level[0]) {
            $vanillaCost = $level[0]
            if ($influence -ge ($reserve + $level[0])) { $cost = $level[0]; $points = $level[1] }
        }
    }
    $side = 'Abstain'
    if ($cost -gt 0) { if ($score -gt 0) { $side = 'Pass' } else { $side = 'Reject' } }
    return [pscustomobject]@{side=$side;enthusiasm=[Math]::Round($willing,2);vanilla_cost=$vanillaCost;cost=$cost;points=$points}
}
function OverrideChance([double]$score, [double]$influence, [double]$reserve,
    [double]$ownCost, [double]$overrideCost, [double]$threshold) {
    if ($score -le 0 -or $influence -lt ($reserve + $ownCost + $overrideCost)) { return 0.0 }
    $gap = [Math]::Min(2 * $score, $influence)
    if ($gap -le 10 -or $gap -le ($threshold + $overrideCost)) { return 0.0 }
    return [Math]::Round(100 * (1 - ($threshold + $overrideCost) / $gap), 2)
}
function Check([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }

# Cost/role reserves are read from the live constants; war supplement applies only
# to rulers and army-leading clans, not every clan in a realm at war.
$constants = Get-Content "$PSScriptRoot/../../BellumCivileConstants.cs" -Raw
function Constant([string]$name) {
    $match = [regex]::Match($constants, "const float $name = ([0-9.]+)f;")
    if (-not $match.Success) { throw "Missing constant $name" }
    return [double]::Parse($match.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture)
}
$clanReserve = Constant 'NpcInfluenceClanReserve'
$rulerReserve = Constant 'NpcInfluenceRulerReserve'
$warExtra = Constant 'NpcInfluenceForeignWarReserveBonus'
Check ((Commitment 40 1000 200 0.5 0).cost -eq 60) 'Ordinary conviction commitment'
Check ((Commitment 40 219 200 0.5 0).cost -eq 0) 'Reserve below boundary'
Check ((Commitment 40 220 200 0.5 0).cost -eq 20) 'Reserve boundary inclusive'
Check ((Commitment 0 1000 200 0.5 100).cost -eq 0) 'Neutral abstention'
Check ((Commitment 40 1000 200 0.9 0).cost -eq 20) 'Comfortable majority spending'
Check ((Merit 1 0 10 0 0 0 $false $false $true) -eq -50) 'Repeal substance inversion'
Check ((Merit 0 0 0 100 0 0 $false $false $true) -eq 20) 'Repeal keeps proposer goodwill'
Check ((OverrideChance 100 600 500 60 100 60) -eq 0) 'Override reserve'
Check ((OverrideChance 100 1000 500 60 100 300) -eq 0) 'Vanilla override threshold'
Check ((OverrideChance 100 1000 500 60 100 60) -eq 20) 'Candidate override threshold'
if ($LibraryOnly) { return }

# Personal stakes are illustrative, not a finalized policy-benefit catalog.
# sponsorRelation is supplied separately: opposing a ruler does not imply that
# the ruler sponsors the rejecting outcome.
$cases = @(
    @{name='Royal Privilege: neutral Glory friend';s=0;h=0;p=0;r=100;m=60;l=0;royal=$true;sponsor=100},
    @{name='Royal Privilege: opposing Nobility friend';s=-1;h=0;p=-20;r=100;m=60;l=10;royal=$true;sponsor=0},
    @{name='Royal Privilege: Nobility ruler';s=-1;h=0;p=30;r=0;m=0;l=0;king=$true;sponsor=0},
    @{name='Royal Privilege: dishonorable Nobility ruler';s=-1;h=-1;p=30;r=0;m=0;l=0;king=$true;sponsor=0},
    @{name='Royal Privilege: Glory ruler';s=0;h=0;p=30;r=0;m=0;l=0;king=$true;sponsor=100},
    @{name='Senate: eligible Nobility house';s=1;h=0;p=20;r=0;m=0;l=0;sponsor=0},
    @{name='Senate: tier-2 neutral Liberty';s=0;h=0;p=0;r=0;m=0;l=0;sponsor=0},
    @{name='Noble Retinues: eligible Glory house';s=1;h=0;p=25;r=0;m=0;l=0;sponsor=0},
    @{name='Noble Retinues: eligible Liberty house';s=-1;h=0;p=25;r=0;m=0;l=0;sponsor=0},
    @{name='Trial by Jury: Liberty supporter';s=1;h=0;p=0;r=0;m=0;l=0;sponsor=0},
    @{name='Trial by Jury: neutral hostile proposer';s=0;h=0;p=0;r=-100;m=0;l=0;sponsor=0},
    @{name='Trial by Jury: Liberty repeal';s=1;h=0;p=0;r=0;m=0;l=0;repeal=$true;sponsor=0}
)
$rows = @()
foreach ($c in $cases) {
    $score = Merit $c.s $c.h $c.p $c.r $c.m $c.l ([bool]$c.royal) ([bool]$c.king) ([bool]$c.repeal)
    $reserve = $clanReserve
    if ($c.king) { $reserve = $rulerReserve }
    foreach ($balance in @(200,250,500,1000)) {
        $vote = Commitment $score $balance $reserve 0.5 $c.sponsor
        $rows += [pscustomobject]@{case=$c.name;score=$score;influence=$balance;reserve=$reserve;vote=$vote}
    }
}
$sweep = @(); $total = 0
foreach ($balance in @(200,250,500,1000)) {
    $abstain=0; $one=0; $two=0; $three=0
    foreach ($stance in @(-1,0,1)) { foreach ($honor in @(-1,0,1)) {
        foreach ($stake in @(-30,0,30)) { foreach ($relation in @(-100,0,100)) {
            foreach ($mood in @(-100,0,100)) { foreach ($leader in @(-10,0,10)) {
                $score = Merit $stance $honor $stake $relation $mood $leader $true $false $false
                # Sensitivity grid, not a simulated kingdom distribution.
                $sponsor = 0; if ($score -gt 0) { $sponsor = $relation }
                $v = Commitment $score $balance $clanReserve 0.5 $sponsor
                Check ($v.cost -eq 0 -or $balance - $v.cost -ge $clanReserve) 'Sweep reserve violation'
                switch ($v.points) { 0 {$abstain++} 1 {$one++} 2 {$two++} 3 {$three++} }
                $total++
            } }
        } }
    } }
    $sweep += [pscustomobject]@{influence=$balance;abstain=$abstain;slight=$one;strong=$two;full=$three}
}
$overrides = @()
foreach ($score in @(30,60,80,100,125)) { foreach ($cost in @(0,50,100,200)) {
    $overrides += [pscustomobject]@{score=$score;cost=$cost;vanilla_pct=(OverrideChance $score 1000 $rulerReserve 60 $cost 300);candidate_pct=(OverrideChance $score 1000 $rulerReserve 60 $cost 60)}
} }
$war = @()
foreach ($reserve in @($clanReserve, ($clanReserve+$warExtra), $rulerReserve, ($rulerReserve+$warExtra))) {
    $war += [pscustomobject]@{reserve=$reserve;influence=500;vote=(Commitment 60 500 $reserve 0.5 0)}
}
[pscustomobject]@{assertions='passed';sweep_cases=$total;examples=$rows;spending_sweep=$sweep;override_sensitivity=$overrides;role_reserve_examples=$war} | ConvertTo-Json -Depth 7
