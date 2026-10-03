using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal sealed class CourtTradeObjectiveSource : ICourtAgendaObjectiveSource
    {
        public string Kind => CourtTradeRules.Kind;
        internal static bool Pair(Kingdom realm, Kingdom target) => CourtDynasticObjectiveSource.RealmPair(realm, target);
        internal static bool Legal(Kingdom realm, Kingdom target, bool foreignSupport)
        {
            if (!Pair(realm, target) || ClientKingdomBehavior.Instance?.CanMakeTradeAgreement(realm, target) == false) return false;
            var model = Campaign.Current.Models.TradeAgreementModel;
            return model.CanMakeTradeAgreement(realm, target, foreignSupport, out _)
                && model.CanMakeTradeAgreement(target, realm, false, out _);
        }
        private static bool Owner(CourtTermContext c, CourtObjectiveOwner o) => o.Faction?.Type == FactionType.Liberty
            && o.Faction.ParentKingdom == c.Realm && o.Faction.Mood > -60 && CourtAgendaBehavior.MemberCount(o.Faction) >= 2;

        internal static IReadOnlyDictionary<Kingdom, float> Discover(CourtTermContext context)
        {
            var result = new Dictionary<Kingdom, float>();
            if (!CourtAgendaBehavior.ValidRealm(context.Realm)) return result;
            var model = Campaign.Current.Models.TradeAgreementModel;
            foreach (var target in Kingdom.All.Where(t => Pair(context.Realm, t)).OrderBy(t => t.StringId))
            {
                if (!Legal(context.Realm, target, true)) continue;
                float score = model.GetScoreOfStartingTradeAgreement(context.Realm, target, context.Realm.RulingClan, out _);
                if (!float.IsNaN(score) && !float.IsInfinity(score)) result[target] = .25f * (.5f + System.Math.Max(0, System.Math.Min(100, score)) / 200);
            }
            return result;
        }
        public IEnumerable<CourtObjectiveCandidate> FindCandidates(CourtTermContext c, CourtObjectiveOwner o) => Owner(c, o)
            ? c.TradeCandidates.Keys.Select(t => new CourtObjectiveCandidate(t.StringId, "trade")) : Enumerable.Empty<CourtObjectiveCandidate>();
        public CourtObjectiveEvaluation EvaluateCandidate(CourtTermContext c, CourtObjectiveOwner o, CourtObjectiveCandidate candidate)
        {
            var match = c.TradeCandidates.FirstOrDefault(p => p.Key.StringId == candidate.TargetId);
            bool valid = Owner(c, o) && match.Key != null;
            bool viable = valid && (c.ManualSelection || ProspectiveSponsor(c.Realm, match.Key, o.Faction) != null);
            float repetition = c.ManualSelection ? 1 : CourtAgendaBehavior.Current?.TradeRepeatWeight(c.Realm, match.Key) ?? 1;
            return new CourtObjectiveEvaluation(valid, viable, new CourtObjectiveWeight(valid ? match.Value * repetition : 0),
                viable ? "named_trade; native_foreign_support" : "trade_no_funded_willing_sponsor");
        }

        internal static Clan ProspectiveSponsor(Kingdom realm, Kingdom target, FactionObject faction, bool active = false)
        {
            if (realm == null || target == null || faction == null) return null;
            var calendar = CourtAgendaBehavior.Current;
            var model = Campaign.Current.Models.TradeAgreementModel;
            var clans = faction.Members.AsEnumerable();
            if (calendar?.GetFavoredBloc(realm) == FactionType.Liberty) clans = clans.Concat(new[] { realm.RulingClan });
            return clans.Where(c => CourtAgendaBehavior.Eligible(c, realm) && c != Clan.PlayerClan && c.CurrentTotalStrength > 0)
                .OrderBy(c => c.StringId).FirstOrDefault(c =>
                {
                    if (active && (calendar?.TradeBonus(realm, target, c) ?? 0) == 0) return false;
                    int cost = model.GetInfluenceCostOfProposingTradeAgreement(c);
                    if (!NpcInfluenceBudgetService.CanAfford(c, cost, NpcInfluenceExpenseKind.Discretionary)) return false;
                    float support = model.GetScoreOfStartingTradeAgreement(realm, target, c, out _);
                    // The model postfix may already contain this term's bonus.
                    if ((calendar?.TradeBonus(realm, target, c) ?? 0) == 0) support = CourtTradeRules.Support(support, true);
                    return support > 50;
                });
        }
        public void ApplySelection(CourtAgendaRecord a, CourtObjectiveChoice choice)
        {
            var previous = a.GetObjective();
            a.ObjectiveData = new CourtObjectiveRecord { Kind = Kind, TargetId = choice.Candidate.TargetId };
            if (previous.HasTermSnapshot)
            {
                a.ObjectiveData.FreezeTerm(previous.SelectedDay, previous.DeadlineDay);
                a.SessionDate = CampaignTime.Days((float)CourtAgendaRules.EarlyObjectiveSession(previous.SelectedDay,
                    CourtAgendaRules.NominationDays(a.TermDays), CampaignTime.Now.ToDays, a.SessionDate.ToDays));
            }
            a.Trade = new CourtTradeRecord { Target = Kingdom.All.FirstOrDefault(t => t.StringId == choice.Candidate.TargetId) };
            a.PolicyId = null;
        }
    }
}
