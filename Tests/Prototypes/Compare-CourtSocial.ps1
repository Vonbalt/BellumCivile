param(
    [string]$ModuleData,
    [switch]$SyntheticOnly
)
$ErrorActionPreference = 'Stop'
$data = & "$PSScriptRoot/Compare-CourtPersonality.ps1" -ModuleData $ModuleData -SyntheticOnly:$SyntheticOnly | ConvertFrom-Json
$candidate = $data.reports | Where-Object candidate -eq 'C_standardized_strengths'
if ($null -eq $candidate) { throw 'Missing standardized personality candidate.' }

# Legal and actual hierarchy are independent strongest-liege pulls, as in live code.
function Social($relations, [int]$marriages, [double]$legal, [double]$actual, [double]$scale) {
    $friend = 0.0
    foreach ($relation in $relations) {
        $friend = [Math]::Max($friend, [Math]::Min(8, [Math]::Max(0, $relation - 20) * 0.1))
    }
    return $scale * ($friend + [Math]::Min(8, 4 * $marriages) + ($legal + $actual) / 2)
}
if ((Social @(100) 2 8 8 1) -ne 24 -or (Social @(100) 2 8 8 0.5) -ne 12) { throw 'Cap failed.' }
if ((Social @(20,-100) 0 0 0 1) -ne 0) { throw 'Threshold failed.' }
if ((Social @(80,80,80) 0 0 0 1) -ne (Social @(80) 0 0 0 1)) { throw 'Friendship stacks.' }
if ((Social @() 20 0 0 1) -ne 8) { throw 'Marriage cap failed.' }
if ((Social @() 0 8 0 1) -ne 4) { throw 'One-sided hierarchy failed.' }

$scenarios = @(
    @{name='No ties'; relations=@(); marriages=0; legal=0; actual=0},
    @{name='Friend at 80'; relations=@(80); marriages=0; legal=0; actual=0},
    @{name='Marriage only'; relations=@(); marriages=1; legal=0; actual=0},
    @{name='Immediate liege in both trees'; relations=@(); marriages=0; legal=8; actual=8},
    @{name='Friend 80 and marriage'; relations=@(80); marriages=1; legal=0; actual=0},
    @{name='Maximum combined ties'; relations=@(100); marriages=2; legal=8; actual=8}
)
$results = @()
foreach ($scale in @(1.0, 0.5)) {
    foreach ($scenario in $scenarios) {
        $pull = Social $scenario.relations $scenario.marriages $scenario.legal $scenario.actual $scale
        $comparisons=0; $overtakes=0; $ties=0
        foreach ($owner in $candidate.owner_scores) {
            $maximum = ($owner.scores | Measure-Object -Maximum).Maximum
            for ($bloc=0; $bloc -lt 3; $bloc++) {
                if ($owner.scores[$bloc] -ge $maximum) { continue }
                $comparisons++
                $newScore = $owner.scores[$bloc] + $pull
                if ($newScore -gt $maximum) { $overtakes++ }
                elseif ($newScore -eq $maximum) { $ties++ }
            }
        }
        $results += [pscustomobject]@{scale=$scale; scenario=$scenario.name; pull=$pull; losing_bloc_comparisons=$comparisons; overtakes=$overtakes; reaches_tie=$ties}
    }
}
[pscustomobject]@{
    scope='Offline sensitivity scenarios, NOT real social networks. Give one losing bloc ties while others receive zero. No political scores, randomness or switching thresholds.'
    owners=$candidate.owner_scores.Count; tests='Caps, threshold, nonstacking friends, marriage saturation, split hierarchy passed'
    results=$results
} | ConvertTo-Json -Depth 6
