using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.Patches;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

internal static class FeudRecruitmentTests
{
    private static Kingdom _source;
    private static Clan _clan;
    private static Hero _hero;
    private static ClaimFeudWarBehavior _feuds;
    private static int _cancelled;
    private static bool Source(ref Kingdom __result) { __result = _source; return false; }
    private static bool HeroClan(ref Clan __result) { __result = _clan; return false; }
    private static bool Conversation(ref Hero __result) { __result = _hero; return false; }
    private static bool Player(ref Clan __result) { __result = null; return false; }
    private static bool Ignore() => false;
    private static bool Cancel() { _cancelled++; return false; }
    private static bool Block(Clan __0, Kingdom __1, ref bool __2, ref bool __result)
    { __2 = IsBlocked(__0, __1); __result = __2; return false; }
    private static bool BlockConversation(ref bool __0, ref bool __result)
    { __0 = true; __result = true; return false; }
    private static bool IsBlocked(Clan clan, Kingdom target) => (bool)AccessTools.Method(typeof(ClaimFeudWarBehavior), "ShouldBlockRecruitment")
        .Invoke(_feuds, new object[] { clan, target });
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.test.feud_recruitment");
        void Patch(MethodBase m, string method) => h.Patch(m, prefix: new HarmonyMethod(typeof(FeudRecruitmentTests), method));
        Kingdom Realm(string id) { var k = Blank<Kingdom>(); k.StringId = id; return k; }
        var parent = Realm("parent"); var claimant = Realm("claimant_shell"); var holder = Realm("holder_shell"); var foreign = Realm("foreign");
        _source = claimant; _clan = Blank<Clan>(); _clan.StringId = "claimant_supporter"; _hero = Blank<Hero>();
        _feuds = new ClaimFeudWarBehavior(); _cancelled = 0;
        var war = new ClaimFeudWarRecord("war", "feud", "parent", "claimant_shell", "holder_shell", "title", "claimant", "holder",
            "claimant,claimant_supporter", "holder,holder_supporter", "", "", 0);
        ((List<ClaimFeudWarRecord>)AccessTools.Field(typeof(ClaimFeudWarBehavior), "_wars").GetValue(_feuds)).Add(war);
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Source));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(HeroClan));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "OneToOneConversationHero"), nameof(Conversation));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            foreach (string id in new[] { "claimant", "claimant_supporter", "holder", "holder_supporter" })
            {
                _clan.StringId = id;
                check(IsBlocked(_clan, foreign), "Active feud blocks recruitment of participant " + id);
                check(IsBlocked(_clan, parent), "Recruitment cannot bypass reconciliation by returning " + id + " early");
            }
            check(!IsBlocked(_clan, claimant), "Same-realm barter is not a defection");
            _clan.StringId = "late_joiner";
            check(IsBlocked(_clan, foreign), "Temporary feud realm protects members absent from the initial roster");
            _source = parent;
            check(!IsBlocked(_clan, foreign), "Uninvolved houses in the parent realm remain recruitable");
            _clan.StringId = "holder_supporter";
            check(IsBlocked(_clan, foreign), "Active saved commitment protects a house even during a realm transfer");
            war.SetActive(false);
            check(!IsBlocked(_clan, foreign), "Resolved feud releases the recruitment restriction");
            check(!IsBlocked(null, foreign) && !IsBlocked(_clan, null), "Missing recruitment parties do not block unrelated dialogue");
            war.SetActive(true); _source = holder;
            Patch(AccessTools.Method(typeof(BlockCivilWarClanRecruitmentPatch), "ShouldBlockRecruitment"), nameof(Block));
            Patch(AccessTools.Method(typeof(BlockCivilWarClanRecruitmentPatch), "ShowRecruitmentBlocked"), nameof(Ignore));
            Patch(AccessTools.Method(typeof(BarterManager), "CancelAndFinalizePlayerBarter"), nameof(Cancel));
            var assembly = typeof(BlockCivilWarClanRecruitmentPatch).Assembly;
            foreach (string patch in new[] { "InternalWarRecruitmentDialoguePatch", "InternalWarRecruitmentConsequencePatch",
                "InternalWarRecruitmentSuccessPatch", "InternalWarRecruitmentBarterPatch" })
                h.CreateClassProcessor(assembly.GetType("BellumCivile.Patches." + patch, true)).Patch();
            var recruitment = Blank<JoinKingdomAsClanBarterable>();
            AccessTools.PropertySetter(typeof(Barterable), "OriginalOwner").Invoke(recruitment, new object[] { _hero });
            AccessTools.Field(typeof(JoinKingdomAsClanBarterable), "TargetKingdom").SetValue(recruitment, foreign);
            AccessTools.PropertySetter(typeof(Barterable), "IsOffered").Invoke(recruitment, new object[] { true });
            recruitment.CurrentAmount = 1;
            var data = Blank<BarterData>();
            AccessTools.Field(typeof(BarterData), "_barterables").SetValue(data, new List<Barterable> { recruitment });
            var manager = Blank<BarterManager>();
            AccessTools.Method(typeof(BarterManager), "ApplyAndFinalizePlayerBarter").Invoke(manager, new object[] { _hero, _hero, data });
            check(_cancelled == 1, "Blocked recruitment cancels the entire barter before native payment or transfers");
            int value = 0;
            check(!BlockCivilWarClanRecruitmentPatch.GetUnitValueForFactionPrefix(recruitment, foreign, ref value) && value < -1000000,
                "Recruitment valuation includes active feud commitments");
            check(!BlockCivilWarClanRecruitmentPatch.ApplyPrefix(recruitment), "Final recruitment callback cannot transfer an active feud participant");
            recruitment.CurrentAmount = 0;
            var barterPrefix = AccessTools.Method(assembly.GetType("BellumCivile.Patches.InternalWarRecruitmentBarterPatch"), "Prefix");
            check((bool)barterPrefix.Invoke(null, new object[] { manager, _hero, _hero, data }), "Unselected recruitment does not block other barter items");
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.Patches.InternalWarRecruitmentDialoguePatch"), "ConversationBlocked"), nameof(BlockConversation));
            var dialogue = Blank<LordDefectionCampaignBehavior>();
            check(dialogue.conversation_lord_from_ruling_clan_on_condition(), "Common recruitment node refuses before building persuasion tasks");
            check(!(bool)AccessTools.Method(typeof(LordDefectionCampaignBehavior), "defection_barter_successful_on_condition").Invoke(dialogue, null),
                "Stale successful-barter state cannot report blocked recruitment as successful");
        }
        finally { h.UnpatchAll(h.Id); _feuds = null; _source = null; _clan = null; _hero = null; }
    }
}
