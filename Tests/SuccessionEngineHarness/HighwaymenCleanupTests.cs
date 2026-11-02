using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Library;

internal static class HighwaymenCleanupTests
{
    private static readonly List<MobileParty> Parties = new List<MobileParty>(), Destroyed = new List<MobileParty>();
    private static readonly Dictionary<MobileParty, PartyBase> Bases = new Dictionary<MobileParty, PartyBase>();
    private static readonly Dictionary<MobileParty, CampaignVec2> Positions = new Dictionary<MobileParty, CampaignVec2>();
    private static readonly Dictionary<MobileParty, MapEvent> Battles = new Dictionary<MobileParty, MapEvent>();
    private static readonly HashSet<MobileParty> Inside = new HashSet<MobileParty>(), Camps = new HashSet<MobileParty>(),
        Sieges = new HashSet<MobileParty>(), Armies = new HashSet<MobileParty>(), Quests = new HashSet<MobileParty>(),
        Seas = new HashSet<MobileParty>(), Transitions = new HashSet<MobileParty>(), Disbanding = new HashSet<MobileParty>(),
        EventSides = new HashSet<MobileParty>();
    private static Settlement _town;
    private static int _scans, _logs;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool All(ref MBReadOnlyList<MobileParty> __result) { _scans++; __result = new MBReadOnlyList<MobileParty>(Parties); return false; }
    private static bool Towns(ref MBReadOnlyList<Settlement> __result) { __result = new MBReadOnlyList<Settlement>(new[] { _town }); return false; }
    private static bool IsTown(ref bool __result) { __result = true; return false; }
    private static bool Base(MobileParty __instance, ref PartyBase __result) { Bases.TryGetValue(__instance, out __result); return false; }
    private static bool Mobile(PartyBase __instance, ref MobileParty __result) { __result = Bases.FirstOrDefault(x => x.Value == __instance).Key; return false; }
    private static bool IsMobile(ref bool __result) { __result = true; return false; }
    private static bool MainParty(ref MobileParty __result) { __result = null; return false; }
    private static bool Position(MobileParty __instance, ref CampaignVec2 __result) { __result = Positions[__instance]; return false; }
    private static bool Current(MobileParty __instance, ref Settlement __result) { __result = Inside.Contains(__instance) ? _town : null; return false; }
    private static bool Battle(MobileParty __instance, ref MapEvent __result) { Battles.TryGetValue(__instance, out __result); return false; }
    private static bool Side(MobileParty __instance, ref MapEventSide __result) { __result = EventSides.Contains(__instance) ? Blank<MapEventSide>() : null; return false; }
    private static bool Camp(MobileParty __instance, ref BesiegerCamp __result) { __result = Camps.Contains(__instance) ? Blank<BesiegerCamp>() : null; return false; }
    private static bool Siege(MobileParty __instance, ref SiegeEvent __result) { __result = Sieges.Contains(__instance) ? Blank<SiegeEvent>() : null; return false; }
    private static bool Army(MobileParty __instance, ref TaleWorlds.CampaignSystem.Army __result) { __result = Armies.Contains(__instance) ? Blank<TaleWorlds.CampaignSystem.Army>() : null; return false; }
    private static bool Quest(MobileParty __instance, ref bool __result) { __result = Quests.Contains(__instance); return false; }
    private static bool Sea(MobileParty __instance, ref bool __result) { __result = Seas.Contains(__instance); return false; }
    private static bool Transition(MobileParty __instance, ref bool __result) { __result = Transitions.Contains(__instance); return false; }
    private static bool Disband(MobileParty __instance, ref bool __result) { __result = Disbanding.Contains(__instance); return false; }
    private static bool Destroy(MobileParty destroyedParty) { Destroyed.Add(destroyedParty); return false; }
    private static bool Log() { _logs++; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.tests.highwaymen_cleanup");
        var previous = Campaign.Current;
        var scene = new PartyNavigationTestScene(); scene.CreateCampaign();
        void Patch(MethodBase target, string name) => h.Patch(target, prefix: new HarmonyMethod(typeof(HighwaymenCleanupTests), name));
        void Getter(Type type, string property, string name) => Patch(AccessTools.PropertyGetter(type, property), name);
        MobileParty Party(string suffix, float x = 101)
        {
            var p = Blank<MobileParty>(); p.StringId = "bellum_highwaymen_" + suffix; p.IsActive = true;
            Parties.Add(p); Bases[p] = Blank<PartyBase>(); Positions[p] = new CampaignVec2(new Vec2(x, 100), true); return p;
        }
        try
        {
            Parties.Clear(); Destroyed.Clear(); Bases.Clear(); Positions.Clear(); Battles.Clear();
            foreach (var set in new[] { Inside, Camps, Sieges, Armies, Quests, Seas, Transitions, Disbanding, EventSides }) set.Clear();
            _scans = _logs = 0; _town = Blank<Settlement>(); _town.StringId = "cleanup_town";
            AccessTools.PropertySetter(typeof(Settlement), "GatePosition").Invoke(_town,
                new object[] { new CampaignVec2(new Vec2(100, 100), true) });
            Getter(typeof(MobileParty), "All", nameof(All)); Getter(typeof(Settlement), "All", nameof(Towns));
            Getter(typeof(Settlement), "IsTown", nameof(IsTown)); Getter(typeof(MobileParty), "MainParty", nameof(MainParty));
            Getter(typeof(MobileParty), "Party", nameof(Base)); Getter(typeof(PartyBase), "MobileParty", nameof(Mobile));
            Getter(typeof(PartyBase), "IsMobile", nameof(IsMobile)); Getter(typeof(MobileParty), "Position", nameof(Position));
            Getter(typeof(MobileParty), "CurrentSettlement", nameof(Current)); Getter(typeof(MobileParty), "MapEvent", nameof(Battle));
            Getter(typeof(MobileParty), "MapEventSide", nameof(Side)); Getter(typeof(MobileParty), "BesiegerCamp", nameof(Camp));
            Getter(typeof(MobileParty), "SiegeEvent", nameof(Siege)); Getter(typeof(MobileParty), "Army", nameof(Army));
            Getter(typeof(MobileParty), "IsCurrentlyUsedByAQuest", nameof(Quest)); Getter(typeof(MobileParty), "IsCurrentlyAtSea", nameof(Sea));
            Getter(typeof(MobileParty), "IsTransitionInProgress", nameof(Transition)); Getter(typeof(MobileParty), "IsDisbanding", nameof(Disband));
            Patch(AccessTools.Method(typeof(DestroyPartyAction), "Apply"), nameof(Destroy));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Log));
            var healthy = Party("healthy"); var invalid = Party("invalid", 102); var disconnected = Party("disconnected", 103);
            scene.InvalidPositions.Add(Positions[invalid].ToVec2()); scene.DisconnectedPositions.Add(Positions[disconnected].ToVec2());
            var battling = Party("battle"); Battles[battling] = Blank<MapEvent>();
            Inside.Add(Party("inside")); Camps.Add(Party("siege_camp")); Sieges.Add(Party("siege"));
            Armies.Add(Party("army")); Quests.Add(Party("quest")); Seas.Add(Party("sea"));
            Transitions.Add(Party("transition")); Disbanding.Add(Party("disbanding")); EventSides.Add(Party("event_side"));
            var inactive = Party("inactive"); inactive.IsActive = false;
            var foreign = Party("foreign", 102); foreign.StringId = "bc_dynamic_merc_company_party";
            Party("far", 120); var missingBase = Party("no_base"); Bases[missingBase] = null;
            var noId = Party("no_id"); noId.StringId = null; Parties.Add(null);

            var session = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(ProxyWarBehavior), "OnSessionLaunched")).ToList();
            check(!session.Any(i => i.operand is MethodInfo m && m.Name == "CleanUpStuckHighwaymenParties"),
                "Session launch no longer destroys parties before native saved battles are restored");
            check(session.Any(i => i.operand is FieldInfo f && f.Name == "_highwaymenCleanupPending"),
                "Each session queues its runtime cleanup without introducing a saved migration flag");
            var nativeSession = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(Campaign), "OnSessionStart")).ToList();
            int afterSession = nativeSession.FindIndex(i => i.operand is MethodInfo m && m.Name == "OnAfterSessionStart");
            int restore = nativeSession.FindIndex(i => i.operand is MethodInfo m && m.Name == "OnAfterLoad" && m.DeclaringType.Name == "MapEventManager");
            check(afterSession >= 0 && restore > afterSession, "Installed native game restores battles after both session-launch events");
            var component = Blank<FieldBattleEventComponent>();
            var remove = AccessTools.Method(typeof(FieldBattleEventComponent), "OnRemoveInvolvedParty");
            if (remove?.DeclaringType == typeof(FieldBattleEventComponent))
            {
                var party = Blank<MapEventParty>(); AccessTools.PropertySetter(typeof(MapEventParty), "Party").Invoke(party, new object[] { Bases[battling] });
                bool threw = false;
                try { remove.Invoke(component, new object[] { party }); }
                catch (TargetInvocationException ex) when (ex.InnerException is NullReferenceException) { threw = true; }
                check(threw && component.MapEvent == null, "Beta native battle removal reproduces the unbound-component null reference before restoration");
            }
            var behavior = new ProxyWarBehavior(); var pending = AccessTools.Field(typeof(ProxyWarBehavior), "_highwaymenCleanupPending");
            var tick = AccessTools.Method(typeof(ProxyWarBehavior), "OnTick");
            tick.Invoke(behavior, new object[] { 0f });
            check(_scans == 0 && Destroyed.Count == 0, "No cleanup runs without a pending session request");
            pending.SetValue(behavior, true); tick.Invoke(behavior, new object[] { 0f });
            check(Destroyed.Count == 2 && Destroyed.Contains(invalid) && Destroyed.Contains(disconnected),
                "Runtime cleanup removes only Bellum highwaymen with proven invalid or disconnected land positions");
            foreach (var party in Parties.Where(p => p != null && p != invalid && p != disconnected))
                check(!Destroyed.Contains(party), "Cleanup preserves valid, unrelated or protected parties: " + party.StringId);
            check(!Destroyed.Contains(battling), "Saved field-battle participants never enter native party destruction");
            int scans = _scans; tick.Invoke(behavior, new object[] { 1f });
            check(_scans == scans && Destroyed.Count == 2, "Cleanup is one runtime pass per session, not a per-frame scan");
            Destroyed.Clear(); scene.InvalidPositions.Clear(); scene.DisconnectedPositions.Clear(); scene.PathDetour = 500;
            pending.SetValue(behavior, true); tick.Invoke(behavior, new object[] { 0f });
            check(Destroyed.Count == 0, "Long but valid routes around terrain do not cause false stuck-party cleanup");
            scene.PathDetour = 0; scene.ThrowOnPath = true;
            pending.SetValue(behavior, true); tick.Invoke(behavior, new object[] { 0f });
            check(Destroyed.Count == 0 && _logs > 2, "Navigation failures are logged and leave parties untouched");
            scene.ThrowOnPath = false; scene.InvalidPositions.Add(_town.GatePosition.ToVec2());
            pending.SetValue(behavior, true); tick.Invoke(behavior, new object[] { 0f });
            check(Destroyed.Count == 0, "An invalid town gate is not evidence that a valid nearby party is stuck");
        }
        finally
        {
            h.UnpatchAll(h.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { previous });
        }
    }
}
