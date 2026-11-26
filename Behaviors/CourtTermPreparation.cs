using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private Dictionary<string, CourtTermPreparationRecord> _pendingCourtTerms = new Dictionary<string, CourtTermPreparationRecord>();
        private List<string> _courtTermsScheduledToday = new List<string>();
        private double _lastTermPreparationDay = -1;
        private long _lastTermPreparationHour = -1;
        private Dictionary<string, Kingdom> _courtPreparationRealms;

        private void SyncTermPreparationData(IDataStore store)
        {
            store.SyncData("BC_CourtPendingTerms", ref _pendingCourtTerms);
            store.SyncData("BC_CourtLastPreparationDay", ref _lastTermPreparationDay);
            store.SyncData("BC_CourtTermsScheduledToday", ref _courtTermsScheduledToday);
            store.SyncData("BC_CourtLastPreparationHour", ref _lastTermPreparationHour);
            _pendingCourtTerms = _pendingCourtTerms ?? new Dictionary<string, CourtTermPreparationRecord>();
            _courtTermsScheduledToday = _courtTermsScheduledToday ?? new List<string>();
            if (store.IsLoading) _courtPreparationRealms = null;
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

        private void ScheduleCourtPreparation(IReadOnlyList<Kingdom> realms, FactionManagerBehavior manager, IdeologyBehavior ideology)
        {
            if (_pendingCourtTerms.Count == 0) return;
            double now = CampaignTime.Now.ToDays;
            if (_lastTermPreparationDay == Math.Floor(now)) return;
            _lastTermPreparationDay = Math.Floor(now);
            _lastTermPreparationHour = CourtTermScheduler.HourSlot(now);
            _courtPreparationRealms = IndexPreparationRealms(realms);
            foreach (var key in _pendingCourtTerms.Keys.ToList())
                if (!_courtPreparationRealms.ContainsKey(key) || _pendingCourtTerms[key]?.RealmId != key
                    || !_pendingCourtTerms[key].IsCurrent(now)) _pendingCourtTerms.Remove(key);
            var batch = CourtTermScheduler.Batch(_pendingCourtTerms.Values.Where(p =>
                TryGetCourtRuler(_courtPreparationRealms[p.RealmId], out _, out _)), now, Clan.PlayerClan?.Kingdom?.StringId);
            _courtTermsScheduledToday.Clear();
            _courtTermsScheduledToday.AddRange(batch.Select(p => p.RealmId));
            OpenScheduledCourtTerms(batch, now, manager, ideology, daily: true);
        }

        private static Dictionary<string, Kingdom> IndexPreparationRealms(IEnumerable<Kingdom> realms) =>
            realms.Where(ValidRealm).ToDictionary(r => r.StringId, StringComparer.Ordinal);

        private void PrepareCourtTermsHourly()
        {
            if (_courtTermsScheduledToday.Count == 0) return;
            var manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            var ideology = Campaign.Current?.GetCampaignBehavior<IdeologyBehavior>();
            if (manager == null || ideology == null) return;
            PrepareScheduledCourtTerms(CampaignTime.Now.ToDays, manager, ideology);
        }

        private void PrepareScheduledCourtTerms(double now, FactionManagerBehavior manager, IdeologyBehavior ideology)
        {
            long hour = CourtTermScheduler.HourSlot(now);
            if (_courtTermsScheduledToday.Count == 0 || _lastTermPreparationDay != Math.Floor(now) || hour <= _lastTermPreparationHour) return;
            _lastTermPreparationHour = hour;
            _agendaTiming.BeginPass(CourtTimingYear(now));
            long started = Stopwatch.GetTimestamp();
            try
            {
                if (_courtPreparationRealms == null) _courtPreparationRealms = IndexPreparationRealms(Kingdom.All);
                var batch = new List<CourtTermPreparationRecord>(_courtTermsScheduledToday.Count);
                for (int i = 0; i < _courtTermsScheduledToday.Count;)
                {
                    if (_pendingCourtTerms.TryGetValue(_courtTermsScheduledToday[i], out var term) && term?.IsCurrent(now) == true)
                    {
                        batch.Add(term);
                        i++;
                    }
                    else _courtTermsScheduledToday.RemoveAt(i);
                }
                OpenScheduledCourtTerms(batch, now, manager, ideology, daily: false);
            }
            finally { _agendaTiming.EndHour(now, Stopwatch.GetTimestamp() - started); }
        }

        private void OpenScheduledCourtTerms(IReadOnlyList<CourtTermPreparationRecord> batch, double now,
            FactionManagerBehavior manager, IdeologyBehavior ideology, bool daily)
        {
            int openings = CourtTermScheduler.OpeningsThisTick(batch, now, daily);
            foreach (var term in batch)
            {
                if (openings == 0) break;
                _courtTermsScheduledToday.Remove(term.RealmId);
                if (!_courtPreparationRealms.TryGetValue(term.RealmId, out var realm) || !ValidRealm(realm))
                {
                    _pendingCourtTerms.Remove(term.RealmId);
                    continue;
                }
                if (!TryGetCourtRuler(realm, out _, out _)) continue;
                openings--;
                // Claim the opening before callbacks can inspect the new term or save the campaign.
                _pendingCourtTerms.Remove(term.RealmId);
                if (!term.HasPreparationWindow(now))
                {
                    BellumCivileLogger.Log($"Court term preparation skipped after prolonged unavailability; realm={term.RealmId}; start={term.StartDay}; end={term.EndDay}; day={now}.");
                    continue;
                }
                long started = Stopwatch.GetTimestamp();
                try { OpenTerm(realm, manager, ideology, term); }
                finally { _agendaTiming.RecordRealm(term.RealmId, Stopwatch.GetTimestamp() - started); }
            }
        }

        private bool AwaitingTermAnnouncement(Kingdom realm, FactionObject faction, CourtAgendaRecord agenda) =>
            realm != null && (faction != null || realm.RulingClan != Clan.PlayerClan)
            && _pendingCourtTerms.TryGetValue(realm.StringId, out var pending) && pending?.IsCurrent(CampaignTime.Now.ToDays) == true
            && !(agenda?.IsUnopened == true || agenda?.IsFiled == true || agenda?.IsOngoingObjective == true);
    }
}
