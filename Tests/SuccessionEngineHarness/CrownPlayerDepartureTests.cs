using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class CrownPlayerDepartureTests
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
        internal readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T data)
        { if (IsLoading) data = (T)Values[key]; else Values[key] = data; return true; }
    }
    private static readonly Type Behavior = typeof(CrownAccessionBehavior);
    private static Campaign _campaign;
    private static Hero _player, _heir, _conversationHero, _heirMother, _heirFather, _playerMother, _playerFather;
    private static Clan _source, _cadet, _heirClan, _ruling;
    private static Kingdom _realm;
    private static MobileParty _party;
    private static bool _conversationRunning;
    private static bool _safe, _inParty, _married, _femalePlayer, _femaleHeir, _dead, _realmGone, _headIsHeir, _inquiryActive, _openSucceeds, _complete;
    private static float _age, _day;
    private static int _opens, _resolves, _mercenaryClears;
    private static readonly List<InquiryData> Inquiries = new List<InquiryData>();
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Campaign(ref Campaign __result) { __result = _campaign; return false; }
    private static bool Player(ref Hero __result) { __result = _player; return false; }
    private static bool PlayerClan(ref Clan __result) { __result = _source; return false; }
    private static bool House(ref Clan __result) { __result = _heirClan; return false; }
    private static bool Head(ref Hero __result) { __result = _headIsHeir ? _heir : _player; return false; }
    private static bool Ruler(ref Clan __result) { __result = _ruling; return false; }
    private static bool HeroName(Hero __instance, ref TextObject __result)
    { __result = new TextObject(__instance == _heir ? "Ira" : "David"); return false; }
    private static bool ClanName(Clan __instance, ref TextObject __result)
    { __result = new TextObject(__instance == _source ? "Banu Shum" : "Pethros II"); return false; }
    private static bool RealmName(ref TextObject __result) { __result = new TextObject("Southern Empire"); return false; }
    private static bool Spouse(Hero __instance, ref Hero __result)
    { __result = _married ? (__instance == _heir ? _player : _heir) : null; return false; }
    private static bool Mother(Hero __instance, ref Hero __result)
    { __result = __instance == _heir ? _heirMother : _playerMother; return false; }
    private static bool Father(Hero __instance, ref Hero __result)
    { __result = __instance == _heir ? _heirFather : _playerFather; return false; }
    private static bool Female(Hero __instance, ref bool __result) { __result = __instance == _heir ? _femaleHeir : _femalePlayer; return false; }
    private static bool Alive(ref bool __result) { __result = !_dead; return false; }
    private static bool Eliminated(ref bool __result) { __result = _realmGone; return false; }
    private static bool Age(ref float __result) { __result = _age; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool MainParty(ref MobileParty __result) { __result = _party; return false; }
    private static bool Party(ref MobileParty __result) { __result = _inParty ? _party : null; return false; }
    private static bool Safe(ref bool __result) { __result = _safe && !_inquiryActive && !_conversationRunning; return false; }
    private static bool Mission(ref object __result) { __result = null; return false; }
    private static bool Conversation(ref Hero __result) { __result = _conversationHero; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(_day); return false; }
    private static bool Open(CrownAccessionBehavior __instance, CrownAccessionRecord record, ref bool __result)
    {
        _opens++;
        if (_openSucceeds)
        {
            AccessTools.Field(Behavior, "_activeCrownDeparture").SetValue(__instance, record);
            _conversationRunning = true; _conversationHero = _heir;
        }
        __result = _openSucceeds;
        return false;
    }
    private static bool Resolve(CrownAccessionRecord record)
    {
        _resolves++;
        if (_complete)
        {
            record.Cadet = _cadet; record.CadetInitialized = record.EndowmentSettled = record.TitleTransferred = record.Completed = true;
            _heirClan = _ruling = _cadet;
        }
        return false;
    }
    private static bool Inquiry(InquiryData data)
    { Inquiries.Add(data); _inquiryActive = true; return false; }
    private static bool ClearMercenary() { _mercenaryClears++; return false; }
    private static object Call(object owner, string method, params object[] args) => AccessTools.Method(Behavior, method).Invoke(owner, args);

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.crown_player_departure");
        var ticksPerDay = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        object previousTicksPerDay = ticksPerDay.GetValue(null);
        void Patch(MethodBase target, string prefix)
        {
            if (target == null) throw new MissingMethodException(prefix);
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(CrownPlayerDepartureTests), prefix));
        }
        _player = Blank<Hero>(); _player.StringId = "player";
        _heir = Blank<Hero>(); _heir.StringId = "ira";
        _source = Blank<Clan>(); _source.StringId = "player_house";
        _cadet = Blank<Clan>(); _cadet.StringId = "crown_cadet";
        _realm = Blank<Kingdom>(); _realm.StringId = "inherited_realm";
        _party = Blank<MobileParty>(); _campaign = Blank<Campaign>();
        var manager = new Manager();
        manager.Items.Add(new DynamicMercenaryBandBehavior());
        _campaign.AddCampaignBehaviorManager(manager);
        CrownAccessionBehavior behavior = null;
        List<CrownAccessionRecord> records = null;
        CrownAccessionRecord Setup()
        {
            behavior = new CrownAccessionBehavior();
            records = (List<CrownAccessionRecord>)AccessTools.Field(Behavior, "_accessions").GetValue(behavior);
            _safe = _inParty = _married = _femaleHeir = _openSucceeds = _complete = true;
            _femalePlayer = _dead = _realmGone = _headIsHeir = _inquiryActive = false;
            _heirMother = _heirFather = _playerMother = _playerFather = _conversationHero = null;
            _heirClan = _source; _ruling = null; _conversationRunning = false; _day = 100; _age = 30;
            _opens = _resolves = _mercenaryClears = 0; Inquiries.Clear();
            var record = new CrownAccessionRecord
            {
                Heir = _heir, IncomingSourceHouse = _source, Realm = _realm, CreatesCadet = true,
                Household = new List<string> { _heir.StringId }, CadetName = "Pethros II"
            };
            records.Add(record);
            return record;
        }
        void Capture(CrownAccessionRecord record, bool dialogue = true) => Call(behavior, "CapturePlayerCrownDeparture", record, dialogue);
        void Tick() => Call(behavior, "ProcessPlayerCrownDepartures", .1f);
        bool Waiting(CrownAccessionRecord record) => (bool)Call(behavior, "WaitForPlayerCrownFarewell", record);
        void AcknowledgeLetter() { Inquiries.Last().AffirmativeAction(); _inquiryActive = false; }
        void Load(CrownAccessionRecord record)
        {
            var copy = new CrownAccessionRecord();
            foreach (var field in typeof(CrownAccessionRecord).GetFields()) field.SetValue(copy, field.GetValue(record));
            var store = new Store { IsLoading = true };
            store.Values["BC_CrownAccessions"] = new List<CrownAccessionRecord> { copy };
            behavior = new CrownAccessionBehavior(); behavior.SyncData(store);
            records = (List<CrownAccessionRecord>)AccessTools.Field(Behavior, "_accessions").GetValue(behavior);
        }
        try
        {
            ticksPerDay.SetValue(null, 1000000L);
            Patch(AccessTools.PropertyGetter(typeof(Campaign), "Current"), nameof(Campaign));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(PlayerClan));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(House));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Head));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Name"), nameof(HeroName));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Name"), nameof(ClanName));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Name"), nameof(RealmName));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Spouse"), nameof(Spouse));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Mother"), nameof(Mother));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Father"), nameof(Father));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsFemale"), nameof(Female));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Alive));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(Eliminated));
            foreach (string property in new[] { "IsDisabled", "IsPrisoner", "IsTraveling" })
                Patch(AccessTools.PropertyGetter(typeof(Hero), property), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Age"), nameof(Age));
            Patch(AccessTools.PropertyGetter(typeof(MobileParty), "MainParty"), nameof(MainParty));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "PartyBelongedTo"), nameof(Party));
            Patch(AccessTools.PropertyGetter(typeof(CampaignMission), "Current"), nameof(Mission));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "OneToOneConversationHero"), nameof(Conversation));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.Method(Behavior, "CanPresentCrownDeparture"), nameof(Safe));
            Patch(AccessTools.Method(Behavior, "OpenCrownDepartureConversation"), nameof(Open));
            Patch(AccessTools.Method(Behavior, "Resolve"), nameof(Resolve));
            Patch(AccessTools.Method(typeof(InformationManager), "ShowInquiry"), nameof(Inquiry));
            Patch(AccessTools.Method(typeof(DynamicMercenaryBandBehavior), "ClearPendingPlayerDeparture"), nameof(ClearMercenary));

            var r = Setup(); Capture(r); Capture(r);
            check(r.PlayerDepartureCaptured && r.PlayerDepartureWantsDialogue && !r.PlayerDepartureExplained
                && _mercenaryClears == 1 && _resolves == 0 && Waiting(r), "Party farewell is captured once before any transfer and takes priority over mercenary departure");
            Tick(); Tick();
            check(_opens == 1 && _resolves == 0 && Inquiries.Count == 0 && Waiting(r),
                "Conversation opens once and the household stays intact while it is active");
            check((bool)Call(behavior, "IsCrownDepartureConversation")
                && r.PlayerDepartureAddress == "My husband" && r.PlayerDepartureIsSpouse,
                "Spouse farewell has the correct speaker, relationship address and unchanged-marriage context");
            check(Call(null, "CrownDepartureFamilyTies", r).ToString() == "Whatever duties this crown brings, I remain your wife.",
                "A wife's farewell reassures her spouse naturally using the departing hero's gender");
            Call(behavior, "AcknowledgeCrownDeparture");
            check(r.PlayerDepartureExplained && Waiting(r) && _resolves == 0,
                "Acknowledgement is saved but does not transfer a hero out of an active conversation");
            _conversationRunning = false; _conversationHero = null; Tick(); Tick();
            check(r.Completed && _resolves == 1 && Inquiries.Count == 0 && _opens == 1,
                "Closing the acknowledged farewell resumes the existing accession once, without a duplicate letter");

            r = Setup(); Capture(r); Load(r); r = records.Single(); Tick(); Tick(); Tick();
            check(_opens == 1 && Waiting(r) && _resolves == 0,
                "Loading before presentation retains the one pending conversation rather than silently treating it as acknowledged");
            Call(behavior, "AcknowledgeCrownDeparture"); Load(r); r = records.Single();
            _conversationRunning = false; _conversationHero = null; Resolve(r); Tick(); Tick(); Tick();
            check(r.Completed && r.PlayerDepartureExplained && Inquiries.Count == 0 && _opens == 1,
                "An acknowledgement saved before transfer survives loading and never triggers a second explanation");

            r = Setup(); _inParty = false; Capture(r);
            Tick(); check(Inquiries.Count == 0 && !r.PlayerDepartureWantsDialogue, "Distant relative is never announced as reigning before accession completes");
            Resolve(r); Tick(); Tick();
            var letter = Inquiries.Single();
            check(letter.IsAffirmativeOptionShown && !letter.IsNegativeOptionShown && letter.NegativeAction == null
                && letter.Text.Contains("I have left Banu Shum") && letter.Text.Contains("Pethros II")
                && letter.Text.Contains("a household of my own")
                && letter.Text.Contains("Whatever duties this crown brings, I remain your wife.") && _opens == 0,
                "Completed distant accession produces one acknowledgement-only letter naming the actual new house");
            AcknowledgeLetter(); Tick(); check(r.PlayerDepartureExplained && Inquiries.Count == 1, "Acknowledged letter cannot repeat");

            r = Setup(); _inParty = false; _femalePlayer = true; _femaleHeir = false; Capture(r); Resolve(r); Tick();
            check(Inquiries.Single().Text.Contains("My wife,")
                && Inquiries.Single().Text.Contains("Whatever duties this crown brings, I remain your husband."),
                "A departing husband's letter addresses his wife and uses his own gender for the reassurance");
            r = Setup(); _inParty = false; _femalePlayer = _femaleHeir = true; Capture(r); Resolve(r); Tick();
            check(Inquiries.Single().Text.Contains("My wife,") && Inquiries.Single().Text.Contains("I remain your wife."),
                "Spousal reassurance follows the departing hero's gender independently of the player's gender");
            r = Setup(); _inParty = _married = false; _heirFather = _player; Capture(r); Resolve(r); Tick();
            check(Inquiries.Single().Text.Contains("Father,")
                && Inquiries.Single().Text.Contains("My duties may change, but we are still family.")
                && !Inquiries.Single().Text.Contains("I remain your wife."),
                "Other relatives use the natural family reassurance rather than marriage wording");

            r = Setup(); _openSucceeds = false; Capture(r); Tick();
            check(r.Completed && _resolves == 1 && _opens == 1 && Inquiries.Count == 1,
                "Failed conversation opening falls back to a letter without blocking accession");
            r = Setup(); _openSucceeds = _complete = false; Capture(r); Tick(); Tick();
            check(!r.Completed && !r.PlayerDepartureWantsDialogue && Inquiries.Count == 0 && _opens == 1 && _resolves == 1,
                "A failed farewell does not repeatedly retry settlement or invent a letter while estate delivery is still pending");
            _complete = true; Resolve(r); Tick();
            check(Inquiries.Count == 1, "A deferred accession sends its fallback letter only after the normal retry finishes");
            r = Setup(); Capture(r); Tick(); _conversationRunning = false; _conversationHero = null; Tick();
            check(r.Completed && !r.PlayerDepartureExplained && Inquiries.Count == 1,
                "An interrupted conversation resumes succession and retains a pending letter until acknowledgement");

            r = Setup(); Capture(r); Tick(); Load(r); r = records.Single(); _conversationRunning = false; _conversationHero = null;
            check(!Waiting(r), "Loading an attempted farewell releases the presentation gate before the normal hourly retry");
            Resolve(r);
            Tick(); Tick(); Tick();
            check(r.Completed && _opens == 1 && Inquiries.Count == 1 && !r.PlayerDepartureWantsDialogue,
                "Loading a saved in-progress conversation releases its gate and uses the letter fallback");
            Load(r); r = records.Single(); _inquiryActive = false; Tick(); Tick(); Tick();
            check(Inquiries.Count == 2 && !r.PlayerDepartureExplained, "Unacknowledged letter survives loading without marking itself delivered");
            AcknowledgeLetter(); Load(r); r = records.Single(); Tick(); Tick(); Tick();
            check(r.PlayerDepartureExplained && Inquiries.Count == 2, "Saved acknowledgement suppresses the letter after loading");

            r = Setup(); Capture(r); _safe = false; Tick();
            check(_opens == 0 && _resolves == 0, "Unsafe campaign UI does not open conversations or popups");
            _day = 102;
            check(!Waiting(r) && !r.PlayerDepartureWantsDialogue,
                "A delayed farewell stops gating inheritance after one campaign day even while UI is unavailable");
            Resolve(r); _safe = true; Tick(); check(Inquiries.Count == 1, "Timed-out farewell is still explained by letter after completion");

            r = Setup(); Capture(r); _inParty = false;
            check(!Waiting(r), "A relative leaving the main party before presentation releases the farewell gate");
            r = Setup(); _age = 12; _married = false; Capture(r); Resolve(r); Tick();
            check(_opens == 0 && Inquiries.Single().Text.Contains("The new household, Pethros II, will be led by a regent"), "Minor heirs receive a regency notice rather than an adult farewell conversation");
            r = Setup(); _inParty = false; Capture(r); Resolve(r); _dead = true; Tick();
            check(Inquiries.Single().Text.StartsWith("Word reaches you") && !Inquiries.Single().Text.Contains("I now lead")
                && Inquiries.Single().Text.Contains("establishing a new household, Pethros II."),
                "Late notice uses historical wording instead of making a dead former ruler speak");
            r = Setup(); _inParty = false; Capture(r); Resolve(r); r.TitleTransferred = false; Tick();
            check(Inquiries.Count == 0 && r.PlayerDepartureExplained,
                "A terminated accession without a transferred Crown cannot publish a false successful departure");

            foreach (string exclusion in new[] { "completed", "ordinary", "concession", "regent", "emergency", "player", "leader", "foreign" })
            {
                r = Setup();
                if (exclusion == "completed") r.Completed = true;
                if (exclusion == "ordinary") r.CreatesCadet = false;
                if (exclusion == "concession") r.ChallengeConcession = true;
                if (exclusion == "regent") r.RegentReplacement = true;
                if (exclusion == "emergency") r.Emergency = true;
                if (exclusion == "player") r.Heir = _player;
                if (exclusion == "leader") _headIsHeir = true;
                if (exclusion == "foreign") r.IncomingSourceHouse = _cadet;
                Capture(r); Tick();
                check(!r.PlayerDepartureCaptured && _opens == 0 && Inquiries.Count == 0,
                    "No farewell is fabricated for an unrelated or historical transition: " + exclusion);
            }
            r = Setup(); r.Cadet = _cadet; r.CadetInitialized = true; _heirClan = _cadet; Capture(r);
            check(r.PlayerDepartureCaptured && !r.PlayerDepartureWantsDialogue,
                "An older partially transferred accession receives a letter, never a pre-departure dialogue or replayed transfer");
            r = Setup(); Capture(r); r.PlayerDepartureObserver = Blank<Hero>(); Tick();
            check(r.PlayerDepartureExplained && !r.PlayerDepartureWantsDialogue && _opens == 0,
                "Changing the player character retires a farewell addressed to the old player without trapping succession");
            r = Setup(); Capture(r); Call(behavior, "ResetPlayerCrownDeparture", r);
            check(!r.PlayerDepartureCaptured && r.PlayerDepartureHero == null && r.PlayerDepartureAddress == null,
                "Replanning an uncommitted household clears stale departure context");

            foreach (string relation in new[] { "wife", "mother", "father", "daughter", "son", "sister", "brother", "name" })
            {
                r = Setup(); _married = relation == "wife"; _femalePlayer = relation == "wife" || relation == "daughter" || relation == "sister";
                if (relation == "mother") _heirMother = _player;
                if (relation == "father") _heirFather = _player;
                if (relation == "daughter" || relation == "son") _playerMother = _heir;
                if (relation == "sister" || relation == "brother") _heirMother = _playerMother = Blank<Hero>();
                Capture(r);
                string expected = relation == "name" ? "David" : relation == "wife" ? "My wife"
                    : relation == "daughter" ? "My daughter" : relation == "son" ? "My son"
                    : char.ToUpperInvariant(relation[0]) + relation.Substring(1);
                check(r.PlayerDepartureAddress == expected, "Relationship-appropriate address is captured before the clan split: " + relation);
            }
            var fields = typeof(CrownAccessionRecord).GetFields().Select(field => field.GetCustomAttributesData()
                .SingleOrDefault(attribute => attribute.AttributeType.Name == "SaveableFieldAttribute"))
                .Where(attribute => attribute != null).Select(attribute => Convert.ToInt32(attribute.ConstructorArguments[0].Value)).ToArray();
            check(fields.Length == 92 && fields.Distinct().Count() == 92 && fields.Min() == 1 && fields.Max() == 92,
                "Departure receipts append unique save fields after the original eighty accession fields");
        }
        finally { harmony.UnpatchAll(harmony.Id); ticksPerDay.SetValue(null, previousTicksPerDay); _campaign = null; }
    }
}
