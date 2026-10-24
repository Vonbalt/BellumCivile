$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw -LiteralPath (Join-Path $root $path) }
function Check($ok, $message) { if (!$ok) { throw $message }; "PASS: $message" }
$grace = Read 'Behaviors/CivilWarGrace.cs'
$manager = Read 'Behaviors/FactionManagerBehavior.cs'
$resolution = Read 'Behaviors/CivilWarResolutionBehavior.cs'
$faction = Read 'FactionObject.cs'
$ideology = Read 'Behaviors/IdeologyBehavior.cs'
Check ($manager.Contains('HourlyTickEvent.AddNonSerializedListener(this, MaintainCivilWarGrace)') -and
    $manager.Contains('SyncCivilWarGrace(dataStore)') -and $grace.Contains('BC_CivilWarGrace')) 'Grace has saved state, legacy initialization and hourly settlement recovery.'
Check ($grace.Contains('!faction.HasStartedCivilWar') -and $grace.Contains('r.Faction == faction') -and
    $grace.Contains('BellumCivileOptions.CivilWarGraceYears * Math.Max(1, CampaignTime.DaysInYear)')) 'Actual wars capture a campaign-year duration once per faction, not once per leader or retry.'
Check ($resolution.Contains('QueueCivilWarGrace(faction, rebelKingdom, outcome)') -and
    $resolution.Contains('CompleteCivilWarTracker(faction, rebelKingdom, "loyalist victory")') -and
    $resolution.Contains('CompleteCivilWarTracker(faction, rebelKingdom, "rebel victory")') -and
    $resolution.Contains('CompleteCivilWarTracker(faction, rebelKingdom, "white peace")')) 'Loyalist victory, rebel victory and negotiated peace share the grace queue.'
Check ($resolution.Contains('AddCivilWarGraceRealms(faction, permanentKingdoms.ToArray())') -and
    $resolution.Contains('"parent realm collapsed into a civil-war successor", restoredSuccessorKingdom') -and
    (Read 'Behaviors/CivilWarCollapseContinuation.cs').Contains('"Crown mantle restored after outside conquest", record.Successor')) 'Independence partitions and restored successors explicitly identify their permanent outcome realms.'
Check ($grace.Contains('record.RebelRealm?.IsEliminated == false') -and $grace.Contains('IsPending(r)') -and
    $grace.Contains('PendingDeposition(r)') -and $grace.Contains('!challenge.ResolutionReturned')) 'Grace does not count down while shell retirement, Crown settlement or hereditary restitution is pending.'
Check ($grace.Contains('!faction.IsJudgmentResistance && !faction.IsCommittedCrownChallenge') -and
    $faction.Contains('[SaveableField(37)] private bool _judgmentResistance') -and
    $faction.Contains('[SaveableField(38)] private bool _committedCourtChallenge')) 'Committed challenges and explicit judgment resistance have persisted exceptions.'
Check (([regex]::Matches($ideology, 'rebelFaction.MarkJudgmentResistance\(\)')).Count -eq 3 -and
    $ideology.Contains('coalitionLeader != null && factionManager.IsClanPacified(coalitionLeader)')) 'Legal expulsions and treason resistance preserve their response while pacified coalition leaders cannot bypass eligibility.'
Check ($manager.Contains('if (IsCivilWarGraceBlocked(newFaction)) return;') -and
    ([regex]::Matches($faction, 'IsCivilWarGraceBlocked\(this\)')).Count -ge 4) 'Backend formation, support, discontent, ultimatum and direct secession paths are guarded.'
Check ((Read 'Behaviors/SuccessionChallengeBehavior.cs').Contains('manager?.HasCivilWarGrace(realm) == true') -and
    (Read 'Behaviors/SuccessionChallengeInitiation.cs').Contains('manager.HasCivilWarGrace(realm)') -and
    (Read 'Behaviors/ElectiveContestBehavior.cs').Contains('Post-civil-war grace protects this accession')) 'Hereditary initiation and new election disputes use the same realm restriction.'
Check ((Read 'Behaviors/SuccessionChallengeWar.cs').Contains('record.RealmBlockedUntil = record.OutcomeDay;') -and
    (Read 'Behaviors/SuccessionChallengeWar.cs').Contains('SuccessionChallengeRules.PersonalPauseYears')) 'New hereditary war outcomes use shared realm grace without removing separate personal cooldowns.'
Check ((Read 'UI/FactionsWindowVM.cs').Contains('CivilWarGraceBlock') -and
    (Read 'Behaviors/SuccessionChallengePlayer.cs').Contains('CivilWarGraceBlock') -and
    (Read 'Patches/PlayerLandedSecessionPatch.cs').Contains('CivilWarGraceBlock')) 'Player formation, challenge, ultimatum and native keep-holdings departure paths explain the restriction before acting.'
$departure = Read 'Patches/PlayerLandedSecessionPatch.cs'
Check ($departure.IndexOf('CivilWarGraceBlock(player.Kingdom)') -lt $departure.IndexOf('!WarPeaceRevampBehavior.IsRevampEnabled()') -and
    $departure.Contains('player?.IsUnderMercenaryService != true') -and $departure.Contains('player?.Fiefs.Any')) 'Native landed secession is guarded even without the revamp, without blocking mercenary or landless departures.'
foreach ($path in @('Behaviors/RealmPeaceEnforcementBehavior.cs', 'Behaviors/ClaimFeudBehavior.cs', 'Behaviors/ClaimFeudWarBehavior.cs')) {
    Check ((Read $path) -notmatch 'CivilWarGrace|PostCivilWar') "Private feud and Enforce Peace behavior remains independent: $path"
}
Check ((Read 'InternalInstabilityPresets.cs').Contains('[nameof(BellumCivileSettings.CivilWarGraceYears)] = Pick(2, 3, 1)')) 'Standard, Reduced and Increased presets use two, three and one years respectively.'
Check ((Read 'BellumCivileSaveDefiner.cs').Contains('AddClassDefinition(typeof(CivilWarGraceRecord), 136)') -and
    (Read 'BellumCivileSaveDefiner.cs').Contains('ConstructContainerDefinition(typeof(List<CivilWarGraceRecord>))')) 'The grace journal and its container have dedicated save definitions.'
Check ($manager.Contains('OnNewGameCreatedEvent.AddNonSerializedListener(this, ArmInitialCivilWarGrace)') -and
    $manager.Contains('OnAfterSessionLaunchedEvent.AddNonSerializedListener(this, ApplyInitialCivilWarGrace)') -and
    $grace.Contains('if (!_initialCivilWarGracePending) return;') -and
    $grace.Contains('BC_InitialCivilWarGraceUntil')) 'Only a new-game event arms startup pacification, with a saved nonrenewing deadline.'
Check ($grace.Contains('Kingdom.All.Where(PermanentGraceRealm)') -and
    $grace.Contains('CampaignTime.Days(Math.Max(1, CampaignTime.DaysInYear))') -and
    (Read 'BellumCivileSaveDefiner.cs').Contains('ConstructContainerDefinition(typeof(Dictionary<Kingdom, CampaignTime>))')) 'Startup protection covers permanent modded realms for one actual calendar year using an already registered save container.'
$picker = Read 'Behaviors/CourtPlayerMotionPicker.cs'
$court = Read 'Behaviors/CourtInstability.cs'
Check ($picker.Contains('CanConsiderCourtChallenge(agenda.Faction, agenda.Realm)') -and
    $picker.Contains('grace == null && Clan.PlayerClan.Influence >= price') -and
    $picker.Contains('PlayerMotionCategoryChoice(agenda, group)')) 'Otherwise eligible tyranny motions and their categories remain visible with disabled grace hints.'
Check ($court.Contains('if (faction.Leader == Clan.PlayerClan) return faction.Mood <= -60;') -and
    $court.IndexOf('HasCivilWarGrace(realm)') -lt $court.IndexOf('if (faction.Leader == Clan.PlayerClan)') -and
    !$court.Contains('if (GetAgenda(realm, faction)?.State != CourtAgendaState.Crisis')) 'Both player-led escalation and already announced court crises respect pacification.'
Check ((Read 'Behaviors/CourtAgendaPlayerFlow.cs').Contains('if (choice == 0 && grace != null)') -and
    $picker.IndexOf('var currentGrace = PlayerMotionGraceBlock(agenda, motion);') -lt $picker.IndexOf('NpcInfluenceBudgetService.TrySpend')) 'Disabled approvals and stale confirmation callbacks are guarded before committing or charging.'
Check ((Read 'Behaviors/CourtAgendaPlayerFlow.cs').Contains('a.State == CourtAgendaState.Crisis && CourtChallengeGraceBlock(a) != null') -and
    $picker.Contains('BC_CourtCrisisSetAside')) 'A previously approved unissued crisis can reopen a blocked approval popup with a free way to set it aside.'
Check ($grace.Contains('The realm is currently pacified. New rebellions against the crown are barred for {DAYS} days.') -and
    !$grace.Contains('BC_CivilWarGrace_Settling')) 'Startup, settlement and postwar commands use one standardized countdown message.'
'Source contracts passed; native campaign/save-load behavior still needs in-game testing.'
