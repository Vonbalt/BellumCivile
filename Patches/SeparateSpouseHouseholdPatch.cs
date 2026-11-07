using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(HeroCreator), nameof(HeroCreator.DeliverOffSpring))]
    public static class SeparateSpouseBirthClanPatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            var clanGetter = AccessTools.PropertyGetter(typeof(Hero), nameof(Hero.Clan));
            if (codes.Count(c => c.Calls(clanGetter)) != 2)
                throw new InvalidOperationException("Birth household hook: native clan assignment anchors changed.");
            foreach (var code in codes)
            {
                if (!code.Calls(clanGetter)) { yield return code; continue; }
                var mother = new CodeInstruction(OpCodes.Ldarg_0);
                mother.labels.AddRange(code.labels);
                mother.blocks.AddRange(code.blocks);
                yield return mother;
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                yield return CodeInstruction.Call(typeof(SeparateSpouseBirthClanPatch), nameof(ResolveBirthClan));
            }
        }

        // Replace only clan selection, before native initialization publishes the child.
        // Parentage, culture, pregnancy and birth notifications remain native.
        public static Clan ResolveBirthClan(Hero nativeParent, Hero mother, Hero father) =>
            mother?.Clan != null && father?.Clan != null && mother.Clan != father.Clan
                ? mother.Clan : nativeParent?.Clan;
    }

    [HarmonyPatch(typeof(PregnancyCampaignBehavior), "CheckAreNearby")]
    public static class ConsistentSpouseVisitPatch
    {
        private static readonly ConditionalWeakTable<Campaign, HashSet<Hero>> ReportedClanlessVisits
            = new ConditionalWeakTable<Campaign, HashSet<Hero>>();

        [HarmonyPrefix]
        public static bool Prefix(Hero hero, Hero spouse, ref bool __result)
        {
            __result = false;
            if (hero?.IsAlive != true || spouse?.IsAlive != true) return false;
            // The native pregnancy model dereferences this woman's clan before calculating her chance.
            if (hero.Clan == null)
            {
                ReportClanlessVisit(hero, spouse);
                return false;
            }
            bool captive = hero.IsPrisoner || spouse.IsPrisoner;
            bool hostile = hero.MapFaction != null && spouse.MapFaction != null
                && hero.MapFaction.IsAtWarWith(spouse.MapFaction);
            GetLocation(hero, out Settlement home, out MobileParty party);
            GetLocation(spouse, out Settlement spouseHome, out MobileParty spouseParty);
            bool together = home != null && home == spouseHome || party != null && party == spouseParty;
            float roll = !captive && !hostile && !together ? MBRandom.RandomFloat : 1f;
            __result = IsVisitEligible(captive, hostile, together, roll);
            return false;
        }

        internal static bool IsVisitEligible(bool captive, bool hostile, bool together, float roll) =>
            !captive && !hostile && (together || roll < 0.2f);

        private static void ReportClanlessVisit(Hero hero, Hero spouse)
        {
            var campaign = Campaign.Current;
            if (campaign != null && !ReportedClanlessVisits.GetValue(campaign, _ => new HashSet<Hero>()).Add(hero)) return;
            BellumCivileLogger.Log("[SPOUSE-VISIT] skipped=missing_pregnancy_clan; "
                + Describe("hero", hero) + "; " + Describe("spouse", spouse) + "; pregnancy_and_household_unchanged=true");
            BellumCivileLogger.Flush();
        }

        private static string Describe(string role, Hero hero) =>
            $"{role}={hero?.StringId ?? "null"}; {role}_clan={hero?.Clan?.StringId ?? "null"}; "
            + $"{role}_companion_of={hero?.CompanionOf?.StringId ?? "null"}; {role}_occupation={hero?.CharacterObject?.Occupation.ToString() ?? "null"}";

        private static void GetLocation(Hero hero, out Settlement settlement, out MobileParty party)
        {
            settlement = hero.CurrentSettlement;
            party = hero.PartyBelongedTo;
            if (party?.AttachedTo != null) party = party.AttachedTo;
            if (settlement == null) settlement = party?.CurrentSettlement;
        }
    }
}
