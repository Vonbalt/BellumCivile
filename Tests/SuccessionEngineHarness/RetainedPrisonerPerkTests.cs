using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using BellumCivile.Behaviors;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

internal static class RetainedPrisonerPerkTests
{
    private static CharacterObject _character, _receivedCharacter;
    private static MobileParty _party;
    private static PerkObject _perk, _receivedPerk;
    private static bool _primary;
    private static BattleEnvironment _environment;
    private static int _personalCalls, _partyCalls;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Character(ref CharacterObject __result) { __result = _character; return false; }
    private static bool Party(ref MobileParty __result) { __result = _party; return false; }
    private static bool Army(ref Army __result) { __result = null; return false; }
    private static bool Leader(ref Hero __result) { __result = null; return false; }
    private static bool Perk(ref PerkObject __result) { __result = _perk; return false; }
    private static bool PartyBonus() { _partyCalls++; return false; }
    private static bool PersonalBonus(PerkObject perk, CharacterObject character, bool isPrimaryBonus,
        ref ExplainedNumber bonuses, BattleEnvironment battleEnvironment, ref bool __result)
    {
        _receivedPerk = perk; _receivedCharacter = character; _primary = isPrimaryBonus;
        _environment = battleEnvironment; _personalCalls++;
        bonuses.Add(.05f); __result = true; return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        var signature = new[] { typeof(PerkObject), typeof(CharacterObject), typeof(bool),
            typeof(ExplainedNumber).MakeByRefType(), typeof(BattleEnvironment) };
        var helper = AccessTools.Method(typeof(PerkHelper), "AddPerkBonusForCharacter", signature);
        check(helper != null && helper.ReturnType == typeof(bool), "Character perk helper uses the 1.5.6 argument order");
        if (helper == null) return;
        check(helper.GetParameters().Last().IsOptional
            && Convert.ToInt32(helper.GetParameters().Last().DefaultValue) == (int)BattleEnvironment.Any,
            "Character perk helper defaults to the Any environment");
        var apply = AccessTools.Method(typeof(RetainedTreatyPrisonerBehavior), "ApplyVanillaPerkModifiers");
        var native = AccessTools.Method(typeof(PrisonerReleaseCampaignBehavior), "DailyHeroTick");
        check(PatchProcessor.GetOriginalInstructions(native).Any(i => i.Calls(helper)),
            "Native prisoner escape uses the same installed perk helper");
        check(PatchProcessor.GetOriginalInstructions(apply).Any(i => i.Calls(helper)),
            "Retained-prisoner escape binds to the same helper as vanilla");

        var harmony = new Harmony("bellum.tests.retained_prisoner_perks");
        void Patch(MethodBase method, string name) => harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(RetainedPrisonerPerkTests), name));
        void Getter(Type type, string property, string name) => Patch(AccessTools.PropertyGetter(type, property), name);
        _character = Blank<CharacterObject>(); _party = Blank<MobileParty>(); _perk = Blank<PerkObject>();
        _personalCalls = _partyCalls = 0;
        try
        {
            Getter(typeof(PartyBase), "IsSettlement", nameof(No));
            Getter(typeof(PartyBase), "IsMobile", nameof(Yes));
            Getter(typeof(PartyBase), "MobileParty", nameof(Party));
            Getter(typeof(MobileParty), "Army", nameof(Army));
            Getter(typeof(MobileParty), "LeaderHero", nameof(Leader));
            Getter(typeof(Hero), "CharacterObject", nameof(Character));
            Getter(typeof(DefaultPerks.Roguery), "FleetFooted", nameof(Perk));
            Getter(typeof(DefaultPerks.Riding), "MountedPatrols", nameof(Perk));
            Getter(typeof(DefaultPerks.Roguery), "RansomBroker", nameof(Perk));
            Getter(typeof(DefaultPerks.Scouting), "KeenSight", nameof(Perk));
            Patch(helper, nameof(PersonalBonus));
            Patch(AccessTools.Method(typeof(PerkHelper), "AddPerkBonusForParty"), nameof(PartyBonus));
            CheckInvocation(check, apply);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            _character = _receivedCharacter = null; _party = null; _perk = _receivedPerk = null;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckInvocation(Action<bool, string> check, MethodInfo apply)
    {
        var args = new object[] { Blank<Hero>(), Blank<PartyBase>(), new ExplainedNumber(.1f) };
        apply.Invoke(null, args);
        check(_personalCalls == 1 && _receivedCharacter == _character && _receivedPerk == _perk,
            "Retained-prisoner calculation passes the captive's character and Fleet Footed perk exactly once");
        check(!_primary && _environment == BattleEnvironment.Any,
            "Retained-prisoner calculation preserves the secondary bonus and Any environment");
        check(Math.Abs(((ExplainedNumber)args[2]).ResultNumber - .15f) < .00001f,
            "Reordered helper still updates the original escape chance by reference");
        check(_partyCalls == 3, "Other mounted-patrol, ransom-broker and keen-sight bonuses are preserved");
    }
}
