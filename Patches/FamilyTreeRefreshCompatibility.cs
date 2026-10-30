using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;

namespace BellumCivile.Patches
{
    internal static class FamilyTreeRefreshCompatibility
    {
        private const string PrefixType = "Bannerlord.FamilyTree.Patches.EncyclopediaHeroPageVMPatch3";
        private static readonly HashSet<MethodInfo> Reported = new HashSet<MethodInfo>();

        internal static void TryApply(Harmony harmony)
        {
            if (harmony == null) return;
            var refresh = AccessTools.DeclaredMethod(typeof(EncyclopediaHeroPageVM), nameof(EncyclopediaHeroPageVM.RefreshValues));
            var prefixes = Harmony.GetPatchInfo(refresh)?.Prefixes
                .Where(p => p.PatchMethod.DeclaringType?.FullName == PrefixType && p.PatchMethod.Name == "Prefix").ToArray();
            if (prefixes == null || prefixes.Length == 0) return;
            foreach (var patch in prefixes)
            {
                var prefix = patch.PatchMethod;
                try
                {
                    if (!IsKnownPrefix(prefix) || HasOwnPatches(prefix))
                    {
                        if (Reported.Add(prefix)) BellumCivileLogger.Log(
                            "[ENC-FAMILY] Family Tree refresh adapter skipped: an unrecognized or already modified prefix was left intact.");
                        continue;
                    }
                    // Remove this exact registration only, preserving all other owners and patch kinds.
                    harmony.Unpatch(refresh, prefix);
                    BellumCivileLogger.Log("[ENC-FAMILY] Enabled original Family Tree 1.4.8 refresh compatibility; "
                        + "removed only its alternating RefreshValues prefix. Tree, relationship and constructor patches remain active.");
                }
                catch (Exception ex)
                {
                    if (Reported.Add(prefix)) BellumCivileLogger.Log(
                        "[ENC-FAMILY] Family Tree refresh adapter unavailable: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private static bool HasOwnPatches(MethodInfo prefix)
        {
            var patches = Harmony.GetPatchInfo(prefix);
            return patches != null && (patches.Prefixes.Count > 0 || patches.Postfixes.Count > 0
                || patches.Transpilers.Count > 0 || patches.Finalizers.Count > 0);
        }

        private static bool IsKnownPrefix(MethodInfo prefix)
        {
            var assembly = prefix.DeclaringType?.Assembly.GetName();
            var parameters = prefix.GetParameters();
            if (assembly?.Name != "Bannerlord.FamilyTree.1.4.8" || assembly.Version != new Version(1, 4, 8, 0)
                || prefix.DeclaringType.FullName != PrefixType || prefix.Name != "Prefix" || !prefix.IsStatic
                || prefix.ReturnType != typeof(bool) || parameters.Length != 1
                || parameters[0].ParameterType != typeof(EncyclopediaPageVM)) return false;
            var toggle = AccessTools.DeclaredField(prefix.DeclaringType, "toggle");
            if (toggle?.FieldType != typeof(bool) || !toggle.IsStatic || toggle.IsInitOnly || toggle.IsLiteral) return false;
            return MatchesToggleBody(toggle, PatchProcessor.GetOriginalInstructions(prefix));
        }

        private static bool MatchesToggleBody(FieldInfo toggle, IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.Where(i => i.opcode != OpCodes.Nop).ToArray();
            return code.Length == 8
                && code[0].opcode == OpCodes.Ldsfld && Equals(code[0].operand, toggle)
                && code[1].LoadsConstant(0) && code[2].opcode == OpCodes.Ceq
                && code[3].opcode == OpCodes.Stsfld && Equals(code[3].operand, toggle)
                && code[4].opcode == OpCodes.Ldsfld && Equals(code[4].operand, toggle)
                && code[5].LoadsConstant(0) && code[6].opcode == OpCodes.Ceq && code[7].opcode == OpCodes.Ret;
        }
    }
}
