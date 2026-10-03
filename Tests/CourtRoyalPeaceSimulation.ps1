$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -TypeDefinition ((Get-Content -Raw (Join-Path $root 'CourtRoyalPeaceRules.cs')).Replace('internal', 'public'))
function Check($condition, $label) { if (!$condition) { throw $label }; "PASS: $label" }
foreach ($enemy in @(500,1000,1500,2000,3000)) {
    foreach ($diverted in @(250,1000)) {
        $chance = [BellumCivile.CourtRoyalPeaceRules]::Chance(1000,$enemy,$diverted,0,0,0)
        "Available 1000; enemy $enemy; diverted $diverted; neutral Crown $chance percent"
        Check ($chance -ge 0 -and $chance -le 95) 'Chance remains bounded.'
        if ($enemy -lt 1000) { Check ($chance -eq 0) 'Weaker foreign attackers do not trigger.' }
    }
}
Check ([BellumCivile.CourtRoyalPeaceRules]::Chance(1000,1000,1000,0,0,0) -eq 45) 'Equal-strength invasion with half the realm diverted: 45 percent.'
Check ([BellumCivile.CourtRoyalPeaceRules]::Chance(1000,2000,1000,1,1,-1) -eq 80) 'Merciful calculating cautious Crown under severe threat: 80 percent.'
Check ([BellumCivile.CourtRoyalPeaceRules]::Chance(1000,2000,1000,-1,-1,1) -eq 50) 'Opposite personality under same threat: 50 percent.'
Check ([BellumCivile.CourtRoyalPeaceRules]::Chance(1000,2000,0,0,0,0) -eq 0) 'No recoverable strength means no crisis.'
Check (![BellumCivile.CourtRoyalPeaceRules]::Substantial([double]::NaN,1000)) 'Invalid military measurements rejected.'
Check ([BellumCivile.CourtRoyalPeaceRules]::ExecutionDay(40,5) -eq 45) 'Full deliberation window guaranteed.'
Check ([BellumCivile.CourtRoyalPeaceRules]::ExecutionDay(40,0) -eq 41) 'No same-tick decree.'
