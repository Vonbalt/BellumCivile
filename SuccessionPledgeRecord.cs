using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public enum SuccessionPledgeChoice { Automatic, AwaitingPlayer, Loyal, Rebel }

    public sealed class SuccessionPledgeRecord
    {
        [SaveableField(1)] public Clan Clan;
        [SaveableField(2)] public Hero Speaker;
        [SaveableField(3)] public double Power;
        [SaveableField(4)] public float PersonalChance;
        [SaveableField(5)] public float Roll;
        [SaveableField(6)] public SuccessionPledgeChoice Choice;
        [SaveableField(7)] public Dictionary<Clan, float> LiegeChances = new Dictionary<Clan, float>();
    }

    internal static class SuccessionPledgeRules
    {
        internal static HashSet<Clan> Resolve(IReadOnlyList<SuccessionPledgeRecord> pledges)
        {
            if (pledges == null || pledges.Any(p => p == null || p.Clan == null || p.Choice == SuccessionPledgeChoice.AwaitingPlayer)
                || pledges.Select(p => p.Clan).Distinct().Count() != pledges.Count) return null;
            var backers = new HashSet<Clan>(pledges.Where(p => p.Choice == SuccessionPledgeChoice.Rebel).Select(p => p.Clan));
            // Frozen scores and one roll per house. A successful liege unlocks its vassal call;
            // cycles cannot recruit themselves, and explicit player choices are never overridden.
            bool changed;
            do
            {
                var additions = pledges.Where(p => p.Choice == SuccessionPledgeChoice.Automatic && !backers.Contains(p.Clan)
                    && Passes(p.Roll, Math.Max(p.PersonalChance,
                        p.LiegeChances.Where(e => backers.Contains(e.Key)).Select(e => e.Value).DefaultIfEmpty(0).Max())))
                    .Select(p => p.Clan).ToList();
                changed = additions.Count > 0;
                backers.UnionWith(additions);
            } while (changed);
            return backers;
        }

        internal static bool Passes(float roll, float chance) => !float.IsNaN(roll) && roll >= 0 && roll < 1
            && !float.IsNaN(chance) && roll < Math.Max(0, Math.Min(1, chance));
    }
}
