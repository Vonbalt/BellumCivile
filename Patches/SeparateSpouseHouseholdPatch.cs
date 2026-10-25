using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Helpers;
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
        // Native birth initialization depth, so the equipment guard below stays scoped to childbirth.
        internal static int BirthDepth;

        [HarmonyPrefix]
        public static void Prefix() => BirthDepth++;

        [HarmonyFinalizer]
        public static void Finalizer() => BirthDepth--;

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

    // Custom cultures can lack the child equipment templates native birth initialization
    // copies from, so the newborn keeps its template equipment instead of crashing the daily tick.
    [HarmonyPatch(typeof(EquipmentHelper), nameof(EquipmentHelper.AssignHeroEquipmentFromEquipment))]
    public static class BirthEquipmentFallbackPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Hero hero, Equipment equipment)
        {
            if (SeparateSpouseBirthClanPatch.BirthDepth <= 0 || hero != null && equipment != null) return true;
            BellumCivileLogger.Log($"Skipped missing newborn equipment template; hero={hero?.StringId}; "
                + $"culture={hero?.Culture?.StringId}; clan={hero?.Clan?.StringId}; equipment_missing={equipment == null}.");
            return false;
        }

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, Hero hero)
        {
            if (!(__exception is NullReferenceException) || SeparateSpouseBirthClanPatch.BirthDepth <= 0) return __exception;
            BellumCivileLogger.Log($"Recovered incomplete newborn equipment template; hero={hero?.StringId}; "
                + $"culture={hero?.Culture?.StringId}; clan={hero?.Clan?.StringId}; error={__exception.Message}");
            return null;
        }
    }

    [HarmonyPatch(typeof(PregnancyCampaignBehavior), "CheckAreNearby")]
    public static class ConsistentSpouseVisitPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Hero hero, Hero spouse, ref bool __result)
        {
            __result = false;
            if (hero?.IsAlive != true || spouse?.IsAlive != true) return false;
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

        private static void GetLocation(Hero hero, out Settlement settlement, out MobileParty party)
        {
            settlement = hero.CurrentSettlement;
            party = hero.PartyBelongedTo;
            if (party?.AttachedTo != null) party = party.AttachedTo;
            if (settlement == null) settlement = party?.CurrentSettlement;
        }
    }
}
