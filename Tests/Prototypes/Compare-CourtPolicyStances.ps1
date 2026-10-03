$ErrorActionPreference = 'Stop'
# Offline arithmetic model. Reads the live roster; never changes game configuration.
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
[xml]$xml = Get-Content -Raw -LiteralPath (Join-Path $root 'ModuleData/bellum_policy_agendas.xml')
$crown = @($xml.BellumPolicyAgendas.Crown.Policy | ForEach-Object { $_.id })
$roster = @{}
foreach ($faction in $xml.BellumPolicyAgendas.Faction) {
    $entries = @{}
    foreach ($policy in $faction.Policy) {
        $entries[$policy.id] = if ($policy.stance -eq 'Oppose') { -1 } elseif ($policy.stance -eq 'Neutral') { 0 } else { 1 }
    }
    $roster[$faction.type] = $entries
}
$universe = @($xml.SelectNodes('//Policy') | ForEach-Object {
    if ($_.id -eq 'policy_land_grants_for_veteran') { 'policy_land_grands_for_veteran' } else { $_.id }
} | Sort-Object -Unique)
$proposed = @{}
foreach ($name in $roster.Keys) { $proposed[$name] = $roster[$name].Clone() }
$proposed.Glory.Remove('policy_land_grants_for_veteran')
$proposed.Glory['policy_land_grands_for_veteran'] = 1
$proposed.Glory['policy_serfdom'] = -1
$proposed.Glory['policy_senate'] = -1

function BaseStance($map, $faction, $policy) {
    if ($map[$faction].ContainsKey($policy)) { return [int]$map[$faction][$policy] }
    return 0
}
function EffectiveStance($faction, $policy, [double]$mood) {
    if ($policy -in $crown) {
        if ($mood -le 20) { return -1 }
        if ($mood -ge 60) { return 1 }
        return 0
    }
    return BaseStance $proposed $faction $policy
}
function PolicyBaseline($faction, $policies) {
    $value = 0
    foreach ($policy in $policies) {
        $value += 10 * (BaseStance $proposed $faction $policy)
        if ($policy -in $crown) { $value -= 5 }
    }
    return $value
}
function Check($condition, $message) { if (!$condition) { throw $message } }
Check ($universe.Count -eq 43 -and $crown.Count -eq 14) 'Unexpected source roster; review simulation assumptions.'
$counts = @()
foreach ($name in @('Nobility', 'Glory', 'Liberty')) {
    foreach ($scenario in @('Stable XML interests', 'Proposed: low', 'Proposed: neutral', 'Proposed: high')) {
        $stances = foreach ($id in $universe) {
            switch ($scenario) {
                'Stable XML interests' { BaseStance $roster $name $id }
                'Proposed: low' { EffectiveStance $name $id 20 }
                'Proposed: neutral' { EffectiveStance $name $id 40 }
                'Proposed: high' { EffectiveStance $name $id 60 }
            }
        }
        $counts += [pscustomobject]@{ Faction=$name; Scenario=$scenario;
            Support=@($stances | Where-Object { $_ -eq 1 }).Count;
            Neutral=@($stances | Where-Object { $_ -eq 0 }).Count;
            Oppose=@($stances | Where-Object { $_ -eq -1 }).Count }
    }
    foreach ($id in $universe) {
        foreach ($mood in @(-100, 20, 20.001, 59.999, 60, 100)) {
            $stance = EffectiveStance $name $id $mood
            $expected = if ($id -notin $crown) { BaseStance $proposed $name $id }
                elseif ($mood -le 20) { -1 } elseif ($mood -ge 60) { 1 } else { 0 }
            Check ($stance -eq $expected) "Boundary failed: $name/$id/$mood"
        }
    }
}
# Four representative opposed Crown laws; other sources remain a hypothetical +80.
$package = @('policy_sacred_majesty', 'policy_royal_privilege', 'policy_imperial_towns', 'policy_royal_commissions')
$timelines = @()
foreach ($name in @('Nobility', 'Glory', 'Liberty')) {
    foreach ($number in @(1, 2, 4)) {
        $baseline = PolicyBaseline $name $package[0..($number-1)]
        $target = [Math]::Max(-100, [Math]::Min(100, 80 + $baseline))
        $mood = 80.0
        $loseSupport = $null
        $oppose = $null
        for ($day=1; $day -le 120; $day++) {
            if ($mood -gt $target) { $mood = [Math]::Max($mood - 1, $target) }
            elseif ($mood -lt $target) { $mood = [Math]::Min($mood + 1, $target) }
            $stance = EffectiveStance $name $package[0] $mood
            if ($stance -ne 1 -and $null -eq $loseSupport) { $loseSupport = $day }
            if ($stance -eq -1 -and $null -eq $oppose) { $oppose = $day }
        }
        $timelines += [pscustomobject]@{ Faction=$name; Laws=$number; PolicyBaseline=$baseline;
            Target=$target; FirstNeutralDay=$loseSupport; FirstOpposeDay=$oppose; MoodDay120=$mood }
    }
}
# Voting arithmetic from CalculateSupportScore, not full native election simulation.
$votes = foreach ($mood in @(20, 20.001, 59.999, 60, 80)) {
    $stance = EffectiveStance 'Glory' $package[0] $mood
    foreach ($honor in @(-1, 0, 1)) {
        $factor = if ($honor -gt 0) { 1.25 } elseif ($honor -lt 0) { .75 } else { 1 }
        $conviction = 40 * $stance * $factor
        [pscustomobject]@{ Mood=$mood; Honor=$honor; Stance=$stance;
            CrownProposalNoRelations=$conviction + [Math]::Max(-15, [Math]::Min(15, $mood * .15));
            NonCrownProposalNoRelations=$conviction }
    }
}
Check ((PolicyBaseline 'Glory' @('policy_royal_guard')) -eq 5) 'Stable Royal Guard interest must not be overwritten by current voting mood.'
Check ((PolicyBaseline 'Nobility' @('policy_royal_guard')) -eq -15) 'Centralization must retain the existing ideological and shared burden.'
Check ((EffectiveStance 'Glory' 'policy_royal_guard' 20) -eq -1) 'Low-mood Glory must oppose further Crown empowerment.'
[pscustomobject]@{ Counts=$counts; Timelines=$timelines; Voting=$votes;
    Notes='Offline arithmetic, not campaign observations. No election outcomes, shocks, accommodation expiry or changing world conditions are simulated. Null timeline days mean no crossing in 120 updates.' } | ConvertTo-Json -Depth 5
