using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.Localization;

internal static class RecruitmentRankTests
{
    private sealed class Manager : ICampaignBehaviorManager
    {
        internal readonly List<CampaignBehaviorBase> Items = new List<CampaignBehaviorBase>();
        public T GetBehavior<T>() => Items.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetBehaviors<T>() => Items.OfType<T>();
        public void AddBehavior(CampaignBehaviorBase behavior) => Items.Add(behavior);
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase => Items.RemoveAll(behavior => behavior is T);
        public void ClearBehaviors() => Items.Clear();
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> behaviors) => Items.AddRange(behaviors);
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }

    private static readonly Assembly Mod = typeof(FeudalTitleBehavior).Assembly;
    private static readonly Type Guard = Mod.GetType("BellumCivile.Patches.RecruitmentRankGuard", true);
    private static Campaign _campaign;
    private static Clan _player, _recruit, _ruler;
    private static Hero _playerHero, _recruitHero;
    private static Kingdom _realm, _foreign, _playerRealm, _recruitRealm;
    private static bool _minor, _mercenary, _clanGone, _realmGone, _playerIsLeader;
    private static int _cancelled, _notices, _persuasionsEnded;
    private static string _refusal;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Campaign(ref Campaign __result) { __result = _campaign; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool MainHero(ref Hero __result) { __result = _playerHero; return false; }
    private static bool Conversation(ref Hero __result) { __result = _recruitHero; return false; }
    private static bool ClanRealm(Clan __instance, ref Kingdom __result)
    { __result = __instance == _player ? _playerRealm : _recruitRealm; return false; }
    private static bool HeroClan(Hero __instance, ref Clan __result)
    { __result = __instance == _playerHero ? _player : _recruit; return false; }
    private static bool Leader(Clan __instance, ref Hero __result)
    { __result = __instance == _player && _playerIsLeader ? _playerHero : _recruitHero; return false; }
    private static bool Ruler(ref Clan __result) { __result = _ruler; return false; }
    private static bool Minor(ref bool __result) { __result = _minor; return false; }
    private static bool Mercenary(ref bool __result) { __result = _mercenary; return false; }
    private static bool ClanGone(ref bool __result) { __result = _clanGone; return false; }
    private static bool RealmGone(ref bool __result) { __result = _realmGone; return false; }
    private static bool Cancel() { _cancelled++; return false; }
    private static bool Notice() { _notices++; return false; }
    private static bool EndPersuasion() { _persuasionsEnded++; return false; }
    private static bool TextVariable(string variableName, TextObject text)
    { if (variableName == "LIEGE_IS_RELATIVE") _refusal = text.ToString(); return false; }
    private static object Prefix(string patch, string method, params object[] args)
        => AccessTools.Method(Mod.GetType("BellumCivile.Patches." + patch, true), method).Invoke(null, args);
    private static bool Blocked(Clan clan, Kingdom realm, out bool peer)
    {
        var args = new object[] { clan, realm, false };
        bool blocked = (bool)AccessTools.Method(Guard, "IsBlocked", new[] { typeof(Clan), typeof(Kingdom), typeof(bool).MakeByRefType() }).Invoke(null, args);
        peer = (bool)args[2];
        return blocked;
    }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.recruitment_rank");
        void Patch(MethodBase method, string prefix) => harmony.Patch(method, prefix: new HarmonyMethod(typeof(RecruitmentRankTests), prefix));
        _player = Blank<Clan>(); _player.StringId = "player_house";
        _recruit = Blank<Clan>(); _recruit.StringId = "count_house";
        _playerHero = Blank<Hero>(); _recruitHero = Blank<Hero>();
        _realm = Blank<Kingdom>(); _realm.StringId = "empire_indep_barony";
        _foreign = Blank<Kingdom>(); _foreign.StringId = "foreign_realm";
        _campaign = Blank<Campaign>();
        var manager = new Manager();
        var titles = new FeudalTitleBehavior();
        manager.Items.Add(titles);
        _campaign.AddCampaignBehaviorManager(manager);
        var registry = (Dictionary<string, FeudalTitleRecord>)AccessTools.Field(typeof(FeudalTitleBehavior), "_titlesById").GetValue(titles);
        FeudalTitleRecord Add(string id, FeudalTitleType rank, Clan actual, Clan legal = null, bool active = true)
        {
            var title = new FeudalTitleRecord(id, id, rank, (legal ?? actual)?.StringId, actual?.StringId, "", "", "", 0, 0, active);
            registry[id] = title;
            return title;
        }
        void Index() => AccessTools.Method(typeof(FeudalTitleBehavior), "RebuildRuntimeIndexes").Invoke(titles, new object[] { true, false });
        void Setup(FeudalTitleType rulerRank = FeudalTitleType.Barony, FeudalTitleType recruitRank = FeudalTitleType.County)
        {
            _ruler = _player; _playerRealm = _realm; _recruitRealm = _foreign;
            _minor = _mercenary = _clanGone = _realmGone = false; _playerIsLeader = true;
            _cancelled = _notices = _persuasionsEnded = 0; _refusal = null;
            registry.Clear();
            Add("sovereign", rulerRank, _player);
            Add("recruit_title", recruitRank, _recruit);
            Index();
        }

        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Campaign), "Current"), nameof(Campaign));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), nameof(MainHero));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "OneToOneConversationHero"), nameof(Conversation));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(ClanRealm));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(HeroClan));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsMinorFaction"), nameof(Minor));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsUnderMercenaryService"), nameof(Mercenary));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), nameof(ClanGone));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(RealmGone));
            Patch(AccessTools.Method(typeof(BarterManager), "CancelAndFinalizePlayerBarter"), nameof(Cancel));
            Patch(AccessTools.Method(Guard, "ShowRefusal"), nameof(Notice));
            Patch(AccessTools.Method(typeof(ConversationManager), "EndPersuasion"), nameof(EndPersuasion));
            Patch(AccessTools.Method(typeof(MBTextManager), "SetTextVariable", new[] { typeof(string), typeof(TextObject), typeof(bool) }), nameof(TextVariable));

            foreach (FeudalTitleType rulerRank in Enum.GetValues(typeof(FeudalTitleType)))
            foreach (FeudalTitleType recruitRank in Enum.GetValues(typeof(FeudalTitleType)))
            {
                Setup(rulerRank, recruitRank);
                bool blocked = Blocked(_recruit, _realm, out bool peer);
                check(blocked == (recruitRank >= rulerRank) && peer == (recruitRank == rulerRank),
                    $"Only a strictly higher sovereign rank can recruit: {rulerRank} versus {recruitRank}");
            }
            Setup();
            check(Blocked(_recruit, _realm, out _), "Independent baron cannot recruit a foreign count");
            Add("promoted_crown", FeudalTitleType.Duchy, _player); Index();
            check(!Blocked(_recruit, _realm, out _), "Promotion immediately permits recruitment of a now-lower-ranked count");
            registry["promoted_crown"].SetActive(false); Index();
            check(Blocked(_recruit, _realm, out _), "An inactive higher crown cannot bypass the current sovereign rank");
            Setup(FeudalTitleType.Duchy, FeudalTitleType.Barony);
            Add("actual_county", FeudalTitleType.County, _recruit);
            Add("actual_kingdom", FeudalTitleType.Kingdom, _recruit); Index();
            check(Blocked(_recruit, _realm, out _), "The recruit's highest held title is checked, not the first or lowest holding");
            registry["actual_kingdom"].SetActive(false); Index();
            check(!Blocked(_recruit, _realm, out _), "Inactive titles do not inflate the recruit's rank");
            var rival = Blank<Clan>(); rival.StringId = "rival_house";
            Add("lost_kingdom", FeudalTitleType.Kingdom, rival, _recruit); Index();
            check(!Blocked(_recruit, _realm, out _), "A lost de jure title is not treated as a currently possessed rank");
            check(titles.GetRealmSovereignTitle(_realm, FeudalHierarchyMode.DeFacto).TitleType == FeudalTitleType.Duchy
                && titles.GetHighestDeFactoTitleHeldByClan(_recruit).TitleType == FeudalTitleType.County,
                "Ignoring a disputed title does not secretly promote the player or demote the recruit");
            Setup();
            registry.Remove("recruit_title"); Index();
            check(!Blocked(_recruit, _realm, out _), "Untitled nobles remain recruitable");
            Setup();
            registry.Remove("sovereign"); Index();
            check(!Blocked(_recruit, _realm, out _), "Missing sovereign data does not invent a rank or block native recruitment");
            Setup();
            manager.Items.Clear();
            check(!Blocked(_recruit, _realm, out _), "No restriction is invented without Bellum's title behavior");
            manager.Items.Add(titles);
            foreach (string exception in new[] { "vassal_player", "not_clan_leader", "same_realm", "minor", "mercenary", "dead_clan", "dead_realm", "landless_player", "no_player" })
            {
                Setup();
                if (exception == "vassal_player") _ruler = _recruit;
                if (exception == "not_clan_leader") _playerIsLeader = false;
                if (exception == "same_realm") _recruitRealm = _realm;
                if (exception == "minor") _minor = true;
                if (exception == "mercenary") _mercenary = true;
                if (exception == "dead_clan") _clanGone = true;
                if (exception == "dead_realm") _realmGone = true;
                if (exception == "landless_player") _playerRealm = null;
                Hero original = _playerHero;
                if (exception == "no_player") _playerHero = null;
                check(!Blocked(_recruit, _realm, out _), "Unrelated recruitment path is unchanged: " + exception);
                _playerHero = original;
            }
            Setup();
            check(!Blocked(null, _realm, out _) && !Blocked(_recruit, null, out _)
                && !Blocked(_player, _realm, out _) && !Blocked(_recruit, _foreign, out _),
                "Null, own-clan and non-player-realm targets are unaffected");
            _recruitRealm = null;
            check(Blocked(_recruit, _realm, out _), "A retained active title still matters when the recruit currently has no kingdom");

            Setup();
            foreach (string patch in new[] { "RecruitmentRankDialoguePatch", "RecruitmentRankConsequencePatch", "RecruitmentRankSuccessPatch",
                "RecruitmentRankBarterPatch", "RecruitmentRankBarterablePatch", "InternalWarRecruitmentDialoguePatch", "ExileRecruitmentDialoguePatch" })
                harmony.CreateClassProcessor(Mod.GetType("BellumCivile.Patches." + patch, true)).Patch();
            var dialogue = Blank<LordDefectionCampaignBehavior>();
            check(dialogue.conversation_lord_from_ruling_clan_on_condition()
                && _refusal == "My title stands above yours. I will not swear fealty to a ruler of lesser rank."
                && AccessTools.Field(typeof(LordDefectionCampaignBehavior), "_allReservations").GetValue(dialogue) == null,
                "Native refusal node gives an immersive rejection before persuasion tasks are created, alongside existing dialogue guards");
            Setup(FeudalTitleType.County, FeudalTitleType.County);
            check(dialogue.conversation_lord_from_ruling_clan_on_condition()
                && _refusal == "We stand as peers in rank. I will not swear fealty to you.",
                "Equal-ranked nobles use the peer-specific refusal");
            AccessTools.Method(typeof(LordDefectionCampaignBehavior), "conversation_leave_faction_barter_consequence").Invoke(dialogue, null);
            dialogue.conversation_lord_defect_to_clan_without_barter_on_consequence();
            check(_persuasionsEnded == 2 && _notices == 2 && _recruitRealm == _foreign,
                "Stale barter and free-recruitment consequences cannot transfer a peer or start a barter");
            check(!(bool)AccessTools.Method(typeof(LordDefectionCampaignBehavior), "defection_barter_successful_on_condition").Invoke(dialogue, null),
                "An old accepted barter cannot report an ineligible recruitment as successful");

            var recruitment = Blank<JoinKingdomAsClanBarterable>();
            AccessTools.PropertySetter(typeof(Barterable), "OriginalOwner").Invoke(recruitment, new object[] { _recruitHero });
            AccessTools.Field(typeof(JoinKingdomAsClanBarterable), "TargetKingdom").SetValue(recruitment, _realm);
            AccessTools.PropertySetter(typeof(Barterable), "IsOffered").Invoke(recruitment, new object[] { true });
            recruitment.CurrentAmount = 1;
            var data = Blank<BarterData>();
            AccessTools.Field(typeof(BarterData), "_barterables").SetValue(data, new List<Barterable> { recruitment });
            var barterManager = Blank<BarterManager>();
            AccessTools.Method(typeof(BarterManager), "ApplyAndFinalizePlayerBarter").Invoke(barterManager, new object[] { _playerHero, _recruitHero, data });
            check(_cancelled == 1 && _notices == 3 && _recruitRealm == _foreign,
                "Selected ineligible recruitment cancels the whole exchange before any native payment or allegiance change");
            check(recruitment.GetUnitValueForFaction(_recruit) < -1000000,
                "Barter valuation rejects an ineligible recruitment");
            recruitment.Apply();
            check(_notices == 4 && _recruitRealm == _foreign, "Direct native barterable application is also guarded before relation changes or clan transfer");
            recruitment.CurrentAmount = 0;
            check((bool)Prefix("RecruitmentRankBarterPatch", "Prefix", barterManager, _playerHero, _recruitHero, data),
                "Zero-amount recruitment does not block other barter items");
            recruitment.CurrentAmount = 1;
            AccessTools.PropertySetter(typeof(Barterable), "IsOffered").Invoke(recruitment, new object[] { false });
            check((bool)Prefix("RecruitmentRankBarterPatch", "Prefix", barterManager, _playerHero, _recruitHero, data),
                "Unoffered recruitment does not block other barter items");
            AccessTools.PropertySetter(typeof(Barterable), "IsOffered").Invoke(recruitment, new object[] { true });
            Add("new_duchy", FeudalTitleType.Duchy, _player); Index();
            check((bool)Prefix("RecruitmentRankBarterPatch", "Prefix", barterManager, _playerHero, _recruitHero, data)
                && (bool)Prefix("RecruitmentRankBarterablePatch", "ApplyPrefix", recruitment)
                && (bool)Prefix("RecruitmentRankConsequencePatch", "Prefix")
                && (bool)Prefix("RecruitmentRankDialoguePatch", "Prefix", false),
                "Every guard passes through normally once the sovereign genuinely outranks the recruit");
            var nullArgs = new object[] { null, false };
            check(!(bool)AccessTools.Method(Guard, "IsBlocked", new[] { typeof(JoinKingdomAsClanBarterable), typeof(bool).MakeByRefType() }).Invoke(null, nullArgs),
                "Null recruitment barterables are harmless");
        }
        finally { harmony.UnpatchAll(harmony.Id); _campaign = null; }
    }
}
