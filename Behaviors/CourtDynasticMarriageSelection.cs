using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Behaviors
{
    internal static partial class BellumMarriageStrategyHelper
    {
        internal static bool CourtMarriageParticipant(Hero hero) => hero?.Clan?.Leader != null
            && hero != hero.Clan.Leader && SuccessionLawHelper.IsBloodRelative(hero, hero.Clan.Leader)
            && MarriageParticipantReady(hero)
            && GetOfferParticipantRejectionReason(hero, true) == BellumMarriageRejectionReason.None
            && !StrategicMarriageBehavior.HasMarriageOfferFor(hero);

        internal static bool CourtMarriageCandidate(Hero hero) => hero?.Clan?.Leader != null
            && hero != hero.Clan.Leader && SuccessionLawHelper.IsBloodRelative(hero, hero.Clan.Leader)
            && MarriageProspectEligible(hero) && !StrategicMarriageBehavior.HasMarriageOfferFor(hero)
            && Campaign.Current?.GetCampaignBehavior<StrategicMarriageBehavior>()?.HasPendingProspect(hero) != true;

        internal static BellumMarriageMatch EvaluateCourtMarriage(Hero first, Hero second, bool requireAcceptance, Clan destination = null)
        {
            if (!CourtMarriageParticipant(first) || !CourtMarriageParticipant(second)) return null;
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (GetPairRejectionReason(first, second, manager, true) != BellumMarriageRejectionReason.None) return null;
            var context = new EvaluationContext(manager, true, new HashSet<Clan> { first.Clan, second.Clan });
            return destination == null ? EvaluateOutcome(first, second, context, requireAcceptance)
                : context.CanChooseHousehold(first, second, destination, out _)
                    ? EvaluateHousehold(first, second, context, destination, requireAcceptance) : null;
        }

        internal static bool CourtMarriageExecutionReady(BellumMarriageMatch match) => match?.Outcome?.StillMatches() == true
            && Campaign.Current.Models.MarriageModel.IsCoupleSuitableForMarriage(match.Suitor, match.Candidate);

        internal static List<BellumMarriageMatch> FindCourtMarriages(Kingdom realm, IReadOnlyList<Kingdom> targets, CourtAgendaRecord excluded = null)
        {
            var result = new List<BellumMarriageMatch>();
            var houses = new HashSet<Clan>(targets.Select(t => t.RulingClan)) { realm.RulingClan };
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            var context = new EvaluationContext(manager, true, houses);
            var relatives = realm.RulingClan.Heroes.Where(CourtMarriageCandidate).ToList();
            int pairs = 0, unavailable = 0, reserved = 0, domesticRefused = 0, foreignRefused = 0;
            foreach (var first in relatives)
            foreach (var target in targets)
            foreach (var second in target.RulingClan.Heroes.Where(CourtMarriageCandidate))
            {
                pairs++;
                if (GetPairRejectionReason(first, second, manager, true, allowTemporary: true) != BellumMarriageRejectionReason.None)
                { unavailable++; continue; }
                if (CourtAgendaBehavior.Current?.HasDynasticReservation(first, second, excluded) == true)
                { reserved++; continue; }
                var match = EvaluateOutcome(first, second, context, false);
                var calendar = CourtAgendaBehavior.Current;
                float forecastBonus = CourtDynasticRules.AcceptanceBonus(first.Clan == Clan.PlayerClan,
                    calendar?.GetFavoredBloc(realm) == FactionType.Nobility, true);
                float existingBonus = calendar?.DynasticMarriageBonus(first, second) ?? 0;
                if (match == null) { unavailable++; continue; }
                bool domestic = first.Clan == Clan.PlayerClan || match.SuitorAcceptance + forecastBonus - existingBonus >= BellumCivileConstants.MarriageStrategyMinimumScore;
                bool foreign = second.Clan == Clan.PlayerClan || match.CandidateAcceptance >= BellumCivileConstants.MarriageStrategyMinimumScore;
                if (!domestic) domesticRefused++;
                if (!foreign) foreignRefused++;
                if (domestic && foreign) result.Add(match);
            }
            BellumCivileDebug.TraceIfEnabled("court", $"Royal marriage candidates; realm={realm.StringId}; eligible_home_relatives={relatives.Count}; pairs={pairs}; unavailable={unavailable}; reserved={reserved}; domestic_refused={domesticRefused}; foreign_refused={foreignRefused}; accepted={result.Count}.");
            return result;
        }
    }
}
