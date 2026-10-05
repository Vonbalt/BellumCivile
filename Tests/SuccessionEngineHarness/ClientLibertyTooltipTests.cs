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
    private static bool _allowed;
    private static int _scoreCalls;
    private static Clan _deterred;
    private static readonly List<KingdomDecision> Pending = new List<KingdomDecision>();
    private static bool No(ref bool __result) { __result = false; return false; }
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
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsUnderMercenaryService"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Name"), nameof(Name));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Name"), nameof(Name));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "UnresolvedDecisions"), nameof(Decisions));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.NpcInfluenceBudgetService"), "Assess"), nameof(Budget));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.WarPeace.WarTargetScoringService"), "ScoreLiberationTarget"), nameof(Score));
            Patch(AccessTools.Method(proposal, "CanMakeDecision"), nameof(Permission));

            wills[clan.StringId] = 60;
            check(Status(realm).Contains("Ready to propose"), "Tooltip includes resolve and all proposal gates before reporting readiness");
            var rows = Rows(realm);
            check(rows["War Will"] == "60" && rows["Liberation resolve"] == "+15"
                && rows["Willingness to propose"] == "75 / 75 required", "Tooltip separates real War Will, resolve and effective willingness");
            check(rows["Effective client power"] == "800" && rows["Opposing bloc power"] == "1000"
                && rows["Suzerain's contribution"] == "700" && rows["Other clients' contribution"] == "200"
                && rows["Allies' contribution"] == "100" && rows["Required power ratio"] == "80%"
                && rows["Power needed"] == "800", "Tooltip renders the actual power snapshot and required ratio");
            check(rows["Influence"] == "500 / 400 required" && rows["Proposal cost / reserve"] == "200 / 200",
                "Tooltip explains the influence requirement including the protected reserve");
            check(wills[clan.StringId] == 60, "Tooltip never credits resolve to stored War Will");

            var mapType = assembly.GetType("BellumCivile.UI.Map.ClientStateMapWidgetItemVM");
            object map = FormatterServices.GetUninitializedObject(mapType);
            AccessTools.Field(mapType, "_assessment").SetValue(map, new ClientLibertyAssessment
                { ClientKingdom = client, RealmLibertyDesire = 5 });
            var freshRows = ((IEnumerable)AccessTools.Method(mapType, "BuildTooltipProperties").Invoke(map, null)).Cast<object>();
            check(freshRows.Any(row => (string)AccessTools.Property(row.GetType(), "DefinitionLabel").GetValue(row) == "Liberty Desire"
                && (string)AccessTools.Property(row.GetType(), "ValueLabel").GetValue(row) == "80 / 60 required"),
                "Map hover rebuilds the assessment instead of displaying its stale widget snapshot");
            var overview = assembly.GetType("BellumCivile.UI.Diplomacy.BellumDiplomacyOverviewVM");
            var clientHints = overview.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                .Select(method => PatchProcessor.GetOriginalInstructions(method).Select(instruction => instruction.operand).OfType<MethodInfo>().ToList())
                .Where(calls => calls.Contains(build)).ToList();
            check(clientHints.Count == 2 && clientHints.All(calls => calls.Any(method => method.Name == "BuildLibertyAssessment")),
                "Both Kingdom overview client perspectives refresh and use the same tooltip builder as the map widget");

            _scoreCalls = 0; wills[clan.StringId] = 59;
            check(Status(realm).Contains("sufficient willingness") && _scoreCalls == 0,
                "Insufficient willingness blocks without running strategic scoring");
            realm.Clans = new[] { Entry(clan, 59) }; wills[clan.StringId] = 100;
            check(Status(realm).Contains("personal desire"), "Personal desire remains a separate proposer gate");
            realm.Clans = new[] { Entry(clan, 80) }; wills[clan.StringId] = 60;
            _available = 399;
            check(Status(realm).Contains("lack the influence") && _scoreCalls == 0,
                "Low influence blocks without scoring or recording a spending attempt");
            _available = 400; _targetScore = 9;
            check(Status(realm).Contains("deterred"), "A willing and funded clan can still be deterred from targeting its suzerain");
            _targetScore = (float)AccessTools.Property(assembly.GetType("BellumCivile.BellumCivileOptions"), "WarTargetMinimumScore").GetValue(null);
            check(Status(realm).Contains("Ready to propose"), "The exact strategic threshold qualifies");
            _allowed = false;
            check(Status(realm).Contains("fixture permission refusal"), "Native permission refusal is retained in the displayed blocker");
            _allowed = true;

            var other = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan)); other.StringId = "tooltip_viable";
            realm.Clans = new[] { Entry(clan, 100), Entry(other, 80) };
            wills[clan.StringId] = 0; wills[other.StringId] = 60;
            check(((ClientClanLibertyAssessment)AccessTools.Field(proposal, "Candidate").GetValue(Find(realm))).Clan == other,
                "A viable proposer outranks the most defiant but exhausted clan");
            wills[clan.StringId] = 100; _deterred = clan;
            check(((ClientClanLibertyAssessment)AccessTools.Field(proposal, "Candidate").GetValue(Find(realm))).Clan == other,
                "Strategic rejection of the strongest candidate does not hide another viable proposer");
            _deterred = null;

            _scoreCalls = 0;
            Pending.Add((DeclareWarDecision)FormatterServices.GetUninitializedObject(typeof(DeclareWarDecision)));
            check(Status(realm).Contains("already pending") && _scoreCalls == 0, "Pending decisions block readiness and skip scoring");
            Pending.Clear();
            realm.CanAttemptLiberation = false; realm.CooldownRemainingDays = 1.2f;
            check(Status(realm).Contains("2 more days") && _scoreCalls == 0, "Binding period is rounded up and skips scoring");
            realm.CooldownRemainingDays = 0; realm.RealmLibertyDesire = 59.9f;
            check(Status(realm).Contains("desire for independence is too low"), "Restless realms are not described as broadly content");
            realm.RealmLibertyDesire = 80; realm.LiberationReadiness = 87.5f;
            check(Status(realm).Contains("insufficient power") && _scoreCalls == 0, "Insufficient power is distinguished from desire or will");
            check(Rows(realm)["Power readiness"] == 87.5f.ToString("0.#") + "% / 100% required", "Power readiness retains meaningful fractional values");

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
            check(((IList)build.Invoke(null, new object[] { null })).Count == 0, "Ended clientage yields an empty tooltip");
        }
        finally
        {
            Pending.Clear(); harmony.UnpatchAll(harmony.Id);
        }
    }
}
