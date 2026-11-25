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
        void Bind(MarriageBarterable item, PlayerMarriageAgreement agreement) =>
            AccessTools.Method(Pricing, "BindAgreement").Invoke(null, new object[] { item, agreement });
        bool Bound(MarriageBarterable item) =>
            (bool)AccessTools.Method(Pricing, "TryGet").Invoke(null, new object[] { item, null });
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
        var dyingHouse = Houses.Where(p => p.Value == brideHouse && p.Key != bride && p.Key != oldLeader).Select(p => p.Key).ToArray();
        foreach (Hero hero in dyingHouse) Dead.Add(hero);
        // An ageing mother and her only viable heir reproduce the reported royal-household case.
        Women.Add(oldLeader);
        var policyType = assembly.GetType("BellumCivile.MarriageHouseholdPolicy");
        check((bool)AccessTools.Method(policyType, "IsLastContinuation").Invoke(Activator.CreateInstance(policyType, true), new object[] { bride }),
            "Ageing ruler's sole viable child reproduces the last-continuation restriction");
        var dialogue = new SpecificMarriageProposalBehavior();
        AccessTools.Field(typeof(SpecificMarriageProposalBehavior), "_selectedPlayerHero").SetValue(dialogue, groom);
        AccessTools.Field(typeof(SpecificMarriageProposalBehavior), "_selectedOtherHero").SetValue(dialogue, bride);
        bool Choice(bool maternal) => (bool)AccessTools.Method(typeof(SpecificMarriageProposalBehavior), "CanChooseHousehold")
            .Invoke(dialogue, new object[] { maternal, null });
        check(Choice(true), "A nonleader player-clan relative can still join the heiress's house");
        Hero playerHead = Leaders[groomHouse]; Leaders[groomHouse] = groom;
        _offerReady = true;
        var guardType = assembly.GetType("BellumCivile.Patches.PlayerMarriageBarterGuard");
        foreach (bool royal in new[] { true, false })
        {
            Crowns.Clear();
            if (royal) Crowns[_realms[0]] = bride;
            foreach (bool femaleHeir in new[] { true, false })
            {
                if (femaleHeir) { Women.Add(bride); Women.Remove(groom); }
                else { Women.Remove(bride); Women.Add(groom); }
                string scenario = (royal ? "Crown" : "Clan") + (femaleHeir ? " heiress" : " heir");
                check(!Valid(Record(false, groomHouse)) && Valid(Record(true, groomHouse)),
                    scenario + ": direct negotiation may bring the sole continuation home, unsolicited offers may not");
                check(Choice(!femaleHeir) && !Choice(femaleHeir),
                    scenario + ": dialogue permits incoming spouse but never sends away the player clan leader");
                var agreement = Record(true, groomHouse);
                check((bool)AccessTools.Method(AgreementBehavior, "Ready").Invoke(null, new object[] { agreement, null }),
                    scenario + ": the same exception survives final wedding readiness checks");
                var item = new MarriageBarterable(groom, null, groom, bride);
                Bind(item, agreement);
                check((int)AccessTools.Method(Pricing, "NegotiatedQuote").Invoke(null, new object[] { agreement })
                    == (royal ? 85000 : 60000), scenario + ": direct negotiation uses the shared heir premium");
                check((bool)AccessTools.Method(guardType, "Validate").Invoke(null, new object[] { new List<Barterable> { item } }),
                    scenario + ": whole-barter preflight allows the explicitly negotiated departure");
                item.Apply();
                check(Spouses[groom] == bride && Houses[bride] == groomHouse && Houses[groom] == groomHouse
                    && _callbackHouse == groomHouse && Leaders[brideHouse] == oldLeader,
                    scenario + ": native barter wedding moves only the heir into the player's house");
                Reset();
            }
        }
        Leaders[groomHouse] = playerHead;
        Women.Remove(oldLeader); Women.Remove(groom); Women.Add(bride);
        Crowns.Clear(); Crowns[_realms[0]] = bride;
        Dead.Clear();
        var changed = Record(true, groomHouse); Houses[bride] = groomHouse;
        check(!Valid(changed), "Changed clan cancels an agreement instead of silently changing its recipient");
        Houses[bride] = brideHouse;

        _offerReady = false;
        check((bool)AccessTools.Method(AgreementBehavior, "Capture").Invoke(behavior, new object[] { groom, bride }),
            "New unsolicited offer records protected heiress household");
        var accepted = records.Single(); Active(groom, bride);
        int incomingDowry = accepted.Dowry, brideGold = brideHouse.Leader.Gold, groomGold = groomHouse.Leader.Gold;
        check(accepted.Destination == brideHouse && _model.GetClanAfterMarriage(groom, bride) == brideHouse,
            "Preview uses the stored matrilineal household");
        _offers.OnMarriageOfferAcceptedOnPopUp();
        check(waiting[groom] == bride && accepted.Accepted && playerField.GetValue(_offers) == null && _offerRewards == 0
            && incomingDowry > 0 && brideHouse.Leader.Gold == brideGold && groomHouse.Leader.Gold == groomGold,
            "Accepted unavailable pair enters the native queue without relation rewards or a wedding");
        AccessTools.Method(typeof(MarriageOfferCampaignBehavior), "HourlyTick").Invoke(_offers, null);
        check(waiting[groom] == bride && _offerRewards == 0, "Temporary unavailability preserves accepted household on hourly retry");
        var store = new OfferStore(); behavior.SyncData(store); store.IsLoading = true;
        var loaded = new PlayerMarriageAgreementBehavior(); loaded.SyncData(store);
        manager.Items.Remove(behavior); manager.Items.Add(loaded); behavior = loaded;
        check(((List<PlayerMarriageAgreement>)recordsField.GetValue(loaded)).Single().Destination == brideHouse,
            "Behavior save/load preserves agreed destination alongside the native queued pair");
        _offerReady = true;
        brideHouse.Leader.Gold = incomingDowry - 1;
        AccessTools.Method(typeof(MarriageOfferCampaignBehavior), "HourlyTick").Invoke(_offers, null);
        check(waiting.TryGetValue(groom, out var deferredPartner) && deferredPartner == bride
            && Spouses.Count == 0 && groomHouse.Leader.Gold == groomGold,
            "The accepted offer waits for the paying house to restore its funds");
        brideHouse.Leader.Gold = brideGold;
        check(_offers.IsHeroEngaged(groom) && _model.IsCoupleSuitableForMarriage(groom, bride),
            "Own queued engagement is ignored only during that pair's wedding suitability check");
        _enabled = false;
        try { AccessTools.Method(typeof(MarriageOfferCampaignBehavior), "HourlyTick").Invoke(_offers, null); }
        finally { _enabled = true; }
        check(Spouses[groom] == bride && Houses[groom] == brideHouse && _offerRewards == 1 && waiting.Count == 0
            && brideHouse.Leader.Gold == brideGold - incomingDowry && groomHouse.Leader.Gold == groomGold + incomingDowry,
            "A saved matrilineal agreement completes at its agreed price even after the revamp is turned off");
        AccessTools.Method(typeof(MarriageOfferCampaignBehavior), "HourlyTick").Invoke(_offers, null);
        check(_offerRewards == 1 && groomHouse.Leader.Gold == groomGold + incomingDowry,
            "Later native ticks cannot repeat the accepted wedding reward or dowry");
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
        check(normal.GetUnitValueForFaction(brideHouse) == -45000 && !Bound(normal),
            "Enabling the revamp does not reprice a native item before Bellum prepares a quote");
        _enabled = false;
        try
        {
            Bind(reverse, Record(true, brideHouse));
            int reads = _nativeQuotes;
            check(normal.GetUnitValueForFaction(brideHouse) == -45000
                && reverse.GetUnitValueForFaction(brideHouse) == -1000 && _nativeQuotes == reads + 2
                && !Bound(normal) && !Bound(reverse),
                "With the revamp off, both native constructor orders keep the original value without creating a Bellum quote");
            var data = Blank<BarterData>();
            object[] args = { data };
            var patch = assembly.GetType("BellumCivile.Patches.PlayerMarriageDowryBarterPatch");
            check((bool)AccessTools.Method(patch, "Prefix").Invoke(null, args) && ReferenceEquals(data, args[0]),
                "The disabled revamp does not replace native barter data");
            var nativeName = new TextObject("Native marriage text");
            object[] nameArgs = { normal, nativeName };
            AccessTools.Method(assembly.GetType("BellumCivile.Patches.PlayerMarriageBarterNamePatch"), "Postfix")
                .Invoke(null, nameArgs);
            check(ReferenceEquals(nameArgs[1], nativeName), "The disabled revamp preserves the native barter label");
        }
        finally { _enabled = true; }
        var incoming = new MarriageBarterable(Leaders[groomHouse], null, groom, bride);
        Bind(incoming, Record(true, brideHouse));
        incoming.Apply();
        check(Spouses[groom] == bride && Houses[groom] == brideHouse,
            "Actual marriage barter item executes the explicitly chosen household");
        Reset();
        var invalid = new MarriageBarterable(Leaders[groomHouse], null, groom, bride);
        Bind(invalid, Record(true, groomHouse)); Houses[bride] = groomHouse;
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
        CheckDirectionalDowryBarter(check, harmony, campaign, bride, groom, Reset);
        RunOfferCommand(check, campaign, behavior, bride, groom);
        RunBetrothalLifecycle(check, campaign, bride, groom, spare);
        _player = null;
    }
}
