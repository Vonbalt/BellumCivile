using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class ExiledClanRecoveryBehavior
    {
        internal const int VoluntaryReturnCooldownDays = 30;
        internal const int RebellionReturnCooldownDays = 90;
        private Dictionary<string, string> _departureRulingHouses = new Dictionary<string, string>();
        private Dictionary<string, int> _departureKinds = new Dictionary<string, int>();
        private Dictionary<string, CampaignTime> _departureDates = new Dictionary<string, CampaignTime>();
        private Dictionary<string, CampaignTime> _exileStartDates = new Dictionary<string, CampaignTime>();
        private bool _admissionHistoryReconciled;

        private enum DepartureKind { Unknown, Expelled, Voluntary, Rebellion, RealmDestroyed }

        private void SyncAdmissionData(IDataStore store)
        {
            // Reuse registered primitive containers; no new saveable record type is required.
            store.SyncData("BellumCivile_ExileDepartureHouses", ref _departureRulingHouses);
            store.SyncData("BellumCivile_ExileDepartureKinds", ref _departureKinds);
            store.SyncData("BellumCivile_ExileDepartureDates", ref _departureDates);
            store.SyncData("BellumCivile_ExileStartDates", ref _exileStartDates);
            if (store.IsLoading) _admissionHistoryReconciled = false;
        }

        private void EnsureAdmissionCollections()
        {
            if (_departureRulingHouses == null) _departureRulingHouses = new Dictionary<string, string>();
            if (_departureKinds == null) _departureKinds = new Dictionary<string, int>();
            if (_departureDates == null) _departureDates = new Dictionary<string, CampaignTime>();
            if (_exileStartDates == null) _exileStartDates = new Dictionary<string, CampaignTime>();
        }

        private static string DepartureKey(Clan clan, Kingdom realm) => clan.StringId + "|" + realm.StringId;

        private static bool IsHistoryEligibleClan(Clan clan) => clan != null && clan != Clan.PlayerClan
            && !clan.IsEliminated && clan.IsNoble && !clan.IsMinorFaction && !clan.IsUnderMercenaryService;

        private static bool IsScriptedDeparture(Kingdom realm) => realm == null
            || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm)
            || CrownAccessionBehavior.Instance?.IsRealmUnionProtected(realm) == true
            || Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>()?.IsCrownPromotionRealmProtected(realm) == true
            || CivilWarConflictBehavior.IsRealmTransferPending(realm);

        private void RecordDeparture(Clan clan, Kingdom realm, ExileCause cause, bool rebelled = false)
        {
            if (!IsHistoryEligibleClan(clan) || realm == null
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm)) return;
            EnsureAdmissionCollections();
            DepartureKind kind = cause == ExileCause.KingdomDestroyed ? DepartureKind.RealmDestroyed
                : cause == ExileCause.VoluntaryDeparture ? (rebelled ? DepartureKind.Rebellion : DepartureKind.Voluntary)
                : cause == ExileCause.Unknown ? DepartureKind.Unknown : DepartureKind.Expelled;
            string key = DepartureKey(clan, realm);
            _departureKinds[key] = (int)kind;
            _departureRulingHouses[key] = realm.RulingClan?.StringId ?? string.Empty;
            _departureDates[key] = CampaignTime.Now;
        }

        internal void RegisterExpulsion(Clan clan, Kingdom realm)
        {
            if (!IsHistoryEligibleClan(clan) || realm == null || clan.Kingdom == realm
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(clan.Kingdom)) return;
            EnsureCollectionsInitialized();
            RecordDeparture(clan, realm, ExileCause.Expulsion);
            if (!IsRecoverableExileCandidate(clan)) return;
            RecordExileContext(clan, realm, ExileCause.Expulsion);
            TrackExiledClan(clan, realm, Enumerable.Empty<Kingdom>(), CampaignTime.Now + CampaignTime.Days(1));
        }

        internal bool HasDepartureRecord(Clan clan, Kingdom realm) => clan != null && realm != null
            && _departureKinds.ContainsKey(DepartureKey(clan, realm));

        internal bool CanReturnToRealm(Clan clan, Kingdom realm)
        {
            if (!HasDepartureRecord(clan, realm)) return true;
            string key = DepartureKey(clan, realm);
            DepartureKind kind = (DepartureKind)_departureKinds[key];
            string house = GetDictionaryValue(_departureRulingHouses, key);
            float elapsed = _departureDates.TryGetValue(key, out CampaignTime date)
                ? Math.Max(0f, (float)date.ElapsedDaysUntilNow) : 0f;
            if (kind == DepartureKind.Expelled)
                return !string.IsNullOrEmpty(house) && realm.RulingClan?.StringId != house;
            if (kind == DepartureKind.Voluntary) return elapsed >= VoluntaryReturnCooldownDays;
            if (kind == DepartureKind.Rebellion) return elapsed >= RebellionReturnCooldownDays;
            return kind == DepartureKind.RealmDestroyed;
        }

        internal float GetReturnDistrust(Clan clan, Kingdom realm)
        {
            if (!HasDepartureRecord(clan, realm)) return 0f;
            string key = DepartureKey(clan, realm);
            var kind = (DepartureKind)_departureKinds[key];
            if (kind != DepartureKind.Voluntary && kind != DepartureKind.Rebellion) return 0f;
            float elapsed = _departureDates.TryGetValue(key, out CampaignTime date)
                ? Math.Max(0f, (float)date.ElapsedDaysUntilNow) : 0f;
            float cooldown = kind == DepartureKind.Rebellion ? RebellionReturnCooldownDays : VoluntaryReturnCooldownDays;
            float fadeDays = Math.Max(1, CampaignTime.DaysInYear);
            return (kind == DepartureKind.Rebellion ? 30f : 15f)
                * Math.Max(0f, 1f - Math.Max(0f, elapsed - cooldown) / fadeDays);
        }

        internal float GetExileDays(Clan clan) => clan != null && _exileStartDates.TryGetValue(clan.StringId, out CampaignTime date)
            ? Math.Max(0f, (float)date.ElapsedDaysUntilNow) : 0f;

        internal bool IsExcludedRefuge(Clan clan, Kingdom realm)
        {
            if (clan == null || realm == null) return true;
            if (!CanReturnToRealm(clan, realm)) return true;
            string origin = GetDictionaryValue(_trackedOriginKingdomIds, clan.StringId);
            if (realm.StringId == origin && !HasDepartureRecord(clan, realm)) return true;
            string excluded = GetDictionaryValue(_trackedExcludedKingdomIds, clan.StringId);
            return !string.IsNullOrEmpty(excluded) && excluded.Split('|').Contains(realm.StringId);
        }

        internal void RecordPlayerInvitation(Clan clan, Kingdom realm)
        {
            if (clan == null || realm?.RulingClan != Clan.PlayerClan || clan.Kingdom != realm) return;
            string key = DepartureKey(clan, realm);
            _departureKinds.Remove(key);
            _departureDates.Remove(key);
            _departureRulingHouses.Remove(key);
        }

        internal bool TakeOwnershipOfAutomaticRecruitment(Clan clan)
        {
            if (!IsRecoverableExileCandidate(clan)) return false;
            EnsureCollectionsInitialized();
            if (!_trackedExiledClanIds.Contains(clan.StringId))
                TrackExiledClan(clan, null, Enumerable.Empty<Kingdom>(), CampaignTime.Now);
            return true;
        }

        private void BackfillUntrackedExiles()
        {
            foreach (Clan clan in Clan.All)
                TakeOwnershipOfAutomaticRecruitment(clan);
        }

        private void ReconcileAdmissionHistory()
        {
            foreach (string clanId in _trackedExiledClanIds)
            {
                // Legacy saves did not record the expelling house. Retain their current
                // origin exclusion instead of attributing the expulsion to a later dynasty.
                if (!_exileStartDates.ContainsKey(clanId)) _exileStartDates[clanId] = CampaignTime.Now;
            }
            _admissionHistoryReconciled = true;
        }

        private void RemoveDepartureHistory(string clanId)
        {
            foreach (string key in _departureKinds.Keys.Where(key => key.StartsWith(clanId + "|", StringComparison.Ordinal)).ToList())
            {
                _departureKinds.Remove(key);
                _departureDates.Remove(key);
                _departureRulingHouses.Remove(key);
            }
        }
    }
}
