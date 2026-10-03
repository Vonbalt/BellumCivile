$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$planner = Get-Content -Raw (Join-Path $root 'FeudalInheritancePlanner.cs')
$votes = Get-Content -Raw (Join-Path $root 'Behaviors/FiefDeliberationBehavior.cs')
$partition = Get-Content -Raw (Join-Path $root 'Behaviors/PartitionSuccessionBehavior.cs')
$delivery = Get-Content -Raw (Join-Path $root 'Behaviors/CrownAbdication.cs')
function Check($condition, $message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS: $message"
}
Check ($planner -match 'estateFiefs \?\? GetPartitionableFiefs\(parentClan\)\)\s+\.Where\(fief => IsHeritableFief') 'Supplied inheritance snapshots also exclude temporary allocation custody.'
Check ($votes -match 'Town.IsOwnerUnassigned' -and $votes -match 'behavior\?\._pendingFiefDate.ContainsKey\(key\)' -and $votes -match 'behavior\?\._pendingFirstRightDate.ContainsKey\(key\)' -and $votes -match 'UnresolvedDecisions.OfType<SettlementClaimantDecision>') 'Custody covers unassigned fiefs, delayed votes, first refusal and active allocation decisions.'
Check ($planner -match 'plan.EstateTitles.RemoveAll' -and $planner -match 'holdings.Count > 0 && holdings.All' -and $planner -match 'title.TitleType >= FeudalTitleType.Kingdom') 'Entirely custodial landed titles are excluded, without excluding mixed estates or sovereign titles.'
Check ($partition -match 'Where\(f => FeudalInheritancePlanner.IsHeritableFief\(f, parentClan\)\)' -and $partition -match 'partitionEstateTitles = inheritancePlan.EstateTitles.ToList') 'Death partition revalidates snapshots and claim registration uses the filtered titles.'
Check ($delivery -match 'owner == source && FiefDeliberationBehavior.IsAwaitingAllocation' -and $delivery -match 'IsAllocationCustodyTitle\(title, titles, source\)') 'Frozen undelivered endowments wait instead of consuming newly pending allocations.'
Check ($votes -match '!openToClaim && newOwner\?\.Clan != oldOwner\?\.Clan') 'Ruler replacement within the holding clan preserves deliberations.'
Check ($votes -match 'detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByKingDecision\)\s+ClearFiefVoteStateForSettlement') 'Authoritative external grants clear stale decisions without removing a currently resolving native vote.'
Write-Output 'Source contracts only; confiscation, abdication, mixed titles and saved pending votes require live campaign testing.'
