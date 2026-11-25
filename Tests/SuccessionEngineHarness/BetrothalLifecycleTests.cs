using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static partial class MarriageHouseholdTests
{
    private static int _pledgeDay, _pledgeRelation, _pledgeMemoryHits, _pledgeMemoryAmount;
    private static float _pledgeMemoryYears;
    private static bool _pledgeAvailable;
    private static bool BetrothalDay(ref int __result) { __result = _pledgeDay; return false; }
    private static bool BetrothalRelation(ref int __result) { __result = _pledgeRelation; return false; }
    private static bool BetrothalAvailable(ref bool __result) { __result = _pledgeAvailable; return false; }
    private static bool BetrothalBirth(Hero __instance, ref CampaignTime __result)
    { __result = CampaignTime.Years(__instance.IsFemale ? 1068 : 1065); return false; }
    private static bool NoCourtship(ref Hero __result) { __result = null; return false; }
    private static bool RecordBetrothalMemory(Hero firstHero, Hero secondHero, int relationChange, string sourceId, float durationYears)
    {
        if (sourceId == "broke_betrothal")
        { _pledgeMemoryHits++; _pledgeMemoryAmount = relationChange; _pledgeMemoryYears = durationYears; }
        return false;
    }

    private static void RunBetrothalLifecycle(Action<bool, string> check, Campaign campaign, Hero first, Hero second, Hero spare)
    {
        var assembly = typeof(BellumMarriageModel).Assembly;
        var type = assembly.GetType("BellumCivile.Behaviors.BetrothalBehavior");
        var rules = assembly.GetType("BellumCivile.BetrothalRules");
        var recordType = assembly.GetType("BellumCivile.BetrothalRecord");
        var helper = assembly.GetType("BellumCivile.Behaviors.BellumMarriageStrategyHelper");
        var manager = (OfferManager)campaign.CampaignBehaviorManager;
        var harmony = new Harmony("bellum.test.betrothal");
        var firstHouse = first.Clan;
        var secondHouse = second.Clan;
        var dayBefore = Ages[first]; var secondAgeBefore = Ages[second];
        var field = AccessTools.Field(type, "_pledges");
        var oldPlayer = _player;
        var oldSecondHeir = Heirs[secondHouse];
        var oldCrown = new System.Collections.Generic.Dictionary<Kingdom, Hero>(Crowns);
        var dayTicks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var yearTicks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerYear");
        var oldDayTicks = dayTicks.GetValue(null); var oldYearTicks = yearTicks.GetValue(null);
        CampaignBehaviorBase behavior = null;
        object Call(string name, params object[] args) => AccessTools.Method(type, name).Invoke(behavior, args);
        bool Creation(bool offer = false) => (bool)Call("Create", first, second, firstHouse, offer);
        bool Reserved(Hero hero) => (bool)Call("IsReserved", hero);
        IList Records() => (IList)field.GetValue(behavior);
        object Pledge() => Records()[0];
        bool Accepted() => (bool)AccessTools.Field(recordType, "Accepted").GetValue(Pledge());
        void Review() => Call("ReviewPledges");
        void Patch(MethodBase target, string prefix) => harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(MarriageHouseholdTests), prefix));
        void UseBehavior(CampaignBehaviorBase replacement = null)
        {
            manager.Items.Remove(behavior);
            behavior = replacement ?? (CampaignBehaviorBase)Activator.CreateInstance(type, nonPublic: true);
            manager.Items.Add(behavior);
        }
        try
        {
            UseBehavior();
            dayTicks.SetValue(null, 100L); yearTicks.SetValue(null, 8400L);
            Patch(AccessTools.PropertyGetter(type, "Day"), nameof(BetrothalDay));
            Patch(AccessTools.PropertyGetter(typeof(CampaignOptions), "IsLifeDeathCycleDisabled"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(CommandKingdom));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "BirthDay"), nameof(BetrothalBirth));
            Patch(AccessTools.Method(typeof(Hero), "GetRelation", new[] { typeof(Hero) }), nameof(BetrothalRelation));
            Patch(AccessTools.Method(typeof(Romance), "GetCourtedHeroInOtherClan"), nameof(NoCourtship));
            Patch(AccessTools.Method(helper, "MarriageParticipantReady"), nameof(BetrothalAvailable));
            Patch(AccessTools.Method(typeof(RelationMemoryService), "ApplyChange"), nameof(RecordBetrothalMemory));
            Patch(AccessTools.Method(type, "ShowPledgeLapsed"), nameof(Skip));
            Patch(AccessTools.Method(type, "ShowBetrothalOutcome"), nameof(Skip));
            Patch(AccessTools.Method(type, "ShowBetrothalSettlement"), nameof(Skip));
            Patch(AccessTools.Method(type, "ShowBetrothalWeddingPayment"), nameof(Skip));
            _pledgeDay = 500; _pledgeRelation = 40; _pledgeAvailable = true; _pledgeMemoryHits = 0;
            Spouses.Clear(); _moves = 0;
            var oldSave = new OfferStore { IsLoading = true };
            behavior.SyncData(oldSave);
            check(Records().Count == 0, "Old saves start without fabricated betrothals");

            check(!Creation(), "Two adults still use the ordinary marriage system");
            Ages[first] = 14; Ages[second] = 16;
            Outsiders.Add(second);
            check(!Creation(), "Retainers are excluded even if they come from a noble clan");
            Outsiders.Remove(second);
            CheckBetrothalOfferCommand(check, behavior, first, second);
            CheckBetrothalProposalDialogue(check, behavior, first, second);
            var payer = firstHouse.Leader;
            var payee = secondHouse.Leader;
            int held = 0;
            int payerStart = payer.Gold, payeeStart = payee.Gold;
            payer.Gold = 9999;
            check(!Creation(true) && Records().Count == 0, "A tier-four betrothal cannot be offered with less than the 10,000-denar deposit");
            payer.Gold = 10000;
            check(Creation(true) && payer.Gold == 10000, "Exactly enough for the deposit is sufficient to send an offer");
            Call("ResolveIncomingOffer", Pledge(), false);

            check(Creation(true), "A stored offer is available for the repricing regression check");
            var olderQuote = Pledge();
            AccessTools.Field(recordType, "Dowry").SetValue(olderQuote, 20000);
            Call("ResolveIncomingOffer", olderQuote, true);
            check(Accepted() && payer.Gold == 5000 && (int)AccessTools.Field(recordType, "HeldDeposit").GetValue(olderQuote) == 5000,
                "Accepting a previously quoted offer still uses its original deposit rather than repricing it");
            Call("End", olderQuote, "fixture_previous_price", null);
            check(payer.Gold == 10000 && payee.Gold == payeeStart, "Refunds also use the previously quoted deposit");

            var settingsField = AccessTools.Field(assembly.GetType("BellumCivile.BellumCivileOptions"), "_settings");
            object previousSettings = settingsField.GetValue(null);
            var settingsType = assembly.GetType("BellumCivile.BellumCivileSettings");
            object settings = Activator.CreateInstance(settingsType, nonPublic: true);
            var costSetting = AccessTools.Property(settingsType, "MarriageCostPercent");
            try
            {
                settingsField.SetValue(null, settings);
                costSetting.SetValue(settings, 50);
                payer.Gold = 5000;
                check(Creation(true) && (int)AccessTools.Field(recordType, "Dowry").GetValue(Pledge()) == 20000,
                    "A new betrothal scales the full dowry and its deposit");
                var discountedOffer = Pledge();
                costSetting.SetValue(settings, 200);
                Call("ResolveIncomingOffer", discountedOffer, true);
                check(Accepted() && payer.Gold == 0 && (int)AccessTools.Field(recordType, "HeldDeposit").GetValue(discountedOffer) == 5000,
                    "Changing the MCM multiplier does not reprice a previously offered betrothal");
                Call("End", discountedOffer, "fixture_previous_multiplier", null);
            }
            finally { settingsField.SetValue(null, previousSettings); }

            payer.Gold = payerStart;
            check(Creation(true) && !Accepted() && Reserved(first) && Reserved(second) && !Creation(),
                "An unanswered incoming offer reserves its candidates without becoming a pledge or permitting a duplicate");
            check(payer.Gold == payerStart && payee.Gold == payeeStart, "An unanswered offer never reserves gold");
            check(!(bool)Call("HasAcceptedPledge", firstHouse, secondHouse), "An unanswered offer has no political benefit");
            ResolveBetrothalThroughPopup(check, Pledge(), first, second, accept: true);
            held = (int)AccessTools.Field(recordType, "HeldDeposit").GetValue(Pledge());
            int dowry = (int)AccessTools.Field(recordType, "Dowry").GetValue(Pledge());
            check(dowry == 40000 && held == 10000 && dowry - held == 30000
                && payer.Gold == payerStart - held && payee.Gold == payeeStart,
                "A new tier-four betrothal escrows 10,000 of the 40,000-denar dowry, leaving 30,000 due");
            check(Accepted() && (bool)Call("HasAcceptedPledge", secondHouse, firstHouse)
                && Spouses.Count == 0 && first.Clan == firstHouse && second.Clan == secondHouse && _moves == 0,
                "Giving your word creates a temporary house tie with no wedding or transfer");
            check(!_model.IsCoupleSuitableForMarriage(first, second) && !_model.IsCoupleSuitableForMarriage(first, spare),
                "The reservation blocks both a premature ceremony and competing marriages");
            var date = AccessTools.Method(recordType, "ExpectedDate");
            var expected16 = (CampaignTime)date.Invoke(Pledge(), new object[] { 16 });
            var expected18 = (CampaignTime)date.Invoke(Pledge(), new object[] { 18 });
            check(Math.Abs(expected16.ToYears - 1084) < .001 && Math.Abs(expected18.ToYears - 1086) < .001,
                "Expected dates follow the younger hero's birthday and the active age, not a fixed number of days");
            var saved = new OfferStore(); behavior.SyncData(saved); saved.IsLoading = true;
            var restored = (CampaignBehaviorBase)Activator.CreateInstance(type, nonPublic: true); restored.SyncData(saved);
            UseBehavior(restored);
            check(Records().Count == 1 && Accepted() && Reserved(first)
                && AccessTools.Field(recordType, "Destination").GetValue(Pledge()) == firstHouse
                && (int)AccessTools.Field(recordType, "HeldDeposit").GetValue(Pledge()) == held && payer.Gold == payerStart - held,
                "Save/load restores both reservations and the originally agreed household");

            var cancelled = Pledge();
            check(!(bool)Call("BreakWithPlayer", cancelled, secondHouse), "A different house cannot end the player's pledge");
            check((bool)Call("BreakWithPlayer", cancelled, firstHouse) && !(bool)Call("BreakWithPlayer", cancelled, firstHouse)
                && _pledgeMemoryHits == 1 && _pledgeMemoryAmount == -30 && _pledgeMemoryYears == 10
                && !Reserved(first) && !(bool)Call("HasAcceptedPledge", firstHouse, secondHouse),
                "Deliberate renunciation removes reservation and political benefit and applies one -30 ten-year memory");
            int premium = (held + 1) / 2;
            check(payer.Gold == payerStart + premium && payee.Gold == payeeStart - premium,
                "The sending clan refunds the full deposit and funds the 50-percent premium itself");

            payerStart = payer.Gold; payeeStart = payee.Gold;
            check(Creation(), "A funded pledge can be used to test a paying house renouncing");
            check((bool)Call("End", Pledge(), "fixture_payer_renounced", firstHouse)
                && payer.Gold == payerStart - held && payee.Gold == payeeStart + held,
                "When the payer breaks its word, the other house receives the deposit without a premium");
            _pledgeMemoryHits = 1;

            payerStart = payer.Gold; payeeStart = payee.Gold;
            check(Creation(), "A pledge can outlast a temporary deterioration in relations");
            _pledgeRelation = -60;
            Review();
            _pledgeDay += 30;
            Review();
            check(Records().Count == 0 && _pledgeMemoryHits == 1 && payer.Gold == payerStart && payee.Gold == payeeStart,
                "Sustained hostility without a deliberate renunciation only refunds the deposit");
            _pledgeRelation = 40;

            check(Creation(offer: true), "Another incoming offer can be reviewed after the earlier pledge ends");
            ResolveBetrothalThroughPopup(check, Pledge(), first, second, accept: false);
            check(Records().Count == 0 && !Reserved(first) && _pledgeMemoryHits == 1,
                "Declining an unaccepted offer clears its reservation without a broken-pledge memory");

            check(Creation(), "Family members can make a new pledge after an earlier one ends");
            var wedding = Pledge();
            payerStart = payer.Gold + held; payeeStart = payee.Gold;
            Ages[first] = 17; Ages[second] = 18;
            Call("HonorPlayerPledge", wedding);
            check(Spouses.Count == 0, "Player consent cannot bypass the younger partner's adulthood");
            Ages[first] = 18; _pledgeDay += 3; Review();
            check(Spouses.Count == 0 && Records().Count == 1, "Both adults still wait for the player's explicit wedding decision");
            _pledgeAvailable = false; Call("HonorPlayerPledge", wedding);
            check(Spouses.Count == 0 && Records().Count == 1, "Player consent is retained while a temporary blocker postpones the ceremony");
            _pledgeDay += 2; _pledgeAvailable = true; Review();
            check(Spouses.Count == 0, "A temporary blocker cannot retry before the saved three-day interval");
            _pledgeDay++; Review(); Review();
            check(Spouses[first] == second && Spouses[second] == first && second.Clan == firstHouse && _moves == 1
                && _callbackHouse == firstHouse && Records().Count == 0
                && payer.Gold == payerStart - dowry && payee.Gold == payeeStart + dowry,
                "One real native wedding joins the recorded household and the pledge cannot fire twice");

            Spouses.Clear(); Houses[second] = secondHouse; Ages[first] = 17; Ages[second] = 18;
            payerStart = payer.Gold; payeeStart = payee.Gold;
            check(Creation(), "A protected heir test begins with a valid direction");
            Heirs[secondHouse] = second; Ages[first] = 18; Review();
            check(Spouses.Count == 0 && Records().Count == 0 && _pledgeMemoryHits == 1
                && payer.Gold == payerStart && payee.Gold == payeeStart,
                "A newly protected heir lapses stale terms without swapping the receiving house or blaming either side");
            Heirs[secondHouse] = oldSecondHeir;

            Ages[first] = 17; Ages[second] = 18; _player = null;
            check(Creation(), "Two NPC houses can seal a valid dynastic pledge");
            _enabled = false; Ages[first] = 18; _pledgeAvailable = false; _pledgeDay++; Review();
            _pledgeDay += 30; Review();
            check(Records().Count == 0 && Spouses.Count == 0 && _pledgeMemoryHits == 1,
                "A wedding unavailable throughout the thirty-day grace period lapses without a renunciation penalty");
            _enabled = true; _pledgeAvailable = true; Ages[first] = 17;
            check(Creation(), "Another NPC pledge can be made while strategic logic is on");
            _enabled = false; Ages[first] = 18; Review();
            check(Records().Count == 0 && Spouses[first] == second, "Toggling strategic matchmaking off does not erase or block an existing pledge");

            var mark = AccessTools.Method(rules, "TrackHostility");
            var expired = AccessTools.Method(rules, "MayEndForHostility");
            check((int)mark.Invoke(null, new object[] { -1, 10, -60f }) == 10
                && (int)mark.Invoke(null, new object[] { 10, 25, -59f }) == -1
                && !(bool)expired.Invoke(null, new object[] { 10, 39 }) && (bool)expired.Invoke(null, new object[] { 10, 40 }),
                "Hostility must remain at -60 or worse for thirty days, and recovers immediately above the threshold");
            check(recordType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Count(f =>
                f.GetCustomAttributes(false).Any(a => a.GetType().Name == "SaveableFieldAttribute")) == 16,
                "Betrothal saves include identity, consent, timing, notification and escrow terms");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id); manager.Items.Remove(behavior);
            dayTicks.SetValue(null, oldDayTicks); yearTicks.SetValue(null, oldYearTicks);
            _enabled = true; _player = oldPlayer; _offerReady = true;
            Spouses.Clear(); Houses[first] = firstHouse; Houses[second] = secondHouse;
            Ages[first] = dayBefore; Ages[second] = secondAgeBefore; Outsiders.Remove(second);
            Heirs[secondHouse] = oldSecondHeir;
            Crowns.Clear(); foreach (var entry in oldCrown) Crowns.Add(entry.Key, entry.Value);
        }
    }
}
