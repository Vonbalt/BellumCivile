using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapNotificationTypes;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class TitleAcquisitionTests
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
        public bool SyncData<T>(string key, ref T value)
        {
            if (IsLoading) { if (Values.TryGetValue(key, out var saved)) value = (T)saved; }
            else Values[key] = value;
            return true;
        }
    }

    private static Hero _main;
    private static Clan _player;
    private static bool _blocked;
    private static bool MainHero(ref Hero __result) { __result = _main; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Name(Hero __instance, ref TextObject __result) { __result = new TextObject("{=!}" + __instance.StringId); return false; }
    private static bool TitleName(FeudalTitleRecord __0, ref string __result) { __result = "Formatted " + __0.Name; return false; }
    private static bool Block(ref bool __result) { __result = _blocked; return false; }
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(Type type, string method, object instance, params object[] args)
        => AccessTools.Method(type, method).Invoke(instance, args);
    private static void Field(object instance, string name, object value) => AccessTools.Field(instance.GetType(), name).SetValue(instance, value);

    internal static void Run(Action<bool, string> check)
    {
        var oldCampaign = Campaign.Current;
        var oldTitles = FeudalTitleBehavior.Instance;
        var harmony = new Harmony("bellum.tests.title_acquisitions");
        var patch = typeof(FeudalTitleRecord).Assembly.GetType("BellumCivile.Patches.TitleAcquisitionNotificationPatch", true);
        GameTexts.Initialize(new GameTextManager());
        try
        {
            void Patch(MethodBase method, string prefix) => harmony.Patch(method, prefix: new HarmonyMethod(typeof(TitleAcquisitionTests), prefix));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), nameof(MainHero));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Name"), nameof(Name));
            Patch(AccessTools.Method(typeof(FeudalTitleRecord).Assembly.GetType("BellumCivile.FeudalTitleDisplayHelper", true),
                "FormatTitleName", new[] { typeof(FeudalTitleRecord), typeof(Clan) }), nameof(TitleName));
            Patch(AccessTools.Method(typeof(CivilWarConflictBehavior), "IsRealmTransferPending"), nameof(Block));
            _player = Blank<Clan>(); _player.StringId = "player_house";
            _main = Blank<Hero>(); _main.StringId = "Player";
            Field(_main, "_clan", _player); Field(_main, "_heroState", Hero.CharacterStates.Active); Field(_player, "_leader", _main);
            var campaign = Blank<Campaign>();
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            var manager = new Manager(); campaign.AddCampaignBehaviorManager(manager);
            var titles = new FeudalTitleBehavior(); var behavior = new TitleAcquisitionBehavior();
            manager.AddBehavior(titles); manager.AddBehavior(behavior);
            AccessTools.PropertySetter(typeof(FeudalTitleBehavior), "Instance").Invoke(null, new object[] { titles });
            var information = new CampaignInformationManager();
            Field(campaign, "<CampaignInformationManager>k__BackingField", information);
            var all = (Dictionary<string, FeudalTitleRecord>)AccessTools.Field(typeof(FeudalTitleBehavior), "_titlesById").GetValue(titles);
            var pending = (List<TitleAcquiredMapNotification>)AccessTools.Field(typeof(TitleAcquisitionBehavior), "_pending").GetValue(behavior);
            var notices = (List<InformationData>)AccessTools.Field(typeof(CampaignInformationManager), "_mapNotices").GetValue(information);
            void Acquire(FeudalTitleRecord t, string legal = "other", string actual = "other", Clan clan = null)
                => Call(typeof(TitleAcquisitionBehavior), "Acquired", null, clan ?? _player, t, legal, actual);
            void Process() => Call(typeof(TitleAcquisitionBehavior), "Process", behavior);
            FeudalTitleRecord Title(FeudalTitleType rank, string id)
            {
                var t = new FeudalTitleRecord(id, id, rank, _player.StringId, _player.StringId, "", id + "_capital", "", 0, 0);
                all.Add(id, t); return t;
            }
            var county = Title(FeudalTitleType.County, "County");
            Acquire(county);
            check(pending.Count == 0 && notices.Count == 0, "Loading or initializing existing holdings does not announce acquisitions");
            Field(behavior, "_ready", true);
            foreach (var rank in new[] { FeudalTitleType.Barony, FeudalTitleType.County, FeudalTitleType.Duchy, FeudalTitleType.Kingdom, FeudalTitleType.Empire })
            {
                var t = Title(rank, rank + "_new"); Acquire(t); Acquire(t);
                check(pending.Count == 1 && pending[0].Rights == 3, "Repeated callbacks coalesce for new " + rank);
                Process();
                check(notices.Count == 1 && notices[0] is TitleAcquiredMapNotification n && n.IsValid()
                    && n.DescriptionText.ToString().Contains("Formatted " + t.Name)
                    && n.DescriptionText.ToString().Contains("Player"), "The round notice uses the actual formatted title and recipient: " + rank);
                Acquire(t); Process();
                check(notices.Count == 1, "Repeated delivery does not duplicate an unread " + rank + " notice");
                notices.Clear(); Acquire(t, _player.StringId, _player.StringId); Process();
                check(notices.Count == 0, "Unchanged rights cannot recreate a dismissed " + rank + " notice");
            }
            county.SetDeJureHolder("other"); Acquire(county); Process();
            var partial = (TitleAcquiredMapNotification)notices.Single();
            county.SetDeJureHolder(_player.StringId); Acquire(county, "other", _player.StringId); Process();
            check(notices.Count == 1 && notices[0] == partial && partial.Rights == 3,
                "Separate practical and lawful delivery stages update one unread notice instead of adding another bubble");
            notices.Clear();
            var npc = Blank<Clan>(); npc.StringId = "npc_house";
            Acquire(county, clan: npc);
            check(pending.Count == 0, "NPC acquisitions do not produce player round notifications");
            var castle = Title(FeudalTitleType.Barony, "castle");
            var settlement = Blank<Settlement>(); settlement.StringId = castle.CapitalSettlementId;
            var native = new SettlementOwnerChangedMapNotification(settlement, _main, null, new TextObject("Native ownership notice"));
            information.NewMapNoticeAdded(native);
            Call(typeof(TitleAcquisitionBehavior), "SettlementChanged", behavior, settlement, false, _main, null, null,
                ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByKingDecision);
            Acquire(castle); Process();
            check(notices.Count == 1 && notices[0] == native, "An actual town or castle transfer keeps only the native ownership notice");
            Acquire(castle, "other", _player.StringId); Process();
            check(notices.Count == 2 && notices[1] is TitleAcquiredMapNotification legalNotice && legalNotice.Rights == 1,
                "A later legal barony usurpation still announces even if an older native notice remains unread");
            notices.Clear(); Acquire(castle);
            Call(typeof(TitleAcquisitionBehavior), "SettlementChanged", behavior, settlement, false, _main, null, null,
                ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByKingDecision);
            _blocked = true; Process();
            check(pending.Count == 1 && pending[0].NativeCovered && notices.Count == 0,
                "A native notice arriving after the title callback remains recorded during a deferred transfer");
            var store = new Store(); behavior.SyncData(store); store.IsLoading = true;
            var loaded = new TitleAcquisitionBehavior(); loaded.SyncData(store);
            var saved = (List<TitleAcquiredMapNotification>)AccessTools.Field(typeof(TitleAcquisitionBehavior), "_pending").GetValue(loaded);
            check(saved.Count == 1 && saved[0].NativeCovered && !(bool)AccessTools.Field(typeof(TitleAcquisitionBehavior), "_ready").GetValue(loaded),
                "Pending notices retain deduplication state and wait for session readiness after a data-store round trip");
            _blocked = false; Process(); check(notices.Count == 0 && pending.Count == 0, "A deferred native-covered barony cannot announce twice");
            Acquire(county); _blocked = true; Process();
            check(pending.Count == 1 && notices.Count == 0, "An unfinished civil-war transfer delays the notice");
            _blocked = false; Process(); check(pending.Count == 0 && notices.Count == 1, "The notice appears after the transfer finishes");
            var stale = (TitleAcquiredMapNotification)notices[0];
            county.SetDeJureHolder("other"); county.SetDeFactoHolder("other");
            check(!stale.IsValid(), "Losing the acquired title invalidates the notice");
            notices.Clear(); Acquire(county); Process();
            check(notices.Count == 0, "A failed or rolled-back acquisition does not announce success");
            county.SetDeJureHolder(_player.StringId); county.SetDeFactoHolder(_player.StringId);
            Acquire(county); Process(); check(notices.Count == 1, "A genuine later reacquisition may announce again");
            _main = Blank<Hero>(); _main.StringId = "DifferentPlayer";
            check(!notices[0].IsValid(), "Changing the player character invalidates the former holder's notice");
            _main = stale.Recipient; notices.Clear();
            var realm = Blank<Kingdom>(); realm.StringId = "new_realm";
            Field(realm, "_rulingClan", _player); Field(_player, "_kingdom", realm);
            var crown = Title(FeudalTitleType.Kingdom, "Crown");
            ((Dictionary<string, string>)AccessTools.Field(typeof(FeudalTitleBehavior), "_currentRealmTitleByKingdomId").GetValue(titles))
                .Add(realm.StringId, crown.TitleId);
            Call(typeof(TitleAcquisitionBehavior), "RealmCreated", behavior, realm);
            Call(typeof(TitleAcquisitionBehavior), "RealmCreated", behavior, realm);
            Process();
            check(notices.Count == 1 && ((TitleAcquiredMapNotification)notices[0]).TitleId == crown.TitleId,
                "A newly formed permanent realm announces its final sovereign title once");
            notices.Clear();
            Call(typeof(TitleAcquisitionBehavior), "Acceded", null, realm, _main, _main); Process();
            check(notices.Count == 0, "Reelecting the same ruler is not a new title acquisition");
            var predecessor = Blank<Hero>(); predecessor.StringId = "PreviousRuler";
            Call(typeof(TitleAcquisitionBehavior), "Acceded", null, realm, predecessor, _main); Process();
            check(notices.Count == 1, "A completed accession announces the player's new Crown");
            notices.Clear();
            var temporary = Blank<Kingdom>(); temporary.StringId = "bc_feud_test"; Field(temporary, "_rulingClan", _player);
            Call(typeof(TitleAcquisitionBehavior), "RealmCreated", behavior, temporary); Process();
            check(notices.Count == 0, "Creating a temporary private-war realm does not announce a sovereign title");
            ((Dictionary<string, List<string>>)AccessTools.Field(typeof(FeudalTitleBehavior), "_titlesByDeJureHolder").GetValue(titles))
                .Add(_player.StringId, new List<string> { county.TitleId });
            var previousPlayer = _main;
            _main = Blank<Hero>(); _main.StringId = "Heir";
            Field(_main, "_clan", _player); Field(_main, "_heroState", Hero.CharacterStates.Active); Field(_player, "_leader", _main);
            Call(typeof(TitleAcquisitionBehavior), "PlayerChanged", behavior, previousPlayer, _main, null, false); Process();
            check(notices.Count == 1 && ((TitleAcquiredMapNotification)notices[0]).Recipient == _main,
                "Taking over the same household as player heir announces its inherited titles");
            notices.Clear(); _main = previousPlayer; Field(_player, "_leader", _main);
            var oldSave = new TitleAcquisitionBehavior(); oldSave.SyncData(new Store { IsLoading = true });
            check(((ICollection)AccessTools.Field(typeof(TitleAcquisitionBehavior), "_pending").GetValue(oldSave)).Count == 0,
                "An older save with no notification keys starts with an empty queue rather than announcing existing holdings");
            Call(patch, "Initialize", null); Call(patch, "Initialize", null);
            var mapType = typeof(TitleAcquisitionBehavior).Assembly.GetReferencedAssemblies().Select(Assembly.Load)
                .Select(a => a.GetType("TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapNotificationVM")).First(t => t != null);
            var map = Activator.CreateInstance(mapType, new object[] { null, null });
            try
            {
                var mappings = (IDictionary)AccessTools.Field(mapType, "_itemConstructors").GetValue(map);
                check(mappings.Contains(typeof(TitleAcquiredMapNotification)) && mappings.Contains(typeof(SettlementOwnerChangedMapNotification)),
                    "The real map-notification VM registers Bellum notices without replacing the native settlement mapping");
                Acquire(county); Process();
                var items = (IEnumerable)AccessTools.Property(mapType, "NotificationItems").GetValue(map);
                var item = items.Cast<object>().Single();
                check((string)AccessTools.Property(item.GetType(), "NotificationIdentifier").GetValue(item) == "settlementownerchanged"
                    && (string)AccessTools.Property(item.GetType(), "SoundId").GetValue(item) == "event:/ui/notification/settlement_owner_change",
                    "The native display pipeline uses the ownership icon and sound for Bellum's notification");
                county.SetDeJureHolder("other"); county.SetDeFactoHolder("other");
                Call(mapType, "OnFrameTick", map, 0f);
                check(!items.Cast<object>().Any(), "The native map refresh automatically removes an obsolete Bellum notice");
            }
            finally { Call(mapType, "OnFinalize", map); }
            CheckHooks(check);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            new Harmony("com.bellumcivile.title.notifications").UnpatchAll("com.bellumcivile.title.notifications");
            AccessTools.Field(patch, "_installed").SetValue(null, false);
            AccessTools.PropertySetter(typeof(FeudalTitleBehavior), "Instance").Invoke(null, new object[] { oldTitles });
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { oldCampaign });
            _blocked = false;
        }
    }

    private static void CheckHooks(Action<bool, string> check)
    {
        foreach (var method in new[] { "TryFormTitle", "LegalizePartitionInheritance", "LegalizeTitleInheritance", "CompleteCourtTitleGrant",
            "TryGrantTitleByRuler", "TryResolveClaimFeudForClaimant", "TryResolveClaimFeudForHolder", "TrySetKingdomTitleRuler" })
        {
            bool hooked = typeof(FeudalTitleBehavior).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == method).Any(m => PatchProcessor.GetOriginalInstructions(m).Any(i =>
                    i.operand is MethodInfo call && call.DeclaringType == typeof(TitleAcquisitionBehavior) && call.Name == "Acquired"));
            check(hooked, "Committed acquisition path queues the shared notification: " + method);
        }
        foreach (var source in new[] { Tuple.Create(typeof(LordshipBehavior), "TryGrantVassal", "Granted"),
            Tuple.Create(typeof(FeudalTitlePlayerActionService), "TryExecuteUsurpation", "Acquired"),
            Tuple.Create(typeof(CrownAccessionBehavior), "Finish", "Acceded") })
            check(source.Item1.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == source.Item2).Any(m => PatchProcessor.GetOriginalInstructions(m).Any(i =>
                    i.operand is MethodInfo call && call.DeclaringType == typeof(TitleAcquisitionBehavior) && call.Name == source.Item3)),
                "Finalized acquisition path includes its notice: " + source.Item1.Name + "." + source.Item2);
        foreach (var method in new[] { "SetDeJureHolder", "SetDeFactoHolder" })
            check(!PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(FeudalTitleRecord), method))
                .Any(i => i.operand is MethodInfo call && call.DeclaringType == typeof(TitleAcquisitionBehavior)),
                "Registry setter remains free of notification side effects: " + method);
    }
}
