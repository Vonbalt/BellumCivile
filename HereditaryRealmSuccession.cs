using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    // The Crown follows people and genealogy, not the membership of its current clan.
    internal static class HereditaryRealmSuccession
    {
        internal static List<Hero> GetLine(Kingdom realm, Hero sovereign = null, SuccessionLawSet? laws = null)
        {
            if (realm == null) return new List<Hero>();
            sovereign = sovereign ?? RegencyBehavior.Instance?.GetLegalClanHead(realm.RulingClan)
                ?? realm.RulingClan?.Leader;
            var candidates = Clan.All.Where(clan => clan != null && !clan.IsEliminated
                    && !clan.IsBanditFaction && !clan.IsUnderMercenaryService
                    && !NobleClanEligibilityHelper.IsNonPlayerMinorClan(clan)
                    && CanConsiderClan(clan, realm))
                .SelectMany(clan => clan.Heroes)
                .Where(hero => !hero.IsWanderer && !hero.IsNotable);
            return OrderLine(candidates, sovereign, laws ?? SuccessionLawHelper.GetLawsForKingdom(realm),
                CrownAccessionBehavior.Instance?.GetAbdicatedMonarchs(realm));
        }

        internal static List<Hero> OrderLine(IEnumerable<Hero> candidates, Hero sovereign,
            SuccessionLawSet laws, IEnumerable<Hero> excluded = null)
        {
            if (sovereign == null || SuccessionRealmRules.Classify(laws.SuccessionLaw) != RealmSuccessionSystem.Hereditary)
                return new List<Hero>();
            var barred = new HashSet<Hero>(excluded ?? Enumerable.Empty<Hero>());
            var ancestors = GetAncestors(sovereign);
            var relatives = candidates.Where(hero => hero != null && hero != sovereign && !barred.Contains(hero)
                && GetAncestors(hero).Overlaps(ancestors));
            if (laws.SuccessionLaw == HouseSuccessionLaw.Seniority || laws.SuccessionLaw == HouseSuccessionLaw.Kinship)
            {
                var eligible = relatives.Where(hero => SuccessionLawHelper.IsBasicSuccessionCandidate(hero, sovereign, true)
                    && SuccessionLawHelper.IsEligibleUnderGenderLaw(hero, laws.GenderLaw)).Distinct();
                if (laws.SuccessionLaw == HouseSuccessionLaw.Seniority)
                    return eligible.OrderByDescending(hero => SuccessionLawHelper.GetGenderPreferenceRank(hero, laws.GenderLaw))
                        .ThenByDescending(hero => hero.Age)
                        .ThenBy(hero => hero.StringId ?? string.Empty, StringComparer.Ordinal).ToList();
                var kinship = new RealmSuccessionKinship();
                return eligible.Select(hero => new { Hero = hero, Kinship = kinship.Get(hero, sovereign) })
                    .Where(candidate => candidate.Kinship > 0)
                    .OrderByDescending(candidate => RealmSuccessionMerit.Rank(candidate.Hero, laws, candidate.Kinship))
                    .ThenBy(candidate => candidate.Hero.StringId ?? string.Empty, StringComparer.Ordinal)
                    .Select(candidate => candidate.Hero).ToList();
            }
            return SuccessionLawHelper.OrderSuccessionCandidates(relatives, sovereign, laws,
                includeUnderage: true, applyInheritanceAdvances: false);
        }

        internal static bool CanConsiderClan(Clan clan, Kingdom realm) => clan != null && realm != null
            && !clan.IsEliminated && !clan.IsBanditFaction && !clan.IsUnderMercenaryService
            && !NobleClanEligibilityHelper.IsNonPlayerMinorClan(clan)
            && (DynasticHeirBehavior.IsClanAttachedToSuccessionRealm(clan, realm)
                || clan.Kingdom != null && !clan.Kingdom.IsEliminated
                    && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(clan.Kingdom));

        private static HashSet<Hero> GetAncestors(Hero hero)
        {
            var result = new HashSet<Hero>();
            var pending = new Stack<Hero>();
            if (hero != null) pending.Push(hero);
            // Parent links only: spouses and clan membership are not blood ties.
            // A visited set tolerates malformed ancestry cycles.
            while (pending.Count > 0)
            {
                Hero current = pending.Pop();
                if (!result.Add(current)) continue;
                if (current.Father != null) pending.Push(current.Father);
                if (current.Mother != null) pending.Push(current.Mother);
            }
            return result;
        }

        internal static bool IsBloodRelative(Hero hero, Hero sovereign) => hero != null && sovereign != null
            && hero != sovereign && GetAncestors(hero).Overlaps(GetAncestors(sovereign));
    }
}
