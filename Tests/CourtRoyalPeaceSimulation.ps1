$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = (Get-Content -Raw (Join-Path $root 'CourtRoyalPeaceRules.cs')).Replace('internal', 'public')
$source += "`n" + (Get-Content -Raw (Join-Path $root 'BellumCivileConstants.cs'))
Add-Type -TypeDefinition $source
function Check($condition, $label) { if (!$condition) { throw $label }; "PASS: $label" }
foreach ($case in @(@(0,0,0), @(1,0,100), @(0,1,100), @(2,0,150), @(0,2,175), @(1,1,150), @(2,3,350))) {
    $feuds, $wars, $cost = $case
    Check ([BellumCivile.CourtRoyalPeaceRules]::Cost($feuds,$wars,$true) -eq $cost) "Wartime price: $feuds unarmed, $wars armed = $cost."
    Check ([BellumCivile.CourtRoyalPeaceRules]::Cost($feuds,$wars,$false) -eq 3*$cost) 'Peacetime triples the entire quote.'
}
Check ([BellumCivile.CourtRoyalPeaceRules]::Cost(-1,1,$true) -eq 100) 'Negative unarmed counts cannot discount an armed war.'
Check ([BellumCivile.CourtRoyalPeaceRules]::Cost(1,-1,$true) -eq 100) 'Negative armed counts cannot discount a feud.'
foreach ($wartime in @($true,$false)) {
    $scale = if ($wartime) { 1 } else { 2 }
    Check ([BellumCivile.CourtRoyalPeaceRules]::RelationPenalty($true,$wartime) -eq -15*$scale) 'Involved-house relation penalty.'
    Check ([BellumCivile.CourtRoyalPeaceRules]::RelationPenalty($false,$wartime) -eq -5*$scale) 'Other noble-house relation penalty.'
    Check ([BellumCivile.CourtRoyalPeaceRules]::QuoteStillValid($wartime,300,$wartime,300)) 'Unchanged quote remains valid.'
    Check ([BellumCivile.CourtRoyalPeaceRules]::QuoteStillValid($wartime,300,$wartime,150)) 'Cheaper quote remains valid.'
    Check (![BellumCivile.CourtRoyalPeaceRules]::QuoteStillValid($wartime,300,$wartime,301)) 'Even a one-point increase requires new consent.'
    Check (![BellumCivile.CourtRoyalPeaceRules]::QuoteStillValid($wartime,300,(!$wartime),100)) 'Mode changes invalidate even cheaper quotes.'
}
function Chance($wartime, $feuds, $wars, $age, $available = 750, $enemy = 1000, $diverted = 250, $mercy = 0, $calculating = 0, $valor = 0) {
    [BellumCivile.CourtRoyalPeaceRules]::RealmChance($wartime,$feuds,$wars,$age,$available,$enemy,$diverted,$mercy,$calculating,$valor)
}
Check ((Chance $false 1 0 100) -eq 0) 'NPCs do not impose peacetime peace on one unarmed feud.'
Check ((Chance $false 2 0 100) -eq 0) 'Two unarmed feuds still do not justify peacetime intervention.'
Check ((Chance $false 3 0 0) -gt 0) 'Three unarmed feuds can justify peacetime intervention.'
Check ((Chance $false 0 1 13.99) -eq 0) 'One fresh private war does not justify peacetime intervention.'
Check ((Chance $false 0 1 14 751 1000 249) -eq 0) 'An old war below one-quarter diverted strength does not qualify.'
Check ((Chance $false 0 1 14) -eq 18) 'A fourteen-day war diverting exactly one quarter qualifies.'
Check ((Chance $false 0 2 0 1000 0 0) -eq 21) 'Two private wars qualify without an age or diverted-strength threshold.'
Check ((Chance $true 1 0 0 1000 500 0) -gt 0) 'Wartime intervention is not restricted to stronger attackers or armed feuds.'
Check ((Chance $true 0 0 100) -eq 0) 'No private conflicts means no decree.'
Check ((Chance $true 1 0 0 1000 0 250) -eq 0) 'No external enemy means no wartime inclination.'
Check ((Chance $true 1 1 14 1000 1000 1000) -eq 52.5) 'Wartime chance combines pressure, diverted strength and conflict counts.'
Check ((Chance $true 1 1 14 1000 1000 1000 1 1 -1) -eq 67.5) 'Mercy, calculation and caution each increase inclination.'
Check ((Chance $true 1 1 14 1000 1000 1000 -1 -1 1) -eq 37.5) 'Opposite traits reduce inclination.'
foreach ($bad in @([double]::NaN,[double]::PositiveInfinity,[double]::NegativeInfinity)) {
    Check ((Chance $true 1 1 14 $bad 1000 250) -eq 0) 'Invalid available strength is rejected.'
    Check ((Chance $true 1 1 14 1000 $bad 250) -eq 0) 'Invalid enemy strength is rejected.'
    Check ((Chance $false 0 2 14 1000 1000 $bad) -eq 0) 'Invalid diverted strength is rejected even in peacetime.'
}
foreach ($wartime in @($true,$false)) {
    $cap = if ($wartime) { 95 } else { 60 }
    foreach ($enemy in @(0,500,1000,3000)) {
        foreach ($diverted in @(0,250,1000)) {
            $chance = Chance $wartime 3 2 30 1000 $enemy $diverted
            Check ($chance -ge 0 -and $chance -le $cap) "Realm chance bounded by $cap (wartime=$wartime, enemy=$enemy, diverted=$diverted)."
        }
    }
    Check ((Chance $wartime 3 2 30 1000 3000 1000 100 100 -100) -eq $cap) 'Extreme favorable traits respect the cap.'
}
Check ([BellumCivile.CourtRoyalPeaceRules]::ExecutionDay(40,5) -eq 45) 'Full deliberation window guaranteed.'
Check ([BellumCivile.CourtRoyalPeaceRules]::ExecutionDay(40,0) -eq 41) 'No same-tick decree.'
Check ([BellumCivile.CourtRoyalPeaceRules]::ExecutionDay(40,-5) -eq 41) 'Invalid negative notice still grants a day.'
