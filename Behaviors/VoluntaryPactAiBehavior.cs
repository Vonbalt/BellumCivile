using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BellumCivile.Behaviors
{
    public sealed class VoluntaryPactAiBehavior : CampaignBehaviorBase
    {
        private sealed class DueRealm
        {
            internal Kingdom Realm;
            internal int Day;
        }
        private sealed class DueComparer : IComparer<DueRealm>
        {
            public int Compare(DueRealm a, DueRealm b)
            {
                int day = a.Day.CompareTo(b.Day);
                return day != 0 ? day : string.CompareOrdinal(a.Realm.StringId, b.Realm.StringId);
            }
        }

        private Dictionary<string, int> _nextDay = new Dictionary<string, int>();
        private int _lastDay = -1;
        private int _nextDiscoveryDay;
        private readonly SortedSet<DueRealm> _schedule = new SortedSet<DueRealm>(new DueComparer());
        private readonly List<Kingdom> _realms = new List<Kingdom>();
        private bool _needsRebuild = true;
        private const int MaxCandidateChecks = 12;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_VoluntaryPactAiNextDay", ref _nextDay);
            dataStore.SyncData("BellumCivile_VoluntaryPactAiLastDay", ref _lastDay);
            dataStore.SyncData("BellumCivile_VoluntaryPactAiDiscoveryDay", ref _nextDiscoveryDay);
            if (_nextDay == null) _nextDay = new Dictionary<string, int>();
            if (dataStore.IsLoading) _needsRebuild = true;
        }

        private void OnDailyTick()
        {
            if (!BellumCivileOptions.EnableWarPeaceLogicRevamp || Campaign.Current == null) return;
            double now = CampaignTime.Now.ToDays;
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || now >= int.MaxValue - 31) return;
            int today = (int)Math.Floor(now);
            if (_lastDay == today) return;
            _lastDay = today;
            if (_needsRebuild || today >= _nextDiscoveryDay) Rebuild(today);
            var owner = Campaign.Current.GetCampaignBehavior<VoluntaryPactProposalBehavior>();
            if (owner == null) return;
            foreach (var realm in TakeDue(today))
            {
                if (realm.IsEliminated || realm.RulingClan == null
                    || realm.RulingClan == Clan.PlayerClan || MBRandom.RandomFloat >= PeacetimeHostagePactRules.EvaluationChance)
                    continue;
                try { Evaluate(owner, realm, today); }
                catch (Exception ex)
                { BellumCivileLogger.Log($"Voluntary pact AI evaluation deferred; realm={realm.StringId}; error={ex}"); }
            }
        }

        private List<Kingdom> TakeDue(int today)
        {
            var result = new List<Kingdom>();
            while (_schedule.Count > 0 && _schedule.Min.Day <= today
                && result.Count < PeacetimeHostagePactRules.MaxRealmEvaluationsPerDay)
            {
                var entry = _schedule.Min;
                _schedule.Remove(entry);
                entry.Day = today + PeacetimeHostagePactRules.EvaluationIntervalDays;
                _nextDay[entry.Realm.StringId] = entry.Day;
                _schedule.Add(entry);
                result.Add(entry.Realm);
            }
            return result;
        }

        private void Rebuild(int today)
        {
            _schedule.Clear();
            _realms.Clear();
            foreach (var realm in Kingdom.All.Where(k => k != null && !k.IsEliminated && !string.IsNullOrEmpty(k.StringId)))
            {
                if (!_nextDay.TryGetValue(realm.StringId, out int due))
                    _nextDay[realm.StringId] = due = today + InitialOffset(realm.StringId);
                _realms.Add(realm);
                _schedule.Add(new DueRealm { Realm = realm, Day = due });
            }
            var liveIds = new HashSet<string>(_realms.Select(k => k.StringId));
            foreach (string id in _nextDay.Keys.Where(id => !liveIds.Contains(id)).ToList()) _nextDay.Remove(id);
            _nextDiscoveryDay = today + PeacetimeHostagePactRules.EvaluationIntervalDays;
            _needsRebuild = false;
        }

        internal static int InitialOffset(string id)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in id ?? string.Empty) hash = (hash ^ c) * 16777619;
                return (int)(hash % PeacetimeHostagePactRules.EvaluationIntervalDays);
            }
        }

        private void Evaluate(VoluntaryPactProposalBehavior owner, Kingdom source, int today)
        {
            var house = source.RulingClan;
            if (house?.Leader == null || house.Influence < PeacetimeHostagePactRules.ProposalInfluenceCost) return;
            var hostage = TreatyHostageEligibility.GetCandidates(source)
                .Where(c => c.Hero != null && c.Hero != Hero.MainHero)
                .OrderByDescending(c => c.Tier).ThenBy(c => c.Hero.StringId, StringComparer.Ordinal).FirstOrDefault();
            if (hostage == null) return;
            var sample = new List<Kingdom>();
            int seen = 0;
            foreach (var realm in _realms)
            {
                if (realm == source || realm.IsEliminated) continue;
                seen++;
                if (sample.Count < MaxCandidateChecks) sample.Add(realm);
                else { int slot = MBRandom.RandomInt(seen); if (slot < MaxCandidateChecks) sample[slot] = realm; }
            }
            Kingdom best = null;
            int bestPayment = 0;
            float bestScore = float.MinValue;
            int duration = BellumCivileOptions.HostagePactMinimumDurationDays;
            foreach (var target in sample)
            {
                if (!owner.CanPropose(source, target, house, out _)) continue;
                var trial = new VoluntaryPactProposalRecord { Id = "ai-preview", Source = source, Target = target,
                    SourceHouse = house, TargetHouse = target.RulingClan, Sponsor = house, SourceHostage = hostage.Hero,
                    DurationDays = duration, BaselineDays = duration, Payer = source.Leader, Payee = target.Leader,
                    CreatedDay = today, Phase = VoluntaryPactProposalPhase.SourceVote };
                if (!PeacetimeHostagePactScoring.TryScore(trial, source, out float ownScore, out _)
                    || ownScore < PeacetimeHostagePactRules.AcceptanceThreshold) continue;
                if (!TryChoosePayment(trial, out int offer, out float theirScore)) continue;
                float combined = ownScore + theirScore - offer / 10000f;
                if (combined > bestScore) { best = target; bestPayment = offer; bestScore = combined; }
            }
            if (best != null) owner.TryCreateAndFile(source, best, house, hostage.Hero, duration, bestPayment, out _);
        }

        internal static int[] CandidatePayments(int availableGold)
        {
            int budget = Math.Min(100000, Math.Max(0, availableGold / 10));
            return new[] { 0, 10000, 25000, 50000, 100000, budget }
                .Where(g => g <= budget).Distinct().OrderBy(g => g).ToArray();
        }

        internal static bool TryChoosePayment(VoluntaryPactProposalRecord trial, out int payment, out float targetScore)
        {
            payment = 0; targetScore = 0;
            if (trial?.Source?.Leader == null || trial.Target?.Leader == null) return false;
            int original = trial.Compensation;
            try
            {
                foreach (int offer in CandidatePayments(trial.Source.Leader.Gold))
                {
                    if (trial.Target.Leader.Gold > int.MaxValue - offer) continue;
                    trial.Compensation = offer;
                    if (!PeacetimeHostagePactScoring.TryScore(trial, trial.Target, out float score, out _)
                        || score < PeacetimeHostagePactRules.AcceptanceThreshold) continue;
                    payment = offer; targetScore = score; return true;
                }
                return false;
            }
            finally { trial.Compensation = original; }
        }
    }
}
