$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$marriage = Get-Content -Raw (Join-Path $root 'Behaviors/DynasticHeirBehavior.cs')
$incoming = Get-Content -Raw (Join-Path $root 'Behaviors/CrownIncomingHouse.cs')
$partition = Get-Content -Raw (Join-Path $root 'Behaviors/PartitionCrossClanInheritance.cs')
function Check([bool] $condition, [string] $label) {
    if (!$condition) { throw $label }
    Write-Output "PASS: $label"
}
Check ($marriage -notmatch 'QueuePendingCadetMarriage|ProcessPendingCadetMarriages|TryResolvePendingCadetMarriage|new PendingCadetMarriageRecord\(') 'No marriage cadet producer or executor remains.'
Check ($marriage -notmatch 'OnBarterAcceptedEvent|capturedMarriageGold|ApplyCadetMarriageDowry|GiveGoldAction|ChangeOwnerOfSettlementAction|Clan.CreateClan') 'Marriage cadet estate/payment actions are removed.'
$hourly = @([regex]::Matches($marriage, 'CampaignEvents\.HourlyTickEvent\.AddNonSerializedListener\(this, ([A-Za-z0-9_]+)\)'))
Check ($hourly.Count -eq 1 -and $hourly[0].Groups[1].Value -eq 'RefreshChangedHouseholds') 'The hourly listener refreshes household caches, not retired marriage cadet jobs.'
Check ($marriage.Contains('_pendingCadetMarriages.Clear();') -and $marriage.Contains('BellumCivile_PendingCadetMarriages')) 'Legacy queued endowments are read and discarded, not executed.'
Check ($marriage.Contains('BellumCivile_CadetBranchOrigins') -and $marriage.Contains('BuildRoyalHeiressCadetName')) 'Existing branch origins and shared accession naming remain available.'
Check ($marriage.Contains('TrackMarriageRights(firstHero, secondHero, destination);') -and $marriage.Contains('TrackMarriageRights(secondHero, firstHero, destination);')) 'Marriage rights are checked for both spouses.'
Check ($marriage -match 'IsHereditaryRealm\(kingdom\)\)\s+SetDynasticState\(kingdom, dynasticHeir, dynastyClan, clanAfterMarriage, dynasticHeir') 'NPC hereditary marriage preserves the personal heir without a cadet.'
Check ($incoming.Contains('CanUseExistingCrownHouse(heir, source.Leader, legalHead)') -and $incoming.Contains('h != source.Leader && h != legalHead')) 'Accession reuses existing leadership and does not move an independently leading spouse.'
Check ($partition.Contains('household.Concat(descendants)') -and $partition.Contains('FeudalInheritancePlanner.GetLivingAccessionShare')) 'Married-out descendants retain the shared death-inheritance package path.'
Write-Output 'Source contracts only; marriage, succession and save/load require campaign testing.'
