$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -TypeDefinition ((Get-Content -Raw (Join-Path $root 'CourtLiberationRules.cs')).Replace('internal', 'public'))
$checks = 0
foreach ($readiness in @(100,125,150,200)) {
    foreach ($desire in @(40,60,80)) {
        $weight = [BellumCivile.CourtLiberationRules]::Weight($readiness,$desire,0,0)
        'Readiness {0}%, baseline Crown desire {1}: weight {2:0.00}; share against three weight-1 alternatives {3:0.0}%' -f $readiness,$desire,$weight,(100*$weight/(3+$weight))
    }
}
foreach ($ready in 100..200) {
    foreach ($valor in -2..2) {
        foreach ($calculating in -2..2) {
            $w = [BellumCivile.CourtLiberationRules]::Weight($ready,60,$valor,$calculating)
            if ($w -lt 0.15 -or $w -gt 1.25) { throw 'Weight out of bounds' }; $checks++
        }
    }
}
if ([BellumCivile.CourtLiberationRules]::Weight(99,60,2,2) -ne 0) { throw 'Inadequate readiness admitted' }; $checks++
if ([BellumCivile.CourtLiberationRules]::Active($true,$true,84,20,84)) { throw 'Bonus survives term expiry' }; $checks++
if ([BellumCivile.CourtLiberationRules]::Active($true,$false,40,20,84)) { throw 'Bonus survives changed identity' }; $checks++
if ([BellumCivile.CourtLiberationRules]::Active($false,$true,40,20,84)) { throw 'Bonus active before session' }; $checks++
"PASS: $checks Crown liberation selection/window checks; shares are illustrative competition, not campaign frequencies."
