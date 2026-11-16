using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;

internal static class PersonalRelationIntegrationTests
{
    private static readonly Assembly Mod = typeof(DynamicRelationBehavior).Assembly;
    private static Hero _player;
    private static bool _managed, _enabled;
    private static int _personal, _political;
    private static float _native;
    private static bool MainHero(ref Hero __result) { __result = _player; return false; }
    private static bool Managed(ref bool __result) { __result = _managed; return false; }
    private static bool Enabled(ref bool __result) { __result = _enabled; return false; }
    private static bool Threshold(ref int __result) { __result = 60; return false; }
    private static bool Personal(ref int __result) { __result = _personal; return false; }
    private static bool Native(ref float __result) { __result = _native; return false; }
    private static bool Day(ref float __result) { __result = 10f; return false; }
    private static bool Political(ref int __result) { __result = _political; return false; }
    private static bool Zero(ref int __result) { __result = 0; return false; }
    private static bool NotSpouses(ref bool __result) { __result = false; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.tests.personal_relations");
        var previous = DynamicRelationBehavior.Instance;
        var previousLordships = LordshipBehavior.Instance;
        var tooltip = Mod.GetType("BellumCivile.Patches.HeroTooltipPersonalRelationPatch", true);
        var summons = Mod.GetType("BellumCivile.ArmySummonsEligibilityHelper", true);
        var kind = Mod.GetType("BellumCivile.ArmySummonsRefusalKind", true);
        var options = Mod.GetType("BellumCivile.BellumCivileOptions", true);
        var nativeCall = AccessTools.Method(typeof(Hero), nameof(Hero.GetRelationWithPlayer));
        var display = AccessTools.Method(tooltip, "GetDisplayedRelation");
        var target = AccessTools.Method(typeof(TooltipRefresherCollection), "RefreshHeroTooltip");
        var instructions = PatchProcessor.GetOriginalInstructions(target).Select(i => new CodeInstruction(i)).ToList();
        check(instructions.Count(i => i.Calls(nativeCall)) == 1, "The installed game has exactly one native hero-tooltip relation call");
        var rewritten = ((IEnumerable<CodeInstruction>)AccessTools.Method(tooltip, "Transpiler")
            .Invoke(null, new object[] { instructions })).ToList();
        check(rewritten.Count(i => i.Calls(display)) == 1 && !rewritten.Any(i => i.Calls(nativeCall)),
            "Only the hero-tooltip relation call is redirected to Bellum's display helper");

        void Patch(MethodBase targetMethod, string prefix) => h.Patch(targetMethod,
            prefix: new HarmonyMethod(typeof(PersonalRelationIntegrationTests), prefix));
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), nameof(MainHero));
            Patch(AccessTools.Method(typeof(DynamicRelationBehavior), "ManagesPair"), nameof(Managed));
            Patch(AccessTools.PropertyGetter(options, "EnableFeudalArmySummons"), nameof(Enabled));
            Patch(AccessTools.PropertyGetter(options, "ArmyPersonalFriendRelationThreshold"), nameof(Threshold));
            Patch(AccessTools.Method(typeof(CharacterRelationManager), "GetHeroRelation"), nameof(Personal));
            Patch(nativeCall, nameof(Native));
            Patch(AccessTools.PropertyGetter(typeof(DynamicRelationBehavior), "CurrentDay"), nameof(Day));
            var baseline = Mod.GetType("BellumCivile.DynamicRelationBaselineHelper", true);
            Patch(AccessTools.Method(baseline, "CalculatePoliticalConditionsRaw"), nameof(Political));
            Patch(AccessTools.Method(baseline, "GetPrisonerCustodyModifier"), nameof(Zero));
            Patch(AccessTools.Method(Mod.GetType("BellumCivile.PersonalKinshipHelper", true), "IsSpouse"), nameof(NotSpouses));
            AccessTools.PropertySetter(typeof(DynamicRelationBehavior), "Instance").Invoke(null, new object[] { new DynamicRelationBehavior() });
            _player = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
            var other = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
            _player.StringId = "cache_player"; other.StringId = "cache_other";
            _personal = -20; _native = 90; _managed = true; _enabled = true;
            check((float)display.Invoke(null, new object[] { other }) == -20f,
                "Managed hero tooltips show the personal relation, not a different native effective value");
            _managed = false;
            check((float)display.Invoke(null, new object[] { other }) == 90f,
                "Unmanaged hero tooltips retain the native relation value");
            var friend = AccessTools.Method(summons, "CanAnswerAsPersonalFriend");
            object legalRefusal = Enum.Parse(kind, "NotLegalVassal");
            object sourRefusal = Enum.Parse(kind, "SouredRelations");
            bool CanAnswer(object refusal) => (bool)friend.Invoke(null, new object[] { _player, other, refusal });
            check(!CanAnswer(legalRefusal), "A favorable clan relation cannot turn a hostile individual into a personal friend");
            _personal = 59;
            check(!CanAnswer(legalRefusal), "A personal relation below the friendship threshold is refused");
            _personal = 60; _native = -90;
            check(CanAnswer(legalRefusal), "A qualifying personal relation works independently of the clan value");
            check(!CanAnswer(sourRefusal), "Personal friendship does not bypass a soured-relations refusal");
            _enabled = false;
            check(!CanAnswer(legalRefusal), "The exception remains disabled when feudal army summons are disabled");

            var lordships = new LordshipBehavior();
            AccessTools.PropertySetter(typeof(LordshipBehavior), "Instance").Invoke(null, new object[] { lordships });
            var behavior = DynamicRelationBehavior.Instance;
            var pairType = Mod.GetType("BellumCivile.DynamicRelationPairKey", true);
            object pair = Activator.CreateInstance(pairType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object[] { _player, other }, null);
            var political = AccessTools.Method(typeof(DynamicRelationBehavior), "GetPoliticalConditionsRaw");
            int ReadPolitical() => (int)political.Invoke(behavior, new object[] { pair, _player, other });
            _political = -10;
            check(ReadPolitical() == -10, "Political conditions are cached before a lordship change");
            _political = 0;
            check(ReadPolitical() == -10, "A political cache hit remains stable while the lordship revision is unchanged");
            var cacheVisible = AccessTools.Method(typeof(DynamicRelationBehavior), "CacheVisibleRelation");
            var readVisible = AccessTools.Method(typeof(DynamicRelationBehavior), "TryGetCachedVisibleRelation");
            cacheVisible.Invoke(behavior, new object[] { pair, -10, 20f });
            object[] visibleArgs = { pair, 0 };
            check((bool)readVisible.Invoke(behavior, visibleArgs) && (int)visibleArgs[1] == -10,
                "The visible relation cache is populated before a lordship change");
            AccessTools.PropertySetter(typeof(LordshipBehavior), "Revision").Invoke(lordships, new object[] { 1 });
            check(ReadPolitical() == 0, "A lordship revision refreshes political conditions immediately");
            visibleArgs = new object[] { pair, 0 };
            check(!(bool)readVisible.Invoke(behavior, visibleArgs), "A lordship revision invalidates the visible relation cache immediately");
        }
        finally
        {
            h.UnpatchAll(h.Id);
            AccessTools.PropertySetter(typeof(DynamicRelationBehavior), "Instance").Invoke(null, new object[] { previous });
            AccessTools.PropertySetter(typeof(LordshipBehavior), "Instance").Invoke(null, new object[] { previousLordships });
        }
    }
}
