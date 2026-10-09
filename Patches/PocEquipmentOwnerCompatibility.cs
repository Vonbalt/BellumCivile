using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.MountAndBlade;

namespace BellumCivile.Patches
{
    internal static class PocEquipmentOwnerCompatibility
    {
        internal static void TryApply(Harmony harmony, Assembly poc)
        {
            MethodInfo transpiler = AccessTools.Method(typeof(PocEquipmentOwnerCompatibility), nameof(Transpiler));
            var patched = new List<MethodInfo>();
            try
            {
                MethodInfo[] targets =
                {
                    AccessTools.DeclaredMethod(poc.GetType("PocColor.PocColorModSetColors+PocColorModAgentEquipItemsFromSpawnEquipment"), "Prefix"),
                    AccessTools.DeclaredMethod(poc.GetType("PocColor.PocColorModSetColors+PocColorModAgentEquipNewEntity"), "Postfix")
                };
                if (targets.Any(m => m == null || m.GetParameters().FirstOrDefault()?.ParameterType != typeof(Agent).MakeByRefType()
                    || PatchProcessor.GetOriginalInstructions(m).Count(IsBannerCacheLookup) != 1))
                    throw new InvalidOperationException("Unsupported POC equipment ownership lookup.");
                foreach (MethodInfo method in targets)
                {
                    harmony.Patch(method, transpiler: new HarmonyMethod(transpiler));
                    patched.Add(method);
                }
                BellumCivileLogger.Log("Enabled POC equipment-owner lookup for identical parent/cadet banners.");
            }
            catch (Exception ex)
            {
                foreach (MethodInfo method in patched) harmony.Unpatch(method, transpiler);
                BellumCivileLogger.Log($"POC equipment-owner adapter unavailable: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static bool IsBannerCacheLookup(CodeInstruction instruction)
        {
            if (!(instruction.operand is MethodInfo method) || method.Name != "get_Item") return false;
            Type type = method.DeclaringType;
            return type?.IsGenericType == true && type.GetGenericTypeDefinition().FullName == "PocColor.Config.Map`2"
                && type.GetGenericArguments().SequenceEqual(new[] { typeof(string), typeof(string) });
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            if (code.Count(IsBannerCacheLookup) != 1)
                throw new InvalidOperationException("POC equipment lookup changed before patching.");
            foreach (CodeInstruction instruction in code)
            {
                yield return instruction;
                if (!IsBannerCacheLookup(instruction)) continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldind_Ref);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PocEquipmentOwnerCompatibility), nameof(ResolveEquipmentClan)));
            }
        }

        private static string ResolveEquipmentClan(string cachedClan, Agent agent)
        {
            PartyBase party = agent?.Origin?.BattleCombatant as PartyBase;
            Clan partyClan = party?.MobileParty?.ActualClan ?? party?.Owner?.Clan;
            Clan heroClan = (agent?.Character as CharacterObject)?.HeroObject?.Clan;
            return SelectClanName(cachedClan, partyClan, heroClan);
        }

        internal static string SelectClanName(string cachedClan, Clan partyClan, Clan heroClan)
        {
            // Banner artwork is not an identity: related houses can legitimately share every layer.
            return partyClan?.Name?.ToString() ?? heroClan?.Name?.ToString() ?? cachedClan;
        }
    }
}
