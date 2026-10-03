using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BellumCivile
{
    internal sealed class TreatyHostageCandidate
    {
        internal Hero Hero { get; }
        internal int Tier { get; }
        internal int Cost => HostagePactRules.GetTreatyCost(Tier);
        internal TreatyHostageCandidate(Hero hero, int tier) { Hero = hero; Tier = tier; }
    }

    internal static class TreatyHostageEligibility
    {
        internal static IReadOnlyList<TreatyHostageCandidate> GetCandidates(Kingdom realm)
        {
            if (realm == null || realm.IsEliminated || realm.RulingClan == null
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm)
                || CivilWarConflictBehavior.IsRealmTransferPending(realm))
                return new List<TreatyHostageCandidate>();
            Hero ruler = RegencyBehavior.Instance?.GetLegalClanHead(realm.RulingClan) ?? realm.Leader;
            if (ruler == null || !ruler.IsAlive) return new List<TreatyHostageCandidate>();
            var laws = SuccessionLawHelper.GetLawsForKingdom(realm);
            var system = SuccessionRealmRules.Classify(laws.SuccessionLaw);
            if (system == RealmSuccessionSystem.Unknown) return new List<TreatyHostageCandidate>();
            List<Hero> line = system == RealmSuccessionSystem.Hereditary
                ? HereditaryRealmSuccession.GetLine(realm, ruler, laws)
                : SuccessionLawHelper.GetLegalSuccessionLine(realm.RulingClan, ruler,
                    SuccessionLawHelper.GetLawsForClan(realm.RulingClan));
            var custody = Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>();
            // Rank against the full legal line, not the eligible subset: an unavailable
            // first heir does not promote the second heir's bargaining value.
            return realm.RulingClan.Heroes.Where(hero => IsAvailableRelative(hero, ruler, realm.RulingClan)
                    && custody?.IsReserved(hero) != true)
                .Select(hero => new TreatyHostageCandidate(hero, TierInLine(line, hero)))
                .OrderBy(candidate => candidate.Tier)
                .ThenBy(candidate => candidate.Hero.StringId, StringComparer.Ordinal).ToList();
        }

        internal static int TierInLine(IList<Hero> line, Hero hero)
            => HostagePactRules.GetTier(hero == null || line == null ? 0 : line.IndexOf(hero) + 1);

        internal static bool IsAvailableRelative(Hero hero, Hero ruler, Clan house)
            => hero != null && ruler != null && house != null && hero != ruler && hero != house.Leader
                && hero.Clan == house && hero.IsAlive && !hero.IsDisabled && !hero.IsPrisoner
                && hero.DeathMark == KillCharacterAction.KillCharacterActionDetail.None
                && (hero.IsActive || (hero.IsChild && hero.IsNotSpawned)) && !hero.IsTraveling
                && !hero.IsWanderer && !hero.IsNotable
                && RegencyBehavior.Instance?.IsGeneratedRegent(hero) != true
                && hero.PartyBelongedTo?.MapEvent == null && hero.PartyBelongedTo?.SiegeEvent == null
                && HereditaryRealmSuccession.IsBloodRelative(hero, ruler);
    }
}
