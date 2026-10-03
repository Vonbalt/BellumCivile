using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(CampaignEventDispatcher), "OnBeforeHeroKilled")]
    public static class HereditaryDeathSnapshotPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Hero victim)
        {
            CivilWarResolutionBehavior.Current?.CaptureCoalitionDeath(victim);
            try { Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>()?.CaptureCrossClanEstate(victim); }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Cross-clan estate snapshot failed; victim={victim?.StringId}; error={ex}");
            }
            try { CrownAccessionBehavior.Instance?.Capture(victim); }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Crown death snapshot failed; native death retains ownership; victim={victim?.StringId}; error={ex}");
            }
        }
    }

    [HarmonyPatch(typeof(CampaignEventDispatcher), "OnBeforeMainCharacterDied")]
    public static class HereditaryPlayerDeathSnapshotPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Hero victim) => HereditaryDeathSnapshotPatch.Prefix(victim);
    }

    [HarmonyPatch(typeof(KillCharacterAction), "ApplyInternal")]
    public static class HereditaryDeathAccessionPatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            var rulingClan = AccessTools.PropertyGetter(typeof(Kingdom), nameof(Kingdom.RulingClan));
            var makeDead = AccessTools.Method(typeof(KillCharacterAction), "MakeDead");
            if (codes.Count(x => x.Calls(rulingClan)) != 1 || codes.Count(x => x.Calls(makeDead)) != 1)
                throw new InvalidOperationException("Hereditary death hook: native Crown/death anchors changed.");
            foreach (var code in codes)
            {
                if (code.Calls(rulingClan))
                {
                    var victim = new CodeInstruction(OpCodes.Ldarg_0);
                    victim.labels.AddRange(code.labels);
                    victim.blocks.AddRange(code.blocks);
                    yield return victim;
                    yield return CodeInstruction.Call(typeof(HereditaryDeathAccessionPatch), nameof(NativeDeathRulingClan));
                }
                else
                {
                    yield return code;
                    if (code.Calls(makeDead))
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return CodeInstruction.Call(typeof(HereditaryDeathAccessionPatch), nameof(AfterDeath));
                    }
                }
            }
        }

        public static Clan NativeDeathRulingClan(Kingdom realm, Hero victim) =>
            CrownAccessionBehavior.Instance?.OwnsDeath(realm, victim) == true ? null : realm.RulingClan;

        public static void AfterDeath(Hero victim) => CrownAccessionBehavior.Instance?.AfterDeath(victim);
    }

    [HarmonyPatch(typeof(DestroyClanAction), nameof(DestroyClanAction.ApplyByClanLeaderDeath))]
    public static class HereditaryInterregnumHousePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Clan destroyedClan) =>
            Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>()?.KeepCrossClanEstateHouse(destroyedClan) != true
            && CrownAccessionBehavior.Instance?.KeepInterregnumHouse(destroyedClan) != true;
    }
}
