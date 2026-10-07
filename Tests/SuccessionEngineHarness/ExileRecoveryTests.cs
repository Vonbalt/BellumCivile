using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CampaignBehaviors.BarterBehaviors;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

internal static class ExileRecoveryTests
{
    private sealed class Manager : ICampaignBehaviorManager
    {
        internal readonly List<CampaignBehaviorBase> Items = new List<CampaignBehaviorBase>();
        public T GetBehavior<T>() => Items.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetBehaviors<T>() => Items.OfType<T>();
        public void AddBehavior(CampaignBehaviorBase b) => Items.Add(b);
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase => Items.RemoveAll(b => b is T);
        public void ClearBehaviors() => Items.Clear();
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> b) => Items.AddRange(b);
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }
    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsSaving)
            {
                Data[key] = data is IDictionary || data is IList ? Activator.CreateInstance(data.GetType(), data) : (object)data;
                return true;
            }
            data = Data.TryGetValue(key, out object saved) ? (T)saved : default;
            return true;
        }
    }

    private static readonly Type Behavior = typeof(ExiledClanRecoveryBehavior);
    private static readonly Assembly Mod = Behavior.Assembly;
    private static readonly Type Selection = Mod.GetType("BellumCivile.Behaviors.RefugeSelectionHelper", true);
    private static readonly Dictionary<Clan, Hero> Leaders = new Dictionary<Clan, Hero>();
    private static readonly Dictionary<Clan, Kingdom> Realms = new Dictionary<Clan, Kingdom>();
    private static readonly Dictionary<Kingdom, Clan> Rulers = new Dictionary<Kingdom, Clan>();
    private static readonly List<Kingdom> Kingdoms = new List<Kingdom>();
    private static readonly HashSet<Hero> Children = new HashSet<Hero>();
    private static readonly Dictionary<Hero, int> Opinions = new Dictionary<Hero, int>();
    private static Clan _exile, _player;
    private static Kingdom _origin;
    private static float _day;
    private static int _joins, _inquiries, _cancelled;
    private static ExiledClanRecoveryBehavior _behavior;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(object instance, string name, params object[] args)
        => AccessTools.Method(instance.GetType(), name).Invoke(instance, args);
    private static object Static(Type type, string name, params object[] args)
        => AccessTools.Method(type, name).Invoke(null, args);
    private static Dictionary<string, T> Data<T>(string name) => (Dictionary<string, T>)AccessTools.Field(Behavior, name).GetValue(_behavior);
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Ignore() => false;
    private static bool Zero(ref int __result) { __result = 0; return false; }
    private static bool Trait(ref TraitObject __result) { __result = null; return false; }
    private static bool Age(ref float __result) { __result = 30; return false; }
    private static bool Child(Hero __instance, ref bool __result) { __result = Children.Contains(__instance); return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool ConversationHero(ref Hero __result) { __result = Leaders[_exile]; return false; }
    private static bool Leader(Clan __instance, ref Hero __result) { Leaders.TryGetValue(__instance, out __result); return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result) { Realms.TryGetValue(__instance, out __result); return false; }
    private static bool Crown(Kingdom __instance, ref Clan __result) { Rulers.TryGetValue(__instance, out __result); return false; }
    private static bool HeroClan(Hero __instance, ref Clan __result)
    { __result = Leaders.FirstOrDefault(entry => entry.Value == __instance).Key; return false; }
    private static bool Heroes(Clan __instance, ref MBReadOnlyList<Hero> __result)
    { __result = new MBReadOnlyList<Hero>(new List<Hero> { Leaders[__instance] }); return false; }
    private static bool AllClans(ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(Leaders.Keys.ToList()); return false; }
    private static bool AllRealms(ref MBReadOnlyList<Kingdom> __result)
    { __result = new MBReadOnlyList<Kingdom>(Kingdoms); return false; }
    private static bool Court(Kingdom __instance, ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(new List<Clan> { Rulers[__instance] }); return false; }
    private static bool Holdings(ref MBReadOnlyList<Town> __result)
    { __result = new MBReadOnlyList<Town>(new List<Town> { Blank<Town>() }); return false; }
    private static bool Settlements(ref MBReadOnlyList<Settlement> __result)
    { __result = new MBReadOnlyList<Settlement>(new List<Settlement> { Blank<Settlement>() }); return false; }
    private static bool Parties(ref MBReadOnlyList<WarPartyComponent> __result)
    { __result = new MBReadOnlyList<WarPartyComponent>(new List<WarPartyComponent>()); return false; }
    private static bool Relation(Hero __instance, Hero __0, ref int __result)
    { Opinions.TryGetValue(__instance == Leaders[_exile] ? __0 : __instance, out __result); return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(_day); return false; }
    private static bool Future(CampaignTime __instance, ref bool __result) { __result = __instance.ToDays > _day; return false; }
    private static bool Elapsed(CampaignTime __instance, ref float __result) { __result = _day - (float)__instance.ToDays; return false; }
    private static bool Inquiry() { _inquiries++; return false; }
    private static bool Cancel() { _cancelled++; return false; }
    private static bool Join(Clan clan, Kingdom newKingdom)
    {
        _joins++; Kingdom old = Realms[clan]; Realms[clan] = newKingdom;
        Call(_behavior, "OnClanChangedKingdom", clan, old, newKingdom, ChangeKingdomAction.ChangeKingdomActionDetail.JoinKingdom, false);
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        CheckScores(check);
        var h = new Harmony("bellum.tests.exile_recovery");
        var previousCampaign = Campaign.Current;
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay"); object oldTicks = ticks.GetValue(null);
        void Patch(MethodBase method, string prefix)
        {
            if (method == null) throw new Exception("Missing exile fixture method: " + prefix);
            h.Patch(method, prefix: new HarmonyMethod(typeof(ExileRecoveryTests), prefix));
        }
        Clan House(string id)
        {
            var c = Blank<Clan>(); c.StringId = id; var hero = Blank<Hero>(); hero.StringId = id + "_leader";
            Leaders[c] = hero; Realms[c] = null; return c;
        }
        Kingdom Kingdom(string id)
        {
            var realm = Blank<Kingdom>(); realm.StringId = id; Kingdoms.Add(realm);
            Rulers[realm] = House(id + "_house"); Realms[Rulers[realm]] = realm; return realm;
        }
        bool CanReturn(Kingdom realm) => (bool)Call(_behavior, "CanReturnToRealm", _exile, realm);
        void Departure(ExileCause cause, bool rebelled = false) => Call(_behavior, "RecordDeparture", _exile, _origin, cause, rebelled);
        void ClearCurrent() => Call(_behavior, "RemoveTrackedExile", _exile.StringId);
        Kingdom Select() => (Kingdom)Static(Selection, "FindBestRefuge", _exile, _origin, new Kingdom[0]);
        void DepartEvent(ChangeKingdomAction.ChangeKingdomActionDetail detail, Kingdom source = null)
            => Call(_behavior, "OnClanChangedKingdom", _exile, source ?? _origin, null, detail, false);
        try
        {
            ticks.SetValue(null, 1000L); _day = 100;
            _exile = House("exile"); _player = House("player");
            _origin = Kingdom("origin"); Kingdom refuge = Kingdom("refuge"), other = Kingdom("other");
            var campaign = Blank<Campaign>(); var manager = new Manager();
            _behavior = new ExiledClanRecoveryBehavior(); manager.AddBehavior(_behavior);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            campaign.AddCampaignBehaviorManager(manager);
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "IsFuture"), nameof(Future));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "ElapsedDaysUntilNow"), nameof(Elapsed));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsNoble"), nameof(Yes));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsMinorFaction"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Heroes"), nameof(Heroes));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "All"), nameof(AllClans));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "WarPartyComponents"), nameof(Parties));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(HeroClan));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Yes));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDisabled"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsChild"), nameof(Child));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Age"), nameof(Age));
            Patch(AccessTools.Method(typeof(Hero), "GetTraitLevel"), nameof(Zero));
            Patch(AccessTools.PropertyGetter(typeof(DefaultTraits), "Mercy"), nameof(Trait));
            Patch(AccessTools.PropertyGetter(typeof(DefaultTraits), "Generosity"), nameof(Trait));
            Patch(AccessTools.Method(typeof(Hero), "GetRelation"), nameof(Relation));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Crown));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(AllRealms));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Clans"), nameof(Court));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Fiefs"), nameof(Holdings));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Settlements"), nameof(Settlements));
            Patch(AccessTools.Method(typeof(Kingdom), "IsAtWarWith"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Settlement), "IsTown"), nameof(Yes));
            Patch(AccessTools.Method(Behavior, "IsLandlessForExile"), nameof(Yes));
            Patch(AccessTools.Method(Behavior, "ShowPlayerAsylumInquiry"), nameof(Inquiry));
            Patch(AccessTools.Method(typeof(ChangeKingdomAction), "ApplyByJoinToKingdom"), nameof(Join));

            Departure(ExileCause.Treason);
            check(!CanReturn(_origin), "Expulsion blocks return to the expelling house");
            ClearCurrent(); check(!CanReturn(_origin), "Finding a home does not erase the departure history");
            Hero expellingLeader = Leaders[Rulers[_origin]];
            Leaders[Rulers[_origin]] = Blank<Hero>();
            check(!CanReturn(_origin), "A new ruler from the same expelling house does not erase the ban");
            Leaders[Rulers[_origin]] = expellingLeader;
            Clan expeller = Rulers[_origin]; Rulers[_origin] = House("replacement_dynasty");
            check(CanReturn(_origin), "A different ruling house reopens consideration, not guaranteed acceptance");
            Rulers[_origin] = expeller;
            _day = 1000; check(!CanReturn(_origin), "Time and desperation cannot remove an expelling-house ban");
            _day = 100; Departure(ExileCause.VoluntaryDeparture);
            _day = 129; check(!CanReturn(_origin), "Voluntary return is blocked before thirty days");
            _day = 130; check(CanReturn(_origin), "Voluntary return opens at the thirty-day boundary");
            check((float)Call(_behavior, "GetReturnDistrust", _exile, _origin) == 15, "Recent voluntary departure retains distrust after cooldown");
            _day = 100; Departure(ExileCause.VoluntaryDeparture, true); _day = 189;
            check(!CanReturn(_origin), "Rebellious departure has a ninety-day return cooldown");
            _day = 190; check(CanReturn(_origin), "Rebellious return becomes eligible after ninety days");
            Departure(ExileCause.KingdomDestroyed); check(CanReturn(_origin), "Realm destruction is not a betrayal penalty");

            _day = 200; ClearCurrent(); DepartEvent(ChangeKingdomAction.ChangeKingdomActionDetail.LeaveKingdom);
            check(Data<string>("_trackedOriginKingdomIds")[_exile.StringId] == _origin.StringId && !CanReturn(_origin),
                "Ordinary leave event records the old realm immediately");
            Call(_behavior, "RegisterExpulsion", _exile, _origin);
            _day = 1000; check(!CanReturn(_origin), "Legal expulsion upgrades the ordinary leave record to an expelling-house ban");
            var save = new Store(); _behavior.SyncData(save); save.IsLoading = true;
            var loaded = new ExiledClanRecoveryBehavior(); loaded.SyncData(save);
            check(!(bool)Call(loaded, "CanReturnToRealm", _exile, _origin), "Departure restriction survives persistence roundtrip");
            check(save.Data.Keys.Count(key => key.StartsWith("BellumCivile_ExileDeparture")) == 3, "House, reason and date are all persisted");
            var legacy = new ExiledClanRecoveryBehavior(); legacy.SyncData(new Store { IsLoading = true });
            check((bool)Call(legacy, "CanReturnToRealm", _exile, refuge), "Missing new save fields do not invent past expulsions");
            ClearCurrent(); DepartEvent(ChangeKingdomAction.ChangeKingdomActionDetail.LeaveByKingdomDestruction);
            check(Data<int>("_trackedExileCauseIds")[_exile.StringId] == (int)ExileCause.KingdomDestroyed && CanReturn(_origin),
                "Native destruction tracks displacement without labelling it betrayal");
            ClearCurrent(); Kingdom shell = Kingdom("bc_feud_fixture");
            DepartEvent(ChangeKingdomAction.ChangeKingdomActionDetail.LeaveKingdom, shell);
            check(!Data<string>("_trackedOriginKingdomIds").ContainsKey(_exile.StringId), "Temporary feud moves create no departure history");
            check(!(bool)Static(Selection, "IsValidRefuge", shell), "Temporary feud realms cannot accept refugees");

            _day = 1000;
            Realms[_exile] = refuge;
            Call(_behavior, "OnClanChangedKingdom", _exile, _origin, refuge,
                ChangeKingdomAction.ChangeKingdomActionDetail.JoinKingdomByDefection, false);
            check(!CanReturn(_origin), "Direct native defection records departure without a separate leave event");
            _day = 1089; check(!CanReturn(_origin), "Defection uses the longer ninety-day return cooldown");
            _day = 1090; check(CanReturn(_origin), "Direct defection becomes eligible at the ninety-day boundary");
            Call(_behavior, "OnClanChangedKingdom", _exile, refuge, other,
                ChangeKingdomAction.ChangeKingdomActionDetail.JoinKingdom, false);
            check(!(bool)Call(_behavior, "HasDepartureRecord", _exile, refuge),
                "Ordinary scripted JoinKingdom movement does not invent a betrayal record");
            Realms[_exile] = null;

            Departure(ExileCause.Treason); Opinions[Leaders[Rulers[refuge]]] = -60;
            Opinions[Leaders[Rulers[other]]] = 70;
            check(Select() == other, "Actual destination selection refuses the hostile ruler and chooses a willing court");
            Opinions[Leaders[Rulers[other]]] = -100;
            check(Select() == null, "With no acceptable host, no realm is forced to receive the exile");
            Static(Selection, "CanRecruitLandlessClan", _exile, refuge);
            check(!(bool)Static(Selection, "CanRecruitLandlessClan", _exile, refuge), "Manual recruitment into an NPC realm obeys the hatred veto");

            _day = 1100; _joins = 0;
            Call(_behavior, "TakeOwnershipOfAutomaticRecruitment", _exile);
            Call(_behavior, "RecoverDueExiles");
            check(_joins == 0 && Realms[_exile] == null && !(bool)Call(_behavior, "ShouldAttemptRetry", _exile.StringId),
                "Refusal leaves the clan independent and starts a retry cooldown");
            Opinions[Leaders[Rulers[other]]] = 70; _day = 1106;
            Call(_behavior, "RecoverDueExiles"); check(_joins == 0, "A newly friendly court does not bypass the retry window");
            _day = 1107; Call(_behavior, "RecoverDueExiles");
            check(_joins == 1 && Realms[_exile] == other, "A due retry joins a willing NPC realm");
            check(!CanReturn(_origin), "Resettlement keeps older expulsion history intact");

            Realms[_exile] = null; _day = 1200; _player = Rulers[refuge]; Opinions[Leaders[_player]] = 80;
            Opinions[Leaders[Rulers[other]]] = -100; _inquiries = 0;
            Call(_behavior, "TakeOwnershipOfAutomaticRecruitment", _exile); Call(_behavior, "RecoverDueExiles");
            check(_inquiries == 1 && Realms[_exile] == null, "Player ruler receives a request instead of an automatic admission");
            check(!(bool)Call(_behavior, "ShouldAttemptRetry", _exile.StringId), "Pending asylum is not repeated by the retry scheduler");
            Call(_behavior, "DenyPlayerAsylum", _exile.StringId, true);
            check((bool)Call(_behavior, "IsExcludedRefuge", _exile, refuge), "Player refusal excludes that realm for this exile episode");
            check(!(bool)Call(_behavior, "ShouldAttemptRetry", _exile.StringId), "Denied asylum still respects the retry window");
            check((bool)Static(Selection, "CanRecruitLandlessClan", _exile, refuge), "Player ruler can deliberately reconsider and invite the clan");
            Call(_behavior, "RecordDeparture", _exile, refuge, ExileCause.Expulsion, false);
            check(!CanReturn(refuge), "Expulsion from the player's house remains recorded before an invitation");
            Realms[_exile] = refuge; Call(_behavior, "RecordPlayerInvitation", _exile, refuge); Realms[_exile] = null;
            check(CanReturn(refuge), "A completed player invitation explicitly pardons that departure");
            ClearCurrent(); Children.Add(Leaders[_exile]);
            check((bool)Static(Behavior, "IsRecoverableExileCandidate", _exile), "A child-only landless family remains protected");
            check(!(bool)Static(Behavior, "CanClanBeRelocated", _exile), "Child-only family waits for an eligible adult representative");
            check(_behavior.TryPreserveClanFromDiscontinuation(_exile), "Discontinuation protection includes surviving children");
            Children.Clear();

            _player = House("another_player"); Opinions[Leaders[Rulers[refuge]]] = -60;
            var patch = Mod.GetType("BellumCivile.Patches.ExileRecruitmentPatch", true);
            var barterPatch = Mod.GetType("BellumCivile.Patches.ExileRecruitmentBarterPatch", true);
            var automaticPatch = Mod.GetType("BellumCivile.Patches.ExileAutomaticRecruitmentPatch", true);
            h.CreateClassProcessor(automaticPatch).Patch();
            Static(typeof(ExileRecoveryTests), "InvokeNativeRecruitment", _exile, refuge);
            check(Data<CampaignTime>("_nextRefugeRetryDates").ContainsKey(_exile.StringId), "Native auto-recruitment defers to the asylum scheduler");
            var offer = Blank<JoinKingdomAsClanBarterable>();
            AccessTools.PropertySetter(typeof(Barterable), "OriginalOwner").Invoke(offer, new object[] { Leaders[_exile] });
            AccessTools.Field(typeof(JoinKingdomAsClanBarterable), "TargetKingdom").SetValue(offer, refuge);
            var valueArgs = new object[] { offer, 0 };
            check(!(bool)Static(patch, "ValuePrefix", valueArgs) && (int)valueArgs[1] < -1000000, "Rejected recruitment has a prohibitive barter value");
            check(!(bool)Static(patch, "ApplyPrefix", offer), "Stale recruitment cannot bypass the final admission guard");
            AccessTools.PropertySetter(typeof(Barterable), "IsOffered").Invoke(offer, new object[] { true }); offer.CurrentAmount = 1;
            var data = Blank<BarterData>(); AccessTools.Field(typeof(BarterData), "_barterables").SetValue(data, new List<Barterable> { offer });
            Patch(AccessTools.Method(typeof(BarterManager), "CancelAndFinalizePlayerBarter"), nameof(Cancel));
            Patch(AccessTools.Method(patch, "ShowRefusal"), nameof(Ignore)); _cancelled = 0;
            check(!(bool)Static(barterPatch, "Prefix", Blank<BarterManager>(), Leaders[_player], Leaders[_exile], data) && _cancelled == 1,
                "The whole rejected recruitment barter is cancelled before taking payment");
            offer.CurrentAmount = 0;
            check((bool)Static(barterPatch, "Prefix", Blank<BarterManager>(), Leaders[_player], Leaders[_exile], data),
                "Unselected recruitment does not cancel unrelated barter items");

            h.CreateClassProcessor(patch).Patch();
            h.CreateClassProcessor(barterPatch).Patch();
            foreach (string name in new[] { "ExileRecruitmentDialoguePatch", "ExileRecruitmentConsequencePatch", "ExileRecruitmentSuccessPatch" })
                h.CreateClassProcessor(Mod.GetType("BellumCivile.Patches." + name, true)).Patch();
            Patch(AccessTools.PropertyGetter(typeof(Hero), "OneToOneConversationHero"), nameof(ConversationHero));
            Patch(AccessTools.Method(typeof(ConversationManager), "EndPersuasion"), nameof(Ignore));
            Realms[_player] = refuge;
            var conversation = Blank<LordDefectionCampaignBehavior>();
            check((bool)Call(conversation, "conversation_lord_from_ruling_clan_on_condition"),
                "Recruitment dialogue explains an NPC ruler's refusal before persuasion");
            int before = _joins;
            Call(conversation, "conversation_lord_defect_to_clan_without_barter_on_consequence");
            Call(conversation, "conversation_leave_faction_barter_consequence");
            check(_joins == before, "Both persuaded recruitment consequences respect the NPC ruler's refusal");
            check(!(bool)Call(conversation, "defection_barter_successful_on_condition"),
                "Rejected recruitment never reports a successful defection");
            offer.Apply();
            check(_joins == before, "Bound barter Apply patch prevents an invalid recruitment");
        }
        finally
        {
            h.UnpatchAll(h.Id); ticks.SetValue(null, oldTicks);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { previousCampaign });
            Leaders.Clear(); Realms.Clear(); Rulers.Clear(); Kingdoms.Clear(); Children.Clear(); Opinions.Clear();
            _exile = _player = null; _origin = null; _behavior = null;
        }
    }

    private static void InvokeNativeRecruitment(Clan clan, Kingdom kingdom) => AccessTools.Method(typeof(DiplomaticBartersBehavior), "ConsiderClanJoin")
        .Invoke(Blank<DiplomaticBartersBehavior>(), new object[] { clan, kingdom });

    private static void CheckScores(Action<bool, string> check)
    {
        Type inputType = Mod.GetType("BellumCivile.Behaviors.RefugeConsiderations", true);
        object Evaluate(params (string Field, object Value)[] fields)
        {
            object input = Activator.CreateInstance(inputType);
            foreach (var field in fields) AccessTools.Field(inputType, field.Field).SetValue(input, field.Value);
            return Static(Selection, "Evaluate", input);
        }
        bool Flag(object result, string name) => (bool)AccessTools.Field(result.GetType(), name).GetValue(result);
        float Score(object result, string name) => (float)AccessTools.Field(result.GetType(), name).GetValue(result);
        for (int relation = -100; relation <= -60; relation++)
        {
            var score = Evaluate(("RulerRelation", relation), ("ExileDays", 10000f), ("SameCulture", true),
                ("EnemyOfOrigin", true), ("FamilyTie", true), ("FriendlyCourtiers", 20), ("AverageCourtRelation", 100f),
                ("ClanTier", 6), ("Strongholds", 100), ("Mercy", 2), ("Generosity", 2));
            check(!Flag(score, "RulerWilling"), "Ruler hatred cannot be outweighed at relation " + relation);
        }
        var acceptable = Evaluate(("RulerRelation", -59), ("SameCulture", true), ("EnemyOfOrigin", true), ("FamilyTie", true));
        check(Flag(acceptable, "RulerWilling"), "Above -60, strategic benefits can make admission acceptable");
        var neutral = Evaluate(); check(Flag(neutral, "ExileWilling") && Flag(neutral, "RulerWilling"), "Neutral houses have a viable baseline");
        var rejected = Evaluate(("RulerRelation", -50), ("AverageCourtRelation", -40f), ("HostileCourtiers", 4));
        check(!Flag(rejected, "RulerWilling"), "Bad court relations can refuse admission even above the hard cutoff");
        var desperate = Evaluate(("RulerRelation", -50), ("AverageCourtRelation", -40f), ("HostileCourtiers", 4), ("ExileDays", 10000f));
        check(Score(rejected, "Acceptance") == Score(desperate, "Acceptance"), "Desperation never increases the receiving ruler's acceptance");
        check(Score(desperate, "Preference") - Score(rejected, "Preference") == 20f, "Exile desperation bonus is capped at twenty");
        var enemy = Evaluate(("EnemyOfOrigin", true), ("ExileRelation", -40));
        var friend = Evaluate(("ExileRelation", 60));
        check(Score(friend, "Preference") > Score(enemy, "Preference"), "A friendly neutral court can outrank an enemy of the old realm");
        var hated = Evaluate(("ExileRelation", -60), ("ExileDays", 10000f), ("SameCulture", true), ("FamilyTie", true));
        check(!Flag(hated, "ExileWilling"), "Automatic asylum does not send a house to a ruler its leader hates");
    }
}
