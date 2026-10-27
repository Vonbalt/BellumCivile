$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$repair = Get-Content -Raw -LiteralPath (Join-Path $root 'Behaviors/IndependenceRulerSaveRepair.cs')
$registry = Get-Content -Raw -LiteralPath (Join-Path $root 'Behaviors/FeudalTitleBehavior.cs')
function Check($ok, $message) { if (!$ok) { throw $message }; "PASS: $message" }
Check ($registry.Contains('CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, SkipNewGameIndependenceRulerRepair);') -and
    $registry.Contains('CampaignEvents.OnAfterSessionLaunchedEvent.AddNonSerializedListener(this, StartIndependenceRulerRepair);')) 'New games are exempt and loaded campaigns wait until after session initialization.'
Check ($registry.Contains('SyncIndependenceRulerRepair(dataStore);') -and
    $repair.Contains('store.SyncData("BellumCivile_IndependenceRulerRepairVersion", ref _independenceRulerRepairVersion);') -and
    $repair.Contains('store.SyncData("BellumCivile_PendingIndependenceRulerRepairs", ref _pendingIndependenceRulerRepairs);')) 'Completion version and deferred realm queue use explicit per-save keys shared by both game branches.'
Check ($repair.Contains('_independenceRulerRepairVersion >= IndependenceRulerRepairVersion || _independenceRulerRepairRunning') -and
    $repair.Contains('if (_pendingIndependenceRulerRepairs.Count != 0) return;')) 'Only an unfinished migration can retry and deferred entries prevent premature completion.'
Check ($repair.Contains('source.DeFactoHolderClanId != source.DeJureHolderClanId') -and
    $repair.Contains('MatchesOriginalIndependenceFounder(id, k, founder)') -and
    $repair.Contains('NobleClanEligibilityHelper.IsValidRulingClan(founder, realm)')) 'Founding ownership, original generated identity and current resident-ruler eligibility must agree.'
Check ($repair.Contains('previous == Clan.PlayerClan') -and
    $repair.Contains('source.AssociatedKingdomId != id') -and
    $repair.Contains('founder_already_referenced_as_another_ruler')) 'Player rulers, changed title associations and competing ruler references are protected.'
foreach ($guard in @('manager.IsRulerRepairSuppressed(realm)', 'IsPending(realm)', 'PendingDeposition(realm)',
    'CivilWarConflictBehavior.IsRealmTransferPending(realm)', 'KingSelectionKingdomDecision')) {
    Check ($repair.Contains($guard)) "Pending transitions retain their authority: $guard"
}
Check ($repair.IndexOf('_currentRealmTitleByKingdomId[id] = source.TitleId;') -lt $repair.IndexOf('realm.RulingClan = founder;')) 'Verified crown binding is established before the callback-capable raw ruler assignment.'
foreach ($forbidden in @('TrySetKingdomTitleRuler(', 'SetDeJureHolder(', 'SetDeFactoHolder(', 'SetAssociatedKingdom(',
    'ChangeKingdomAction.', 'ChangeOwnerOfSettlementAction.', 'ChangeRulingClanAction.', 'CreateKingdom(', 'DeclareWarAction.')) {
    Check (!$repair.Contains($forbidden)) "Migration does not change ownership, membership, or replay political actions: $forbidden"
}
Check ($repair.Contains('stage=repaired') -and $repair.Contains('stage=skipped') -and $repair.Contains('stage=deferred') -and
    !$repair.Contains('BellumCivileDebug')) 'Repaired, ambiguous and deferred cases are logged independently of debug settings.'
'Independence ruler save-repair source contracts passed; run the compiled migration and settlement regressions.'
