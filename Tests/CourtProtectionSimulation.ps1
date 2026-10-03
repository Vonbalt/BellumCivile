$ErrorActionPreference = 'Stop'
# Design simulation only. No campaign state, runtime scoring or selection frequency is modeled.
# Ratio includes applicant plus protector/client military forces, each realm once,
# divided by the named enemy bloc. Other fronts exclude that entire named bloc.
function Clamp([double]$value, [double]$minimum, [double]$maximum) {
    [Math]::Max($minimum, [Math]::Min($maximum, $value))
}
function Military([double]$ratio, [bool]$sharper) {
    $scale = if ($sharper -and $ratio -lt 1) { 60 } else { 40 }
    Clamp ($scale * ($ratio - 1)) -30 25
}
function Acceptance([double]$ratio, [double]$otherLoad, [double]$value,
    [double]$relation, [double]$personality, [bool]$already, [bool]$sharper = $false) {
    50 + (Military $ratio $sharper) - (25 * (Clamp $otherLoad 0 1)) + (Clamp $value 0 15) +
        (Clamp ($relation * 0.1) -10 10) + (Clamp $personality -10 10) + $(if ($already) { 15 } else { 0 })
}
$script:checks = 0
function Check($condition, $message) {
    if (!$condition) { throw $message }
    $script:checks++
}

# Value/personality are explicit point fixtures, not yet formulas for geography or traits.
$cases = @(
    @{ Name = 'Easy rescue'; Ratio = 1.8; Load = 0; Value = 10; Relation = 0; Trait = 0; Already = $false },
    @{ Name = 'Costly but worthwhile'; Ratio = 1.2; Load = 0.4; Value = 15; Relation = 50; Trait = 0; Already = $false },
    @{ Name = 'Exact borderline'; Ratio = 1; Load = 0.2; Value = 10; Relation = 50; Trait = 0; Already = $false },
    @{ Name = 'Borderline, relation 49'; Ratio = 1; Load = 0.2; Value = 10; Relation = 49; Trait = 0; Already = $false },
    @{ Name = 'Strong but overstretched'; Ratio = 1.2; Load = 1; Value = 10; Relation = 0; Trait = 0; Already = $false },
    @{ Name = 'Hopeless new intervention'; Ratio = 0.4; Load = 0.4; Value = 10; Relation = 0; Trait = 0; Already = $false },
    @{ Name = 'Outmatched, all inducements'; Ratio = 0.5; Load = 0; Value = 15; Relation = 100; Trait = 10; Already = $false },
    @{ Name = 'Already fighting, near parity'; Ratio = 0.9; Load = 0.2; Value = 10; Relation = 0; Trait = 0; Already = $true },
    @{ Name = 'Already fighting, heavily outmatched'; Ratio = 0.5; Load = 0; Value = 15; Relation = 100; Trait = 10; Already = $true },
    @{ Name = 'Overwhelming but hostile'; Ratio = 2; Load = 0; Value = 5; Relation = -100; Trait = -10; Already = $false }
)
$rows = foreach ($case in $cases) {
    $linear = Acceptance $case.Ratio $case.Load $case.Value $case.Relation $case.Trait $case.Already
    $sharper = Acceptance $case.Ratio $case.Load $case.Value $case.Relation $case.Trait $case.Already $true
    [pscustomobject]@{ Scenario = $case.Name; Ratio = $case.Ratio; Linear = [Math]::Round($linear, 2);
        Decision = $(if ($linear -ge 60) { 'Accept' } else { 'Decline' }); Sharper = [Math]::Round($sharper, 2);
        RevisedDecision = $(if ($sharper -ge 60) { 'Accept' } else { 'Decline' }) }
}
$rows | Format-Table -AutoSize | Out-String -Width 180 | Write-Output

'Military curve sensitivity (neutral ruler, value +10, no other fronts):'
$militaryRows = foreach ($ratio in @(0.25, 0.5, 0.75, 0.9, 1, 1.25, 1.5, 1.625, 2, 3)) {
    [pscustomobject]@{ Ratio = $ratio; LinearMilitary = (Military $ratio $false); SharperMilitary = (Military $ratio $true);
        NewWar = (Acceptance $ratio 0 10 0 0 $false $true); ExistingWar = (Acceptance $ratio 0 10 0 0 $true $true) }
}
$militaryRows | Format-Table -AutoSize | Out-String -Width 180 | Write-Output

# Deterministic factorial sweep. Counts describe only this synthetic grid, not campaign probabilities.
$counts = @{}; $changes = 0
foreach ($already in @($false, $true)) {
    $key = if ($already) { 'Existing war' } else { 'New intervention' }
    $counts[$key] = @{ Total = 0; Linear = 0; Sharper = 0; LowLinear = 0; LowSharper = 0; LowTotal = 0 }
    foreach ($ratio in @(0.25, 0.5, 0.75, 1, 1.25, 1.5, 2)) {
        foreach ($load in @(0, 0.5, 1)) {
            foreach ($value in @(0, 7.5, 15)) {
                foreach ($relation in @(-100, 0, 100)) {
                    foreach ($trait in @(-10, 0, 10)) {
                        $a = Acceptance $ratio $load $value $relation $trait $already
                        $b = Acceptance $ratio $load $value $relation $trait $already $true
                        $c = $counts[$key]; $c.Total++
                        if ($a -ge 60) { $c.Linear++ }
                        if ($b -ge 60) { $c.Sharper++ }
                        if ($ratio -le 0.5) {
                            $c.LowTotal++
                            if ($a -ge 60) { $c.LowLinear++ }
                            if ($b -ge 60) { $c.LowSharper++ }
                        }
                        if (($a -ge 60) -ne ($b -ge 60)) { $changes++ }
                        Check ($b -le $a + 0.000001) 'Sharper downside increased acceptance.'
                        Check ($b -ge -25 -and $b -le 125) 'Score escaped specified modifier bounds.'
                        Check ((Acceptance ($ratio + 0.05) $load $value $relation $trait $already $true) -ge $b - 0.000001) 'More military strength reduced acceptance.'
                        Check ((Acceptance $ratio ($load + 0.1) $value $relation $trait $already $true) -le $b + 0.000001) 'Another war increased acceptance.'
                    }
                }
            }
        }
    }
}
foreach ($key in @('New intervention', 'Existing war')) {
    $c = $counts[$key]
    "Grid: $key; fixtures=$($c.Total); linear accepts=$($c.Linear); sharper accepts=$($c.Sharper); ratio<=0.5 fixtures=$($c.LowTotal), accepts=$($c.LowLinear)/$($c.LowSharper)."
}
"Changed decisions across both grids: $changes. These are not campaign acceptance rates."

Check ((Acceptance 1 0.2 10 50 0 $false $true) -eq 60) 'Threshold should include exactly 60.'
Check ((Acceptance 1 0.2 10 49 0 $false $true) -lt 60) 'Displayed rounding must not turn 59.9 into acceptance.'
Check ((Acceptance 0.5 0 15 100 10 $false $true) -lt 60) 'Maximum inducements should not overcome a 2:1 disadvantage in a new intervention.'
Check ((Acceptance 0.5 0 15 100 10 $true $true) -ge 60) 'An existing belligerent may still value a client without starting a new war.'
Check ((Military 10 $true) -eq 25 -and (Military 0 $true) -eq -30) 'Military caps drifted.'
# Discovery boundaries are independent of acceptance: an attractive client cannot
# bypass the approved desperation or territorial eligibility gates.
foreach ($enemyRatio in @(1.99, 2, 2.01, 4)) {
    foreach ($cost in @(149, 150, 151)) {
        $eligible = $enemyRatio -ge 2 -and $cost -lt 150
        Check ($eligible -eq ($enemyRatio -ne 1.99 -and $cost -eq 149)) 'Desperation or fief-value boundary drifted.'
    }
}
# A high score must remain subordinate to legal preflight, not be used as a bypass.
foreach ($legal in @($false, $true)) {
    $score = Acceptance 2 0 15 100 10 $false $true
    Check (($legal -and $score -ge 60) -eq $legal) 'Legal gate was bypassed.'
}
"PASS: $script:checks arithmetic checks. No runtime behavior or campaign/save state changed."
