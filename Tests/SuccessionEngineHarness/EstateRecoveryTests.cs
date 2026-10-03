using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class EstateRecoveryTests
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
    private static Kingdom _realm;
    private static bool _eliminated;
    private static Hero _head, _dead, _successor, _treasury;
    private static Clan _house, _source;
    private static FeudalTitleRecord _title;
    private static int _legalizations;
    private static int _debits, _credits, _gold;
    private static bool All(ref MBReadOnlyList<Kingdom> __result)
    { __result = new MBReadOnlyList<Kingdom>(new List<Kingdom> { _realm }); return false; }
    private static bool Eliminated(ref bool __result) { __result = _eliminated; return false; }
    private static bool Alive(Hero __instance, ref bool __result) { __result = __instance != _dead; return false; }
    private static bool Head(Clan __instance, ref Hero __result)
    { __result = __instance == _source ? _treasury : _head; return false; }
    private static bool House(ref Clan __result) { __result = _house; return false; }
    private static bool Name(ref TextObject __result) { __result = new TextObject("Test house"); return false; }
    private static bool Title(ref FeudalTitleRecord __result) { __result = _title; return false; }
    private static bool Heirs(ref List<Hero> __result) { __result = new List<Hero> { _successor }; return false; }
    private static bool Laws(ref SuccessionLawSet __result) { __result = new SuccessionLawSet(); return false; }
    private static bool Heroes(ref MBReadOnlyList<Hero> __result)
    { __result = new MBReadOnlyList<Hero>(new List<Hero>()); return false; }
    private static bool Legalize(Clan __0, FeudalTitleRecord __1)
    { _legalizations++; __1.SetDeJureHolder(__0.StringId); return false; }
    private static bool NotFuture(ref bool __result) { __result = false; return false; }
    private static bool Skip() => false;
    private static bool Deliver(CrownAccessionRecord __0, ref bool __result)
    {
        if (!__0.GoldCredited)
        {
            if (!__0.GoldDebited) { _debits++; __0.DeliveredGold = __0.EndowmentGold; __0.GoldDebited = true; }
            _credits++; _gold += __0.DeliveredGold; __0.GoldCredited = true;
        }
        __result = true; return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var old = Campaign.Current;
        var h = new Harmony("bellum.test.estate_recovery");
        void Patch(MethodBase m, string prefix) => h.Patch(m, prefix: new HarmonyMethod(typeof(EstateRecoveryTests), prefix));
        var campaign = Blank<Campaign>();
        var type = typeof(PartitionSuccessionBehavior);
        var partition = new PartitionSuccessionBehavior();
        var titles = Blank<FeudalTitleBehavior>();
        var manager = new Manager(); manager.AddBehavior(titles);
        _realm = Blank<Kingdom>(); _realm.StringId = "estate_realm";
        var source = Blank<Clan>(); source.StringId = "estate_source";
        _source = source; _treasury = Blank<Hero>(); _treasury.StringId = "treasury_successor";
        _house = Blank<Clan>(); _house.StringId = "estate_recipient";
        _head = Blank<Hero>(); _head.StringId = "heir";
        _dead = Blank<Hero>(); _dead.StringId = "dead_heir";
        _successor = _head;
        _eliminated = false; _legalizations = 0;
        var record = new CrossClanEstateRecord { Source = source, Deceased = _dead, Realm = _realm,
            RealmCrownTitleId = "county_crown", GoldPrepared = true };
        CrossClanEstateShare Share() => new CrossClanEstateShare { Heir = _head, Recipient = _house,
            Titles = new List<string> { "county_crown" }, GoldPayment = new CrownAccessionRecord
            { GoldDebited = true, GoldCredited = true, DeliveredGold = 123 } };
        void Settle(CrossClanEstateShare s) => AccessTools.Method(type, "SettleCrossClanShare")
            .Invoke(partition, new object[] { record, s, titles, null });
        try
        {
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            check(CourtAgendaBehavior.Current == null, "Startup: non-null Campaign with no behavior manager is safe");
            var finalizer = AccessTools.Method(type.Assembly.GetType("BellumCivile.Patches.ClientLiberationWarCommitPatch"), "Finalizer");
            var nativeFailure = new InvalidOperationException("native failure");
            check(ReferenceEquals(finalizer.Invoke(null, new object[] { null, null, nativeFailure }), nativeFailure),
                "Startup liberation finalizer preserves original exceptions without an agenda manager");
            campaign.AddCampaignBehaviorManager(manager);
            check(CourtAgendaBehavior.Current == null, "Startup: manager without agenda is safe");
            var agenda = new CourtAgendaBehavior(); manager.AddBehavior(agenda);
            check(CourtAgendaBehavior.Current == agenda, "Startup: initialized agenda lookup retains behavior");
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(All));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(Eliminated));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Alive));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(House));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Name"), nameof(Name));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Name"), nameof(Name));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Head));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Heroes"), nameof(Heroes));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "IsFuture"), nameof(NotFuture));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetKingdomPoliticalTitle"), nameof(Title));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetTitle"), nameof(Title));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "LegalizeTitleInheritance",
                new[] { typeof(Clan), typeof(FeudalTitleRecord), typeof(string), typeof(bool) }), nameof(Legalize));
            Patch(AccessTools.Method(type, "OrderEstateHeirs"), nameof(Heirs));
            Patch(AccessTools.Method(typeof(CrownAccessionBehavior), "DeliverAbdicationGold"), nameof(Deliver));
            Patch(AccessTools.Method(type.Assembly.GetType("BellumCivile.SuccessionLawHelper"), "GetLawsForClan", new[] { typeof(Clan) }), nameof(Laws));
            foreach (var m in type.Assembly.GetType("BellumCivile.BellumCivileNotifications").GetMethods()
                .Where(m => m.Name == "Show")) Patch(m, nameof(Skip));
            _title = new FeudalTitleRecord("county_crown", "County", FeudalTitleType.County,
                source.StringId, source.StringId, "", "", "estate_realm", 0, 0);
            var share = Share(); share.Heir = _dead; share.SupersededTitles = null; share.SupersededFiefs = null;
            Settle(share);
            check(share.Completed && share.LandedSettled && _legalizations == 0,
                "Legacy dead heir completes delivered estate without inheriting an elective county Crown");
            check(share.SupersededTitles.SequenceEqual(new[] { "county_crown" }) && share.DeliveredTitles.Count == 0,
                "Political Crown handoff has a distinct receipt, not a forged property delivery");
            Settle(share);
            check(share.GoldPayment.DeliveredGold == 123 && share.GoldPayment.GoldCredited && _legalizations == 0,
                "Repeated recovery preserves paid gold and does not transfer the Crown");
            _eliminated = true;
            share = Share(); Settle(share);
            check(share.Completed && _legalizations == 1 && _title.DeJureHolderClanId == _house.StringId,
                "Dissolved realm's surviving county settles as ordinary property");
            share = Share(); share.Recipient = source;
            _title.SetDeJureHolder(source.StringId); Settle(share);
            check(share.Completed && share.Recipient == _house && share.BeneficiaryReconciled,
                "Pending property follows a moved heir without requiring the old household");
            share = Share(); share.Heir = _dead;
            _title.SetDeJureHolder(source.StringId); Settle(share);
            check(share.Completed && share.Heir == _successor && share.BeneficiaryReconciled,
                "Outstanding share follows the selected lawful successor of a dead heir");
            share = Share(); _title.SetDeJureHolder("lawful_third_house");
            int before = _legalizations; Settle(share);
            check(share.Completed && _legalizations == before && share.SupersededTitles.Contains("county_crown"),
                "Recovery does not seize a title already legally transferred to a third house");
            share = Share(); share.GoldPayment.GoldCredited = false;
            check(!(bool)AccessTools.Method(type, "EstateDeliveryComplete").Invoke(null, new object[] { share }),
                "Uncredited gold never counts as completed inheritance");
            share.Titles.Clear(); share.GoldPayment.DeliveredGold = 70;
            share.GoldPayment.GoldRecipient = _dead; share.GoldPayment.IncomingSourceHead = _dead;
            _debits = _credits = _gold = 0;
            Settle(share);
            check(share.Completed && _debits == 0 && _credits == 1 && _gold == 70
                && share.GoldPayment.GoldRecipient == _head,
                "Interrupted gold credit follows living recipient without a second debit");
            var restored = new CrossClanEstateShare();
            foreach (var f in typeof(CrossClanEstateShare).GetFields())
            {
                object value = f.GetValue(share);
                if (value is List<string> list) value = new List<string>(list);
                f.SetValue(restored, value);
            }
            restored.Completed = false;
            Settle(restored);
            check(restored.Completed && _credits == 1 && _gold == 70,
                "Restored delivery journal recognizes paid gold without replay");
            share = Share(); share.Titles.Clear();
            share.GoldPayment = new CrownAccessionRecord { IncomingSourceHead = _dead,
                GoldRecipient = _head, EndowmentGold = 50 };
            Settle(share);
            check(share.Completed && share.GoldPayment.EndowmentDonor == _treasury && _debits == 1 && _gold == 120,
                "Undebited inheritance follows the estate treasury successor once");
            var blocked = Share(); blocked.Titles = new List<string> { "missing" }; blocked.GoldPayment = null;
            _title = null;
            var complete = Share(); complete.Titles.Clear();
            record.Shares = new List<CrossClanEstateShare> { blocked, complete };
            AccessTools.Method(type, "TrySettleCrossClanEstate").Invoke(partition, new object[] { record, null });
            check(!blocked.Completed && complete.Completed && !record.Completed,
                "One invalid share does not block a later independently completed share");
        }
        finally
        {
            h.UnpatchAll(h.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { old });
        }
    }
}
