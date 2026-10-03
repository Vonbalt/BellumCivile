$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS: $message"
}
$line = Read 'HereditaryRealmSuccession.cs'
$dynasty = Read 'Behaviors/DynasticHeirBehavior.cs'
$panel = Read 'UI/VanillaTabs/Kingdoms/Succession/KingdomSuccessionTabVM.cs'
Check ($line -match 'Clan.All.Where' -and $line -match 'CanConsiderClan' -and $line -match 'IsTemporaryBellumKingdom') 'Realm candidates include permanent foreign households while handling temporary realms explicitly.'
Check ($line -match 'GetAncestors\(hero\).Overlaps\(ancestors\)' -and $line -match 'applyInheritanceAdvances: false') 'Crown eligibility requires genealogy, not household membership or a fresh estate entitlement.'
Check ($panel -match 'return HereditaryRealmSuccession.GetLine\(_kingdom\)') 'Succession panel uses the complete realm line.'
foreach ($file in @('Behaviors/CrownAccessionBehavior.cs', 'Behaviors/CrownAbdication.cs', 'Behaviors/CrownForcedAbdication.cs')) {
    $code = Read $file
    Check ($code -match 'HereditaryRealmSuccession.GetLine' -and $code -notmatch 'SuccessionLawHelper.GetLegalSuccessionLine') "$file has no clan-local Crown fallback."
}
Check ($dynasty -match 'hereditary_realm_line' -and $dynasty -match 'HereditaryRealmSuccession.IsBloodRelative\(cached, sovereign\)') 'Hereditary state and cached title checks reject stale unrelated heirs.'
Write-Output 'Source contracts only; full campaign succession and save/load require in-game verification.'
