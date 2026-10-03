$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$store = Get-Content -Raw (Join-Path $root 'Behaviors/RealmLawBehavior.cs')
$adapter = Get-Content -Raw (Join-Path $root 'Behaviors/SuccessionLawBehavior.cs')
$vm = Get-Content -Raw (Join-Path $root 'UI/VanillaTabs/Kingdoms/Succession/KingdomSuccessionTabVM.cs')
$registry = Get-Content -Raw (Join-Path $root 'RealmLawRegistry.cs')
function Check([bool] $condition, [string] $label) {
    if (!$condition) { throw $label }
    Write-Output "PASS: $label"
}
$laws = [xml](Get-Content -Raw (Join-Path $root 'ModuleData/bellum_laws.xml'))
$strings = [xml](Get-Content -Raw (Join-Path $root 'ModuleData/Languages/EN/strings.xml'))
$enums = Get-Content -Raw (Join-Path $root 'SuccessionLaws.cs')
$gender = $laws.SelectNodes('//Group[@id="gender"]/Law')
Check (($gender | ForEach-Object { $_.name -replace '^\{[^}]+\}', '' }) -join ',' -eq 'Male Only,Male Preference,Equal,Female Preference,Female Only') 'Gender law labels and order match the approved roster.'
foreach ($law in $laws.SelectNodes('//Group[@id!="elective_terms"]/Law')) {
    $snake = [regex]::Replace($law.value, '([a-z])([A-Z])', '$1_$2').ToLowerInvariant()
    $prefix = if ($law.ParentNode.id -eq 'gender') { 'BC_GenderLaw_' } else { 'BC_SuccessionLaw_' }
    Check ($law.id -eq ('law_' + $law.ParentNode.id + '_' + $snake) -and $enums -match ('\b' + $law.value + '\s*=')) "Canonical ID and enum for $($law.value)."
    foreach ($field in @('name', 'description')) {
        $key = $prefix + $law.value + $(if ($field -eq 'description') { '_Desc' } else { '' })
        $localized = $strings.SelectSingleNode("//string[@id='$key']")
        Check ($law.$field.StartsWith('{=' + $key + '}') -and $localized.text -eq ($law.$field -replace '^\{[^}]+\}', '')) "Matching localization key and fallback for $($law.value) $field."
    }
}
Check ($store.Contains('BellumCivile_RealmLawGroups') -and !$adapter.Contains('_realmLaws')) 'Only generic group selections own saved realm law state.'
Check ($adapter.Contains('GetCampaignBehavior<RealmLawBehavior>().GetSuccessionLaws') -and $adapter.Contains('GetDefaultLaws(cultureId, null')) 'Kingdom households use the typed adapter; independent cultural defaults remain.'
Check ($store.IndexOf('CanPlayerChangeLaws(realm') -lt $store.IndexOf('TryReplace(record') -and $store.IndexOf('TryReplace(record') -lt $store.IndexOf('CompletePlayerLawChange(realm')) 'Authority/pending/Crown/cost checks precede replacement and once-only consequences.'
Check ($store.Contains('current != expectedActiveLawId') -and $vm.Contains('definition.Id, expected')) 'UI confirmation uses an expected-current-law guard.'
Check (!$store.Contains('ActivePolicies') -and !$store.Contains('new PolicyObject') -and !$registry.Contains('new PolicyObject')) 'Grouped laws cannot leak into native independent policy proposals.'
Check ($registry.Contains('DtdProcessing.Prohibit') -and $registry.Contains('No law effect handler')) 'XML definitions reject unsafe parsing and unimplemented effects.'
Check ($store.Contains('new Dictionary<string, string>(origin.SelectedLaws') -and $store.Contains('Validate(origin)')) 'Generated realms inherit independent copies of the full law selection.'
Check ($vm.Contains('registry.InGroup(RealmLawRegistry.SuccessionGroup)') -and $vm.Contains('definition.Category')) 'Hereditary/elective columns share one exclusive succession group.'
Check ($store.Contains('public override void RegisterEvents() { }')) 'The law store adds no recurring scheduler.'
Write-Output 'Source contracts only; campaign save/load, payments and UI need in-game verification.'
