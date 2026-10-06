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
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;
using TaleWorlds.Localization;

// Runs inside the resolve tests' campaign fixture, with real tooltip/proposer logic.
internal static class ClientLibertyTooltipTests
{
    private static float _available, _targetScore;
    private static bool _allowed, _atWar, _revampEnabled;
    private static int _scoreCalls;
    private static Clan _deterred;
    private static readonly List<KingdomDecision> Pending = new List<KingdomDecision>();
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool AtWar(ref bool __result) { __result = _atWar; return false; }
    private static bool RevampEnabled(ref bool __result) { __result = _revampEnabled; return false; }
    private static bool Name(object __instance, ref TextObject __result)
    {
        __result = new TextObject(__instance is Clan clan ? clan.StringId : ((Kingdom)__instance).StringId);
        return false;
    }
    private static bool Decisions(ref MBReadOnlyList<KingdomDecision> __result)
    { __result = new MBReadOnlyList<KingdomDecision>(Pending); return false; }
    private static bool Budget(Clan clan, MethodBase __originalMethod, ref object __result)
    {
        __result = Activator.CreateInstance(((MethodInfo)__originalMethod).ReturnType,
            new object[] { clan, _available, 200f, 200f });
        return false;
    }
    private static bool Score(Clan clan, MethodBase __originalMethod, ref object __result)
    {
        _scoreCalls++;
        var type = ((MethodInfo)__originalMethod).ReturnType;
        __result = Activator.CreateInstance(type);
        AccessTools.Property(type, "Score").SetValue(__result, clan == _deterred ? 0f : _targetScore);
        return false;
    }
    private static bool Permission(ref bool __result, ref TextObject reason)
    { __result = _allowed; reason = new TextObject("fixture permission refusal"); return false; }

    internal static void Run(Action<bool, string> check, WarPeaceRevampBehavior war,
        Kingdom client, Kingdom suzerain, Clan clan, Dictionary<string, float> wills)
    {
        var assembly = typeof(ClientKingdomBehavior).Assembly;
        var tooltip = assembly.GetType("BellumCivile.UI.Map.ClientLibertyTooltip");
        var proposal = assembly.GetType("BellumCivile.ClientLiberationProposalAssessment");
        var harmony = new Harmony("bellum.test.client_liberty_tooltip");
        void Patch(MethodBase method, string prefix) => harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(ClientLibertyTooltipTests), prefix));
        var find = AccessTools.Method(proposal, "FindBest");
        var status = AccessTools.Method(tooltip, "GetStatus");
        var build = AccessTools.Method(tooltip, "Build");
        var reasons = AccessTools.Method(tooltip, "BuildRealmReasons");
        object Find(ClientLibertyAssessment realm) => find.Invoke(null, new object[] { realm });
        string Status(ClientLibertyAssessment realm) => status.Invoke(null, new[] { realm, Find(realm) }).ToString();
        Dictionary<string, string> Rows(ClientLibertyAssessment realm) => ((IEnumerable)build.Invoke(null, new object[] { realm }))
            .Cast<object>().Select(row => new {
                Label = (string)AccessTools.Property(row.GetType(), "DefinitionLabel").GetValue(row),
                Value = (string)AccessTools.Property(row.GetType(), "ValueLabel").GetValue(row)
            }).Where(row => !string.IsNullOrEmpty(row.Label)).ToDictionary(row => row.Label, row => row.Value);
        string[] Paragraphs(ClientLibertyAssessment realm) => ((IEnumerable)build.Invoke(null, new object[] { realm }))
            .Cast<object>().Where(row => string.IsNullOrEmpty((string)AccessTools.Property(row.GetType(), "DefinitionLabel").GetValue(row)))
            .Select(row => (string)AccessTools.Property(row.GetType(), "ValueLabel").GetValue(row))
            .Where(value => !string.IsNullOrEmpty(value)).ToArray();
        ClientClanLibertyAssessment Entry(Clan member, float desire, float power = 700)
            => new ClientClanLibertyAssessment(member, desire, new[] { new ClientLibertyReason("client condition", desire) }) { Power = power };
        var realm = new ClientLibertyAssessment {
            ClientKingdom = client, SuzerainKingdom = suzerain, RealmLibertyDesire = 80,
            CanAttemptLiberation = true, LiberationReadiness = 100,
            EffectiveClientPower = 800, SuzerainBlocPower = 1000, RequiredPowerRatio = .8f,
            SuzerainPower = 700, OtherClientsPower = 200, AlliesPower = 100,
            Clans = new[] { Entry(clan, 80) }
        };
        try
        {
            _available = 500; _targetScore = 60; _allowed = true; _scoreCalls = 0; _deterred = null; Pending.Clear();
            _atWar = false; _revampEnabled = true;
            Patch(AccessTools.Method(typeof(Kingdom), "IsAtWarWith"), nameof(AtWar));
            Patch(AccessTools.Method(typeof(WarPeaceRevampBehavior), "IsRevampEnabled"), nameof(RevampEnabled));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsUnderMercenaryService"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Name"), nameof(Name));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Name"), nameof(Name));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "UnresolvedDecisions"), nameof(Decisions));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.NpcInfluenceBudgetService"), "Assess"), nameof(Budget));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.WarPeace.WarTargetScoringService"), "ScoreLiberationTarget"), nameof(Score));
            Patch(AccessTools.Method(proposal, "CanMakeDecision"), nameof(Permission));

            wills[clan.StringId] = 60;
            check(Status(realm).Contains("sufficient to propose") && Status(realm).Contains("council approval"),
                "Ready summary includes resolve and all proposal gates without promising a declaration");
            var rows = Rows(realm);
            check(rows["Liberty Desire"] == "80 (Defiant, 60+)" && rows["Liberation Readiness"] == "100% / 100%",
                "Compact headline shows defiance threshold and the actual power readiness requirement");
            check(rows["Suzerain"] == suzerain.StringId && rows["Principal causes"] == string.Empty
                && rows["Client condition"] == "+80" && rows["Most defiant clan"] == clan.StringId + " (80)",
                "Compact tooltip keeps the suzerain, cause breakdown and most-defiant footer");
            check(rows.Keys.SequenceEqual(new[] { "Client " + client.StringId, "Suzerain", "Liberty Desire",
                "Liberation Readiness", "Principal causes", "Client condition", "Most defiant clan" })
                && Paragraphs(realm).Length == 1,
                "Headline precedes causes and footer without raw power, War Will or influence budget rows");
            check(wills[clan.StringId] == 60, "Tooltip never credits resolve to stored War Will");

            var mapType = assembly.GetType("BellumCivile.UI.Map.ClientStateMapWidgetItemVM");
            object map = Activator.CreateInstance(mapType, new object[] { null });
            var banner = AccessTools.Field(mapType, "_clientBanner");
            banner.SetValue(map, FormatterServices.GetUninitializedObject(banner.FieldType));
            var updateMap = AccessTools.Method(mapType, "Update");
            bool MapFlag(string name) => (bool)AccessTools.Property(mapType, name).GetValue(map);
            foreach (float desire in new[] { 39.5f, 39.9f, 40f, 39.9f, 59.5f, 59.9f, 60f, 59.9f, 100f })
            {
                realm.RealmLibertyDesire = desire;
                updateMap.Invoke(map, new object[] { realm });
                string state = desire < 40f ? "Content" : desire < 60f ? "Restless" : "Defiant, 60+";
                check(MapFlag("IsContent") == (desire < 40f) && MapFlag("IsRestless") == (desire >= 40f && desire < 60f)
                    && MapFlag("IsDefiant") == (desire >= 60f)
                    && Rows(realm)["Liberty Desire"] == desire.ToString("0.#") + " (" + state + ")",
                    "Widget and tooltip use actual thresholds even when rounded display is unchanged: " + desire);
            }
            realm.RealmLibertyDesire = 80;
            AccessTools.Field(mapType, "_assessment").SetValue(map, new ClientLibertyAssessment
                { ClientKingdom = client, RealmLibertyDesire = 5 });
            var freshRows = ((IEnumerable)AccessTools.Method(mapType, "BuildTooltipProperties").Invoke(map, null)).Cast<object>();
            check(freshRows.Any(row => (string)AccessTools.Property(row.GetType(), "DefinitionLabel").GetValue(row) == "Liberty Desire"
                && (string)AccessTools.Property(row.GetType(), "ValueLabel").GetValue(row) == "80 (Defiant, 60+)"),
                "Map hover rebuilds the assessment instead of displaying its stale widget snapshot");
            var overview = assembly.GetType("BellumCivile.UI.Diplomacy.BellumDiplomacyOverviewVM");
            var clientHints = overview.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                .Select(method => PatchProcessor.GetOriginalInstructions(method).Select(instruction => instruction.operand).OfType<MethodInfo>().ToList())
                .Where(calls => calls.Contains(build)).ToList();
            check(clientHints.Count == 2 && clientHints.All(calls => calls.Any(method => method.Name == "BuildLibertyAssessment")),
                "Both Kingdom overview client perspectives refresh and use the same tooltip builder as the map widget");

            _scoreCalls = 0; wills[clan.StringId] = 59;
            check(Status(realm).Contains("reluctant to act") && _scoreCalls == 0,
                "Insufficient willingness blocks without running strategic scoring");
            realm.Clans = new[] { Entry(clan, 59) }; wills[clan.StringId] = 100;
            check(Status(realm).Contains("determined enough"), "Personal desire remains a separate proposer gate");
            realm.Clans = new[] { Entry(clan, 80) }; wills[clan.StringId] = 60;
            _available = 399;
            check(Status(realm).Contains("lack the influence") && _scoreCalls == 0,
                "Low influence blocks without scoring or recording a spending attempt");
            _available = 400; _targetScore = 9;
            check(Status(realm).Contains("judge war with the suzerain unwise"), "A willing and funded clan can still be deterred from targeting its suzerain");
            _targetScore = (float)AccessTools.Property(assembly.GetType("BellumCivile.BellumCivileOptions"), "WarTargetMinimumScore").GetValue(null);
            check(Status(realm).Contains("sufficient to propose"), "The exact strategic threshold qualifies");
            _allowed = false;
            check(Status(realm).Contains("fixture permission refusal"), "Native permission refusal is retained in the displayed blocker");
            _allowed = true;

            var other = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan)); other.StringId = "tooltip_viable";
            realm.Clans = new[] { Entry(clan, 100), Entry(other, 80) };
            wills[clan.StringId] = 0; wills[other.StringId] = 60;
            check(((ClientClanLibertyAssessment)AccessTools.Field(proposal, "Candidate").GetValue(Find(realm))).Clan == other,
                "A viable proposer outranks the most defiant but exhausted clan");
            check(Rows(realm)["Most defiant clan"] == clan.StringId + " (100)" && Status(realm).Contains("sufficient to propose"),
                "Footer names the most defiant clan independently of the viable proposer used by the status");
            wills[clan.StringId] = 100; _deterred = clan;
            check(((ClientClanLibertyAssessment)AccessTools.Field(proposal, "Candidate").GetValue(Find(realm))).Clan == other,
                "Strategic rejection of the strongest candidate does not hide another viable proposer");
            _deterred = null;

            _scoreCalls = 0;
            Pending.Add((DeclareWarDecision)FormatterServices.GetUninitializedObject(typeof(DeclareWarDecision)));
            check(Status(realm).Contains("pending war decision") && _scoreCalls == 0, "Pending decisions block readiness and skip scoring");
            Pending.Clear();
            realm.CanAttemptLiberation = false; realm.CooldownRemainingDays = 1.2f;
            check(Paragraphs(realm).Length == 2 && Paragraphs(realm).Last().Contains("2 more days")
                && !Status(realm).Contains("sufficient to propose") && _scoreCalls == 0,
                "Binding period is separate, rounded up, and cannot report proposal eligibility");
            realm.LiberationReadiness = 31.8f;
            check(Paragraphs(realm).First().Contains("lacks the power") && Paragraphs(realm).Last().Contains("2 more days"),
                "Binding notice does not hide insufficient power");
            _atWar = true;
            check(Paragraphs(realm).Length == 1 && Status(realm).Contains("war is already underway"),
                "Active liberation war replaces binding and power obstacle notices");
            _atWar = false; _revampEnabled = false;
            check(Paragraphs(realm).Length == 1 && Status(realm).Contains("Revamp is disabled"),
                "Disabled revamp is explicit and never reports readiness to rebel");
            _revampEnabled = true;
            realm.CooldownRemainingDays = 0; realm.RealmLibertyDesire = 59.9f;
            check(Status(realm).Contains("Discontent is growing"), "Restless realms are not described as broadly content");
            realm.RealmLibertyDesire = 39.9f;
            check(Status(realm).Contains("broadly content"), "Only realms below 40 are described as content");
            realm.RealmLibertyDesire = 80; realm.LiberationReadiness = 87.5f;
            check(Status(realm).Contains("lacks the power") && _scoreCalls == 0, "Insufficient power is distinguished from desire or will");
            check(Rows(realm)["Liberation Readiness"] == 87.5f.ToString("0.#") + "% / 100%", "Power readiness retains meaningful fractional values");
            realm.LiberationReadiness = 99.9f;
            check(Status(realm).Contains("lacks the power"), "Readiness below 100 does not satisfy the power gate");
            realm.LiberationReadiness = 100;
            check(Status(realm).Contains("not currently available"), "Other realm restrictions remain authoritative at sufficient power");

            realm.Clans = new[] {
                new ClientClanLibertyAssessment(clan, 100, new[] { new ClientLibertyReason("client condition", 120) }) { Power = 3 },
                new ClientClanLibertyAssessment(other, 0, new[] { new ClientLibertyReason("client condition", -10) }) { Power = 1 }
            };
            var weighted = (IEnumerable<KeyValuePair<string, float>>)reasons.Invoke(null, new object[] { realm });
            check(weighted.Sum(row => row.Value) == 75 && weighted.Single(row => row.Key == "desire limits").Value == -12.5f,
                "Weighted causes reconcile to clamped individual desire, not the unclamped sum");
            realm.Clans = new[] { Entry(clan, 80) };
            wills.Remove(clan.StringId); int count = wills.Count;
            Rows(realm);
            check(wills.Count == count && !wills.ContainsKey(clan.StringId), "Hovering never initializes missing War Will records");
            var ranked = (IDictionary)AccessTools.Field(typeof(WarPeaceRevampBehavior), "_targetScoresByClanId").GetValue(war);
            check(ranked.Count == 0 && Pending.Count == 0, "Hovering creates neither AI target preferences nor pending decisions");
            realm.CanAttemptLiberation = true; realm.LiberationReadiness = 100; realm.Clans = Array.Empty<ClientClanLibertyAssessment>();
            check(Status(realm).Contains("No eligible clan"), "An empty client realm cannot be reported as ready");
            check(!Rows(realm).ContainsKey("Most defiant clan"), "Empty client realm has no invented most-defiant footer");
            check(((IList)build.Invoke(null, new object[] { null })).Count == 0, "Ended clientage yields an empty tooltip");
        }
        finally
        {
            Pending.Clear(); harmony.UnpatchAll(harmony.Id);
        }
    }
}
