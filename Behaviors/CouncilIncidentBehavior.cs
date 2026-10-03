using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Incidents;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    internal sealed class ActiveCouncilAssignmentModifier
    {
        public ActiveCouncilAssignmentModifier(
            string sourceId,
            string aspectId,
            TextObject incidentTitle,
            float multiplier,
            float remainingDays)
        {
            SourceId = sourceId ?? string.Empty;
            AspectId = aspectId ?? string.Empty;
            IncidentTitle = incidentTitle ?? TextObject.GetEmpty();
            Multiplier = multiplier;
            RemainingDays = Math.Max(0f, remainingDays);
        }

        public string SourceId { get; }
        public string AspectId { get; }
        public TextObject IncidentTitle { get; }
        public float Multiplier { get; }
        public float RemainingDays { get; }
    }

    public sealed class CouncilIncidentBehavior : CampaignBehaviorBase
    {
        internal const string RuntimeIncidentIdPrefix = "bc_council_incident_runtime_";

        private sealed class AspectMultiplierAggregate
        {
            public float Multiplier = 1f;
            public float NextExpiryDay = float.PositiveInfinity;
        }

        private struct AspectLookupKey : IEquatable<AspectLookupKey>
        {
            private readonly string _kingdomId;
            private readonly PrivyCouncilOffice _office;
            private readonly string _assignmentId;
            private readonly string _aspectId;

            public AspectLookupKey(
                string kingdomId,
                PrivyCouncilOffice office,
                string assignmentId,
                string aspectId)
            {
                _kingdomId = kingdomId ?? string.Empty;
                _office = office;
                _assignmentId = assignmentId ?? string.Empty;
                _aspectId = aspectId ?? string.Empty;
            }

            public bool Equals(AspectLookupKey other)
            {
                return _office == other._office
                    && string.Equals(_kingdomId, other._kingdomId, StringComparison.Ordinal)
                    && string.Equals(_assignmentId, other._assignmentId, StringComparison.Ordinal)
                    && string.Equals(_aspectId, other._aspectId, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is AspectLookupKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = StringComparer.Ordinal.GetHashCode(_kingdomId);
                    hash = hash * 397 ^ (int)_office;
                    hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(_assignmentId);
                    hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(_aspectId);
                    return hash;
                }
            }
        }

        private Dictionary<string, float> _incidentCooldownUntilDayByKey = new Dictionary<string, float>();
        private Dictionary<string, float> _aspectMultiplierByKey = new Dictionary<string, float>();
        private Dictionary<string, float> _aspectExpiryDayByKey = new Dictionary<string, float>();
        private readonly Dictionary<AspectLookupKey, AspectMultiplierAggregate> _aspectMultiplierByLookup =
            new Dictionary<AspectLookupKey, AspectMultiplierAggregate>();
        private bool _aspectIndexDirty = true;
        private float _nextAspectIndexExpiryDay = float.PositiveInfinity;
        private float _nextIncidentCheckDay;
        private string _incidentScheduleKingdomId = string.Empty;
        private int _incidentSerial;

        private string _pendingEventId = string.Empty;
        private string _pendingKingdomId = string.Empty;
        private string _pendingAssignmentId = string.Empty;
        private string _pendingHolderClanId = string.Empty;
        private int _pendingOffice = -1;
        private int _pendingQuality = -1;
        private float _pendingCompetence;
        private float _pendingCreatedDay = -1f;

        private bool _pendingPresented;

        public override void RegisterEvents()
        {
            CouncilAssignmentRuntimePatches.BindIncidentBehavior(this);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_CouncilIncident_CooldownDays", ref _incidentCooldownUntilDayByKey);
            dataStore.SyncData("BellumCivile_CouncilIncident_AspectMultipliers", ref _aspectMultiplierByKey);
            dataStore.SyncData("BellumCivile_CouncilIncident_AspectExpiryDays", ref _aspectExpiryDayByKey);
            dataStore.SyncData("BellumCivile_CouncilIncident_NextCheckDay", ref _nextIncidentCheckDay);
            dataStore.SyncData("BellumCivile_CouncilIncident_ScheduleKingdom", ref _incidentScheduleKingdomId);
            dataStore.SyncData("BellumCivile_CouncilIncident_Serial", ref _incidentSerial);
            dataStore.SyncData("BellumCivile_CouncilIncident_PendingEvent", ref _pendingEventId);
            dataStore.SyncData("BellumCivile_CouncilIncident_PendingKingdom", ref _pendingKingdomId);
            dataStore.SyncData("BellumCivile_CouncilIncident_PendingAssignment", ref _pendingAssignmentId);
            dataStore.SyncData("BellumCivile_CouncilIncident_PendingHolder", ref _pendingHolderClanId);
            dataStore.SyncData("BellumCivile_CouncilIncident_PendingOffice", ref _pendingOffice);
            dataStore.SyncData("BellumCivile_CouncilIncident_PendingQuality", ref _pendingQuality);
            dataStore.SyncData("BellumCivile_CouncilIncident_PendingCompetence", ref _pendingCompetence);
            dataStore.SyncData("BellumCivile_CouncilIncident_PendingCreatedDay", ref _pendingCreatedDay);

            if (_incidentCooldownUntilDayByKey == null)
                _incidentCooldownUntilDayByKey = new Dictionary<string, float>();
            if (_aspectMultiplierByKey == null)
                _aspectMultiplierByKey = new Dictionary<string, float>();
            if (_aspectExpiryDayByKey == null)
                _aspectExpiryDayByKey = new Dictionary<string, float>();
            if (_pendingEventId == null)
                _pendingEventId = string.Empty;
            if (_pendingKingdomId == null)
                _pendingKingdomId = string.Empty;
            if (_pendingAssignmentId == null)
                _pendingAssignmentId = string.Empty;
            if (_pendingHolderClanId == null)
                _pendingHolderClanId = string.Empty;
            if (_incidentScheduleKingdomId == null)
                _incidentScheduleKingdomId = string.Empty;

            if (dataStore.IsLoading)
            {
                _pendingPresented = false;
                InvalidateAspectIndex();
            }
        }

        public float GetAssignmentAspectMultiplier(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            string assignmentId,
            string aspectId)
        {
            if (kingdom == null
                || string.IsNullOrEmpty(assignmentId)
                || string.IsNullOrEmpty(aspectId))
            {
                return 1f;
            }

            float currentDay = (float)CampaignTime.Now.ToDays;
            EnsureAspectIndex(currentDay);
            AspectLookupKey key = new AspectLookupKey(kingdom.StringId, office, assignmentId, aspectId);
            return _aspectMultiplierByLookup.TryGetValue(key, out AspectMultiplierAggregate aggregate)
                ? aggregate.Multiplier
                : 1f;
        }

        internal IReadOnlyList<ActiveCouncilAssignmentModifier> GetActiveAssignmentModifiers(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            string assignmentId)
        {
            List<ActiveCouncilAssignmentModifier> modifiers = new List<ActiveCouncilAssignmentModifier>();
            if (kingdom == null || string.IsNullOrEmpty(assignmentId))
                return modifiers;

            float currentDay = (float)CampaignTime.Now.ToDays;
            string prefix = BuildAssignmentAspectPrefix(kingdom.StringId, office, assignmentId) + "|";
            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            Clan holder = council?.GetOfficeHolder(kingdom, office);
            float competence = office <= PrivyCouncilOffice.Spymaster
                ? council?.GetEffectiveCoreOfficeCompetence(kingdom, office) ?? 0f
                : council?.GetOfficeCompetence(kingdom, office) ?? 0f;

            foreach (KeyValuePair<string, float> entry in _aspectMultiplierByKey)
            {
                if (!entry.Key.StartsWith(prefix, StringComparison.Ordinal)
                    || !_aspectExpiryDayByKey.TryGetValue(entry.Key, out float expiryDay)
                    || expiryDay <= currentDay)
                {
                    continue;
                }

                string suffix = entry.Key.Substring(prefix.Length);
                int separatorIndex = suffix.IndexOf('|');
                if (separatorIndex <= 0 || separatorIndex >= suffix.Length - 1)
                    continue;

                string aspectId = suffix.Substring(0, separatorIndex);
                string sourceId = suffix.Substring(separatorIndex + 1);
                CouncilIncidentDefinition definition = CouncilIncidentRegistry.GetDefinition(sourceId);
                CouncilIncidentContext context = new CouncilIncidentContext(
                    this,
                    council,
                    kingdom,
                    office,
                    assignmentId,
                    holder,
                    competence,
                    CouncilIncidentQuality.Neutral);
                TextObject title = definition?.GetTitle(context)
                    ?? new TextObject("{=BC_Council_AssignmentUnknownIncident}Council incident");
                modifiers.Add(new ActiveCouncilAssignmentModifier(
                    sourceId,
                    aspectId,
                    title,
                    entry.Value,
                    expiryDay - currentDay));
            }

            return modifiers
                .OrderBy(modifier => modifier.RemainingDays)
                .ThenBy(modifier => modifier.SourceId, StringComparer.Ordinal)
                .ThenBy(modifier => modifier.AspectId, StringComparer.Ordinal)
                .ToList();
        }

        public void SetAssignmentAspectModifier(
            Kingdom kingdom,
            PrivyCouncilOffice office,
            string assignmentId,
            string aspectId,
            string sourceId,
            float multiplier,
            float durationDays)
        {
            if (kingdom == null
                || string.IsNullOrEmpty(assignmentId)
                || string.IsNullOrEmpty(aspectId)
                || string.IsNullOrEmpty(sourceId)
                || durationDays <= 0f)
            {
                return;
            }

            string key = BuildAspectKey(
                kingdom.StringId,
                office,
                assignmentId,
                aspectId,
                sourceId);
            _aspectMultiplierByKey[key] = Clamp(multiplier, 0.25f, 3f);
            _aspectExpiryDayByKey[key] = (float)CampaignTime.Now.ToDays + durationDays;
            InvalidateAspectIndex();
        }

        public void ClearAssignmentAspectModifiers(Kingdom kingdom, PrivyCouncilOffice office)
        {
            if (kingdom == null)
                return;

            string prefix = (kingdom.StringId ?? string.Empty) + "|" + office + "|";
            List<string> keys = _aspectMultiplierByKey.Keys
                .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();
            foreach (string key in keys)
            {
                _aspectMultiplierByKey.Remove(key);
                _aspectExpiryDayByKey.Remove(key);
            }

            if (keys.Count > 0)
                InvalidateAspectIndex();
        }

        private void EnsureAspectIndex(float currentDay)
        {
            if (!_aspectIndexDirty && currentDay < _nextAspectIndexExpiryDay)
                return;

            bool modifierExpiredSinceLastBuild = !_aspectIndexDirty;
            _aspectMultiplierByLookup.Clear();
            _nextAspectIndexExpiryDay = float.PositiveInfinity;
            foreach (KeyValuePair<string, float> entry in _aspectMultiplierByKey)
            {
                if (!_aspectExpiryDayByKey.TryGetValue(entry.Key, out float expiryDay)
                    || expiryDay <= currentDay)
                {
                    continue;
                }

                if (!TryParseAspectLookupKey(entry.Key, out AspectLookupKey lookupKey))
                    continue;

                if (!_aspectMultiplierByLookup.TryGetValue(lookupKey, out AspectMultiplierAggregate aggregate))
                {
                    aggregate = new AspectMultiplierAggregate();
                    _aspectMultiplierByLookup.Add(lookupKey, aggregate);
                }

                aggregate.Multiplier *= entry.Value;
                aggregate.NextExpiryDay = Math.Min(aggregate.NextExpiryDay, expiryDay);
                _nextAspectIndexExpiryDay = Math.Min(_nextAspectIndexExpiryDay, expiryDay);
            }

            foreach (AspectMultiplierAggregate aggregate in _aspectMultiplierByLookup.Values)
                aggregate.Multiplier = Clamp(aggregate.Multiplier, 0.25f, 3f);

            _aspectIndexDirty = false;
            if (modifierExpiredSinceLastBuild)
                CouncilAssignmentRuntimePatches.GetCouncilBehavior()?.InvalidateRuntimeCache();
        }

        private void InvalidateAspectIndex()
        {
            _aspectIndexDirty = true;
            _nextAspectIndexExpiryDay = float.NegativeInfinity;
            CouncilAssignmentRuntimePatches.GetCouncilBehavior()?.InvalidateRuntimeCache();
        }

        private static bool TryParseAspectLookupKey(string serializedKey, out AspectLookupKey lookupKey)
        {
            lookupKey = default(AspectLookupKey);
            if (string.IsNullOrEmpty(serializedKey))
                return false;

            int first = serializedKey.IndexOf('|');
            int second = first < 0 ? -1 : serializedKey.IndexOf('|', first + 1);
            int third = second < 0 ? -1 : serializedKey.IndexOf('|', second + 1);
            int fourth = third < 0 ? -1 : serializedKey.IndexOf('|', third + 1);
            if (first <= 0 || second <= first || third <= second || fourth <= third)
                return false;

            if (!Enum.TryParse(
                    serializedKey.Substring(first + 1, second - first - 1),
                    out PrivyCouncilOffice office))
            {
                return false;
            }

            lookupKey = new AspectLookupKey(
                serializedKey.Substring(0, first),
                office,
                serializedKey.Substring(second + 1, third - second - 1),
                serializedKey.Substring(third + 1, fourth - third - 1));
            return true;
        }

        public bool TryQueuePlayerCouncilIncident(string eventId, out string result)
        {
            result = string.Empty;
            if (HasPendingIncident)
            {
                result = "A council incident is already pending.";
                return false;
            }

            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (kingdom?.RulingClan?.Leader != Hero.MainHero)
            {
                result = "The player must be the ruler of a kingdom.";
                return false;
            }

            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            if (council == null)
            {
                result = "The Privy Council behavior is unavailable.";
                return false;
            }

            CouncilIncidentDefinition definition = string.IsNullOrWhiteSpace(eventId)
                ? FindFirstEligibleDefinition(kingdom, council)
                : CouncilIncidentRegistry.GetDefinition(eventId);
            if (definition == null)
            {
                result = "No eligible council incident was found.";
                return false;
            }

            PrivyCouncilAssignmentDefinition assignment = council.GetOfficeAssignment(kingdom, definition.Office);
            if (assignment == null
                || !string.Equals(assignment.Id, definition.AssignmentId, StringComparison.OrdinalIgnoreCase)
                || !council.IsAssignmentActive(kingdom, definition.Office, definition.AssignmentId))
            {
                result = "The required assignment is not active: " + definition.AssignmentId;
                return false;
            }

            QueueIncident(kingdom, council, definition);
            result = "Council incident queued: " + definition.Id;
            return true;
        }

        private bool HasPendingIncident => !string.IsNullOrEmpty(_pendingEventId);

        private void OnDailyTick()
        {
            PruneExpiredAspectModifiers();
            PruneExpiredIncidentCooldowns();

            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (kingdom?.RulingClan?.Leader != Hero.MainHero)
            {
                if (HasPendingIncident)
                    CancelPendingIncident("the player no longer holds the throne");

                _incidentScheduleKingdomId = string.Empty;
                _nextIncidentCheckDay = 0f;
                return;
            }

            float currentDay = (float)CampaignTime.Now.ToDays;
            if (!string.Equals(_incidentScheduleKingdomId, kingdom.StringId, StringComparison.Ordinal)
                || _nextIncidentCheckDay <= 0f)
            {
                _incidentScheduleKingdomId = kingdom.StringId ?? string.Empty;
                ScheduleNextIncidentCheck(currentDay);
                return;
            }

            if (HasPendingIncident || currentDay < _nextIncidentCheckDay)
                return;

            // Advance first so an interrupted review cannot become an expired
            // schedule that retries on every following daily tick.
            ScheduleNextIncidentCheck(currentDay);

            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            if (council == null)
                return;

            council.EnsureCouncilForKingdom(kingdom);
            List<PrivyCouncilOfficeRecord> records = council.GetOfficeRecords(kingdom)
                .Where(record => record != null)
                .OrderBy(record => record.Office)
                .ToList();

            List<IReadOnlyList<CouncilIncidentDefinition>> successfulAssignmentPools =
                new List<IReadOnlyList<CouncilIncidentDefinition>>();
            foreach (PrivyCouncilOfficeRecord record in records)
            {
                PrivyCouncilAssignmentDefinition assignment = council.GetOfficeAssignment(kingdom, record.Office);
                if (assignment == null
                    || assignment.Id.EndsWith("_none", StringComparison.OrdinalIgnoreCase)
                    || !council.IsAssignmentActive(kingdom, record.Office, assignment.Id))
                {
                    continue;
                }

                IReadOnlyList<CouncilIncidentDefinition> eligible =
                    CouncilIncidentRegistry.GetEligibleDefinitions(record.Office, assignment.Id)
                        .Where(definition => !IsIncidentOnCooldown(kingdom, definition, currentDay))
                        .ToList();
                if (eligible.Count == 0
                    || MBRandom.RandomFloat > C.CouncilIncidentAssignmentChance)
                {
                    continue;
                }

                successfulAssignmentPools.Add(eligible);
            }

            if (successfulAssignmentPools.Count == 0)
                return;

            IReadOnlyList<CouncilIncidentDefinition> selectedPool = successfulAssignmentPools[
                MBRandom.RandomInt(successfulAssignmentPools.Count)];
            CouncilIncidentDefinition selected = SelectWeightedDefinition(selectedPool);
            if (selected != null)
                QueueIncident(kingdom, council, selected);
        }

        private void OnTick(float deltaTime)
        {
            if (!HasPendingIncident || _pendingPresented)
                return;

            CouncilIncidentContext context = ResolvePendingContext();
            CouncilIncidentDefinition definition = CouncilIncidentRegistry.GetDefinition(_pendingEventId);
            if (context == null || definition == null)
            {
                CancelPendingIncident("pending council incident context became invalid");
                return;
            }

            if (!CanOpenIncident(out MapState mapState))
                return;

            Incident incident = BuildRuntimeIncident(definition, context);
            if (incident == null)
            {
                CancelPendingIncident("pending council incident could not be constructed");
                return;
            }

            mapState.NextIncident = incident;
            _pendingPresented = true;
        }

        private void QueueIncident(
            Kingdom kingdom,
            PrivyCouncilBehavior council,
            CouncilIncidentDefinition definition)
        {
            Clan holder = council.GetOfficeHolder(kingdom, definition.Office);
            float competence = definition.Office <= PrivyCouncilOffice.Spymaster
                ? council.GetEffectiveCoreOfficeCompetence(kingdom, definition.Office)
                : PrivyCouncilBehavior.CalculateCompetence(holder?.Leader, definition.Office);

            _pendingEventId = definition.Id;
            _pendingKingdomId = kingdom.StringId ?? string.Empty;
            _pendingAssignmentId = definition.AssignmentId;
            _pendingHolderClanId = holder?.StringId ?? string.Empty;
            _pendingOffice = (int)definition.Office;
            _pendingQuality = (int)RollQuality(competence);
            _pendingCompetence = competence;
            _pendingCreatedDay = (float)CampaignTime.Now.ToDays;
            _pendingPresented = false;
            _incidentSerial++;

            BellumCivileLogger.Log($"Council incident queued; event={definition.Id}; kingdom={kingdom.StringId}; office={definition.Office}; assignment={definition.AssignmentId}; competence={competence:0.0}; quality={(CouncilIncidentQuality)_pendingQuality}.");
        }

        private Incident BuildRuntimeIncident(
            CouncilIncidentDefinition definition,
            CouncilIncidentContext context)
        {
            List<CouncilIncidentOptionDefinition> options = definition.GetOptions(context);
            if (options.Count == 0)
                return null;

            Incident incident = new Incident(RuntimeIncidentIdPrefix + _incidentSerial);
            incident.Initialize(
                definition.GetTitle(context).ToString(),
                definition.GetDescription(context).ToString(),
                (IncidentsCampaignBehaviour.IncidentTrigger)0,
                definition.IncidentType,
                CampaignTime.Zero,
                description => true);

            foreach (CouncilIncidentOptionDefinition option in options)
            {
                CouncilIncidentOptionDefinition capturedOption = option;
                incident.AddOption(
                    capturedOption.Text.ToString(),
                    new List<IncidentEffect>
                    {
                        IncidentEffect.Custom(
                            () => true,
                            () => ApplyOptionSafely(capturedOption, context),
                            effect => capturedOption.GetHints(context))
                    },
                    condition: null,
                    consequence: CompletePendingIncident);
            }

            return incident;
        }

        private List<TextObject> ApplyOptionSafely(
            CouncilIncidentOptionDefinition option,
            CouncilIncidentContext context)
        {
            try
            {
                return option.Apply(context);
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Council incident consequence failed; event={_pendingEventId}; error={ex.GetType().Name}:{ex.Message}");
                return new List<TextObject>
                {
                    new TextObject("{=BC_CouncilIncident_ResultFailed}The council's proposal could not be carried out.")
                };
            }
        }

        private CouncilIncidentContext ResolvePendingContext()
        {
            if (!HasPendingIncident
                || _pendingOffice < 0
                || !Enum.IsDefined(typeof(PrivyCouncilOffice), _pendingOffice)
                || !Enum.IsDefined(typeof(CouncilIncidentQuality), _pendingQuality))
            {
                return null;
            }

            Kingdom kingdom = Kingdom.All.FirstOrDefault(candidate =>
                candidate != null && candidate.StringId == _pendingKingdomId);
            if (kingdom?.RulingClan?.Leader != Hero.MainHero)
                return null;

            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            if (council == null)
                return null;

            PrivyCouncilOffice office = (PrivyCouncilOffice)_pendingOffice;
            PrivyCouncilAssignmentDefinition assignment = council.GetOfficeAssignment(kingdom, office);
            Clan holder = council.GetOfficeHolder(kingdom, office);
            if (assignment == null
                || holder?.Leader == null
                || holder.Leader.IsPrisoner
                || holder.StringId != _pendingHolderClanId
                || !string.Equals(assignment.Id, _pendingAssignmentId, StringComparison.OrdinalIgnoreCase)
                || !council.IsAssignmentActive(kingdom, office, _pendingAssignmentId))
            {
                return null;
            }

            return new CouncilIncidentContext(
                this,
                council,
                kingdom,
                office,
                _pendingAssignmentId,
                holder,
                _pendingCompetence,
                (CouncilIncidentQuality)_pendingQuality);
        }

        private static bool CanOpenIncident(out MapState mapState)
        {
            mapState = Game.Current?.GameStateManager?.ActiveState as MapState;
            if (mapState == null
                || mapState.AtMenu
                || mapState.MapConversationActive
                || mapState.NextIncident != null
                || Campaign.Current?.CurrentMenuContext != null
                || Campaign.Current?.ConversationManager?.IsConversationFlowActive == true
                || Hero.MainHero?.IsPrisoner == true
                || PlayerEncounter.Current != null)
            {
                return false;
            }

            return true;
        }

        private void CompletePendingIncident()
        {
            BellumCivileLogger.Log($"Council incident resolved; event={_pendingEventId}; kingdom={_pendingKingdomId}.");
            if (!string.IsNullOrEmpty(_pendingKingdomId) && !string.IsNullOrEmpty(_pendingEventId))
            {
                _incidentCooldownUntilDayByKey[BuildIncidentCooldownKey(
                    _pendingKingdomId,
                    _pendingEventId)] = (float)CampaignTime.Now.ToDays
                        + C.CouncilIncidentSpecificCooldownDays;
            }

            ClearPendingFields();
        }

        private void CancelPendingIncident(string reason)
        {
            BellumCivileLogger.Log($"Council incident cancelled; event={_pendingEventId}; kingdom={_pendingKingdomId}; reason={reason}.");
            ClearPendingFields();
        }

        private void ClearPendingFields()
        {
            _pendingEventId = string.Empty;
            _pendingKingdomId = string.Empty;
            _pendingAssignmentId = string.Empty;
            _pendingHolderClanId = string.Empty;
            _pendingOffice = -1;
            _pendingQuality = -1;
            _pendingCompetence = 0f;
            _pendingCreatedDay = -1f;
            _pendingPresented = false;
        }

        private void PruneExpiredAspectModifiers()
        {
            float currentDay = (float)CampaignTime.Now.ToDays;
            List<string> expiredKeys = _aspectExpiryDayByKey
                .Where(entry => entry.Value <= currentDay || !_aspectMultiplierByKey.ContainsKey(entry.Key))
                .Select(entry => entry.Key)
                .ToList();
            foreach (string key in expiredKeys)
            {
                _aspectExpiryDayByKey.Remove(key);
                _aspectMultiplierByKey.Remove(key);
            }

            List<string> orphanedMultipliers = _aspectMultiplierByKey.Keys
                .Where(key => !_aspectExpiryDayByKey.ContainsKey(key))
                .ToList();
            foreach (string key in orphanedMultipliers)
                _aspectMultiplierByKey.Remove(key);

            if (expiredKeys.Count > 0 || orphanedMultipliers.Count > 0)
                InvalidateAspectIndex();
        }

        private void PruneExpiredIncidentCooldowns()
        {
            float currentDay = (float)CampaignTime.Now.ToDays;
            List<string> expiredKeys = _incidentCooldownUntilDayByKey
                .Where(entry => entry.Value <= currentDay)
                .Select(entry => entry.Key)
                .ToList();
            foreach (string key in expiredKeys)
                _incidentCooldownUntilDayByKey.Remove(key);
        }

        private void ScheduleNextIncidentCheck(float currentDay)
        {
            int randomExtraDays = C.CouncilIncidentCheckIntervalRandomExtraDays > 0
                ? MBRandom.RandomInt(C.CouncilIncidentCheckIntervalRandomExtraDays)
                : 0;
            _nextIncidentCheckDay = currentDay
                + C.CouncilIncidentCheckIntervalBaseDays
                + randomExtraDays;
        }

        private bool IsIncidentOnCooldown(
            Kingdom kingdom,
            CouncilIncidentDefinition definition,
            float currentDay)
        {
            if (kingdom == null || definition == null)
                return true;

            return _incidentCooldownUntilDayByKey.TryGetValue(
                    BuildIncidentCooldownKey(kingdom.StringId, definition.Id),
                    out float cooldownUntilDay)
                && cooldownUntilDay > currentDay;
        }

        private static CouncilIncidentDefinition FindFirstEligibleDefinition(
            Kingdom kingdom,
            PrivyCouncilBehavior council)
        {
            foreach (PrivyCouncilOffice office in Enum.GetValues(typeof(PrivyCouncilOffice)))
            {
                PrivyCouncilAssignmentDefinition assignment = council.GetOfficeAssignment(kingdom, office);
                CouncilIncidentDefinition definition = assignment == null
                    || !council.IsAssignmentActive(kingdom, office, assignment.Id)
                    ? null
                    : CouncilIncidentRegistry.GetEligibleDefinitions(office, assignment.Id).FirstOrDefault();
                if (definition != null)
                    return definition;
            }

            return null;
        }

        private static CouncilIncidentDefinition SelectWeightedDefinition(
            IReadOnlyList<CouncilIncidentDefinition> definitions)
        {
            float totalWeight = definitions.Sum(definition => Math.Max(0f, definition.SelectionWeight));
            if (totalWeight <= 0f)
                return definitions.FirstOrDefault();

            float roll = MBRandom.RandomFloat * totalWeight;
            foreach (CouncilIncidentDefinition definition in definitions)
            {
                roll -= Math.Max(0f, definition.SelectionWeight);
                if (roll <= 0f)
                    return definition;
            }

            return definitions.LastOrDefault();
        }

        private static CouncilIncidentQuality RollQuality(float competence)
        {
            float chance = Clamp(competence / 100f, 0f, 1f);
            if (MBRandom.RandomFloat <= chance)
                return CouncilIncidentQuality.Success;
            if (MBRandom.RandomFloat <= chance)
                return CouncilIncidentQuality.Neutral;
            return CouncilIncidentQuality.Failure;
        }

        private static string BuildIncidentCooldownKey(string kingdomId, string eventId)
        {
            return (kingdomId ?? string.Empty) + ":" + (eventId ?? string.Empty);
        }

        private static string BuildAspectPrefix(
            string kingdomId,
            PrivyCouncilOffice office,
            string assignmentId,
            string aspectId)
        {
            return BuildAssignmentAspectPrefix(kingdomId, office, assignmentId)
                + "|" + (aspectId ?? string.Empty)
                + "|";
        }

        private static string BuildAssignmentAspectPrefix(
            string kingdomId,
            PrivyCouncilOffice office,
            string assignmentId)
        {
            return (kingdomId ?? string.Empty)
                + "|" + office
                + "|" + (assignmentId ?? string.Empty);
        }

        private static string BuildAspectKey(
            string kingdomId,
            PrivyCouncilOffice office,
            string assignmentId,
            string aspectId,
            string sourceId)
        {
            return BuildAspectPrefix(kingdomId, office, assignmentId, aspectId) + (sourceId ?? string.Empty);
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            if (value < minimum) return minimum;
            if (value > maximum) return maximum;
            return value;
        }
    }
}
