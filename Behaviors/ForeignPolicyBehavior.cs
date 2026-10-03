using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using BellumCivile.WarPeace;

namespace BellumCivile.Behaviors
{
    public sealed class ForeignPolicyBehavior : CampaignBehaviorBase
    {

        private const float PendingContextLifetimeDays = 30f;

        private List<ActiveForeignWarRecord> _activeWars = new List<ActiveForeignWarRecord>();
        private List<ActiveForeignWarRecord> _pendingWarContexts = new List<ActiveForeignWarRecord>();

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnMakePeace);
            CampaignEvents.KingdomDestroyedEvent.AddNonSerializedListener(this, OnKingdomDestroyed);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
            CampaignEvents.RulingClanChanged.AddNonSerializedListener(this, OnRulingClanChanged);
            CampaignEvents.KingdomDecisionConcluded.AddNonSerializedListener(this, OnKingdomDecisionConcluded);
            CampaignEvents.KingdomDecisionCancelled.AddNonSerializedListener(this, OnKingdomDecisionCancelled);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_ActiveForeignWars", ref _activeWars);
            dataStore.SyncData("BellumCivile_PendingForeignWarContexts", ref _pendingWarContexts);
            EnsureCollectionsInitialized();
        }

        public IReadOnlyCollection<ActiveForeignWarRecord> GetActiveWars()
        {
            EnsureCollectionsInitialized();
            return _activeWars.ToList();
        }

        public ActiveForeignWarRecord GetActiveWar(Kingdom firstKingdom, Kingdom secondKingdom)
        {
            if (firstKingdom == null || secondKingdom == null)
                return null;

            string key = BuildWarKey(firstKingdom.StringId, secondKingdom.StringId);
            return _activeWars.FirstOrDefault(record => record != null && record.WarKey == key);
        }

        internal ActiveForeignWarRecord GetWarDeclarationContext(Kingdom attacker, Kingdom defender)
        {
            if (attacker == null || defender == null)
                return null;

            EnsureCollectionsInitialized();
            string key = BuildWarKey(attacker.StringId, defender.StringId);
            return _activeWars.FirstOrDefault(record => IsMatchingDeclaration(record, key, attacker, defender))
                ?? _pendingWarContexts.FirstOrDefault(record => IsMatchingDeclaration(record, key, attacker, defender));
        }

        internal void CapturePendingWarDeclarationMotive(
            Kingdom attacker,
            Kingdom defender,
            Clan sponsorClan)
        {
            if (!IsValidKingdom(attacker) || !IsValidKingdom(defender))
                return;

            WarPeaceRevampBehavior revamp = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            Clan evaluator = sponsorClan?.Kingdom == attacker ? sponsorClan : attacker.RulingClan;
            WarTargetScore assessment = revamp?
                .GetRankedTargets(evaluator, forceRefresh: true)
                .FirstOrDefault(score => score.TargetKingdom == defender);
            WarTargetMotive motive = WarDeclarationReasonTextHelper.SelectPrimaryMotive(assessment);
            if (motive == null)
                return;

            EnsureCollectionsInitialized();
            string key = BuildWarKey(attacker.StringId, defender.StringId);
            ActiveForeignWarRecord record = _pendingWarContexts.FirstOrDefault(candidate =>
                IsMatchingDeclaration(candidate, key, attacker, defender));
            if (record == null)
            {
                record = CreateRecord(
                    attacker,
                    defender,
                    ForeignPolicyMotive.Unknown,
                    null,
                    evaluator?.StringId,
                    null,
                    startedDay: -1f);
                _pendingWarContexts.Add(record);
            }

            record.SetPublicDeclarationMotive(motive);
        }


        public ForeignPolicyObjectiveStatus GetObjectiveStatus(ActiveForeignWarRecord war)
        {
            ForeignPolicyObjectiveStatus status = new ForeignPolicyObjectiveStatus();
            if (war == null || war.TargetTitleIds == null || war.TargetTitleIds.Count == 0)
                return status;

            Kingdom beneficiary = Kingdom.All.FirstOrDefault(kingdom => kingdom != null
                && kingdom.StringId == war.AttackerKingdomId);
            FeudalTitleBehavior titleBehavior = FeudalTitleBehavior.Instance;
            if (beneficiary == null || titleBehavior == null)
                return status;

            foreach (string titleId in war.TargetTitleIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct())
            {
                status.TotalObjectives++;
                FeudalTitleRecord title = titleBehavior.GetTitle(titleId);
                if (IsTitlePhysicallyControlledByKingdom(titleBehavior, title, beneficiary))
                {
                    status.ControlledObjectives++;
                    status.ControlledTitleIds.Add(titleId);
                }
                else
                {
                    status.OutstandingTitleIds.Add(titleId);
                }
            }

            return status;
        }

        public ForeignPolicyObjectiveStatus GetObjectiveStatus(Kingdom firstKingdom, Kingdom secondKingdom)
        {
            return GetObjectiveStatus(GetActiveWar(firstKingdom, secondKingdom));
        }

        public void RegisterPendingWarContext(
            Kingdom attacker,
            Kingdom defender,
            ForeignPolicyMotive motive,
            FactionType? sponsorFactionType,
            Clan sponsorClan,
            IEnumerable<string> targetTitleIds)
        {
            if (!IsValidKingdom(attacker) || !IsValidKingdom(defender) || attacker == defender)
                return;

            EnsureCollectionsInitialized();
            string key = BuildWarKey(attacker.StringId, defender.StringId);
            _pendingWarContexts.RemoveAll(record => record == null || record.WarKey == key);
            _pendingWarContexts.Add(CreateRecord(
                attacker,
                defender,
                motive,
                sponsorFactionType?.ToString(),
                sponsorClan?.StringId,
                targetTitleIds,
                startedDay: -1f));
            ForeignPolicyEvaluationService.InvalidateCache();
        }

        public void ClearPendingWarContext(Kingdom attacker, Kingdom defender)
        {
            if (attacker == null || defender == null)
                return;

            EnsureCollectionsInitialized();
            string key = BuildWarKey(attacker.StringId, defender.StringId);
            _pendingWarContexts.RemoveAll(record => record == null
                || (record.WarKey == key
                    && record.AttackerKingdomId == attacker.StringId
                    && record.DefenderKingdomId == defender.StringId));
        }

        private static bool IsMatchingDeclaration(
            ActiveForeignWarRecord record,
            string key,
            Kingdom attacker,
            Kingdom defender)
        {
            return record != null
                && record.WarKey == key
                && record.AttackerKingdomId == attacker.StringId
                && record.DefenderKingdomId == defender.StringId;
        }

        private void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
        {
            ReconcileActiveWars();
        }

        private void OnDailyTick()
        {
            ForeignPolicyEvaluationService.InvalidateCache();
            EnsureCollectionsInitialized();
            float currentDay = CurrentDay;
            _pendingWarContexts.RemoveAll(record => record == null || currentDay - record.ContextCreatedDay > PendingContextLifetimeDays);

        }

        // Keep old console callers harmless; court objectives do not replace native diplomacy.
        public bool TryRunRulerForeignPolicyProposal(Kingdom kingdom, bool force, out string report)
        {
            report = "The legacy ruler foreign-policy scheduler has been retired. Court objectives are selected through court agendas; normal war and peace proposal behavior remains active. This command takes no action, including with force.";
            return false;
        }


        private void OnWarDeclared(IFaction firstFaction, IFaction secondFaction, DeclareWarAction.DeclareWarDetail detail)
        {
            Kingdom attacker = firstFaction as Kingdom;
            Kingdom defender = secondFaction as Kingdom;
            if (!IsValidKingdom(attacker) || !IsValidKingdom(defender))
                return;

            EnsureCollectionsInitialized();
            string key = BuildWarKey(attacker.StringId, defender.StringId);
            ActiveForeignWarRecord record = _pendingWarContexts.FirstOrDefault(candidate => candidate != null
                && candidate.WarKey == key
                && candidate.AttackerKingdomId == attacker.StringId
                && candidate.DefenderKingdomId == defender.StringId);
            _pendingWarContexts.RemoveAll(candidate => candidate == null
                || (candidate.WarKey == key
                    && candidate.AttackerKingdomId == attacker.StringId
                    && candidate.DefenderKingdomId == defender.StringId));

            if (record == null)
            {
                record = CreateRecord(attacker, defender, ForeignPolicyMotive.Unknown, null, null, null, CurrentDay);
            }
            else
            {
                record.MarkStarted(CurrentDay, GetKingdomStrength(attacker), GetKingdomStrength(defender));
            }

            _activeWars.RemoveAll(candidate => candidate == null || candidate.WarKey == key);
            _activeWars.Add(record);
            ForeignPolicyEvaluationService.InvalidateCache();
            BellumCivileLogger.Log($"Foreign war context activated; war={key}; attacker={attacker.StringId}; defender={defender.StringId}; motive={record.DeclaredMotive}; titles={record.TargetTitleIds.Count}.");
        }

        private void OnMakePeace(IFaction firstFaction, IFaction secondFaction, MakePeaceAction.MakePeaceDetail detail)
        {
            Kingdom firstKingdom = firstFaction as Kingdom;
            Kingdom secondKingdom = secondFaction as Kingdom;
            if (firstKingdom == null || secondKingdom == null)
                return;

            EnsureCollectionsInitialized();
            string key = BuildWarKey(firstKingdom.StringId, secondKingdom.StringId);
            _activeWars.RemoveAll(record => record == null || record.WarKey == key);
            _pendingWarContexts.RemoveAll(record => record == null || record.WarKey == key);
            ForeignPolicyEvaluationService.InvalidateCache();
        }

        private void OnKingdomDestroyed(Kingdom kingdom)
        {
            if (kingdom == null)
                return;

            EnsureCollectionsInitialized();
            string kingdomId = kingdom.StringId;
            _activeWars.RemoveAll(record => record == null || record.AttackerKingdomId == kingdomId || record.DefenderKingdomId == kingdomId);
            _pendingWarContexts.RemoveAll(record => record == null || record.AttackerKingdomId == kingdomId || record.DefenderKingdomId == kingdomId);
            ForeignPolicyEvaluationService.InvalidateCache();
        }

        private void OnClanChangedKingdom(
            Clan clan,
            Kingdom oldKingdom,
            Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail,
            bool showNotification)
        {
            ForeignPolicyEvaluationService.InvalidateCache();
        }

        private void OnRulingClanChanged(Kingdom kingdom, Clan newRulingClan)
        {
            ForeignPolicyEvaluationService.InvalidateCache();
        }

        private void OnKingdomDecisionConcluded(KingdomDecision decision, DecisionOutcome chosenOutcome, bool isPlayerInvolved)
        {
            if (!(decision is DeclareWarDecision warDecision)
                || !(warDecision.FactionToDeclareWarOn is Kingdom targetKingdom))
            {
                return;
            }

            bool approved = chosenOutcome is DeclareWarDecision.DeclareWarDecisionOutcome outcome
                && outcome.ShouldWarBeDeclared;
            if (!approved)
                ClearPendingWarContext(warDecision.Kingdom, targetKingdom);
        }

        private void OnKingdomDecisionCancelled(KingdomDecision decision, bool isPlayerInvolved)
        {
            if (decision is DeclareWarDecision warDecision
                && warDecision.FactionToDeclareWarOn is Kingdom targetKingdom)
            {
                ClearPendingWarContext(warDecision.Kingdom, targetKingdom);
            }
        }

        private static bool IsTitlePhysicallyControlledByKingdom(
            FeudalTitleBehavior titleBehavior,
            FeudalTitleRecord title,
            Kingdom kingdom)
        {
            if (titleBehavior == null || title == null || kingdom == null || !title.IsActive)
                return false;

            if (title.TitleType == FeudalTitleType.Barony)
                return ResolveClan(title.DeFactoHolderClanId)?.Kingdom == kingdom;

            List<FeudalTitleRecord> controlUnits = FeudalTitleUsurpationBehavior.GetControlUnits(titleBehavior, title);
            if (controlUnits.Count == 0)
                return ResolveClan(title.DeFactoHolderClanId)?.Kingdom == kingdom;

            int controlled = controlUnits.Count(unit => ResolveClan(unit.DeFactoHolderClanId)?.Kingdom == kingdom);
            return controlled >= FeudalTitleUsurpationAssessmentService.GetStrictMajorityCount(controlUnits.Count);
        }

        private static Clan ResolveClan(string clanId)
        {
            if (string.IsNullOrWhiteSpace(clanId))
                return null;
            return Clan.All.FirstOrDefault(clan => clan != null && clan.StringId == clanId);
        }

        private void ReconcileActiveWars()
        {
            EnsureCollectionsInitialized();
            List<Kingdom> kingdoms = Kingdom.All.Where(IsValidKingdom).ToList();
            HashSet<string> currentWarKeys = new HashSet<string>();

            for (int firstIndex = 0; firstIndex < kingdoms.Count; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < kingdoms.Count; secondIndex++)
                {
                    Kingdom firstKingdom = kingdoms[firstIndex];
                    Kingdom secondKingdom = kingdoms[secondIndex];
                    if (!firstKingdom.IsAtWarWith(secondKingdom))
                        continue;

                    string key = BuildWarKey(firstKingdom.StringId, secondKingdom.StringId);
                    currentWarKeys.Add(key);
                    if (_activeWars.Any(record => record != null && record.WarKey == key))
                        continue;

                    _activeWars.Add(CreateRecord(firstKingdom, secondKingdom, ForeignPolicyMotive.Unknown, null, null, null, CurrentDay));
                }
            }

            _activeWars.RemoveAll(record => record == null || !currentWarKeys.Contains(record.WarKey));
        }


        private static ActiveForeignWarRecord CreateRecord(
            Kingdom attacker,
            Kingdom defender,
            ForeignPolicyMotive motive,
            string sponsorFactionType,
            string sponsorClanId,
            IEnumerable<string> targetTitleIds,
            float startedDay)
        {
            return new ActiveForeignWarRecord(
                BuildWarKey(attacker.StringId, defender.StringId),
                attacker.StringId,
                defender.StringId,
                motive,
                sponsorFactionType,
                sponsorClanId,
                targetTitleIds,
                GetKingdomStrength(attacker),
                GetKingdomStrength(defender),
                startedDay,
                CurrentDay);
        }

        private static float GetKingdomStrength(Kingdom kingdom)
        {
            return kingdom?.CurrentTotalStrength ?? 0f;
        }

        private static bool IsValidKingdom(Kingdom kingdom)
        {
            return kingdom != null
                && !kingdom.IsEliminated
                && !string.IsNullOrWhiteSpace(kingdom.StringId)
                && kingdom.RulingClan != null;
        }

        public static string BuildWarKey(string firstKingdomId, string secondKingdomId)
        {
            string first = firstKingdomId ?? string.Empty;
            string second = secondKingdomId ?? string.Empty;
            return string.CompareOrdinal(first, second) <= 0
                ? first + "|" + second
                : second + "|" + first;
        }

        private static float CurrentDay => (float)CampaignTime.Now.ToDays;

        private void EnsureCollectionsInitialized()
        {
            if (_activeWars == null)
                _activeWars = new List<ActiveForeignWarRecord>();
            if (_pendingWarContexts == null)
                _pendingWarContexts = new List<ActiveForeignWarRecord>();
        }
    }
}
