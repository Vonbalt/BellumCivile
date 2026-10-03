using System;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(CharacterRelationCampaignBehavior), "OnHeroKilled")]
    internal static class NativeExecutionRelationContextPatch
    {
        private static void Prefix(out IDisposable __state) => __state = DynamicRelationBehavior.Instance?.BeginNativeExecutionRelations();
        private static void Postfix(IDisposable __state) => __state?.Dispose();
        private static Exception Finalizer(Exception __exception, IDisposable __state)
        { __state?.Dispose(); return __exception; }
    }

    [HarmonyPatch(typeof(KillCharacterAction), "ApplyInternal")]
    internal static class DeathRelationContextPatch
    {
        private static void Prefix(out IDisposable __state) => __state = DynamicRelationBehavior.Instance?.PreserveDeathContext();
        private static Exception Finalizer(Exception __exception, IDisposable __state)
        { __state?.Dispose(); return __exception; }
    }

    [HarmonyPatch(typeof(CampaignEventDispatcher), "OnHeroKilled")]
    internal static class DeathRelationCompletionPatch
    {
        // Resolve after all subscribers, including native execution reactions, regardless of registration order.
        private static void Postfix(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail)
            => DynamicRelationBehavior.Instance?.CompleteDeathMemories(victim, killer, detail);
    }
}
