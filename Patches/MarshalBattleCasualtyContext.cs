using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;

namespace BellumCivile.Patches
{
    internal static class MarshalBattleCasualtyContext
    {
        [ThreadStatic] private static MapEvent _current;
        internal static MapEvent Current => _current;

        internal static MapEvent Enter(MapEvent battle)
        {
            MapEvent previous = _current;
            _current = battle;
            return previous;
        }

        internal static void Restore(MapEvent previous) => _current = previous;
    }

    // Native actions clear party links before dispatching their hero events.
    [HarmonyPatch(typeof(TakePrisonerAction), "ApplyInternal")]
    internal static class MarshalPrisonerBattleContextPatch
    {
        private static void Prefix(PartyBase capturerParty, Hero prisonerCharacter, out MapEvent __state) =>
            __state = MarshalBattleCasualtyContext.Enter(prisonerCharacter?.PartyBelongedTo?.MapEvent
                ?? capturerParty?.MapEvent ?? MarshalBattleCasualtyContext.Current);

        private static Exception Finalizer(Exception __exception, MapEvent __state)
        {
            MarshalBattleCasualtyContext.Restore(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(KillCharacterAction), "ApplyInternal")]
    internal static class MarshalDeathBattleContextPatch
    {
        private static void Prefix(Hero victim, out MapEvent __state) =>
            __state = MarshalBattleCasualtyContext.Enter(victim?.PartyBelongedTo?.MapEvent
                ?? MarshalBattleCasualtyContext.Current);

        private static Exception Finalizer(Exception __exception, MapEvent __state)
        {
            MarshalBattleCasualtyContext.Restore(__state);
            return __exception;
        }
    }

    // Deferred battle deaths fire after this method detaches the party from its map event.
    [HarmonyPatch(typeof(MapEventSide), "HandleMapEventEndForPartyInternal")]
    internal static class MarshalDeferredDeathBattleContextPatch
    {
        private static void Prefix(MapEventSide __instance, out MapEvent __state) =>
            __state = MarshalBattleCasualtyContext.Enter(__instance.MapEvent);

        private static Exception Finalizer(Exception __exception, MapEvent __state)
        {
            MarshalBattleCasualtyContext.Restore(__state);
            return __exception;
        }
    }
}
