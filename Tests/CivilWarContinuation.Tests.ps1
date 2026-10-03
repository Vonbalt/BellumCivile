$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$manager = Read 'Behaviors/FactionManagerBehavior.cs'
$event = $manager.Substring($manager.IndexOf('private void OnRulingClanChanged'))
Check ($event.IndexOf('if (faction.HasTrackedRebelKingdom') -lt $event.IndexOf('if (faction.Type == FactionType.Abdication)')) 'Active wars bypass synchronous ruler-change faction removal and ultimatum logic.'
Check ($manager.Contains('!f.HasTrackedRebelKingdom && !f.IsChallengeStartupPending')) 'Daily pruning cannot discard a tracked war whose claimant gains the Crown.'
$settlement = Read 'Behaviors/CivilWarDemandContinuation.cs'
foreach ($gate in @('IsChallengeStartupPending', 'IsPending(realm)', '_pendingSuccessionCandidates.ContainsKey', 'KingSelectionKingdomDecision', 'DeJureHolderClanId', 'DeFactoHolderClanId')) {
    Check ($settlement.Contains($gate)) "Satisfied-demand settlement checks: $gate"
}
Check ($settlement.Contains('faction.AbdicationMonarch != null') -and $settlement.Contains('legalRuler != faction.AbdicationMonarch')) 'Abdication is person-specific and does not invent a target for missing legacy records.'
Check ($settlement.Contains('ResolveWhitePeace(faction, rebel, demandSatisfied: true)') -and $settlement -notmatch 'ApplyPostWarConsequences|QueuePlayerTribunal|TriggerUltimatum') 'Satisfied demands settle their own coalition without punishment or a fresh ultimatum.'
$faction = Read 'FactionObject.cs'
Check ($faction.IndexOf('if (type == FactionType.Abdication)') -lt $faction.IndexOf('if (type == FactionType.Abdication && CrownAccessionBehavior.IsHereditaryRealm')) 'Elective and hereditary abdication factions both capture their targeted monarch.'
Check ((Read 'Behaviors/ElectiveContestDispatch.cs').Contains('!StartRivalry(record,')) 'Dual claimant dispatch connects to coordinated rivalry tracking.'
[xml]$strings = Read 'ModuleData/Languages/EN/strings.xml'
Check (@($strings.base.strings.string | Where-Object id -eq 'BC_CivilWar_DemandSatisfied').Count -eq 1) 'Satisfied-demand notification has one localized definition.'
'Source contracts only; live succession, realm transfers and save/load remain untested.'
$journal = Read 'Behaviors/CivilWarConflictBehavior.cs'
Check ($journal.Contains('store.SyncData("BC_CivilWarConflicts"') -and $journal.Contains('SovereignTitleId == crown.TitleId')) 'Conflict journal persists and groups concurrent wars by sovereign title.'
Check ($journal -notmatch 'DeclareWarAction|DailyTickEvent|ResolveRebelVictory') 'Journal observation does not initiate rivalry or resolve wars.'
Check ([regex]::Matches($faction, 'CivilWarConflictBehavior.Instance\?\.Observe').Count -eq 3) 'Normal startup and both recovery paths observe their existing wars.'
$save = Read 'BellumCivileSaveDefiner.cs'
foreach ($type in @('CivilWarConflictRecord', 'CivilWarSideRecord', 'CivilWarPairRecord')) {
    Check ($save.Contains("AddClassDefinition(typeof($type)") -and $save.Contains("ConstructContainerDefinition(typeof(List<$type>))")) "Conflict save registration: $type"
}
Check ($faction.Contains('_civilWarOriginShellPrefix = _civilWarOriginShellPrefix ?? shell.StringId;')) 'Parent retarget retains the rebellion shell identity.'
$score = Read 'Behaviors/WarScoreBehavior.cs'
Check ($score.Contains('w != record && w.IsActive && w.WarKey == key')) 'Score retarget rejects collision with a distinct active conflict.'
Check ((Read 'WarScoreRecord.cs').Contains('!_whitePeaceOfferPending && !_parleyPending && !_resolutionPending')) 'Retarget waits for outstanding settlement choices.'
$transfer = Read 'Behaviors/CivilWarCrownTransfer.cs'
Check ($transfer.Contains('_transfers.Add(transfer)') -and $transfer.Contains('transfer.Started = true;')) 'Original native state is saved before transfer stages start.'
Check ($transfer.Contains('FactionManager.DeclareWar(') -and $transfer.Contains('FactionManager.SetNeutral(') -and $transfer -notmatch 'MakePeaceAction|DeclareWarAction|ChangeKingdomAction|ReleasePrisoner') 'Native stance transfer avoids negotiated peace and new-war action side effects.'
Check ($transfer.Contains('previous.Clans.Any(c => !c.IsEliminated)') -and $transfer.Contains('title.DeFactoHolderClanId')) 'Successor transfer waits for old households to move and the new title mantle to settle.'
Check ($transfer.IndexOf('TransferPostconditions(transfer, p)') -lt $transfer.IndexOf('transfer.Conflict.CrownRealm = transfer.Successor')) 'Conflict Crown identity commits only after all pair postconditions pass.'
Check ($journal.Contains('HourlyTickEvent.AddNonSerializedListener(this, ResumePendingCrownTransfers)') -and $transfer.Contains('_transfers.Where(t => !t.Completed)')) 'Hourly recovery only resumes saved incomplete transfers; it does not begin new conflicts.'
$challenge = Read 'Behaviors/SuccessionChallengeWar.cs'
Check ($challenge.Contains('!record.WarShell.IsAtWarWith(record.WarRealm)') -and $challenge.Contains('record.WarRealm.Clans.Where')) 'Challenge maintenance and tribunal loser selection follow the current Crown opponent.'
Check ((Read 'SuccessionChallengeRecord.cs').Contains('WarRealm => WarFaction?.ParentKingdom ?? Realm')) 'Challenge origin and estate receipts are not rewritten when its war changes opponent.'
Check ($challenge.Contains('IsFactionTransferPending(record.WarFaction)')) 'Hereditary maintenance waits for the complete Crown transfer.'
$resolution = Read 'Behaviors/CivilWarResolutionBehavior.cs'
foreach ($method in @('CleanupExternallyResolvedFaction', 'ResolveLiegeVictoryInternal', 'ResolveRebelVictory', 'ResolveUnscriptedPeace', 'ResolveWhitePeace', 'ResolveRecognizedIndependence')) {
    Check ([regex]::IsMatch($resolution, "(?:private|public|internal) (?:void|bool) $method\([^\r\n]*\)\s*\{\s*(?:CivilWarTransitionDiagnostics\.Log\([^\r\n]*(?:\r?\n[^\r\n]*)?\);\s*)?if \(CivilWarConflictBehavior.IsFactionTransferPending")) "Settlement entry waits for pending transfer: $method"
}
Check ($score.Contains('if (CivilWarConflictBehavior.IsScoreTransferPending(war)) return false;')) 'Queued terminal score resolution is deferred without clearing its receipt.'
Check ($manager.Contains('!CivilWarConflictBehavior.IsRealmTransferPending(k)')) 'Superseded-shell cleanup excludes realms owned by pending transfers.'
$collapse = Read 'Behaviors/CivilWarCollapseContinuation.cs'
Check ($resolution.Contains('TryBeginContinuingCollapse(collapsedParent, activeRebellions)') -and $resolution.Contains('ResumePendingCollapses();')) 'Supported live collapses enter the saved coordinator and retry hourly.'
Check ($collapse.IndexOf('_collapses.Add(record);') -lt $collapse.IndexOf('if (!deferResume) ResumeCollapse(record);')) 'Collapse intent is recorded before successor creation or household transfers.'
Check ($collapse.Contains('k.StringId == record.SuccessorId') -and $collapse.Contains('CreateKingdom(record.SuccessorId')) 'Recovery reuses the saved successor identity instead of choosing another suffix.'
Check ($collapse.IndexOf('BeginCrownTransfer(record.Conflict') -lt $collapse.IndexOf('TransferClansToKingdom(record.WinnerRealm')) 'Surviving native wars are captured before household movement.'
Check ($collapse.IndexOf('ResumeCrownTransfer(record.Transfer)') -lt $collapse.IndexOf('DrainAndDestroyRebelKingdom(record.WinnerRealm')) 'Old shells are drained only after surviving opponent transfers commit.'
Check ($collapse -notmatch 'ResolveWhitePeace|ApplyPacifiedCooldowns' -and $collapse.Contains('TryQueueChallengeTribunal(record.Winner, record.Successor)')) 'Continuing rivals are not white-peaced or pacified; only the victorious challenge queues its saved tribunal.'
Check ($collapse.Contains('p.Score.WhitePeaceOfferPending || p.Score.ParleyPending || p.Score.ResolutionPending')) 'Outstanding settlement choices finish before collapse protection begins.'
Check ($resolution.Contains('SyncData("BC_CivilWarCollapses"') -and $save.Contains('AddClassDefinition(typeof(CivilWarCollapseRecord), 86)')) 'Collapse coordinator has a registered save type and persistent journal.'
$challengeRecovery = Read 'Behaviors/CivilWarSuccessionSettlement.cs'
Check ($challengeRecovery.IndexOf('TryResumeChallengeCollapse(record)') -lt $challengeRecovery.IndexOf('realm = CreateRestoredSuccessorKingdom')) 'Owned hereditary outcomes defer to collapse recovery before any alternate successor creation.'
Check ($collapse.IndexOf('PrepareWarOutcome(winner, SuccessionChallengeOutcome.Victory, parent)') -lt $collapse.IndexOf('_collapses.Add(record)')) 'Hereditary winner snapshots the original loyalists before any household transfer.'
Check ($collapse.IndexOf('record.Stage = 3') -lt $collapse.IndexOf('LegalizeChallengeVictory(record.Winner, record.Successor)')) 'Hereditary estates become lawful only after the successor mantle and rival wars commit.'
Check ($collapse.Contains('record.Challenge.Phase != SuccessionChallengePhase.Settled') -and $collapse.Contains('CompleteWarOutcome(record.Winner, record.Successor)')) 'Collapse completion waits for the existing hereditary outcome and cooldown path.'
Check ($collapse.Contains('if (record.Challenge == null) record.RewardsApplied = true;') -and $collapse.Contains('if (!record.InfluenceRestored)')) 'Hereditary reward retries use per-clan receipts without resetting influence again.'
$destruction = Read 'Patches/CivilWarParentDestructionPatch.cs'
Check ($destruction.Contains('[HarmonyPatch(typeof(DestroyKingdomAction), "ApplyInternal")]') -and $destruction.Contains('if (isKingdomLeaderDeath) return true;')) 'Normal destruction is intercepted before deactivation; leader-death destruction remains separate.'
Check ($collapse.Contains('TryBeginContinuingCollapse(parent, wars, deferResume: true)') -and $collapse.Contains('return existing.Stage < 3;')) 'Destruction only reserves a collapse; the old shell can be destroyed after war transfer commits.'
Check ($collapse.IndexOf('journal.CaptureCrownPairs(conflict, winner)') -lt $collapse.IndexOf('_collapses.Add(record)') -and $collapse.Contains('NativePairs = nativePairs')) 'Native war history is captured and saved before normal destruction is postponed.'
Check ($transfer.Contains('matching.Count != 1 || matching[0].Stage != 0 || matching[0].NativeCounts.Count != 10')) 'Only matching, complete and unconsumed native snapshots can initialize a transfer.'
Check ($collapse.Contains('record.Transfer == null && !record.Parent.IsEliminated')) 'Deferred snapshots refresh before household movement only while native history still exists.'
