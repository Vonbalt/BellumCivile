using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;

namespace BellumCivile
{
    internal sealed class CourtTitleGrantObjectiveSource : ICourtAgendaObjectiveSource
    {
        public string Kind { get; }
        internal CourtTitleGrantObjectiveSource(string kind) { Kind = kind; }
        internal static FeudalTitleBehavior Titles => Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
        internal static FeudalTitleRecord Title(string id) => Titles?.GetTitle(id);
        internal static Clan ClanById(string id) => Clan.All.FirstOrDefault(c => c.StringId == id);
        internal static bool Eligible(Kingdom realm, FeudalTitleRecord title, Clan recipient, bool requireClaim = true)
        {
            var service = Titles;
            if (!CourtAgendaBehavior.ValidRealm(realm) || title?.IsActive != true || service == null
                || !CourtAgendaBehavior.Eligible(recipient, realm) || recipient == realm.RulingClan
                || recipient.Leader.IsDead || realm.Leader.IsDead || title.TitleType <= FeudalTitleType.Barony) return false;
            var sovereign = service.GetRealmSovereignTitle(realm, FeudalHierarchyMode.DeFacto) ?? service.GetKingdomPoliticalTitle(realm);
            if (sovereign == null || title.TitleType >= sovereign.TitleType || service.IsRealmSovereignTitle(realm, title)) return false;
            if (title.DeJureHolderClanId != realm.RulingClan.StringId && title.DeFactoHolderClanId != realm.RulingClan.StringId) return false;
            if (requireClaim && !service.HasActiveClaim(recipient, title)) return false;
            return service.GetChildTitles(title, FeudalHierarchyMode.DeFacto).Concat(service.GetChildTitles(title, FeudalHierarchyMode.DeJure))
                .Any(t => t.IsActive && (t.DeJureHolderClanId == recipient.StringId || t.DeFactoHolderClanId == recipient.StringId));
        }
        internal static float Acceptance(Kingdom realm, FeudalTitleRecord title, Clan recipient)
        {
            int claim = title.DeJureHolderClanId == recipient.StringId ? 35
                : Titles.HasActiveClaim(recipient, title, FeudalClaimStrength.Strong) ? 25 : 10;
            var leader = realm.RulingClan.Leader;
            var faction = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.GetIdeologicalFaction(recipient);
            bool favored = faction != null && CourtAgendaBehavior.Current?.TitleFactionFavored(realm, faction.Type) == true;
            bool legal = title.DeJureHolderClanId == realm.RulingClan.StringId;
            bool practical = title.DeFactoHolderClanId == realm.RulingClan.StringId;
            return CourtTitleGrantRules.Score((int)title.TitleType, claim, leader.GetRelation(recipient.Leader),
                leader.GetTraitLevel(DefaultTraits.Generosity), leader.GetTraitLevel(DefaultTraits.Honor), favored, practical, legal && practical);
        }
        internal static IReadOnlyList<CourtObjectiveCandidate> Discover(CourtTermContext c)
        {
            var result = new List<CourtObjectiveCandidate>();
            if (!CourtAgendaBehavior.ValidRealm(c.Realm) || Titles == null) return result;
            var titles = Titles.GetTitlesHeldByClan(c.Realm.RulingClan).Concat(Titles.GetTitlesHeldByClan(c.Realm.RulingClan, false))
                .GroupBy(t => t.TitleId).Select(g => g.First()).Where(t => t.TitleType > FeudalTitleType.Barony).OrderBy(t => t.TitleId);
            foreach (var title in titles)
                foreach (var recipient in c.Voters.Where(v => v != c.Realm.RulingClan).OrderBy(v => v.StringId))
                    if (Eligible(c.Realm, title, recipient)) result.Add(new CourtObjectiveCandidate(title.TitleId, "grant", recipient.StringId));
            return result;
        }
        private bool Owner(CourtTermContext c, CourtObjectiveOwner o) => Kind == CourtTitleGrantRules.Grant
            ? o.Faction == null && o.Sponsor == c.Realm.RulingClan
            : o.Faction?.Type == FactionType.Nobility && o.Faction.ParentKingdom == c.Realm && o.Faction.Mood > -60 && CourtAgendaBehavior.MemberCount(o.Faction) >= 2;
        public IEnumerable<CourtObjectiveCandidate> FindCandidates(CourtTermContext c, CourtObjectiveOwner o) => Owner(c, o)
            ? c.TitleGrantCandidates.Where(x => o.Faction == null || o.Faction.Members.Contains(ClanById(x.BeneficiaryId))) : Enumerable.Empty<CourtObjectiveCandidate>();
        public CourtObjectiveEvaluation EvaluateCandidate(CourtTermContext c, CourtObjectiveOwner o, CourtObjectiveCandidate candidate)
        {
            var title = Title(candidate.TargetId); var recipient = ClanById(candidate.BeneficiaryId);
            bool eligible = Owner(c, o) && c.TitleGrantCandidates.Any(x => x.TargetId == candidate.TargetId && x.BeneficiaryId == candidate.BeneficiaryId)
                && (o.Faction == null || o.Faction.Members.Contains(recipient))
                && CourtAgendaBehavior.Current?.ConflictingTitleMotion(c.Realm, candidate.TargetId, recipient, c.PlayerAgenda) != true;
            float score = eligible ? Acceptance(c.Realm, title, recipient) : 0;
            bool viable = eligible && (Kind != CourtTitleGrantRules.Grant || c.ManualSelection || score >= 60);
            return new CourtObjectiveEvaluation(eligible, viable, new CourtObjectiveWeight(.25 + score / 100), "claimed_higher_title; no_independence");
        }
        public void ApplySelection(CourtAgendaRecord a, CourtObjectiveChoice choice)
        {
            var old = a.GetObjective(); var title = Title(choice.Candidate.TargetId); var recipient = ClanById(choice.Candidate.BeneficiaryId);
            a.ObjectiveData = new CourtObjectiveRecord { Kind = Kind, TargetId = title.TitleId, ActionId = "grant" };
            if (old.HasTermSnapshot)
            {
                a.ObjectiveData.FreezeTerm(old.SelectedDay, old.DeadlineDay);
                a.SessionDate = CampaignTime.Days((float)CourtAgendaRules.EarlyObjectiveSession(old.SelectedDay, CourtAgendaRules.NominationDays(a.TermDays), CampaignTime.Now.ToDays, a.SessionDate.ToDays));
            }
            bool legal = title.DeJureHolderClanId == a.Realm.RulingClan.StringId, practical = title.DeFactoHolderClanId == a.Realm.RulingClan.StringId;
            a.TitleGrant = new CourtTitleGrantRecord { Realm = a.Realm, TitleId = title.TitleId, Recipient = recipient, Beneficiary = recipient.Leader,
                Grantor = a.Realm.RulingClan, Ruler = a.Realm.Leader, OldLegal = title.DeJureHolderClanId, OldPractical = title.DeFactoHolderClanId,
                Legal = legal, Practical = practical, Deadline = old.DeadlineDay, RelationGain = CourtTitleGrantRules.Reward((int)title.TitleType, legal && practical) };
            a.PolicyId = null;
        }
    }
}
