using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        AccessTools.Field(type, "_bloodMembers").SetValue(__instance, new Dictionary<Hero, bool>());
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

            check(Command("invalid").StartsWith("Usage:"), "Unknown offer mode shows usage without creating an offer");
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { null });
            check(Command().StartsWith("Error: Load a campaign"), "Offer command is safe at the main menu");
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            _enabled = false;
            check(Command("matrilineal").Contains("Enable Bellum"), "Offer command explains when strategic marriage is disabled");
            _enabled = true;

            check(Command("matrilineal", "not_a_player_member").Contains("No player-clan member found") && records.Count == 0,
                "Unknown player member selector cannot silently target someone else");

            string result = Command("MaTrIlInEaL");
            check(result.StartsWith("Success!") && records.Single().Other == heiress
                && records.Single().Destination == heiress.Clan && _commandNotifications == 1,
                "Matrilineal command finds the natural heiress arrangement and records it through production capture");
            check(result.Contains("joins") || result.Contains("will leave"), "Console output identifies the household transfer");
            check(Spouses.Count == 0 && _offerRewards == 0, "Requesting a test offer performs no wedding or relation reward");
            check(Command("patrilineal").Contains("already active") && _commandNotifications == 1
                && records.Single().Other == heiress, "Active offer cannot be overwritten by another test mode");
            ClearOffer();

            result = Command("patrilineal", player.StringId.ToUpperInvariant());
            check(result.StartsWith("Success!") && records.Single().Other == ordinary
                && records.Single().Destination == player.Clan,
                "Player-member StringId selector is case-insensitive and retains NPC heiress protection");
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
            check(result.Contains("npc_score=94/95") && result.Contains("closest_refused="),
                "Failed search distinguishes an NPC acceptance shortfall from household restrictions");
            var options = assembly.GetType("BellumCivile.BellumCivileOptions");
            var settingsField = AccessTools.Field(options, "_settings");
            var oldSettings = settingsField.GetValue(null);
            var settings = Activator.CreateInstance(settingsField.FieldType, nonPublic: true);
            void SetThreshold(int value) => AccessTools.Property(settingsField.FieldType, "MarriageAcceptanceThreshold").SetValue(settings, value);
            try
            {
                settingsField.SetValue(null, settings);
                int Threshold() => (int)AccessTools.PropertyGetter(options, "MarriageAcceptanceThreshold").Invoke(null, null);
                check(Threshold() == 95, "New settings retain the original strategic floor");
                SetThreshold(1500);
                check(Threshold() == 150, "Manually edited values are capped at the top of the slider");
                SetThreshold(-2);
                check(Threshold() == 50, "Manually edited values are capped at the bottom of the slider");
                SetThreshold(94);
                check(Command("matrilineal").StartsWith("Success!") && records.Single().Other == heiress,
                    "Lowering the configured floor allows the same legal strategic offer at the new threshold");
                ClearOffer();
                SetThreshold(111);
                _firstScore = 110;
                check(Command("matrilineal").Contains("npc_score=110/111") && records.Count == 0,
                    "Raising the floor refuses an otherwise legal offer previously acceptable at 95");
            }
            finally { settingsField.SetValue(null, oldSettings); }
            _firstScore = 110;
            waiting[player] = heiress;
            result = Command("any", player.Name.ToString());
            check(result.StartsWith("Error:") && result.Contains("available_player_members=0"),
                "Already-engaged player member is excluded from the test search");
            check(result.Contains("selected_member_rejected=reserved"), "Targeted member diagnostics identify an existing reservation");
            waiting.Clear();
            result = Command();
            check(result.StartsWith("Success!") && records.Single().Destination == heiress.Clan,
                "Original no-argument command continues to allow either natural arrangement");
            ClearOffer();
            _suppressCommandNotification = true;
            check(Command("matrilineal").Contains("was not registered"), "Suppressed native offer creation cannot report false success");
            check(records.Count == 0, "Suppressed publication discards the orphaned household record");
            _suppressCommandNotification = false;
            RunAlternatePlayerOffers(check, campaign, behavior, ordinary, player, ClearOffer);
            CheckScheduledDistantBudget(check, helper, heiress, player);
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

    private static void CheckScheduledDistantBudget(Action<bool, string> check, Type helper, Hero suitor, Hero player)
    {
        var marketType = helper.GetNestedType("ScheduledMarriageMarket", BindingFlags.NonPublic);
        var searchType = helper.GetNestedType("ScheduledMarriageSearch", BindingFlags.NonPublic);
        var graphType = helper.Assembly.GetType("BellumCivile.Behaviors.MarriageRealmGeography");
        var original = _commandCandidates;
        var home = Rulers.Single(entry => entry.Value == suitor.Clan).Key;
        var nearby = Rulers.Single(entry => entry.Value == player.Clan).Key;
        var others = new List<Kingdom>();
        var created = new List<Hero>();

        try
        {
            _commandCandidates = new List<Hero> { suitor };
            var graph = Activator.CreateInstance(graphType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { true }, null);
            AccessTools.Method(graphType, "Add").Invoke(graph, new[] { home, nearby });
            foreach (int i in Enumerable.Range(0, 12))
            {
                var realm = Blank<Kingdom>(); realm.StringId = "marriage_distant_realm_" + i;
                var clan = Blank<Clan>(); clan.StringId = "marriage_distant_house_" + i;
                others.Add(realm); Rulers.Add(realm, clan);
            }

            int Search(int heroesPerHouse)
            {
                foreach (var old in created) { Houses.Remove(old); Ages.Remove(old); }
                created.Clear(); _commandCandidates.RemoveRange(1, _commandCandidates.Count - 1);
                foreach (var realm in others)
                for (int i = 0; i < heroesPerHouse; i++)
                {
                    var hero = Blank<Hero>(); hero.StringId = realm.StringId + "_relative_" + i;
                    Houses[hero] = Rulers[realm]; Ages[hero] = 27;
                    created.Add(hero); _commandCandidates.Add(hero);
                }
                var market = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(marketType);
                var relatives = _commandCandidates.Concat(new[] { player }).GroupBy(hero => hero.Clan).ToDictionary(group => group.Key, group => group.ToList());
                var marketStats = AccessTools.Field(marketType, "Stats");
                marketStats.SetValue(market, Activator.CreateInstance(marketStats.FieldType, nonPublic: true));
                AccessTools.Field(marketType, "Realms").SetValue(market,
                    relatives.Keys.ToDictionary(house => house, house => Rulers.Single(entry => entry.Value == house).Key));
                AccessTools.Field(marketType, "_bySex").SetValue(market, relatives.ToDictionary(entry => entry.Key, entry => entry.Value.ToLookup(hero => hero.IsFemale)));
                AccessTools.Field(marketType, "_geography").SetValue(market, graph);

                var search = Activator.CreateInstance(searchType, BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new[] { suitor.Clan, (object)false, true, null, market, 800 }, null);
                var advance = AccessTools.Method(searchType, "Advance");
                var complete = AccessTools.PropertyGetter(searchType, "Complete");
                while (!(bool)complete.Invoke(search, null)) advance.Invoke(search, new object[] { 7 });
                var stats = AccessTools.Field(searchType, "Stats").GetValue(search);
                check(AccessTools.Field(searchType, "Best").GetValue(search) != null
                    && (int)AccessTools.Field(stats.GetType(), "DistantPasses").GetValue(stats) == 1,
                    "A scored neighboring option still permits a limited Crown-to-Crown search");
                return (int)AccessTools.Field(stats.GetType(), "DistantPairChecks").GetValue(stats);
            }

            check(Search(1) == 8, "Twelve distant houses with suitable candidates are bounded to eight house searches");
            check(Search(12) == 48, "Dense distant households are bounded to 48 pair checks across hourly slices");
        }
        finally
        {
            _commandCandidates = original;
            foreach (var old in created) { Houses.Remove(old); Ages.Remove(old); }
            foreach (var realm in others) Rulers.Remove(realm);
        }
    }
}
