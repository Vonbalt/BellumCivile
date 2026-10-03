param([switch]$CrownHelpersOnly)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Compare-CourtPolicyVoting.ps1" -LibraryOnly

function CrownMerit([int]$stance, [int]$honor, [double]$ordinaryStake,
    [double]$crownStake, [bool]$isRuler, [bool]$centralizing, [bool]$repeal) {
    $score = Merit $stance $honor $ordinaryStake 0 0 0 $false $isRuler $repeal
    if ($isRuler -and $centralizing) {
        $replacement = $crownStake - (Clamp $ordinaryStake -30 30)
        if ($repeal) { $replacement = -$replacement }
        $score += $replacement
    }
    return $score
}

# Audited DefaultClanPoliticsModel NPC path. Bellum delegates policy costs to
# its base model. No perks/other-mod overrides; point deficit already includes
# the ruler's own vote. Royal Privilege must already be active for the discount.
function NpcOverrideCost([int]$pointDeficit, [bool]$royalPrivilege) {
    $raw = $pointDeficit / 3.0 * 150 * 1.4 * 0.8
    $rounded = 5 * [Math]::Floor($raw / 5)
    if ($royalPrivilege) { $rounded *= 0.8 }
    return [int][Math]::Truncate($rounded)
}
Check ((NpcOverrideCost 1 $false) -eq 55) 'One point override cost'
Check ((NpcOverrideCost 2 $false) -eq 110) 'Two point override cost'
Check ((NpcOverrideCost 1 $true) -eq 44) 'Existing Royal Privilege discount'
Check ((CrownMerit -1 0 30 70 $false $true $false) -eq -10) 'No crown benefit for vassals'
Check ((CrownMerit -1 0 30 70 $true $false $false) -eq -10) 'No crown benefit on ordinary policy'
Check ((CrownMerit -1 0 30 70 $true $true $false) -eq 30) 'Replace rather than stack benefit'
Check ((CrownMerit -1 0 30 70 $true $true $true) -eq -30) 'Repeal reverses crown interest'
Check ((Commitment 10 1000 $rulerReserve 0.5 0).cost -eq 20) 'Low positive crown conviction'
Check ((Commitment 10 1000 $rulerReserve 0.9 0).cost -eq 0) 'Low conviction majority discount'
Check ((Commitment 20 1000 $rulerReserve 0.9 0).cost -eq 20) 'Moderate conviction majority discount'
if ($CrownHelpersOnly) { return }

$preferences = @(); $overrides = @(); $budget = @()
foreach ($crown in @(60,70,80)) {
    foreach ($bloc in @('Nobility','Glory','Liberty')) {
        $stance = -1; if ($bloc -eq 'Glory') { $stance = 0 }
        foreach ($honor in @(-2,-1,0,1,2)) {
            $score = CrownMerit $stance $honor 30 $crown $true $true $false
            $reverse = CrownMerit $stance $honor 30 $crown $true $true $true
            Check ($score -eq -$reverse) 'Crown repeal symmetry'
            Check ($score -gt 0) 'Crown candidate should favor designated centralization'
            $preferences += [pscustomobject]@{crown=$crown;bloc=$bloc;honor=$honor;merit=$score;
                neutral_sponsor_cost=(Commitment $score 1000 $rulerReserve 0.5 0).cost;
                friendly_sponsor_cost=(Commitment $score 1000 $rulerReserve 0.5 100).cost}
            foreach ($threshold in @(0,20,40,60,300)) {
                foreach ($deficit in @(1,2,3,4)) {
                    foreach ($privilege in @($false,$true)) {
                        $cost = NpcOverrideCost $deficit $privilege
                        $chance = OverrideChance $score 1000 $rulerReserve 60 $cost $threshold
                        Check ($chance -ge 0 -and $chance -le 100) 'Probability bounds'
                        $overrides += [pscustomobject]@{crown=$crown;bloc=$bloc;honor=$honor;
                            merit=$score;threshold=$threshold;deficit=$deficit;privilege=$privilege;
                            cost=$cost;chance=$chance}
                    }
                }
            }
        }
    }
}

# Explicit commitment and reserve sweep: no proposal cost is charged here;
# balances represent remaining influence at election resolution.
foreach ($reserve in @($rulerReserve,($rulerReserve+$warExtra))) {
    foreach ($balance in @(500,600,700,800,1000)) {
        foreach ($ownCost in @(0,20,60,150)) {
            foreach ($deficit in @(1,2,3,4)) {
                $cost = NpcOverrideCost $deficit $false
                $chance = OverrideChance 70 $balance $reserve $ownCost $cost 20
                if ($chance -gt 0) { Check ($balance - $cost - $ownCost -ge $reserve) 'Override reserve breached' }
                $budget += [pscustomobject]@{reserve=$reserve;balance=$balance;own_commitment=$ownCost;deficit=$deficit;chance=$chance}
            }
        }
    }
}
$summary = @()
foreach ($crown in @(60,70,80)) {
    foreach ($threshold in @(0,20,40,60,300)) {
        $group = @($overrides | Where-Object { $_.crown -eq $crown -and $_.threshold -eq $threshold -and -not $_.privilege })
        $summary += [pscustomobject]@{crown=$crown;threshold=$threshold;
            positive_override_cases=@($group | Where-Object chance -gt 0).Count;total=$group.Count}
    }
}
[pscustomobject]@{assertions='passed';preference_cases=$preferences.Count;override_cases=$overrides.Count;
    budget_cases=$budget.Count;preferences=$preferences;override_summary=$summary;
    overrides=$overrides;budget=$budget} | ConvertTo-Json -Depth 6
