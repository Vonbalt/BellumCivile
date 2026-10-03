using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;

namespace BellumCivile
{
    internal static class HostageCustodyGuard
    {
        [ThreadStatic] private static Dictionary<Hero, int> _authorized;
        internal static bool IsProtected(Hero hero)
            => hero != null && Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>()?.IsProtectedHostage(hero) == true;
        internal static bool BlocksOrdinaryAction(Hero hero)
            => IsProtected(hero) && (_authorized == null || !_authorized.ContainsKey(hero));

        // Authorization is hero-scoped and exception-safe, never a global bypass.
        internal static IDisposable Authorize(Hero hero)
        {
            if (hero == null) throw new ArgumentNullException(nameof(hero));
            if (_authorized == null) _authorized = new Dictionary<Hero, int>();
            _authorized.TryGetValue(hero, out int depth);
            _authorized[hero] = depth + 1;
            return new Scope(hero);
        }
        private sealed class Scope : IDisposable
        {
            private Hero _hero;
            internal Scope(Hero hero) { _hero = hero; }
            public void Dispose()
            {
                if (_hero == null) return;
                if (--_authorized[_hero] == 0) _authorized.Remove(_hero);
                _hero = null;
            }
        }
        internal static TroopRoster WithoutHostages(TroopRoster roster)
        {
            if (roster == null || !roster.GetTroopRoster().Any(e => IsProtected(e.Character?.HeroObject))) return roster;
            var filtered = TroopRoster.CreateDummyTroopRoster();
            foreach (var entry in roster.GetTroopRoster())
                if (!IsProtected(entry.Character?.HeroObject)) filtered.Add(entry);
            return filtered;
        }
        internal static bool PreservesCustody(TroopRoster original, TroopRoster proposed)
        {
            var before = original == null ? Enumerable.Empty<TroopRosterElement>() : original.GetTroopRoster();
            var after = proposed == null ? Enumerable.Empty<TroopRosterElement>() : proposed.GetTroopRoster();
            var heroes = before.Concat(after)
                .Select(e => e.Character).Where(c => IsProtected(c?.HeroObject)).Distinct();
            return heroes.All(c => (original?.GetTroopCount(c) ?? 0) == (proposed?.GetTroopCount(c) ?? 0));
        }
    }
}
