$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw -LiteralPath (Join-Path $root $path) }
function Check($ok, $message) { if (!$ok) { throw $message }; "PASS: $message" }
$recovery = Read 'Behaviors/RestoredRealmRecovery.cs'
$resolution = Read 'Behaviors/CivilWarResolutionBehavior.cs'
$collapse = Read 'Behaviors/CivilWarCollapseContinuation.cs'
$record = Read 'RestoredRealmCreationRecord.cs'
$orphan = Read 'Behaviors/RestoredRealmOrphanRecovery.cs'
$migration = Read 'Patches/ProtectedRealmLoadMigrationPatch.cs'
$save = Read 'BellumCivileSaveDefiner.cs'
Check ($resolution.Contains('SyncData("BC_RestoredRealmCreations"') -and
    $save.Contains('AddClassDefinition(typeof(RestoredRealmCreationRecord), 137)') -and
    $save.Contains('ConstructContainerDefinition(typeof(List<RestoredRealmCreationRecord>))')) 'Restoration identity has a persistent, registered save journal.'
Check ($recovery.IndexOf('_restoredCreations.Add(record)') -lt $recovery.IndexOf('KingdomCreationSafetyHelper.CreateKingdom') -and
    $recovery.Contains('k.StringId == record.SuccessorId')) 'Allocation and retries use an identity recorded before native callbacks.'
Check ($record.Contains('!Ready') -and $record.Contains('!MovesComplete') -and
    $recovery.Contains('IsOtherRealmTransferPending(realm)')) 'Restoration-owned retirement never bypasses another conflict transfer.'
Check ($resolution.Contains('ResumePendingRestoredCreations();') -and $resolution.Contains('_resumingRestoredCreations.Contains(restoration)') -and
    $resolution.Contains('kvp.Key == restoration?.Owner')) 'Hourly continuation preserves its original owner rather than selecting a new victor.'
Check ($resolution.Contains('if (!TransferCivilWarClan(preferredRuler') -and
    $resolution.Contains('if (!rebelKingdom.IsEliminated)') -and $resolution.Contains('QueuePendingRebelCleanup(rebelKingdom, resolvedFallback)')) 'Failed founder movement and no-op destruction cannot report successful completion.'
Check ($recovery.IndexOf('source retirement pending') -lt $recovery.IndexOf('OnKingdomCreated(realm)') -and
    $recovery.IndexOf('successor crown transfer pending') -lt $recovery.IndexOf('OnKingdomCreated(realm)') -and
    $recovery.IndexOf('record.CreatedNotified = true') -lt $recovery.IndexOf('OnKingdomCreated(realm)')) 'Publication follows verified transfer, retirement and crown delivery with a pre-callback receipt.'
Check ($collapse.Contains('record.CreatedNotified = true') -and
    (Read 'CivilWarCollapseRecord.cs').Contains('[SaveableField(21)] public bool CreatedNotified;')) 'Staged collapse uses an append-only publication receipt too.'
Check ($collapse.Contains('c.Stage == 2 && c.Winner == faction') -and $collapse.Contains('c.WinnerClans.Contains(clan)') -and
    (Read 'Behaviors/FactionManagerBehavior.cs').Contains('OwnsCollapseHouseholdTransfer(faction, clan, oldKingdom, newKingdom)')) 'Only the running collapse owner can preserve coalition membership during its captured household transfers.'
Check ($collapse.Contains('claim.OutcomeVictor != record.WinnerHouse') -and $collapse.Contains('CollapseCrownDelivered(record)') -and
    $collapse.Contains('claim.HasWarCrownReceipt') -and $collapse.Contains('"restored hereditary challenge victory"')) 'Hereditary collapse recovery validates the saved victor and verifies legal Crown delivery without reelecting a coalition leader.'
foreach ($gate in @('realm.Leader != null', 'CountStrongholds(realm) > 0', 'Clan.All.Any', 'Settlement.All.Any',
    'IsRealmTransferPending', 'IsPending(realm)', 'IsRealmUnionProtected', 'PendingDeposition',
    'IsCrownPromotionRealmProtected', 'IsKingdomIdReferencedByBellumCivileState', 'FindExistingRestoredSuccessor')) {
    Check ($orphan.Contains($gate)) "Historical orphan recovery respects: $gate"
}
Check ($migration.Contains('MBSaveLoad.IsUpdatingGameVersion') -and $migration.Contains('v1.3.14') -and
    $migration.Contains('realm.IsEliminated || realm.RulingClan != null') -and $migration.Contains('!IsProtected(realm)') -and
    $migration.Contains('private static void Finalizer(') -and !$migration.Contains('ref Exception')) 'Load recovery is confined to the native version migration and preserves existing successors, retirement and exceptions.'
'Source contracts passed; run --restored-realm-recovery against each game installation.'
