using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    public enum CouncilAppointmentReaction
    {
        MemberAppointed,
        IncumbentRetained,
        CandidatePassedOver,
        RulerOverride,
        MemberDismissed
    }

    /// <summary>
    /// Why did I do this file?
    /// To isolate the logic for applying immediate, temporary mood shocks to political factions when major campaign events occur (e.g., executing a noble, losing a major battle, or promoting a commoner).
    /// </summary>
    public class IdeologyEventShockBehavior : CampaignBehaviorBase
    {
        private Dictionary<string, CampaignTime> _recentMaterialEvents = new Dictionary<string, CampaignTime>();

        private void RecordMaterialEvent(Kingdom realm, string kind, float shock)
        {
            if (realm != null && shock != 0)
                _recentMaterialEvents[realm.StringId + "_" + kind] = CampaignTime.Now + CampaignTime.Days(System.Math.Abs(shock));
        }

        public bool HasRecentMaterialEvent(Kingdom realm, string kind) => realm != null
            && _recentMaterialEvents.TryGetValue(realm.StringId + "_" + kind, out var expiry) && expiry.IsFuture;

        public void RecordAgitation(FactionObject faction)
        {
            if (faction?.ParentKingdom == null) return;
            ApplyMoodShock(faction.ParentKingdom, faction.Type, -20f);
            RecordMaterialEvent(faction.ParentKingdom, "Agitation_" + faction.Type, -20f);
        }
        private List<string> _recordedRoyalExecutions = new List<string>();
        private Dictionary<string, CampaignTime> _recentTribunalApproval = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _recentTribunalReprisal = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _recentFiefAwards        = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _recentFiefSnubs         = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _recentNobleExecutions   = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _recentRulerCaptured     = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _recentVillageRaids      = new Dictionary<string, CampaignTime>();
        private Dictionary<string, int> _recentVillageRaidCounts          = new Dictionary<string, int>();
        private Dictionary<string, int> _recentVillageRaidShockSteps      = new Dictionary<string, int>();
        private Dictionary<string, CampaignTime> _recentMajorVictories    = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _recentMajorDefeats      = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _recentCompanionPromotions = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _recentCandidateWon      = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _recentCandidateLost     = new Dictionary<string, CampaignTime>();
        private Dictionary<string, string> _announcedSovereigns = new Dictionary<string, string>();
        private Dictionary<string, CampaignTime> _recentUsurperAscension  = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _recentFiefHumiliation   = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _recentExpulsionAttempt  = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _recentLordImprisoned    = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _recentCouncilAppointments = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _recentCouncilRetentions = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _recentCouncilSnubs = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _recentCouncilOverrides = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _recentCouncilDismissals = new Dictionary<string, CampaignTime>();

        public override void RegisterEvents()
        {
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.VillageLooted.AddNonSerializedListener(this, OnVillageLooted);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnHeroPrisonerTaken);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.OnClanCreatedEvent.AddNonSerializedListener(this, OnClanCreated);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BC_RecentMaterialEvents", ref _recentMaterialEvents);
            _recentMaterialEvents = _recentMaterialEvents ?? new Dictionary<string, CampaignTime>();
            dataStore.SyncData("BC_RecordedRoyalExecutions", ref _recordedRoyalExecutions);
            _recordedRoyalExecutions = _recordedRoyalExecutions ?? new List<string>();
            dataStore.SyncData("BC_RecentTribunalApproval", ref _recentTribunalApproval);
            dataStore.SyncData("BC_RecentTribunalReprisal", ref _recentTribunalReprisal);
            _recentTribunalApproval = _recentTribunalApproval ?? new Dictionary<string, CampaignTime>();
            _recentTribunalReprisal = _recentTribunalReprisal ?? new Dictionary<string, CampaignTime>();
            dataStore.SyncData("BC_AnnouncedSovereigns", ref _announcedSovereigns);
            _announcedSovereigns = _announcedSovereigns ?? new Dictionary<string, string>();
            dataStore.SyncData("BellumCivile_RecentFiefAwards",    ref _recentFiefAwards);
            dataStore.SyncData("BellumCivile_RecentFiefSnubs",     ref _recentFiefSnubs);
            dataStore.SyncData("BellumCivile_RecentExecutions",    ref _recentNobleExecutions);
            dataStore.SyncData("BellumCivile_RecentRulerCaptured", ref _recentRulerCaptured);
            dataStore.SyncData("BellumCivile_RecentVillageRaids",  ref _recentVillageRaids);
            dataStore.SyncData("BellumCivile_RecentVillageRaidCounts", ref _recentVillageRaidCounts);
            dataStore.SyncData("BellumCivile_RecentVillageRaidShockSteps", ref _recentVillageRaidShockSteps);
            dataStore.SyncData("BellumCivile_RecentVictories",     ref _recentMajorVictories);
            dataStore.SyncData("BellumCivile_RecentDefeats",       ref _recentMajorDefeats);
            dataStore.SyncData("BellumCivile_RecentPromotions",    ref _recentCompanionPromotions);
            dataStore.SyncData("BellumCivile_RecentCandidateWon",  ref _recentCandidateWon);
            dataStore.SyncData("BellumCivile_RecentCandidateLost", ref _recentCandidateLost);
            dataStore.SyncData("BellumCivile_RecentUsurper",       ref _recentUsurperAscension);
            if (dataStore.IsLoading)
                _recentUsurperAscension?.Clear();
            dataStore.SyncData("BellumCivile_RecentFiefHumiliation", ref _recentFiefHumiliation);
            dataStore.SyncData("BellumCivile_RecentExpulsionAttempt", ref _recentExpulsionAttempt);
            dataStore.SyncData("BellumCivile_RecentLordImprisoned", ref _recentLordImprisoned);
            dataStore.SyncData("BellumCivile_RecentCouncilAppointments", ref _recentCouncilAppointments);
            dataStore.SyncData("BellumCivile_RecentCouncilRetentions", ref _recentCouncilRetentions);
            dataStore.SyncData("BellumCivile_RecentCouncilSnubs", ref _recentCouncilSnubs);
            dataStore.SyncData("BellumCivile_RecentCouncilOverrides", ref _recentCouncilOverrides);
            dataStore.SyncData("BellumCivile_RecentCouncilDismissals", ref _recentCouncilDismissals);
            EnsureCollectionsInitialized();
        }

        private void EnsureCollectionsInitialized()
        {
            if (_recentFiefAwards == null)         _recentFiefAwards         = new Dictionary<string, CampaignTime>();
            if (_recentFiefSnubs == null)          _recentFiefSnubs          = new Dictionary<string, CampaignTime>();
            if (_recentNobleExecutions == null)    _recentNobleExecutions    = new Dictionary<string, CampaignTime>();
            if (_recentRulerCaptured == null)      _recentRulerCaptured      = new Dictionary<string, CampaignTime>();
            if (_recentVillageRaids == null)       _recentVillageRaids       = new Dictionary<string, CampaignTime>();
            if (_recentVillageRaidCounts == null)  _recentVillageRaidCounts  = new Dictionary<string, int>();
            if (_recentVillageRaidShockSteps == null) _recentVillageRaidShockSteps = new Dictionary<string, int>();
            if (_recentMajorVictories == null)     _recentMajorVictories     = new Dictionary<string, CampaignTime>();
            if (_recentMajorDefeats == null)       _recentMajorDefeats       = new Dictionary<string, CampaignTime>();
            if (_recentCompanionPromotions == null)_recentCompanionPromotions= new Dictionary<string, CampaignTime>();
            if (_recentCandidateWon == null)       _recentCandidateWon       = new Dictionary<string, CampaignTime>();
            if (_recentCandidateLost == null)      _recentCandidateLost      = new Dictionary<string, CampaignTime>();
            if (_recentUsurperAscension == null)   _recentUsurperAscension   = new Dictionary<string, CampaignTime>();
            if (_recentFiefHumiliation == null)    _recentFiefHumiliation    = new Dictionary<string, CampaignTime>();
            if (_recentExpulsionAttempt == null)    _recentExpulsionAttempt    = new Dictionary<string, CampaignTime>();
            if (_recentLordImprisoned == null)  _recentLordImprisoned  = new Dictionary<string, CampaignTime>();
            if (_recentCouncilAppointments == null) _recentCouncilAppointments = new Dictionary<string, CampaignTime>();
            if (_recentCouncilRetentions == null) _recentCouncilRetentions = new Dictionary<string, CampaignTime>();
            if (_recentCouncilSnubs == null) _recentCouncilSnubs = new Dictionary<string, CampaignTime>();
            if (_recentCouncilOverrides == null) _recentCouncilOverrides = new Dictionary<string, CampaignTime>();
            if (_recentCouncilDismissals == null) _recentCouncilDismissals = new Dictionary<string, CampaignTime>();
        }

        public void ApplyMoodShock(Kingdom kingdom, FactionType targetFactionType, float amount)
        {
            if (kingdom == null) return;
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager == null) return;

            FactionObject faction = manager.GetFactionsInKingdom(kingdom).FirstOrDefault(f => f.Type == targetFactionType);
            if (faction != null)
            {
                faction.Mood = MathF.Clamp(faction.Mood + amount, -100f, 100f);
            }
        }

        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (BellumTreatyTransferContext.IsTreatyTransfer) return;
            if (newOwner?.Clan?.Kingdom == null) return;

            Kingdom newKingdom = newOwner.Clan.Kingdom;
            Kingdom oldKingdom = oldOwner?.Clan?.Kingdom;

            if (newKingdom != null && newKingdom == oldKingdom && newOwner != oldOwner
                && (detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByKingDecision
                    || detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByGift))
            {
                // The ballot has supporter information and owns its complete aftermath.
                if (!Patches.FiefVoteResolutionPatch.DeferAward(settlement))
                    RecordFiefAward(newKingdom, newOwner.Clan);
            }
            else if (newKingdom != oldKingdom && detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege)
            {
                ApplyMoodShock(newKingdom, FactionType.Glory, 10f);
                RecordMaterialEvent(newKingdom, "Conquest", 10f);

                if (oldKingdom != null)
                {
                    ApplyMoodShock(oldKingdom, FactionType.Glory, -10f);
                    RecordMaterialEvent(oldKingdom, "LostSettlement", -10f);
                }
            }

            if (detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByRebellion && oldKingdom != null)
            {
                ApplyMoodShock(oldKingdom, FactionType.Liberty, -10f);
                RecordMaterialEvent(oldKingdom, "SettlementRebellion", -10f);
            }
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (!mapEvent.IsFieldBattle || mapEvent.Winner == null || mapEvent.InvolvedParties.Sum(p => p.NumberOfHealthyMembers) < 300) return;

            Kingdom winningKingdom = mapEvent.Winner.MapFaction as Kingdom;
            Kingdom losingKingdom = (mapEvent.Winner == mapEvent.AttackerSide ? mapEvent.DefenderSide : mapEvent.AttackerSide).MapFaction as Kingdom;

            if (winningKingdom != null)
            {
                ApplyMoodShock(winningKingdom, FactionType.Glory, 5f);
                _recentMajorVictories[winningKingdom.StringId] = CampaignTime.Now + CampaignTime.Days(5);
            }
            if (losingKingdom != null)
            {
                ApplyMoodShock(losingKingdom, FactionType.Glory, -5f);
                _recentMajorDefeats[losingKingdom.StringId] = CampaignTime.Now + CampaignTime.Days(5);
            }
        }

        private void OnVillageLooted(Village village)
        {
            Kingdom kingdom = village.Settlement?.OwnerClan?.Kingdom;
            if (kingdom != null)
            {
                string kingdomId = kingdom.StringId;
                if (!_recentVillageRaids.TryGetValue(kingdomId, out CampaignTime raidWindow) || raidWindow.IsPast)
                {
                    _recentVillageRaidCounts[kingdomId] = 0;
                    _recentVillageRaidShockSteps[kingdomId] = 0;
                }

                int raidCount = (_recentVillageRaidCounts.TryGetValue(kingdomId, out int currentCount) ? currentCount : 0) + 1;
                _recentVillageRaidCounts[kingdomId] = raidCount;
                _recentVillageRaids[kingdomId] = CampaignTime.Now + CampaignTime.Days(C.PopRaidShockWindowDays);

                int shockStep = raidCount / C.PopRaidShockVillageThreshold;
                int previousShockStep = _recentVillageRaidShockSteps.TryGetValue(kingdomId, out int currentStep) ? currentStep : 0;
                if (shockStep > previousShockStep)
                {
                    ApplyMoodShock(kingdom, FactionType.Liberty, C.PopRaidShockAmount);
                    RecordMaterialEvent(kingdom, "VillageRaids", C.PopRaidShockAmount);
                    _recentVillageRaidShockSteps[kingdomId] = shockStep;
                }
            }
        }

        private void OnDailyTick()
        {
            foreach (var key in _recentMaterialEvents.Where(e => !e.Value.IsFuture).Select(e => e.Key).ToList())
                _recentMaterialEvents.Remove(key);
            PruneExpiredVillageRaidWindows();
        }

        private void PruneExpiredVillageRaidWindows()
        {
            if (_recentVillageRaids == null) return;

            List<string> expiredKeys = _recentVillageRaids
                .Where(x => x.Value.IsPast)
                .Select(x => x.Key)
                .ToList();

            foreach (string key in expiredKeys)
            {
                _recentVillageRaids.Remove(key);
                _recentVillageRaidCounts.Remove(key);
                _recentVillageRaidShockSteps.Remove(key);
            }
        }

        private void OnHeroPrisonerTaken(PartyBase capturer, Hero prisoner)
        {
            if (prisoner.Clan?.Kingdom == null || prisoner.Clan.IsMinorFaction) return;

            Kingdom kingdom = prisoner.Clan.Kingdom;

            if (kingdom.RulingClan?.Leader == prisoner)
            {
                ApplyMoodShock(kingdom, FactionType.Glory, -20f);
                _recentRulerCaptured[kingdom.StringId] = CampaignTime.Now + CampaignTime.Days(20);
            }
            else
            {
                ApplyMoodShock(kingdom, FactionType.Nobility, -5f);
                _recentLordImprisoned[kingdom.StringId] = CampaignTime.Now + CampaignTime.Days(5);
            }
        }

        internal void RecordTribunalAftermath(Kingdom kingdom, FactionType bloc, int amount)
        {
            if (kingdom == null || amount == 0) return;
            ApplyMoodShock(kingdom, bloc, amount);
            string key = $"{kingdom.StringId}_{bloc}";
            (amount > 0 ? _recentTribunalApproval : _recentTribunalReprisal)[key] =
                CampaignTime.Now + CampaignTime.Days(System.Math.Abs(amount));
        }

        public bool HasRecentTribunalApproval(FactionObject faction) => HasActiveFactionEntry(_recentTribunalApproval, faction);
        public bool HasRecentTribunalReprisal(FactionObject faction) => HasActiveFactionEntry(_recentTribunalReprisal, faction);

        internal void RecordFiefAward(Kingdom kingdom, Clan winner, HashSet<Clan> supporters = null)
        {
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager == null || kingdom == null || winner?.Kingdom != kingdom) return;
            FactionObject winnerFaction = manager.GetIdeologicalFaction(winner);
            foreach (var faction in manager.GetFactionsInKingdom(kingdom).Where(f => f.IsIdeology))
            {
                bool won = winnerFaction == faction;
                if (!won && winnerFaction != null && supporters?.Contains(faction.Leader) == true)
                    continue;
                string key = $"{kingdom.StringId}_{faction.Type}";
                faction.Mood = MathF.Clamp(faction.Mood + (won ? 5f : -5f), -100f, 100f);
                if (won) _recentFiefAwards[key] = CampaignTime.Now + CampaignTime.Days(5);
                else _recentFiefSnubs[key] = CampaignTime.Now + CampaignTime.Days(5);
            }
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>()?.OwnsExecutionReaction(victim) == true)
                return;
            if (victim.Clan == null || victim.Clan.IsMinorFaction || victim.Clan.Kingdom == null) return;

            if (detail == KillCharacterAction.KillCharacterActionDetail.Executed && killer?.Clan?.Kingdom != null)
            {
                if (killer.Clan == killer.Clan.Kingdom.RulingClan)
                {
                    RecordRoyalExecution(killer.Clan.Kingdom, victim);
                }
            }
        }

        internal void RecordRoyalExecution(Kingdom sentencingRealm, Hero victim)
        {
            if (sentencingRealm == null || victim == null || string.IsNullOrEmpty(victim.StringId)
                || _recordedRoyalExecutions.Contains(victim.StringId)) return;
            _recordedRoyalExecutions.Add(victim.StringId);
            ApplyMoodShock(sentencingRealm, FactionType.Nobility, -30f);
            _recentNobleExecutions[sentencingRealm.StringId] = CampaignTime.Now + CampaignTime.Days(30);
        }

        private void OnClanCreated(Clan clan, bool isCompanion)
        {
            if (isCompanion)
                OnCompanionClanCreated(clan);
        }

        private void OnCompanionClanCreated(Clan clan)
        {
            if (clan?.Kingdom != null)
            {
                ApplyMoodShock(clan.Kingdom, FactionType.Liberty, 20f);
                _recentCompanionPromotions[clan.Kingdom.StringId] = CampaignTime.Now + CampaignTime.Days(20);
            }
        }

        private bool IsCompletedLegalAccession(Kingdom kingdom, Hero sovereign)
        {
            if (kingdom == null || kingdom.IsEliminated || sovereign?.IsAlive != true
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom)
                || ElectiveSuccessionBehavior.LegalHead(kingdom.RulingClan) != sovereign) return false;
            var title = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>()?.GetKingdomPoliticalTitle(kingdom);
            return title?.DeJureHolderClanId == kingdom.RulingClan.StringId
                && title.DeFactoHolderClanId == kingdom.RulingClan.StringId;
        }

        internal bool CompleteCrownAccession(Kingdom kingdom, Hero sovereign, CrownAccessionRecord accession = null)
        {
            if (accession?.AccessionReactionsApplied == true) return true;
            if (!IsCompletedLegalAccession(kingdom, sovereign)) return false;
            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return false;
            // Journal receipts identify a reign, not a hero who might legitimately return later.
            // Direct calls are owned by an actual change of legal title holder.
            if (accession != null) accession.AccessionReactionsApplied = true;
            _announcedSovereigns[kingdom.StringId] = sovereign.StringId;
            Clan actualRulingClan = kingdom.RulingClan;

            foreach (var faction in factionManager.GetFactionsInKingdom(kingdom).Where(f => f.IsIdeology))
            {
                faction.Mood = 0f;
                string key = $"{kingdom.StringId}_{faction.Type}";
                _recentCandidateWon.Remove(key);
                _recentCandidateLost.Remove(key);
            }

            if (Clan.PlayerClan?.Kingdom == kingdom && actualRulingClan != null)
            {
                TextObject text = new TextObject("{=BC_Shock_Ascension}With the ascension of {RULER_NAME} of the {CLAN_NAME} to the throne, the realm looks forward to a long and prosperous reign.");
                text.SetTextVariable("RULER_NAME", sovereign.Name);
                text.SetTextVariable("CLAN_NAME", actualRulingClan.Name);
                BellumCivileNotifications.ShowPersonal(text, BellumNotificationColors.InheritanceWarning);
            }
            return true;
        }

        internal bool CaptureElectionEndorsements(CrownAccessionRecord accession, ElectiveSuccessionRecord ballot = null)
        {
            if (accession.ElectionEndorsementsCaptured) return true;
            var kingdom = accession.Realm;
            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return false;
            var endorsements = new Dictionary<FactionType, Clan>();
            if (accession.ElectiveElection && !accession.Emergency)
            {
                if (ballot?.Frozen != true || ballot.Realm != kingdom || ballot.Winner != accession.Heir) return false;
                foreach (var faction in factionManager.GetFactionsInKingdom(kingdom).Where(f => f.IsIdeology))
                {
                    var favored = ballot.Votes.Where(v => faction.Members.Contains(v.Clan) && v.Supported?.Clan != null)
                        .GroupBy(v => v.Supported.Clan).OrderByDescending(g => g.Sum(v => v.Weight))
                        .ThenBy(g => g.Key.StringId, System.StringComparer.Ordinal).FirstOrDefault();
                    if (favored != null) endorsements[faction.Type] = favored.Key;
                }
            }
            else if (accession.EmergencyElection != null
                && BellumCivile.Patches.KingSelectionAIPatch.ActiveElections.TryGetValue(kingdom, out var electionData)
                && electionData.SourceDecision == accession.EmergencyElection)
                endorsements = new Dictionary<FactionType, Clan>(electionData.Endorsements);
            else
                BellumCivileLogger.Log($"Election reaction snapshot unavailable; kingdom={kingdom?.StringId}; ignoring unverified native election profile.");

            accession.ElectionEndorsements = endorsements;
            accession.ElectionEndorsementsCaptured = true;
            return true;
        }

        internal bool CompleteElectionReactions(CrownAccessionRecord accession, ElectiveSuccessionRecord ballot = null)
        {
            if (accession.ElectionReactionsApplied) return true;
            if (!IsCompletedLegalAccession(accession.Realm, accession.Heir)
                || !CaptureElectionEndorsements(accession, ballot)) return false;
            var kingdom = accession.Realm;
            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return false;
            accession.ElectionReactionsApplied = true;
            var endorsements = accession.ElectionEndorsements ?? new Dictionary<FactionType, Clan>();

            foreach (var faction in factionManager.GetFactionsInKingdom(kingdom).Where(f => f.IsIdeology))
            {
                string key = $"{kingdom.StringId}_{faction.Type}";

                // A fresh election supersedes prior election history, even if the ruler is unchanged.
                _recentCandidateWon.Remove(key);
                _recentCandidateLost.Remove(key);

                if (endorsements.TryGetValue(faction.Type, out Clan endorsedClan))
                {
                    if (endorsedClan == kingdom.RulingClan)
                    {
                        ApplyMoodShock(kingdom, faction.Type, 20f);
                        _recentCandidateWon[key] = CampaignTime.Now + CampaignTime.Days(20);
                    }
                    else
                    {
                        ApplyMoodShock(kingdom, faction.Type, -30f);
                        _recentCandidateLost[key] = CampaignTime.Now + CampaignTime.Days(30);
                    }
                }
            }
            if (BellumCivile.Patches.KingSelectionAIPatch.ActiveElections.TryGetValue(kingdom, out var profile)
                && profile.SourceDecision == accession.EmergencyElection && accession.EmergencyElection != null)
                BellumCivile.Patches.KingSelectionAIPatch.ActiveElections.Remove(kingdom);
            return true;
        }

        public bool HasRecentFiefAward(FactionObject faction)
        {
            if (faction == null || faction.ParentKingdom == null) return false;
            string key = $"{faction.ParentKingdom.StringId}_{faction.Type}";
            return _recentFiefAwards.TryGetValue(key, out CampaignTime time) && !time.IsPast;
        }

        public bool HasRecentFiefSnub(FactionObject faction)
        {
            if (faction == null || faction.ParentKingdom == null) return false;
            string key = $"{faction.ParentKingdom.StringId}_{faction.Type}";
            return _recentFiefSnubs.TryGetValue(key, out CampaignTime time) && !time.IsPast;
        }

        public bool HasRecentNobleExecution(Kingdom kingdom)
        {
            if (kingdom == null) return false;
            return _recentNobleExecutions.TryGetValue(kingdom.StringId, out CampaignTime time) && !time.IsPast;
        }

        public bool HasRecentRulerCapture(Kingdom kingdom)
        {
            if (kingdom == null) return false;
            return _recentRulerCaptured.TryGetValue(kingdom.StringId, out CampaignTime time) && !time.IsPast;
        }

        public bool HasRecentVillageRaid(Kingdom kingdom)
        {
            return HasRecentMaterialEvent(kingdom, "VillageRaids");
        }

        public bool HasRecentMajorVictory(Kingdom kingdom)
        {
            if (kingdom == null) return false;
            return _recentMajorVictories.TryGetValue(kingdom.StringId, out CampaignTime time) && !time.IsPast;
        }

        public bool HasRecentMajorDefeat(Kingdom kingdom)
        {
            if (kingdom == null) return false;
            return _recentMajorDefeats.TryGetValue(kingdom.StringId, out CampaignTime time) && !time.IsPast;
        }

        public bool HasRecentCompanionPromotion(Kingdom kingdom)
        {
            if (kingdom == null) return false;
            return _recentCompanionPromotions.TryGetValue(kingdom.StringId, out CampaignTime time) && !time.IsPast;
        }

        public bool HasRecentCandidateWon(FactionObject faction)
        {
            if (faction == null || faction.ParentKingdom == null) return false;
            return _recentCandidateWon.TryGetValue($"{faction.ParentKingdom.StringId}_{faction.Type}", out CampaignTime time) && !time.IsPast;
        }

        public bool HasRecentCandidateLost(FactionObject faction)
        {
            if (faction == null || faction.ParentKingdom == null) return false;
            return _recentCandidateLost.TryGetValue($"{faction.ParentKingdom.StringId}_{faction.Type}", out CampaignTime time) && !time.IsPast;
        }

        public bool HasRecentUsurperAscension(FactionObject faction)
        {
            if (faction == null || faction.ParentKingdom == null) return false;
            return _recentUsurperAscension.TryGetValue($"{faction.ParentKingdom.StringId}_{faction.Type}", out CampaignTime time) && !time.IsPast;
        }

        public void RecordFiefHumiliation(FactionObject faction, int durationDays)
        {
            if (faction == null || faction.ParentKingdom == null) return;
            string key = $"{faction.ParentKingdom.StringId}_{faction.Type}";
            _recentFiefHumiliation[key] = CampaignTime.Now + CampaignTime.Days(durationDays);
        }

        public bool HasRecentFiefHumiliation(FactionObject faction)
        {
            if (faction == null || faction.ParentKingdom == null) return false;
            string key = $"{faction.ParentKingdom.StringId}_{faction.Type}";
            return _recentFiefHumiliation.TryGetValue(key, out CampaignTime time) && !time.IsPast;
        }

        public bool HasRecentLordImprisoned(Kingdom kingdom)
        {
            if (kingdom == null) return false;
            return _recentLordImprisoned.TryGetValue(kingdom.StringId, out CampaignTime time) && !time.IsPast;
        }

        public void RecordExpulsionAttempt(FactionObject faction, int durationDays)
        {
            if (faction == null || faction.ParentKingdom == null) return;
            string key = $"{faction.ParentKingdom.StringId}_{faction.Type}";
            _recentExpulsionAttempt[key] = CampaignTime.Now + CampaignTime.Days(durationDays);
        }

        public bool HasRecentExpulsionAttempt(FactionObject faction)
        {
            if (faction == null || faction.ParentKingdom == null) return false;
            string key = $"{faction.ParentKingdom.StringId}_{faction.Type}";
            return _recentExpulsionAttempt.TryGetValue(key, out CampaignTime time) && !time.IsPast;
        }

        public void RecordCouncilAppointmentReaction(
            FactionObject faction,
            CouncilAppointmentReaction reaction,
            float moodShock)
        {
            if (faction?.ParentKingdom == null || moodShock == 0f)
                return;

            string key = $"{faction.ParentKingdom.StringId}_{faction.Type}";
            CampaignTime expiry = CampaignTime.Now + CampaignTime.Days(System.Math.Abs(moodShock));
            switch (reaction)
            {
                case CouncilAppointmentReaction.MemberAppointed:
                    _recentCouncilAppointments[key] = expiry;
                    break;
                case CouncilAppointmentReaction.IncumbentRetained:
                    _recentCouncilRetentions[key] = expiry;
                    break;
                case CouncilAppointmentReaction.CandidatePassedOver:
                    _recentCouncilSnubs[key] = expiry;
                    break;
                case CouncilAppointmentReaction.RulerOverride:
                    _recentCouncilOverrides[key] = expiry;
                    break;
                case CouncilAppointmentReaction.MemberDismissed:
                    _recentCouncilDismissals[key] = expiry;
                    break;
            }

            ApplyMoodShock(faction.ParentKingdom, faction.Type, moodShock);
        }

        public bool HasRecentCouncilAppointment(FactionObject faction)
        {
            return HasActiveFactionEntry(_recentCouncilAppointments, faction);
        }

        public bool HasRecentCouncilRetention(FactionObject faction)
        {
            return HasActiveFactionEntry(_recentCouncilRetentions, faction);
        }

        public bool HasRecentCouncilSnub(FactionObject faction)
        {
            return HasActiveFactionEntry(_recentCouncilSnubs, faction);
        }

        public bool HasRecentCouncilOverride(FactionObject faction)
        {
            return HasActiveFactionEntry(_recentCouncilOverrides, faction);
        }

        public bool HasRecentCouncilDismissal(FactionObject faction)
        {
            return HasActiveFactionEntry(_recentCouncilDismissals, faction);
        }

        private static bool HasActiveFactionEntry(
            IReadOnlyDictionary<string, CampaignTime> entries,
            FactionObject faction)
        {
            if (entries == null || faction?.ParentKingdom == null)
                return false;

            string key = $"{faction.ParentKingdom.StringId}_{faction.Type}";
            return entries.TryGetValue(key, out CampaignTime expiry) && !expiry.IsPast;
        }
    }
}
