using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.UI.VanillaTabs.Kingdoms.Succession
{
    // Presentation only: never feed these entries back into the lawful succession line.
    internal sealed class SuccessionPretender
    {
        internal Hero Hero;
        internal FeudalClaimStrength Strength;
        internal double Power;
        internal static string Label(FeudalClaimStrength strength) => new TextObject(strength == FeudalClaimStrength.Strong
            ? "{=BC_Succession_StrongPretender}Strong pretender"
            : "{=BC_Succession_WeakPretender}Weak pretender").ToString();

        internal static List<SuccessionPretender> Order(IEnumerable<SuccessionPretender> candidates, IEnumerable<Hero> heirs, Hero sovereign)
        {
            var excluded = new HashSet<Hero>(heirs) { sovereign };
            return candidates.Where(p => p.Hero != null && !excluded.Contains(p.Hero))
                .GroupBy(p => p.Hero).Select(g => g.OrderByDescending(p => p.Strength).First())
                .OrderByDescending(p => p.Strength).ThenByDescending(p => p.Power)
                .ThenBy(p => p.Hero.StringId, StringComparer.Ordinal).ToList();
        }

        internal static List<SuccessionPretender> Get(Kingdom realm, IEnumerable<Hero> heirs)
        {
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            var crown = titles?.GetRealmSovereignTitle(realm);
            if (crown == null) return new List<SuccessionPretender>();
            Hero sovereign = RegencyBehavior.Instance?.GetLegalClanHead(realm.RulingClan) ?? realm.Leader;
            var candidates = new List<SuccessionPretender>();
            var power = new Dictionary<Clan, double>();
            foreach (var group in titles.GetActiveClaimsByTitle(crown)
                .Where(c => !string.IsNullOrEmpty(c.CarrierHeroId)).GroupBy(c => c.CarrierHeroId))
            {
                Hero carrier = CrownAccessionBehavior.ResolveAbdicationHero(group.Key);
                if (carrier?.IsAlive != true || carrier == realm.Leader || carrier.IsWanderer || carrier.IsNotable
                    || !HereditaryRealmSuccession.CanConsiderClan(carrier.Clan, realm)) continue;
                var claim = group.Where(c => c.ClaimantClanId == carrier.Clan.StringId)
                    .OrderByDescending(c => c.Strength).FirstOrDefault();
                if (claim == null) continue;
                if (!power.TryGetValue(carrier.Clan, out double weight))
                    power[carrier.Clan] = weight = RebellionPowerHelper.CalculateClanPower(carrier.Clan);
                candidates.Add(new SuccessionPretender { Hero = carrier, Strength = claim.Strength, Power = weight });
            }
            return Order(candidates, heirs, sovereign);
        }
    }
}
