param([double]$BribeMultiplier = 1)
$ErrorActionPreference = 'Stop'
if ($BribeMultiplier -le 0) { throw 'Bribe multiplier must be positive.' }
# Synthetic balance exploration, not native Charm success chances or campaign outcomes.
$constants = Get-Content -Raw (Join-Path (Split-Path $PSScriptRoot -Parent) 'BellumCivileConstants.cs')
function Constant([string]$name) {
    $match = [regex]::Match($constants, "\b$name\s*=\s*([0-9.]+)f")
    if (!$match.Success) { throw "Missing constant: $name" }
    [double]::Parse($match.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture)
}
function Bound([double]$value, [double]$low, [double]$high) { [Math]::Max($low, [Math]::Min($high, $value)) }
$weights = @{}
foreach ($suffix in @('OpennessBase', 'OpennessThreshold', 'HonorPenalty', 'DishonorBonus', 'MercyPenalty',
    'CrueltyBonus', 'GenerosityPenalty', 'GreedBonus', 'CalculatingBonus', 'HotheadPenalty', 'PlayerRelationScale')) {
    $weights[$suffix] = Constant "FiefBribe$suffix"
}
function Assess([string]$name, [double]$relation, [double]$share, [double]$gap,
    [int]$honor = 0, [int]$mercy = 0, [int]$generosity = 0, [int]$calculating = 0, [bool]$selfVoting = $false) {
    $relation = Bound $relation -100 100
    $share = Bound $share 0 100
    $gap = Bound $gap 0 100
    if ($selfVoting) { $gap = [Math]::Max(50, $gap) }
    $open = $weights.OpennessBase + $relation * $weights.PlayerRelationScale
    $open += if ($honor -gt 0) { -$honor * $weights.HonorPenalty } else { -$honor * $weights.DishonorBonus }
    $open += if ($mercy -gt 0) { -$mercy * $weights.MercyPenalty } else { -$mercy * $weights.CrueltyBonus }
    $open += if ($generosity -gt 0) { -$generosity * $weights.GenerosityPenalty } else { -$generosity * $weights.GreedBonus }
    $open += if ($calculating -gt 0) { $calculating * $weights.CalculatingBonus } else { $calculating * $weights.HotheadPenalty }
    # Proposed election adaptation of policy stance resistance; no extra candidate-relation term.
    $open -= $gap * 0.25
    # Existing council-vote price modifier; election share/resistance factors are proposed.
    $person = Bound (1 - $relation / 100 * 0.30 + $honor * 0.25 - $generosity * 0.10) 0.25 2
    $price = [Math]::Round(50000 * (1 + [Math]::Min($share / 10, 2)) * (1 + $gap / 50) * $person * $BribeMultiplier)
    $harder = if ($gap -gt 60) { 3 } elseif ($gap -gt 30) { 2 } elseif ($gap -gt 10) { 1 } else { 0 }
    $friend = if ($relation -ge 90) { 2 } elseif ($relation -ge 60) { 1 } else { 0 }
    $labels = @('Extremely easy', 'Very easy', 'Easy', 'Normal', 'Hard', 'Very hard', 'Extremely hard')
    $normal = [int](Bound ($harder - $friend) -3 3)
    $matched = [int](Bound ($harder - $friend - 1) -3 3)
    [pscustomobject]@{
        Profile = $name; Relation = $relation; Share = $share; Gap = $gap; SelfVoting = $selfVoting
        Openness = $open; Bribe = $open -ge $weights.OpennessThreshold; Price = $price
        Persuasion = $relation -ge 30
        NeutralArgument = $labels[$normal + 3]; MatchedArgument = $labels[$matched + 3]
        MatchedWithSelfFloor = $labels[([int]$(if ($selfVoting) { [Math]::Max(1, $matched) } else { $matched })) + 3]
    }
}
$profiles = @(
    Assess 'Friendly minor house' 60 5 0
    Assess 'Neutral ordinary elector' 0 10 25
    Assess 'Friendly reluctant elector' 60 10 25
    Assess 'Hostile neutral lord' -40 10 25
    Assess 'Hostile opportunist' -40 10 25 -1 0 -1 1
    Assess 'Principled friend' 60 10 25 1 1 1 0
    Assess 'Devoted principled friend' 100 10 0 2 1 1 0
    Assess 'Greedy major rival supporter' 30 20 60 -1 0 -1 1
    Assess 'Distant self-voting candidate' 30 20 0 0 0 0 0 $true
    Assess 'Friendly self-voting candidate' 90 20 0 0 0 0 0 $true
)
$profiles | Select-Object Profile, Relation, Share, Gap, Openness, Bribe, Price, Persuasion, NeutralArgument, MatchedArgument | Format-Table -AutoSize
'Self-candidacy sensitivity:'
$profiles | Where-Object SelfVoting | Select-Object Profile, MatchedArgument, MatchedWithSelfFloor | Format-Table -AutoSize

$checks = 0
foreach ($relation in @(-100, -40, 0, 30, 60, 90, 100)) {
    foreach ($share in @(0, 5, 10, 20, 50, 100)) {
        $previous = 0
        foreach ($gap in @(0, 10, 11, 30, 31, 50, 60, 61, 100)) {
            $r = Assess 'Sweep' $relation $share $gap
            if ($r.Price -lt $previous -or $r.Price -le 0) { throw 'Price must rise with resistance and stay positive.' }
            if ($r.Persuasion -ne ($relation -ge 30)) { throw 'Persuasion gate drifted.' }
            $previous = $r.Price
            $checks += 2
        }
    }
}
if (($profiles | Where-Object Profile -eq 'Devoted principled friend').Bribe) { throw 'Principled profile unexpectedly accepts bribery.' }
$checks++
if (($profiles | Where-Object Profile -eq 'Hostile opportunist').Persuasion) { throw 'Hostile lord bypasses trust gate.' }
$checks++
if ((Assess 'Cap' 0 20 50).Price -ne (Assess 'Cap' 0 100 50).Price) { throw 'Voting-share cap drifted.' }
$checks++
"$checks synthetic invariants passed. Price is a quote only when Bribe=True; values do not predict native persuasion success."

'Illustrative attempt-length sensitivity, independent rolls and no critical outcomes (NOT native odds):'
$attempts = foreach ($p in @(0.25, 0.40, 0.60)) {
    foreach ($n in @(3, 4, 5)) {
        $chance = 1 - [Math]::Pow(1 - $p, $n) - $n * $p * [Math]::Pow(1 - $p, $n - 1)
        [pscustomobject]@{ AssumedSingleRoll = $p; MaximumArguments = $n; AtLeastTwoSuccesses = [Math]::Round(100 * $chance, 1) }
    }
}
$attempts | Format-Table -AutoSize
