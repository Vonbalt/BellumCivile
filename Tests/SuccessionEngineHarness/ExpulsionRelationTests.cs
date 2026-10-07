using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.Patches;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class ExpulsionRelationTests
{
    private static readonly List<Clan> Members = new List<Clan>();
    private static readonly Dictionary<Clan, Hero> Leaders = new Dictionary<Clan, Hero>();
    private static readonly Dictionary<Hero, int> Relations = new Dictionary<Hero, int>();
    private static readonly HashSet<Hero> Dead = new HashSet<Hero>();
    private static readonly List<RelationMemoryRecord> Memories = new List<RelationMemoryRecord>();
    private static Clan _ruling, _player;
    private static Hero _expelledLeader;

    private static bool Clans(ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(Members); return false; }
    private static bool Leader(Clan __instance, ref Hero __result)
    { Leaders.TryGetValue(__instance, out __result); return false; }
    private static bool IsDead(Hero __instance, ref bool __result)
    { __result = Dead.Contains(__instance); return false; }
    private static bool Ruling(ref Clan __result) { __result = _ruling; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Name(Hero __instance, ref TextObject __result)
    { __result = new TextObject(__instance.StringId); return false; }
    private static bool Relation(Hero __instance, Hero __0, ref int __result)
    {
        if (__0 != _expelledLeader) throw new InvalidOperationException("Friendship must be checked against the expelled leader.");
        Relations.TryGetValue(__instance, out __result);
        return false;
    }
    private static bool Capture(Hero __0, Hero __1, int __2)
    {
        object descriptor = AccessTools.Property(typeof(RelationMemoryService), "CurrentDescriptor").GetValue(null);
        if (descriptor == null) throw new InvalidOperationException("Expulsion reaction has no explicit memory context.");
        object Read(string name) => AccessTools.Property(descriptor.GetType(), name).GetValue(descriptor);
        Memories.Add(new RelationMemoryRecord((RelationMemoryScope)Read("Scope"), __0.StringId, __1.StringId,
            (string)Read("SourceId"), (string)Read("ContextText"), __2, 0f, (float)Read("DurationDays")));
        return false;
    }
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.expulsion_relations");
        void Patch(MethodBase method, string prefix) => harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(ExpulsionRelationTests), prefix));
        Clan Member(string id, int relation, bool mercenary = false, bool include = true)
        {
            var clan = Blank<Clan>(); clan.StringId = id;
            var leader = Blank<Hero>(); leader.StringId = id + "_leader";
            Leaders[clan] = leader; Relations[leader] = relation;
            if (include) Members.Add(clan);
            if (mercenary) clan.StartMercenaryService();
            return clan;
        }
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Clans"), nameof(Clans));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruling));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDead"), nameof(IsDead));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Name"), nameof(Name));
            Patch(AccessTools.Method(typeof(Hero), "GetRelation", new[] { typeof(Hero) }), nameof(Relation));
            Patch(AccessTools.Method(typeof(ChangeRelationAction), "ApplyRelationChangeBetweenHeroes"), nameof(Capture));

            var realm = Blank<Kingdom>();
            _ruling = Member("crown", 100);
            var expelled = Member("expelled", 100);
            _expelledLeader = Leaders[expelled];
            var friend = Member("vassal_friend", 11);
            _player = Member("player_mercenary_friend", 11, mercenary: true);
            Member("mercenary_neutral", 0, mercenary: true);
            Member("vassal_at_threshold", 10);
            Member("enemy", -50);
            Member("foreign_friend", 100, include: false);
            Dead.Add(Leaders[Member("dead_friend", 100)]);
            Members.Add(Blank<Clan>());
            Members.Add(null);

            var helper = typeof(RelationMemoryService).Assembly.GetType("BellumCivile.ExpulsionRelationHelper");
            var apply = AccessTools.Method(helper, "ApplyFriendMemories");
            void Apply() => apply.Invoke(null, new object[] { realm, _ruling, Leaders[_ruling], expelled });
            Apply();
            check(_player.IsUnderMercenaryService && Memories.Count == 2,
                "Only qualifying friends react, including a serving player mercenary");
            check(Memories.All(memory => memory.Value == -5 && memory.Scope == RelationMemoryScope.Personal
                && memory.SourceId == RelationMemorySources.ExpelledMyFriend
                && memory.ContextText == _expelledLeader.StringId
                && memory.ExpiryDay == 10f * Math.Max(1, CampaignTime.DaysInYear)),
                "Both friendship reactions are named personal -5 memories for ten campaign years");
            check(Memories.All(memory => memory.PairKey == RelationMemoryRecord.BuildPairKey(Leaders[_ruling].StringId, Leaders[friend].StringId)
                || memory.PairKey == RelationMemoryRecord.BuildPairKey(Leaders[_ruling].StringId, Leaders[_player].StringId)),
                "The grievance targets the ruler, not the expelled clan or an uninvolved leader");
            check(RelationMemorySources.GetDisplayText(Memories[0].SourceId, Memories[0].ContextText)
                == "Expelled my friend from the realm: " + _expelledLeader.StringId,
                "Memory display identifies the expelled friend");
            check(AccessTools.Property(typeof(RelationMemoryService), "CurrentDescriptor").GetValue(null) == null,
                "Expulsion memory context is restored after applying reactions");

            Memories.Clear();
            Dead.Add(_expelledLeader); Apply();
            check(Memories.Count == 0, "A dead expelled leader cannot receive duplicate exile-friendship reactions");
            Dead.Remove(_expelledLeader); Dead.Add(Leaders[_ruling]); Apply();
            check(Memories.Count == 0, "A dead ruler receives no expulsion friendship reactions");
            Dead.Remove(Leaders[_ruling]);
            apply.Invoke(null, new object[] { null, _ruling, Leaders[_ruling], expelled });
            apply.Invoke(null, new object[] { realm, _ruling, Leaders[_ruling], null });
            check(Memories.Count == 0, "Missing realm or expelled clan is harmless");

            AccessTools.Method(typeof(ExpelClanDecisionPatch), "ApplyExpulsionAftermath").Invoke(null,
                new object[] { expelled, _ruling, realm });
            check(Memories.Count == 3 && Memories.Count(memory => memory.SourceId == RelationMemorySources.ExpelledMyFriend) == 2,
                "Ordinary expulsion applies the shared reaction exactly once per qualifying friend");
            var ownGrievance = Memories.Single(memory => memory.SourceId == RelationMemorySources.ExiledMyHouse);
            check(ownGrievance.Value == -10 && ownGrievance.Scope == RelationMemoryScope.House
                && ownGrievance.ExpiryDay == 15f * Math.Max(1, CampaignTime.DaysInYear),
                "The expelled house's own grievance remains unchanged");

            foreach (string method in new[] { "SubmitPlayerTreasonJudgment", "ExecuteTreasonPurge" })
            {
                var instructions = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(IdeologyBehavior), method)).ToList();
                check(instructions.Count(instruction => instruction.Calls(apply)) == 1
                    && !instructions.Any(instruction => (instruction.operand as MethodInfo)?.Name == "ApplyRelationChangeBetweenHeroes"),
                    "Treason path uses the shared named reaction instead of raw -20: " + method);
                check(instructions.Count(instruction => (instruction.operand as MethodInfo)?.Name == "ExecuteTreasonSentence") == 1,
                    "Treason path retains its separate execution handling: " + method);
            }
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            Members.Clear(); Leaders.Clear(); Relations.Clear(); Dead.Clear(); Memories.Clear();
            _ruling = null; _player = null; _expelledLeader = null;
        }
    }
}
