using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile
{
    internal sealed class CourtClaimObjectiveSource : ICourtAgendaObjectiveSource
    {
        public string Kind => CourtClaimRules.Kind;
        internal static Settlement Fief(string id) => Settlement.All.FirstOrDefault(s => s.StringId == id);
        internal static Clan Beneficiary(string id) => Clan.All.FirstOrDefault(c => c.StringId == id);

        internal static FeudalTitleRecord FindClaimTitle(Clan clan, Settlement fief)
        {
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (clan == null || titles == null || !titles.TryGetBarony(fief, out var title)) return null;
            var visited = new HashSet<string>();
            while (title != null && title.IsActive && visited.Add(title.TitleId))
            {
                if (title.DeJureHolderClanId == clan.StringId || titles.HasActiveClaim(clan, title)) return title;
                title = titles.GetTitle(title.ParentTitleId);
            }
            return null;
        }

        internal static bool ValidClaim(CourtClaimRecord plan)
        {
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            var claimed = titles?.GetTitle(plan?.ClaimTitleId);
            if (plan?.Beneficiary == null || claimed?.IsActive != true || !titles.TryGetBarony(plan.Fief, out var title)) return false;
            var visited = new HashSet<string>();
            while (title != null && visited.Add(title.TitleId))
            {
                if (title == claimed) return claimed.DeJureHolderClanId == plan.Beneficiary.StringId
                    || titles.HasActiveClaim(plan.Beneficiary, claimed);
                title = titles.GetTitle(title.ParentTitleId);
            }
            return false;
        }

        private static bool EligibleOwner(CourtTermContext context, CourtObjectiveOwner owner) =>
            owner.Faction?.Type == FactionType.Nobility && owner.Faction.ParentKingdom == context.Realm
            && owner.Faction.Mood > -60 && CourtAgendaBehavior.MemberCount(owner.Faction) >= 2
            && CourtAgendaBehavior.ValidRealm(context.Realm) && WarPeaceRevampBehavior.IsRevampEnabled()
            && ClientKingdomBehavior.Instance?.IsClientKingdom(context.Realm) != true;

        internal static IReadOnlyList<CourtObjectiveCandidate> Discover(CourtTermContext context, FactionObject faction)
        {
            var result = new List<CourtObjectiveCandidate>();
            var members = faction.Members.Where(c => CourtAgendaBehavior.Eligible(c, context.Realm)
                && c != context.Realm.RulingClan).Distinct().ToList();
            var wars = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>();
            foreach (var fief in Settlement.All.Where(s => s.Town != null && s.OwnerClan?.Kingdom != context.Realm))
            {
                var target = fief.OwnerClan?.Kingdom;
                if (!CourtCampaignObjectiveSource.ValidPair(context.Realm, target)
                    || ClientKingdomBehavior.Instance?.IsClientKingdom(target) == true) continue;
                bool war = context.Realm.IsAtWarWith(target);
                if (war ? !CourtPeaceObjectiveSource.EligibleWar(context.Realm, target, wars?.GetActiveWar(context.Realm, target))
                    : !context.CanSupportCampaign(target)) continue;
                foreach (var clan in members)
                {
                    if (CourtAgendaBehavior.Current?.HasPendingClaimObjective(context.Realm, fief, clan) == true) continue;
                    if (!FiefNominationHelper.IsWithinOrdinaryOwnershipCeiling(clan) && !FiefNominationHelper.IsDirectDeJureHolder(clan, fief)) continue;
                    if (FindClaimTitle(clan, fief) == null) continue;
                    if (!war && !context.CampaignTargets(clan).Any(t => t.TargetKingdom == target && !t.IsActiveWar && !t.IsLiberationTarget)) continue;
                    result.Add(new CourtObjectiveCandidate(fief.StringId, "restore_claim", clan.StringId));
                }
            }
            return result;
        }

        public IEnumerable<CourtObjectiveCandidate> FindCandidates(CourtTermContext context, CourtObjectiveOwner owner) =>
            EligibleOwner(context, owner) ? context.ClaimCandidates(owner.Faction) : new List<CourtObjectiveCandidate>();

        public CourtObjectiveEvaluation EvaluateCandidate(CourtTermContext context, CourtObjectiveOwner owner, CourtObjectiveCandidate candidate)
        {
            bool eligible = EligibleOwner(context, owner) && context.ClaimCandidates(owner.Faction).Any(c =>
                c.TargetId == candidate.TargetId && c.ActionId == candidate.ActionId && c.BeneficiaryId == candidate.BeneficiaryId);
            return new CourtObjectiveEvaluation(eligible, eligible, new CourtObjectiveWeight(CourtClaimRules.SelectionWeight),
                "nobility_named_claim; actual_award_required; allocation_bonus=15");
        }

        public void ApplySelection(CourtAgendaRecord agenda, CourtObjectiveChoice choice)
        {
            var previous = agenda.GetObjective();
            var fief = Fief(choice.Candidate.TargetId);
            var clan = Beneficiary(choice.Candidate.BeneficiaryId);
            agenda.ObjectiveData = new CourtObjectiveRecord { Kind = Kind, TargetId = choice.Candidate.TargetId, ActionId = "restore_claim" };
            if (previous.HasTermSnapshot)
            {
                agenda.ObjectiveData.FreezeTerm(previous.SelectedDay, previous.DeadlineDay);
                agenda.SessionDate = CampaignTime.Days((float)CourtAgendaRules.EarlyObjectiveSession(previous.SelectedDay,
                    CourtAgendaRules.NominationDays(agenda.TermDays), CampaignTime.Now.ToDays, agenda.SessionDate.ToDays));
            }
            agenda.Claim = new CourtClaimRecord { Fief = fief, Beneficiary = clan, Target = fief?.OwnerClan?.Kingdom,
                ClaimTitleId = FindClaimTitle(clan, fief)?.TitleId, GraceDays = System.Math.Max(1, BellumCivileOptions.PoliticalDeliberationDays) };
            agenda.PolicyId = null;
        }
    }
}
