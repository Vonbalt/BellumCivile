using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

internal static class ClientVoluntaryReleaseTests
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
        public bool IsSaving { get; set; }
        public bool IsLoading => !IsSaving;
        public bool SyncData<T>(string key, ref T value)
        {
            if (!IsSaving) { value = Data.TryGetValue(key, out var saved) ? (T)saved : default; return true; }
            if (value is IList list)
            {
                var copy = (IList)Activator.CreateInstance(value.GetType());
                foreach (object item in list)
                    copy.Add(item is RelationMemoryRecord m ? new RelationMemoryRecord(m.Scope, m.FirstId, m.SecondId,
                        m.SourceId, m.ContextText, m.Value, m.StartDay, m.ExpiryDay, m.LegacyWeeklyDecay, m.EventId) : item);
                Data[key] = copy;
            }
            else Data[key] = value;
            return true;
        }
    }
    private sealed class Line
    {
        internal string Output;
        internal Delegate Condition, Consequence, Clickable;
    }
    private static readonly Dictionary<string, Line> Lines = new Dictionary<string, Line>();
    private static readonly Dictionary<string, TextObject> Variables = new Dictionary<string, TextObject>();
    private static readonly Type Clients = typeof(ClientKingdomBehavior);
    private static readonly Type Adapter = Clients.Assembly.GetType("BellumCivile.ClientReleaseAgreements", true);
    private static Kingdom _client, _overlord;
    private static Clan _playerHouse;
    private static Hero _player, _recipient, _speaker;
    private static DynamicRelationBehavior _relations;
    private static double _day;
    private static float _multiplier;
    private static bool _war;
    private static int _rewards;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(Type type, string method, object target, params object[] args) => AccessTools.Method(type, method).Invoke(target, args);
    private static void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
    private static bool Player(ref Hero __result) { __result = _player; return false; }
    private static bool House(ref Clan __result) { __result = _playerHouse; return false; }
    private static bool Speaker(ref Hero __result) { __result = _speaker; return false; }
    private static bool ResolveHero(string heroId, ref Hero __result)
    { __result = heroId == _player?.StringId ? _player : heroId == _recipient?.StringId ? _recipient : null; return false; }
    private static bool Name(Kingdom __instance, ref TextObject __result) { __result = new TextObject("{=!}" + __instance.StringId); return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days((float)_day); return false; }
    private static bool Past(CampaignTime __instance, ref bool __result) { __result = __instance.ToDays <= _day; return false; }
    private static bool Year(ref int __result) { __result = 84; return false; }
    private static bool AllianceDuration(ref CampaignTime __result) { __result = CampaignTime.Days(84); return false; }
    private static bool TradeDuration(ref CampaignTime __result) { __result = CampaignTime.Days(42); return false; }
    private static bool Multiplier(ref float __result) { __result = _multiplier; return false; }
    private static bool War(ref bool __result) { __result = _war; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Skip() => false;
    private static bool CaptureText(string __0, TextObject __1) { Variables[__0] = __1; return false; }
    private static bool Register(object[] __args, ref ConversationSentence __result)
    {
        Lines[(string)__args[0]] = new Line { Output = (string)__args[2], Condition = __args[4] as Delegate,
            Consequence = __args[5] as Delegate, Clickable = __args.Length > 7 ? __args[7] as Delegate : null };
        __result = null; return false;
    }
    private static bool Reward(Hero __0, Hero __1, int __2)
    {
        _rewards++;
        var descriptor = AccessTools.Property(typeof(RelationMemoryService), "CurrentDescriptor").GetValue(null);
        Call(typeof(DynamicRelationBehavior), "AddMemoryForChange", _relations, __0, __1, __2, descriptor);
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.tests.voluntary-client-release");
        var oldCampaign = Campaign.Current; var oldClients = ClientKingdomBehavior.Instance;
        var oldRelations = DynamicRelationBehavior.Instance;
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay"); var oldTicks = ticks.GetValue(null);
        void Patch(Type type, string member, string handler, bool getter = false)
        {
            var method = getter ? AccessTools.PropertyGetter(type, member) : AccessTools.Method(type, member);
            if (method == null) throw new MissingMethodException(type.FullName, member);
            h.Patch(method, prefix: new HarmonyMethod(typeof(ClientVoluntaryReleaseTests), handler));
        }
        void Instance(Type type, object instance) => AccessTools.PropertySetter(type, "Instance").Invoke(null, new[] { instance });
        try
        {
            Patch(typeof(Hero), "MainHero", nameof(Player), true); Patch(typeof(Clan), "PlayerClan", nameof(House), true);
            Patch(typeof(Hero), "OneToOneConversationHero", nameof(Speaker), true);
            Patch(typeof(Kingdom), "Name", nameof(Name), true); Patch(typeof(Kingdom), "IsEliminated", nameof(No), true);
            Patch(typeof(Kingdom), "IsAtWarWith", nameof(War)); Patch(typeof(CampaignTime), "Now", nameof(Now), true);
            Patch(typeof(CampaignTime), "DaysInYear", nameof(Year), true);
            Patch(typeof(CampaignTime), "IsPast", nameof(Past), true);
            Patch(typeof(DefaultAllianceModel), "MaxDurationOfAlliance", nameof(AllianceDuration), true);
            Patch(typeof(DefaultTradeAgreementModel), "GetTradeAgreementDurationInYears", nameof(TradeDuration));
            Patch(typeof(BellumCivileLogger), "Log", nameof(Skip));
            var options = Clients.Assembly.GetType("BellumCivile.BellumCivileOptions");
            Patch(options, "EnableDynamicRelationDrift", nameof(Yes), true);
            Patch(options, "RelationMemoryDurationMultiplier", nameof(Multiplier), true);
            Patch(typeof(DynamicRelationBehavior), "ShouldAffectPair", nameof(Yes));
            Patch(typeof(DynamicRelationBehavior), "ResolveHero", nameof(ResolveHero));
            Patch(typeof(DynamicRelationBehavior), "RebuildObjectResolutionCaches", nameof(Skip));
            Patch(typeof(ChangeRelationAction), "ApplyRelationChangeBetweenHeroes", nameof(Reward));
            foreach (string name in new[] { "AddPlayerLine", "AddDialogLine" })
                foreach (var method in AccessTools.GetDeclaredMethods(typeof(CampaignGameStarter)).Where(m => m.Name == name))
                    h.Patch(method, prefix: new HarmonyMethod(typeof(ClientVoluntaryReleaseTests), nameof(Register)));
            h.Patch(AccessTools.Method(typeof(MBTextManager), "SetTextVariable", new[] { typeof(string), typeof(TextObject), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(ClientVoluntaryReleaseTests), nameof(CaptureText)));
            ticks.SetValue(null, 24000L); _multiplier = 1;
            var campaign = Blank<Campaign>(); var models = Blank<GameModels>();
            Set(campaign, "CurrentConversationContext", ConversationContext.Default);
            AccessTools.PropertySetter(typeof(GameModels), "AllianceModel").Invoke(models, new object[] { new DefaultAllianceModel() });
            AccessTools.PropertySetter(typeof(GameModels), "TradeAgreementModel").Invoke(models, new object[] { new DefaultTradeAgreementModel() });
            Set(campaign, "_gameModels", models);
            var manager = new Manager(); campaign.AddCampaignBehaviorManager(manager);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            Hero Ruler(string id, out Clan clan, out Kingdom realm)
            {
                realm = Blank<Kingdom>(); realm.StringId = id;
                clan = Blank<Clan>(); clan.StringId = id + "_house";
                var hero = Blank<Hero>(); hero.StringId = id + "_ruler";
                Set(hero, "_heroState", Hero.CharacterStates.Active); Set(hero, "_clan", clan);
                Set(clan, "_leader", hero); Set(clan, "_kingdom", realm); Set(realm, "_rulingClan", clan);
                return hero;
            }
            _player = Ruler("overlord", out _playerHouse, out _overlord);
            AccessTools.PropertySetter(typeof(Clan), "IsMinorFaction").Invoke(_playerHouse, new object[] { true });
            _recipient = Ruler("client", out var clientHouse, out _client);
            var stranger = Ruler("stranger", out var strangerHouse, out var foreign);
            _speaker = _recipient;
            var clients = new ClientKingdomBehavior(); Instance(Clients, clients);
            _relations = new DynamicRelationBehavior(); Instance(typeof(DynamicRelationBehavior), _relations);
            Set(_relations, "_campaignThreadId", Thread.CurrentThread.ManagedThreadId);
            Set(_relations, "_memoryDurationReady", true); Set(_relations, "_appliedMemoryDurationMultiplier", 1f);
            var alliances = new AllianceCampaignBehavior(); var trade = new TradeAgreementsCampaignBehavior();
            manager.Items.AddRange(new CampaignBehaviorBase[] { clients, alliances, trade, _relations });
            var records = (List<ClientKingdomRecord>)AccessTools.Field(Clients, "_clients").GetValue(clients);
            var allianceRows = (IList)AccessTools.Field(typeof(AllianceCampaignBehavior), "_alliances").GetValue(alliances);
            var calls = (IList)AccessTools.Field(typeof(AllianceCampaignBehavior), "_callToWarAgreements").GetValue(alliances);
            var trades = (List<TradeAgreementsCampaignBehavior.TradeAgreement>)AccessTools.Field(typeof(TradeAgreementsCampaignBehavior), "_tradeAgreements").GetValue(trade);
            var memories = (List<RelationMemoryRecord>)AccessTools.Field(typeof(DynamicRelationBehavior), "_relationMemories").GetValue(_relations);
            object NativeRow(IList list, params object[] args) => Activator.CreateInstance(list.GetType().GetGenericArguments()[0],
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, null);
            void Reset(bool resetMemory = true)
            {
                _day = 10; _war = false; _speaker = _recipient;
                Set(_playerHouse, "_leader", _player); Set(_playerHouse, "_kingdom", _overlord);
                Set(clientHouse, "_leader", _recipient); Set(_overlord, "_rulingClan", _playerHouse);
                Set(_player, "_heroState", Hero.CharacterStates.Active); Set(_recipient, "_heroState", Hero.CharacterStates.Active);
                records.Clear(); records.Add(new ClientKingdomRecord("client", "overlord", 0, false, 100));
                allianceRows.Clear(); calls.Clear(); trades.Clear();
                allianceRows.Add(NativeRow(allianceRows, _client, _overlord, CampaignTime.Days(15)));
                calls.Add(NativeRow(calls, _overlord, _client, foreign, CampaignTime.Days(60)));
                trades.Add(new TradeAgreementsCampaignBehavior.TradeAgreement(_client, _overlord, CampaignTime.Years(100))
                { Kingdom1GoldGained = 17, Kingdom2GoldGained = 29, Kingdom1GoldGainedTotal = 100, Kingdom2GoldGainedTotal = 200 });
                if (resetMemory) { memories.Clear(); Call(typeof(DynamicRelationBehavior), "RebuildMemoryIndexes", _relations); _rewards = 0; }
                Call(Clients, "ClearReleaseOffer", clients);
            }
            object Offer(Hero recipient = null) => Call(Clients, "CreateReleaseOffer", clients, _player, recipient ?? _recipient);
            bool Release(object offer) => (bool)Call(Clients, "TryVoluntarilyRelease", clients, offer, null);
            bool Prepare(out object plan)
            { object[] args = { _overlord, _client, null }; bool result = (bool)Call(Adapter, "TryPrepare", null, args); plan = args[2]; return result; }
            double AllianceEnd() => alliances.GetAllianceEndDate(_client, _overlord).ToDays;
            void Open() => Lines["bc_client_release_open"].Consequence.DynamicInvoke();
            Reset();
            Call(Clients, "AddVoluntaryReleaseDialogues", clients, Blank<CampaignGameStarter>());
            check((bool)Lines["bc_client_release_open"].Condition.DynamicInvoke(), "A player suzerain can offer independence to their actual client ruler");
            foreach (string state in new[] { "bc_client_release_proposal", "bc_client_release_confirmation" })
            {
                Open(); Lines[state + "_cancel"].Consequence.DynamicInvoke();
                check(records.Count == 1 && AllianceEnd() == 15 && _rewards == 0 && Lines[state + "_cancel"].Output == "lord_talk_speak_diplomacy_2",
                    "Cancellation returns to normal conversation without mutation: " + state);
            }
            check(Offer(stranger) == null && Call(Clients, "CreateReleaseOffer", clients, stranger, _recipient) == null,
                "Neither unrelated rulers nor NPC suzerains receive the player-only release action");
            Reset(); records.Clear(); records.Add(new ClientKingdomRecord("client", "stranger", 0, true, 100));
            check(Call(Clients, "CreateReleaseOffer", clients, stranger, _recipient) == null,
                "Even a legitimate NPC suzerain cannot initiate this player-only release");
            Reset(); Open(); _speaker = stranger;
            Lines["bc_client_release_confirm"].Consequence.DynamicInvoke();
            check(records.Count == 1 && AllianceEnd() == 15 && _rewards == 0,
                "Changing the conversation partner cannot confirm an earlier ruler's release");
            foreach (var invalid in new (string Name, Action Change)[] {
                ("player no longer ruler", () => Set(_overlord, "_rulingClan", strangerHouse)),
                ("recipient no longer ruler", () => Set(clientHouse, "_leader", stranger)),
                ("recipient imprisoned", () => Set(_recipient, "_heroState", Hero.CharacterStates.Prisoner)),
                ("recipient has died", () => Set(_recipient, "_heroState", Hero.CharacterStates.Dead)),
                ("player imprisoned", () => Set(_player, "_heroState", Hero.CharacterStates.Prisoner)),
                ("hostile realms", () => _war = true),
                ("replaced clientage record", () => { records.Clear(); records.Add(new ClientKingdomRecord("client", "overlord", 1, true, 101)); }),
                ("client belongs to another suzerain", () => { records.Clear(); records.Add(new ClientKingdomRecord("client", "stranger", 0, true, 100)); }) })
            {
                Reset(); var offer = Offer(); invalid.Change();
                check(!Release(offer) && records.Count == 1 && AllianceEnd() == 15 && _rewards == 0,
                    "Confirmation revalidates before altering any diplomacy: " + invalid.Name);
            }
            Reset(); var unchanged = Offer(); trades.Clear();
            check(!Release(unchanged) && records.Count == 1 && AllianceEnd() == 15, "A missing trade pact fails closed without granting independence");
            Reset(); unchanged = Offer(); allianceRows.Clear();
            check(!Release(unchanged) && records.Count == 1 && trades.Count == 1,
                "A missing alliance fails closed without discarding trade or clientage");
            Reset(); unchanged = Offer(); trades.Add(trades[0]);
            check(!Release(unchanged) && records.Count == 1 && AllianceEnd() == 15, "Duplicate pacts cannot receive a partial conversion");
            Reset(); unchanged = Offer(); allianceRows.Add(allianceRows[0]);
            check(!Release(unchanged) && records.Count == 1 && trades.Count == 1,
                "Duplicate alliances are rejected before either agreement is changed");
            Reset();
            check(Prepare(out var plan), "The installed native pact layouts support a bounded, in-place conversion");
            var changed = trades[0]; changed.Kingdom1GoldGained++;
            trades[0] = changed;
            check(!(bool)Call(Adapter, "TryApply", plan) && AllianceEnd() == 15 && records.Count == 1,
                "An agreement changed after preparation is not overwritten");
            Reset(); double protectedTradeEnd = trades[0].EndTime.ToDays; Prepare(out plan);
            check((bool)Call(Adapter, "TryApply", plan), "A prepared agreement conversion can be applied before clientage is committed");
            Call(Adapter, "Restore", plan);
            check(AllianceEnd() == 15 && trades[0].EndTime.ToDays == protectedTradeEnd && trades[0].Kingdom1GoldGained == 17 && records.Count == 1,
                "An uncommitted conversion can restore both original pact records without losing trade income");
            Reset(); allianceRows[0] = NativeRow(allianceRows, _client, _overlord, CampaignTime.Days(200));
            check(Release(Offer()) && AllianceEnd() == 200, "A legitimate longer alliance term is never shortened on release");
            Reset(); Ruler("other_client", out _, out var otherClient);
            records.Add(new ClientKingdomRecord("other_client", "overlord", 0, true, 100));
            allianceRows.Add(NativeRow(allianceRows, _overlord, otherClient, CampaignTime.Days(70)));
            trades.Add(new TradeAgreementsCampaignBehavior.TradeAgreement(_overlord, otherClient, CampaignTime.Days(75)));
            check(Release(Offer()) && clients.IsClientOf(otherClient, _overlord) && records.Count == 1
                && alliances.GetAllianceEndDate(_overlord, otherClient).ToDays == 70
                && trade.GetTradeAgreementEndDate(_overlord, otherClient).ToDays == 75,
                "Releasing one client leaves all other clients and their pact dates untouched");
            Reset(); Open();
            object[] hint = { null };
            check((bool)Lines["bc_client_release_confirm"].Clickable.DynamicInvoke(hint)
                && ((TextObject)hint[0]).ToString().Contains("15 years") && records.Count == 1,
                "The confirmation tooltip explains the memory and pacts without committing the release");
            object obligation = calls[0];
            Lines["bc_client_release_confirm"].Consequence.DynamicInvoke();
            check(records.Count == 0 && clients.RuntimeRevision > 0 && _rewards == 1,
                "Confirmation removes exactly the clientage and awards gratitude once");
            check(AllianceEnd() == 94 && trades[0].EndTime.ToDays == 52,
                "Normal alliance and trade terms begin at release using the active game models");
            check(trades[0].Kingdom1GoldGained == 17 && trades[0].Kingdom2GoldGained == 29
                && trades[0].Kingdom1GoldGainedTotal == 100 && trades[0].Kingdom2GoldGainedTotal == 200,
                "Conversion preserves both unpaid trade balances and lifetime income counters");
            check(calls.Count == 1 && calls[0].Equals(obligation) && allianceRows.Count == 1 && trades.Count == 1,
                "Existing call-to-war obligations survive without restarting or duplicating pacts");
            check(clients.CanEndAlliance(_client, _overlord) && clients.CanEndTradeAgreement(_client, _overlord),
                "After release both pacts are ordinary terminable agreements");
            check((bool)Lines["bc_client_release_thanks"].Condition.DynamicInvoke() && memories.Count == 1
                && memories[0].SourceId == RelationMemorySources.GrantedRealmIndependence && memories[0].Scope == RelationMemoryScope.Personal
                && memories[0].Value == 40 && memories[0].ExpiryDay == 10 + 15 * 84,
                "The grateful response follows success and records a fifteen-year personal forty-point favor: "
                    + string.Join(";", memories.Select(m => m.SourceId + "/" + m.Scope + "/" + m.Value + "/" + m.ExpiryDay)));
            Lines["bc_client_release_confirm"].Consequence.DynamicInvoke();
            check(_rewards == 1 && (bool)Lines["bc_client_release_thanks"].Condition.DynamicInvoke(),
                "Duplicate confirmation neither repeats rewards nor replaces success with a failure message");
            Lines["bc_client_release_thanks"].Consequence.DynamicInvoke();
            check(!(bool)Lines["bc_client_release_open"].Condition.DynamicInvoke(), "A released realm no longer offers the client-only dialogue");
            Call(Clients, "ReconcileClientKingdoms", clients);
            check(records.Count == 0 && trades[0].EndTime.ToDays == 52, "Maintenance cannot recreate released clientage or its hundred-year trade term");
            var store = new Store { IsSaving = true }; clients.SyncData(store); alliances.SyncData(store); trade.SyncData(store); _relations.SyncData(store);
            var loadedClients = new ClientKingdomBehavior(); var loadedAlliances = new AllianceCampaignBehavior(); var loadedTrade = new TradeAgreementsCampaignBehavior();
            store.IsSaving = false; loadedClients.SyncData(store); loadedAlliances.SyncData(store); loadedTrade.SyncData(store);
            Instance(Clients, clients);
            check(loadedClients.GetClientRecord(_client) == null && loadedAlliances.GetAllianceEndDate(_client, _overlord).ToDays == 94
                && loadedTrade.GetTradeAgreementEndDate(_client, _overlord).ToDays == 52,
                "Bellum and native save-data roundtrips retain independence and converted pact dates");
            var loadedRelations = new DynamicRelationBehavior(); loadedRelations.SyncData(store);
            Set(loadedRelations, "_campaignThreadId", Thread.CurrentThread.ManagedThreadId); Set(loadedRelations, "_memoryDurationReady", true);
            check((bool)Call(typeof(DynamicRelationBehavior), "HasVoluntaryReleaseGratitude", loadedRelations, _recipient, _player),
                "Personal gratitude and its non-stacking guard survive save/load in either ruler order");
            Instance(typeof(DynamicRelationBehavior), _relations);
            float expiry = memories[0].ExpiryDay;
            Reset(false); _day = 20;
            check(Release(Offer()) && _rewards == 1 && memories.Count == 1 && memories[0].ExpiryDay == expiry,
                "A subsequent release while gratitude is active cannot stack or renew its memory");
            _multiplier = 2;
            check((bool)Call(typeof(DynamicRelationBehavior), "HasVoluntaryReleaseGratitude", _relations, _player, _recipient)
                && memories[0].ExpiryDay == _day + (expiry - _day) * 2,
                "The existing MCM multiplier rescales remaining gratitude duration normally");
            _day = memories[0].ExpiryDay;
            check(!(bool)Call(typeof(DynamicRelationBehavior), "HasVoluntaryReleaseGratitude", _relations, _player, _recipient),
                "Expired gratitude does not permanently prohibit a later reward");
            _day = 53;
            check(!loadedTrade.HasTradeAgreement(_client, _overlord, out _), "Converted trade expires through its unmodified native lifecycle");
        }
        finally
        {
            h.UnpatchAll(h.Id); Instance(Clients, oldClients); Instance(typeof(DynamicRelationBehavior), oldRelations);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { oldCampaign }); ticks.SetValue(null, oldTicks);
            Lines.Clear(); Variables.Clear(); _client = _overlord = null; _playerHouse = null;
            _player = _recipient = _speaker = null; _relations = null;
        }
    }
}
