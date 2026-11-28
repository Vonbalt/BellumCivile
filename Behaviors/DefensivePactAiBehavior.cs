using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BellumCivile.Behaviors
{
    // Fixed campaign days, independent of the normal/fast calendar setting.
    public sealed class DefensivePactAiBehavior : CampaignBehaviorBase
    {
        internal const int EvaluationIntervalDays = 30;
        internal const int MaxRealmEvaluationsPerDay = 4;
        internal const int MaxCandidateChecks = 12;
        private const double MaximumRoll = DefensivePactAiRules.BaseMonthlyChance
            + DefensivePactAiRules.UnderstrengthBonus + DefensivePactAiRules.RelativeWeaknessBonus;

        private sealed class DueRealm { internal Kingdom Realm; internal int Day; }
        private sealed class DueComparer : IComparer<DueRealm>
        {
            public int Compare(DueRealm first, DueRealm second)
            {
                int day = first.Day.CompareTo(second.Day);
                return day != 0 ? day : string.CompareOrdinal(first.Realm.StringId, second.Realm.StringId);
            }
        }

        private Dictionary<string, int> _nextDay = new Dictionary<string, int>(StringComparer.Ordinal);
        private int _lastDay = -1;
        private int _nextDiscoveryDay;
        private readonly SortedSet<DueRealm> _schedule = new SortedSet<DueRealm>(new DueComparer());
        private readonly List<Kingdom> _realms = new List<Kingdom>();
        private readonly Dictionary<string, Kingdom> _realmsById = new Dictionary<string, Kingdom>(StringComparer.Ordinal);
        private bool _needsRebuild = true;

        public override void RegisterEvents() => CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_DefensivePactAiNextDay", ref _nextDay);
            dataStore.SyncData("BellumCivile_DefensivePactAiLastDay", ref _lastDay);
            dataStore.SyncData("BellumCivile_DefensivePactAiDiscoveryDay", ref _nextDiscoveryDay);
            if (_nextDay == null) _nextDay = new Dictionary<string, int>(StringComparer.Ordinal);
            if (dataStore.IsLoading) _needsRebuild = true;
        }

        private void OnDailyTick()
        {
            if (!BellumCivileOptions.DefensivePactsActive || Campaign.Current == null) return;
            double now = CampaignTime.Now.ToDays;
            if (!DefensivePactThreatRules.Finite(now) || now < 0 || now >= int.MaxValue - EvaluationIntervalDays) return;
            int today = (int)Math.Floor(now);
            if (_lastDay == today) return;
            _lastDay = today;
            if (_needsRebuild || today >= _nextDiscoveryDay) Rebuild(today);
            var pacts = Campaign.Current.GetCampaignBehavior<DefensivePactBehavior>();
            var threat = Campaign.Current.GetCampaignBehavior<DefensivePactThreatBehavior>();
            if (pacts == null || threat?.IsReady != true) return;
            foreach (var realm in TakeDue(today))
            {
                if (realm.IsEliminated || realm.RulingClan?.Leader == null || realm.RulingClan == Clan.PlayerClan) continue;
                double roll = MBRandom.RandomFloat;
                if (roll >= MaximumRoll) continue;
                try { Evaluate(pacts, threat, realm, now, roll); }
                catch (Exception ex) { BellumCivileLogger.Log($"Defensive pact AI evaluation deferred; realm={realm.StringId}; error={ex}"); }
            }
        }

        private List<Kingdom> TakeDue(int today)
        {
            var result = new List<Kingdom>();
            while (_schedule.Count > 0 && _schedule.Min.Day <= today && result.Count < MaxRealmEvaluationsPerDay)
            {
                var entry = _schedule.Min;
                _schedule.Remove(entry);
                entry.Day = today + EvaluationIntervalDays;
                _nextDay[entry.Realm.StringId] = entry.Day;
                _schedule.Add(entry);
                result.Add(entry.Realm);
            }
            return result;
        }

        private void Rebuild(int today)
        {
            _schedule.Clear(); _realms.Clear(); _realmsById.Clear();
            foreach (var realm in Kingdom.All.Where(k => k != null && !k.IsEliminated && !string.IsNullOrWhiteSpace(k.StringId)
                && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(k)))
            {
                if (_realmsById.ContainsKey(realm.StringId)) continue;
                if (!_nextDay.TryGetValue(realm.StringId, out int due)) _nextDay[realm.StringId] = due = today + InitialOffset(realm.StringId);
                _realms.Add(realm); _realmsById.Add(realm.StringId, realm);
                _schedule.Add(new DueRealm { Realm = realm, Day = due });
            }
            foreach (string id in _nextDay.Keys.Where(id => !_realmsById.ContainsKey(id)).ToList()) _nextDay.Remove(id);
            _nextDiscoveryDay = today + EvaluationIntervalDays;
            _needsRebuild = false;
        }

        internal static int InitialOffset(string id)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in id ?? string.Empty) hash = (hash ^ c) * 16777619;
                return (int)(hash % EvaluationIntervalDays);
            }
        }

        private void Evaluate(DefensivePactBehavior pacts, DefensivePactThreatBehavior threats, Kingdom member, double now, double roll)
        {
            if (member.RulingClan.Influence < DefensivePactVoteRules.InfluenceCost || pacts.HasMembership(member.StringId)) return;
            var sample = new List<Kingdom>();
            int seen = 0;
            int fiefs = member.Fiefs?.Count ?? 0;
            foreach (var realm in _realms)
            {
                if (realm == member || realm.IsEliminated || !IsThreatCandidate(pacts, member, realm, fiefs, threats.GetThreat(realm.StringId, now))) continue;
                seen++;
                if (sample.Count < MaxCandidateChecks) sample.Add(realm);
                else { int slot = MBRandom.RandomInt(seen); if (slot < MaxCandidateChecks) sample[slot] = realm; }
            }
            Kingdom best = null;
            double bestThreat = -1;
            foreach (var target in sample)
            {
                var assessment = pacts.Assess(member, target);
                if (assessment?.IsEligible != true || assessment.TargetStrength <= 0) continue;
                double pactStrength = 0;
                foreach (var entry in pacts.GetMembersAgainst(target.StringId))
                    if (entry != null && _realmsById.TryGetValue(entry.RealmId, out var ally) && !ally.IsEliminated)
                        pactStrength += Math.Max(0, ally.CurrentTotalStrength);
                double chance = DefensivePactAiRules.JoinChance(assessment.MemberStrength / assessment.TargetStrength,
                    pactStrength / assessment.TargetStrength);
                if (roll < chance && assessment.Threat > bestThreat) { best = target; bestThreat = assessment.Threat; }
            }
            if (best != null) pacts.TryFileJoinVote(member.RulingClan, best);
        }

        private static bool IsThreatCandidate(DefensivePactBehavior pacts, Kingdom member, Kingdom target, int fiefs, double threat) =>
            threat >= DefensivePactEligibilityRules.NeighborThreshold
            || (threat >= DefensivePactEligibilityRules.EarlyNeighborThreshold
                && DefensivePactEligibilityRules.CanUseEarlyNeighborBand(fiefs, DefensivePactCampaignAssessment.SharesCulture(member, target))
                && pacts.HasKnownDirectFrontier(member.StringId, target.StringId));
    }
}
