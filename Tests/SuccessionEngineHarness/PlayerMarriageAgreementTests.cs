using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static partial class MarriageHouseholdTests
{
    private sealed class OfferManager : ICampaignBehaviorManager
    {
        internal List<CampaignBehaviorBase> Items = new List<CampaignBehaviorBase>();
        public T GetBehavior<T>() => Items.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetBehaviors<T>() => Items.OfType<T>();
        public void AddBehavior(CampaignBehaviorBase b) => Items.Add(b);
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase => Items.RemoveAll(b => b is T);
        public void ClearBehaviors() => Items.Clear();
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> b) => Items.AddRange(b);
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }
    private sealed class OfferStore : IDataStore
    {
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T value)
        {
            if (IsSaving) { Data[key] = value; return true; }
            if (!Data.TryGetValue(key, out object saved)) return false;
            value = (T)saved; return true;
        }
    }
    private static bool _offerReady;
    private static int _offerRewards, _nativeQuotes;
    private static MarriageOfferCampaignBehavior _offers;
    private static readonly Type AgreementBehavior = typeof(PlayerMarriageAgreementBehavior);
    private static readonly Type Pricing = typeof(BellumMarriageModel).Assembly.GetType("BellumCivile.PlayerMarriagePricing");
    private static bool OfferCouple(Hero firstHero, Hero secondHero, ref bool __result)
    {
        bool prospect = (bool)AccessTools.Property(typeof(BellumMarriageModel).Assembly.GetType("BellumCivile.MarriageProspectEvaluation"), "Active").GetValue(null);
        __result = (_offerReady || prospect) && firstHero.Spouse == null && secondHero.Spouse == null
            && !_offers.IsHeroEngaged(firstHero) && !_offers.IsHeroEngaged(secondHero);
        return false;
    }
    private static bool Reward() { _offerRewards++; return false; }
    private static bool NativePrice(MarriageBarterable __instance, IFaction faction, ref int __result)
    {
        _nativeQuotes++;
        __result = __instance.HeroBeingProposedTo.Clan == _player ? -45000 : -1000;
        return false;
    }
    private static bool HalfRisk(ref float __result) { __result = .5f; return false; }
    private static bool SameTier(ref int __result) { __result = 4; return false; }
    private static bool OfferName(Hero __instance, ref TextObject __result) { __result = new TextObject(__instance.StringId); return false; }
    private static bool HouseName(Clan __instance, ref TextObject __result) { __result = new TextObject(__instance.StringId); return false; }
    private static bool GameText(string id, ref TextObject __result) { __result = new TextObject(id); return false; }
    private static bool Decline(MarriageOfferCampaignBehavior __instance)
    { AccessTools.Method(typeof(MarriageOfferCampaignBehavior), "FinalizeMarriageOffer").Invoke(__instance, null); return false; }

    private static void RunPlayer(Action<bool, string> check, Harmony harmony, Campaign campaign,
        Hero bride, Hero groom, Clan brideHouse, Clan groomHouse, Hero spare)
    {
        _player = groomHouse;
        Spouses.Clear(); Houses[bride] = brideHouse; Houses[groom] = groomHouse;
        Heirs[brideHouse] = bride; Heirs[groomHouse] = spare; Crowns.Clear(); Crowns[_realms[0]] = bride;
        _offers = new MarriageOfferCampaignBehavior();
        var behavior = new PlayerMarriageAgreementBehavior();
        var manager = new OfferManager { Items = new List<CampaignBehaviorBase> { _offers, behavior } };
        campaign.AddCampaignBehaviorManager(manager);
        var waitingField = AccessTools.Field(typeof(MarriageOfferCampaignBehavior), "_acceptedMarriageOffersThatWaitingForAvailability");
        var waiting = (Dictionary<Hero, Hero>)waitingField.GetValue(_offers);
        var playerField = AccessTools.Field(typeof(MarriageOfferCampaignBehavior), "_currentOfferedPlayerClanHero");
        var otherField = AccessTools.Field(typeof(MarriageOfferCampaignBehavior), "_currentOfferedOtherClanHero");
        var recordsField = AccessTools.Field(AgreementBehavior, "_agreements");
        var records = (List<PlayerMarriageAgreement>)recordsField.GetValue(behavior);
        var assembly = typeof(BellumMarriageModel).Assembly;
        void Patch(MethodBase target, string method) => harmony.Patch(target, prefix: new HarmonyMethod(typeof(MarriageHouseholdTests), method));
        void Active(Hero player, Hero other) { playerField.SetValue(_offers, player); otherField.SetValue(_offers, other); }
        PlayerMarriageAgreement Record(bool negotiated, Clan destination) => new PlayerMarriageAgreement {
            Player = groom, Other = bride, PlayerHouse = groomHouse, OtherHouse = brideHouse,
            Destination = destination, Negotiated = negotiated };
        bool Valid(PlayerMarriageAgreement r) => (bool)AccessTools.Method(typeof(PlayerMarriageAgreement), "Validate")
            .Invoke(r, new object[] { null });
        object Quote(MarriageBarterable item, PlayerMarriageAgreement agreement = null)
        {
            var args = agreement == null ? new object[] { item, null } : new object[] { item, agreement, null };
            AccessTools.Method(Pricing, agreement == null ? "TryGet" : "Bind").Invoke(null, args);
            return args[args.Length - 1];
        }
        void Reset()
        {
            Spouses.Clear(); Houses[bride] = brideHouse; Houses[groom] = groomHouse;
            waiting.Clear(); records.Clear(); Active(null, null); _offerRewards = 0; _moves = 0;
        }
        harmony.Unpatch(AccessTools.Method(typeof(DefaultMarriageModel), "IsCoupleSuitableForMarriage"), AccessTools.Method(typeof(MarriageHouseholdTests), nameof(Yes)));
        Patch(AccessTools.Method(typeof(DefaultMarriageModel), "IsCoupleSuitableForMarriage"), nameof(OfferCouple));
        Patch(AccessTools.Method(typeof(DefaultMarriageModel), "ShouldNpcMarriageBetweenClansBeAllowed"), nameof(Yes));
        Patch(AccessTools.Method(typeof(TaleWorlds.CampaignSystem.Actions.ChangeRelationAction), "ApplyPlayerRelation"), nameof(Reward));
        Patch(AccessTools.Method(typeof(InformationManager), "ShowInquiry"), nameof(Skip));
        Patch(AccessTools.Method(typeof(TaleWorlds.Core.GameTexts), "FindText"), nameof(GameText));
        Patch(AccessTools.Method(AgreementBehavior, "Notify"), nameof(Skip));
        Patch(AccessTools.Method(typeof(MarriageOfferCampaignBehavior), "OnMarriageOfferDeclinedOnPopUp"), nameof(Decline));
        Patch(AccessTools.PropertyGetter(typeof(Hero), "Name"), nameof(OfferName));
        Patch(AccessTools.PropertyGetter(typeof(Clan), "Name"), nameof(HouseName));
        Patch(AccessTools.PropertyGetter(typeof(Clan), "Tier"), nameof(SameTier));
        Patch(AccessTools.Method(assembly.GetType("BellumCivile.Behaviors.BellumMarriageStrategyHelper"), "PlayerMarriageHouseRisk"), nameof(HalfRisk));
        Patch(AccessTools.Method(typeof(MarriageBarterable), "GetUnitValueForFaction"), nameof(NativePrice));
        foreach (string name in new[] { "PlayerMarriageOfferCapturePatch", "PlayerMarriageOwnEngagementPatch",
            "PlayerMarriageOfferConsequencesPatch", "PlayerMarriageOfferAcceptPatch", "PlayerMarriageOfferHourlyPatch",
            "PlayerMarriageOfferWeddingPatch", "MarriageDowryPatch", "PlayerMarriageBarterCommitPatch",
            "PlayerMarriageBarterAtomicPatch", "PlayerMarriageBarterWeddingPatch", "PlayerMarriageBarterNamePatch" })
            check(harmony.CreateClassProcessor(assembly.GetType("BellumCivile.Patches." + name)).Patch().Count > 0,
                "Installed native method binds player-marriage patch: " + name);

        check(!Valid(Record(false, groomHouse)) && Valid(Record(true, groomHouse)),
            "NPC heir departure requires deliberate negotiation, not an unsolicited offer");
        Hero oldLeader = Leaders[brideHouse]; Leaders[brideHouse] = bride;
        check(!Valid(Record(true, groomHouse)) && Valid(Record(true, brideHouse)), "Money cannot move a ruling clan head");
        Leaders[brideHouse] = oldLeader;
        var dyingHouse = Houses.Where(p => p.Value == brideHouse && p.Key != bride).Select(p => p.Key).ToArray();
        foreach (Hero hero in dyingHouse) Dead.Add(hero);
        check(!Valid(Record(true, groomHouse)), "Money cannot buy the last continuation of the NPC bloodline");
        Dead.Clear();
        var changed = Record(true, groomHouse); Houses[bride] = groomHouse;
        check(!Valid(changed), "Changed clan cancels an agreement instead of silently changing its recipient");
        Houses[bride] = brideHouse;

        _offerReady = false;
        check((bool)AccessTools.Method(AgreementBehavior, "Capture").Invoke(behavior, new object[] { groom, bride }),
            "New unsolicited offer records protected heiress household");
        var accepted = records.Single(); Active(groom, bride);
        check(accepted.Destination == brideHouse && _model.GetClanAfterMarriage(groom, bride) == brideHouse,
            "Preview uses the stored matrilineal household");
        _offers.OnMarriageOfferAcceptedOnPopUp();
        check(waiting[groom] == bride && accepted.Accepted && playerField.GetValue(_offers) == null && _offerRewards == 0,
            "Accepted unavailable pair enters the native queue without relation rewards or a wedding");
        AccessTools.Method(typeof(MarriageOfferCampaignBehavior), "HourlyTick").Invoke(_offers, null);
        check(waiting[groom] == bride && _offerRewards == 0, "Temporary unavailability preserves accepted household on hourly retry");
        var store = new OfferStore(); behavior.SyncData(store); store.IsLoading = true;
        var loaded = new PlayerMarriageAgreementBehavior(); loaded.SyncData(store);
        manager.Items.Remove(behavior); manager.Items.Add(loaded); behavior = loaded;
        check(((List<PlayerMarriageAgreement>)recordsField.GetValue(loaded)).Single().Destination == brideHouse,
            "Behavior save/load preserves agreed destination alongside the native queued pair");
        _offerReady = true;
        check(_offers.IsHeroEngaged(groom) && _model.IsCoupleSuitableForMarriage(groom, bride),
            "Own queued engagement is ignored only during that pair's wedding suitability check");
        AccessTools.Method(typeof(MarriageOfferCampaignBehavior), "HourlyTick").Invoke(_offers, null);
        check(Spouses[groom] == bride && Houses[groom] == brideHouse && _offerRewards == 1 && waiting.Count == 0,
            "Actual native hourly path completes delayed matrilineal wedding and rewards once");
        AccessTools.Method(typeof(MarriageOfferCampaignBehavior), "HourlyTick").Invoke(_offers, null);
        check(_offerRewards == 1, "Later native ticks cannot repeat the accepted wedding reward");
        Reset();
        check(_model.GetClanAfterMarriage(groom, bride) == groomHouse,
            "Old native pending offers without a Bellum record keep the original household");
        var legacy = new PlayerMarriageAgreementBehavior(); legacy.SyncData(new OfferStore { IsLoading = true });
        check(((List<PlayerMarriageAgreement>)recordsField.GetValue(legacy)).Count == 0, "Old saves do not synthesize changed offer terms");

        var stale = Record(false, brideHouse); stale.Accepted = true;
        records.Add(stale); waiting[groom] = bride; Houses[bride] = groomHouse;
        AccessTools.Method(typeof(MarriageOfferCampaignBehavior), "HourlyTick").Invoke(_offers, null);
        check(waiting.Count == 0 && _offerRewards == 0 && Spouses.Count == 0,
            "Stale queued household is canceled before native relation rewards or marriage");
        Reset();

        var normal = new MarriageBarterable(Leaders[groomHouse], null, groom, bride);
        var reverse = new MarriageBarterable(Leaders[groomHouse], null, bride, groom);
        int firstPrice = normal.GetUnitValueForFaction(brideHouse);
        check(firstPrice == -170000 && reverse.GetUnitValueForFaction(brideHouse) == firstPrice,
            "Production barter patch normalizes both constructor orders and charges one Crown premium plus health cost");
        int reads = _rosterReads, crownReads = _crownReads;
        for (int i = 0; i < 100; i++) normal.GetUnitValueForFaction(brideHouse);
        check(_rosterReads == reads && _crownReads == crownReads, "Barter redraws reuse quoted health and heir snapshot");
        var incoming = new MarriageBarterable(Leaders[groomHouse], null, groom, bride);
        Quote(incoming, Record(true, brideHouse));
        check(incoming.GetUnitValueForFaction(brideHouse) == -33750,
            "Useful incoming husband receives health discount without an heir departure fee");
        incoming.Apply();
        check(Spouses[groom] == bride && Houses[groom] == brideHouse,
            "Actual marriage barter item executes the explicitly chosen household");
        Reset();
        var invalid = new MarriageBarterable(Leaders[groomHouse], null, groom, bride);
        Quote(invalid, Record(true, groomHouse)); Houses[bride] = groomHouse;
        var guard = assembly.GetType("BellumCivile.Patches.PlayerMarriageBarterGuard");
        check(!(bool)AccessTools.Method(guard, "Validate").Invoke(null, new object[] { new List<Barterable> { invalid } }),
            "Whole-barter preflight rejects invalid agreement before any item can be applied");
        invalid.Apply();
        check(Spouses.Count == 0, "Item-level fallback also refuses invalid marriage");
        Reset();
        foreach (var field in typeof(PlayerMarriageAgreement).GetFields(BindingFlags.Public | BindingFlags.Instance))
            check(field.GetCustomAttributes(false).Any(a => a.GetType().Name == "SaveableFieldAttribute"),
                "Player agreement field is persisted: " + field.Name);
        PlayerMarriageSaveDefinitionTests.Run(check);
        RunOfferCommand(check, campaign, behavior, bride, groom);
        _player = null;
    }
}
