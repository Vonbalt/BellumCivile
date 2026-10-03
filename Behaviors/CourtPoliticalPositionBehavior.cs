using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    // Only realm standing is persistent. All other position inputs are read in a scoring batch.
    public sealed class CourtPoliticalPositionBehavior : CampaignBehaviorBase
    {
        private Dictionary<string, string> _realmByClan = new Dictionary<string, string>();
        private Dictionary<string, float> _standingStartDays = new Dictionary<string, float>();
        private bool _sessionReady;
        private int _reportYear = -1, _batches, _evaluations, _switches, _neighborReads, _missingCouncil, _affiliationBatches;
        private double _scoringMilliseconds;
        private CourtPoliticalPositionScore _totals;
        private bool _reportedNeighborFailure;
        private int _socialEvaluations, _relationReads;
        private double _socialMilliseconds;
        private float _friendshipTotal, _marriageTotal, _hierarchyTotal;

        internal void RecordSocialEvaluation(CourtSocialTieScore score, double milliseconds, int relationReads)
        {
            FlushTelemetry();
            _socialEvaluations++;
            _relationReads += relationReads;
            _socialMilliseconds += milliseconds;
            _friendshipTotal += score.Friendship;
            _marriageTotal += score.Marriage;
            _hierarchyTotal += score.Hierarchy;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
            CampaignEvents.OnClanCreatedEvent.AddNonSerializedListener(this, OnClanCreated);
            CampaignEvents.OnClanDestroyedEvent.AddNonSerializedListener(this, OnClanDestroyed);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, FlushTelemetry);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_CourtStandingRealms", ref _realmByClan);
            dataStore.SyncData("BellumCivile_CourtStandingStartDays", ref _standingStartDays);
            _realmByClan = _realmByClan ?? new Dictionary<string, string>();
            _standingStartDays = _standingStartDays ?? new Dictionary<string, float>();
        }

        internal static bool IsNoble(Clan clan) => clan != null && !clan.IsEliminated
            && !clan.IsMinorFaction && !clan.IsUnderMercenaryService;

        internal static Kingdom ResolveRealm(Kingdom kingdom)
        {
            if (kingdom == null)
                return null;
            Kingdom realm = Campaign.Current?.GetCampaignBehavior<SuccessionLawBehavior>()?.ResolvePermanentRealm(kingdom) ?? kingdom;
            return BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm) ? null : realm;
        }

        private static float CurrentDay => (float)CampaignTime.Now.ToDays;
        private static int DaysPerYear => Math.Max(1, CampaignTime.DaysInYear);

        private void OnNewGameCreated(CampaignGameStarter starter)
        {
            // Original houses predate the campaign; this is setup credit, not inferred history.
            foreach (Clan clan in Clan.All)
                if (IsNoble(clan) && clan != Clan.PlayerClan && ResolveRealm(clan.Kingdom) is Kingdom realm)
                    SetStanding(clan, realm, C.IdeologyEstablishedRealmYears);
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            foreach (Clan clan in Clan.All)
            {
                if (!IsNoble(clan) || _realmByClan.ContainsKey(clan.StringId))
                    continue;
                Kingdom realm = ResolveRealm(clan.Kingdom);
                if (realm == null)
                    continue;
                // Old saves cannot reconstruct continuous tenure across temporary wars.
                // Credit only the known vanilla interval, capped at half established standing.
                float knownYears = Math.Max(0f, clan.LastFactionChangeTime.ElapsedDaysUntilNow) / DaysPerYear;
                SetStanding(clan, realm, Math.Min(C.IdeologyLegacyRealmYears, knownYears));
            }
            _sessionReady = true;
        }

        private void SetStanding(Clan clan, Kingdom realm, float years)
        {
            _realmByClan[clan.StringId] = realm.StringId;
            _standingStartDays[clan.StringId] = CurrentDay - Math.Max(0f, years) * DaysPerYear;
        }

        internal float GetStandingYears(Clan clan, Kingdom realm)
        {
            if (!IsNoble(clan) || realm == null)
                return 0f;
            if (!_realmByClan.TryGetValue(clan.StringId, out string recordedRealm) || recordedRealm != realm.StringId
                || !_standingStartDays.ContainsKey(clan.StringId))
                SetStanding(clan, realm, 0f);
            return Math.Max(0f, CurrentDay - _standingStartDays[clan.StringId]) / DaysPerYear;
        }

        internal static void MoveToSuccessorRealm(Clan clan, Kingdom formerRealm, Kingdom successor, Action move)
        {
            CourtPoliticalPositionBehavior history = Campaign.Current?.GetCampaignBehavior<CourtPoliticalPositionBehavior>();
            Kingdom origin = ResolveRealm(formerRealm);
            if (history == null || !IsNoble(clan) || origin == null || successor == null)
            {
                move();
                return;
            }
            float years = history.GetStandingYears(clan, origin);
            history.SetStanding(clan, successor, years);
            try
            {
                move();
            }
            finally
            {
                // A failed move must not erase the house's old standing; a completed one
                // retains it even if a lifecycle callback briefly read the previous realm.
                Kingdom actualRealm = ResolveRealm(clan.Kingdom);
                if (actualRealm == successor || actualRealm == origin)
                    history.SetStanding(clan, actualRealm, years);
            }
        }

        private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
        {
            if (!_sessionReady || clan == null || clan.IsMinorFaction)
                return;
            Kingdom oldRealm = ResolveRealm(oldKingdom);
            Kingdom newRealm = ResolveRealm(newKingdom);
            // Unknown temporary shells are not evidence that a house emigrated.
            if (newKingdom != null && newRealm == null)
                return;
            if (newRealm == null || clan.IsUnderMercenaryService)
            {
                OnClanDestroyed(clan);
                return;
            }
            if (detail == ChangeKingdomAction.ChangeKingdomActionDetail.CreateKingdom && oldRealm != null)
            {
                SetStanding(clan, newRealm, GetStandingYears(clan, oldRealm));
                return;
            }
            if (oldRealm == newRealm
                || (_realmByClan.TryGetValue(clan.StringId, out string recordedRealm) && recordedRealm == newRealm.StringId))
                return;
            SetStanding(clan, newRealm, 0f);
        }

        private void OnClanCreated(Clan clan, bool isCompanion)
        {
            if (_sessionReady && IsNoble(clan) && ResolveRealm(clan.Kingdom) is Kingdom realm)
                SetStanding(clan, realm, 0f);
        }

        private void OnClanDestroyed(Clan clan)
        {
            if (clan == null) return;
            _realmByClan.Remove(clan.StringId);
            _standingStartDays.Remove(clan.StringId);
        }

        internal void RecordScore(CourtPoliticalPositionScore score)
        {
            FlushTelemetry();
            _evaluations++;
            _totals.Add(score);
        }

        internal void RecordBatch(double milliseconds, int neighborReads, bool councilKnown, bool affiliationOnly = false)
        {
            FlushTelemetry();
            _batches++;
            _scoringMilliseconds += milliseconds;
            _neighborReads += neighborReads;
            if (affiliationOnly) _affiliationBatches++;
            else if (!councilKnown) _missingCouncil++;
        }

        internal void RecordSwitch() { FlushTelemetry(); _switches++; }

        internal void ReportNeighborFailure(Exception exception)
        {
            if (_reportedNeighborFailure) return;
            _reportedNeighborFailure = true;
            BellumCivileLogger.Log("Court political frontier input unavailable; no pathfinding fallback used: " + exception);
        }

        private void FlushTelemetry()
        {
            int year = (int)(CurrentDay / DaysPerYear);
            if (_reportYear == year) return;
            if (_reportYear >= 0 && _batches > 0)
            {
                float n = Math.Max(1, _evaluations);
                BellumCivileDebug.TraceYearlyReport("court_position", FormattableString.Invariant(
                    $"year={_reportYear}; batches={_batches}; evaluations={_evaluations}; switches={_switches}; scoring_ms={_scoringMilliseconds:F2}; neighbor_reads={_neighborReads}; council_unknown_batches={_missingCouncil}; affiliation_only_batches={_affiliationBatches}; mean_components: lawful_tenure={_totals.LawfulTenure/n:F2}, realm_standing={_totals.RealmStanding/n:F2}, subordinate_houses={_totals.SubordinateHouses/n:F2}, legal_territory={_totals.LegalTerritory/n:F2}, military_strength={_totals.MilitaryStrength/n:F2}, frontier={_totals.FrontierExposure/n:F2}, land_shortage={_totals.LandShortage/n:F2}, political_exclusion={_totals.PoliticalExclusion/n:F2}"));
            }
            if (_reportYear >= 0 && _socialEvaluations > 0)
            {
                float n = _socialEvaluations;
                BellumCivileDebug.TraceYearlyReport("court_social", FormattableString.Invariant(
                    $"year={_reportYear}; bloc_evaluations={_socialEvaluations}; relation_reads={_relationReads}; scoring_ms={_socialMilliseconds:F2}; mean_components: friendship={_friendshipTotal/n:F2}, marriage={_marriageTotal/n:F2}, hierarchy={_hierarchyTotal/n:F2}"));
            }
            _socialEvaluations = _relationReads = 0;
            _socialMilliseconds = 0;
            _friendshipTotal = _marriageTotal = _hierarchyTotal = 0f;
            _reportYear = year;
            _batches = _evaluations = _switches = _neighborReads = _missingCouncil = _affiliationBatches = 0;
            _scoringMilliseconds = 0;
            _totals = default;
        }
    }
}
