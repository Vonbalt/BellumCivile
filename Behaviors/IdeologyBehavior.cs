using System.Collections.Generic;
using System.Linq;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using BellumCivile.Patches;
using C = BellumCivile.BellumCivileConstants;
using O = BellumCivile.BellumCivileOptions;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Why did I do this file?
    /// This is the core tick engine for the permanent political parties. It drives the sorting of lords into ideologies, calculates the target baseline mood, applies daily drift, and evaluates triggers for armed rebellions, massive map events, and cascading defections.
    /// </summary>
    public class IdeologyBehavior : CampaignBehaviorBase
    {
        internal static bool IsModAddingDecision = false;

        public static void AddDecisionAsModAction(Kingdom kingdom, KingdomDecision decision, bool ignoreInfluenceCost = true)
        {
            if (kingdom == null || decision == null)
                return;

            if (BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom))
            {
                BellumCivileLogger.Log($"Blocked Bellum political decision in temporary realm; kingdom={kingdom.StringId}; decision={decision.GetType().Name}.");
                return;
            }

            IsModAddingDecision = true;
            try { kingdom.AddDecision(decision, ignoreInfluenceCost); }
            finally { IsModAddingDecision = false; }
        }
        private const int CurrentSaveVersion = 2;
        private int _saveVersion = 0;

        #region Treason & Expulsion Data
        private Dictionary<string, int> _rejectedExpulsionRelations = new Dictionary<string, int>();
        private Dictionary<string, CampaignTime> _expulsionOutcomeLocks = new Dictionary<string, CampaignTime>();
        #endregion

        private bool _isInSessionLaunch = false;

        private Dictionary<string, int> _kingdomPeaceDays = new Dictionary<string, int>();
        private Dictionary<string, int> _kingdomWarDays = new Dictionary<string, int>();
        private CampaignTime _nextNeutralityPenaltyDate = CampaignTime.Zero;

        private Dictionary<string, CampaignTime> _rebelFactionRejoinCooldowns = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _rebellionReviewDates = new Dictionary<string, CampaignTime>();
        private Action _pendingPlayerTreasonJudgmentAction;
        private static readonly FactionType[] RebelCausePriorityOrder =
        {
            FactionType.InstallRuler,
            FactionType.Independence,
            FactionType.Abdication
        };


        public override void RegisterEvents()
        {
            // Position scores require the title registry's session initialization to finish first.
            CampaignEvents.OnAfterSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_SaveVersion", ref _saveVersion);

            #region Treason & Expulsion Sync
            dataStore.SyncData("BellumCivile_RejectedExpulsionRelations", ref _rejectedExpulsionRelations);
            dataStore.SyncData("BellumCivile_ExpulsionOutcomeLocks", ref _expulsionOutcomeLocks);
            #endregion

            dataStore.SyncData("BellumCivile_PeaceDays", ref _kingdomPeaceDays);
            dataStore.SyncData("BellumCivile_WarDays", ref _kingdomWarDays);
            dataStore.SyncData("BellumCivile_NeutralityDate", ref _nextNeutralityPenaltyDate);
            dataStore.SyncData("BellumCivile_RebelFactionRejoinCooldowns", ref _rebelFactionRejoinCooldowns);
            dataStore.SyncData("BellumCivile_RebellionReviewDates", ref _rebellionReviewDates);

            EnsureCollectionsInitialized();
            CheckSaveMigration();
        }

        private void EnsureCollectionsInitialized()
        {
            if (_rejectedExpulsionRelations == null)      _rejectedExpulsionRelations      = new Dictionary<string, int>();
            if (_expulsionOutcomeLocks == null)          _expulsionOutcomeLocks          = new Dictionary<string, CampaignTime>();
            if (_kingdomPeaceDays == null)                _kingdomPeaceDays                = new Dictionary<string, int>();
            if (_kingdomWarDays == null)                  _kingdomWarDays                  = new Dictionary<string, int>();
            if (_rebelFactionRejoinCooldowns == null)      _rebelFactionRejoinCooldowns      = new Dictionary<string, CampaignTime>();
            if (_rebellionReviewDates == null)              _rebellionReviewDates              = new Dictionary<string, CampaignTime>();
        }

        private void CheckSaveMigration()
        {
            if (_saveVersion >= CurrentSaveVersion) return;

            BellumCivileLogger.Log($"IdeologyBehavior: migrating save from v{_saveVersion} to v{CurrentSaveVersion}.");
            _saveVersion = CurrentSaveVersion;
        }

        public void ApplyAcceptedUltimatumPacification(Kingdom kingdom, IEnumerable<Clan> clans, int days)
        {
            if (kingdom == null || clans == null || days <= 0) return;

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            foreach (Clan clan in clans.Where(c => c != null && !c.IsEliminated && !c.IsMinorFaction && !c.IsUnderMercenaryService))
            {
                factionManager?.ApplyPacifiedCooldown(clan, days);
            }
        }

        private static string GetExpulsionOutcomeKey(Kingdom kingdom, Clan targetClan)
        {
            return kingdom?.StringId + "|" + targetClan?.StringId;
        }


        private bool HasActiveCrownRebellion(Kingdom kingdom, FactionManagerBehavior factionManager)
        {
            return kingdom != null
                && factionManager?.GetFactionsInKingdom(kingdom).Any(faction =>
                    faction != null
                    && !faction.IsIdeology
                    && (faction.IsGrandCoalition
                        || faction.IsCivilWarActive()
                        || faction.HasTrackedRebelKingdom)) == true;
        }

        private bool IsRejectedExpulsionLocked(Kingdom kingdom, Clan targetClan, int currentRelation, bool readOnly = false)
        {
            string key = GetExpulsionOutcomeKey(kingdom, targetClan);
            if (string.IsNullOrEmpty(key)
                || !_rejectedExpulsionRelations.TryGetValue(key, out int rejectedRelation))
            {
                return false;
            }

            bool reachedDecreeThresholdAfterRejection =
                currentRelation <= C.TreasonDecreeRelationThreshold
                && rejectedRelation > C.TreasonDecreeRelationThreshold;
            if (currentRelation > C.TreasonInductRelationThreshold
                || reachedDecreeThresholdAfterRejection
                || currentRelation <= rejectedRelation - C.TreasonRejectedRelationDrop)
            {
                if (!readOnly) _rejectedExpulsionRelations.Remove(key);
                return false;
            }

            return true;
        }

        public void RecordExpulsionVoteOutcome(Kingdom kingdom, Clan targetClan, bool expelled)
        {
            string key = GetExpulsionOutcomeKey(kingdom, targetClan);
            if (string.IsNullOrEmpty(key))
                return;

            if (expelled || targetClan?.Leader == null || kingdom?.RulingClan?.Leader == null)
            {
                _rejectedExpulsionRelations.Remove(key);
                return;
            }

            _rejectedExpulsionRelations[key] = targetClan.Leader.GetRelation(kingdom.RulingClan.Leader);
        }

        public bool CanRulerIndictClan(
            Kingdom kingdom,
            Clan targetClan,
            bool highTreason,
            out TextObject explanation, bool readOnly = false)
        {
            explanation = new TextObject("");
            Clan rulingClan = kingdom?.RulingClan;
            Hero ruler = rulingClan?.Leader;

            if (kingdom == null || rulingClan == null || ruler == null || ruler.IsDead)
            {
                explanation = new TextObject("{=BC_Treason_InvalidRuler}The realm has no ruler able to pass judgment.");
                return false;
            }

            if (targetClan == null || targetClan == rulingClan || targetClan.Kingdom != kingdom
                || targetClan.IsEliminated || targetClan.Leader == null || targetClan.Leader.IsDead
                || targetClan.IsUnderMercenaryService || (targetClan.IsMinorFaction && targetClan != Clan.PlayerClan))
            {
                explanation = new TextObject("{=BC_Treason_InvalidTarget}This clan cannot be indicted for treason.");
                return false;
            }

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (HasActiveCrownRebellion(kingdom, factionManager))
            {
                explanation = new TextObject("{=BC_Treason_ActiveCivilWar}The realm is already divided by open rebellion. Questions of loyalty must await its outcome.");
                return false;
            }

            ExpulsionDeliberationBehavior deliberation = Campaign.Current?.GetCampaignBehavior<ExpulsionDeliberationBehavior>();
            if (deliberation?.HasPendingExpulsion(kingdom) == true
                || kingdom.UnresolvedDecisions.OfType<ExpelClanFromKingdomDecision>().Any())
            {
                explanation = new TextObject("{=BC_UI_ExpelDeliberating}A judgment is already being deliberated. Await its conclusion before calling another.");
                return false;
            }

            int relation = targetClan.Leader.GetRelation(ruler);
            int requiredRelation = highTreason
                ? C.HighTreasonInductRelationThreshold
                : C.TreasonInductRelationThreshold;
            if (relation > requiredRelation)
            {
                explanation = new TextObject("{=BC_Treason_RelationRequirement}This charge requires relations with the accused to be {REQUIRED_RELATION} or lower. Current relation: {CURRENT_RELATION}.");
                explanation.SetTextVariable("REQUIRED_RELATION", requiredRelation);
                explanation.SetTextVariable("CURRENT_RELATION", relation);
                return false;
            }

            if (!highTreason && IsRejectedExpulsionLocked(kingdom, targetClan, relation, readOnly))
            {
                int rejectedRelation = _rejectedExpulsionRelations[GetExpulsionOutcomeKey(kingdom, targetClan)];
                if (rejectedRelation <= C.TreasonDecreeRelationThreshold)
                {
                    explanation = new TextObject("{=BC_Treason_CourtRejectedAtFloor}The court has rejected this accusation at the lowest possible relation. Only a charge of high treason may now bypass its judgment.");
                    return false;
                }

                int renewedThreshold = Math.Max(C.TreasonDecreeRelationThreshold, rejectedRelation - C.TreasonRejectedRelationDrop);
                explanation = new TextObject("{=BC_Treason_CourtRejected}The court has already rejected this accusation. Relations must deteriorate to {REQUIRED_RELATION} or recover above {RESET_RELATION} before a new charge can be brought. Current relation: {CURRENT_RELATION}.");
                explanation.SetTextVariable("REQUIRED_RELATION", renewedThreshold);
                explanation.SetTextVariable("RESET_RELATION", C.TreasonInductRelationThreshold);
                explanation.SetTextVariable("CURRENT_RELATION", relation);
                return false;
            }

            return true;
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            BellumCivile.Patches.PolicyVoteAIPatch.ResetReflectionCache();
            BellumCivile.Patches.ExpulsionVoteAIPatch.ResetReflectionCache();
            BellumCivile.Patches.FiefVoteAIPatch.ResetReflectionCache();
            BellumCivile.Patches.KingSelectionAIPatch.ResetReflectionCache();

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return;

            _isInSessionLaunch = true;
            try
            {
                foreach (Kingdom kingdom in Kingdom.All)
                {
                    if (kingdom.IsEliminated || kingdom.RulingClan == null || kingdom.RulingClan.Leader == null) continue;
                    if (BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom)) continue;
                    if (factionManager.GetFactionByRebelKingdom(kingdom) != null) continue;

                    ProcessKingdomIdeologies(kingdom, factionManager);
                }
            }
            finally
            {
                _isInSessionLaunch = false;
            }
        }

        private void OnDailyTick()
        {
            EnsureCollectionsInitialized();
            PruneExpiredRebelFactionRejoinCooldowns();

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return;

            foreach (string expiredKey in _expulsionOutcomeLocks
                .Where(kv => kv.Value.IsPast)
                .Select(kv => kv.Key)
                .ToList())
            {
                _expulsionOutcomeLocks.Remove(expiredKey);
            }

            factionManager.RestorePlayerCourtAffiliation();
            if (_nextNeutralityPenaltyDate == CampaignTime.Zero || _nextNeutralityPenaltyDate.IsPast)
            {
                ApplyPlayerNeutralityPenalty(factionManager);
            }

            foreach (Kingdom kingdom in Kingdom.All.ToList())
            {
                if (kingdom.IsEliminated || kingdom.RulingClan == null || kingdom.RulingClan.Leader == null) continue;
                if (BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom)) continue;

                bool isAtWar = Kingdom.All.Any(k => k != kingdom && !k.IsEliminated && kingdom.IsAtWarWith(k) && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(k));
                int currentWarDays = 0;
                int currentPeaceDays = 0;
                _kingdomWarDays.TryGetValue(kingdom.StringId, out currentWarDays);
                _kingdomPeaceDays.TryGetValue(kingdom.StringId, out currentPeaceDays);
                if (isAtWar)
                {
                    _kingdomWarDays[kingdom.StringId] = currentWarDays + 1;
                    _kingdomPeaceDays[kingdom.StringId] = 0;
                }
                else
                {
                    _kingdomPeaceDays[kingdom.StringId] = currentPeaceDays + 1;
                    _kingdomWarDays[kingdom.StringId] = 0;
                }

                bool isRebelKingdom = factionManager.GetFactionByRebelKingdom(kingdom) != null;
                if (!isRebelKingdom)
                {
                    OnDailyTick_Consolidated(kingdom, factionManager);

                    ProcessKingdomIdeologies(kingdom, factionManager);
                }
            }
        }

        private void ProcessKingdomIdeologies(Kingdom kingdom, FactionManagerBehavior factionManager, bool advanceMood = true)
        {
            if (!CanRunCourtPolitics(kingdom))
            {
                return;
            }

            if (kingdom == Clan.PlayerClan?.Kingdom)
                factionManager.RestorePlayerCourtAffiliation();
            List<FactionObject> kingdomFactions = factionManager.GetFactionsInKingdom(kingdom);
            List<Clan> eligibleClans = kingdom.Clans.Where(c => CourtMembershipEligibility.CanBelong(c, kingdom) && c != Clan.PlayerClan).ToList();

            CourtPoliticalPositionContext politicalContext = null;
            try
            {
                foreach (Clan clan in eligibleClans)
                {
                    if (factionManager.GetIdeologicalFaction(clan) == null)
                    {
                        // A ruler formally renounced by a court faction must not be silently
                        // assigned back while the resulting grand coalition is still active.
                        if (clan == kingdom.RulingClan && kingdomFactions.Any(f => f.IsGrandCoalition))
                            continue;

                        politicalContext = politicalContext ?? new CourtPoliticalPositionContext(kingdom, affiliationOnly: true);
                        FactionType preferredType = DeterminePreferredIdeology(clan, factionManager, politicalContext);
                        FactionObject targetFaction = kingdomFactions.FirstOrDefault(f => f.Type == preferredType);

                        if (targetFaction != null) targetFaction.AddMember(clan);
                        else
                        {
                            string factionName = GetIdeologyName(preferredType, kingdom);
                            targetFaction = new FactionObject(factionName, kingdom, clan, preferredType);
                            factionManager.RegisterNewFaction(targetFaction);
                            kingdomFactions.Add(targetFaction);
                        }
                    }
                }
            }
            finally
            {
                politicalContext?.Dispose();
            }

            if (!advanceMood) return;
            foreach (FactionObject faction in kingdomFactions.Where(f => f.IsIdeology))
            {
                if (faction.Members.Count == 0 || _isInSessionLaunch) continue;

                float baseThreshold = CalculateTargetBaseline(faction, kingdom, factionManager);

                if (faction.UnderlyingMood < baseThreshold) faction.UnderlyingMood = MathF.Min(faction.UnderlyingMood + 1f, baseThreshold);
                else if (faction.UnderlyingMood > baseThreshold) faction.UnderlyingMood = MathF.Max(faction.UnderlyingMood - 1f, baseThreshold);

                faction.UnderlyingMood = MathF.Clamp(faction.UnderlyingMood, -100f, 100f);



                 if (faction.Mood >= 100f)
                {
                    PacifyIdeologyMembers(faction, kingdom, factionManager, false);
                }

            }
        }

        internal bool CanSelectCourtChallenge(FactionObject faction, Kingdom kingdom)
        {
            if (!CanRunCourtPolitics(kingdom) || faction == null || faction.ParentKingdom != kingdom
                || !faction.IsIdeology || faction.Mood > -60 || CourtAgendaBehavior.MemberCount(faction) < 2
                || faction.Leader == kingdom.RulingClan || !CourtAgendaBehavior.Eligible(faction.Leader, kingdom)) return false;
            return Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionsInKingdom(kingdom)
                .Any(f => !f.IsIdeology && f.IsGrandCoalition
                    && (f.IsUltimatumPending || f.IsCivilWarActive() || f.HasTrackedRebelKingdom)) == false;
        }

        internal bool TryLaunchCourtChallenge(FactionObject faction, Kingdom kingdom)
        {
            if (!CanRunCourtPolitics(kingdom) || CourtAgendaBehavior.MemberCount(faction) < 2) return false;
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            return faction.Members.Contains(kingdom.RulingClan)
                ? TryExpelKingForRebellion(faction, kingdom, manager)
                : TriggerGrandCoalition(faction, kingdom, manager, showFailureMessage: false);
        }

        internal void BeginCourtTerm(Kingdom kingdom)
        {
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            var factions = manager.GetFactionsInKingdom(kingdom).Where(f => f.IsIdeology).ToList();
            var reviewed = new HashSet<Clan>();
            foreach (var faction in factions)
                ReevaluateIdeologyMembership(faction, kingdom, manager, kingdom == Clan.PlayerClan?.Kingdom, reviewed);
            foreach (var faction in manager.GetFactionsInKingdom(kingdom).Where(f => f.IsIdeology).ToList())
            {
                Clan leader = GetRankedCourtFactionLeadershipCandidates(faction, kingdom, false).FirstOrDefault();
                if (leader != null && leader != faction.Leader)
                {
                    if (leader == Clan.PlayerClan) ShowPlayerCourtFactionLeadershipInquiry(faction, kingdom);
                    else ApplyCourtFactionLeadershipChange(faction, kingdom, leader);
                }
            }
        }


        internal static bool CanRunCourtPolitics(Kingdom kingdom)
        {
            return kingdom != null
                && !kingdom.IsEliminated
                && kingdom.RulingClan?.Leader != null
                && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom);
        }


        public void RefreshKingdomIdeologies(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated || kingdom.RulingClan == null || kingdom.RulingClan.Leader == null)
                return;

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null || factionManager.GetFactionByRebelKingdom(kingdom) != null)
                return;

            factionManager.DetachForeignIdeologiesFromKingdom(kingdom);
            ProcessKingdomIdeologies(kingdom, factionManager);
        }

        internal void RefreshPartitionCourtMembership(Kingdom kingdom)
        {
            var manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (!CanRunCourtPolitics(kingdom) || manager == null)
                throw new InvalidOperationException("Partition court membership service is unavailable.");
            manager.DetachForeignIdeologiesFromKingdom(kingdom);
            ProcessKingdomIdeologies(kingdom, manager, advanceMood: false);
        }

        private FactionType? GetRivalFaction(FactionType type)
        {
            switch (type)
            {
                case FactionType.Royalists: return FactionType.Nobility;
                case FactionType.Nobility: return FactionType.Royalists;
                case FactionType.Liberty: return FactionType.Glory;
                case FactionType.Glory: return FactionType.Liberty;
                default: return null;
            }
        }

        // Calculates the static target baseline mood for a political party based on active policies, prolonged war, and their three specific geopolitical pillars (e.g., Militarists evaluating realm strength).
        private float CalculateTargetBaseline(FactionObject faction, Kingdom kingdom, FactionManagerBehavior factionManager)
        {
            return MathF.Clamp(GetMoodConditions(faction, kingdom, factionManager).Sum(c => c.Value), -100f, 100f);
        }

        public (float, string) GetTargetBaselineBreakdown(FactionObject faction, Kingdom kingdom, FactionManagerBehavior factionManager)
        {
            var conditions = GetMoodConditions(faction, kingdom, factionManager);
            return (MathF.Clamp(conditions.Sum(c => c.Value), -100f, 100f),
                string.Join("\n", conditions.Where(c => c.Value != 0).Select(c => c.Label + ": " + c.Value.ToString("+0;-0;0"))));
        }

        public sealed class MoodCondition
        {
            public string Id;
            public string Label;
            public float Value;
            public bool Positive;
            public string Rule;
        }

        public List<MoodCondition> GetMoodConditions(FactionObject faction, Kingdom kingdom, FactionManagerBehavior manager)
        {
            var result = new List<MoodCondition>();
            if (faction == null || kingdom == null || !faction.IsIdeology) return result;
            void Add(string id, string label, float value, bool positive, string rule)
            {
                result.Add(new MoodCondition { Id = id, Label = CourtMoodPresentation.Name(id, faction.Type, new TextObject("{=" + id + "}" + label).ToString()),
                    Value = value, Positive = positive, Rule = new TextObject("{=" + id + "_Rule}" + rule).ToString() });
            }
            int support = 0, opposition = 0, centralization = 0;
            foreach (var policy in kingdom.ActivePolicies)
            {
                CourtPolicyStance stance = IdeologyPolicyRoster.GetStance(faction.Type, policy);
                if (stance == CourtPolicyStance.Support) support += 10;
                if (stance == CourtPolicyStance.Oppose) opposition -= 10;
                if (IdeologyPolicyRoster.IsCrownPolicy(policy)) centralization -= 5;
            }
            Add("BC_CourtSupportedPolicies", "Supported Policies", support, true, "+10 baseline per supported policy in effect.");
            Add("BC_CourtOpposedPolicies", "Opposed Policies", opposition, false, "-10 baseline per opposed policy in effect. Unlisted policies are neutral.");
            Add("BC_CourtCentralization", "Centralized Authority", centralization, false, "-5 baseline per Crown-centralizing policy in effect, in addition to ideological policy preferences.");

            _kingdomPeaceDays.TryGetValue(kingdom.StringId, out int peace);
            _kingdomWarDays.TryGetValue(kingdom.StringId, out int war);
            float quarter = CampaignTime.DaysInYear / 4f;
            if (faction.Type == FactionType.Glory || faction.Type == FactionType.Liberty)
            {
                bool glory = faction.Type == FactionType.Glory;
                Add(glory ? "BC_CourtGloryWar" : "BC_CourtLibertyPeace", glory ? "Foreign War" : "Peace",
                    (glory ? war : peace) >= quarter ? 10 : 0, true, "+10 after a quarter-year in this condition. Temporary rebel and feud realms do not count as foreign wars.");
                Add(glory ? "BC_CourtGloryPeace" : "BC_CourtLibertyWar", glory ? "Prolonged Peace" : "Prolonged Foreign War",
                    (glory ? peace : war) >= quarter ? -10 : 0, false, "-10 after a quarter-year in this condition.");
            }
            if (faction.Type == FactionType.Glory)
            {
                int tribute = GetNetTribute(kingdom);
                Add("BC_CourtTributeReceived", "Receiving Tribute", tribute > 0 ? 10 : 0, true, "+10 when net tribute is positive.");
                Add("BC_CourtTributePaid", "Paying Tribute", tribute < 0 ? -10 : 0, false, "-10 when net tribute is negative.");
            }
            if (faction.Type == FactionType.Liberty)
            {
                bool trade = HasAnyTradeAgreement(kingdom);
                Add("BC_CourtTrade", "Trade Agreements", trade ? 10 : 0, true, "+10 with an active trade agreement.");
                Add("BC_CourtNoTrade", "No Trade Agreements", trade ? 0 : -10, false, "-10 without an active trade agreement.");
                var towns = kingdom.Fiefs.Where(f => f.IsTown).ToList();
                bool hunger = towns.Count > 0 && towns.Count(t => t.FoodStocks <= 0) * 4 >= towns.Count;
                Add("BC_CourtProsperity", "Prosperous Towns", towns.Count > 0 && !hunger && towns.Average(t => t.Prosperity) > 5000 ? 10 : 0,
                    true, "+10 when average town prosperity exceeds 5000 and fewer than a quarter of towns have exhausted their food stocks.");
                Add("BC_CourtHunger", "Widespread Hunger", hunger ? -15 : 0, false, "-15 when at least a quarter of towns have exhausted their food stocks.");
            }
            if (faction.Type == FactionType.Nobility)
            {
                bool alliance = HasAnyAlliance(kingdom);
                Add("BC_CourtAlliances", "Foreign Alliances", alliance ? 10 : 0, true, "+10 with a foreign alliance.");
                Add("BC_CourtNoAlliances", "No Foreign Alliances", alliance ? 0 : -10, false, "-10 without a foreign alliance.");
                bool internalWar = manager.GetFactionsInKingdom(kingdom).Any(f => !f.IsIdeology && f.IsCivilWarActive())
                    || Kingdom.All.Any(k => k != kingdom && !k.IsEliminated && BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(k) && kingdom.IsAtWarWith(k));
                Add("BC_CourtInternalPeace", "Internal Peace", internalWar ? 0 : 10, true, "+10 without an active internal war. Mere conspiracies do not count.");
                Add("BC_CourtInternalWar", "Internal War", internalWar ? -15 : 0, false, "-15 during a civil or private feud war.");
                var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
                int assessed = 0, mismatch = 0;
                if (titles != null)
                    foreach (var fief in kingdom.Fiefs)
                        if (titles.TryGetBarony(fief.Settlement, out var barony) && barony != null && fief.OwnerClan != null)
                        {
                            assessed++;
                            if (barony.DeJureHolderClanId != fief.OwnerClan.StringId) mismatch++;
                        }
                Add("BC_CourtLawfulEstates", "Lawful Landholding", assessed > 0 && mismatch == 0 ? 10 : 0, true, "+10 when every assessed barony is held by its legal owner.");
                Add("BC_CourtUnlawfulEstates", "Unlawful Landholding", assessed > 0 && mismatch * 4 >= assessed ? -10 : 0, false, "-10 when at least a quarter of assessed baronies are not held by their legal owners.");
            }
            if (faction.Type != FactionType.Glory)
            {
                var holdings = kingdom.Clans.Where(c => c != kingdom.RulingClan && CourtAgendaBehavior.Eligible(c, kingdom))
                    .Select(c => c.Fiefs.Count).OrderByDescending(n => n).ToList();
                int total = holdings.Sum(), topCount = (holdings.Count + 2) / 3;
                bool broad = false, concentrated = false;
                if (holdings.Count >= 3 && total > 0)
                {
                    // Compare cross-products; equality belongs to the stated thresholds.
                    int concentration = CourtAgendaRules.LandConcentration(holdings.Count, total, holdings.Take(topCount).Sum());
                    broad = concentration < 0;
                    concentrated = concentration > 0;
                }
                bool nobility = faction.Type == FactionType.Nobility;
                Add("BC_CourtConcentratedLand", "Concentrated Landownership", concentrated ? (nobility ? 10 : -10) : 0, nobility,
                    "The largest third of non-ruling noble houses holds at least 1.8 times its proportionate share of fiefs. Requires at least three houses.");
                Add("BC_CourtBroadLand", "Broad Landownership", broad ? (nobility ? -10 : 10) : 0, !nobility,
                    "The largest third of non-ruling noble houses holds no more than 1.2 times its proportionate share of fiefs. Landless houses count; the ruler does not.");
            }
            var council = Campaign.Current.GetCampaignBehavior<PrivyCouncilBehavior>();
            int own = 0, others = 0;
            if (council != null)
                foreach (var office in council.GetOfficeRecords(kingdom))
                {
                    if (!council.IsOfficeUnlocked(kingdom, office.Office)) continue;
                    Clan holder = council.GetOfficeHolder(kingdom, office.Office);
                    if (!CourtAgendaBehavior.Eligible(holder, kingdom)) continue;
                    var bloc = manager.GetIdeologicalFaction(holder);
                    if (bloc == null) continue;
                    if (bloc == faction) own += 5; else others -= 5;
                }
            Add("BC_CourtOwnSeats", "Council Representation", own, true, "+5 per occupied council office held by a member of this faction.");
            Add("BC_CourtOtherSeats", "Competing Council Representation", others, false, "-5 per occupied council office held by another court faction. These penalties stack.");
            Add("BC_CourtAdvisorRepresentation", "Advisor Representation", council?.GetRepresentedFactionMoodBaseline(faction) ?? 0, true,
                "Additional baseline support from advisors assigned to represent this faction.");
            int crownFavor = CourtAgendaBehavior.Current?.GetFavorMood(faction) ?? 0;
            Add("BC_CourtCrownFavor", "Favored by the Crown", Math.Max(0, crownFavor), true, "The Crown favors this faction for the current court term.");
            Add("BC_CourtCrownFavorsOthers", "Crown Favors Another Faction", Math.Min(0, crownFavor), false, "The Crown favors another faction for the current court term.");
            return result;
        }

        private static bool DoGreatHousesControlMajority(Kingdom kingdom)
        {
            if (kingdom?.Fiefs == null || kingdom.Fiefs.Count == 0)
                return false;

            int greatHouseFiefs = kingdom.Fiefs.Count(f =>
            {
                Clan owner = f?.OwnerClan;
                FeudalTitleType? highestTitle = FeudalPoliticalWeightHelper.GetHighestHeldTitleRank(owner);
                return highestTitle.HasValue && highestTitle.Value >= FeudalTitleType.County;
            });

            return greatHouseFiefs > kingdom.Fiefs.Count / 2;
        }

        private static int CountUnlawfulUpstartFiefs(Kingdom kingdom)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (kingdom?.Fiefs == null || titleBehavior == null)
                return 0;

            CultureObject kingdomCulture = kingdom.Culture;
            int count = 0;
            foreach (Town fief in kingdom.Fiefs)
            {
                Clan owner = fief?.OwnerClan;
                if (owner == null || fief.Settlement?.Culture != kingdomCulture)
                    continue;

                FeudalTitleType? ownerHighestTitle = FeudalPoliticalWeightHelper.GetHighestHeldTitleRank(owner);
                if (ownerHighestTitle.HasValue && ownerHighestTitle.Value > FeudalTitleType.Barony)
                    continue;

                if (!titleBehavior.TryGetBarony(fief.Settlement, out FeudalTitleRecord barony) || barony == null)
                    continue;

                if (barony.DeFactoHolderClanId == owner.StringId
                    && barony.DeJureHolderClanId != owner.StringId)
                {
                    count++;
                }
            }

            return count;
        }




        internal static int CalculateRebellionSuppressionCost(float mood)
        {
            float clampedMood = MathF.Clamp(mood, C.CourtRebellionSuppressMoodMax, C.CourtRebellionSuppressMoodMin);
            float progress = (C.CourtRebellionSuppressMoodMin - clampedMood) / (C.CourtRebellionSuppressMoodMin - C.CourtRebellionSuppressMoodMax);
            return (int)Math.Round(O.RebellionSuppressionMinimumCost + ((O.RebellionSuppressionMaximumCost - O.RebellionSuppressionMinimumCost) * progress));
        }


        private void Msg(string text, Color color)
        {
            BellumCivileNotifications.ShowPersonal(text, color);
        }

        private Dictionary<FactionType, float> CalculateRawIdeologyScores(Clan clan, FactionManagerBehavior factionManager, CourtPoliticalPositionContext politicalContext)
        {
            Hero h = clan.Leader;
            int honor = h?.GetTraitLevel(DefaultTraits.Honor) ?? 0;
            int generosity = h?.GetTraitLevel(DefaultTraits.Generosity) ?? 0;
            int mercy = h?.GetTraitLevel(DefaultTraits.Mercy) ?? 0;
            int valor = h?.GetTraitLevel(DefaultTraits.Valor) ?? 0;
            int calculating = h?.GetTraitLevel(DefaultTraits.Calculating) ?? 0;
            var scores = CourtAffiliationMath.Personality(honor, generosity, mercy, valor, calculating);
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            int rank = 0;
            if (titles != null)
                foreach (var title in titles.GetTitlesHeldByClan(clan, true).Where(t => t.IsActive))
                    rank = Math.Max(rank, CourtAffiliationMath.Rank(title.TitleType));
            scores[FactionType.Nobility] += rank * 2;
            scores[FactionType.Glory] += Math.Max(0, Math.Min(8, 2 * (clan.Tier - 2)));
            scores[FactionType.Liberty] += 8 - rank * 2;
            var training = CourtAffiliationMath.Training(h);
            foreach (var type in CourtFactionRoster.Types) scores[type] += training[type];
            foreach (var faction in factionManager.GetFactionsInKingdom(clan.Kingdom).Where(f => f.IsIdeology))
                scores[faction.Type] += politicalContext.GetSocialScore(clan, faction).Total;
            return scores;
        }

        private FactionType DeterminePreferredIdeology(Clan clan, FactionManagerBehavior factionManager, CourtPoliticalPositionContext politicalContext)
        {
            var scores = CalculateRawIdeologyScores(clan, factionManager, politicalContext);
            float maximum = scores.Values.Max();
            var tied = scores.Where(p => p.Value == maximum).Select(p => p.Key).ToList();
            return tied[MBRandom.RandomInt(tied.Count)];
        }

        private void ReevaluateIdeologyMembership(FactionObject faction, Kingdom kingdom, FactionManagerBehavior factionManager, bool isPlayerKingdom, HashSet<Clan> reviewed = null)
        {
            var membersToCheck = faction.Members
                .Where(c => c != Clan.PlayerClan
                         && CourtMembershipEligibility.CanBelong(c, kingdom)
                         && c.Kingdom == kingdom
                         && c.Leader != null
                         && !c.Leader.IsDead)
                .ToList();

            if (membersToCheck.Count == 0) return;
            using (var politicalContext = new CourtPoliticalPositionContext(kingdom, affiliationOnly: true))
            {
                foreach (Clan clan in membersToCheck)
                {
                    if (reviewed != null && !reviewed.Add(clan)) continue;
                    bool isFactionLeader = clan == faction.Leader;
                    var scores = CalculateRawIdeologyScores(clan, factionManager, politicalContext);
                    float currentScore = scores[faction.Type];

                    var best = scores.OrderByDescending(kv => kv.Value).First();
                    if (best.Key == faction.Type) continue;
                    float requiredLead = isFactionLeader ? C.IdeologyLeaderSwitchThreshold : C.IdeologySwitchThreshold;
                    if (best.Value - currentScore < requiredLead) continue;

                    FactionObject targetFaction = factionManager.GetFactionsInKingdom(kingdom)
                        .FirstOrDefault(f => f.IsIdeology && f.Type == best.Key);

                    if (targetFaction == null)
                    {
                        targetFaction = new FactionObject(GetIdeologyName(best.Key, kingdom), kingdom, clan, best.Key);
                        factionManager.RegisterNewFaction(targetFaction);
                    }

                    TextObject oldName = faction.GetDisplayName();

                    faction.RemoveMember(clan);
                    targetFaction.AddMember(clan);
                    Campaign.Current?.GetCampaignBehavior<CourtPoliticalPositionBehavior>()?.RecordSwitch();

                    if (faction.Members.Count == 0)
                        factionManager.RemoveFaction(faction);

                    if (isPlayerKingdom)
                    {
                        TextObject text = new TextObject("{=BC_Ideology_SwitchedSides}{LEADER_NAME} of {CLAN_NAME} has reconsidered their political allegiances in {KINGDOM_NAME}, abandoning the {OLD_FACTION} in favor of the {NEW_FACTION}.");
                        text.SetTextVariable("LEADER_NAME", clan.Leader.Name);
                        text.SetTextVariable("CLAN_NAME", clan.Name);
                        text.SetTextVariable("OLD_FACTION", oldName);
                        text.SetTextVariable("NEW_FACTION", targetFaction.GetDisplayName());
                        text.SetTextVariable("KINGDOM_NAME", kingdom.Name);
                        Msg(text.ToString(), Colors.Yellow);
                    }
                }
            }
        }

        public string ForceGrandCoalition(Kingdom kingdom, FactionType ideologyType)
        {
            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return "FactionManagerBehavior not found";

            FactionObject ideology = factionManager.GetFactionsInKingdom(kingdom)
                .FirstOrDefault(f => f.IsIdeology && f.Type == ideologyType);

            if (ideology == null)
                return $"No active {ideologyType} faction found in {kingdom.Name}";

            if (ideology.Members.Count == 0 || ideology.Members.All(c => c == kingdom.RulingClan))
                return $"The {ideologyType} faction in {kingdom.Name} has no eligible rebel members";

            ideology.Mood = -100f;

            ideology.RemoveMember(kingdom.RulingClan);

            TriggerGrandCoalition(ideology, kingdom, factionManager);
            return null;
        }

        public string ForceFactionMeeting(Kingdom kingdom, FactionType ideologyType)
            => "Legacy faction meetings have been retired. Motions and mood activities are selected through court agendas; this command takes no action.";

        private bool TryExpelKingForRebellion(FactionObject faction, Kingdom kingdom, FactionManagerBehavior factionManager)
        {
            if (!faction.Members.Contains(kingdom.RulingClan))
                return false;

            Clan originalLeader = faction.Leader;
            if (faction.Leader == kingdom.RulingClan)
            {
                Clan newLeader = faction.Members
                    .Where(c => c != kingdom.RulingClan && !c.IsEliminated && c.Kingdom == kingdom)
                    .OrderByDescending(RebellionPowerHelper.CalculateClanPower)
                    .FirstOrDefault();

                if (newLeader == null)
                    return false;

                faction.Leader = newLeader;
            }

            Clan expelled = kingdom.RulingClan;
            faction.RemoveMember(expelled);

            bool coalitionLaunched = TriggerGrandCoalition(faction, kingdom, factionManager);
            if (!coalitionLaunched)
            {
                // Renunciation is committed only once the faction can actually issue its
                // ultimatum. Otherwise the daily ideology assignment would add the ruler
                // back and repeat the same announcement every day.
                faction.AddMember(expelled);
                faction.Leader = originalLeader;
                return false;
            }

            TextObject text = new TextObject("{=BC_Ideology_KingExpelled}The {FACTION_NAME} of {KINGDOM_NAME} have renounced {RULER_NAME}, declaring them a traitor to their cause. {LEADER_NAME} rises as their new champion!");
            text.SetTextVariable("FACTION_NAME", GetFactionDisplayName(faction));
            text.SetTextVariable("KINGDOM_NAME", kingdom.Name);
            text.SetTextVariable("RULER_NAME", expelled.Leader?.Name ?? expelled.Name);
            text.SetTextVariable("LEADER_NAME", faction.Leader?.Leader?.Name ?? faction.Leader?.Name ?? new TextObject("?"));
            BellumCivileNotifications.Show(text, BellumNotificationColors.Danger, primaryKingdom: kingdom, primaryClan: faction.Leader, secondaryClan: expelled);

            return true;
        }

        private bool TriggerGrandCoalition(FactionObject triggeringFaction, Kingdom kingdom, FactionManagerBehavior factionManager, bool showFailureMessage = true)
        {
            FactionObject existingGrandCoalition = factionManager?
                .GetFactionsInKingdom(kingdom)
                .FirstOrDefault(faction => faction != null
                    && !faction.IsIdeology
                    && faction.IsGrandCoalition
                    && (faction.IsUltimatumPending
                        || faction.IsCivilWarActive()
                        || faction.HasTrackedRebelKingdom));
            if (existingGrandCoalition != null)
            {
                BellumCivileLogger.Log(
                    $"Skipped duplicate grand coalition; kingdom={kingdom?.StringId ?? "null"}; existing={existingGrandCoalition.Name}; triggering={triggeringFaction?.Name ?? "null"}.");
                return false;
            }

            bool isPlayerKingdom = Clan.PlayerClan.Kingdom != null && Clan.PlayerClan.Kingdom == kingdom;

            Clan coalitionLeader = triggeringFaction.Leader;

            if (coalitionLeader == null || coalitionLeader.Kingdom != kingdom)
            {
                coalitionLeader = triggeringFaction.Members.FirstOrDefault(c => c.Kingdom == kingdom && !c.IsEliminated);
            }

            if (coalitionLeader == null)
            {
                triggeringFaction.Mood = -20f;
                return false;
            }

            List<FactionObject> ideologicalFactions = factionManager.GetFactionsInKingdom(kingdom).Where(f => f.IsIdeology).ToList();
            HashSet<Clan> coalitionMembers = BuildGrandCoalitionMembers(
                triggeringFaction,
                kingdom,
                factionManager,
                ideologicalFactions,
                coalitionLeader,
                out bool playerWasPulled,
                out FactionObject playerIdFaction);

            if (!CanGrandCoalitionChallengeCrown(triggeringFaction, kingdom, coalitionLeader, coalitionMembers, isPlayerKingdom && showFailureMessage))
                return false;

            if (FactionObject.IsLeaderUnsafeForRebellion(coalitionLeader, kingdom, coalitionMembers, out Settlement unsafeSettlement))
            {
                triggeringFaction.Mood = -100f;
                BellumCivileLogger.Log(
                    $"Delayed grand coalition for {GetFactionDisplayName(triggeringFaction)} of {kingdom.StringId}; leader={coalitionLeader.StringId} is inside hostile-on-rebellion settlement {unsafeSettlement?.StringId ?? "null"}.");
                return false;
            }

            if (isPlayerKingdom)
            {
                TextObject text = new TextObject("{=BC_Ideology_CallCoalition}The {FACTION_NAME} of {KINGDOM_NAME} have reached their breaking point and are calling for a grand coalition to overthrow their tyrant!");
                text.SetTextVariable("FACTION_NAME", GetFactionDisplayName(triggeringFaction));
                text.SetTextVariable("KINGDOM_NAME", kingdom.Name);
                Msg(text.ToString(), Colors.Red);
            }

            FactionType blueprint = FactionType.Abdication;
            string sourceFactionName = triggeringFaction.GetDisplayName().ToString();
            string coalitionName = $"{kingdom.Name} {sourceFactionName} Grand Coalition";
            FactionObject grandCoalition = new FactionObject(coalitionName, kingdom, coalitionLeader, blueprint);
            grandCoalition.MarkAsGrandCoalition(triggeringFaction.Type);
            factionManager.RegisterNewFaction(grandCoalition);

            foreach (Clan member in coalitionMembers.ToList())
            {
                FactionObject existingRebellion = factionManager.GetRebelFaction(member);
                if (existingRebellion != null && existingRebellion != grandCoalition)
                {
                    if (existingRebellion.Leader == member) factionManager.RemoveFaction(existingRebellion);
                    else existingRebellion.RemoveMember(member);
                }

                grandCoalition.AddMember(member);
            }

            CivilWarPlayerCallContext playerCallContext = playerWasPulled
                ? new CivilWarPlayerCallContext(CivilWarPlayerCallReason.CourtFactionMember, coalitionLeader)
                : null;
            bool ultimatumTriggered = grandCoalition.TriggerUltimatum(playerCallContext, suppressAutomaticPlayerChoice: false);
            if (ultimatumTriggered)
            {
                triggeringFaction.Mood = -20f;
            }
            else
            {
                // Do not leave an inert grand-coalition entry behind if validation changed
                // between assembling its members and issuing the ultimatum.
                factionManager.RemoveFaction(grandCoalition);
            }

            return ultimatumTriggered;
        }

        internal HashSet<Clan> GetGrandCoalitionPreviewMembers(Kingdom kingdom, FactionManagerBehavior manager) =>
            BuildGrandCoalitionMembers(null, kingdom, manager, manager.GetFactionsInKingdom(kingdom).Where(f => f.IsIdeology).ToList(),
                null, out _, out _);

        private HashSet<Clan> BuildGrandCoalitionMembers(
            FactionObject triggeringFaction,
            Kingdom kingdom,
            FactionManagerBehavior factionManager,
            List<FactionObject> ideologicalFactions,
            Clan coalitionLeader,
            out bool playerWasPulled,
            out FactionObject playerIdFaction)
        {
            HashSet<Clan> members = new HashSet<Clan>();
            playerWasPulled = false;
            playerIdFaction = null;

            if (IsValidGrandCoalitionMember(coalitionLeader, kingdom, factionManager, allowPlayer: true))
                members.Add(coalitionLeader);

            foreach (FactionObject idFaction in ideologicalFactions)
            {
                bool isTriggeringFaction = idFaction == triggeringFaction;
                if (!isTriggeringFaction && idFaction.Mood > O.GrandCoalitionJoinMoodThreshold)
                    continue;

                foreach (Clan member in idFaction.Members.ToList())
                {
                    if (member == Clan.PlayerClan && member != coalitionLeader && member.Kingdom == kingdom && !factionManager.IsClanPacified(member))
                    {
                        playerWasPulled = true;
                        playerIdFaction = idFaction;
                        continue;
                    }

                    if (IsValidGrandCoalitionMember(member, kingdom, factionManager, allowPlayer: true))
                        members.Add(member);
                }
            }

            return members;
        }

        private static bool IsValidGrandCoalitionMember(Clan clan, Kingdom kingdom, FactionManagerBehavior factionManager, bool allowPlayer)
        {
            bool isPlayerClan = clan == Clan.PlayerClan;

            return clan != null
                && clan.Kingdom == kingdom
                && !clan.IsEliminated
                && (!clan.IsMinorFaction || (allowPlayer && isPlayerClan))
                && !clan.IsUnderMercenaryService
                && clan != kingdom.RulingClan
                && (allowPlayer || !isPlayerClan)
                && !factionManager.IsClanPacified(clan);
        }

        private bool CanGrandCoalitionChallengeCrown(FactionObject triggeringFaction, Kingdom kingdom, Clan coalitionLeader, HashSet<Clan> coalitionMembers, bool isPlayerKingdom)
        {
            if (coalitionMembers == null || coalitionMembers.Count == 0)
                return false;

            RebellionPowerProjection projection = RebellionPowerHelper.CalculateProjectedConflictPower(
                kingdom,
                coalitionMembers,
                coalitionLeader,
                rebelFaction: null,
                includeProjectedSupport: true);
            float coalitionPower = projection.FactionPower;
            float loyalistPower = projection.LoyalistPower;
            bool desperate = triggeringFaction.Mood <= -100f;
            float threshold = RebellionPowerHelper.CalculateGrandCoalitionPowerThreshold(coalitionLeader?.Leader, desperate);

            if (loyalistPower <= 0f || coalitionPower >= loyalistPower * threshold)
                return true;

            BellumCivileLogger.Log($"Grand coalition delayed by weak balance of power; kingdom={kingdom?.StringId ?? "null"} faction={triggeringFaction?.Name ?? "null"} leader={coalitionLeader?.StringId ?? "null"} coalition_power={coalitionPower:0.0} loyalist_power={loyalistPower:0.0} threshold={threshold:0.00} mood={triggeringFaction?.Mood ?? 0f:0.0}.");

            if (isPlayerKingdom)
            {
                TextObject text = new TextObject("{=BC_Ideology_CoalitionTooWeak}The {FACTION_NAME} of {KINGDOM_NAME} agitate for open rebellion, but too few banners answer their call. The grand coalition lacks the strength to risk an ultimatum.");
                text.SetTextVariable("FACTION_NAME", GetFactionDisplayName(triggeringFaction));
                text.SetTextVariable("KINGDOM_NAME", kingdom.Name);
                Msg(text.ToString(), Colors.Yellow);
            }

            return false;
        }

        private bool PacifyIdeologyMembers(FactionObject ideology, Kingdom kingdom, FactionManagerBehavior factionManager, bool isMeetingRoll)
        {
            bool actionTaken = false;
            bool isPlayerKingdom = Clan.PlayerClan.Kingdom != null && Clan.PlayerClan.Kingdom == kingdom;

            foreach (Clan member in ideology.Members.Where(c => c.Kingdom == kingdom).ToList())
            {
                FactionObject rebelFaction = factionManager.GetRebelFaction(member);
                if (rebelFaction != null)
                {
                    if (rebelFaction.Leader == member)
                    {
                        factionManager.RemoveFaction(rebelFaction);
                        TextObject reason = new TextObject("{=BC_Ideology_DisbandReason_LoyaltyMandate}the {IDEOLOGY_NAME} mandated absolute loyalty to the crown");
                        reason.SetTextVariable("IDEOLOGY_NAME", GetFactionDisplayName(ideology));
                        NotificationHelper.ShowFactionDisbanded(rebelFaction, reason.ToString());
                    }
                    else
                    {
                        rebelFaction.RemoveMember(member);
                        TextObject reason = new TextObject("{=BC_Ideology_LeftReason_LoyaltyMandate}their political party, the {IDEOLOGY_NAME}, mandated loyalty");
                        reason.SetTextVariable("IDEOLOGY_NAME", GetFactionDisplayName(ideology));
                        NotificationHelper.ShowClanLeftFaction(member, rebelFaction, reason.ToString());
                    }

                    factionManager.ApplyPacifiedCooldown(member, 30); 
                    actionTaken = true;
                }
            }

            if (isPlayerKingdom)
            {
                if (actionTaken && !isMeetingRoll)
                {
                    TextObject text = new TextObject("{=BC_Ideology_PurgeSympathizers}Reaching a zenith of satisfaction, the {FACTION_NAME} of {KINGDOM_NAME} completely purged their ranks of any rebel sympathizers.");
                    text.SetTextVariable("FACTION_NAME", GetFactionDisplayName(ideology));
                    text.SetTextVariable("KINGDOM_NAME", kingdom.Name);
                    Msg(text.ToString(), Colors.Green);
                }
                else if (isMeetingRoll)
                {
                    if (actionTaken)
                    {
                        TextObject text = new TextObject("{=BC_Ideology_MandateLoyalty}During their council, the {FACTION_NAME} of {KINGDOM_NAME} mandated absolute loyalty to {RULER_NAME}, forcing all members to abandon their intrigues for the time being.");
                        text.SetTextVariable("FACTION_NAME", GetFactionDisplayName(ideology));
                        text.SetTextVariable("KINGDOM_NAME", kingdom.Name);
                        text.SetTextVariable("RULER_NAME", kingdom.RulingClan?.Leader?.Name ?? new TextObject("?"));
                        Msg(text.ToString(), Colors.Green);
                    }
                    else
                    {
                        TextObject text = new TextObject("{=BC_Ideology_GrandRally}The {FACTION_NAME} of {KINGDOM_NAME} hosted a grand rally, celebrating the unbroken peace and their absolute loyalty to the crown.");
                        text.SetTextVariable("FACTION_NAME", GetFactionDisplayName(ideology));
                        text.SetTextVariable("KINGDOM_NAME", kingdom.Name);
                        Msg(text.ToString(), Colors.Green);
                    }
                }
            }

            return actionTaken;
        }

        private void ApplyPlayerNeutralityPenalty(FactionManagerBehavior factionManager)
        {
            Clan playerClan = Clan.PlayerClan;
            Kingdom realm = playerClan?.Kingdom;
            if (!CanRunCourtPolitics(realm) || !CourtMembershipEligibility.CanBelong(playerClan, realm)
                || factionManager.GetFactionByRebelKingdom(realm) != null) return;

            if (factionManager.GetIdeologicalFaction(playerClan) == null)
            {
                List<Clan> otherClans = realm.Clans.Where(c => c != playerClan && CourtMembershipEligibility.CanBelong(c, realm)
                    && c.Leader?.IsAlive == true).ToList();
                if (otherClans.Count > 0)
                {
                    Clan randomClan = otherClans[MBRandom.RandomInt(otherClans.Count)];
                    if (randomClan.Leader != null)
                    {
                        RelationMemoryService.ApplyChangeWithDefaultDuration(Hero.MainHero, randomClan.Leader, -1, false,
                            RelationMemorySources.CourtNeutrality, RelationMemoryScope.Personal);
                        TextObject text = new TextObject("{=BC_Ideology_NeutralityPenalty}The lords of the realm wonder where your loyalty lies. You lost relation with {CLAN_LEADER} (-1).");
                        text.SetTextVariable("CLAN_LEADER", randomClan.Leader.Name);
                        Msg(text.ToString(), Colors.Yellow);
                    }
                }

                _nextNeutralityPenaltyDate = CampaignTime.Now + CampaignTime.Days(MBRandom.RandomInt(1, 4)); 
            }
        }

        private static string FormatBreakdownLine(string labelId, string fallbackLabel, float value)
        {
            TextObject label = new TextObject("{=" + labelId + "}" + fallbackLabel);
            string sign = value > 0 ? "+" : string.Empty;
            return $"{label}: {sign}{value:0.#}";
        }

        private static string GetFactionDisplayName(FactionObject faction)
        {
            if (faction == null) return string.Empty;
            return faction.GetDisplayName().ToString();
        }

        private static bool IsEligibleCourtRebellionLeader(Clan clan, Kingdom kingdom)
        {
            return clan != null
                && clan != kingdom?.RulingClan
                && clan.Kingdom == kingdom
                && !clan.IsEliminated
                && !clan.IsUnderMercenaryService
                && (!clan.IsMinorFaction || clan == Clan.PlayerClan)
                && clan.Leader != null
                && clan.Leader.IsAlive
                && !clan.Leader.IsDisabled;
        }

        private static List<Clan> GetRankedCourtFactionLeadershipCandidates(
            FactionObject faction,
            Kingdom kingdom,
            bool excludeRulingClan = false)
        {
            return faction?.Members?
                .Where(clan => clan != null
                    && clan.Kingdom == kingdom
                    && CourtMembershipEligibility.CanBelong(clan, kingdom)
                    && !clan.IsEliminated
                    && !clan.IsUnderMercenaryService
                    && clan.Leader != null
                    && clan.Leader.IsAlive
                    && !clan.Leader.IsDisabled)
                .OrderByDescending(RebellionPowerHelper.CalculateClanPower)
                .ThenByDescending(clan => clan.Influence)
                .ThenByDescending(clan => clan.Tier)
                .ThenBy(clan => clan.StringId)
                .ToList() ?? new List<Clan>();
        }

        private static void ApplyCourtFactionLeadershipChange(FactionObject faction, Kingdom kingdom, Clan newLeader)
        {
            if (faction == null || kingdom == null || !CourtMembershipEligibility.CanBelong(newLeader, kingdom) || faction.Leader == newLeader)
                return;

            faction.Leader = newLeader;

            if (newLeader == Clan.PlayerClan)
                CourtAgendaBehavior.Current?.ReviewInheritedFactionAgenda(faction);

            TextObject text = new TextObject("{=BC_Ideology_LeaderChange}Following a council meeting in {KINGDOM_NAME}, {CLAN_NAME} has officially taken over leadership of the {FACTION_NAME}.");
            text.SetTextVariable("CLAN_NAME", newLeader.Name);
            text.SetTextVariable("FACTION_NAME", GetFactionDisplayName(faction));
            text.SetTextVariable("KINGDOM_NAME", kingdom.Name);
            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.Politics,
                primaryKingdom: kingdom,
                primaryClan: newLeader);
        }

        private static void ShowPlayerCourtFactionLeadershipInquiry(FactionObject faction, Kingdom kingdom)
        {
            if (faction == null || kingdom == null)
                return;

            bool hasAlternativeCandidate = GetRankedCourtFactionLeadershipCandidates(faction, kingdom)
                .Any(clan => clan != Clan.PlayerClan);
            TextObject title = new TextObject("{=BC_Ideology_PlayerLeader_Title}Court Faction Leadership");
            TextObject description = new TextObject("{=BC_Ideology_PlayerLeader_Desc}Based on the perceived strength and influence of your house, your peers in the {FACTION_NAME} have chosen you to represent their interests in court.");
            description.SetTextVariable("FACTION_NAME", GetFactionDisplayName(faction));

            InformationManager.ShowInquiry(new InquiryData(
                title.ToString(),
                description.ToString(),
                true,
                hasAlternativeCandidate,
                new TextObject("{=BC_Ideology_PlayerLeader_Accept}Accept").ToString(),
                new TextObject("{=BC_Ideology_PlayerLeader_Refuse}Refuse").ToString(),
                () =>
                {
                    if (CourtMembershipEligibility.CanBelong(Clan.PlayerClan, kingdom)
                        && faction.Members?.Contains(Clan.PlayerClan) == true)
                    {
                        ApplyCourtFactionLeadershipChange(faction, kingdom, Clan.PlayerClan);
                    }
                },
                () =>
                {
                    Clan nextCandidate = GetRankedCourtFactionLeadershipCandidates(faction, kingdom)
                        .FirstOrDefault(clan => clan != Clan.PlayerClan);
                    if (nextCandidate == null)
                        return;

                    faction.Leader = nextCandidate;
                    TextObject refusal = new TextObject("{=BC_Ideology_PlayerLeader_Refused}Having declined leadership of the {FACTION_NAME}, you leave {CLAN_NAME} to represent its interests in court.");
                    refusal.SetTextVariable("FACTION_NAME", GetFactionDisplayName(faction));
                    refusal.SetTextVariable("CLAN_NAME", nextCandidate.Name);
                    BellumCivileNotifications.Show(
                        refusal,
                        BellumNotificationColors.Politics,
                        primaryKingdom: kingdom,
                        primaryClan: nextCandidate);
                }), true);
        }

        private static TextObject GetIdeologyNameText(FactionType type, Kingdom kingdom)
        {
            return CourtInstitutionDisplayHelper.GetCourtFactionName(type, kingdom);
        }

        private string GetIdeologyName(FactionType type, Kingdom kingdom)
        {
            return GetIdeologyNameText(type, kingdom).ToString();
        }

        private float GetRealmStrength(Kingdom kingdom)
        {
            float power = 0f;
            foreach (Clan clan in kingdom.Clans.Where(c => !c.IsUnderMercenaryService))
            {
                power += RebellionPowerHelper.CalculateClanPower(clan);
            }
            return power;
        }

        private static int GetNetTribute(Kingdom kingdom)
        {
            int net = 0;
            foreach (Kingdom other in Kingdom.All)
            {
                if (other == kingdom || other.IsEliminated) continue;
                StanceLink stance = kingdom.GetStanceWith(other);
                if (stance == null || stance.IsAtWar) continue;
                net += stance.GetDailyTributeToPay(other) - stance.GetDailyTributeToPay(kingdom);
            }
            return net;
        }

        private static bool HasAnyAlliance(Kingdom kingdom)
        {
            return kingdom.AlliedKingdoms != null && kingdom.AlliedKingdoms.Count > 0;
        }

        private static bool HasAnyTradeAgreement(Kingdom kingdom)
        {
            var tradeAgreements = Campaign.Current.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
            if (tradeAgreements == null) return false;
            foreach (Kingdom other in Kingdom.All)
            {
                if (other == kingdom || other.IsEliminated) continue;
                if (tradeAgreements.HasTradeAgreement(kingdom, other, out _)) return true;
            }
            return false;
        }

        private void OnDailyTick_Consolidated(Kingdom kingdom, FactionManagerBehavior factionManager)
        {
            List<Clan> kingdomClans = kingdom.Clans.Where(c => !c.IsUnderMercenaryService && (!c.IsMinorFaction || c == Clan.PlayerClan) && !c.IsEliminated && c.Leader != null && c.Leader.IsAlive).ToList();
            foreach (Clan clan in kingdomClans)
            {
                if (clan != Clan.PlayerClan && clan != kingdom.RulingClan)
                {
                    if (ShouldRunRebellionReview(clan))
                        OnDailyTickClan_Rebellion(clan, factionManager);
                }
            }
        }

        private bool ShouldRunRebellionReview(Clan clan)
        {
            if (clan == null)
                return false;

            string clanId = clan.StringId;
            if (_rebellionReviewDates.TryGetValue(clanId, out CampaignTime nextReview) && nextReview.IsFuture)
                return false;

            int interval = MBRandom.RandomInt(
                C.RebellionIntentReviewMinimumDays,
                C.RebellionIntentReviewMaximumDays + 1);
            _rebellionReviewDates[clanId] = CampaignTime.Now + CampaignTime.Days(interval);
            return nextReview != CampaignTime.Zero;
        }

        private static string GetRebelFactionRejoinCooldownKey(Clan clan, FactionObject faction)
        {
            string clanId = clan?.StringId ?? "null";
            string kingdomId = faction?.ParentKingdom?.StringId ?? "null";
            string leaderId = faction?.Leader?.StringId ?? "null";
            string typeId = faction?.Type.ToString() ?? "Unknown";
            return $"{kingdomId}|{typeId}|{leaderId}|{clanId}";
        }

        private void ApplyRebelFactionRejoinCooldown(Clan clan, FactionObject faction)
        {
            if (clan == null || faction == null || faction.IsIdeology)
                return;

            _rebelFactionRejoinCooldowns[GetRebelFactionRejoinCooldownKey(clan, faction)] =
                CampaignTime.Now + CampaignTime.Days(C.RebelFactionRejoinCooldownDays);
        }

        private bool IsRebelFactionRejoinOnCooldown(Clan clan, FactionObject faction)
        {
            if (clan == null || faction == null)
                return false;

            string key = GetRebelFactionRejoinCooldownKey(clan, faction);
            if (_rebelFactionRejoinCooldowns.TryGetValue(key, out CampaignTime cooldown))
            {
                if (cooldown.IsFuture)
                    return true;

                _rebelFactionRejoinCooldowns.Remove(key);
            }

            return false;
        }

        private void PruneExpiredRebelFactionRejoinCooldowns()
        {
            if (_rebelFactionRejoinCooldowns == null || _rebelFactionRejoinCooldowns.Count == 0)
                return;

            foreach (string expiredKey in _rebelFactionRejoinCooldowns
                .Where(kv => kv.Value.IsPast)
                .Select(kv => kv.Key)
                .ToList())
            {
                _rebelFactionRejoinCooldowns.Remove(expiredKey);
            }
        }

        private void OnDailyTickClan_Rebellion(Clan clan, FactionManagerBehavior manager)
        {
            if (manager.IsClanPacified(clan)) return;

            float score = CalculateRebellionScore(clan);
            float intentThreshold = O.RebelliousIntentThreshold;
            float withdrawalThreshold = intentThreshold * 0.5f;
            FactionObject currentFaction = manager.GetRebelFaction(clan);

            if (score >= intentThreshold)
            {
                if (currentFaction == null)
                    Campaign.Current?.GetCampaignBehavior<RebellionSummaryBehavior>()
                        ?.RecordIntentThresholdCrossed(clan, score, intentThreshold);

                bool defectedToCivilWar = AttemptDefectToActiveCivilWar(clan, manager);
                if (defectedToCivilWar) return; 
            }

            if (currentFaction != null)
            {
                if (currentFaction.Leader == clan)
                {
                    if (score < withdrawalThreshold)
                    {
                        RecordPreWarFactionDisband(currentFaction, "leader_below_half_intent");
                        manager.RemoveFaction(currentFaction);
                        NotificationHelper.ShowFactionDisbanded(currentFaction, new TextObject("{=BC_Ideology_DisbandReason_Appeased}their leader was appeased by the crown").ToString());
                        return;
                    }

                    float daysActive = currentFaction.CreationDate.ElapsedDaysUntilNow;
                    float gracePeriod = 28f;

                    int valor = clan.Leader.GetTraitLevel(DefaultTraits.Valor);
                    int calc = clan.Leader.GetTraitLevel(DefaultTraits.Calculating);

                    if (valor >= 2 || calc >= 2) gracePeriod += 14f;
                    else if (valor <= -2 || calc <= -2) gracePeriod -= 7f;

                    if (daysActive >= gracePeriod && currentFaction.Discontent < C.RebelFactionWeakSupportDiscontentFloor)
                    {
                        float factionPower = currentFaction.CalculateFactionPower();
                        float loyalistPower = currentFaction.CalculateLoyalistPower();

                        if (factionPower < loyalistPower * 0.40f)
                        {
                            RecordPreWarFactionDisband(currentFaction, "weak_support_stagnation");
                            manager.RemoveFaction(currentFaction);
                            NotificationHelper.ShowFactionDisbanded(currentFaction, new TextObject("{=BC_Ideology_DisbandReason_WeakSupport}it failed to gather enough support").ToString());
                            return;
                        }
                    }
                }
                else
                {
                    if (score < withdrawalThreshold)
                    {
                        currentFaction.RemoveMember(clan);
                        ApplyRebelFactionRejoinCooldown(clan, currentFaction);
                        Campaign.Current?.GetCampaignBehavior<RebellionSummaryBehavior>()
                            ?.RecordFactionLeft(clan, currentFaction, "member_below_half_intent");
                        NotificationHelper.ShowClanLeftFaction(clan, currentFaction, new TextObject("{=BC_Ideology_LeftReason_ColdFeet}they got cold feet").ToString());
                        return;
                    }

                    if (currentFaction.Leader?.Leader != null && clan.Leader.GetRelation(currentFaction.Leader.Leader) < O.RebelFactionLeaderRivalryThreshold)
                    {
                        currentFaction.RemoveMember(clan);
                        ApplyRebelFactionRejoinCooldown(clan, currentFaction);
                        Campaign.Current?.GetCampaignBehavior<RebellionSummaryBehavior>()
                            ?.RecordFactionLeft(clan, currentFaction, "leader_rivalry");
                        NotificationHelper.ShowClanLeftFaction(clan, currentFaction, new TextObject("{=BC_Ideology_LeftReason_BitterRivalry}due to a bitter rivalry with the faction leader").ToString());
                        return;
                    }
                }
            }
            else if (score >= intentThreshold)
            {
                AttemptJoinOrFormFaction(clan, manager);
            }
        }

        private static void RecordPreWarFactionDisband(FactionObject faction, string reason)
        {
            Campaign.Current?.GetCampaignBehavior<RebellionSummaryBehavior>()
                ?.RecordFactionDisbanded(faction, reason);
        }

        private sealed class RebelFactionChoice
        {
            public FactionObject Faction;
            public float Score;
        }

        // Active civil wars take precedence over conspiracies, but a lord still needs
        // both acceptable military odds and a political reason to back that cause.
        private bool AttemptDefectToActiveCivilWar(Clan clan, FactionManagerBehavior manager)
        {
            Kingdom parentKingdom = clan?.Kingdom;
            if (parentKingdom == null || clan.Leader == null || manager == null)
                return false;

            List<FactionObject> activeRebelFactions = manager.GetFactionsInKingdom(parentKingdom)
                .Where(faction => faction != null && !faction.IsIdeology && faction.IsCivilWarActive())
                .ToList();
            if (activeRebelFactions.Count == 0)
                return false;

            float loyalistPower = parentKingdom.Clans
                .Where(candidate => candidate != null && !candidate.IsEliminated && !candidate.IsUnderMercenaryService)
                .Sum(RebellionPowerHelper.CalculateClanPower);

            foreach (FactionType cause in RebelCausePriorityOrder)
            {
                RebelFactionChoice bestChoice = null;
                foreach (FactionObject faction in activeRebelFactions.Where(candidate => candidate.Type == cause))
                {
                    if (!TryCalculateActiveRebellionReadiness(clan, faction, loyalistPower, out float readinessScore)
                        || !TryCalculateRebelFactionPoliticalScore(
                            clan,
                            faction,
                            manager,
                            allowSocialTieToIndependence: true,
                            out float politicalScore))
                    {
                        continue;
                    }

                    float score = politicalScore + readinessScore;
                    if (bestChoice == null || score > bestChoice.Score)
                        bestChoice = new RebelFactionChoice { Faction = faction, Score = score };
                }

                if (bestChoice == null)
                    continue;

                FactionObject rebelFaction = bestChoice.Faction;
                Kingdom activeRebellion = rebelFaction.GetRebelKingdom();
                if (activeRebellion == null || activeRebellion.IsEliminated)
                    continue;

                rebelFaction.AddMember(clan);
                rebelFaction.MoveClanToKingdomPreservingCivilWarInfluence(
                    clan,
                    activeRebellion,
                    preserveCustomBanner: true,
                    showNotification: false);
                Campaign.Current?.GetCampaignBehavior<RebellionSummaryBehavior>()
                    ?.RecordFactionJoined(clan, rebelFaction);
                NotificationHelper.ShowClanDefectedToRebellion(clan, activeRebellion, parentKingdom);
                Campaign.Current?.GetCampaignBehavior<ConflictCallResponseBehavior>()
                    ?.QueueLateDefection(rebelFaction, clan);
                return true;
            }

            return false;
        }

        private static bool TryCalculateActiveRebellionReadiness(
            Clan clan,
            FactionObject faction,
            float loyalistPower,
            out float readinessScore)
        {
            readinessScore = 0f;
            Kingdom activeRebellion = faction?.GetRebelKingdom();
            if (clan?.Leader == null || activeRebellion == null || activeRebellion.IsEliminated)
                return false;

            float rebelPower = activeRebellion.Clans
                .Where(candidate => candidate != null && !candidate.IsEliminated && !candidate.IsUnderMercenaryService)
                .Sum(RebellionPowerHelper.CalculateClanPower);
            float powerRatio = loyalistPower > 0f ? rebelPower / loyalistPower : 1f;
            float requiredPowerRatio = 0.50f;

            int calculating = clan.Leader.GetTraitLevel(DefaultTraits.Calculating);
            if (calculating >= 1) requiredPowerRatio += 0.30f;
            else if (calculating <= -1) requiredPowerRatio -= 0.30f;

            int valor = clan.Leader.GetTraitLevel(DefaultTraits.Valor);
            if (valor <= -1) requiredPowerRatio += 0.20f;
            else if (valor >= 1) requiredPowerRatio -= 0.20f;

            requiredPowerRatio = MathF.Max(0f, requiredPowerRatio);
            if (powerRatio < requiredPowerRatio)
                return false;

            float rebelShare = rebelPower / MathF.Max(1f, rebelPower + loyalistPower);
            readinessScore = MathF.Clamp(rebelShare, 0f, 1f) * C.RebelFactionMaxReadinessScore;
            return true;
        }

        // What does this complex formula do?
        // Calculates a numeric logic score indicating a clan's willingness to commit treason based on relation with the liege, fief hunger, cultural friction, and traits.
        public float CalculateRebellionScore(Clan clan)
        {
            return RebellionIntentCalculator.Assess(clan).Total;
        }

        private void AttemptJoinOrFormFaction(Clan clan, FactionManagerBehavior manager)
        {
            Kingdom kingdom = clan?.Kingdom;
            if (manager == null || kingdom == null || clan.Leader == null)
                return;

            List<FactionObject> kingdomFactions = manager.GetFactionsInKingdom(kingdom);
            RebellionSummaryBehavior summary = Campaign.Current?.GetCampaignBehavior<RebellionSummaryBehavior>();

            // A recognized claimant is considered first. A lord who rejects every
            // available claimant then follows their own territorial cause instead.
            FactionObject factionToJoin = FindBestPreparationFaction(
                clan,
                kingdom,
                kingdomFactions,
                FactionType.InstallRuler,
                manager,
                summary);
            if (TryJoinPreparationFaction(clan, factionToJoin, summary))
                return;

            if (RoyalistClaimHelper.TryGetThroneClaimStrength(clan, kingdom, manager, out _))
            {
                CreateRebelFaction(clan, FactionType.InstallRuler, manager);
                return;
            }

            if (HasIndependenceStake(clan, kingdom))
            {
                factionToJoin = FindBestPreparationFaction(
                    clan,
                    kingdom,
                    kingdomFactions,
                    FactionType.Independence,
                    manager,
                    summary);
                if (TryJoinPreparationFaction(clan, factionToJoin, summary))
                    return;

                CreateRebelFaction(clan, FactionType.Independence, manager);
                return;
            }

            factionToJoin = FindBestPreparationFaction(
                clan,
                kingdom,
                kingdomFactions,
                FactionType.Abdication,
                manager,
                summary);
            if (!TryJoinPreparationFaction(clan, factionToJoin, summary))
                CreateRebelFaction(clan, FactionType.Abdication, manager);
        }

        private FactionObject FindBestPreparationFaction(
            Clan clan,
            Kingdom kingdom,
            IEnumerable<FactionObject> factions,
            FactionType cause,
            FactionManagerBehavior manager,
            RebellionSummaryBehavior summary)
        {
            RebelFactionChoice bestChoice = null;
            foreach (FactionObject faction in factions.Where(candidate => candidate != null && candidate.Type == cause))
            {
                if (!IsAvailableConspiracyForRecruitment(faction, kingdom))
                {
                    summary?.RecordJoinRejection("unavailable_faction_state");
                    continue;
                }

                if (IsRebelFactionRejoinOnCooldown(clan, faction))
                {
                    summary?.RecordJoinRejection("rejoin_cooldown");
                    continue;
                }

                if (!TryCalculateRebelFactionPoliticalScore(
                    clan,
                    faction,
                    manager,
                    allowSocialTieToIndependence: false,
                    out float politicalScore))
                {
                    summary?.RecordJoinRejection("politically_misaligned");
                    continue;
                }

                float score = politicalScore + CalculatePreparationReadinessScore(faction);
                if (bestChoice == null || score > bestChoice.Score)
                    bestChoice = new RebelFactionChoice { Faction = faction, Score = score };
            }

            return bestChoice?.Faction;
        }

        private static bool TryJoinPreparationFaction(
            Clan clan,
            FactionObject faction,
            RebellionSummaryBehavior summary)
        {
            if (clan == null || faction == null)
                return false;

            faction.AddMember(clan);
            summary?.RecordFactionJoined(clan, faction);
            NotificationHelper.ShowClanJoinedFaction(clan, faction);
            return true;
        }

        private static void CreateRebelFaction(Clan clan, FactionType cause, FactionManagerBehavior manager)
        {
            if (clan?.Kingdom == null || manager == null)
                return;

            string factionName = $"{clan.Name} Revolt";
            switch (cause)
            {
                case FactionType.Independence: factionName = $"{clan.Name} Secessionists"; break;
                case FactionType.Abdication: factionName = $"{clan.Name} Coalition"; break;
                case FactionType.InstallRuler: factionName = $"{clan.Name} Claimants"; break;
            }

            FactionObject faction = new FactionObject(factionName, clan.Kingdom, clan, cause);
            manager.RegisterNewFaction(faction);
            NotificationHelper.ShowFactionFormed(faction);
        }

        private static float CalculatePreparationReadinessScore(FactionObject faction)
        {
            if (faction == null)
                return 0f;

            float factionPower = MathF.Max(0f, faction.CalculateFactionPower());
            float loyalistPower = MathF.Max(0f, faction.CalculateLoyalistPower());
            float share = factionPower / MathF.Max(1f, factionPower + loyalistPower);
            return MathF.Clamp(share, 0f, 1f) * C.RebelFactionMaxReadinessScore;
        }

        private static bool TryCalculateRebelFactionPoliticalScore(
            Clan clan,
            FactionObject faction,
            FactionManagerBehavior manager,
            bool allowSocialTieToIndependence,
            out float score)
        {
            score = 0f;
            Kingdom kingdom = faction?.ParentKingdom;
            Clan factionLeader = faction?.Leader;
            Hero clanLeader = clan?.Leader;
            Hero rebelLeader = factionLeader?.Leader;
            Hero ruler = kingdom?.RulingClan?.Leader;
            if (kingdom == null || clanLeader == null || rebelLeader == null || ruler == null)
                return false;

            int relationWithLeader = clanLeader.GetRelation(rebelLeader);
            if (relationWithLeader < O.RebelFactionLeaderRivalryThreshold)
                return false;

            int relationWithRuler = clanLeader.GetRelation(ruler);
            float relationComparison = MathF.Clamp(
                (relationWithLeader - relationWithRuler) * C.RebelFactionRelationComparisonScale,
                -C.RebelFactionMaxRelationComparison,
                C.RebelFactionMaxRelationComparison);

            bool marriageAlliance = MarriageAllianceHelper.HasMarriageAlliance(clan, factionLeader);
            bool dynasticKin = CivilWarSolidarityHelper.AreCloseDynasticKin(clan, factionLeader);
            bool closeFriend = relationWithLeader >= C.TreasonSolidarityFriendRelationThreshold;
            float hierarchyPull = CalculateHierarchicalRebelLeaderPull(clan, factionLeader);
            bool hierarchyTie = hierarchyPull >= C.RebelFactionHierarchicalLoyaltyJoinThreshold;
            bool hasSocialTie = marriageAlliance || dynasticKin || closeFriend || hierarchyTie;

            float socialPull = hierarchyPull;
            if (marriageAlliance) socialPull += C.TreasonSolidarityMarriageSupport;
            if (dynasticKin) socialPull += C.CivilWarSolidarityDynasticKinSupport;
            if (closeFriend) socialPull += C.RebelFactionFriendshipJoinBonus;

            switch (faction.Type)
            {
                case FactionType.InstallRuler:
                    if (!RoyalistClaimHelper.TryGetThroneClaimStrength(
                        factionLeader,
                        kingdom,
                        manager,
                        out FeudalClaimStrength claimStrength))
                    {
                        return false;
                    }

                    bool recognizedClaim = RoyalistClaimHelper.IsRoyalistSupporterOfClaimant(
                        clan,
                        factionLeader,
                        kingdom,
                        manager);
                    if (relationWithLeader <= relationWithRuler && !hasSocialTie && !recognizedClaim)
                        return false;

                    score = relationComparison
                        + socialPull
                        + (claimStrength == FeudalClaimStrength.Strong
                            ? C.RebelInstallRulerStrongClaimJoinBonus
                            : C.RebelInstallRulerWeakClaimJoinBonus)
                        + (recognizedClaim ? C.RebelInstallRulerRecognizedClaimJoinBonus : 0f);
                    break;

                case FactionType.Independence:
                    bool independenceStake = HasIndependenceStake(clan, kingdom);
                    if (!independenceStake && !(allowSocialTieToIndependence && hasSocialTie))
                        return false;

                    score = relationComparison
                        + socialPull
                        + (independenceStake ? CalculateIndependenceStakeScore(clan, kingdom) : 0f);
                    break;

                case FactionType.Abdication:
                    score = C.RebelAbdicationBaseWeight + relationComparison + socialPull;
                    break;

                default:
                    return false;
            }

            return score >= C.RebelFactionPoliticalAlignmentFloor;
        }

        private static bool IsAvailableConspiracyForRecruitment(FactionObject faction, Kingdom kingdom)
        {
            return faction != null
                && !faction.IsIdeology
                && !faction.IsGrandCoalition
                && faction.ParentKingdom == kingdom
                && faction.Leader?.Kingdom == kingdom
                && !faction.IsCivilWarActive()
                && !faction.HasTrackedRebelKingdom
                && !faction.IsUltimatumPending;
        }

        private static bool HasIndependenceStake(Clan clan, Kingdom kingdom)
        {
            CultureObject rulerCulture = kingdom?.RulingClan?.Culture ?? kingdom?.Culture;
            if (clan == null || kingdom == null || rulerCulture == null)
                return false;

            if (clan.Culture != rulerCulture
                || clan.Fiefs.Any(fief => fief?.Settlement?.Culture != null && fief.Settlement.Culture != rulerCulture))
            {
                return true;
            }

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            return titleBehavior != null
                && titleBehavior.GetTitlesHeldByClan(clan, deJure: false)
                    .Any(title => IsTitleOutsideRealmDeJure(titleBehavior, title, kingdom));
        }

        private static float CalculateIndependenceStakeScore(Clan clan, Kingdom kingdom)
        {
            CultureObject rulerCulture = kingdom?.RulingClan?.Culture ?? kingdom?.Culture;
            if (clan == null || kingdom == null || rulerCulture == null)
                return 0f;

            bool culturalStake = clan.Culture != rulerCulture
                || clan.Fiefs.Any(fief => fief?.Settlement?.Culture != null && fief.Settlement.Culture != rulerCulture);
            float score = 0f;
            if (culturalStake)
            {
                score += C.RebelIndependenceCulturalStakeWeight;
                if (clan.Leader?.GetTraitLevel(DefaultTraits.Honor) >= 1)
                    score -= C.RebelIndependenceHonorablePenalty;
            }

            score += CalculateIndependenceTitleWeight(clan, kingdom, rulerCulture);
            return MathF.Max(0f, score);
        }

        private static float CalculateIndependenceTitleWeight(Clan clan, Kingdom kingdom, CultureObject rulerCulture)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null || clan == null || kingdom == null)
                return 0f;

            float bestScore = 0f;
            foreach (FeudalTitleRecord title in titleBehavior.GetTitlesHeldByClan(clan, deJure: false))
            {
                if (title == null
                    || !title.IsActive
                    || !IsTitleOutsideRealmDeJure(titleBehavior, title, kingdom))
                {
                    continue;
                }

                float baseScore = GetIndependenceTitleBaseWeight(title.TitleType);
                if (baseScore <= 0f)
                    continue;

                bool deJureHolder = title.DeJureHolderClanId == clan.StringId;
                float score = deJureHolder
                    ? baseScore
                    : baseScore * C.RebelIndependenceDeFactoOnlyTitleMultiplier;

                if (TitleRegionFavorsClanCulture(titleBehavior, title, clan.Culture, rulerCulture))
                    score += C.RebelIndependenceCulturalTitleRegionBonus;

                bestScore = MathF.Max(bestScore, score);
            }

            return bestScore;
        }

        private static float GetIndependenceTitleBaseWeight(FeudalTitleType titleType)
        {
            switch (titleType)
            {
                case FeudalTitleType.Barony:
                    return C.RebelIndependenceBaronyTitleWeight;
                case FeudalTitleType.County:
                    return C.RebelIndependenceCountyTitleWeight;
                case FeudalTitleType.Duchy:
                    return C.RebelIndependenceDuchyTitleWeight;
                case FeudalTitleType.Kingdom:
                    return C.RebelIndependenceKingdomTitleWeight;
                default:
                    return 0f;
            }
        }

        private static bool TitleRegionFavorsClanCulture(FeudalTitleBehavior titleBehavior, FeudalTitleRecord title, CultureObject clanCulture, CultureObject rulerCulture)
        {
            if (titleBehavior == null || title == null || clanCulture == null || clanCulture == rulerCulture)
                return false;

            List<Settlement> settlements = titleBehavior.GetDescendantBaronyTitles(title)
                .Where(barony => barony != null && !string.IsNullOrWhiteSpace(barony.CapitalSettlementId))
                .Select(barony => Settlement.All.FirstOrDefault(settlement => settlement != null && settlement.StringId == barony.CapitalSettlementId))
                .Where(settlement => settlement != null)
                .ToList();

            if (settlements.Count == 0 && !string.IsNullOrWhiteSpace(title.CapitalSettlementId))
            {
                Settlement capital = Settlement.All.FirstOrDefault(settlement => settlement != null && settlement.StringId == title.CapitalSettlementId);
                if (capital != null)
                    settlements.Add(capital);
            }

            if (settlements.Count == 0)
                return false;

            int clanCultureSettlements = settlements.Count(settlement => settlement.Culture == clanCulture);
            return clanCultureSettlements * 2 >= settlements.Count;
        }

        private static bool IsTitleOutsideRealmDeJure(
            FeudalTitleBehavior titleBehavior,
            FeudalTitleRecord title,
            Kingdom kingdom)
        {
            if (titleBehavior == null || title == null || !title.IsActive || kingdom == null)
                return false;

            FeudalTitleRecord sovereignTitle = titleBehavior.GetRealmSovereignTitle(kingdom, FeudalHierarchyMode.DeJure)
                ?? titleBehavior.GetKingdomPoliticalTitle(kingdom);
            if (sovereignTitle == null || !sovereignTitle.IsActive)
                return false;

            FeudalTitleRecord current = title;
            HashSet<string> visited = new HashSet<string>();
            for (int depth = 0; current != null && depth < 24 && visited.Add(current.TitleId); depth++)
            {
                if (string.Equals(current.TitleId, sovereignTitle.TitleId, StringComparison.Ordinal))
                    return false;

                current = titleBehavior.GetParentTitle(current, FeudalHierarchyMode.DeJure);
            }

            return true;
        }

        private static float CalculateHierarchicalRebelLeaderPull(Clan supporter, Clan rebelLeader)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null || supporter == null || rebelLeader == null || supporter == rebelLeader)
                return 0f;

            float bestPull = 0f;
            foreach (FeudalTitleRecord title in titleBehavior.GetTitlesHeldByClan(supporter, deJure: false))
            {
                if (title == null || !title.IsActive || string.IsNullOrWhiteSpace(title.ParentTitleId))
                    continue;

                bestPull = MathF.Max(bestPull, CalculateTitleChainLiegePull(titleBehavior, title, rebelLeader));
            }

            return bestPull;
        }

        private static float CalculateTitleChainLiegePull(FeudalTitleBehavior titleBehavior, FeudalTitleRecord title, Clan rebelLeader)
        {
            string parentId = title?.ParentTitleId;
            int depth = 0;
            while (!string.IsNullOrWhiteSpace(parentId) && depth++ < 12)
            {
                FeudalTitleRecord parent = titleBehavior.GetTitle(parentId);
                if (parent == null || !parent.IsActive)
                    break;

                if (parent.DeFactoHolderClanId == rebelLeader.StringId || parent.DeJureHolderClanId == rebelLeader.StringId)
                {
                    return depth == 1
                        ? C.RebelFactionHierarchicalDirectLiegePull
                        : C.RebelFactionHierarchicalHighLiegePull;
                }

                parentId = parent.ParentTitleId;
            }

            return 0f;
        }


        public bool TryStartTreasonVote(Kingdom kingdom, Clan clan, Clan proposerClan, out TextObject explanation)
        {
            if (kingdom?.RulingClan == Clan.PlayerClan && proposerClan == Clan.PlayerClan)
            {
                if (!CanRulerIndictClan(kingdom, clan, false, out explanation)) return false;
                return CourtAgendaBehavior.Current?.TryPlayerTreasonBusiness(kingdom, clan, out explanation) == true;
            }
            explanation = new TextObject("{=BC_CourtAgenda_Unavailable}The court cannot receive a motion at present.");
            return CourtAgendaBehavior.Current != null
                && CourtAgendaBehavior.Current.TryNominateTreason(kingdom, clan, proposerClan, out explanation);
        }

        internal bool FileAgendaTreasonVote(Kingdom kingdom, Clan clan, Clan proposerClan, CampaignTime voteDate)
        {
            if (!CanRulerIndictClan(kingdom, clan, false, out _))
                return false;

            var deliberation = Campaign.Current.GetCampaignBehavior<ExpulsionDeliberationBehavior>();
            if (deliberation?.QueueExpulsionVote(kingdom, clan, proposerClan, voteDate) != true)
                return false;

            ApplyTreasonIndictmentFactionShock(clan, Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>());
            return true;
        }

        private static void ApplyTreasonIndictmentFactionShock(Clan targetClan, FactionManagerBehavior factionManager)
        {
            FactionObject targetIdeology = factionManager?.GetIdeologicalFaction(targetClan);
            if (targetIdeology == null)
                return;

            targetIdeology.Mood = MathF.Max(targetIdeology.Mood - C.ExpelFactionMoodShock, -100f);
            var shockBehavior = Campaign.Current.GetCampaignBehavior<IdeologyEventShockBehavior>();
            shockBehavior?.RecordExpulsionAttempt(targetIdeology, (int)C.ExpelFactionMoodShock);
        }

        public bool TryResolvePlayerLegalExpulsionJudgment(Kingdom kingdom, Clan sponsorClan, Clan targetClan)
        {
            if (targetClan != Clan.PlayerClan) return false;
            if (kingdom == null || targetClan?.Leader == null || targetClan.Leader.IsDead) return false;
            if (targetClan.Kingdom != kingdom) return false;

            Clan rulingClan = kingdom.RulingClan;
            Hero ruler = rulingClan?.Leader ?? sponsorClan?.Leader;
            if (rulingClan == null || ruler == null || ruler.IsDead) return false;

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return false;

            ShowPlayerTreasonJudgment(kingdom, rulingClan, ruler, targetClan, factionManager, RollTreasonExecution(ruler), true);
            return true;
        }

        public string ForceTreasonJudgmentForTesting(Kingdom kingdom, Clan targetClan, bool royalDecree)
        {
            if (kingdom == null) return "missing kingdom";
            if (targetClan == null) return "missing target clan";
            if (kingdom.RulingClan == null || kingdom.RulingClan.Leader == null) return "kingdom has no valid ruler";
            if (targetClan.Kingdom != kingdom) return $"{targetClan.Name} does not belong to {kingdom.Name}";
            if (targetClan == kingdom.RulingClan) return "the ruling clan cannot indict itself";
            if (targetClan.IsUnderMercenaryService) return "mercenary clans cannot be indicted for treason";
            if (targetClan.IsMinorFaction && targetClan != Clan.PlayerClan) return "minor clans cannot be indicted for treason";
            if (targetClan.IsEliminated || targetClan.Leader == null || targetClan.Leader.IsDead) return "target clan has no valid living leader";

            Clan rulingClan = kingdom.RulingClan;
            Hero ruler = rulingClan.Leader;
            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return "could not find FactionManagerBehavior";

            if (royalDecree)
            {
                RecordExpulsionVoteOutcome(kingdom, targetClan, true);
                if (targetClan == Clan.PlayerClan)
                    ShowPlayerTreasonJudgment(kingdom, rulingClan, ruler, targetClan, factionManager, RollTreasonExecution(ruler), false);
                else
                    ExecuteTreasonPurge(kingdom, rulingClan, ruler, targetClan, factionManager);

                return null;
            }

            var expDeliberation = Campaign.Current.GetCampaignBehavior<ExpulsionDeliberationBehavior>();
            // Explicit consequence-testing command, not an autonomous proposal source.
            bool queuedExpulsion = expDeliberation?.QueueExpulsionVote(kingdom, targetClan, rulingClan,
                CampaignTime.Now + CampaignTime.Days(O.PoliticalDeliberationDays)) ?? false;
            if (!queuedExpulsion)
            {
                AddDecisionAsModAction(kingdom, new ExpelClanFromKingdomDecision(rulingClan, targetClan));
            }

            return null;
        }

        public string ForceForeignPolicyMotion(Kingdom kingdom, FactionType ideologyType)
        {
            return "Bellum court foreign-policy motions are currently disabled; vanilla war and peace proposal behavior is active.";
        }

        private static bool RollTreasonExecution(Hero ruler)
        {
            if (ruler == null) return false;

            int honor = ruler.GetTraitLevel(DefaultTraits.Honor);
            int mercy = ruler.GetTraitLevel(DefaultTraits.Mercy);
            float executionChance = 0.4f;
            if (mercy <= -2) executionChance += 0.4f; else if (mercy == -1) executionChance += 0.2f; else if (mercy == 1) executionChance -= 0.2f; else if (mercy >= 2) executionChance -= 0.4f;
            if (honor <= -1) executionChance += 0.2f; else if (honor >= 1) executionChance -= 0.2f;
            return MBRandom.RandomFloat <= MathF.Clamp(executionChance, 0f, 1f);
        }

        internal bool CanScheduleTreasonDecree(Kingdom realm, Clan target) => realm?.RulingClan?.Leader?.IsAlive == true
            && target != realm.RulingClan && CourtAgendaBehavior.Eligible(target, realm)
            && target.Leader.IsAlive && target.Leader.GetRelation(realm.RulingClan.Leader) <= C.TreasonDecreeRelationThreshold;

        internal bool CanIssueTreasonDecree(Kingdom realm, Clan target) => CanScheduleTreasonDecree(realm, target)
            && !HasActiveCrownRebellion(realm, Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>());

        internal bool TryIssuePlayerTreasonDecree(Kingdom realm, Clan target)
        {
            if (realm?.RulingClan != Clan.PlayerClan || !CanIssueTreasonDecree(realm, target)
                || !CanRulerIndictClan(realm, target, true, out _)) return false;
            IssueScheduledTreasonDecree(realm, target);
            return true;
        }

        internal void IssueScheduledTreasonDecree(Kingdom realm, Clan target)
        {
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            RecordExpulsionVoteOutcome(realm, target, true);
            if (target == Clan.PlayerClan)
                ShowPlayerTreasonJudgment(realm, realm.RulingClan, realm.RulingClan.Leader, target, manager,
                    RollTreasonExecution(realm.RulingClan.Leader), false);
            else ExecuteTreasonPurge(realm, realm.RulingClan, realm.RulingClan.Leader, target, manager);
        }

        private void ShowPlayerTreasonJudgment(Kingdom kingdom, Clan rulingClan, Hero ruler, Clan targetClan, FactionManagerBehavior factionManager, bool execute, bool fromVote)
        {
            if (kingdom == null || rulingClan == null || ruler == null || targetClan != Clan.PlayerClan)
                return;

            TextObject title = execute
                ? new TextObject(fromVote ? "{=BC_Treason_PlayerJudgmentVoteExecute_Title}Council Judgment of High Treason" : "{=BC_Treason_PlayerJudgmentDecreeExecute_Title}Royal Decree of High Treason")
                : new TextObject(fromVote ? "{=BC_Treason_PlayerJudgmentVote_Title}Council Judgment of Treason" : "{=BC_Treason_PlayerJudgmentDecree_Title}Royal Decree of Treason");

            EstimateTreasonDefianceBalance(kingdom, targetClan, factionManager, out float rebelPower, out float loyalistPower);
            TextObject balanceReport = ConflictBalanceReportHelper.Build(
                ConflictBalanceReportHelper.BuildLeaderSupportersSideName(targetClan.Leader?.Name ?? targetClan.Name),
                rebelPower,
                ConflictBalanceReportHelper.CrownLoyalistsSideName,
                loyalistPower,
                playerUncommitted: false);

            TextObject desc = execute
                ? new TextObject(fromVote
                    ? "{=BC_Treason_PlayerJudgmentVoteExecute_Desc}The council has sustained the crown's charge against the {CLAN_NAME}. By {RULER_NAME}'s will, your lands are forfeit and your life is demanded for high treason.\n\n{BALANCE_REPORT}\n\nWill you submit to judgment, or defy the crown and raise your banners in rebellion?"
                    : "{=BC_Treason_PlayerJudgmentDecreeExecute_Desc}{RULER_NAME} has decreed that the {CLAN_NAME} are guilty of high treason. Your lands are forfeit and your life is demanded by royal justice.\n\n{BALANCE_REPORT}\n\nWill you submit to judgment, or defy the crown and raise your banners in rebellion?")
                : new TextObject(fromVote
                    ? "{=BC_Treason_PlayerJudgmentVote_Desc}The council has sustained the crown's charge against the {CLAN_NAME}. By {RULER_NAME}'s will, your lands are forfeit and your clan is cast out from the realm.\n\n{BALANCE_REPORT}\n\nWill you submit to judgment, or defy the crown and raise your banners in rebellion?"
                    : "{=BC_Treason_PlayerJudgmentDecree_Desc}{RULER_NAME} has decreed that the {CLAN_NAME} are guilty of treason. Your lands are forfeit and your clan is cast out from the realm.\n\n{BALANCE_REPORT}\n\nWill you submit to judgment, or defy the crown and raise your banners in rebellion?");

            desc.SetTextVariable("CLAN_NAME", targetClan.Name);
            desc.SetTextVariable("RULER_NAME", ruler.Name);
            desc.SetTextVariable("BALANCE_REPORT", balanceReport);

            InquiryData inquiry = new InquiryData(
                title.ToString(),
                desc.ToString(),
                true,
                true,
                new TextObject("{=BC_Treason_PlayerJudgmentSubmit}Submit to Judgment").ToString(),
                new TextObject("{=BC_Treason_PlayerJudgmentDefy}Defy the Crown").ToString(),
                () => QueuePlayerTreasonJudgmentAction(() => SubmitPlayerTreasonJudgment(kingdom, rulingClan, ruler, targetClan, execute)),
                () => QueuePlayerTreasonJudgmentAction(() => DefyPlayerTreasonJudgment(kingdom, targetClan, factionManager)));

            InformationManager.ShowInquiry(inquiry, true);
        }

        private void QueuePlayerTreasonJudgmentAction(Action action)
        {
            _pendingPlayerTreasonJudgmentAction = action;
        }

        private void OnTick(float dt)
        {
            Action pendingAction = _pendingPlayerTreasonJudgmentAction;
            if (pendingAction == null)
                return;

            _pendingPlayerTreasonJudgmentAction = null;
            pendingAction();
        }

        private static void ExecuteTreasonSentence(Kingdom sentencingRealm, Hero victim, Hero ruler)
        {
            bool nobleSentence = victim?.IsAlive == true && victim.Clan != null && !victim.Clan.IsMinorFaction;
            try
            {
                KillCharacterAction.ApplyByExecution(victim, ruler, true);
            }
            finally
            {
                // The player clan may already be exiled; the sentencing realm remains authoritative.
                if (nobleSentence && victim.IsDead)
                    Campaign.Current.GetCampaignBehavior<IdeologyEventShockBehavior>()?
                        .RecordRoyalExecution(sentencingRealm, victim);
            }
        }

        private void SubmitPlayerTreasonJudgment(Kingdom kingdom, Clan rulingClan, Hero ruler, Clan targetClan, bool execute)
        {
            if (kingdom == null || rulingClan == null || ruler == null || targetClan != Clan.PlayerClan)
                return;

            Hero targetLeader = targetClan.Leader;

            FiefDeliberationBehavior fiefDeliberation = Campaign.Current?.GetCampaignBehavior<FiefDeliberationBehavior>();
            foreach (Town fief in targetClan.Fiefs.ToList())
            {
                Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>()?.RecordAllocationCustody(fief.Settlement, rulingClan);
                ChangeOwnerOfSettlementAction.ApplyByDefault(ruler, fief.Settlement);
                fiefDeliberation?.QueueConfiscatedSettlementVote(
                    fief.Settlement,
                    kingdom,
                    rulingClan,
                    targetClan);
            }

            (FeudalTitleBehavior.Instance ?? Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>())
                ?.ConfiscateNonBaronyTitlesForExile(targetClan, rulingClan, kingdom, "player treason judgment submitted");

            if (targetLeader != null && !execute)
                ExpulsionRelationHelper.ApplyFriendMemories(kingdom, rulingClan, ruler, targetClan);

            if (targetClan.Kingdom == kingdom)
                ChangeKingdomAction.ApplyByLeaveKingdom(targetClan, false);

            if (execute && targetLeader != null && !targetLeader.IsDead)
                ExecuteTreasonSentence(kingdom, targetLeader, ruler);

            TextObject msg = execute && targetLeader?.IsDead == true
                ? new TextObject("{=BC_Treason_PlayerJudgmentSubmittedExecute}You submit to the crown's judgment. Your lands are forfeit, and royal justice claims your life.")
                : new TextObject("{=BC_Treason_PlayerJudgmentSubmitted}You submit to the crown's judgment. Your lands are forfeit, and your clan is cast out from the realm.");
            BellumCivileNotifications.ShowPersonal(msg, BellumNotificationColors.Danger);
        }

        private void DefyPlayerTreasonJudgment(Kingdom kingdom, Clan targetClan, FactionManagerBehavior factionManager)
        {
            if (kingdom == null || targetClan != Clan.PlayerClan || factionManager == null)
                return;

            FactionObject rebelFaction = CreateExpelledLordRebelFaction(targetClan, kingdom, factionManager);
            if (rebelFaction == null)
                return;

            TextObject text = new TextObject("{=BC_Ideology_DefiantRebellion}Defiant to the last, {LEADER_NAME} of {CLAN_NAME} refuses to accept the crown's judgment, they have raised their banners against {KINGDOM_NAME}!");
            text.SetTextVariable("LEADER_NAME", targetClan.Leader?.Name ?? Hero.MainHero?.Name);
            text.SetTextVariable("CLAN_NAME", targetClan.Name);
            text.SetTextVariable("KINGDOM_NAME", kingdom.Name);
            UltimatumResolution result = StartTreasonRebellion(
                rebelFaction,
                kingdom,
                targetClan,
                factionManager,
                text,
                personalNotification: true);
            if (result == UltimatumResolution.Failed
                && !rebelFaction.HasTrackedRebelKingdom
                && targetClan.Kingdom == kingdom)
            {
                factionManager.RemoveFaction(rebelFaction);
            }
        }

        private void ExecuteTreasonPurge(Kingdom kingdom, Clan rulingClan, Hero ruler, Clan targetClan, FactionManagerBehavior factionManager)
        {
            Hero targetLeader = targetClan.Leader;
            string targetClanName = targetClan.Name.ToString();

            FactionObject ledFaction = factionManager.GetFactionsInKingdom(kingdom).FirstOrDefault(f => f.Leader == targetClan && !f.IsIdeology);
            if (ledFaction != null)
            {
                TextObject text = new TextObject("{=BC_Ideology_RebellionInsteadOfExecution}Rather than face the executioner, {LEADER_NAME} of {CLAN_NAME} has raised their banners in open rebellion!");
                text.SetTextVariable("LEADER_NAME", targetLeader.Name);
                text.SetTextVariable("CLAN_NAME", targetClanName);
                UltimatumResolution result = StartTreasonRebellion(
                    ledFaction,
                    kingdom,
                    targetClan,
                    factionManager,
                    text,
                    personalNotification: false);
                if (result != UltimatumResolution.Failed)
                    return;
            }

            if (TryExpelledLordRebellion(targetClan, kingdom, triggerImmediately: true))
                return;

            FiefDeliberationBehavior fiefDeliberation = Campaign.Current?.GetCampaignBehavior<FiefDeliberationBehavior>();
            foreach (var fief in targetClan.Fiefs.ToList())
            {
                Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>()?.RecordAllocationCustody(fief.Settlement, rulingClan);
                ChangeOwnerOfSettlementAction.ApplyByDefault(ruler, fief.Settlement);
                fiefDeliberation?.QueueConfiscatedSettlementVote(
                    fief.Settlement,
                    kingdom,
                    rulingClan,
                    targetClan);
            }

            (FeudalTitleBehavior.Instance ?? Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>())
                ?.ConfiscateNonBaronyTitlesForExile(targetClan, rulingClan, kingdom, "treason purge");

            bool execute = RollTreasonExecution(ruler);

            if (!execute)
                ExpulsionRelationHelper.ApplyFriendMemories(kingdom, rulingClan, ruler, targetClan);

            if (execute)
                ExecuteTreasonSentence(kingdom, targetLeader, ruler);

            if (execute && targetLeader.IsDead)
            {
                TextObject text = new TextObject("{=BC_Ideology_ExecutedTreason}By order of {RULER_NAME}, {LEADER_NAME} of {CLAN_NAME} has been indicted for treason. The Lord has been stripped of all his properties and executed, his remaining relatives have fled for their lives.");
                text.SetTextVariable("RULER_NAME", ruler.Name);
                text.SetTextVariable("LEADER_NAME", targetLeader.Name);
                text.SetTextVariable("CLAN_NAME", targetClanName);
                BellumCivileNotifications.Show(text, BellumNotificationColors.Danger, primaryKingdom: kingdom, primaryClan: rulingClan, secondaryClan: targetClan);
            }
            else
            {
                TextObject text = new TextObject("{=BC_Ideology_ExiledTreason}By order of {RULER_NAME}, {LEADER_NAME} of {CLAN_NAME} has been indicted for treason. The Lord has been stripped of all his properties, and has fled for his life.");
                text.SetTextVariable("RULER_NAME", ruler.Name);
                text.SetTextVariable("LEADER_NAME", targetLeader.Name);
                text.SetTextVariable("CLAN_NAME", targetClanName);
                BellumCivileNotifications.Show(text, BellumNotificationColors.Danger, primaryKingdom: kingdom, primaryClan: rulingClan, secondaryClan: targetClan);
            }

            if (ExiledClanRecoveryBehavior.CanClanBeRelocated(targetClan))
            {
                ExiledClanRecoveryBehavior exileRecovery = Campaign.Current.GetCampaignBehavior<ExiledClanRecoveryBehavior>();
                if (exileRecovery != null)
                    exileRecovery.ResolveClanExile(targetClan, kingdom, null, ExileCause.Treason);
                else
                {
                    Kingdom foreignRefuge = RefugeSelectionHelper.FindBestRefuge(targetClan, kingdom);
                    if (foreignRefuge != null)
                        ChangeKingdomAction.ApplyByJoinToKingdom(targetClan, foreignRefuge);
                    else
                        ChangeKingdomAction.ApplyByLeaveKingdom(targetClan, false);
                }
            }
        }

        public bool TryResolveLegalExpulsionRebellion(Kingdom kingdom, Clan sponsorClan, Clan targetClan)
        {
            return TryResolveLegalExpulsionRebellionDetailed(kingdom, sponsorClan, targetClan)
                != UltimatumResolution.Failed;
        }

        public UltimatumResolution TryResolveLegalExpulsionRebellionDetailed(
            Kingdom kingdom,
            Clan sponsorClan,
            Clan targetClan,
            Action<UltimatumResolution> deferredCompletion = null)
        {
            if (kingdom == null || targetClan == null || sponsorClan == null)
                return UltimatumResolution.Failed;
            if (targetClan.Leader == null || targetClan.Leader.IsDead)
                return UltimatumResolution.Failed;
            if (targetClan.Kingdom != kingdom)
                return UltimatumResolution.Failed;

            string outcomeKey = GetExpulsionOutcomeKey(kingdom, targetClan);
            if (_expulsionOutcomeLocks.TryGetValue(outcomeKey, out CampaignTime outcomeLock) && outcomeLock.IsFuture)
                return UltimatumResolution.Failed;

            _expulsionOutcomeLocks[outcomeKey] = CampaignTime.Now + CampaignTime.Days(1f);

            FactionManagerBehavior factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null)
            {
                _expulsionOutcomeLocks.Remove(outcomeKey);
                return UltimatumResolution.Failed;
            }

            FactionObject rebelFaction = factionManager.GetFactionsInKingdom(kingdom)
                .FirstOrDefault(f => !f.IsIdeology && f.Leader == targetClan);
            bool createdForExpulsion = false;

            if (rebelFaction == null)
            {
                if (!ShouldExpelledLordRebel(targetClan, kingdom))
                {
                    _expulsionOutcomeLocks.Remove(outcomeKey);
                    return UltimatumResolution.Failed;
                }

                rebelFaction = CreateExpelledLordRebelFaction(targetClan, kingdom, factionManager);
                createdForExpulsion = rebelFaction != null;
            }

            if (rebelFaction == null)
            {
                CleanupFailedLegalExpulsionRebellion(
                    factionManager,
                    rebelFaction,
                    targetClan,
                    kingdom,
                    outcomeKey,
                    createdForExpulsion,
                    "no rebellion faction was available");
                return UltimatumResolution.Failed;
            }

            if (factionManager.TryReconcileRebelKingdom(
                    rebelFaction,
                    out Kingdom existingRebelKingdom,
                    out string reconciliationFailure))
            {
                BellumCivileLogger.Log(
                    $"Legal expulsion recognized an existing active rebellion; kingdom={kingdom.StringId}; target={targetClan.StringId}; faction={rebelFaction.Name}; rebel={existingRebelKingdom?.StringId ?? "none"}.");
                return UltimatumResolution.RebellionStarted;
            }

            if (!string.IsNullOrEmpty(reconciliationFailure))
            {
                CleanupFailedLegalExpulsionRebellion(
                    factionManager,
                    rebelFaction,
                    targetClan,
                    kingdom,
                    outcomeKey,
                    createdForExpulsion,
                    reconciliationFailure);
                return UltimatumResolution.Failed;
            }

            if (!rebelFaction.CanTriggerUltimatum(out _))
            {
                CleanupFailedLegalExpulsionRebellion(
                    factionManager,
                    rebelFaction,
                    targetClan,
                    kingdom,
                    outcomeKey,
                    createdForExpulsion,
                    "ultimatum preflight failed");
                return UltimatumResolution.Failed;
            }

            RallyTreasonSolidaritySupporters(rebelFaction, kingdom, targetClan, factionManager);
            UltimatumResolution result = rebelFaction.TriggerUltimatumDetailed(
                null,
                suppressAutomaticPlayerChoice: true,
                deferredResult =>
                {
                    UltimatumResolution verified = FinalizeLegalExpulsionRebellion(
                        deferredResult,
                        rebelFaction,
                        kingdom,
                        targetClan,
                        factionManager,
                        outcomeKey,
                        createdForExpulsion);
                    deferredCompletion?.Invoke(verified);
                });

            if (result == UltimatumResolution.PendingPlayerChoice)
            {
                BellumCivileLogger.Log(
                    $"Legal expulsion rebellion is pending player-ruler judgment; kingdom={kingdom.StringId}; target={targetClan.StringId}; faction={rebelFaction.Name}.");
                return result;
            }

            return FinalizeLegalExpulsionRebellion(
                result,
                rebelFaction,
                kingdom,
                targetClan,
                factionManager,
                outcomeKey,
                createdForExpulsion);
        }

        private UltimatumResolution FinalizeLegalExpulsionRebellion(
            UltimatumResolution result,
            FactionObject rebelFaction,
            Kingdom kingdom,
            Clan targetClan,
            FactionManagerBehavior factionManager,
            string outcomeKey,
            bool createdForExpulsion)
        {
            if (result == UltimatumResolution.RebellionStarted)
            {
                Kingdom rebelKingdom = null;
                string validationFailure = rebelFaction == null ? "the rebellion faction is missing" : null;
                if (rebelFaction == null
                    || !rebelFaction.TryValidateActiveRebellion(out rebelKingdom, out validationFailure))
                {
                    CleanupFailedLegalExpulsionRebellion(
                        factionManager,
                        rebelFaction,
                        targetClan,
                        kingdom,
                        outcomeKey,
                        createdForExpulsion,
                        "rebellion postcondition failed: " + (validationFailure ?? "unknown"));
                    return UltimatumResolution.Failed;
                }

                TextObject text = new TextObject("{=BC_Ideology_DefiantRebellion}Defiant to the last, {LEADER_NAME} of {CLAN_NAME} refuses to accept the crown's judgment, they have raised their banners against {KINGDOM_NAME}!");
                text.SetTextVariable("LEADER_NAME", targetClan.Leader.Name);
                text.SetTextVariable("CLAN_NAME", targetClan.Name);
                text.SetTextVariable("KINGDOM_NAME", kingdom.Name);
                BellumCivileNotifications.Show(
                    text,
                    BellumNotificationColors.Rebellion,
                    primaryKingdom: kingdom,
                    secondaryKingdom: rebelKingdom,
                    primaryClan: targetClan);
                NotificationHelper.ShowTreasonSolidarityChoiceToPlayer(rebelFaction, kingdom, targetClan);
            }
            else if (result == UltimatumResolution.Failed)
            {
                CleanupFailedLegalExpulsionRebellion(
                    factionManager,
                    rebelFaction,
                    targetClan,
                    kingdom,
                    outcomeKey,
                    createdForExpulsion,
                    "ultimatum resolution returned Failed");
            }

            BellumCivileLogger.Log(
                $"Legal expulsion custom resolution completed; kingdom={kingdom?.StringId ?? "none"}; target={targetClan?.StringId ?? "none"}; result={result}.");
            return result;
        }

        private void CleanupFailedLegalExpulsionRebellion(
            FactionManagerBehavior factionManager,
            FactionObject rebelFaction,
            Clan targetClan,
            Kingdom kingdom,
            string outcomeKey,
            bool createdForExpulsion,
            string reason)
        {
            _expulsionOutcomeLocks.Remove(outcomeKey);
            bool hasPartialRebellionState = rebelFaction != null
                && (rebelFaction.HasTrackedRebelKingdom || targetClan?.Kingdom != kingdom);

            if (createdForExpulsion && rebelFaction != null && !hasPartialRebellionState)
                factionManager?.RemoveFaction(rebelFaction);

            BellumCivileLogger.Log(
                $"Legal expulsion custom resolution failed; kingdom={kingdom?.StringId ?? "none"}; target={targetClan?.StringId ?? "none"}; faction={rebelFaction?.Name ?? "none"}; partialState={hasPartialRebellionState}; reason={reason ?? "unknown"}. Vanilla expulsion fallback is required.");
        }

        private bool ShouldExpelledLordRebel(Clan targetClan, Kingdom originalKingdom)
        {
            if (targetClan == null || targetClan.Leader == null || targetClan.Leader.IsDead) return false;
            if (originalKingdom == null || targetClan.Kingdom != originalKingdom) return false;

            Hero targetLeader = targetClan.Leader;
            float rebelChance = C.ExpelRebelBaseChance;

            int honor = targetLeader.GetTraitLevel(DefaultTraits.Honor);
            if (honor >= 2)       rebelChance -= C.ExpelRebelHonorHighPenalty;
            else if (honor == 1)  rebelChance -= C.ExpelRebelHonorMidPenalty;
            else if (honor == -1) rebelChance += C.ExpelRebelDisgraceMidBonus;
            else if (honor <= -2) rebelChance += C.ExpelRebelDisgraceHighBonus;

            int valor = targetLeader.GetTraitLevel(DefaultTraits.Valor);
            if (valor >= 2)       rebelChance += C.ExpelRebelValorHighBonus;
            else if (valor == 1)  rebelChance += C.ExpelRebelValorMidBonus;
            else if (valor == -1) rebelChance -= C.ExpelRebelCowMidPenalty;
            else if (valor <= -2) rebelChance -= C.ExpelRebelCowHighPenalty;

            int calc = targetLeader.GetTraitLevel(DefaultTraits.Calculating);
            if (calc <= -2)      rebelChance += C.ExpelRebelHotheadHighBonus;
            else if (calc == -1) rebelChance += C.ExpelRebelHotheadMidBonus;
            else if (calc >= 1)
            {
                float ownPower = RebellionPowerHelper.CalculateClanPower(targetClan);
                float loyalistPower = originalKingdom.Clans
                    .Where(c => !c.IsEliminated)
                    .Sum(RebellionPowerHelper.CalculateClanPower);
                if (ownPower < loyalistPower * C.ExpelRebelCalcPowerGate) return false;
            }

            rebelChance += CalculateTreasonSolidarityRebellionBonus(originalKingdom, targetClan);
            rebelChance = MathF.Clamp(rebelChance, 0f, 1f);
            return MBRandom.RandomFloat <= rebelChance;
        }

        private FactionObject CreateExpelledLordRebelFaction(Clan targetClan, Kingdom originalKingdom, FactionManagerBehavior factionManager)
        {
            if (targetClan == null || originalKingdom == null || factionManager == null) return null;
            if (targetClan.Kingdom != originalKingdom) return null;

            FactionObject existingFaction = factionManager.GetFactionsInKingdom(originalKingdom)
                .FirstOrDefault(f => !f.IsIdeology && f.Leader == targetClan);
            if (existingFaction != null) return existingFaction;

            string factionName = $"{targetClan.Name} Secessionists";
            FactionObject rebelFaction = new FactionObject(factionName, originalKingdom, targetClan, FactionType.Independence);
            factionManager.RegisterNewFaction(rebelFaction);
            return rebelFaction;
        }

        public bool TryExpelledLordRebellion(Clan targetClan, Kingdom originalKingdom, bool triggerImmediately = false)
        {
            if (targetClan == null || targetClan.Leader == null || targetClan.Leader.IsDead) return false;
            if (originalKingdom == null || targetClan.Kingdom != originalKingdom) return false;

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return false;

            FactionObject existingFaction = factionManager.GetFactionsInKingdom(originalKingdom)
                .FirstOrDefault(f => f.Leader == targetClan && !f.IsIdeology);
            if (existingFaction != null) return false;
            if (!ShouldExpelledLordRebel(targetClan, originalKingdom)) return false;

            FactionObject rebelFaction = CreateExpelledLordRebelFaction(targetClan, originalKingdom, factionManager);
            if (rebelFaction == null) return false;

            if (triggerImmediately)
            {
                TextObject text = new TextObject("{=BC_Ideology_DefiantRebellion}Defiant to the last, {LEADER_NAME} of {CLAN_NAME} refuses to accept the crown's judgment, they have raised their banners against {KINGDOM_NAME}!");
                text.SetTextVariable("LEADER_NAME", targetClan.Leader.Name);
                text.SetTextVariable("CLAN_NAME", targetClan.Name);
                text.SetTextVariable("KINGDOM_NAME", originalKingdom.Name);

                Action<UltimatumResolution> deferredCleanup = deferredResult =>
                {
                    if (deferredResult == UltimatumResolution.Failed
                        && !rebelFaction.HasTrackedRebelKingdom
                        && targetClan.Kingdom == originalKingdom)
                    {
                        factionManager.RemoveFaction(rebelFaction);
                    }
                };
                UltimatumResolution result = StartTreasonRebellion(
                    rebelFaction,
                    originalKingdom,
                    targetClan,
                    factionManager,
                    text,
                    personalNotification: targetClan == Clan.PlayerClan,
                    deferredCompletion: deferredCleanup);
                if (result == UltimatumResolution.Failed)
                {
                    deferredCleanup(result);
                    return false;
                }
            }

            return true;
        }

        internal UltimatumResolution StartTreasonRebellion(
            FactionObject rebelFaction,
            Kingdom kingdom,
            Clan targetClan,
            FactionManagerBehavior factionManager,
            TextObject startMessage,
            bool personalNotification,
            Action<UltimatumResolution> deferredCompletion = null)
        {
            RallyTreasonSolidaritySupporters(rebelFaction, kingdom, targetClan, factionManager);
            UltimatumResolution result = rebelFaction.TriggerUltimatumDetailed(
                null,
                suppressAutomaticPlayerChoice: true,
                deferredResult =>
                {
                    UltimatumResolution verified = FinalizeTreasonRebellion(
                        deferredResult,
                        rebelFaction,
                        kingdom,
                        targetClan,
                        startMessage,
                        personalNotification);
                    deferredCompletion?.Invoke(verified);
                });

            if (result == UltimatumResolution.PendingPlayerChoice)
                return result;

            return FinalizeTreasonRebellion(
                result,
                rebelFaction,
                kingdom,
                targetClan,
                startMessage,
                personalNotification);
        }

        private static UltimatumResolution FinalizeTreasonRebellion(
            UltimatumResolution result,
            FactionObject rebelFaction,
            Kingdom kingdom,
            Clan targetClan,
            TextObject startMessage,
            bool personalNotification)
        {
            if (result != UltimatumResolution.RebellionStarted)
                return result;

            Kingdom rebelKingdom = null;
            string failureReason = rebelFaction == null ? "missing faction" : null;
            if (rebelFaction == null
                || !rebelFaction.TryValidateActiveRebellion(out rebelKingdom, out failureReason))
            {
                BellumCivileLogger.Log(
                    $"Treason rebellion failed postcondition validation; kingdom={kingdom?.StringId ?? "none"}; target={targetClan?.StringId ?? "none"}; faction={rebelFaction?.Name ?? "none"}; reason={failureReason ?? "missing faction"}.");
                return UltimatumResolution.Failed;
            }

            if (startMessage != null
                && (Clan.PlayerClan?.Kingdom == kingdom || targetClan == Clan.PlayerClan || personalNotification))
            {
                BellumCivileNotifications.Show(
                    startMessage,
                    BellumNotificationColors.Rebellion,
                    primaryKingdom: kingdom,
                    secondaryKingdom: rebelKingdom,
                    primaryClan: targetClan,
                    isPersonal: personalNotification);
            }

            NotificationHelper.ShowTreasonSolidarityChoiceToPlayer(rebelFaction, kingdom, targetClan);
            return result;
        }

        private void EstimateTreasonDefianceBalance(
            Kingdom kingdom,
            Clan accusedClan,
            FactionManagerBehavior factionManager,
            out float rebelPower,
            out float loyalistPower)
        {
            rebelPower = RebellionPowerHelper.CalculateClanPower(accusedClan);
            loyalistPower = 0f;
            if (kingdom == null || accusedClan == null || factionManager == null)
                return;

            foreach (Clan clan in kingdom.Clans.Where(clan => clan != null && !clan.IsEliminated && clan != accusedClan))
            {
                float clanPower = RebellionPowerHelper.CalculateClanPower(clan);
                float expectedJoinChance = 0f;
                if (IsEligibleTreasonSolidaritySupporter(clan, accusedClan, kingdom, factionManager))
                {
                    FactionObject existingRebellion = factionManager.GetRebelFaction(clan);
                    if (existingRebellion == null || !existingRebellion.IsCivilWarActive())
                    {
                        float supportScore = CalculateTreasonSolidaritySupportScore(clan, accusedClan, kingdom, factionManager);
                        if (supportScore >= C.TreasonSolidarityJoinThreshold)
                            expectedJoinChance = 1f;
                        else if (supportScore >= C.TreasonSolidarityRollThreshold)
                            expectedJoinChance = MathF.Clamp(supportScore / 100f, 0f, 1f);
                    }
                }

                rebelPower += clanPower * expectedJoinChance;

                FactionObject ideology = factionManager.GetIdeologicalFaction(clan);
                float loyalistContribution = clan == kingdom.RulingClan
                    || clan.IsUnderMercenaryService
                    || clan.IsMinorFaction
                    || ideology == null
                        ? 1f
                        : RebellionPowerHelper.CalculateLoyalistContributionMultiplier(kingdom, clan, ideology);
                loyalistPower += clanPower * loyalistContribution * (1f - expectedJoinChance);
            }

            rebelPower = MathF.Max(0f, rebelPower);
            loyalistPower = MathF.Max(0f, loyalistPower);
        }

        public float CalculateTreasonSolidarityRebellionBonus(Kingdom kingdom, Clan accusedClan, FactionManagerBehavior factionManager = null)
        {
            if (kingdom == null || accusedClan == null)
                return 0f;

            factionManager = factionManager ?? Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null)
                return 0f;

            float supportScore = 0f;
            foreach (Clan supporter in kingdom.Clans.ToList())
            {
                if (!IsEligibleTreasonSolidaritySupporter(supporter, accusedClan, kingdom, factionManager))
                    continue;

                // Ideological sympathy may influence a personal call, but a shared
                // court faction alone does not oblige a clan to take up arms.
                if (!HasTreasonSolidarityTie(supporter, accusedClan))
                    continue;

                float score = CalculateTreasonSolidaritySupportScore(supporter, accusedClan, kingdom, factionManager);
                if (score >= C.TreasonSolidarityRollThreshold)
                    supportScore += score;
            }

            if (supportScore <= 0f)
                return 0f;

            return MathF.Min(C.TreasonSolidarityMaxRebelChanceBonus, supportScore / C.TreasonSolidarityRebelChanceScale);
        }

        public void RallyTreasonSolidaritySupporters(FactionObject rebelFaction, Kingdom kingdom, Clan accusedClan, FactionManagerBehavior factionManager = null)
        {
            if (rebelFaction == null || kingdom == null || accusedClan == null)
                return;

            factionManager = factionManager ?? Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null)
                return;

            foreach (Clan supporter in kingdom.Clans.ToList())
            {
                if (!IsEligibleTreasonSolidaritySupporter(supporter, accusedClan, kingdom, factionManager))
                    continue;

                float score = CalculateTreasonSolidaritySupportScore(supporter, accusedClan, kingdom, factionManager);
                if (score < C.TreasonSolidarityRollThreshold)
                    continue;

                bool joins = score >= C.TreasonSolidarityJoinThreshold || MBRandom.RandomFloat <= MathF.Clamp(score / 100f, 0f, 1f);
                if (!joins)
                    continue;

                FactionObject existingRebellion = factionManager.GetRebelFaction(supporter);
                if (existingRebellion != null && existingRebellion != rebelFaction)
                {
                    if (existingRebellion.IsCivilWarActive())
                        continue;

                    if (existingRebellion.Leader == supporter)
                        factionManager.RemoveFaction(existingRebellion);
                    else
                        existingRebellion.RemoveMember(supporter);
                }

                rebelFaction.AddMember(supporter);

                TextObject text = new TextObject("{=BC_Treason_SolidarityJoin}Bound by personal ties, {LEADER_NAME} of {CLAN_NAME} has joined {ACCUSED_NAME}'s defiance against the crown!");
                text.SetTextVariable("LEADER_NAME", supporter.Leader?.Name ?? supporter.Name);
                text.SetTextVariable("CLAN_NAME", supporter.Name);
                text.SetTextVariable("ACCUSED_NAME", accusedClan.Leader?.Name ?? accusedClan.Name);
                BellumCivileNotifications.Show(text, BellumNotificationColors.Rebellion, primaryKingdom: kingdom, primaryClan: supporter, secondaryClan: accusedClan);
                Campaign.Current?.GetCampaignBehavior<ConflictCallResponseBehavior>()
                    ?.QueueTreasonResponse(
                        rebelFaction,
                        supporter,
                        accusedClan,
                        DetermineTreasonSolidarityReason(supporter, accusedClan, factionManager));

                BellumCivileLogger.Log($"Treason solidarity supporter joined rebellion: kingdom={kingdom.StringId}; accused={accusedClan.StringId}; supporter={supporter.StringId}; score={score:0.0}; rebel_faction={rebelFaction.Name}.");
            }
        }

        private static bool IsEligibleTreasonSolidaritySupporter(Clan supporter, Clan accusedClan, Kingdom kingdom, FactionManagerBehavior factionManager)
        {
            if (supporter == null || accusedClan == null || kingdom == null || factionManager == null)
                return false;

            if (supporter == accusedClan || supporter == kingdom.RulingClan || supporter == Clan.PlayerClan)
                return false;

            if (supporter.Kingdom != kingdom || supporter.IsEliminated || supporter.IsMinorFaction || supporter.IsUnderMercenaryService)
                return false;

            if (supporter.Leader == null || supporter.Leader.IsDead || accusedClan.Leader == null || accusedClan.Leader.IsDead)
                return false;

            if (factionManager.IsClanPacified(supporter))
                return false;

            if (MarriageAllianceHelper.HasMarriageAlliance(supporter, kingdom.RulingClan))
                return false;

            Hero ruler = kingdom.RulingClan?.Leader;
            if (ruler != null && supporter.Leader.GetRelation(ruler) >= C.TreasonSolidarityRulerLoyalRelationThreshold)
                return false;

            return true;
        }

        private static float CalculateTreasonSolidaritySupportScore(Clan supporter, Clan accusedClan, Kingdom kingdom, FactionManagerBehavior factionManager)
        {
            float score = 0f;

            if (MarriageAllianceHelper.HasMarriageAlliance(supporter, accusedClan))
                score += C.TreasonSolidarityMarriageSupport;

            if (CivilWarSolidarityHelper.AreCloseDynasticKin(supporter, accusedClan))
                score += C.CivilWarSolidarityDynasticKinSupport;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (IsDirectTitleVassalOf(titleBehavior, supporter, accusedClan))
                score += C.TreasonSolidarityDirectVassalSupport;

            int relationWithAccused = supporter.Leader?.GetRelation(accusedClan.Leader) ?? 0;
            if (relationWithAccused >= C.TreasonSolidarityFriendRelationThreshold)
            {
                score += C.TreasonSolidarityFriendSupportBase
                    + ((relationWithAccused - C.TreasonSolidarityFriendRelationThreshold) * C.TreasonSolidarityFriendSupportScale);
            }

            FactionObject supporterIdeology = factionManager.GetIdeologicalFaction(supporter);
            FactionObject accusedIdeology = factionManager.GetIdeologicalFaction(accusedClan);
            if (supporterIdeology != null)
            {
                if (supporterIdeology == accusedIdeology)
                    score += C.TreasonSolidaritySameFactionSupport;

                if (supporterIdeology.Mood <= C.GrandCoalitionJoinMoodThreshold)
                    score += C.TreasonSolidarityRebelliousFactionSupport;
                else if (supporterIdeology.Mood <= C.MoodThresholdUnhappy)
                    score += C.TreasonSolidarityAngryFactionSupport;
            }

            return score;
        }

        private static bool HasTreasonSolidarityTie(Clan supporter, Clan accusedClan)
        {
            if (supporter?.Leader == null || accusedClan?.Leader == null)
                return false;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            return IsDirectTitleVassalOf(titleBehavior, supporter, accusedClan)
                || MarriageAllianceHelper.HasMarriageAlliance(supporter, accusedClan)
                || CivilWarSolidarityHelper.AreCloseDynasticKin(supporter, accusedClan)
                || supporter.Leader.GetRelation(accusedClan.Leader) >= C.TreasonSolidarityFriendRelationThreshold;
        }

        private static CivilWarSolidarityReason DetermineTreasonSolidarityReason(
            Clan supporter,
            Clan accusedClan,
            FactionManagerBehavior factionManager)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (IsDirectTitleVassalOf(titleBehavior, supporter, accusedClan))
                return CivilWarSolidarityReason.DirectVassal;
            if (MarriageAllianceHelper.HasMarriageAlliance(supporter, accusedClan))
                return CivilWarSolidarityReason.MarriageAlliance;
            if (CivilWarSolidarityHelper.AreCloseDynasticKin(supporter, accusedClan))
                return CivilWarSolidarityReason.DynasticKin;
            if ((supporter?.Leader?.GetRelation(accusedClan?.Leader) ?? 0) >= C.TreasonSolidarityFriendRelationThreshold)
                return CivilWarSolidarityReason.Friendship;
            return CivilWarSolidarityReason.RebelliousIntent;
        }

        private static bool IsDirectTitleVassalOf(FeudalTitleBehavior titleBehavior, Clan possibleVassal, Clan possibleLiege)
        {
            return CivilWarSolidarityHelper.IsImmediateVassalOf(titleBehavior, possibleVassal, possibleLiege);
        }
    }
}
