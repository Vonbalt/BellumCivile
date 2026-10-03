param(
    [double[]] $RivalWeights = @(0, 0.5, 1),
    [switch] $SupportBasedBaseline
)
$ErrorActionPreference = 'Stop'
# Synthetic deterministic sensitivity analysis, not campaign frequencies or runtime code.
# New provisional mappings: margin +0.5 per percentage point (cap +15);
# prospects clamp(20 * (1 - power / requiredPower), -20, +20).
function Bound([double]$value, [double]$low, [double]$high) {
    return [Math]::Max($low, [Math]::Min($high, $value))
}
function Threshold([int]$calculating, [int]$valor) {
    return [Math]::Max($script:floor, $script:base + (Bound $calculating -2 2) * $script:small - (Bound $valor -2 2) * $script:small)
}
function Feasible([double]$own, [double]$crown, [double]$rival, [double]$weight, [double]$threshold) {
    return $own -gt 0 -and $own -ge ($crown + $weight * $rival) * $threshold
}
function Acceptance($case, [double]$rivalWeight) {
    $threshold = Threshold $case.Calculating $case.Valor
    $opposition = $case.Crown + $rivalWeight * $case.Rival
    $personality = Bound (10 * $case.Honor + 5 * $case.Mercy) -25 25
    $relation = (Bound $case.Relation -100 100) * 0.25
    $claim = switch ($case.Claim) { 'Strong' { -15 }; 'Weak' { -5 }; default { 0 } }
    $margin = Bound (($case.WinnerVote - $case.Vote) * 0.5) 0 15
    $military = if ($case.Own -le 0) { 20.0 } elseif ($opposition -le 0) { -20.0 }
        else { Bound (20 * (1 - $case.Own / ($threshold * $opposition))) -20 20 }
    $baseline = if ($SupportBasedBaseline) { 100 - (Bound $case.Vote 0 100) } else { 80 }
    $raw = $baseline + $personality + $relation + $claim + $margin + $military
    $acceptance = Bound $raw 0 100
    [pscustomobject]@{
        Kind = 'Assessment'; Scenario = $case.Name; RivalWeight = $rivalWeight
        Baseline = $baseline; VoteSupport = $case.Vote
        OwnPower = $case.Own; LoyalistPower = $case.Crown; RivalPower = $case.Rival
        Threshold = $threshold; Personality = $personality; Relations = $relation
        Claim = $claim; DefeatMargin = $margin; Military = [Math]::Round($military, 3)
        Raw = [Math]::Round($raw, 3); Acceptance = [Math]::Round($acceptance, 3)
        SeekBackingPercent = [Math]::Round(100 - $acceptance, 3)
        SeekBackingProbability = (100 - $acceptance) / 100
        PowerGate = Feasible $case.Own $case.Crown $case.Rival $rivalWeight $threshold
    }
}
# Read live constants so the experiment cannot quietly substitute a different threshold.
$constants = Get-Content -Raw (Join-Path (Split-Path $PSScriptRoot -Parent) 'BellumCivileConstants.cs')
function ReadConstant([string]$name) {
    $match = [regex]::Match($constants, "\b$name\s*=\s*([0-9.]+)f")
    if (!$match.Success) { throw "Missing threshold constant: $name" }
    [double]::Parse($match.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture)
}
$script:base = ReadConstant 'RebellionPowerThresholdBase'
$script:small = ReadConstant 'TraitThresholdAdjSmall'
$script:floor = ReadConstant 'TraitThresholdFloor'
if ((ReadConstant 'TraitThresholdAdjLarge') -ne 2 * $script:small) { throw 'Trait mapping changed; update simulation.' }
$cases = @(
    @{ Name='Close election'; WinnerVote=51; Vote=49; Own=480; Crown=520; Rival=0; Honor=0; Mercy=0; Relation=0; Claim='None' },
    @{ Name='Landslide, weak loser'; WinnerVote=70; Vote=20; Own=200; Crown=800; Rival=0; Honor=0; Mercy=0; Relation=0; Claim='None' },
    @{ Name='Hostile strong claimant'; WinnerVote=45; Vote=40; Own=600; Crown=400; Rival=0; Honor=-1; Mercy=0; Relation=-40; Claim='Strong' },
    @{ Name='Honorable strong claimant'; WinnerVote=45; Vote=40; Own=600; Crown=400; Rival=0; Honor=2; Mercy=1; Relation=0; Claim='Strong' },
    @{ Name='Split A, both appeals active'; WinnerVote=40; Vote=32; Own=420; Crown=200; Rival=380; Honor=0; Mercy=0; Relation=0; Claim='None' },
    @{ Name='Split B, both appeals active'; WinnerVote=40; Vote=28; Own=380; Crown=200; Rival=420; Honor=0; Mercy=0; Relation=0; Claim='None' },
    @{ Name='Split A, no rival assumed'; WinnerVote=40; Vote=32; Own=420; Crown=580; Rival=0; Honor=0; Mercy=0; Relation=0; Claim='None' },
    @{ Name='Split B, no rival assumed'; WinnerVote=40; Vote=28; Own=380; Crown=620; Rival=0; Honor=0; Mercy=0; Relation=0; Claim='None' },
    @{ Name='Withdrawal A, before'; WinnerVote=40; Vote=35; Own=420; Crown=300; Rival=280; Honor=0; Mercy=0; Relation=0; Claim='None' },
    @{ Name='Withdrawal B, before'; WinnerVote=40; Vote=25; Own=280; Crown=300; Rival=420; Honor=0; Mercy=0; Relation=0; Claim='None' },
    @{ Name='Withdrawal A, B returns to Crown'; WinnerVote=40; Vote=35; Own=420; Crown=580; Rival=0; Honor=0; Mercy=0; Relation=0; Claim='None' },
    @{ Name='Withdrawal A, half of B backs A'; WinnerVote=40; Vote=35; Own=560; Crown=440; Rival=0; Honor=0; Mercy=0; Relation=0; Claim='None' }
)
$results = foreach ($weight in $RivalWeights) {
    if ($weight -lt 0 -or $weight -gt 1) { throw 'Rival weights must be within 0..1.' }
    foreach ($case in $cases) { Acceptance $case $weight }
}
$results
# Initiation probabilities must NOT assume the other loser has already challenged.
# Exact four-branch illustration: fixed conditional pledges, no random sampling.
# In this example all non-initiating/withdrawn-side supporters default to the Crown.
$a = Acceptance ($cases | Where-Object Name -eq 'Split A, no rival assumed') 0.5
$b = Acceptance ($cases | Where-Object Name -eq 'Split B, no rival assumed') 0.5
$pa = $a.SeekBackingProbability
$pb = $b.SeekBackingProbability
foreach ($startsA in @($false, $true)) {
    foreach ($startsB in @($false, $true)) {
        $probability = $(if ($startsA) { $pa } else { 1-$pa }) * $(if ($startsB) { $pb } else { 1-$pb })
        $activeA = $startsA; $activeB = $startsB
        for ($pass=0; $pass -lt 2; $pass++) {
            $crown = 200 + $(if ($activeA) { 0 } else { 420 }) + $(if ($activeB) { 0 } else { 380 })
            $nextA = $activeA -and (Feasible 420 $crown $(if ($activeB) { 380 } else { 0 }) 0.5 $script:base)
            $nextB = $activeB -and (Feasible 380 $crown $(if ($activeA) { 420 } else { 0 }) 0.5 $script:base)
            $activeA = $nextA; $activeB = $nextB
        }
        [pscustomobject]@{ Kind='InitiationBranches'; Scenario='Split election'; StartsA=$startsA; StartsB=$startsB
            ProbabilityPercent=[Math]::Round($probability*100, 3); UltimatumA=$activeA; UltimatumB=$activeB }
    }
}
foreach ($traits in @(@(0,0), @(2,-2), @(-2,2))) {
    $case = $cases[0].Clone(); $case.Calculating=$traits[0]; $case.Valor=$traits[1]
    $case.Name="Close election, Calculating $($traits[0]), Valor $($traits[1])"
    Acceptance $case 0.5
}
foreach ($claim in @('None', 'Weak', 'Strong')) {
    $case = $cases[0].Clone(); $case.Claim=$claim; $case.Name="Close election, $claim claim"
    Acceptance $case 0.5
}
$memoryCase = $cases[0].Clone(); $memoryCase.Relation=-10; $memoryCase.Name='Close election, prior failed-appeal memory'
Acceptance $memoryCase 0.5
# Captured UI components are rounded; these are controlled approximations, not save reads.
foreach ($example in @(
    @{ Name='Saratis screenshot'; Support=40.9; Personality=0; Relations=-11; Margin=0.62; Military=-2.06 },
    @{ Name='Desporion screenshot'; Support=17; Personality=5; Relations=9.25; Margin=12.59; Military=17.92 }
)) {
    $baseline = if ($SupportBasedBaseline) { 100 - $example.Support } else { 80 }
    $raw = $baseline + $example.Personality + $example.Relations + $example.Margin + $example.Military
    $acceptance = Bound $raw 0 100
    [pscustomobject]@{ Kind='Screenshot'; Scenario=$example.Name; VoteSupport=$example.Support; Baseline=$baseline
        Raw=[Math]::Round($raw, 3); Acceptance=[Math]::Round($acceptance, 3); SeekBackingPercent=[Math]::Round(100-$acceptance, 3) }
}
if (!(Feasible 80 100 0 0.5 0.8) -or (Feasible 0 0 0 0.5 0.8)) { throw 'Power boundary invariant failed.' }
if (($results | Where-Object { $_.Acceptance -lt 0 -or $_.Acceptance -gt 100 }).Count) { throw 'Acceptance bounds failed.' }
if (($cases | Where-Object { $_.Own + $_.Crown + $_.Rival -ne 1000 }).Count) { throw 'Scenario power conservation failed.' }
