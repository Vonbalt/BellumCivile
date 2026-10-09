using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using TaleWorlds.CampaignSystem;
using BellumCivile;
using HarmonyLib;

internal static class HostageTreatyTermTests
{
    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(TreatyTermRecord).Assembly;
        var service = assembly.GetType("BellumCivile.TreatyHostageTerms");
        bool Holding(WarScoreFiefSnapshotRecord snapshot, params TreatyTermRecord[] terms) =>
            (bool)AccessTools.Method(service, "HoldingSurvivesSettlement").Invoke(null,
                new object[] { snapshot?.OwnerKingdomId == "enemy", "realm", "house", "castle", terms });
        var home = new WarScoreFiefSnapshotRecord("castle", "realm", "house", false, true);
        check(Holding(home), "Original ruling-house castle survives ordinary peace");
        check(!Holding(new WarScoreFiefSnapshotRecord("castle", "enemy", "enemy_house", false, true)),
            "Occupied enemy holding cannot host a new treaty pledge");
        check(Holding(new WarScoreFiefSnapshotRecord("castle", "realm", "vassal", false, true)),
            "Internal confiscation remains suitable even when another house owned it at war start");
        check(!Holding(home, new TreatyTermRecord(TreatyTermType.TransferFief, 10, settlementId: "castle")),
            "Explicitly ceded castle cannot host pledge");
        check(Holding(null), "Holding absent from war snapshots is not automatically restored by settlement");
        var enemy = new WarScoreFiefSnapshotRecord("castle", "enemy", "enemy_house", false, true);
        check(Holding(enemy, new TreatyTermRecord(TreatyTermType.TransferFief, 10,
            settlementId: "castle", fromKingdomId: "enemy", toKingdomId: "realm")),
            "Explicitly retained occupied castle can host the pledge");
        check(!Holding(home, new TreatyTermRecord(TreatyTermType.TransferFief, 10,
            settlementId: "castle", fromKingdomId: "realm", toKingdomId: "enemy")),
            "Ceded home castle cannot host the pledge");
        check(!Holding(enemy, new TreatyTermRecord(TreatyTermType.RecognizeClientOccupation, 10,
            settlementId: "castle", thirdKingdomId: "client", clanId: "client_house")),
            "Recognition of another house's possession cannot secure ruler custody");
        check(Holding(new WarScoreFiefSnapshotRecord("castle", "ally", "ally_house", false, true)),
            "Non-opposing ownership history is not treated as enemy occupation");
        var localize = AccessTools.Method(assembly.GetType("BellumCivile.UI.Parley.PeaceParleyVM"), "LocalizeActionReport");
        foreach (var reason in new[] {
            "a peace pact permits only one hostage from each realm", "the draft contains an invalid hostage pledge",
            "the same relative cannot be promised in marriage and as a hostage",
            "hostage peace cannot accompany white peace, annexation, or enforced rebel demands",
            "hostage peace requires two established sovereign realms", "hostage peace is only available in foreign wars",
            "a ruling house has changed since the hostage was selected", "the selected relative is no longer available as a hostage",
            "the hostage succession rank has changed; select the relative again",
            "the receiving ruling house has no town or castle free of siege for the hostage",
            "the treaty leaves no suitable holding in the receiving ruling house's possession" })
        {
            string message = (string)localize.Invoke(null, new object[] { reason });
            check(!string.IsNullOrWhiteSpace(message) && !message.Contains("The treaty action could not be completed"),
                "Specific hostage rejection message: " + reason);
        }
        bool Valid(params TreatyTermRecord[] terms)
        {
            object[] args = { terms, "winner", "loser", null };
            return (bool)AccessTools.Method(service, "ValidShape").Invoke(null, args);
        }
        TreatyTermRecord Hostage(bool offer = false, int tier = 1, int cost = 30, int days = 100, string hero = null)
            => new TreatyTermRecord(TreatyTermType.HostagePeace, cost, durationDays: days,
                fromKingdomId: offer ? "winner" : "loser", toKingdomId: offer ? "loser" : "winner",
                heroId: hero ?? (offer ? "offered_heir" : "demanded_heir"), clanId: offer ? "winner_house" : "loser_house",
                wasVoluntaryOffering: offer, hostageTier: tier, hostageReceivingClanId: offer ? "loser_house" : "winner_house");
        check((int)TreatyTermType.RecognizeClientOccupation == 17 && (int)TreatyTermType.HostagePeace == 18,
            "Hostage term appends enum ID without renumbering old saves");
        var fields = typeof(TreatyTermRecord).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
        var ids = fields.SelectMany(f => f.GetCustomAttributesData().Where(a => a.AttributeType.Name == "SaveableFieldAttribute")
            .Select(a => Convert.ToInt32(a.ConstructorArguments[0].Value))).ToArray();
        check(ids.Length == 18 && ids.Distinct().Count() == 18, "Treaty pricing version appends a unique save field");
        check(Valid(Hostage()) && Valid(Hostage(true)), "One-sided demand or voluntary offering is supported");
        check(Valid(Hostage(), Hostage(true)), "Reciprocal pact accepts one hostage per side");
        check(!Valid(Hostage(), Hostage(hero: "other_heir")), "Two hostages from one realm are rejected");
        check(!Valid(Hostage(hero: "same"), Hostage(true, hero: "same")), "Same hero cannot secure both sides");
        check(Valid(Hostage(days: 0)) && Valid(Hostage(days: 99)) && Valid(Hostage(days: 1000)),
            "Stored hostage clauses support configurable duration including zero and maximum");
        check(!Valid(Hostage(cost: 1)) && !Valid(Hostage(days: -1)) && !Valid(Hostage(days: 1001))
            && !Valid(Hostage(tier: 0)), "Forged cost, out-of-range duration and tier are rejected");
        check(Valid(Hostage(days: 250), Hostage(true, days: 250))
            && !Valid(Hostage(days: 250), Hostage(true, days: 100)),
            "Reciprocal pledges must agree on the same pact duration");
        var reverse = new TreatyTermRecord(TreatyTermType.HostagePeace, 30, durationDays: 100,
            fromKingdomId: "winner", toKingdomId: "loser", heroId: "heir", clanId: "winner_house", hostageTier: 1,
            hostageReceivingClanId: "loser_house");
        check(!Valid(reverse), "Losing side cannot demand a winner hostage as a free reciprocal exchange");
        foreach (var type in new[] { TreatyTermType.WhitePeace, TreatyTermType.ForceVassalization, TreatyTermType.EnforceRebelDemands })
            check(!Valid(Hostage(), new TreatyTermRecord(type, 0)), "Hostage pledge excludes incompatible clause " + type);
        check(!Valid(Hostage(), new TreatyTermRecord(TreatyTermType.ArrangeRoyalMarriage, 20, secondaryHeroId: "demanded_heir")),
            "Marriage cannot simultaneously transfer the hostage");
        var accounting = assembly.GetType("BellumCivile.TreatyWarScoreAccounting");
        var ai = assembly.GetType("BellumCivile.TreatyAiDraftService");
        bool Combines(TreatyTermRecord next, params TreatyTermRecord[] existing) =>
            (bool)AccessTools.Method(ai, "CanCombineWithSelectedTerms").Invoke(null, new object[] { next, existing });
        check(Combines(Hostage(true), Hostage()), "AI supports reciprocal hostage offerings");
        check(Combines(Hostage(true, days: 250), Hostage(days: 250))
            && !Combines(Hostage(true, days: 100), Hostage(days: 250)),
            "AI reciprocal pledges preserve the drafted duration");
        check(!Combines(Hostage(hero: "other"), Hostage()), "AI rejects multiple hostages from one side");
        check(!Combines(new TreatyTermRecord(TreatyTermType.ForceVassalization, 100), Hostage()), "AI cannot append annexation to hostage pledge");
        check(!Combines(Hostage(), new TreatyTermRecord(TreatyTermType.ArrangeRoyalMarriage, 20, heroId: "demanded_heir")),
            "AI excludes hostage already promised in marriage");
        var cloned = (List<TreatyTermRecord>)AccessTools.Method(assembly.GetType("BellumCivile.UI.Parley.PeaceParleyVM"), "CloneTerms")
            .Invoke(null, new object[] { new[] { Hostage(true) } });
        check(cloned[0].HostageTier == 1 && cloned[0].HostageReceivingClanId == "loser_house" && cloned[0].WasVoluntaryOffering,
            "Parley draft reset/clone preserves hostage value, receiving house and offer flag");
        object Summary(int budget, params TreatyTermRecord[] terms) => AccessTools.Method(accounting, "Calculate")
            .Invoke(null, new object[] { terms, "winner", budget });
        int Value(object summary, string name) => (int)summary.GetType().GetProperty(name).GetValue(summary);
        var reciprocal = Summary(100, Hostage(), Hostage(true));
        check(Value(reciprocal, "DemandCost") == 30 && Value(reciprocal, "AppliedOfferingCredit") == 15
            && Value(reciprocal, "AppliedReciprocalExchangeCredit") == 0 && Value(reciprocal, "UsedWarScore") == 15,
            "Reciprocal hostage uses half-value offering credit, not prisoner exchange credit");
        check(Value(Summary(20, Hostage(), Hostage(true)), "AppliedOfferingCredit") == 10,
            "Hostage offering respects half-budget cap");
        var wealth = new TreatyTermRecord(TreatyTermType.Reparations, 200, fromKingdomId: "winner", toKingdomId: "loser", wasVoluntaryOffering: true);
        check(Value(Summary(100, Hostage(true), wealth), "AppliedOfferingCredit") == 50,
            "Hostages and wealth share one absolute offering-credit cap");
        var winner = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom)); winner.StringId = "winner";
        var loser = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom)); loser.StringId = "loser";
        var war = (WarScoreRecord)FormatterServices.GetUninitializedObject(typeof(WarScoreRecord));
        var proposal = (TreatyProposalRecord)FormatterServices.GetUninitializedObject(typeof(TreatyProposalRecord));
        var validator = AccessTools.Method(assembly.GetType("BellumCivile.TreatyDraftService"), "TryValidateAndNormalize");
        foreach (var invalid in new[] { new[] { Hostage(cost: 1) }, new[] { Hostage(), Hostage(hero: "other") },
            new[] { Hostage(), new TreatyTermRecord(TreatyTermType.WhitePeace, 0) }, new[] { reverse } })
        {
            object[] args = { war, proposal, winner, loser, invalid, null, null, false, false };
            check(!(bool)validator.Invoke(null, args) && !string.IsNullOrEmpty(args[6] as string),
                "Central treaty normalization rejects malformed hostage clauses before campaign scans");
        }
    }
}
