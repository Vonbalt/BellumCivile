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
'Source contracts passed; native campaign/save-load behavior still needs in-game testing.'
