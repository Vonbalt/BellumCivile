using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class PrivyCouncilBehavior : CampaignBehaviorBase
    {
        public const int AppointmentProposalInfluenceCost = 100;
        public const int DirectDismissalInfluenceCost = 100;
        public const float AssignmentCooldownDays = 30f;
        public const float VacancyGraceDays = 7f;
        public const float EmergencyVacancyDays = 30f;
        private const float VacancyControversyPerDay = 1f;
        private const float CaptiveCouncillorControversyPerDay = 0.5f;
        private const int FirstAdvisorStrongholdRequirement = 30;
        private const int BaseDailyCouncilSalary = 500;
        private const float DailyCouncilInfluence = 1f;
        private const float DisgraceDurationDays = 100f;
        private const float AssignmentReviewMinimumDays = 28f;
        private const float AssignmentReviewVarianceDays = 8f;
        private const float AssignmentInertia = 15f;
        private const float AssignmentSwitchMargin = 20f;
        private const int InfrastructureCostPerStronghold = 50;
        private const int ProvisionsCostPerStronghold = 30;
        private static readonly PrivyCouncilOffice[] AllOffices =
            (PrivyCouncilOffice[])Enum.GetValues(typeof(PrivyCouncilOffice));

        private sealed class CouncilOfficeRuntimeState
        {
            public PrivyCouncilOfficeRecord Record;
            public Clan Holder;
            public string HolderClanId;
            public PrivyCouncilAssignmentDefinition Assignment;
            public string AssignmentId;
            public float RawCompetence;
            public float EffectiveCompetence;
            public float EffectMultiplier;
            public float DrawbackMultiplier;
            public string FundingKey;
        }

        private sealed class CouncilRuntimeSnapshot
        {
            public int CampaignDay;
            public float CounselCrownCompetenceBonus;
            public readonly Dictionary<PrivyCouncilOffice, CouncilOfficeRuntimeState> Offices =
                new Dictionary<PrivyCouncilOffice, CouncilOfficeRuntimeState>();
        }

        private sealed class PendingPlayerCouncilDismissal
        {
            public PendingPlayerCouncilDismissal(string title, string body)
            {
                Title = title;
                Body = body;
            }

            public string Title { get; }
            public string Body { get; }
        }

        private List<PrivyCouncilOfficeRecord> _officeRecords = new List<PrivyCouncilOfficeRecord>();
        private Dictionary<string, float> _disgracedUntilByClan = new Dictionary<string, float>();
        private Dictionary<string, float> _disgraceIntentByClan = new Dictionary<string, float>();
        private Dictionary<string, float> _captivityDismissalUntilByClan = new Dictionary<string, float>();
        private Dictionary<string, float> _assignmentRelationProgressByKingdom = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _fundedAssignmentUntilDayByKingdom = new Dictionary<string, float>();
        private readonly Dictionary<string, List<PrivyCouncilOfficeRecord>> _recordsByKingdomId =
            new Dictionary<string, List<PrivyCouncilOfficeRecord>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<PrivyCouncilOffice, PrivyCouncilOfficeRecord>> _recordByOfficeByKingdomId =
            new Dictionary<string, Dictionary<PrivyCouncilOffice, PrivyCouncilOfficeRecord>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Clan> _clanById =
            new Dictionary<string, Clan>(StringComparer.Ordinal);
        private readonly Dictionary<string, CouncilRuntimeSnapshot> _runtimeSnapshotsByKingdomId =
            new Dictionary<string, CouncilRuntimeSnapshot>(StringComparer.Ordinal);
        private readonly HashSet<string> _runtimeUnavailableKingdomIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<PendingPlayerCouncilDismissal> _pendingPlayerDismissals =
            new Queue<PendingPlayerCouncilDismissal>();
        private bool _recordIndexesDirty = true;
        private bool _isShowingPlayerDismissal;
        private CouncilIncidentBehavior _incidentBehavior;

        public override void RegisterEvents()
        {
            CouncilAssignmentRuntimePatches.BindCouncilBehavior(this);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.RulingClanChanged.AddNonSerializedListener(this, OnRulingClanChanged);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BC_CouncilSalaryCredits", ref _salaryCredits);
            dataStore.SyncData("BC_CouncilPayrollDay", ref _payrollDay);
            _salaryCredits = _salaryCredits ?? new List<CouncilSalaryCredit>();
            _payrollDay = _payrollDay ?? new Dictionary<string, float>();
            dataStore.SyncData("BellumCivile_PrivyCouncil_Offices", ref _officeRecords);
            dataStore.SyncData("BellumCivile_PrivyCouncil_DisgracedUntil", ref _disgracedUntilByClan);
            dataStore.SyncData("BellumCivile_PrivyCouncil_DisgraceIntent", ref _disgraceIntentByClan);
            dataStore.SyncData("BellumCivile_PrivyCouncil_CaptivityDismissals", ref _captivityDismissalUntilByClan);
            _captivityDismissalUntilByClan = _captivityDismissalUntilByClan ?? new Dictionary<string, float>();
            dataStore.SyncData("BellumCivile_PrivyCouncil_AssignmentRelationProgress", ref _assignmentRelationProgressByKingdom);
            if (_officeRecords == null)
                _officeRecords = new List<PrivyCouncilOfficeRecord>();
            if (_disgracedUntilByClan == null)
                _disgracedUntilByClan = new Dictionary<string, float>();
            if (_disgraceIntentByClan == null)
                _disgraceIntentByClan = new Dictionary<string, float>();
            if (_assignmentRelationProgressByKingdom == null)
                _assignmentRelationProgressByKingdom = new Dictionary<string, float>();

            InvalidateRuntimeCache(recordsChanged: true);
        }

        private static void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (party?.IsPatrolParty == true && settlement == party.HomeSettlement)
                CouncilAssignmentRuntimePatches.ReinforcePatrolAssignmentSlots(party);
        }

        private void OnTick(float dt)
        {
            if (_isShowingPlayerDismissal
                || _pendingPlayerDismissals.Count == 0
                || InformationManager.IsAnyInquiryActive()
                || !(Game.Current?.GameStateManager?.ActiveState is MapState))
            {
                return;
            }

            PendingPlayerCouncilDismissal dismissal = _pendingPlayerDismissals.Dequeue();
            _isShowingPlayerDismissal = true;
            InformationManager.ShowInquiry(new InquiryData(
                dismissal.Title,
                dismissal.Body,
                true,
                false,
                new TextObject("{=BC_Council_DisgracedDismissalAcknowledge}Understood").ToString(),
                null,
                () => _isShowingPlayerDismissal = false,
                null), true, false);
        }

        internal bool TryGetExistingOfficeHolders(Kingdom kingdom, HashSet<string> holders)
        {
            if (kingdom == null || holders == null || !IsEligiblePermanentRealm(kingdom))
                return false;
            EnsureRecordIndexes();
            if (!_recordsByKingdomId.TryGetValue(kingdom.StringId, out List<PrivyCouncilOfficeRecord> records))
                return false;
            foreach (PrivyCouncilOfficeRecord record in records)
                if (record != null && !string.IsNullOrEmpty(record.HolderClanId) && IsOfficeUnlocked(kingdom, record.Office))
                    holders.Add(record.HolderClanId);
            return true;
        }

        public IReadOnlyList<PrivyCouncilOfficeRecord> GetOfficeRecords(Kingdom kingdom)
        {
            if (kingdom == null || string.IsNullOrEmpty(kingdom.StringId))
                return Array.Empty<PrivyCouncilOfficeRecord>();

            EnsureRecordIndexes();
            if (!_recordsByKingdomId.TryGetValue(kingdom.StringId, out List<PrivyCouncilOfficeRecord> records))
            {
                EnsureCouncilForKingdom(kingdom);
                EnsureRecordIndexes();
                _recordsByKingdomId.TryGetValue(kingdom.StringId, out records);
            }

            if (records != null)
                return records;
            return Array.Empty<PrivyCouncilOfficeRecord>();
        }

        public PrivyCouncilOfficeRecord GetOfficeRecord(Kingdom kingdom, PrivyCouncilOffice office)
        {
            if (kingdom == null || string.IsNullOrEmpty(kingdom.StringId))
                return null;

            EnsureRecordIndexes();
            if (TryGetIndexedOfficeRecord(kingdom.StringId, office, out PrivyCouncilOfficeRecord record))
                return record;

            EnsureCouncilForKingdom(kingdom);
            EnsureRecordIndexes();
            TryGetIndexedOfficeRecord(kingdom.StringId, office, out record);
            return record;
        }

        public Clan GetOfficeHolder(Kingdom kingdom, PrivyCouncilOffice office)
        {
            return ResolveClan(GetOfficeRecord(kingdom, office)?.HolderClanId);
        }

        public bool IsOfficeHolderCaptive(Kingdom kingdom, PrivyCouncilOffice office)
        {
            return GetOfficeHolder(kingdom, office)?.Leader?.IsPrisoner == true;
        }

        public TextObject GetAssignmentCaptivityHint(Kingdom kingdom, PrivyCouncilOffice office)
        {
            Hero councillor = GetOfficeHolder(kingdom, office)?.Leader;
            if (councillor?.IsPrisoner != true)
                return TextObject.GetEmpty();

            TextObject hint = new TextObject(
                "{=BC_Council_AssignmentCaptive}{COUNCILLOR_NAME} is held captive and cannot be assigned new duties until released.");
            hint.SetTextVariable("COUNCILLOR_NAME", councillor.Name);
            return hint;
        }

        public void EnsureCouncilForKingdom(Kingdom kingdom)
        {
            if (!IsEligiblePermanentRealm(kingdom))
                return;

            EnsureRecordIndexes();
            float currentDay = (float)CampaignTime.Now.ToDays;
            List<PrivyCouncilOfficeRecord> newlyCreated = new List<PrivyCouncilOfficeRecord>();
            foreach (PrivyCouncilOffice office in AllOffices)
            {
                if (TryGetIndexedOfficeRecord(kingdom.StringId, office, out _))
                    continue;

                PrivyCouncilOfficeRecord officeRecord = new PrivyCouncilOfficeRecord(kingdom.StringId, office, currentDay);
                _officeRecords.Add(officeRecord);
                newlyCreated.Add(officeRecord);
            }

            if (newlyCreated.Count > 0)
            {
                InvalidateRuntimeCache(recordsChanged: true);
                SeedInitialCouncil(kingdom, newlyCreated, currentDay);
                EnsureRecordIndexes();
            }

            if (_recordsByKingdomId.TryGetValue(kingdom.StringId, out List<PrivyCouncilOfficeRecord> records))
            {
                foreach (PrivyCouncilOfficeRecord record in records)
                {
                    EnsureValidAssignment(record, currentDay);
                    EnsureAssignmentReviewScheduled(kingdom, record, currentDay);
                }
            }

            MigrateLegacyControversy(kingdom);
        }

        internal void InvalidateRuntimeCache(bool recordsChanged = false)
        {
            _runtimeSnapshotsByKingdomId.Clear();
            _runtimeUnavailableKingdomIds.Clear();
            if (recordsChanged)
                _recordIndexesDirty = true;
        }

        private void EnsureRecordIndexes()
        {
            if (!_recordIndexesDirty)
                return;

            _recordsByKingdomId.Clear();
            _recordByOfficeByKingdomId.Clear();
            foreach (PrivyCouncilOfficeRecord record in _officeRecords)
            {
                if (record == null || string.IsNullOrEmpty(record.KingdomId))
                    continue;

                if (!_recordsByKingdomId.TryGetValue(record.KingdomId, out List<PrivyCouncilOfficeRecord> records))
                {
                    records = new List<PrivyCouncilOfficeRecord>();
                    _recordsByKingdomId.Add(record.KingdomId, records);
                    _recordByOfficeByKingdomId.Add(
                        record.KingdomId,
                        new Dictionary<PrivyCouncilOffice, PrivyCouncilOfficeRecord>());
                }

                records.Add(record);
                Dictionary<PrivyCouncilOffice, PrivyCouncilOfficeRecord> officeRecords =
                    _recordByOfficeByKingdomId[record.KingdomId];
                if (!officeRecords.ContainsKey(record.Office))
                    officeRecords.Add(record.Office, record);
            }

            foreach (List<PrivyCouncilOfficeRecord> records in _recordsByKingdomId.Values)
                records.Sort((left, right) => left.Office.CompareTo(right.Office));

            _clanById.Clear();
            foreach (Clan clan in Clan.All)
            {
                if (clan != null && !string.IsNullOrEmpty(clan.StringId) && !_clanById.ContainsKey(clan.StringId))
                    _clanById.Add(clan.StringId, clan);
            }

            _recordIndexesDirty = false;
        }

        private bool TryGetIndexedOfficeRecord(
            string kingdomId,
            PrivyCouncilOffice office,
            out PrivyCouncilOfficeRecord record)
        {
            record = null;
            return !string.IsNullOrEmpty(kingdomId)
                && _recordByOfficeByKingdomId.TryGetValue(
                    kingdomId,
                    out Dictionary<PrivyCouncilOffice, PrivyCouncilOfficeRecord> records)
                && records.TryGetValue(office, out record);
        }

        private CouncilOfficeRuntimeState GetRuntimeState(Kingdom kingdom, PrivyCouncilOffice office)
        {
            CouncilRuntimeSnapshot snapshot = GetRuntimeSnapshot(kingdom);
            if (snapshot == null || !snapshot.Offices.TryGetValue(office, out CouncilOfficeRuntimeState state))
                return null;

            string currentHolderId = state.Record?.HolderClanId ?? string.Empty;
            string cachedHolderId = state.HolderClanId ?? string.Empty;
            if (!string.Equals(currentHolderId, cachedHolderId, StringComparison.Ordinal)
                || !string.Equals(
                    state.Record?.AssignmentId ?? string.Empty,
                    state.AssignmentId ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase))
            {
                _runtimeSnapshotsByKingdomId.Remove(kingdom.StringId);
                snapshot = GetRuntimeSnapshot(kingdom);
                state = null;
                if (snapshot != null)
                    snapshot.Offices.TryGetValue(office, out state);
            }

            return state;
        }

        private CouncilRuntimeSnapshot GetRuntimeSnapshot(Kingdom kingdom)
        {
            if (kingdom == null || string.IsNullOrEmpty(kingdom.StringId))
                return null;

            if (_runtimeUnavailableKingdomIds.Contains(kingdom.StringId))
                return null;

            int campaignDay = (int)Math.Floor(CampaignTime.Now.ToDays);
            if (_runtimeSnapshotsByKingdomId.TryGetValue(
                    kingdom.StringId,
                    out CouncilRuntimeSnapshot snapshot)
                && snapshot.CampaignDay == campaignDay)
            {
                return snapshot;
            }

            snapshot = BuildRuntimeSnapshot(kingdom, campaignDay);
            if (snapshot != null)
                _runtimeSnapshotsByKingdomId[kingdom.StringId] = snapshot;
            else
                _runtimeUnavailableKingdomIds.Add(kingdom.StringId);
            return snapshot;
        }

        private CouncilRuntimeSnapshot BuildRuntimeSnapshot(Kingdom kingdom, int campaignDay)
        {
            // Economy and party models query assignments many times per tick. Resolve the
            // council graph once per realm/day and keep their reads allocation-free.
            IReadOnlyList<PrivyCouncilOfficeRecord> records = GetOfficeRecords(kingdom);
            if (records.Count == 0)
                return null;

            CouncilRuntimeSnapshot snapshot = new CouncilRuntimeSnapshot
            {
                CampaignDay = campaignDay
            };
            foreach (PrivyCouncilOfficeRecord record in records)
            {
                Clan holder = ResolveClan(record.HolderClanId);
                PrivyCouncilAssignmentDefinition assignment =
                    PrivyCouncilAssignmentRegistry.GetAssignment(record.AssignmentId);
                if (assignment?.Office != record.Office)
                    assignment = PrivyCouncilAssignmentRegistry.GetDefaultAssignment(record.Office);

                float rawCompetence = CalculateCompetence(holder?.Leader, record.Office);
                CouncilOfficeRuntimeState state = new CouncilOfficeRuntimeState
                {
                    Record = record,
                    Holder = holder,
                    HolderClanId = record.HolderClanId ?? string.Empty,
                    Assignment = assignment,
                    AssignmentId = record.AssignmentId ?? string.Empty,
                    RawCompetence = rawCompetence,
                    EffectiveCompetence = rawCompetence,
                    EffectMultiplier = Clamp(0.5f + rawCompetence / 100f, 0.5f, 1.5f),
                    DrawbackMultiplier = Clamp(1.5f - rawCompetence / 100f, 0.5f, 1.5f),
                    FundingKey = BuildAssignmentFundingKey(kingdom, assignment?.Id)
                };
                snapshot.Offices[record.Office] = state;
            }

            float ordinaryAdvisorTotal = 0f;
            float incidentAdvisorDelta = 0f;
            foreach (PrivyCouncilOffice advisorOffice in new[]
            {
                PrivyCouncilOffice.FirstAdvisor,
                PrivyCouncilOffice.SecondAdvisor
            })
            {
                if (!snapshot.Offices.TryGetValue(advisorOffice, out CouncilOfficeRuntimeState advisor))
                    continue;

                string assignmentId = GetAdvisorAssignmentId(advisorOffice, "counsel_crown");
                if (!IsRuntimeStateActive(advisor, assignmentId))
                    continue;

                float contribution = 5f * advisor.EffectMultiplier;
                float incidentMultiplier = GetIncidentBehavior()?.GetAssignmentAspectMultiplier(
                    kingdom,
                    advisorOffice,
                    assignmentId,
                    CouncilIncidentAspects.AdvisorCounselCompetence) ?? 1f;
                ordinaryAdvisorTotal += contribution;
                incidentAdvisorDelta += contribution * (incidentMultiplier - 1f);
            }

            snapshot.CounselCrownCompetenceBonus = Clamp(
                Math.Min(8f, ordinaryAdvisorTotal) + incidentAdvisorDelta,
                0f,
                12f);
            foreach (CouncilOfficeRuntimeState state in snapshot.Offices.Values)
            {
                if (state.Record.Office > PrivyCouncilOffice.Spymaster || state.RawCompetence <= 0f)
                    continue;

                state.EffectiveCompetence = Clamp(
                    state.RawCompetence + snapshot.CounselCrownCompetenceBonus,
                    0f,
                    100f);
                state.EffectMultiplier = Clamp(
                    0.5f + state.EffectiveCompetence / 100f,
                    0.5f,
                    1.5f);
                state.DrawbackMultiplier = Clamp(
                    1.5f - state.EffectiveCompetence / 100f,
                    0.5f,
                    1.5f);
            }

            return snapshot;
        }

        private static bool IsRuntimeStateActive(CouncilOfficeRuntimeState state, string assignmentId)
        {
            return state?.Assignment != null
                && !string.IsNullOrEmpty(assignmentId)
                && state.Assignment.Office == state.Record.Office
                && string.Equals(state.Assignment.Id, assignmentId, StringComparison.OrdinalIgnoreCase)
                && state.Holder?.Leader != null
                && state.Holder.Leader.IsPrisoner == false;
        }

        private bool IsAssignmentFunded(CouncilOfficeRuntimeState state)
        {
            return state != null
                && !string.IsNullOrEmpty(state.FundingKey)
                && _fundedAssignmentUntilDayByKingdom.TryGetValue(
                    state.FundingKey,
                    out float fundedUntilDay)
                && fundedUntilDay >= (float)CampaignTime.Now.ToDays;
        }

        private CouncilIncidentBehavior GetIncidentBehavior()
        {
            if (_incidentBehavior == null)
                _incidentBehavior = CouncilAssignmentRuntimePatches.GetIncidentBehavior();
            return _incidentBehavior;
        }

        public IReadOnlyList<PrivyCouncilAssignmentDefinition> GetAssignments(PrivyCouncilOffice office)
        {
            return PrivyCouncilAssignmentRegistry.GetAssignments(office);
        }

        public TextObject GetAssignmentDescription(
            PrivyCouncilAssignmentDefinition assignment,
            Kingdom kingdom = null)
        {
            if (assignment == null)
                return TextObject.GetEmpty();

            if (CouncilAssignmentHapIntegration.IsExternalRequisitionApiActive
                && string.Equals(
                    assignment.Id,
                    "seneschal_stockpile_provisions",
                    StringComparison.OrdinalIgnoreCase))
            {
                return CourtInstitutionDisplayHelper.ApplyOfficeName(new TextObject(
                    "{=BC_Council_AssignmentStockpileProvisionsHapHint}At average competence: requisitions up to 2% of eligible market food into each stronghold's granary every day. Actual transfers depend on market supply and available granary capacity. The ruler pays 30 denars per stronghold each day, and no requisition occurs unless the full daily cost can be paid."),
                    assignment.Office,
                    kingdom);
            }

            return CourtInstitutionDisplayHelper.ApplyOfficeName(
                assignment.Description ?? TextObject.GetEmpty(),
                assignment.Office,
                kingdom);
        }

        public PrivyCouncilAssignmentDefinition GetOfficeAssignment(Kingdom kingdom, PrivyCouncilOffice office)
        {
            PrivyCouncilOfficeRecord record = GetOfficeRecord(kingdom, office);
            PrivyCouncilAssignmentDefinition assignment = PrivyCouncilAssignmentRegistry.GetAssignment(record?.AssignmentId);
            return assignment?.Office == office
                ? assignment
                : PrivyCouncilAssignmentRegistry.GetDefaultAssignment(office);
        }

        public float GetAssignmentCooldownRemaining(Kingdom kingdom, PrivyCouncilOffice office)
        {
            PrivyCouncilOfficeRecord record = GetOfficeRecord(kingdom, office);
            if (record == null || record.LastAssignmentChangedDay < 0f)
                return 0f;

            float elapsed = (float)CampaignTime.Now.ToDays - record.LastAssignmentChangedDay;
            return Math.Max(0f, AssignmentCooldownDays - elapsed);
        }

        public bool TrySetOfficeAssignment(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            string assignmentId,
            Clan assigningClan,
            bool ignoreCooldown,
            out TextObject failureReason)
        {
            failureReason = TextObject.GetEmpty();
            if (!IsEligiblePermanentRealm(kingdom) || !IsOfficeUnlocked(kingdom, office))
            {
                failureReason = new TextObject("{=BC_Council_AssignmentUnavailable}This council office is not available to the realm.");
                return false;
            }

            if (assigningClan == null || assigningClan != kingdom.RulingClan)
            {
                failureReason = CourtInstitutionDisplayHelper.ApplyPrivyCouncilName(
                    new TextObject("{=BC_Council_AssignmentRulerOnly}Only the ruler may assign work to the {COUNCIL_NAME}."),
                    kingdom);
                return false;
            }

            PrivyCouncilOfficeRecord record = GetOfficeRecord(kingdom, office);
            Clan holder = GetOfficeHolder(kingdom, office);
            if (record == null || holder == null)
            {
                failureReason = new TextObject("{=BC_Council_AssignmentVacant}A vacant council office cannot be given an assignment.");
                return false;
            }

            if (holder.Leader?.IsPrisoner == true)
            {
                failureReason = GetAssignmentCaptivityHint(kingdom, office);
                return false;
            }

            PrivyCouncilAssignmentDefinition assignment = PrivyCouncilAssignmentRegistry.GetAssignment(assignmentId);
            if (assignment == null || assignment.Office != office)
            {
                failureReason = new TextObject("{=BC_Council_AssignmentInvalid}That assignment is not available to this council office.");
                return false;
            }

            if (string.Equals(record.AssignmentId, assignment.Id, StringComparison.OrdinalIgnoreCase))
                return true;

            float remaining = GetAssignmentCooldownRemaining(kingdom, office);
            if (!ignoreCooldown && remaining > 0f)
            {
                TextObject cooldown = new TextObject("{=BC_Council_AssignmentCooldown}This councillor may be reassigned in {DAYS} days.");
                cooldown.SetTextVariable("DAYS", Math.Max(1, (int)Math.Ceiling(remaining)));
                failureReason = cooldown;
                return false;
            }

            record.SetAssignment(assignment.Id, (float)CampaignTime.Now.ToDays);
            GetIncidentBehavior()?.ClearAssignmentAspectModifiers(kingdom, office);
            InvalidateRuntimeCache();
            BellumCivileLogger.Log($"Privy council assignment changed; kingdom={kingdom.StringId}; office={office}; assignment={assignment.Id}; ruler={assigningClan.StringId}.");
            return true;
        }

        public float EvaluateAssignmentForAi(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            PrivyCouncilAssignmentDefinition assignment)
        {
            PrivyCouncilAssignmentContext context = BuildAssignmentContext(kingdom, office);
            return assignment == null || assignment.Office != office || context == null
                ? float.MinValue
                : assignment.EvaluateAiSuitability(context);
        }

        public float GetAssignmentEffectMultiplier(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            string assignmentId,
            bool requireFunding = false)
        {
            CouncilOfficeRuntimeState state = GetRuntimeState(kingdom, office);
            if (!IsRuntimeStateActive(state, assignmentId))
                return 0f;

            if (requireFunding && !IsAssignmentFunded(state))
                return 0f;

            return state.EffectMultiplier;
        }

        public float GetAssignmentDrawbackMultiplier(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            string assignmentId)
        {
            CouncilOfficeRuntimeState state = GetRuntimeState(kingdom, office);
            return IsRuntimeStateActive(state, assignmentId)
                ? state.DrawbackMultiplier
                : 0f;
        }

        public bool IsAssignmentActive(Kingdom kingdom, PrivyCouncilOffice office, string assignmentId)
        {
            return IsRuntimeStateActive(GetRuntimeState(kingdom, office), assignmentId);
        }

        public float GetEffectiveCoreOfficeCompetence(Kingdom kingdom, PrivyCouncilOffice office)
        {
            CouncilOfficeRuntimeState state = GetRuntimeState(kingdom, office);
            return state?.EffectiveCompetence ?? 0f;
        }

        public float GetRebelConspiracyGrowthMultiplier(Kingdom kingdom)
        {
            float efficiency = GetAssignmentEffectMultiplier(
                kingdom,
                PrivyCouncilOffice.Spymaster,
                "spymaster_uncover_dissent");
            efficiency *= Campaign.Current?
                .GetCampaignBehavior<CouncilIncidentBehavior>()?
                .GetAssignmentAspectMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Spymaster,
                    "spymaster_uncover_dissent",
                    CouncilIncidentAspects.DissentConspiracySuppression) ?? 1f;
            return Clamp(1f - 0.15f * efficiency, 0.75f, 1f);
        }

        public float GetFabricationProgressMultiplier(Clan fabricatingClan)
        {
            Kingdom kingdom = fabricatingClan?.Kingdom;
            if (kingdom?.RulingClan != fabricatingClan)
                return 1f;

            float efficiency = GetAssignmentEffectMultiplier(
                kingdom,
                PrivyCouncilOffice.Chancellor,
                "chancellor_fabricate_grievances");
            float incidentMultiplier = Campaign.Current?
                .GetCampaignBehavior<CouncilIncidentBehavior>()?
                .GetAssignmentAspectMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Chancellor,
                    "chancellor_fabricate_grievances",
                    CouncilIncidentAspects.FabricationProgress) ?? 1f;
            return 1f + 0.25f * efficiency * incidentMultiplier;
        }

        public float GetFabricationDiscoveryChanceModifier(Clan fabricatingClan, Clan defendingClan)
        {
            float modifier = 0f;
            Kingdom fabricatingKingdom = fabricatingClan?.Kingdom;
            if (fabricatingKingdom?.RulingClan == fabricatingClan)
            {
                float incidentMultiplier = Campaign.Current?
                    .GetCampaignBehavior<CouncilIncidentBehavior>()?
                    .GetAssignmentAspectMultiplier(
                        fabricatingKingdom,
                        PrivyCouncilOffice.Chancellor,
                        "chancellor_fabricate_grievances",
                        CouncilIncidentAspects.FabricationDiscoveryRisk) ?? 1f;
                modifier += 0.05f * GetAssignmentDrawbackMultiplier(
                    fabricatingKingdom,
                    PrivyCouncilOffice.Chancellor,
                    "chancellor_fabricate_grievances") * incidentMultiplier;
            }

            Kingdom defendingKingdom = defendingClan?.Kingdom;
            if (defendingKingdom != null)
            {
                CouncilIncidentBehavior incidents = Campaign.Current?
                    .GetCampaignBehavior<CouncilIncidentBehavior>();
                float counterEspionageDiscovery = incidents?.GetAssignmentAspectMultiplier(
                    defendingKingdom,
                    PrivyCouncilOffice.Spymaster,
                    "spymaster_counter_espionage",
                    CouncilIncidentAspects.CounterEspionageClaimDiscovery) ?? 1f;
                float dissentDiscovery = incidents?.GetAssignmentAspectMultiplier(
                    defendingKingdom,
                    PrivyCouncilOffice.Spymaster,
                    "spymaster_uncover_dissent",
                    CouncilIncidentAspects.DissentClaimDiscovery) ?? 1f;
                modifier += 0.05f * GetAssignmentEffectMultiplier(
                    defendingKingdom,
                    PrivyCouncilOffice.Spymaster,
                    "spymaster_counter_espionage") * counterEspionageDiscovery;
                modifier += 0.10f * GetAssignmentEffectMultiplier(
                    defendingKingdom,
                    PrivyCouncilOffice.Spymaster,
                    "spymaster_uncover_dissent") * dissentDiscovery;
            }

            return modifier;
        }

        public float GetRepresentedFactionMoodBaseline(FactionObject faction)
        {
            return GetRepresentedFactionEffect(faction, CouncilIncidentAspects.AdvisorRepresentationMood);
        }

        public float GetAuditVassalsServiceBonusRate(Kingdom kingdom)
        {
            float multiplier = GetAssignmentEffectMultiplier(
                kingdom,
                PrivyCouncilOffice.Seneschal,
                "seneschal_audit_vassals");
            CouncilIncidentBehavior incidents = Campaign.Current?
                .GetCampaignBehavior<CouncilIncidentBehavior>();
            multiplier *= incidents?.GetAssignmentAspectMultiplier(
                kingdom,
                PrivyCouncilOffice.Seneschal,
                "seneschal_audit_vassals",
                CouncilIncidentAspects.AuditServiceStrength) ?? 1f;
            multiplier *= incidents?.GetAssignmentAspectMultiplier(
                kingdom,
                PrivyCouncilOffice.Seneschal,
                "seneschal_audit_vassals",
                CouncilIncidentAspects.AuditReach) ?? 1f;
            return 0.10f * multiplier;
        }

        public float EvaluateBuiltInAssignment(string assignmentId, PrivyCouncilAssignmentContext context)
        {
            if (context?.Kingdom == null || string.IsNullOrEmpty(assignmentId))
                return 0f;

            return CalculateAssignmentSuitability(assignmentId, context, BuildAssignmentAiSnapshot(context.Kingdom));
        }

        public void ApplyBuiltInAssignmentDailyEffect(string assignmentId, PrivyCouncilAssignmentContext context)
        {
            if (context?.Kingdom == null || string.IsNullOrEmpty(assignmentId))
                return;

            if (assignmentId == "seneschal_subsidize_infrastructure")
            {
                ApplyFundedSettlementAssignment(
                    context,
                    assignmentId,
                    town => { });
            }
            else if (assignmentId == "seneschal_stockpile_provisions")
            {
                float efficiency = GetAssignmentEffectMultiplier(
                    context.Kingdom,
                    PrivyCouncilOffice.Seneschal,
                    assignmentId);
                CouncilIncidentBehavior incidents = Campaign.Current?
                    .GetCampaignBehavior<CouncilIncidentBehavior>();
                efficiency *= incidents?.GetAssignmentAspectMultiplier(
                    context.Kingdom,
                    PrivyCouncilOffice.Seneschal,
                    assignmentId,
                    CouncilIncidentAspects.ProvisionsFoodYield) ?? 1f;
                efficiency *= incidents?.GetAssignmentAspectMultiplier(
                    context.Kingdom,
                    PrivyCouncilOffice.Seneschal,
                    assignmentId,
                    CouncilIncidentAspects.ProvisionsDistributionEfficiency) ?? 1f;
                float requisitionPercent = 2f * efficiency;
                ApplyFundedSettlementAssignment(
                    context,
                    assignmentId,
                    town =>
                    {
                        if (CouncilAssignmentHapIntegration.IsExternalRequisitionApiActive)
                            CouncilAssignmentHapIntegration.TryRequisitionProvisions(
                                town,
                                requisitionPercent,
                                out _);
                    });
            }
        }

        public int GetFundedAssignmentDailyCost(Kingdom kingdom, string assignmentId)
        {
            if (kingdom == null || string.IsNullOrEmpty(assignmentId))
                return 0;

            int costPerStronghold;
            if (string.Equals(assignmentId, "seneschal_subsidize_infrastructure", StringComparison.OrdinalIgnoreCase))
                costPerStronghold = InfrastructureCostPerStronghold;
            else if (string.Equals(assignmentId, "seneschal_stockpile_provisions", StringComparison.OrdinalIgnoreCase))
                costPerStronghold = ProvisionsCostPerStronghold;
            else
                return 0;

            if (!IsAssignmentActive(kingdom, PrivyCouncilOffice.Seneschal, assignmentId))
                return 0;

            int strongholdCount = kingdom.Fiefs.Count(town => town != null);
            if (strongholdCount <= 0)
                return 0;

            float costMultiplier = GetAssignmentDrawbackMultiplier(
                kingdom,
                PrivyCouncilOffice.Seneschal,
                assignmentId);
            CouncilIncidentBehavior incidents = Campaign.Current?
                .GetCampaignBehavior<CouncilIncidentBehavior>();
            if (string.Equals(assignmentId, "seneschal_subsidize_infrastructure", StringComparison.OrdinalIgnoreCase))
            {
                costMultiplier *= incidents?.GetAssignmentAspectMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Seneschal,
                    assignmentId,
                    CouncilIncidentAspects.InfrastructureDailyCost) ?? 1f;
                float efficiency = incidents?.GetAssignmentAspectMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Seneschal,
                    assignmentId,
                    CouncilIncidentAspects.InfrastructureEfficiency) ?? 1f;
                costMultiplier /= Math.Max(0.25f, efficiency);
            }
            else if (string.Equals(assignmentId, "seneschal_stockpile_provisions", StringComparison.OrdinalIgnoreCase))
            {
                costMultiplier *= incidents?.GetAssignmentAspectMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Seneschal,
                    assignmentId,
                    CouncilIncidentAspects.ProvisionsDailyCost) ?? 1f;
            }
            return (int)Math.Ceiling(
                strongholdCount * costPerStronghold * Math.Max(0.05f, costMultiplier));
        }

        public TextObject GetAssignmentStatusHint(Kingdom kingdom, PrivyCouncilOffice office)
        {
            if (IsOfficeHolderCaptive(kingdom, office))
                return GetAssignmentCaptivityHint(kingdom, office);

            PrivyCouncilAssignmentDefinition assignment = GetOfficeAssignment(kingdom, office);
            if (assignment == null)
                return TextObject.GetEmpty();

            List<string> lines = new List<string>
            {
                assignment.Name.ToString(),
                CourtInstitutionDisplayHelper.ApplyOfficeName(
                    BuildAssignmentEffectSummary(kingdom, office, assignment.Id),
                    office,
                    kingdom).ToString()
            };

            CouncilIncidentBehavior incidents = Campaign.Current?
                .GetCampaignBehavior<CouncilIncidentBehavior>();
            IReadOnlyList<ActiveCouncilAssignmentModifier> modifiers =
                incidents?.GetActiveAssignmentModifiers(kingdom, office, assignment.Id);
            if (modifiers != null && modifiers.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add(new TextObject("{=BC_Council_AssignmentActiveIncidents}Active incident effects:").ToString());
                foreach (ActiveCouncilAssignmentModifier modifier in modifiers)
                {
                    TextObject line = new TextObject(
                        "{=BC_Council_AssignmentIncidentEffect}{INCIDENT}: {CHANGE}% to {ASPECT} ({DAYS} days remaining).");
                    line.SetTextVariable("INCIDENT", modifier.IncidentTitle);
                    line.SetTextVariable("CHANGE", FormatSignedPercentChange(modifier.Multiplier));
                    line.SetTextVariable("ASPECT", GetAssignmentAspectDisplayName(modifier.AspectId));
                    line.SetTextVariable("DAYS", Math.Max(1, (int)Math.Ceiling(modifier.RemainingDays)));
                    lines.Add(line.ToString());
                }
            }

            return new TextObject(string.Join("\n", lines));
        }

        private TextObject BuildAssignmentEffectSummary(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            string assignmentId)
        {
            if (kingdom == null || string.IsNullOrEmpty(assignmentId))
                return TextObject.GetEmpty();

            float effect = GetAssignmentEffectMultiplier(kingdom, office, assignmentId);
            float drawback = GetAssignmentDrawbackMultiplier(kingdom, office, assignmentId);
            CouncilIncidentBehavior incidents = Campaign.Current?
                .GetCampaignBehavior<CouncilIncidentBehavior>();
            Func<string, float> aspect = aspectId => incidents?.GetAssignmentAspectMultiplier(
                kingdom,
                office,
                assignmentId,
                aspectId) ?? 1f;
            TextObject summary;

            if (assignmentId.EndsWith("_none", StringComparison.OrdinalIgnoreCase))
            {
                return new TextObject("{=BC_Council_AssignmentStatusNone}No duties are assigned. The ruler loses 1 relation with this councillor each week.");
            }

            switch (assignmentId)
            {
                case "marshal_organize_patrols":
                    summary = new TextObject("{=BC_Council_AssignmentStatusOrganizePatrols}Patrol parties are {SIZE}% larger and form {SPEED}% faster. Protection failures generate {CONTROVERSY}% more Marshal controversy.");
                    summary.SetTextVariable("SIZE", FormatNumber(10f * effect * aspect(CouncilIncidentAspects.PatrolPartySize)));
                    summary.SetTextVariable("SPEED", FormatNumber(15f * effect * aspect(CouncilIncidentAspects.PatrolFormationSpeed)));
                    summary.SetTextVariable("CONTROVERSY", FormatNumber(25f * drawback * aspect(CouncilIncidentAspects.RoadFailureControversy)));
                    return summary;
                case "marshal_train_militia":
                    summary = new TextObject("{=BC_Council_AssignmentStatusTrainMilitia}Positive militia growth is increased by {GROWTH}% and veteran militia chance by {VETERANS}%. Village production is reduced by {PRODUCTION}%.");
                    summary.SetTextVariable("GROWTH", FormatNumber(10f * effect * aspect(CouncilIncidentAspects.MilitiaGrowth)));
                    summary.SetTextVariable("VETERANS", FormatNumber(10f * effect * aspect(CouncilIncidentAspects.VeteranMilitiaChance)));
                    summary.SetTextVariable("PRODUCTION", FormatNumber(10f * drawback * aspect(CouncilIncidentAspects.VillageProductionPenalty)));
                    return summary;
                case "marshal_oversee_logistics":
                    summary = new TextObject("{=BC_Council_AssignmentStatusOverseeLogistics}Army cohesion loss is reduced by {COHESION}% and food consumption by {FOOD}%. Generates {CONTROVERSY} Marshal controversy each week.");
                    summary.SetTextVariable("COHESION", FormatNumber(15f * effect * aspect(CouncilIncidentAspects.ArmyCohesionRetention)));
                    summary.SetTextVariable("FOOD", FormatNumber(15f * effect * aspect(CouncilIncidentAspects.ArmyFoodEfficiency)));
                    summary.SetTextVariable("CONTROVERSY", FormatNumber(drawback * aspect(CouncilIncidentAspects.LogisticsWeeklyControversy)));
                    return summary;
                case "chancellor_appease_nobles":
                    summary = new TextObject("{=BC_Council_AssignmentStatusAppeaseNobles}Builds approximately {RELATION} relation each week with the most estranged loyal vassal, stopping at +25. Generates {CONTROVERSY} Chancellor controversy each week.");
                    summary.SetTextVariable("RELATION", FormatNumber(effect * aspect(CouncilIncidentAspects.NobleAppeasementStrength)));
                    summary.SetTextVariable("CONTROVERSY", FormatNumber(drawback * aspect(CouncilIncidentAspects.NobleAppeasementWeeklyControversy)));
                    return summary;
                case "chancellor_improve_foreign_relations":
                    summary = new TextObject("{=BC_Council_AssignmentStatusImproveForeignRelations}Builds approximately {RELATION} relation each week with the most estranged neighboring ruler at peace with the realm, stopping at +25. Generates {CONTROVERSY} Chancellor controversy each week.");
                    summary.SetTextVariable("RELATION", FormatNumber(effect * aspect(CouncilIncidentAspects.ForeignRelationsStrength)));
                    summary.SetTextVariable("CONTROVERSY", FormatNumber(drawback * aspect(CouncilIncidentAspects.ForeignRelationsWeeklyControversy)));
                    return summary;
                case "chancellor_fabricate_grievances":
                    summary = new TextObject("{=BC_Council_AssignmentStatusFabricateGrievances}Ruling-clan claim fabrication is {PROGRESS}% faster, with {DISCOVERY} additional percentage points of discovery risk. Generates {CONTROVERSY} Chancellor controversy each week.");
                    summary.SetTextVariable("PROGRESS", FormatNumber(25f * effect * aspect(CouncilIncidentAspects.FabricationProgress)));
                    summary.SetTextVariable("DISCOVERY", FormatNumber(5f * drawback * aspect(CouncilIncidentAspects.FabricationDiscoveryRisk)));
                    summary.SetTextVariable("CONTROVERSY", FormatNumber(drawback * aspect(CouncilIncidentAspects.FabricationWeeklyControversy)));
                    return summary;
                case "seneschal_audit_vassals":
                    summary = new TextObject("{=BC_Council_AssignmentStatusAuditVassals}Feudal service income reaching the crown is increased by {SERVICE}%. Generates {CONTROVERSY} Seneschal controversy each week.");
                    summary.SetTextVariable("SERVICE", FormatNumber(GetAuditVassalsServiceBonusRate(kingdom) * 100f));
                    summary.SetTextVariable("CONTROVERSY", FormatNumber(drawback * aspect(CouncilIncidentAspects.AuditWeeklyControversy)));
                    return summary;
                case "seneschal_subsidize_infrastructure":
                    summary = new TextObject("{=BC_Council_AssignmentStatusSubsidizeInfrastructure}Provides +{CONSTRUCTION}% construction in {STRONGHOLDS} strongholds while fully funded. Daily cost: {COST} denars.");
                    summary.SetTextVariable("CONSTRUCTION", FormatNumber(10f * effect
                        * aspect(CouncilIncidentAspects.InfrastructureConstructionStrength)
                        * aspect(CouncilIncidentAspects.InfrastructureEfficiency)));
                    summary.SetTextVariable("STRONGHOLDS", kingdom.Fiefs.Count(town => town != null));
                    summary.SetTextVariable("COST", GetFundedAssignmentDailyCost(kingdom, assignmentId));
                    return summary;
                case "seneschal_stockpile_provisions":
                    float food = 2f * effect
                        * aspect(CouncilIncidentAspects.ProvisionsFoodYield)
                        * aspect(CouncilIncidentAspects.ProvisionsDistributionEfficiency);
                    if (CouncilAssignmentHapIntegration.IsExternalRequisitionApiActive)
                    {
                        summary = new TextObject("{=BC_Council_AssignmentStatusStockpileProvisionsHap}Requisitions up to {PERCENT}% of eligible market food into the granaries of {STRONGHOLDS} strongholds each day while fully funded. Actual transfers depend on market supply and available granary capacity. Daily cost: {COST} denars.");
                        summary.SetTextVariable("PERCENT", FormatNumber(food));
                        summary.SetTextVariable("STRONGHOLDS", kingdom.Fiefs.Count(town => town != null));
                        summary.SetTextVariable("COST", GetFundedAssignmentDailyCost(kingdom, assignmentId));
                        return summary;
                    }
                    summary = new TextObject("{=BC_Council_AssignmentStatusStockpileProvisions}Provides up to +{FOOD} food per day in {STRONGHOLDS} strongholds while fully funded. Daily cost: {COST} denars.");
                    summary.SetTextVariable("FOOD", FormatNumber(food));
                    summary.SetTextVariable("STRONGHOLDS", kingdom.Fiefs.Count(town => town != null));
                    summary.SetTextVariable("COST", GetFundedAssignmentDailyCost(kingdom, assignmentId));
                    return summary;
                case "spymaster_counter_espionage":
                    summary = new TextObject("{=BC_Council_AssignmentStatusCounterEspionage}Provides +{DEFENSE} defensive intrigue and +{DISCOVERY} percentage points to hostile claim discovery, while imposing -{OFFENSE} offensive intrigue.");
                    summary.SetTextVariable("DEFENSE", FormatNumber(15f * effect * aspect(CouncilIncidentAspects.CounterEspionageDefense)));
                    summary.SetTextVariable("DISCOVERY", FormatNumber(5f * effect * aspect(CouncilIncidentAspects.CounterEspionageClaimDiscovery)));
                    summary.SetTextVariable("OFFENSE", FormatNumber(5f * drawback * aspect(CouncilIncidentAspects.CounterEspionageOffensePenalty)));
                    return summary;
                case "spymaster_uncover_dissent":
                    summary = new TextObject("{=BC_Council_AssignmentStatusUncoverDissent}Covert rebel conspiracies grow {SUPPRESSION}% slower and hostile claim discovery gains {DISCOVERY} percentage points. Generates {CONTROVERSY} Spymaster controversy each week.");
                    summary.SetTextVariable("SUPPRESSION", FormatNumber((1f - GetRebelConspiracyGrowthMultiplier(kingdom)) * 100f));
                    summary.SetTextVariable("DISCOVERY", FormatNumber(10f * effect * aspect(CouncilIncidentAspects.DissentClaimDiscovery)));
                    summary.SetTextVariable("CONTROVERSY", FormatNumber(drawback * aspect(CouncilIncidentAspects.DissentWeeklyControversy)));
                    return summary;
                case "spymaster_sow_rumors":
                    summary = new TextObject("{=BC_Council_AssignmentStatusSowRumors}Provides +{OFFENSE} offensive intrigue against rival courts, while imposing -{DEFENSE} defensive intrigue. Generates {CONTROVERSY} Spymaster controversy each week.");
                    summary.SetTextVariable("OFFENSE", FormatNumber(15f * effect * aspect(CouncilIncidentAspects.RumorOffense)));
                    summary.SetTextVariable("DEFENSE", FormatNumber(5f * drawback * aspect(CouncilIncidentAspects.RumorDefensePenalty)));
                    summary.SetTextVariable("CONTROVERSY", FormatNumber(drawback * aspect(CouncilIncidentAspects.RumorWeeklyControversy)));
                    return summary;
            }

            if (assignmentId.EndsWith("_counsel_crown", StringComparison.OrdinalIgnoreCase))
            {
                float contribution = 5f * effect * aspect(CouncilIncidentAspects.AdvisorCounselCompetence);
                summary = new TextObject("{=BC_Council_AssignmentStatusCounselCrown}Contributes +{CONTRIBUTION} effective competence to all four core offices. Combined advisor contribution: +{TOTAL}.");
                summary.SetTextVariable("CONTRIBUTION", FormatNumber(contribution));
                summary.SetTextVariable("TOTAL", FormatNumber(GetCounselCrownCompetenceBonus(kingdom)));
                return summary;
            }

            if (assignmentId.EndsWith("_mediate_council", StringComparison.OrdinalIgnoreCase))
            {
                float supportContribution = 5f * effect * aspect(CouncilIncidentAspects.AdvisorMediationSupport);
                float controversyContribution = effect * aspect(CouncilIncidentAspects.AdvisorMediationControversy);
                float controversyTotal = GetCombinedAdvisorAssignmentEfficiency(
                    kingdom,
                    "mediate_council",
                    CouncilIncidentAspects.AdvisorMediationControversy,
                    1.5f,
                    2.5f);
                summary = new TextObject("{=BC_Council_AssignmentStatusMediateCouncil}Contributes +{SUPPORT} support to core councillors and removes {CONTROVERSY} controversy each week. Combined totals: +{TOTAL_SUPPORT} support and -{TOTAL_CONTROVERSY} controversy.");
                summary.SetTextVariable("SUPPORT", FormatNumber(supportContribution));
                summary.SetTextVariable("CONTROVERSY", FormatNumber(controversyContribution));
                summary.SetTextVariable("TOTAL_SUPPORT", FormatNumber(GetMediateCouncilSupportBonus(kingdom)));
                summary.SetTextVariable("TOTAL_CONTROVERSY", FormatNumber(controversyTotal));
                return summary;
            }

            if (assignmentId.EndsWith("_represent_court", StringComparison.OrdinalIgnoreCase))
            {
                Clan holder = GetOfficeHolder(kingdom, office);
                FactionObject faction = Campaign.Current?
                    .GetCampaignBehavior<FactionManagerBehavior>()?
                    .GetIdeologicalFaction(holder);
                if (faction == null)
                    return new TextObject("{=BC_Council_AssignmentStatusRepresentCourtNone}This advisor is not affiliated with a court faction and therefore represents no factional interest.");

                float overall = aspect(CouncilIncidentAspects.AdvisorRepresentationOverall);
                float mood = Math.Min(10f, 5f * effect
                    * aspect(CouncilIncidentAspects.AdvisorRepresentationMood)
                    * overall);
                float intent = Math.Min(10f, 5f * effect
                    * aspect(CouncilIncidentAspects.AdvisorRepresentationIntent)
                    * overall);
                summary = new TextObject("{=BC_Council_AssignmentStatusRepresentCourt}Represents the {FACTION}, providing +{MOOD} faction mood and -{INTENT} rebellious intent. Duplicate representation does not stack.");
                summary.SetTextVariable("FACTION", NotificationHelper.GetFactionDisplayName(faction));
                summary.SetTextVariable("MOOD", FormatNumber(mood));
                summary.SetTextVariable("INTENT", FormatNumber(intent));
                return summary;
            }

            return PrivyCouncilAssignmentRegistry.GetAssignment(assignmentId)?.Description
                ?? TextObject.GetEmpty();
        }

        private static string FormatNumber(float value)
        {
            return value.ToString("0.#");
        }

        private static string FormatSignedPercentChange(float multiplier)
        {
            float change = (multiplier - 1f) * 100f;
            return (change > 0.05f ? "+" : string.Empty) + FormatNumber(change);
        }

        private static TextObject GetAssignmentAspectDisplayName(string aspectId)
        {
            switch (aspectId)
            {
                case CouncilIncidentAspects.PatrolPartySize: return new TextObject("{=BC_Council_AspectPatrolPartySize}patrol party size");
                case CouncilIncidentAspects.PatrolFormationSpeed: return new TextObject("{=BC_Council_AspectPatrolFormationSpeed}patrol formation speed");
                case CouncilIncidentAspects.RoadFailureControversy: return new TextObject("{=BC_Council_AspectRoadFailureControversy}road-security controversy");
                case CouncilIncidentAspects.MilitiaGrowth: return new TextObject("{=BC_Council_AspectMilitiaGrowth}militia growth");
                case CouncilIncidentAspects.VeteranMilitiaChance: return new TextObject("{=BC_Council_AspectVeteranMilitiaChance}veteran militia chance");
                case CouncilIncidentAspects.VillageProductionPenalty: return new TextObject("{=BC_Council_AspectVillageProductionPenalty}village production penalty");
                case CouncilIncidentAspects.ArmyCohesionRetention: return new TextObject("{=BC_Council_AspectArmyCohesionRetention}army cohesion retention");
                case CouncilIncidentAspects.ArmyFoodEfficiency: return new TextObject("{=BC_Council_AspectArmyFoodEfficiency}army food efficiency");
                case CouncilIncidentAspects.LogisticsWeeklyControversy: return new TextObject("{=BC_Council_AspectLogisticsControversy}logistics controversy");
                case CouncilIncidentAspects.NobleAppeasementStrength: return new TextObject("{=BC_Council_AspectNobleAppeasementStrength}noble appeasement");
                case CouncilIncidentAspects.NobleAppeasementReach: return new TextObject("{=BC_Council_AspectNobleAppeasementReach}noble appeasement reach");
                case CouncilIncidentAspects.NobleAppeasementWeeklyControversy: return new TextObject("{=BC_Council_AspectNobleAppeasementControversy}noble appeasement controversy");
                case CouncilIncidentAspects.ForeignRelationsStrength: return new TextObject("{=BC_Council_AspectForeignRelationsStrength}foreign relations");
                case CouncilIncidentAspects.ForeignRelationsReach: return new TextObject("{=BC_Council_AspectForeignRelationsReach}foreign-relations reach");
                case CouncilIncidentAspects.ForeignRelationsWeeklyControversy: return new TextObject("{=BC_Council_AspectForeignRelationsControversy}foreign-relations controversy");
                case CouncilIncidentAspects.FabricationProgress: return new TextObject("{=BC_Council_AspectFabricationProgress}claim fabrication progress");
                case CouncilIncidentAspects.FabricationDiscoveryRisk: return new TextObject("{=BC_Council_AspectFabricationDiscoveryRisk}fabrication discovery risk");
                case CouncilIncidentAspects.FabricationWeeklyControversy: return new TextObject("{=BC_Council_AspectFabricationControversy}fabrication controversy");
                case CouncilIncidentAspects.AuditServiceStrength: return new TextObject("{=BC_Council_AspectAuditServiceStrength}feudal service income");
                case CouncilIncidentAspects.AuditReach: return new TextObject("{=BC_Council_AspectAuditReach}audit reach");
                case CouncilIncidentAspects.AuditWeeklyControversy: return new TextObject("{=BC_Council_AspectAuditControversy}audit controversy");
                case CouncilIncidentAspects.InfrastructureConstructionStrength: return new TextObject("{=BC_Council_AspectInfrastructureConstruction}construction subsidies");
                case CouncilIncidentAspects.InfrastructureDailyCost: return new TextObject("{=BC_Council_AspectInfrastructureCost}infrastructure cost");
                case CouncilIncidentAspects.InfrastructureEfficiency: return new TextObject("{=BC_Council_AspectInfrastructureEfficiency}infrastructure efficiency");
                case CouncilIncidentAspects.ProvisionsFoodYield: return new TextObject("{=BC_Council_AspectProvisionsFoodYield}provision yield");
                case CouncilIncidentAspects.ProvisionsDailyCost: return new TextObject("{=BC_Council_AspectProvisionsCost}provision cost");
                case CouncilIncidentAspects.ProvisionsDistributionEfficiency: return new TextObject("{=BC_Council_AspectProvisionsDistribution}provision distribution");
                case CouncilIncidentAspects.CounterEspionageDefense: return new TextObject("{=BC_Council_AspectCounterEspionageDefense}defensive intrigue");
                case CouncilIncidentAspects.CounterEspionageClaimDiscovery: return new TextObject("{=BC_Council_AspectCounterEspionageDiscovery}hostile claim discovery");
                case CouncilIncidentAspects.CounterEspionageOffensePenalty: return new TextObject("{=BC_Council_AspectCounterEspionageOffensePenalty}offensive intrigue penalty");
                case CouncilIncidentAspects.DissentConspiracySuppression: return new TextObject("{=BC_Council_AspectDissentSuppression}conspiracy suppression");
                case CouncilIncidentAspects.DissentClaimDiscovery: return new TextObject("{=BC_Council_AspectDissentClaimDiscovery}hostile claim discovery");
                case CouncilIncidentAspects.DissentWeeklyControversy: return new TextObject("{=BC_Council_AspectDissentControversy}dissent investigation controversy");
                case CouncilIncidentAspects.RumorOffense: return new TextObject("{=BC_Council_AspectRumorOffense}offensive intrigue");
                case CouncilIncidentAspects.RumorDefensePenalty: return new TextObject("{=BC_Council_AspectRumorDefensePenalty}defensive intrigue penalty");
                case CouncilIncidentAspects.RumorWeeklyControversy: return new TextObject("{=BC_Council_AspectRumorControversy}rumor-mongering controversy");
                case CouncilIncidentAspects.AdvisorCounselCompetence: return new TextObject("{=BC_Council_AspectAdvisorCounselCompetence}council competence");
                case CouncilIncidentAspects.AdvisorMediationSupport: return new TextObject("{=BC_Council_AspectAdvisorMediationSupport}council support");
                case CouncilIncidentAspects.AdvisorMediationControversy: return new TextObject("{=BC_Council_AspectAdvisorMediationControversy}controversy mediation");
                case CouncilIncidentAspects.AdvisorRepresentationMood: return new TextObject("{=BC_Council_AspectAdvisorRepresentationMood}represented faction mood");
                case CouncilIncidentAspects.AdvisorRepresentationIntent: return new TextObject("{=BC_Council_AspectAdvisorRepresentationIntent}represented faction rebellious intent");
                case CouncilIncidentAspects.AdvisorRepresentationOverall: return new TextObject("{=BC_Council_AspectAdvisorRepresentationOverall}court representation");
                default: return new TextObject("{=BC_Council_AspectUnknown}assignment effect");
            }
        }

        private void ApplyFundedSettlementAssignment(
            PrivyCouncilAssignmentContext context,
            string assignmentId,
            Action<Town> effect)
        {
            if (context?.Kingdom == null || effect == null)
                return;

            List<Town> strongholds = context.Kingdom.Fiefs
                .Where(town => town != null)
                .ToList();
            Hero payer = context.Kingdom.RulingClan?.Leader;
            int cost = GetFundedAssignmentDailyCost(context.Kingdom, assignmentId);
            if (payer == null || strongholds.Count == 0 || cost <= 0 || payer.Gold < cost)
                return;

            _fundedAssignmentUntilDayByKingdom[BuildAssignmentFundingKey(context.Kingdom, assignmentId)] =
                (float)CampaignTime.Now.ToDays + 1.05f;

            foreach (Town town in strongholds)
                effect(town);
        }

        private float GetCounselCrownCompetenceBonus(Kingdom kingdom)
        {
            return GetRuntimeSnapshot(kingdom)?.CounselCrownCompetenceBonus ?? 0f;
        }

        private float GetMediateCouncilSupportBonus(Kingdom kingdom)
        {
            return GetCombinedAdvisorCappedBonus(
                kingdom,
                "mediate_council",
                CouncilIncidentAspects.AdvisorMediationSupport,
                8f,
                12f);
        }

        private float GetRepresentedFactionIntentRelief(FactionObject faction)
        {
            return GetRepresentedFactionEffect(faction, CouncilIncidentAspects.AdvisorRepresentationIntent);
        }

        private void ApplyWeeklyAssignmentEffects(Kingdom kingdom)
        {
            ApplySidelinedCouncillorRelations(kingdom);
            ApplyAppeaseNobility(kingdom);
            ApplyImproveForeignRelations(kingdom);
            ApplyAssignmentUpkeepControversy(kingdom);

            float mediationEfficiency = GetCombinedAdvisorAssignmentEfficiency(
                kingdom,
                "mediate_council",
                CouncilIncidentAspects.AdvisorMediationControversy,
                1.5f,
                2.5f);
            if (mediationEfficiency <= 0f)
                return;

            PrivyCouncilOfficeRecord mostControversial = GetOfficeRecords(kingdom)
                .Where(record => record.Office <= PrivyCouncilOffice.Spymaster
                    && !string.IsNullOrEmpty(record.HolderClanId))
                .OrderByDescending(record => record.Controversy)
                .FirstOrDefault();
            if (mostControversial?.Controversy > 0f)
            {
                mostControversial.ChangeControversy(
                    -mediationEfficiency,
                    "council mediation",
                    (float)CampaignTime.Now.ToDays);
            }
        }

        private void ApplyAppeaseNobility(Kingdom kingdom)
        {
            float efficiency = GetAssignmentEffectMultiplier(
                kingdom,
                PrivyCouncilOffice.Chancellor,
                "chancellor_appease_nobles");
            if (efficiency <= 0f)
                return;

            CouncilIncidentBehavior incidents = Campaign.Current?
                .GetCampaignBehavior<CouncilIncidentBehavior>();
            efficiency *= incidents?.GetAssignmentAspectMultiplier(
                kingdom,
                PrivyCouncilOffice.Chancellor,
                "chancellor_appease_nobles",
                CouncilIncidentAspects.NobleAppeasementStrength) ?? 1f;
            float reach = incidents?.GetAssignmentAspectMultiplier(
                kingdom,
                PrivyCouncilOffice.Chancellor,
                "chancellor_appease_nobles",
                CouncilIncidentAspects.NobleAppeasementReach) ?? 1f;

            Hero ruler = kingdom?.RulingClan?.Leader;
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            List<Clan> targets = GetEligibleCouncilClans(kingdom, includePrisoners: true)
                .Where(clan => clan?.Leader != null
                    && ruler != null
                    && ruler.GetRelation(clan.Leader) < 25
                    && (factionManager == null
                        || !factionManager.IsClanOnActiveCivilWarRebelSide(clan, out _, out _)))
                .OrderBy(clan => ruler.GetRelation(clan.Leader))
                .ThenBy(clan => clan.StringId)
                .Take(2)
                .ToList();
            ApplyAssignmentRelationReach(
                kingdom,
                "chancellor_appease_nobles",
                ruler,
                targets.Select(target => target.Leader).ToList(),
                efficiency,
                reach);
        }

        private void ApplyImproveForeignRelations(Kingdom kingdom)
        {
            float efficiency = GetAssignmentEffectMultiplier(
                kingdom,
                PrivyCouncilOffice.Chancellor,
                "chancellor_improve_foreign_relations");
            Hero ruler = kingdom?.RulingClan?.Leader;
            if (efficiency <= 0f || ruler == null)
                return;

            CouncilIncidentBehavior incidents = Campaign.Current?
                .GetCampaignBehavior<CouncilIncidentBehavior>();
            efficiency *= incidents?.GetAssignmentAspectMultiplier(
                kingdom,
                PrivyCouncilOffice.Chancellor,
                "chancellor_improve_foreign_relations",
                CouncilIncidentAspects.ForeignRelationsStrength) ?? 1f;
            float reach = incidents?.GetAssignmentAspectMultiplier(
                kingdom,
                PrivyCouncilOffice.Chancellor,
                "chancellor_improve_foreign_relations",
                CouncilIncidentAspects.ForeignRelationsReach) ?? 1f;

            List<Hero> targets = new ForeignPolicyEvaluationService()
                .EvaluateWarTargets(kingdom)
                .Where(evaluation => evaluation?.TargetKingdom?.RulingClan?.Leader != null
                    && evaluation.IsNeighboringRealm
                    && !kingdom.IsAtWarWith(evaluation.TargetKingdom))
                .Select(evaluation => evaluation.TargetKingdom)
                .Where(candidate => ruler.GetRelation(candidate.RulingClan.Leader) < 25)
                .OrderBy(candidate => ruler.GetRelation(candidate.RulingClan.Leader))
                .ThenBy(candidate => candidate.StringId)
                .Take(2)
                .Select(candidate => candidate.RulingClan.Leader)
                .ToList();
            ApplyAssignmentRelationReach(
                kingdom,
                "chancellor_improve_foreign_relations",
                ruler,
                targets,
                efficiency,
                reach);
        }

        private void ApplyAssignmentRelationReach(
            Kingdom kingdom,
            string assignmentId,
            Hero source,
            IReadOnlyList<Hero> targets,
            float efficiency,
            float reach)
        {
            if (targets == null || targets.Count == 0 || reach <= 0f)
                return;

            float remainingReach = Math.Max(0f, Math.Min(2f, reach));
            for (int index = 0; index < targets.Count && remainingReach > 0f; index++)
            {
                float targetShare = Math.Min(1f, remainingReach);
                ApplyAssignmentRelationGain(
                    kingdom,
                    assignmentId,
                    source,
                    targets[index],
                    efficiency * targetShare);
                remainingReach -= targetShare;
            }
        }

        private void ApplyAssignmentRelationGain(
            Kingdom kingdom,
            string assignmentId,
            Hero source,
            Hero target,
            float efficiency)
        {
            if (kingdom == null || source == null || target == null || efficiency <= 0f)
                return;

            int currentRelation = source.GetRelation(target);
            if (currentRelation >= 25)
                return;

            string key = BuildAssignmentFundingKey(kingdom, assignmentId);
            float progress = (_assignmentRelationProgressByKingdom.TryGetValue(key, out float stored)
                ? stored
                : 0f) + efficiency;
            int relationGain = Math.Min(25 - currentRelation, (int)Math.Floor(progress));
            _assignmentRelationProgressByKingdom[key] = progress - relationGain;
            if (relationGain <= 0)
                return;

            RelationMemoryService.ApplyChangeWithDefaultDuration(
                source,
                target,
                relationGain,
                source == Hero.MainHero || target == Hero.MainHero,
                assignmentId == "chancellor_appease_nobles"
                    ? RelationMemorySources.ChancellorAppeasement : RelationMemorySources.ChancellorDiplomacy,
                RelationMemoryScope.Personal);
        }

        private void ApplyAssignmentUpkeepControversy(Kingdom kingdom)
        {
            if (IsAssignmentActive(kingdom, PrivyCouncilOffice.Marshal, "marshal_oversee_logistics"))
            {
                float incidentMultiplier = Campaign.Current?
                    .GetCampaignBehavior<CouncilIncidentBehavior>()?
                    .GetAssignmentAspectMultiplier(
                        kingdom,
                        PrivyCouncilOffice.Marshal,
                        "marshal_oversee_logistics",
                        CouncilIncidentAspects.LogisticsWeeklyControversy) ?? 1f;
                AddAssignmentControversy(
                    kingdom,
                    PrivyCouncilOffice.Marshal,
                    "marshal_oversee_logistics",
                    incidentMultiplier,
                    "the burdens of army logistics");
            }

            PrivyCouncilAssignmentDefinition chancellorAssignment = GetOfficeAssignment(
                kingdom,
                PrivyCouncilOffice.Chancellor);
            if (chancellorAssignment != null
                && !chancellorAssignment.Id.EndsWith("_none", StringComparison.Ordinal)
                && IsAssignmentActive(kingdom, PrivyCouncilOffice.Chancellor, chancellorAssignment.Id))
            {
                string controversyAspect = chancellorAssignment.Id == "chancellor_appease_nobles"
                    ? CouncilIncidentAspects.NobleAppeasementWeeklyControversy
                    : chancellorAssignment.Id == "chancellor_improve_foreign_relations"
                        ? CouncilIncidentAspects.ForeignRelationsWeeklyControversy
                        : CouncilIncidentAspects.FabricationWeeklyControversy;
                float incidentMultiplier = Campaign.Current?
                    .GetCampaignBehavior<CouncilIncidentBehavior>()?
                    .GetAssignmentAspectMultiplier(
                        kingdom,
                        PrivyCouncilOffice.Chancellor,
                        chancellorAssignment.Id,
                        controversyAspect) ?? 1f;
                AddAssignmentControversy(
                    kingdom,
                    PrivyCouncilOffice.Chancellor,
                    chancellorAssignment.Id,
                    incidentMultiplier,
                    "political resentment over chancery policy");
            }

            if (IsAssignmentActive(kingdom, PrivyCouncilOffice.Seneschal, "seneschal_audit_vassals"))
            {
                float incidentMultiplier = Campaign.Current?
                    .GetCampaignBehavior<CouncilIncidentBehavior>()?
                    .GetAssignmentAspectMultiplier(
                        kingdom,
                        PrivyCouncilOffice.Seneschal,
                        "seneschal_audit_vassals",
                        CouncilIncidentAspects.AuditWeeklyControversy) ?? 1f;
                AddAssignmentControversy(
                    kingdom,
                    PrivyCouncilOffice.Seneschal,
                    "seneschal_audit_vassals",
                    incidentMultiplier,
                    "resentment over vassal audits");
            }

            if (IsAssignmentActive(kingdom, PrivyCouncilOffice.Spymaster, "spymaster_uncover_dissent"))
            {
                float incidentMultiplier = Campaign.Current?
                    .GetCampaignBehavior<CouncilIncidentBehavior>()?
                    .GetAssignmentAspectMultiplier(
                        kingdom,
                        PrivyCouncilOffice.Spymaster,
                        "spymaster_uncover_dissent",
                        CouncilIncidentAspects.DissentWeeklyControversy) ?? 1f;
                AddAssignmentControversy(
                    kingdom,
                    PrivyCouncilOffice.Spymaster,
                    "spymaster_uncover_dissent",
                    incidentMultiplier,
                    "resentment over domestic surveillance");
            }

            if (IsAssignmentActive(kingdom, PrivyCouncilOffice.Spymaster, "spymaster_sow_rumors"))
            {
                float incidentMultiplier = Campaign.Current?
                    .GetCampaignBehavior<CouncilIncidentBehavior>()?
                    .GetAssignmentAspectMultiplier(
                        kingdom,
                        PrivyCouncilOffice.Spymaster,
                        "spymaster_sow_rumors",
                        CouncilIncidentAspects.RumorWeeklyControversy) ?? 1f;
                AddAssignmentControversy(
                    kingdom,
                    PrivyCouncilOffice.Spymaster,
                    "spymaster_sow_rumors",
                    incidentMultiplier,
                    "exposure to covert intrigue");
            }
        }

        private void ApplySidelinedCouncillorRelations(Kingdom kingdom)
        {
            Hero ruler = kingdom?.RulingClan?.Leader;
            if (ruler == null)
                return;

            foreach (PrivyCouncilOfficeRecord record in GetOfficeRecords(kingdom))
            {
                Clan holder = ResolveClan(record?.HolderClanId);
                if (holder?.Leader == null
                    || holder.Leader == ruler
                    || holder.Leader.IsPrisoner)
                    continue;

                PrivyCouncilAssignmentDefinition assignment = GetOfficeAssignment(kingdom, record.Office);
                if (assignment == null
                    || !assignment.Id.EndsWith("_none", StringComparison.Ordinal))
                {
                    continue;
                }

                RelationMemoryService.ApplyChangeWithDefaultDuration(
                    ruler,
                    holder.Leader,
                    -1,
                    ruler == Hero.MainHero || holder.Leader == Hero.MainHero,
                    RelationMemorySources.SidelinedFromCouncil, RelationMemoryScope.Personal);
            }
        }

        private void AddAssignmentControversy(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            string assignmentId,
            float amount,
            string reason)
        {
            PrivyCouncilOfficeRecord record = GetOfficeRecord(kingdom, office);
            if (record == null || string.IsNullOrEmpty(record.HolderClanId))
                return;

            float drawbackMultiplier = GetAssignmentDrawbackMultiplier(
                kingdom,
                office,
                assignmentId);
            record.ChangeControversy(
                amount * Math.Max(0.5f, drawbackMultiplier),
                reason,
                (float)CampaignTime.Now.ToDays);
        }

        private float GetCombinedAdvisorCappedBonus(
            Kingdom kingdom,
            string assignmentSuffix,
            string aspectId,
            float ordinaryCap,
            float incidentCap)
        {
            float ordinaryTotal = 0f;
            float incidentDelta = 0f;
            foreach (PrivyCouncilOffice office in new[]
            {
                PrivyCouncilOffice.FirstAdvisor,
                PrivyCouncilOffice.SecondAdvisor
            })
            {
                string assignmentId = GetAdvisorAssignmentId(office, assignmentSuffix);
                float contribution = 5f * GetAssignmentEffectMultiplier(kingdom, office, assignmentId);
                ordinaryTotal += contribution;
                incidentDelta += contribution * (GetAdvisorAspectMultiplier(
                    kingdom,
                    office,
                    assignmentId,
                    aspectId) - 1f);
            }

            return Clamp(Math.Min(ordinaryCap, ordinaryTotal) + incidentDelta, 0f, incidentCap);
        }

        private float GetCombinedAdvisorAssignmentEfficiency(
            Kingdom kingdom,
            string assignmentSuffix,
            string aspectId,
            float ordinaryCap,
            float incidentCap)
        {
            float ordinaryTotal = 0f;
            float incidentDelta = 0f;
            foreach (PrivyCouncilOffice office in new[]
            {
                PrivyCouncilOffice.FirstAdvisor,
                PrivyCouncilOffice.SecondAdvisor
            })
            {
                string assignmentId = GetAdvisorAssignmentId(office, assignmentSuffix);
                float contribution = GetAssignmentEffectMultiplier(kingdom, office, assignmentId);
                ordinaryTotal += contribution;
                incidentDelta += contribution * (GetAdvisorAspectMultiplier(
                    kingdom,
                    office,
                    assignmentId,
                    aspectId) - 1f);
            }

            return Clamp(Math.Min(ordinaryCap, ordinaryTotal) + incidentDelta, 0f, incidentCap);
        }

        private float GetRepresentedFactionEffect(FactionObject faction, string aspectId)
        {
            if (faction?.ParentKingdom == null || !faction.IsIdeology)
                return 0f;

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            float best = 0f;
            foreach (PrivyCouncilOffice office in new[]
            {
                PrivyCouncilOffice.FirstAdvisor,
                PrivyCouncilOffice.SecondAdvisor
            })
            {
                Clan advisor = GetOfficeHolder(faction.ParentKingdom, office);
                if (advisor == null || factionManager?.GetIdeologicalFaction(advisor) != faction)
                    continue;

                string assignmentId = GetAdvisorAssignmentId(office, "represent_court");
                float incidentMultiplier = GetAdvisorAspectMultiplier(
                    faction.ParentKingdom,
                    office,
                    assignmentId,
                    aspectId);
                incidentMultiplier *= GetAdvisorAspectMultiplier(
                    faction.ParentKingdom,
                    office,
                    assignmentId,
                    CouncilIncidentAspects.AdvisorRepresentationOverall);
                incidentMultiplier = Clamp(incidentMultiplier, 0.25f, 2f);

                float contribution = 5f
                    * GetAssignmentEffectMultiplier(faction.ParentKingdom, office, assignmentId)
                    * incidentMultiplier;
                best = Math.Max(best, contribution);
            }

            return Math.Min(10f, best);
        }

        private static string GetAdvisorAssignmentId(PrivyCouncilOffice office, string assignmentSuffix)
        {
            return (office == PrivyCouncilOffice.FirstAdvisor ? "first_advisor_" : "second_advisor_")
                + assignmentSuffix;
        }

        private static float GetAdvisorAspectMultiplier(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            string assignmentId,
            string aspectId)
        {
            return Campaign.Current?
                .GetCampaignBehavior<CouncilIncidentBehavior>()?
                .GetAssignmentAspectMultiplier(kingdom, office, assignmentId, aspectId) ?? 1f;
        }

        private static string BuildAssignmentFundingKey(Kingdom kingdom, string assignmentId)
        {
            return (kingdom?.StringId ?? string.Empty) + ":" + (assignmentId ?? string.Empty);
        }

        private void EnsureAssignmentReviewScheduled(
            Kingdom kingdom,
            PrivyCouncilOfficeRecord record,
            float currentDay)
        {
            if (kingdom == null || record == null || record.NextAssignmentReviewDay > 0f)
                return;

            record.ScheduleAssignmentReview(currentDay + GetAssignmentReviewDelay(kingdom, record.Office));
        }

        private void TryReviewAiAssignment(Kingdom kingdom)
        {
            if (kingdom?.RulingClan == null)
                return;

            float currentDay = (float)CampaignTime.Now.ToDays;
            List<PrivyCouncilOfficeRecord> dueRecords = GetOfficeRecords(kingdom)
                .Where(record => record != null
                    && IsOfficeUnlocked(kingdom, record.Office)
                    && GetOfficeHolder(kingdom, record.Office)?.Leader?.IsPrisoner == false
                    && record.NextAssignmentReviewDay > 0f
                    && currentDay >= record.NextAssignmentReviewDay)
                .OrderBy(record => record.NextAssignmentReviewDay)
                .ThenBy(record => record.Office)
                .ToList();
            if (dueRecords.Count == 0)
                return;

            PrivyCouncilOfficeRecord recordToReview = dueRecords[0];
            if (kingdom.RulingClan == Clan.PlayerClan)
            {
                recordToReview.ScheduleAssignmentReview(
                    currentDay + GetAssignmentReviewDelay(kingdom, recordToReview.Office));
                return;
            }

            if (GetAssignmentCooldownRemaining(kingdom, recordToReview.Office) > 0f)
            {
                recordToReview.ScheduleAssignmentReview(
                    currentDay + GetAssignmentReviewDelay(kingdom, recordToReview.Office));
                return;
            }

            PrivyCouncilAssignmentContext context = BuildAssignmentContext(kingdom, recordToReview.Office);
            PrivyCouncilAssignmentDefinition current = GetOfficeAssignment(kingdom, recordToReview.Office);
            if (context == null || current == null)
            {
                recordToReview.ScheduleAssignmentReview(
                    currentDay + GetAssignmentReviewDelay(kingdom, recordToReview.Office));
                return;
            }

            List<PrivyCouncilAssignmentDefinition> assignments = GetAssignments(recordToReview.Office).ToList();
            AssignmentAiSnapshot snapshot = BuildAssignmentAiSnapshot(kingdom);
            float currentScore = EvaluateAssignmentForAi(current, context, snapshot) + AssignmentInertia;
            PrivyCouncilAssignmentDefinition best = current;
            float bestScore = currentScore;
            foreach (PrivyCouncilAssignmentDefinition assignment in assignments)
            {
                if (assignment == null || assignment == current)
                    continue;

                float score = EvaluateAssignmentForAi(assignment, context, snapshot);
                if (score > bestScore)
                {
                    best = assignment;
                    bestScore = score;
                }
            }

            if (best != current && bestScore >= currentScore + AssignmentSwitchMargin)
            {
                TrySetOfficeAssignment(
                    kingdom,
                    recordToReview.Office,
                    best.Id,
                    kingdom.RulingClan,
                    ignoreCooldown: false,
                    out TextObject _);
            }

            recordToReview.ScheduleAssignmentReview(
                currentDay + GetAssignmentReviewDelay(kingdom, recordToReview.Office));
        }

        private float EvaluateAssignmentForAi(
            PrivyCouncilAssignmentDefinition assignment,
            PrivyCouncilAssignmentContext context,
            AssignmentAiSnapshot snapshot)
        {
            return IsBuiltInAssignmentId(assignment?.Id)
                ? CalculateAssignmentSuitability(assignment.Id, context, snapshot)
                : assignment?.EvaluateAiSuitability(context) ?? float.MinValue;
        }

        private static bool IsBuiltInAssignmentId(string assignmentId)
        {
            if (string.IsNullOrEmpty(assignmentId))
                return false;

            return assignmentId.StartsWith("marshal_", StringComparison.Ordinal)
                || assignmentId.StartsWith("chancellor_", StringComparison.Ordinal)
                || assignmentId.StartsWith("seneschal_", StringComparison.Ordinal)
                || assignmentId.StartsWith("spymaster_", StringComparison.Ordinal)
                || assignmentId.StartsWith("first_advisor_", StringComparison.Ordinal)
                || assignmentId.StartsWith("second_advisor_", StringComparison.Ordinal);
        }

        private float CalculateAssignmentSuitability(
            string assignmentId,
            PrivyCouncilAssignmentContext context,
            AssignmentAiSnapshot snapshot)
        {
            if (context?.Kingdom == null || snapshot == null)
                return 0f;

            if (assignmentId.EndsWith("_none", StringComparison.Ordinal))
                return -100f;

            float score;
            switch (assignmentId)
            {
                case "marshal_organize_patrols":
                    score = (100f - snapshot.AverageSecurity) * 0.7f
                        + snapshot.Strongholds * 1.5f
                        + snapshot.ActiveWars * 5f;
                    break;
                case "marshal_train_militia":
                    score = 20f + snapshot.ActiveWars * 25f
                        + (100f - snapshot.AverageSecurity) * 0.25f;
                    break;
                case "marshal_oversee_logistics":
                    score = 15f + snapshot.ActiveWars * 35f;
                    break;
                case "chancellor_appease_nobles":
                    score = Math.Max(0f, 25f - snapshot.LowestVassalRelation) * 1.1f
                        + snapshot.ActiveConspiracies * 5f;
                    break;
                case "chancellor_improve_foreign_relations":
                    score = 20f + snapshot.StrainedNeighborRelations * 12f
                        - snapshot.ActiveWars * 4f;
                    break;
                case "chancellor_fabricate_grievances":
                    score = 15f + snapshot.ActiveFabrications * 35f
                        + snapshot.AvailableForeignClaimTargets * 8f
                        - snapshot.ActiveWars * 8f;
                    break;
                case "seneschal_audit_vassals":
                    score = snapshot.VassalClans * 6f
                        + Math.Max(0f, 50000f - snapshot.CrownGold) / 2500f;
                    break;
                case "seneschal_subsidize_infrastructure":
                    float infrastructureCostMultiplier = Clamp(
                        1.5f - context.Competence / 100f,
                        0.5f,
                        1.5f);
                    score = Math.Max(0f, 6000f - snapshot.AverageProsperity) / 100f
                        + (snapshot.CrownGold >= snapshot.Strongholds
                                * InfrastructureCostPerStronghold
                                * infrastructureCostMultiplier
                                * 30f
                            ? 15f
                            : -40f)
                        - snapshot.StarvingStrongholds * 8f;
                    break;
                case "seneschal_stockpile_provisions":
                    float provisionsCostMultiplier = Clamp(
                        1.5f - context.Competence / 100f,
                        0.5f,
                        1.5f);
                    score = snapshot.StarvingStrongholds * 35f
                        + snapshot.LowFoodStrongholds * 12f
                        + snapshot.ActiveWars * 10f
                        + (snapshot.CrownGold >= snapshot.Strongholds
                                * ProvisionsCostPerStronghold
                                * provisionsCostMultiplier
                                * 30f
                            ? 10f
                            : -30f);
                    break;
                case "spymaster_counter_espionage":
                    score = 20f + snapshot.ActiveWars * 12f
                        + snapshot.HostileForeignRealms * 4f;
                    break;
                case "spymaster_uncover_dissent":
                    score = snapshot.ActiveConspiracies * 30f
                        + Math.Max(0f, -snapshot.LowestFactionMood) * 0.6f;
                    break;
                case "spymaster_sow_rumors":
                    score = 15f + snapshot.ActiveFabrications * 15f
                        + snapshot.AvailableForeignClaimTargets * 6f
                        - snapshot.ActiveConspiracies * 5f;
                    break;
                default:
                    if (assignmentId.EndsWith("_counsel_crown", StringComparison.Ordinal))
                    {
                        score = 30f + Math.Max(0f, 60f - snapshot.AverageCoreCompetence) * 0.8f;
                    }
                    else if (assignmentId.EndsWith("_mediate_council", StringComparison.Ordinal))
                    {
                        score = snapshot.HighestCoreControversy * 1.2f
                            + Math.Max(0f, 60f - snapshot.LowestCoreSupport) * 0.5f;
                    }
                    else if (assignmentId.EndsWith("_represent_court", StringComparison.Ordinal))
                    {
                        score = 20f + Math.Max(0f, -snapshot.LowestFactionMood)
                            + snapshot.ActiveConspiracies * 8f;
                    }
                    else
                    {
                        score = 0f;
                    }
                    break;
            }

            score += GetAssignmentIdeologyPreference(context.Kingdom.RulingClan, assignmentId);
            score += GetAssignmentPersonalityPreference(context.Kingdom.RulingClan?.Leader, assignmentId);
            return score;
        }

        private AssignmentAiSnapshot BuildAssignmentAiSnapshot(Kingdom kingdom)
        {
            List<Town> strongholds = kingdom?.Fiefs.Where(town => town != null).ToList()
                ?? new List<Town>();
            List<Clan> vassals = GetEligibleCouncilClans(kingdom, includePrisoners: true);
            Hero ruler = kingdom?.RulingClan?.Leader;
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            List<FactionObject> factions = factionManager?.GetFactionsInKingdom(kingdom)
                .Where(faction => faction != null)
                .ToList() ?? new List<FactionObject>();
            List<FactionObject> ideologies = factions.Where(faction => faction.IsIdeology).ToList();
            List<ForeignPolicyEvaluation> foreignTargets = kingdom == null
                ? new List<ForeignPolicyEvaluation>()
                : new ForeignPolicyEvaluationService().EvaluateWarTargets(kingdom).ToList();

            float averageCoreCompetence = Enum.GetValues(typeof(PrivyCouncilOffice))
                .Cast<PrivyCouncilOffice>()
                .Where(office => office <= PrivyCouncilOffice.Spymaster)
                .Select(office => GetOfficeCompetence(kingdom, office))
                .DefaultIfEmpty(0f)
                .Average();
            float lowestCoreSupport = Enum.GetValues(typeof(PrivyCouncilOffice))
                .Cast<PrivyCouncilOffice>()
                .Where(office => office <= PrivyCouncilOffice.Spymaster)
                .Select(office =>
                {
                    Clan holder = GetOfficeHolder(kingdom, office);
                    return holder == null ? 0f : CalculateSupportPercent(kingdom, holder, office);
                })
                .DefaultIfEmpty(0f)
                .Min();

            return new AssignmentAiSnapshot
            {
                ActiveWars = Kingdom.All.Count(other => other != null
                    && other != kingdom
                    && !other.IsEliminated
                    && kingdom.IsAtWarWith(other)),
                Strongholds = strongholds.Count,
                StarvingStrongholds = strongholds.Count(town => town.FoodStocks <= 0f),
                LowFoodStrongholds = strongholds.Count(town => town.FoodStocks > 0f && town.FoodStocks < 50f),
                AverageSecurity = strongholds.Select(town => town.Security).DefaultIfEmpty(50f).Average(),
                AverageProsperity = strongholds.Select(town => town.Prosperity).DefaultIfEmpty(0f).Average(),
                VassalClans = vassals.Count,
                LowestVassalRelation = ruler == null
                    ? 0f
                    : vassals.Select(clan => (float)clan.Leader.GetRelation(ruler)).DefaultIfEmpty(25f).Min(),
                LowestFactionMood = ideologies.Select(faction => faction.Mood).DefaultIfEmpty(0f).Min(),
                ActiveConspiracies = factions.Count(faction => !faction.IsIdeology && !faction.IsCivilWarActive()),
                ActiveFabrications = Campaign.Current?.GetCampaignBehavior<FeudalClaimFabricationBehavior>()?
                    .GetActiveFabrications()
                    .Count(record => record != null
                        && record.IsActive
                        && record.FabricatorClanId == kingdom?.RulingClan?.StringId) ?? 0,
                AvailableForeignClaimTargets = foreignTargets.Count(target => target.ClaimStakes.Count > 0),
                StrainedNeighborRelations = ruler == null
                    ? 0
                    : foreignTargets.Count(target => target.IsNeighboringRealm
                        && target.TargetKingdom?.RulingClan?.Leader != null
                        && ruler.GetRelation(target.TargetKingdom.RulingClan.Leader) < 0),
                HostileForeignRealms = foreignTargets.Count(target => target.ProvisionalUrgency >= 50f),
                CrownGold = ruler?.Gold ?? 0,
                AverageCoreCompetence = averageCoreCompetence,
                HighestCoreControversy = GetOfficeRecords(kingdom)
                    .Where(record => record.Office <= PrivyCouncilOffice.Spymaster)
                    .Select(record => record.Controversy)
                    .DefaultIfEmpty(0f)
                    .Max(),
                LowestCoreSupport = lowestCoreSupport
            };
        }

        private static float GetAssignmentIdeologyPreference(Clan holder, string assignmentId)
        {
            FactionObject faction = Campaign.Current?
                .GetCampaignBehavior<FactionManagerBehavior>()?
                .GetIdeologicalFaction(holder);
            if (faction == null)
                return 0f;

            switch (faction.Type)
            {
                case FactionType.Glory:
                    return assignmentId == "marshal_train_militia"
                        || assignmentId == "marshal_oversee_logistics"
                        || assignmentId == "spymaster_sow_rumors" ? 12f : 0f;
                case FactionType.Liberty:
                    return assignmentId == "chancellor_appease_nobles"
                        || assignmentId == "seneschal_subsidize_infrastructure"
                        || assignmentId == "seneschal_stockpile_provisions"
                        || assignmentId.EndsWith("_represent_court", StringComparison.Ordinal) ? 12f : 0f;
                case FactionType.Royalists:
                    return assignmentId == "spymaster_counter_espionage"
                        || assignmentId.EndsWith("_counsel_crown", StringComparison.Ordinal) ? 12f : 0f;
                case FactionType.Nobility:
                    return assignmentId == "seneschal_audit_vassals"
                        || assignmentId == "chancellor_fabricate_grievances"
                        || assignmentId.EndsWith("_represent_court", StringComparison.Ordinal) ? 12f : 0f;
                default:
                    return 0f;
            }
        }

        private static float GetAssignmentPersonalityPreference(Hero ruler, string assignmentId)
        {
            if (ruler == null)
                return 0f;

            float score = 0f;
            int calculating = ruler.GetTraitLevel(DefaultTraits.Calculating);
            int generosity = ruler.GetTraitLevel(DefaultTraits.Generosity);
            int honor = ruler.GetTraitLevel(DefaultTraits.Honor);
            int mercy = ruler.GetTraitLevel(DefaultTraits.Mercy);
            int valor = ruler.GetTraitLevel(DefaultTraits.Valor);

            if (assignmentId.Contains("counter_espionage")
                || assignmentId.Contains("uncover_dissent")
                || assignmentId.Contains("audit_vassals")
                || assignmentId.Contains("counsel_crown"))
            {
                score += calculating * 4f;
            }
            if (assignmentId.Contains("appease_nobles")
                || assignmentId.Contains("subsidize_infrastructure")
                || assignmentId.Contains("stockpile_provisions"))
            {
                score += generosity * 4f + mercy * 3f;
            }
            if (assignmentId.Contains("fabricate_grievances") || assignmentId.Contains("sow_rumors"))
                score -= honor * 4f + mercy * 2f;
            if (assignmentId.Contains("oversee_logistics"))
                score += calculating * 3f + valor * 2f;
            if (assignmentId.Contains("train_militia") || assignmentId.Contains("organize_patrols"))
                score += valor * 3f;

            return score;
        }

        private static float GetAssignmentReviewDelay(Kingdom kingdom, PrivyCouncilOffice office)
        {
            string key = (kingdom?.StringId ?? string.Empty) + ":" + office;
            int hash = 17;
            foreach (char character in key)
                hash = unchecked(hash * 31 + character);
            float offset = Math.Abs(hash % ((int)AssignmentReviewVarianceDays + 1));
            return AssignmentReviewMinimumDays + offset;
        }

        private sealed class AssignmentAiSnapshot
        {
            public int ActiveWars;
            public int Strongholds;
            public int StarvingStrongholds;
            public int LowFoodStrongholds;
            public int VassalClans;
            public int ActiveConspiracies;
            public int ActiveFabrications;
            public int AvailableForeignClaimTargets;
            public int StrainedNeighborRelations;
            public int HostileForeignRealms;
            public float AverageSecurity;
            public float AverageProsperity;
            public float LowestVassalRelation;
            public float LowestFactionMood;
            public float CrownGold;
            public float AverageCoreCompetence;
            public float HighestCoreControversy;
            public float LowestCoreSupport;
        }

        public bool IsOfficeUnlocked(Kingdom kingdom, PrivyCouncilOffice office)
        {
            if (office <= PrivyCouncilOffice.Spymaster)
                return IsEligiblePermanentRealm(kingdom);

            if (office == PrivyCouncilOffice.FirstAdvisor)
                return BellumKingdomVisibilityHelper.CountStrongholds(kingdom) > FirstAdvisorStrongholdRequirement;

            FeudalTitleType? sovereignRank = FeudalPoliticalWeightHelper.GetHighestHeldTitleRank(kingdom?.RulingClan);
            return sovereignRank == FeudalTitleType.Empire;
        }

        public bool IsVacancyExcused(Kingdom kingdom, PrivyCouncilOffice office)
        {
            if (office > PrivyCouncilOffice.Spymaster || !IsOfficeUnlocked(kingdom, office))
                return true;

            int sustainableSeats = Math.Min(4, GetEligibleCouncilClans(kingdom, includePrisoners: true).Count);
            return (int)office >= sustainableSeats;
        }

        public float GetVacancyDays(PrivyCouncilOfficeRecord record)
        {
            if (record == null || !string.IsNullOrEmpty(record.HolderClanId) || record.VacancyStartedDay < 0f)
                return 0f;

            return (float)Math.Max(0d, CampaignTime.Now.ToDays - record.VacancyStartedDay);
        }

        public float GetOfficeCompetence(Kingdom kingdom, PrivyCouncilOffice office)
        {
            return GetRuntimeState(kingdom, office)?.RawCompetence ?? 0f;
        }

        public float GetOfficeSupportPercent(Kingdom kingdom, PrivyCouncilOffice office)
        {
            Clan holder = GetOfficeHolder(kingdom, office);
            if (holder == null)
                return 0f;

            float support = CalculateSupportPercent(kingdom, holder, office);
            if (office <= PrivyCouncilOffice.Spymaster)
                support += GetMediateCouncilSupportBonus(kingdom);
            return Clamp(support, 0f, 100f);
        }

        public float GetRulerControversy(Kingdom kingdom)
        {
            if (kingdom == null)
                return 0f;

            float controversy = 0f;
            int coreOffices = 0;
            foreach (PrivyCouncilOfficeRecord record in GetOfficeRecords(kingdom))
            {
                if (record.Office > PrivyCouncilOffice.Spymaster)
                    continue;

                controversy += record.Controversy;
                coreOffices++;
            }

            return coreOffices == 0 ? 0f : controversy / 4f;
        }

        public IReadOnlyList<Clan> GetAppointmentCandidates(Kingdom kingdom, PrivyCouncilOffice office)
        {
            return GetAppointmentCandidatesForVote(kingdom, office);
        }

        public IReadOnlyList<Clan> GetAppointmentCandidatesForVote(Kingdom kingdom, PrivyCouncilOffice office)
        {
            if (!IsOfficeUnlocked(kingdom, office))
                return new List<Clan>();

            Clan incumbent = GetOfficeHolder(kingdom, office);
            HashSet<string> occupiedClanIds = new HashSet<string>(GetOfficeRecords(kingdom)
                .Where(record => record != null && !string.IsNullOrEmpty(record.HolderClanId))
                .Select(record => record.HolderClanId));

            return GetEligibleCouncilClans(kingdom, includePrisoners: false)
                .Where(candidate => candidate != kingdom.RulingClan
                    && (candidate == incumbent || !occupiedClanIds.Contains(candidate.StringId)))
                .OrderByDescending(candidate => CalculateAppointmentMerit(kingdom, candidate, office))
                .ThenBy(candidate => candidate.StringId)
                .ToList();
        }

        public IReadOnlyList<Clan> GetAppointmentChallengers(Kingdom kingdom, PrivyCouncilOffice office)
        {
            Clan incumbent = GetOfficeHolder(kingdom, office);
            return GetAppointmentCandidatesForVote(kingdom, office)
                .Where(candidate => candidate != incumbent)
                .ToList();
        }

        public bool HasAppointmentContest(Kingdom kingdom, PrivyCouncilOffice office)
        {
            Clan incumbent = GetOfficeHolder(kingdom, office);
            return incumbent == null
                ? GetAppointmentCandidatesForVote(kingdom, office).Count > 0
                : GetAppointmentChallengers(kingdom, office).Count > 0;
        }

        public float GetOfficeTenureDays(Kingdom kingdom, PrivyCouncilOffice office)
        {
            PrivyCouncilOfficeRecord record = GetOfficeRecord(kingdom, office);
            return record == null || record.AppointedDay < 0f
                ? 0f
                : Math.Max(0f, (float)CampaignTime.Now.ToDays - record.AppointedDay);
        }

        public float CalculateAppointmentMerit(Kingdom kingdom, Clan candidate, PrivyCouncilOffice office)
        {
            if (kingdom == null
                || candidate?.Leader == null
                || candidate == kingdom.RulingClan)
                return -100f;

            float competence = CalculateCompetence(candidate.Leader, office);
            float support = CalculateSupportPercent(kingdom, candidate, office);
            float rank = GetPoliticalWeight(candidate);
            float renown = Math.Min(10f, Math.Max(0f, candidate.Renown) / 200f);
            return competence * 0.45f
                + support * 0.35f
                + rank * 2f
                + renown
                - GetDisgracePenalty(kingdom, candidate);
        }

        public float GetCouncilInfluenceGain(Clan clan)
        {
            if (clan == null || clan.Kingdom == null)
                return 0f;

            int heldSeats = GetOfficeRecords(clan.Kingdom).Count(record => record != null
                && record.HolderClanId == clan.StringId
                && IsOfficeUnlocked(clan.Kingdom, record.Office));
            return heldSeats * DailyCouncilInfluence;
        }

        public int GetDailySalary(Kingdom kingdom, PrivyCouncilOffice office)
        {
            Clan holder = GetOfficeHolder(kingdom, office);
            if (holder?.Leader == null || holder == kingdom?.RulingClan)
                return 0;

            float competence = CalculateCompetence(holder.Leader, office);
            return (int)Math.Round(BaseDailyCouncilSalary * GetSalaryMultiplier(competence));
        }

        public int GetFactionMotionInfluenceCost(Clan actingClan, int baseCost)
        {
            if (actingClan?.Kingdom == null || baseCost <= 0)
                return Math.Max(0, baseCost);

            Kingdom kingdom = actingClan.Kingdom;
            Clan chancellor = GetOfficeHolder(kingdom, PrivyCouncilOffice.Chancellor);
            if (chancellor?.Leader == null)
                return baseCost;

            FactionManagerBehavior factions = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject actingFaction = factions?.GetIdeologicalFaction(actingClan);
            FactionObject chancellorFaction = factions?.GetIdeologicalFaction(chancellor);
            if (actingFaction == null || actingFaction != chancellorFaction)
                return baseCost;

            float competence = GetEffectiveCoreOfficeCompetence(kingdom, PrivyCouncilOffice.Chancellor);
            float discountRate = Clamp(competence / 100f * 0.25f, 0f, 0.25f);
            return Math.Max(0, (int)Math.Round(baseCost * (1f - discountRate)));
        }

        public float GetClaimFeudPressureMultiplier(Kingdom kingdom)
        {
            if (kingdom == null)
                return 1f;

            Clan chancellor = GetOfficeHolder(kingdom, PrivyCouncilOffice.Chancellor);
            float competence = chancellor?.Leader == null
                ? 0f
                : GetEffectiveCoreOfficeCompetence(kingdom, PrivyCouncilOffice.Chancellor);
            return Clamp(1.5f - competence / 100f, 0.5f, 1.5f);
        }

        public float GetSeneschalTaxBonusRate(Kingdom kingdom)
        {
            Clan seneschal = GetOfficeHolder(kingdom, PrivyCouncilOffice.Seneschal);
            if (seneschal?.Leader == null)
                return 0f;

            float competence = GetEffectiveCoreOfficeCompetence(kingdom, PrivyCouncilOffice.Seneschal);
            return Clamp(competence / 100f * 0.10f, 0f, 0.10f);
        }

        public Hero GetIntrigueProxy(Kingdom kingdom)
        {
            Clan spymaster = GetOfficeHolder(kingdom, PrivyCouncilOffice.Spymaster);
            return spymaster?.Leader ?? kingdom?.RulingClan?.Leader;
        }

        public float GetIntrigueSkillModifier(Kingdom kingdom, bool actingSide)
        {
            Clan spymaster = GetOfficeHolder(kingdom, PrivyCouncilOffice.Spymaster);
            if (spymaster?.Leader == null)
                return actingSide ? 0f : -50f;

            float competence = GetEffectiveCoreOfficeCompetence(kingdom, PrivyCouncilOffice.Spymaster);
            float assignmentModifier = 0f;
            CouncilIncidentBehavior incidents = Campaign.Current?
                .GetCampaignBehavior<CouncilIncidentBehavior>();
            if (actingSide)
            {
                float rumorOffense = incidents?.GetAssignmentAspectMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Spymaster,
                    "spymaster_sow_rumors",
                    CouncilIncidentAspects.RumorOffense) ?? 1f;
                float counterOffensePenalty = incidents?.GetAssignmentAspectMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Spymaster,
                    "spymaster_counter_espionage",
                    CouncilIncidentAspects.CounterEspionageOffensePenalty) ?? 1f;
                assignmentModifier += 15f * GetAssignmentEffectMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Spymaster,
                    "spymaster_sow_rumors") * rumorOffense;
                assignmentModifier -= 5f * GetAssignmentDrawbackMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Spymaster,
                    "spymaster_counter_espionage") * counterOffensePenalty;
            }
            else
            {
                float counterDefense = incidents?.GetAssignmentAspectMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Spymaster,
                    "spymaster_counter_espionage",
                    CouncilIncidentAspects.CounterEspionageDefense) ?? 1f;
                float rumorDefensePenalty = incidents?.GetAssignmentAspectMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Spymaster,
                    "spymaster_sow_rumors",
                    CouncilIncidentAspects.RumorDefensePenalty) ?? 1f;
                assignmentModifier += 15f * GetAssignmentEffectMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Spymaster,
                    "spymaster_counter_espionage") * counterDefense;
                assignmentModifier -= 5f * GetAssignmentDrawbackMultiplier(
                    kingdom,
                    PrivyCouncilOffice.Spymaster,
                    "spymaster_sow_rumors") * rumorDefensePenalty;
            }

            return (competence - 50f) * 0.5f + assignmentModifier;
        }

        public float GetDisgraceDaysRemaining(Kingdom kingdom, Clan clan)
        {
            if (kingdom == null || clan == null)
                return 0f;

            string key = BuildDisgraceKey(kingdom, clan);
            if (!_disgracedUntilByClan.TryGetValue(key, out float untilDay))
                return 0f;

            return Math.Max(0f, untilDay - (float)CampaignTime.Now.ToDays);
        }

        public float GetDismissalGrievance(Kingdom kingdom, Clan clan)
        {
            if (kingdom == null || clan == null)
                return 0f;

            string key = BuildDisgraceKey(kingdom, clan);
            if (!_disgraceIntentByClan.TryGetValue(key, out float initialGrievance))
                return 0f;

            float remainingDays = GetDisgraceDaysRemaining(kingdom, clan);
            return remainingDays <= 0f
                ? 0f
                : initialGrievance * Math.Min(1f, remainingDays / DisgraceDurationDays);
        }

        public void GetRebelliousIntentComponents(
            Clan clan,
            out float seatAmbition,
            out float factionRepresentation,
            out float dismissalGrievance)
        {
            seatAmbition = 0f;
            factionRepresentation = 0f;
            dismissalGrievance = 0f;
            Kingdom kingdom = clan?.Kingdom;
            if (!IsEligiblePermanentRealm(kingdom)
                || clan == kingdom.RulingClan
                || !NobleClanEligibilityHelper.IsLiveNobleClan(clan))
            {
                return;
            }

            EnsureCouncilForKingdom(kingdom);
            dismissalGrievance = GetDismissalGrievance(kingdom, clan);
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject clanFaction = factionManager?.GetIdeologicalFaction(clan);
            Dictionary<FactionObject, int> seatsByFaction = new Dictionary<FactionObject, int>();
            bool holdsSeat = false;

            foreach (PrivyCouncilOfficeRecord record in _officeRecords)
            {
                if (record == null
                    || record.KingdomId != kingdom.StringId
                    || !IsOfficeUnlocked(kingdom, record.Office)
                    || string.IsNullOrEmpty(record.HolderClanId))
                {
                    continue;
                }

                Clan holder = ResolveClan(record.HolderClanId);
                if (holder == null)
                    continue;

                if (holder == clan)
                    holdsSeat = true;

                FactionObject holderFaction = factionManager?.GetIdeologicalFaction(holder);
                if (holderFaction == null)
                    continue;

                seatsByFaction.TryGetValue(holderFaction, out int count);
                seatsByFaction[holderFaction] = count + 1;
            }

            if (!holdsSeat)
                seatAmbition = GetCouncilSeatAmbition(clan);

            if (clanFaction != null && seatsByFaction.TryGetValue(clanFaction, out int friendlySeats) && friendlySeats > 0)
            {
                factionRepresentation = -10f;
                factionRepresentation -= GetRepresentedFactionIntentRelief(clanFaction);
                return;
            }

        }

        public float GetRebelliousIntentModifier(Clan clan)
        {
            GetRebelliousIntentComponents(clan, out float ambition, out float representation, out float dismissal);
            return ambition + representation + dismissal;
        }

        public float CalculateAppointmentSupport(
            Kingdom kingdom,
            Clan voter,
            Clan candidate,
            PrivyCouncilOffice office)
        {
            if (kingdom == null
                || voter?.Leader == null
                || candidate?.Leader == null
                || voter.Kingdom != kingdom
                || candidate.Kingdom != kingdom
                || candidate == kingdom.RulingClan)
            {
                return -100f;
            }

            float score = voter == candidate ? 35f : voter.Leader.GetRelation(candidate.Leader) * 0.55f;
            score += CalculateCompetence(candidate.Leader, office) * 0.35f;
            score += GetPoliticalWeight(candidate) * 2f;
            score += Math.Min(10f, Math.Max(0f, candidate.Renown) / 200f);

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject voterFaction = factionManager?.GetIdeologicalFaction(voter);
            FactionObject candidateFaction = factionManager?.GetIdeologicalFaction(candidate);
            if (voterFaction != null && candidateFaction != null)
            {
                if (voterFaction == candidateFaction)
                    score += 15f;
            }

            if (MarriageAllianceHelper.HasMarriageAlliance(voter, candidate))
                score += 15f;

            if (candidate == GetOfficeHolder(kingdom, office))
            {
                float controversy = GetOfficeRecord(kingdom, office)?.Controversy ?? 100f;
                score += 15f - controversy * 0.35f;
            }

            return Clamp(score, -100f, 100f);
        }

        public bool TryAppointOffice(Kingdom kingdom, PrivyCouncilOffice office, Clan candidate, bool showNotification = true)
        {
            if (kingdom == null
                || candidate == null
                || candidate == kingdom.RulingClan
                || !IsOfficeUnlocked(kingdom, office))
                return false;

            Clan incumbent = GetOfficeHolder(kingdom, office);
            if (candidate == incumbent)
            {
                BellumCivileLogger.Log($"Privy council retained incumbent; kingdom={kingdom.StringId}; office={office}; clan={candidate.StringId}.");
                return true;
            }

            if (!GetAppointmentCandidatesForVote(kingdom, office).Contains(candidate))
                return false;

            PrivyCouncilOfficeRecord record = GetOfficeRecord(kingdom, office);
            if (record == null)
                return false;

            float currentDay = (float)CampaignTime.Now.ToDays;
            GetIncidentBehavior()?.ClearAssignmentAspectModifiers(kingdom, office);
            record.Appoint(candidate.StringId, currentDay);
            PrivyCouncilAssignmentDefinition defaultAssignment = PrivyCouncilAssignmentRegistry.GetDefaultAssignment(office);
            record.RepairAssignment(
                defaultAssignment?.Id ?? string.Empty,
                currentDay - AssignmentCooldownDays,
                currentDay);
            record.ScheduleAssignmentReview(currentDay);
            InvalidateRuntimeCache();
            string disgraceKey = BuildDisgraceKey(kingdom, candidate);
            _disgracedUntilByClan.Remove(disgraceKey);
            _disgraceIntentByClan.Remove(disgraceKey);
            _captivityDismissalUntilByClan.Remove(disgraceKey);

            if (showNotification)
            {
                TextObject message = new TextObject("{=BC_Council_Appointed}{CANDIDATE_NAME} has been appointed {OFFICE} on the {COUNCIL_NAME} of {KINGDOM_NAME}.");
                message.SetTextVariable("CANDIDATE_NAME", candidate.Leader?.Name ?? candidate.Name);
                message.SetTextVariable("OFFICE", GetLocalizedOfficeName(office, kingdom));
                message.SetTextVariable("KINGDOM_NAME", kingdom.Name);
                message = CourtInstitutionDisplayHelper.ApplyPrivyCouncilName(message, kingdom);
                BellumCivileNotifications.Show(
                    message,
                    BellumNotificationColors.Politics,
                    primaryKingdom: kingdom,
                    primaryClan: candidate,
                    isPersonal: kingdom == Clan.PlayerClan?.Kingdom);
            }
            return true;
        }

        public bool TryDismissOfficeHolderByRuler(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            Clan actingClan,
            out TextObject failureReason)
        {
            failureReason = TextObject.GetEmpty();
            if (kingdom == null || kingdom.IsEliminated || actingClan == null || actingClan != kingdom.RulingClan)
            {
                failureReason = new TextObject("{=BC_Council_DismissOnlyRuler}Only the ruler may outright dismiss a councillor.");
                return false;
            }

            if (!IsOfficeUnlocked(kingdom, office))
            {
                failureReason = new TextObject("{=BC_Council_ProposalLockedOffice}This council office is not available to the realm.");
                return false;
            }

            CouncilAppointmentDeliberationBehavior deliberation =
                Campaign.Current?.GetCampaignBehavior<CouncilAppointmentDeliberationBehavior>();
            if (deliberation?.HasPendingAppointment(kingdom) == true
                || kingdom.UnresolvedDecisions.OfType<PrivyCouncilAppointmentDecision>().Any())
            {
                failureReason = new TextObject("{=BC_Council_DismissPending}A council appointment is already before the realm and must be settled first.");
                return false;
            }

            PrivyCouncilOfficeRecord record = GetOfficeRecord(kingdom, office);
            Clan formerHolder = ResolveClan(record?.HolderClanId);
            if (record == null || formerHolder == null)
            {
                failureReason = new TextObject("{=BC_Council_DismissVacant}Select an occupied council office before dismissing a councillor.");
                return false;
            }

            if (actingClan.Influence < DirectDismissalInfluenceCost)
            {
                TextObject influence = new TextObject("{=BC_Council_DismissInfluence}You need {COST} influence to dismiss this councillor outright.");
                influence.SetTextVariable("COST", DirectDismissalInfluenceCost);
                failureReason = influence;
                return false;
            }

            Hero ruler = actingClan.Leader;
            Hero councilor = formerHolder.Leader;
            ChangeClanInfluenceAction.Apply(actingClan, -DirectDismissalInfluenceCost);
            record.Vacate((float)CampaignTime.Now.ToDays);
            InvalidateRuntimeCache();

            if (ruler != null && councilor != null && ruler != councilor)
            {
                RelationMemoryService.ApplyChange(
                    councilor,
                    ruler,
                    BellumCivileConstants.CouncilDismissalRelationPenalty,
                    councilor == Hero.MainHero || ruler == Hero.MainHero,
                    RelationMemorySources.DismissedMeFromCouncil,
                    10f,
                    RelationMemoryScope.Personal,
                    GetLocalizedOfficeName(office, kingdom).ToString());
            }

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject dismissedFaction = factionManager?.GetIdeologicalFaction(formerHolder);
            Campaign.Current?
                .GetCampaignBehavior<IdeologyEventShockBehavior>()?
                .RecordCouncilAppointmentReaction(
                    dismissedFaction,
                    CouncilAppointmentReaction.MemberDismissed,
                    BellumCivileConstants.CouncilDismissalFactionMood);

            TextObject message = new TextObject("{=BC_Council_DismissedByRuler}{RULER_NAME} has dismissed {COUNCILOR_NAME} as {OFFICE} from the {COUNCIL_NAME} of {KINGDOM_NAME}.");
            message.SetTextVariable("RULER_NAME", ruler?.Name ?? actingClan.Name);
            message.SetTextVariable("COUNCILOR_NAME", councilor?.Name ?? formerHolder.Name);
            message.SetTextVariable("OFFICE", GetLocalizedOfficeName(office, kingdom));
            message.SetTextVariable("KINGDOM_NAME", kingdom.Name);
            message = CourtInstitutionDisplayHelper.ApplyPrivyCouncilName(message, kingdom);
            BellumCivileNotifications.Show(
                message,
                BellumNotificationColors.Warning,
                primaryKingdom: kingdom,
                primaryClan: formerHolder,
                isPersonal: kingdom == Clan.PlayerClan?.Kingdom);
            BellumCivileLogger.Log($"Ruler dismissed privy councillor; kingdom={kingdom.StringId}; office={office}; holder={formerHolder.StringId}; ruler={actingClan.StringId}; influence_cost={DirectDismissalInfluenceCost}.");
            return true;
        }

        public int ApplyDismissalAftermath(
            Kingdom kingdom,
            Clan formerHolder,
            float controversy,
            PrivyCouncilOffice? office = null,
            bool captivity = false)
        {
            if (kingdom == null || formerHolder == null)
                return 0;

            int severity = DismissalSeverity(controversy, captivity);
            float currentDay = (float)CampaignTime.Now.ToDays;
            string key = BuildDisgraceKey(kingdom, formerHolder);
            _disgracedUntilByClan[key] = currentDay + DisgraceDurationDays;
            _disgraceIntentByClan[key] = severity;
            if (captivity) _captivityDismissalUntilByClan[key] = currentDay + DisgraceDurationDays;
            else _captivityDismissalUntilByClan.Remove(key);

            Hero ruler = kingdom.RulingClan?.Leader;
            Hero councilor = formerHolder.Leader;
            if (ruler != null && councilor != null && ruler != councilor)
            {
                RelationMemoryService.ApplyChange(
                    councilor,
                    ruler,
                    -severity,
                    councilor == Hero.MainHero || ruler == Hero.MainHero,
                    captivity ? RelationMemorySources.RelievedDuringCaptivity : RelationMemorySources.DismissedMeFromCouncil,
                    10f,
                    RelationMemoryScope.Personal,
                    office.HasValue ? GetLocalizedOfficeName(office.Value, kingdom).ToString() : null);
            }

            return severity;
        }

        public void AddControversy(Kingdom kingdom, PrivyCouncilOffice office, float amount, string reason)
        {
            if (kingdom == null || amount == 0f)
                return;

            EnsureCouncilForKingdom(kingdom);
            PrivyCouncilOfficeRecord record = GetOfficeRecord(kingdom, office);
            if (record == null || !IsOfficeUnlocked(kingdom, office))
                return;

            float scaledAmount = amount;
            if (scaledAmount > 0f && !string.IsNullOrEmpty(record.HolderClanId))
            {
                float competence = office <= PrivyCouncilOffice.Spymaster
                    ? GetEffectiveCoreOfficeCompetence(kingdom, office)
                    : CalculateCompetence(GetOfficeHolder(kingdom, office)?.Leader, office);
                scaledAmount *= GetShockMultiplier(competence);
            }

            if (scaledAmount > 0f
                && office == PrivyCouncilOffice.Marshal
                && IsRoadSecurityFailure(reason))
            {
                bool organizingPatrols = IsAssignmentActive(
                    kingdom,
                    PrivyCouncilOffice.Marshal,
                    "marshal_organize_patrols");
                if (organizingPatrols)
                {
                    float drawbackMultiplier = GetAssignmentDrawbackMultiplier(
                        kingdom,
                        PrivyCouncilOffice.Marshal,
                        "marshal_organize_patrols");
                    float incidentMultiplier = Campaign.Current?
                        .GetCampaignBehavior<CouncilIncidentBehavior>()?
                        .GetAssignmentAspectMultiplier(
                            kingdom,
                            PrivyCouncilOffice.Marshal,
                            "marshal_organize_patrols",
                            CouncilIncidentAspects.RoadFailureControversy) ?? 1f;
                    scaledAmount *= 1f
                        + 0.25f
                        * Math.Max(0.5f, drawbackMultiplier)
                        * incidentMultiplier;
                }
            }

            record.ChangeControversy(scaledAmount, reason, (float)CampaignTime.Now.ToDays);
        }

        public static float CalculateCompetence(Hero hero, PrivyCouncilOffice office)
        {
            if (hero == null)
                return 0f;

            var factors = GetCompetenceFactors(office);
            int firstSkill = hero.GetSkillValue(factors.First);
            int secondSkill = hero.GetSkillValue(factors.Second);
            int attribute = hero.GetAttributeValue(factors.Attribute);

            float skillPart = Clamp(firstSkill, 0, 300) / 300f * 40f
                + Clamp(secondSkill, 0, 300) / 300f * 40f;
            float attributePart = Clamp(attribute, 0, 10) / 10f * 20f;
            return Clamp(skillPart + attributePart, 0f, 100f);
        }

        internal static (SkillObject First, SkillObject Second, CharacterAttribute Attribute) GetCompetenceFactors(PrivyCouncilOffice office)
        {
            switch (office)
            {
                case PrivyCouncilOffice.Marshal:
                    return (DefaultSkills.Tactics, DefaultSkills.Leadership, DefaultCharacterAttributes.Endurance);
                case PrivyCouncilOffice.Chancellor:
                    return (DefaultSkills.Charm, DefaultSkills.Steward, DefaultCharacterAttributes.Social);
                case PrivyCouncilOffice.Seneschal:
                    return (DefaultSkills.Steward, DefaultSkills.Trade, DefaultCharacterAttributes.Intelligence);
                case PrivyCouncilOffice.Spymaster:
                    return (DefaultSkills.Roguery, DefaultSkills.Scouting, DefaultCharacterAttributes.Cunning);
                default:
                    return (DefaultSkills.Charm, DefaultSkills.Leadership, DefaultCharacterAttributes.Social);
            }
        }

        public static string GetCompetenceTierKey(float competence)
        {
            if (competence < 20f) return "Inapt";
            if (competence < 40f) return "Mediocre";
            if (competence < 60f) return "Average";
            if (competence < 80f) return "Skillful";
            return "Masterful";
        }

        private void OnDailyTick()
        {
            InvalidateRuntimeCache(recordsChanged: true);
            float currentDay = (float)CampaignTime.Now.ToDays;
            foreach (Kingdom kingdom in Kingdom.All.Where(IsEligiblePermanentRealm).ToList())
            {
                try
                {
                    EnsureCouncilForKingdom(kingdom);
                    foreach (PrivyCouncilOfficeRecord record in GetOfficeRecords(kingdom))
                    {
                        ValidateHolder(kingdom, record, currentDay);
                        if (TryRelieveCaptiveOfficeHolder(kingdom, record, currentDay))
                            continue;
                        if (TryDismissDisgracedOfficeHolder(kingdom, record, currentDay))
                            continue;

                        Clan holder = ResolveClan(record.HolderClanId);
                        if (holder?.Leader?.IsPrisoner == true)
                        {
                            CancelCaptiveCouncillorAssignment(kingdom, record, currentDay);
                            record.ChangeControversy(
                                CaptiveCouncillorControversyPerDay,
                                "councillor held captive",
                                currentDay);
                            continue;
                        }

                        ApplyDailyAssignmentEffect(kingdom, record);

                        if (record.Office > PrivyCouncilOffice.Spymaster)
                            continue;

                        if (holder != null)
                        {
                            float recovery = GetDailyRecovery(GetEffectiveCoreOfficeCompetence(kingdom, record.Office));
                            record.ChangeControversy(-recovery, "competent administration", currentDay);
                        }
                        else if (!IsVacancyExcused(kingdom, record.Office)
                            && GetVacancyDays(record) > VacancyGraceDays)
                        {
                            record.ChangeControversy(
                                VacancyControversyPerDay,
                                "vacant council office",
                                currentDay);
                        }
                    }

                }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log($"Privy council daily update failed for {kingdom?.StringId ?? "null"}: {ex.Message}");
                }
            }
        }

        private void OnWeeklyTick()
        {
            List<Kingdom> liveRealms = Kingdom.All.Where(IsEligiblePermanentRealm).ToList();
            foreach (Kingdom kingdom in liveRealms)
            {
                try
                {
                    EnsureCouncilForKingdom(kingdom);
                    ApplyMarshalPressures(kingdom);
                    ApplyChancellorPressures(kingdom);
                    ApplySeneschalPressures(kingdom);
                    ApplySpymasterPressures(kingdom);
                    ApplyWeeklyAssignmentEffects(kingdom);
                    TryReviewAiAssignment(kingdom);
                }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log($"Privy council weekly pressure update failed for {kingdom?.StringId ?? "null"}: {ex.Message}");
                }
            }

            HashSet<string> liveRealmIds = new HashSet<string>(liveRealms.Select(kingdom => kingdom.StringId));
            _officeRecords.RemoveAll(record => record == null || !liveRealmIds.Contains(record.KingdomId));

            float currentDay = (float)CampaignTime.Now.ToDays;
            foreach (string key in _disgracedUntilByClan
                .Where(entry => entry.Value <= currentDay)
                .Select(entry => entry.Key)
                .ToList())
            {
                _disgracedUntilByClan.Remove(key);
                _disgraceIntentByClan.Remove(key);
                _captivityDismissalUntilByClan.Remove(key);
            }
        }

        private void OnRulingClanChanged(Kingdom kingdom, Clan newRulingClan)
        {
            if (!IsEligiblePermanentRealm(kingdom))
                return;

            EnsureCouncilForKingdom(kingdom);
            float currentDay = (float)CampaignTime.Now.ToDays;
            foreach (PrivyCouncilOfficeRecord record in GetOfficeRecords(kingdom))
            {
                if (!string.IsNullOrEmpty(record.HolderClanId) && record.HolderClanId == newRulingClan?.StringId)
                {
                    record.Vacate(currentDay);
                    InvalidateRuntimeCache();
                }
            }
        }

        private void ApplyMarshalPressures(Kingdom kingdom)
        {
            int activeWars = Kingdom.All.Count(other => other != null
                && other != kingdom
                && !other.IsEliminated
                && kingdom.IsAtWarWith(other));
            if (activeWars > 1)
                AddControversy(kingdom, PrivyCouncilOffice.Marshal, (activeWars - 1) * 2f, "war on multiple fronts");

            if (kingdom.RulingClan?.Leader?.IsPrisoner == true)
                AddControversy(kingdom, PrivyCouncilOffice.Marshal, 3f, "ruler held captive");

            WarScoreBehavior warScoreBehavior = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>();
            if (warScoreBehavior == null)
                return;

            float worstScore = warScoreBehavior.GetDisplayableWars(kingdom)
                .Select(war => war.GetSelfRelativeScore(kingdom.StringId))
                .DefaultIfEmpty(0f)
                .Min();
            if (worstScore <= -60f)
                AddControversy(kingdom, PrivyCouncilOffice.Marshal, 2f, "disastrous war progress");
            else if (worstScore <= -25f)
                AddControversy(kingdom, PrivyCouncilOffice.Marshal, 1f, "poor war progress");
        }

        private void ApplyChancellorPressures(Kingdom kingdom)
        {
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            List<FactionObject> ideologies = factionManager?.GetFactionsInKingdom(kingdom)
                .Where(faction => faction != null && faction.IsIdeology)
                .ToList() ?? new List<FactionObject>();

            float averageMood = ideologies.Count == 0 ? 0f : ideologies.Average(faction => faction.Mood);
            if (averageMood < -50f)
                AddControversy(kingdom, PrivyCouncilOffice.Chancellor, 2f, "court factions in open discontent");
            else if (averageMood < -20f)
                AddControversy(kingdom, PrivyCouncilOffice.Chancellor, 1f, "court factions dissatisfied");

            List<Clan> vassals = GetEligibleCouncilClans(kingdom, includePrisoners: true);
            Hero ruler = kingdom.RulingClan?.Leader;
            if (ruler != null && vassals.Count > 0)
            {
                float averageRelation = (float)vassals.Average(clan => clan.Leader.GetRelation(ruler));
                if (averageRelation < -50f)
                    AddControversy(kingdom, PrivyCouncilOffice.Chancellor, 2f, "crown isolated from its vassals");
                else if (averageRelation < -20f)
                    AddControversy(kingdom, PrivyCouncilOffice.Chancellor, 1f, "strained relations with the nobility");
            }

            ClaimFeudBehavior feudBehavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            int activeFeuds = feudBehavior?.GetActiveFeuds().Count(feud => feud != null && feud.ParentKingdomId == kingdom.StringId) ?? 0;
            if (activeFeuds > 0)
                AddControversy(kingdom, PrivyCouncilOffice.Chancellor, Math.Min(2f, activeFeuds * 0.5f), "unresolved noble feuds");
        }

        private void ApplySeneschalPressures(Kingdom kingdom)
        {
            List<Town> fiefs = kingdom.Fiefs.Where(fief => fief != null).ToList();
            if (fiefs.Count == 0)
                return;

            int starving = fiefs.Count(fief => fief.FoodStocks <= 0f);
            int disloyal = fiefs.Count(fief => fief.Loyalty < 30f);
            if (starving > 0)
                AddControversy(kingdom, PrivyCouncilOffice.Seneschal, Math.Min(3f, starving), "starving settlements");
            if (disloyal > 0)
                AddControversy(kingdom, PrivyCouncilOffice.Seneschal, Math.Min(3f, disloyal), "low settlement loyalty");

            PrivyCouncilOfficeRecord record = GetOfficeRecord(kingdom, PrivyCouncilOffice.Seneschal);
            float averageProsperity = fiefs.Average(fief => fief.Prosperity);
            if (record.LastRealmMetric > 0f && averageProsperity < record.LastRealmMetric * 0.98f)
                AddControversy(kingdom, PrivyCouncilOffice.Seneschal, 1f, "declining realm prosperity");
            record.SetLastRealmMetric(averageProsperity);
        }

        private void ApplySpymasterPressures(Kingdom kingdom)
        {
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null)
                return;

            List<FactionObject> factions = factionManager.GetFactionsInKingdom(kingdom)
                .Where(faction => faction != null)
                .ToList();
            int conspiracies = factions.Count(faction => !faction.IsIdeology && !faction.IsCivilWarActive());
            int activeRebellions = factions.Count(faction => !faction.IsIdeology && faction.IsCivilWarActive());
            int rebelliousIdeologies = factions.Count(faction => faction.IsIdeology && faction.Mood < -50f);

            if (conspiracies > 0)
                AddControversy(kingdom, PrivyCouncilOffice.Spymaster, Math.Min(2f, conspiracies * 0.5f), "active conspiracies");
            if (activeRebellions > 0)
                AddControversy(kingdom, PrivyCouncilOffice.Spymaster, Math.Min(4f, activeRebellions * 2f), "rebellion escaped detection");
            if (rebelliousIdeologies > 0)
                AddControversy(kingdom, PrivyCouncilOffice.Spymaster, Math.Min(2f, rebelliousIdeologies * 0.5f), "dangerous court agitation");
        }

        private void SeedInitialCouncil(Kingdom kingdom, IEnumerable<PrivyCouncilOfficeRecord> newlyCreated, float currentDay)
        {
            List<Clan> candidates = GetEligibleCouncilClans(kingdom, includePrisoners: false);
            HashSet<string> usedClanIds = new HashSet<string>();

            foreach (PrivyCouncilOfficeRecord record in newlyCreated.OrderBy(item => item.Office))
            {
                if (!IsOfficeUnlocked(kingdom, record.Office))
                {
                    record.Initialize(string.Empty, currentDay);
                    continue;
                }

                Clan best = candidates
                    .Where(candidate => !usedClanIds.Contains(candidate.StringId))
                    .OrderByDescending(candidate => CalculateCompetence(candidate.Leader, record.Office) * 0.65f
                        + CalculateSupportPercent(kingdom, candidate, record.Office) * 0.35f)
                    .ThenBy(candidate => candidate.StringId)
                    .FirstOrDefault();

                record.Initialize(best?.StringId, currentDay);
                if (best != null)
                    usedClanIds.Add(best.StringId);
            }
        }

        private void ValidateHolder(Kingdom kingdom, PrivyCouncilOfficeRecord record, float currentDay)
        {
            Clan holder = ResolveClan(record.HolderClanId);
            if (holder == null)
                return;

            bool temporarilyServingInPrivateFeud = IsTemporarilyServingInPrivateFeud(holder, kingdom);
            if (!NobleClanEligibilityHelper.IsLiveNobleClan(holder)
                || (holder.Kingdom != kingdom && !temporarilyServingInPrivateFeud)
                || holder == kingdom.RulingClan)
            {
                record.Vacate(currentDay);
                InvalidateRuntimeCache();
            }
        }

        internal void ReconcilePartitionCouncil(Kingdom kingdom)
        {
            if (!IsEligiblePermanentRealm(kingdom))
                throw new InvalidOperationException("Partition council requires a permanent realm.");
            EnsureCouncilForKingdom(kingdom);
            foreach (var record in GetOfficeRecords(kingdom))
            {
                string previousHolder = record.HolderClanId;
                ValidateHolder(kingdom, record, (float)CampaignTime.Now.ToDays);
                if (!string.IsNullOrEmpty(previousHolder) && string.IsNullOrEmpty(record.HolderClanId))
                    GetIncidentBehavior()?.ClearAssignmentAspectModifiers(kingdom, record.Office);
            }
            InvalidateRuntimeCache(recordsChanged: true);
        }

        private static bool IsTemporarilyServingInPrivateFeud(Clan holder, Kingdom parentKingdom)
        {
            if (holder?.Kingdom == null || parentKingdom == null || holder.Kingdom == parentKingdom)
                return false;

            ClaimFeudWarBehavior feudWars = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>();
            return feudWars?.IsTemporaryFeudKingdomForParent(holder.Kingdom, parentKingdom) == true;
        }

        internal static int DismissalSeverity(float controversy, bool captivity)
        {
            int normal = Math.Min(25, Math.Max(1, (int)Math.Round(Math.Max(0f, controversy) / 4f)));
            return captivity ? (normal + 1) / 2 : normal;
        }

        internal static bool ShouldRelieveCaptive(bool npcRuler, bool captive) => npcRuler && captive;

        private bool TryRelieveCaptiveOfficeHolder(Kingdom kingdom, PrivyCouncilOfficeRecord record, float currentDay)
        {
            var holder = ResolveClan(record.HolderClanId);
            if (!ShouldRelieveCaptive(kingdom.RulingClan != null && kingdom.RulingClan != Clan.PlayerClan,
                holder?.Leader?.IsPrisoner == true)) return false;
            float controversy = record.Controversy;
            CancelCaptiveCouncillorAssignment(kingdom, record, currentDay);
            record.VacateForCaptivity(currentDay);
            InvalidateRuntimeCache();
            int grievance = ApplyDismissalAftermath(kingdom, holder, controversy, record.Office, captivity: true);
            CourtAgendaBehavior.Current?.MarkCouncilCaptivityVacancy(kingdom, record.Office);
            var message = new TextObject("{=BC_Council_CaptiveDismissal}With {COUNCILLOR_NAME} held captive and unable to serve, {RULER_NAME} has relieved them as {OFFICE}. The court must now choose a successor.");
            message.SetTextVariable("COUNCILLOR_NAME", holder.Leader.Name);
            message.SetTextVariable("RULER_NAME", kingdom.RulingClan.Leader?.Name ?? kingdom.RulingClan.Name);
            message.SetTextVariable("OFFICE", GetLocalizedOfficeName(record.Office, kingdom));
            BellumCivileNotifications.Show(message, BellumNotificationColors.Warning,
                primaryKingdom: kingdom, primaryClan: holder, isPersonal: kingdom == Clan.PlayerClan?.Kingdom);
            if (holder == Clan.PlayerClan) QueuePlayerDisgracedDismissal(kingdom, record.Office, captivity: true);
            BellumCivileLogger.Log($"Councillor relieved during captivity; realm={kingdom.StringId}; office={record.Office}; house={holder.StringId}; controversy={controversy}; grievance={grievance}; urgent_vacancy=true.");
            return true;
        }

        private bool TryDismissDisgracedOfficeHolder(
            Kingdom kingdom,
            PrivyCouncilOfficeRecord record,
            float currentDay)
        {
            if (record == null
                || record.Office > PrivyCouncilOffice.Spymaster
                || record.Controversy < 100f)
            {
                return false;
            }

            Clan holder = ResolveClan(record.HolderClanId);
            if (holder == null)
                return false;

            Hero councilor = holder.Leader;
            float controversy = record.Controversy;
            record.Vacate(currentDay);
            InvalidateRuntimeCache();
            ApplyDismissalAftermath(kingdom, holder, controversy, record.Office);

            TextObject message = new TextObject("{=BC_Council_DisgracedDismissal}{COUNCILOR_NAME} has been dismissed as {OFFICE} after the office fell into complete disrepute.");
            message.SetTextVariable("COUNCILOR_NAME", councilor?.Name ?? holder.Name);
            message.SetTextVariable("OFFICE", GetLocalizedOfficeName(record.Office, kingdom));
            BellumCivileNotifications.Show(
                message,
                BellumNotificationColors.Warning,
                primaryKingdom: kingdom,
                primaryClan: holder,
                isPersonal: kingdom == Clan.PlayerClan?.Kingdom);

            if (holder == Clan.PlayerClan && kingdom?.RulingClan != Clan.PlayerClan)
                QueuePlayerDisgracedDismissal(kingdom, record.Office);
            return true;
        }

        private void QueuePlayerDisgracedDismissal(Kingdom kingdom, PrivyCouncilOffice office, bool captivity = false)
        {
            Hero ruler = kingdom?.RulingClan?.Leader;
            if (ruler == null)
                return;

            TextObject title = new TextObject(
                captivity ? "{=BC_Council_CaptiveDismissalTitle}Relieved from the {COUNCIL_NAME}"
                    : "{=BC_Council_DisgracedDismissalTitle}Dismissed from the {COUNCIL_NAME}");
            title = CourtInstitutionDisplayHelper.ApplyPrivyCouncilName(title, kingdom);

            TextObject body = new TextObject(
                captivity ? "{=BC_Council_CaptiveDismissalBody}A sealed message arrives from {RULER_NAME}:\n\n\"Word of your captivity has reached the court. While you remain in enemy hands, you cannot discharge the duties of {OFFICE}. I therefore relieve you of that charge in the {COUNCIL_NAME}, so another may serve in your absence. This is no judgment upon your loyalty. Yet the needs of the realm cannot await your release.\""
                    : "{=BC_Council_DisgracedDismissalBody}A sealed message arrives from {RULER_NAME}:\n\n\"The scandals surrounding your tenure have brought the office of {OFFICE} into disrepute. I therefore relieve you of that position in the {COUNCIL_NAME}, effective immediately.\"");
            body.SetTextVariable("RULER_NAME", ruler.Name);
            body.SetTextVariable("OFFICE", GetLocalizedOfficeName(office, kingdom));
            body = CourtInstitutionDisplayHelper.ApplyPrivyCouncilName(body, kingdom);

            _pendingPlayerDismissals.Enqueue(new PendingPlayerCouncilDismissal(
                title.ToString(),
                body.ToString()));
        }




        private void EnsureValidAssignment(PrivyCouncilOfficeRecord record, float currentDay)
        {
            if (record != null
                && string.Equals(record.AssignmentId, "marshal_enforce_impressment", StringComparison.OrdinalIgnoreCase))
            {
                record.RepairAssignment(
                    "marshal_oversee_logistics",
                    record.LastAssignmentChangedDay,
                    currentDay);
                InvalidateRuntimeCache();
            }

            PrivyCouncilAssignmentDefinition current = PrivyCouncilAssignmentRegistry.GetAssignment(record.AssignmentId);
            if (current != null && current.Office == record.Office)
                return;

            PrivyCouncilAssignmentDefinition fallback = PrivyCouncilAssignmentRegistry.GetDefaultAssignment(record.Office);
            record.RepairAssignment(
                fallback?.Id ?? string.Empty,
                currentDay - AssignmentCooldownDays,
                currentDay);
            InvalidateRuntimeCache();
        }

        private void CancelCaptiveCouncillorAssignment(
            Kingdom kingdom,
            PrivyCouncilOfficeRecord record,
            float currentDay)
        {
            if (kingdom == null || record == null)
                return;

            PrivyCouncilAssignmentDefinition defaultAssignment =
                PrivyCouncilAssignmentRegistry.GetDefaultAssignment(record.Office);
            string defaultAssignmentId = defaultAssignment?.Id ?? string.Empty;
            if (string.Equals(record.AssignmentId, defaultAssignmentId, StringComparison.OrdinalIgnoreCase))
                return;

            record.RepairAssignment(
                defaultAssignmentId,
                currentDay - AssignmentCooldownDays,
                currentDay);
            GetIncidentBehavior()?.ClearAssignmentAspectModifiers(kingdom, record.Office);
            InvalidateRuntimeCache();
            BellumCivileLogger.Log(
                $"Privy council assignment cancelled by captivity; kingdom={kingdom.StringId}; office={record.Office}; holder={record.HolderClanId}.");
        }

        private void ApplyDailyAssignmentEffect(Kingdom kingdom, PrivyCouncilOfficeRecord record)
        {
            PrivyCouncilAssignmentDefinition assignment = GetOfficeAssignment(kingdom, record.Office);
            PrivyCouncilAssignmentContext context = BuildAssignmentContext(kingdom, record.Office);
            if (assignment != null && context != null)
                assignment.ApplyDailyEffect(context);
        }

        private PrivyCouncilAssignmentContext BuildAssignmentContext(Kingdom kingdom, PrivyCouncilOffice office)
        {
            Clan holder = GetOfficeHolder(kingdom, office);
            if (kingdom == null || holder?.Leader == null || holder.Leader.IsPrisoner)
                return null;

            return new PrivyCouncilAssignmentContext(
                this,
                kingdom,
                office,
                holder,
                CalculateCompetence(holder.Leader, office));
        }

        private void MigrateLegacyControversy(Kingdom kingdom)
        {
            PrivyCouncilOfficeRecord marshal = _officeRecords.FirstOrDefault(record => record != null
                && record.KingdomId == kingdom.StringId
                && record.Office == PrivyCouncilOffice.Marshal);
            if (marshal == null || marshal.LegacyControversyMigrated)
                return;

            ControversyBehavior legacyBehavior = Campaign.Current?.GetCampaignBehavior<ControversyBehavior>();
            int legacyValue = legacyBehavior?.ConsumeLegacyTrackedControversy(kingdom) ?? 0;
            if (legacyValue > 0)
                marshal.ChangeControversy(legacyValue, "legacy ruler controversy", (float)CampaignTime.Now.ToDays);
            marshal.MarkLegacyControversyMigrated();
        }

        private float CalculateSupportPercent(Kingdom kingdom, Clan candidate, PrivyCouncilOffice office)
        {
            if (kingdom == null || candidate?.Leader == null)
                return 0f;

            List<Clan> electorate = kingdom.Clans
                .Where(NobleClanEligibilityHelper.IsLiveNobleClan)
                .ToList();
            if (electorate.Count == 0)
                return 0f;

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject candidateFaction = factionManager?.GetIdeologicalFaction(candidate);
            float competence = CalculateCompetence(candidate.Leader, office);
            float supportingWeight = 0f;
            float totalWeight = 0f;

            foreach (Clan voter in electorate)
            {
                float weight = GetPoliticalWeight(voter);
                totalWeight += weight;

                float score = CalculateAppointmentSupport(kingdom, voter, candidate, office);

                if (score >= 25f)
                    supportingWeight += weight;
            }

            return totalWeight <= 0f ? 0f : supportingWeight / totalWeight * 100f;
        }

        private static float GetPoliticalWeight(Clan clan)
        {
            FeudalTitleType? rank = FeudalPoliticalWeightHelper.GetHighestHeldTitleRank(clan);
            return rank.HasValue ? (int)rank.Value + 2f : 1f;
        }

        private static float GetCouncilSeatAmbition(Clan clan)
        {
            FeudalTitleType? rank = FeudalPoliticalWeightHelper.GetHighestHeldTitleRank(clan);
            if (!rank.HasValue)
                return 0f;

            switch (rank.Value)
            {
                case FeudalTitleType.Barony: return 5f;
                case FeudalTitleType.County: return 10f;
                case FeudalTitleType.Duchy: return 15f;
                default: return 20f;
            }
        }

        private float GetDisgracePenalty(Kingdom kingdom, Clan clan)
        {
            if (kingdom != null && clan != null && _captivityDismissalUntilByClan.TryGetValue(BuildDisgraceKey(kingdom, clan), out float until)
                && until > CampaignTime.Now.ToDays) return 0f;
            float remaining = GetDisgraceDaysRemaining(kingdom, clan);
            return Clamp(remaining / DisgraceDurationDays * 100f, 0f, 100f);
        }

        private static float GetSalaryMultiplier(float competence)
        {
            if (competence < 20f) return 0.5f;
            if (competence < 40f) return 0.75f;
            if (competence < 60f) return 1f;
            if (competence < 80f) return 1.25f;
            return 1.5f;
        }

        private static string BuildDisgraceKey(Kingdom kingdom, Clan clan)
        {
            return (kingdom?.StringId ?? string.Empty) + ":" + (clan?.StringId ?? string.Empty);
        }

        public static TextObject GetOfficeName(PrivyCouncilOffice office, Kingdom kingdom = null)
        {
            return CourtInstitutionDisplayHelper.GetCouncilOfficeName(office, kingdom);
        }

        public static TextObject GetLocalizedOfficeName(PrivyCouncilOffice office, Kingdom kingdom = null)
        {
            return GetOfficeName(office, kingdom);
        }

        private static List<Clan> GetEligibleCouncilClans(Kingdom kingdom, bool includePrisoners)
        {
            return kingdom?.Clans
                .Where(clan => clan != kingdom.RulingClan
                    && NobleClanEligibilityHelper.IsLiveNobleClan(clan)
                    && clan.Kingdom == kingdom
                    && (includePrisoners || clan.Leader?.IsPrisoner != true))
                .OrderBy(clan => clan.StringId)
                .ToList() ?? new List<Clan>();
        }

        private static bool IsEligiblePermanentRealm(Kingdom kingdom)
        {
            return kingdom != null
                && !kingdom.IsEliminated
                && !string.IsNullOrEmpty(kingdom.StringId)
                && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom)
                && !BellumKingdomVisibilityHelper.IsSupersededEmptyKingdomShell(kingdom);
        }

        private Clan ResolveClan(string clanId)
        {
            if (string.IsNullOrEmpty(clanId))
                return null;

            EnsureRecordIndexes();
            if (_clanById.TryGetValue(clanId, out Clan clan))
                return clan;

            clan = Clan.All.FirstOrDefault(candidate => candidate != null && candidate.StringId == clanId);
            if (clan != null)
                _clanById[clanId] = clan;
            return clan;
        }

        private static float GetShockMultiplier(float competence)
        {
            if (competence < 20f) return 1.25f;
            if (competence < 40f) return 1.10f;
            if (competence < 60f) return 1f;
            if (competence < 80f) return 0.90f;
            return 0.75f;
        }

        private static float GetDailyRecovery(float competence)
        {
            if (competence < 20f) return 0.10f;
            if (competence < 40f) return 0.15f;
            if (competence < 60f) return 0.20f;
            if (competence < 80f) return 0.25f;
            return 0.30f;
        }

        private static bool IsRoadSecurityFailure(string reason)
        {
            return string.Equals(reason, "own caravan destroyed", StringComparison.Ordinal)
                || string.Equals(reason, "own village raided", StringComparison.Ordinal);
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum) return minimum;
            if (value > maximum) return maximum;
            return value;
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            if (value < minimum) return minimum;
            if (value > maximum) return maximum;
            return value;
        }
    }
}
