$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$titles = Get-Content -Raw (Join-Path $root 'Behaviors/FeudalTitleBehavior.cs')
$custody = Get-Content -Raw (Join-Path $root 'Behaviors/FeudalAllocationCustody.cs')
$ideology = Get-Content -Raw (Join-Path $root 'Behaviors/IdeologyBehavior.cs')
Check ($titles.Contains('dataStore.SyncData("BellumCivile_AllocationCustodians", ref _allocationCustodianByTitle)')) 'Custody survives saving and loading independently of pending vote removal.'
Check ($titles.Contains('if (!wasAllocationCustody || previousDeJure != previousDeFacto)')) 'Custody suppresses only the temporary holder claim, not a third-party legal owner claim.'
Check ($titles.Contains('if (!IsAllocationCustodian(title.TitleId, newClanId))')) 'Existing custodian claims are not erased before distribution.'
Check ($custody.Contains('GetTitle(titleId)?.DeJureHolderClanId == custodian.StringId')) 'Prior Crown legal ownership is not misclassified as temporary rights.'
Check ([regex]::Matches($ideology, 'RecordAllocationCustody\(fief.Settlement, rulingClan\);\s*ChangeOwnerOfSettlementAction.ApplyByDefault').Count -eq 2) 'Both treason confiscation paths record custody before the transfer.'
Check ($titles.Contains('RecordAllocationCustody(settlement, parentRulingClan);')) 'Peaceful sovereign-separation redistribution also records custody.'
Check ($titles.Contains('detail, openToClaim)')) 'Ownership handler forwards pending-allocation status to legal-transfer rules.'
