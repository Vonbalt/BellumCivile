using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    // Short-lived snapshot: one heir/household lookup per realm/clan, never one per candidate pair.
    internal sealed class MarriageHouseholdPolicy
    {
        private HashSet<Hero> _crownHeirs;
        private readonly Dictionary<Clan, Hero> _clanHeirs = new Dictionary<Clan, Hero>();
        private readonly Dictionary<Clan, HashSet<Hero>> _continuation = new Dictionary<Clan, HashSet<Hero>>();

        internal HashSet<Hero> CrownHeirs
        {
            get
            {
                if (_crownHeirs == null)
                {
                    _crownHeirs = new HashSet<Hero>();
                    foreach (Kingdom realm in Kingdom.All)
                    {
                        if (realm == null || realm.IsEliminated || !CrownAccessionBehavior.IsHereditaryRealm(realm)
                            || BellumMarriageStrategyHelper.MarriagePoliticalRealm(realm.RulingClan) != realm) continue;
                        Hero heir = HereditaryRealmSuccession.GetLine(realm).FirstOrDefault();
                        if (heir != null) _crownHeirs.Add(heir);
                    }
                }
                return _crownHeirs;
            }
        }

        internal bool MustRemain(Hero hero)
        {
            Clan clan = hero?.Clan;
            if (clan == null) return false;
            if (hero == clan.Leader || hero == LegalHead(clan) || CrownHeirs.Contains(hero)) return true;
            if (!_clanHeirs.TryGetValue(clan, out Hero heir))
                _clanHeirs[clan] = heir = SuccessionLawHelper.GetLegalSuccessionLine(clan).FirstOrDefault();
            return hero == heir;
        }

        internal static Hero LegalHead(Clan clan) => RegencyBehavior.Instance?.GetLegalClanHead(clan) ?? clan?.Leader;

        internal bool IsContinuation(Hero hero)
        {
            if (hero?.Clan == null) return false;
            IsLastContinuation(hero);
            return _continuation[hero.Clan].Contains(hero);
        }

        internal bool TryChoosePlayerOffer(Hero player, Hero other, Clan ordinary, out Clan destination)
        {
            destination = MustRemain(other) || IsLastContinuation(other) ? other.Clan : ordinary;
            return PlayerMarriageAgreement.CanChoose(player, other, destination, false, this, out _);
        }

        internal bool IsLastContinuation(Hero departing)
        {
            Clan clan = departing?.Clan;
            if (clan == null) return false;
            if (!_continuation.TryGetValue(clan, out var remaining))
            {
                remaining = new HashSet<Hero>();
                Hero head = LegalHead(clan);
                foreach (Hero hero in clan.Heroes)
                {
                    if (hero == null || !hero.IsAlive || hero.IsDisabled || !hero.IsLord
                        || RegencyBehavior.Instance?.IsGeneratedRegent(hero) == true
                        || hero != head && !SuccessionLawHelper.IsBloodRelative(hero, head)) continue;
                    bool young = hero.IsChild;
                    bool unmarried = hero.Spouse == null && (!hero.IsFemale
                        || hero.Age <= BellumCivileConstants.MarriageFemaleMaximumAge);
                    Hero spouse = hero.Spouse;
                    Hero mother = hero.IsFemale ? hero : spouse;
                    bool couple = spouse?.IsAlive == true && spouse.Clan == clan && mother?.IsFemale == true
                        && mother.Age >= SuccessionLawHelper.GetAgeOfMajority() && mother.Age <= 45f;
                    // Captivity, armies and temporary recovery do not erase a family's future.
                    if (young || unmarried || couple || hero.IsPregnant) remaining.Add(hero);
                }
                _continuation[clan] = remaining;
            }
            return remaining.Contains(departing) && remaining.Count == 1;
        }

        internal bool TryChoose(Hero first, Hero second, Clan ordinary, out Clan destination)
        {
            destination = ordinary;
            if (first?.Clan == null || second?.Clan == null || ordinary == null || ordinary.IsEliminated) return false;
            if (TreatyMarriageClanContext.TryResolve(first, second, out Clan treaty))
            { destination = treaty; return true; }
            if (!BellumCivileOptions.EnableBellumStrategicMarriageLogic) return true;
            if (first.Clan == Clan.PlayerClan || second.Clan == Clan.PlayerClan)
                return TryChoosePlayerOffer(first.Clan == Clan.PlayerClan ? first : second,
                    first.Clan == Clan.PlayerClan ? second : first, ordinary, out destination);
            if (first.IsFemale == second.IsFemale || first.Clan == second.Clan) return false;
            if (ordinary != first.Clan && ordinary != second.Clan) return false;
            Hero bride = first.IsFemale ? first : second;
            if (MustRemain(bride)) destination = bride.Clan;
            Hero departing = destination == first.Clan ? second : first;
            // Protect either sex: changing the receiving house must not sacrifice another heir.
            return !MustRemain(departing) && !IsLastContinuation(departing);
        }

        internal static Clan Resolve(Hero first, Hero second)
        {
            if (first?.Clan == null || second?.Clan == null) return null;
            Clan ordinary = Campaign.Current?.Models?.MarriageModel?.GetClanAfterMarriage(first, second);
            return new MarriageHouseholdPolicy().TryChoose(first, second, ordinary, out Clan destination) ? destination : null;
        }

        internal static bool Matches(Hero first, Hero second, Clan destination)
        {
            if (destination == null || first?.Spouse != null || second?.Spouse != null
                || Resolve(first, second) != destination) return false;
            // Another mod's replacement model must honor the outcome before we mutate spouses/clans.
            using (new NpcMarriageClanContext(first, second, destination))
                return Campaign.Current.Models.MarriageModel.GetClanAfterMarriage(first, second) == destination;
        }
    }
}
