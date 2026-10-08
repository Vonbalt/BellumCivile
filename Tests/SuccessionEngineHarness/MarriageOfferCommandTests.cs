using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

internal static partial class MarriageHouseholdTests
{
    private static List<Hero> _commandCandidates;
    private static Hero _commandPlayer;
    private static int _commandNotifications;
    private static bool _suppressCommandNotification;

    private static bool CommandContext(object __instance)
    {
        // Keep the real pair/household/acceptance search; supply a small candidate roster.
        var assembly = typeof(BellumMarriageModel).Assembly;
        var type = __instance.GetType();
        AccessTools.Field(type, "_households").SetValue(__instance,
            Activator.CreateInstance(assembly.GetType("BellumCivile.MarriageHouseholdPolicy"), true));
        AccessTools.Field(type, "CrownHeirs").SetValue(__instance, new HashSet<Hero>(Crowns.Values));
        var participants = new Dictionary<Clan, List<Hero>> { [_player] = new List<Hero> { _commandPlayer } };
        foreach (var house in _commandCandidates.GroupBy(h => h.Clan)) participants[house.Key] = house.ToList();
        AccessTools.Field(type, "Participants").SetValue(__instance, participants);
        AccessTools.Field(type, "Realms").SetValue(__instance,
            participants.Keys.ToDictionary(h => h, h => Rulers.First(p => p.Value == h).Key));
        return false;
    }

    private static bool CommandKingdom(Clan __instance, ref Kingdom __result)
    { __result = Rulers.FirstOrDefault(p => p.Value == __instance).Key; return false; }

    private static bool PublishTestOffer(MarriageOfferCampaignBehavior __instance, Hero __0, Hero __1)
    {
        // The production capture prefix has already run. Only the engine notification is substituted.
        if (!_suppressCommandNotification)
        {
            AccessTools.Field(typeof(MarriageOfferCampaignBehavior), "_currentOfferedPlayerClanHero").SetValue(__instance, __0);
            AccessTools.Field(typeof(MarriageOfferCampaignBehavior), "_currentOfferedOtherClanHero").SetValue(__instance, __1);
            _commandNotifications++;
        }
        return false;
    }

    private static void RunOfferCommand(Action<bool, string> check, Campaign campaign,
        PlayerMarriageAgreementBehavior behavior, Hero heiress, Hero player)
    {
        var assembly = typeof(BellumMarriageModel).Assembly;
        var helper = assembly.GetType("BellumCivile.Behaviors.BellumMarriageStrategyHelper");
        var context = helper.GetNestedType("EvaluationContext", System.Reflection.BindingFlags.NonPublic);
        var harmony = new Harmony("bellum.test.marriage_offer_command");
        var ordinary = Blank<Hero>(); ordinary.StringId = "ordinary_test_bride";
        Houses[ordinary] = heiress.Clan; Ages[ordinary] = 28; Women.Add(ordinary);
        _commandCandidates = new List<Hero> { heiress, ordinary }; _commandPlayer = player;
        _commandNotifications = 0; _suppressCommandNotification = false; _offerReady = true;
        float oldFirst = _firstScore, oldSecond = _secondScore;
        _firstScore = 110; _secondScore = 120;
        var records = (List<PlayerMarriageAgreement>)AccessTools.Field(AgreementBehavior, "_agreements").GetValue(behavior);
        var waiting = (Dictionary<Hero, Hero>)AccessTools.Field(typeof(MarriageOfferCampaignBehavior),
            "_acceptedMarriageOffersThatWaitingForAvailability").GetValue(_offers);
        void ClearOffer()
        {
            AccessTools.Method(typeof(MarriageOfferCampaignBehavior), "FinalizeMarriageOffer").Invoke(_offers, null);
            records.Clear(); waiting.Clear();
        }
        string Command(params string[] args) => CheatCommands.ForceMarriageOffer(args.ToList());
        try
        {
            harmony.Patch(AccessTools.GetDeclaredConstructors(context).Single(),
                prefix: new HarmonyMethod(typeof(MarriageHouseholdTests), nameof(CommandContext)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"),
                prefix: new HarmonyMethod(typeof(MarriageHouseholdTests), nameof(CommandKingdom)));
            harmony.Patch(AccessTools.Method(typeof(TaleWorlds.Core.MBRandom), "RandomInt", new[] { typeof(int) }),
                prefix: new HarmonyMethod(typeof(MarriageHouseholdTests), nameof(Zero)));
            harmony.Patch(AccessTools.Method(typeof(MarriageOfferCampaignBehavior), "CreateMarriageOffer"),
                prefix: new HarmonyMethod(typeof(MarriageHouseholdTests), nameof(PublishTestOffer)) { priority = Priority.Last });

            check(Command("invalid").StartsWith("Usage:") && Command("matrilineal", "patrilineal").StartsWith("Usage:"),
                "Unknown or multiple offer modes show usage without creating an offer");
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { null });
            check(Command().StartsWith("Error: Load a campaign"), "Offer command is safe at the main menu");
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            _enabled = false;
            check(Command("matrilineal").Contains("Enable Bellum"), "Offer command explains when strategic marriage is disabled");
            _enabled = true;

            string result = Command("MaTrIlInEaL");
            check(result.StartsWith("Success!") && records.Single().Other == heiress
                && records.Single().Destination == heiress.Clan && _commandNotifications == 1,
                "Matrilineal command finds the natural heiress arrangement and records it through production capture");
            check(result.Contains("joins") || result.Contains("will leave"), "Console output identifies the household transfer");
            check(Spouses.Count == 0 && _offerRewards == 0, "Requesting a test offer performs no wedding or relation reward");
            check(Command("patrilineal").Contains("already active") && _commandNotifications == 1
                && records.Single().Other == heiress, "Active offer cannot be overwritten by another test mode");
            ClearOffer();

            result = Command("patrilineal");
            check(result.StartsWith("Success!") && records.Single().Other == ordinary
                && records.Single().Destination == player.Clan,
                "Patrilineal command finds a different eligible pair without moving the protected heiress");
            ClearOffer();
            _commandCandidates = new List<Hero> { heiress };
            int notifications = _commandNotifications;
            result = Command("patrilineal");
            check(result.StartsWith("Error: No eligible patrilineal") && result.Contains("eligible_matrilineal=1")
                && records.Count == 0 && _commandNotifications == notifications,
                "Unavailable mode reports diagnostics instead of changing heir status or forcing the other arrangement");

            _firstScore = 94;
            result = Command("matrilineal");
            check(result.StartsWith("Error:") && result.Contains("acceptance_rejected=1"), "Test offers retain the NPC's 95 acceptance floor");
            _firstScore = 110;
            waiting[player] = heiress;
            result = Command();
            check(result.StartsWith("Error:") && result.Contains("available_player_members=0"),
                "Already-engaged player member is excluded from the test search");
            waiting.Clear();
            result = Command();
            check(result.StartsWith("Success!") && records.Single().Destination == heiress.Clan,
                "Original no-argument command continues to allow either natural arrangement");
            ClearOffer();
            _suppressCommandNotification = true;
            check(Command("matrilineal").Contains("was not registered"), "Suppressed native offer creation cannot report false success");
        }
        finally
        {
            ClearOffer(); _enabled = true; _suppressCommandNotification = false;
            _firstScore = oldFirst; _secondScore = oldSecond;
            Houses.Remove(ordinary); Ages.Remove(ordinary); Women.Remove(ordinary);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            harmony.UnpatchAll(harmony.Id);
        }
    }
}
