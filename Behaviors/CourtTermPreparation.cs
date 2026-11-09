using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private Dictionary<string, CourtTermPreparationRecord> _pendingCourtTerms = new Dictionary<string, CourtTermPreparationRecord>();
        private double _lastTermPreparationDay = -1;

        private void SyncTermPreparationData(IDataStore store)
        {
            store.SyncData("BC_CourtPendingTerms", ref _pendingCourtTerms);
            store.SyncData("BC_CourtLastPreparationDay", ref _lastTermPreparationDay);
            _pendingCourtTerms = _pendingCourtTerms ?? new Dictionary<string, CourtTermPreparationRecord>();
        }

        private bool QueueCourtTerm(Kingdom realm)
        {
            double now = CampaignTime.Now.ToDays;
            if (!_nextTerms.TryGetValue(realm.StringId, out var next))
            {
                _nextTerms[realm.StringId] = CampaignTime.Days((float)CourtAgendaRules.NextBoundary(now, BellumCivileOptions.CourtTermDays));
                return false;
            }
            if (next.ToDays > now) return false;
            var term = CourtTermScheduler.Create(realm.StringId, next.ToDays, now,
                BellumCivileOptions.CourtTermDays, BellumCivileOptions.PoliticalDeliberationDays);
            if (term == null) return false;
            _pendingCourtTerms[realm.StringId] = term;
            _nextTerms[realm.StringId] = CampaignTime.Days((float)term.EndDay);
            return true;
        }

        private void PrepareQueuedCourtTerms(IReadOnlyList<Kingdom> realms, FactionManagerBehavior manager, IdeologyBehavior ideology)
        {
            if (_pendingCourtTerms.Count == 0) return;
            double now = CampaignTime.Now.ToDays;
            var byId = realms.Where(ValidRealm).ToDictionary(r => r.StringId, StringComparer.Ordinal);
            foreach (var key in _pendingCourtTerms.Keys.ToList())
                if (!byId.ContainsKey(key) || _pendingCourtTerms[key]?.RealmId != key
                    || !_pendingCourtTerms[key].IsCurrent(now)) _pendingCourtTerms.Remove(key);
            if (_lastTermPreparationDay == Math.Floor(now)) return;
            _lastTermPreparationDay = Math.Floor(now);
            var batch = CourtTermScheduler.Batch(_pendingCourtTerms.Values.Where(p =>
                TryGetCourtRuler(byId[p.RealmId], out _, out _)), now, Clan.PlayerClan?.Kingdom?.StringId);
            foreach (var term in batch)
            {
                Kingdom realm = byId[term.RealmId];
                if (!TryGetCourtRuler(realm, out _, out _)) continue;
                // Claim the opening before callbacks can inspect the new term or save the campaign.
                _pendingCourtTerms.Remove(term.RealmId);
                if (!term.HasPreparationWindow(now))
                {
                    BellumCivileLogger.Log($"Court term preparation skipped after prolonged unavailability; realm={term.RealmId}; start={term.StartDay}; end={term.EndDay}; day={now}.");
                    continue;
                }
                OpenTerm(realm, manager, ideology, term);
            }
        }

        private bool AwaitingTermAnnouncement(Kingdom realm, FactionObject faction, CourtAgendaRecord agenda) =>
            realm != null && (faction != null || realm.RulingClan != Clan.PlayerClan)
            && _pendingCourtTerms.TryGetValue(realm.StringId, out var pending) && pending?.IsCurrent(CampaignTime.Now.ToDays) == true
            && !(agenda?.IsUnopened == true || agenda?.IsFiled == true || agenda?.IsOngoingObjective == true);
    }
}
