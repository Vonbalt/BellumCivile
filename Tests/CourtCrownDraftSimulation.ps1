$ErrorActionPreference = 'Stop'
# Arithmetic model of ScoreAiDraftCandidate, not a live campaign frequency test.
function Score($used, $preferred, $winnerSupport, $loserSupport, $accepted = 2,
    $overrideCost = -1, $forced = $false) {
    $value = 10000 * $accepted + [Math]::Max(-200, [Math]::Min(200, $winnerSupport))
    if (!$forced) { $value += [Math]::Max(-200, [Math]::Min(200, $loserSupport)) }
    $value += $used * 2 - [Math]::Abs($used - $preferred) * 6
    if ($overrideCost -ge 0) { $value -= 50 + $overrideCost * 0.25 }
    return $value
}
function Check($condition, $message) {
    if (!$condition) { throw $message }
}
$baseline = Score 100 100 100 100
$cases = @(
    @{ Name = 'Otherwise equal'; Used = 100; W = 100; L = 100; A = 2; O = -1 },
    @{ Name = 'Five leverage unused'; Used = 95; W = 100; L = 100; A = 2; O = -1 },
    @{ Name = 'Ten leverage unused'; Used = 90; W = 100; L = 100; A = 2; O = -1 },
    @{ Name = 'Fifteen leverage unused'; Used = 85; W = 100; L = 100; A = 2; O = -1 },
    @{ Name = 'Council margin 60 lower'; Used = 100; W = 40; L = 100; A = 2; O = -1 },
    @{ Name = 'Council margin 120 lower'; Used = 100; W = 40; L = 40; A = 2; O = -1 },
    @{ Name = 'Override costs 100 influence'; Used = 100; W = 100; L = 100; A = 2; O = 100 },
    @{ Name = 'Other side rejects'; Used = 100; W = 100; L = 100; A = 1; O = -1 }
)
$rows = foreach ($case in $cases) {
    $loss = $baseline - (Score $case.Used 100 $case.W $case.L $case.A $case.O)
    $row = [ordered]@{ Scenario = $case.Name; OrdinaryAdvantage = $loss }
    foreach ($bonus in @(40, 80, 120)) {
        $row["Bonus$bonus"] = if ($bonus -gt $loss) { 'Objective' }
            elseif ($bonus -eq $loss) { 'Tie: ordinary' } else { 'Ordinary' }
    }
    [pscustomobject]$row
}
$rows | Format-Table -AutoSize | Out-String -Width 180 | Write-Output

$checks = 0
foreach ($preferred in @(0, 50, 100, 150)) {
    foreach ($used in 0..150) {
        foreach ($bonus in @(40, 80, 120)) {
            # Most favorable rejected package versus least favorable accepted one
            # within this bounded no-override fixture, with equal preferred spend.
            $rejected = (Score $used $preferred 200 200 1) + $bonus
            $acceptedWorst = [Math]::Min((Score 0 $preferred -200 -200 2),
                (Score 150 $preferred -200 -200 2))
            Check ($rejected -lt $acceptedWorst) 'Preference overrode acceptance in bounded fixture.'
            $checks++
        }
    }
}
Check (($baseline - (Score 90 100 100 100)) -eq 80) 'Ten unused points should cost 80.'
Check ((Score 100 100 100 100 2 100) -eq ($baseline - 75)) 'Override penalty drift.'
Check ((Score 100 100 100 -200 2 -1 $true) -eq (Score 100 100 100 200 2 -1 $true)) 'Forced loser margin should be omitted.'

# Illustrative profiles, not a distribution of rulers or predictions of frequency.
$profiles = @(
    @{ Name = 'Neutral'; Raw = 45 },
    @{ Name = 'Calculating +1'; Raw = 60 },
    @{ Name = 'Valor +1'; Raw = 53 },
    @{ Name = 'Mercy +1'; Raw = 33 },
    @{ Name = 'Honor +1'; Raw = 40 },
    @{ Name = 'Relation -40'; Raw = 55 }
)
$gateRows = foreach ($profile in $profiles) {
    [pscustomobject]@{
        Profile = $profile.Name
        Base = $profile.Raw
        Plus15 = $profile.Raw + 15
        Plus30 = $profile.Raw + 30
        Plus40 = $profile.Raw + 40
        PassesWith40 = ($profile.Raw + 40 -ge 85)
    }
}
$gateRows | Format-Table -AutoSize | Out-String -Width 180 | Write-Output
"PASS: $checks bounded acceptance comparisons and three formula checks."
