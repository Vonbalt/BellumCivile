$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$partition = Get-Content -Raw (Join-Path $root 'Behaviors/PartitionSuccessionBehavior.cs')
$marriage = Get-Content -Raw (Join-Path $root 'Behaviors/DynasticHeirBehavior.cs')
$household = Get-Content -Raw (Join-Path $root 'Behaviors/CadetHouseholdBehavior.cs')
$companion = Get-Content -Raw (Join-Path $root 'Behaviors/CompanionSubinfeudationService.cs')
function Check($condition, $message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS: $message"
}
Check (([regex]::Matches($partition, 'EnsureFoundingMembers\(')).Count -eq 2) 'Partition and abdication both seed cadet members.'
Check ($marriage -notmatch 'EnsureFoundingMembers\(|Clan.CreateClan\(') 'Marriage no longer creates or populates cadet branches.'
Check ($partition.IndexOf('EnsureCrownHeirRegency(cadet') -lt $partition.IndexOf('EnsureFoundingMembers(cadet)')) 'Abdication establishes regency before adding nobles.'
Check ($household -match 'RegisterEvents\(\) \{ \}' -and $household -notmatch 'AddNonSerializedListener') 'No population-maintenance scheduler or broad clan-created listener.'
Check ($household -match 'CompanionSubinfeudationService.InitializeNewHouseMemberSkills' -and $companion -match 'InitializeNewHouseMemberSkills\(hero, steward\)') 'Cadets and companion promotion share skill generation.'
Check ($household -notmatch '\.(Father|Mother|Spouse)\s*=|RegisterClaim|SetLeader|EnsureCrownHeirRegency') 'Generated nobles do not fabricate family ties, claims or replace leaders/regents.'
$registry = Get-Content -Raw (Join-Path $root 'BellumCivileSaveDefiner.cs')
$module = Get-Content -Raw (Join-Path $root 'SubModule.cs')
Check ($registry -match 'typeof\(Dictionary<string, Hero>\)' -and $module -match 'AddBehavior\(new CadetHouseholdBehavior\(\)\)') 'Founding-member behavior and save container are registered.'
Write-Output 'Source contracts only; real hero generation and engine save/load require campaign testing.'
