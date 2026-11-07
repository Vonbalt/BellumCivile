using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class HouseholdMembership
    {
        private sealed class Entry
        {
            internal Hero Leader, Root;
            internal int Count;
            internal readonly HashSet<Hero> BloodFamily = new HashSet<Hero>();
        }

        private static readonly Dictionary<Clan, Entry> Cache = new Dictionary<Clan, Entry>();

        internal static bool Supports(Clan clan) => clan != null && !clan.IsEliminated
            && clan.Leader?.IsAlive == true && !clan.IsBanditFaction && (clan == Clan.PlayerClan || !clan.IsMinorFaction);

        internal static IEnumerable<Hero> Members(Clan clan) => clan == null
            ? Enumerable.Empty<Hero>()
            : (clan.Heroes ?? Enumerable.Empty<Hero>()).Concat(clan.Companions ?? Enumerable.Empty<Hero>())
                .Where(h => h != null).Distinct();

        internal static void Invalidate(Clan clan = null)
        {
            if (clan == null) Cache.Clear();
            else Cache.Remove(clan);
        }

        internal static bool IsFamily(Hero hero)
        {
            Clan clan = hero?.Clan;
            if (clan == null) return false;
            Hero root = RegencyBehavior.Instance?.GetLegalClanHead(clan) ?? clan.Leader;
            int count = (clan.Heroes?.Count ?? 0) + (clan.Companions?.Count ?? 0);
            if (!Cache.TryGetValue(clan, out var entry) || entry.Leader != clan.Leader
                || entry.Root != root || entry.Count != count)
            {
                entry = new Entry { Leader = clan.Leader, Root = root, Count = count };
                var members = Members(clan).ToList();
                var ancestors = Ancestors(root);
                foreach (Hero member in members)
                {
                    var kin = Ancestors(member);
                    if (member == root || ancestors.Contains(member) || kin.Contains(root) || kin.Overlaps(ancestors))
                        entry.BloodFamily.Add(member);
                }
                Cache[clan] = entry;
            }
            if (entry.BloodFamily.Contains(hero)) return true;
            // Marriage mods can change one side's link without changing the clan roster or raising native events.
            foreach (Hero relative in entry.BloodFamily)
            {
                if (relative.Clan != clan) continue;
                if (relative.Spouse == hero || hero.Spouse == relative) return true;
                // Preserve widowed family members, but not former spouses of living blood relatives.
                if (relative.IsDead && (relative.ExSpouses?.Contains(hero) == true
                    || hero.ExSpouses?.Contains(relative) == true)) return true;
            }
            return false;
        }

        internal static bool IsBloodRelative(Hero hero, Hero relative)
        {
            if (hero == null || relative == null) return false;
            if (hero == relative) return true;
            var ancestors = Ancestors(relative);
            var kin = Ancestors(hero);
            return ancestors.Contains(hero) || kin.Contains(relative) || kin.Overlaps(ancestors);
        }

        private static HashSet<Hero> Ancestors(Hero hero)
        {
            var result = new HashSet<Hero>();
            var pending = new Stack<Hero>();
            if (hero != null) pending.Push(hero);
            while (pending.Count > 0)
            {
                Hero current = pending.Pop();
                if (current.Father != null && result.Add(current.Father)) pending.Push(current.Father);
                if (current.Mother != null && result.Add(current.Mother)) pending.Push(current.Mother);
            }
            result.Remove(hero);
            return result;
        }

        internal static bool IsRetainer(Hero hero) => hero != null && Supports(hero.Clan)
            && hero != hero.Clan.Leader && !hero.IsNotable && !IsFamily(hero);
    }
}
