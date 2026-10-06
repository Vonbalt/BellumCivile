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
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;

internal static class ClientLiberationResolveTests
{
    private sealed class Manager : ICampaignBehaviorManager
    {
        internal readonly List<CampaignBehaviorBase> Items = new List<CampaignBehaviorBase>();
        public T GetBehavior<T>() => Items.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetBehaviors<T>() => Items.OfType<T>();
        public void AddBehavior(CampaignBehaviorBase behavior) => Items.Add(behavior);
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase => Items.RemoveAll(b => b is T);
        public void ClearBehaviors() => Items.Clear();
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> behaviors) => Items.AddRange(behaviors);
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }

    private static ClientKingdomBehavior _clients;
    private static Kingdom _client, _suzerain, _target;
    private static Clan _clan;
    private static Hero _leader;
    private static float _desire;
    private static bool _eligible, _includeTarget;
    private static int _rankings;
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Zero(ref int __result) { __result = 0; return false; }
    private static bool Trait(ref TraitObject __result) { __result = null; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _client; return false; }
    private static bool Crown(ref Clan __result) { __result = _clan; return false; }
    private static bool Leader(ref Hero __result) { __result = _leader; return false; }
    private static bool Instance(ref ClientKingdomBehavior __result) { __result = _clients; return false; }
    private static bool Resolve(string kingdomId, ref Kingdom __result)
    { __result = kingdomId == _suzerain.StringId ? _suzerain : null; return false; }
    private static bool Decisions(ref MBReadOnlyList<KingdomDecision> __result)
    { __result = new MBReadOnlyList<KingdomDecision>(new List<KingdomDecision>()); return false; }
    private static bool Enemies(ref IEnumerable<Kingdom> __result)
    { __result = Enumerable.Empty<Kingdom>(); return false; }
    private static ClientLibertyAssessment MakeAssessment(float bonus = 0)
    {
        return new ClientLibertyAssessment {
            ClientKingdom = _client, SuzerainKingdom = _suzerain, CanAttemptLiberation = _eligible,
            RealmLibertyDesire = 80, LiberationReadiness = 110, BlockReason = "fixture blocked",
            Clans = new[] { new ClientClanLibertyAssessment(_clan, Math.Min(100, _desire + bonus), null) }
        };
    }
    private static bool Assessment(ref ClientLibertyAssessment __result)
    { __result = MakeAssessment(); return false; }
    private static bool Boosted(float bonus, ref ClientLibertyAssessment __result)
    { __result = MakeAssessment(bonus); return false; }
    private static bool ClanDesire(float courtBonus, ref ClientClanLibertyAssessment __result)
    { __result = new ClientClanLibertyAssessment(_clan, Math.Min(100, _desire + courtBonus), null); return false; }
    private static bool Targets(MethodBase __originalMethod, ref object __result)
    {
        _rankings++;
        var itemType = ((MethodInfo)__originalMethod).ReturnType.GetGenericArguments()[0];
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
        if (_includeTarget)
        {
            var item = Activator.CreateInstance(itemType);
            AccessTools.Property(itemType, "TargetKingdom").SetValue(item, _target);
            AccessTools.Property(itemType, "Score").SetValue(item, 60f);
            list.Add(item);
        }
        __result = list;
        return false;
    }
    private static bool EmptyFront(MethodBase __originalMethod, ref object __result)
    { __result = FormatterServices.GetUninitializedObject(((MethodInfo)__originalMethod).ReturnType); return false; }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var previousCampaign = Campaign.Current;
        var harmony = new Harmony("bellum.test.client_liberation_resolve");
        void Patch(MethodBase method, string prefix, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            if (method == null) throw new InvalidOperationException($"Missing engine hook {prefix} at line {line}");
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(ClientLiberationResolveTests), prefix));
        }
        var clientsType = typeof(ClientKingdomBehavior);
        var warType = typeof(WarPeaceRevampBehavior);
        var assembly = clientsType.Assembly;
        _clients = new ClientKingdomBehavior();
        var war = new WarPeaceRevampBehavior();
        _client = Blank<Kingdom>(); _client.StringId = "resolve_client";
        _suzerain = Blank<Kingdom>(); _suzerain.StringId = "resolve_suzerain";
        _target = _suzerain;
        var foreign = Blank<Kingdom>(); foreign.StringId = "resolve_foreign";
        _clan = Blank<Clan>(); _clan.StringId = "resolve_clan";
        _leader = Blank<Hero>();
        var records = (List<ClientKingdomRecord>)AccessTools.Field(clientsType, "_clients").GetValue(_clients);
        records.Add(new ClientKingdomRecord(_client.StringId, _suzerain.StringId, 0, false, 0));
        var wills = (Dictionary<string, float>)AccessTools.Field(warType, "_warWillByClanId").GetValue(war);
        var manager = new Manager(); manager.AddBehavior(war); manager.AddBehavior(_clients);
        var campaign = Blank<Campaign>(); campaign.AddCampaignBehaviorManager(manager);
        var adjustedWill = AccessTools.Method(clientsType, "GetLiberationWarWill");
        var vote = AccessTools.Method(warType, "TryEvaluateWarSupport");
        var decision = Blank<DeclareWarDecision>();
        bool CanPropose(float desire, float will)
        {
            _desire = desire; wills[_clan.StringId] = will;
            return _clients.TryCanProposeLiberation(_clan, out _, out _);
        }
        float Support(bool yes)
        {
            AccessTools.Field(typeof(DeclareWarDecision), "FactionToDeclareWarOn").SetValue(decision, _target);
            object[] args = { decision, _clan, yes, 0f };
            check((bool)vote.Invoke(war, args), "Production council evaluation handles the fixture");
            return (float)args[3];
        }
        try
        {
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            Patch(AccessTools.PropertyGetter(clientsType, "Instance"), nameof(Instance));
            Patch(AccessTools.Method(clientsType, "ResolveKingdom"), nameof(Resolve));
            Patch(AccessTools.Method(clientsType, "BuildLibertyAssessment"), nameof(Assessment));
            Patch(AccessTools.Method(clientsType, "BuildLibertyAssessmentWithBonus"), nameof(Boosted));
            Patch(AccessTools.Method(clientsType, "CalculateClanLiberty"), nameof(ClanDesire));
            Patch(AccessTools.Method(clientsType, "IsLawfulSuzerainty"), nameof(No));
            Patch(AccessTools.Method(clientsType, "IsEligiblePoliticalClan"), nameof(Yes));
            Patch(AccessTools.Method(warType, "IsRevampEnabled"), nameof(Yes));
            Patch(AccessTools.Method(warType, "IsValidEvaluatedClan"), nameof(Yes));
            Patch(AccessTools.Method(warType, "GetRankedTargets"), nameof(Targets));
            Patch(AccessTools.Method(warType, "GetActiveDirectWarKingdoms"), nameof(Enemies));
            Patch(AccessTools.Method(warType, "IsTemporaryWarKingdom"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Crown));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "UnresolvedDecisions"), nameof(Decisions));
            Patch(AccessTools.Method(typeof(Kingdom), "IsAtWarWith"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(KingdomDecision), "Kingdom"), nameof(Realm));
            var getTrait = PatchProcessor.GetOriginalInstructions(vote).Select(instruction => instruction.operand)
                .OfType<MethodInfo>().First(method => method.Name == "GetTraitLevel");
            Patch(getTrait, nameof(Zero));
            foreach (string name in new[] { "Valor", "Mercy", "Calculating" })
                Patch(AccessTools.PropertyGetter(typeof(DefaultTraits), name), nameof(Trait));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "ValidRealm"), nameof(Yes));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.WarPeace.WarFrontReadinessService"), "Assess"), nameof(EmptyFront));

            _eligible = true; _includeTarget = false;
            check(CanPropose(80, 60), "Personal desire 80 plus real War Will 60 qualifies for liberation");
            check(war.GetWarWill(_clan) == 60, "Eligibility never credits resolve to stored War Will");
            check(!CanPropose(80, 59.9f), "Liberation willingness just below 75 remains blocked");
            check(CanPropose(100, 45) && !CanPropose(100, 44.9f), "Maximum resolve retains a real War Will floor of 45");
            check(!CanPropose(100, 0), "A fully exhausted clan cannot initiate liberation");
            check(!CanPropose(59, 100), "High War Will cannot bypass personal desire eligibility");
            _eligible = false;
            check(!CanPropose(100, 100), "Realm readiness and binding-settlement vetoes remain authoritative");
            _eligible = true; CanPropose(80, 60); _rankings = 0;
            object[] proposalArgs = { _clan, null, null };
            check(!(bool)AccessTools.Method(warType, "TryCreateWarDecision").Invoke(war, proposalArgs)
                && _rankings == 1 && (string)proposalArgs[2] == "no credible war target is currently available",
                "AI proposal reaches target evaluation without a second unadjusted War Will veto");

            _includeTarget = true;
            check(Support(true) == 41 && Support(false) == -41, "Council applies resolve exactly once to both outcomes");
            _includeTarget = false;
            check(Support(true) == 20, "Liberation resolve still applies when a voter has no ranked target entry");
            _includeTarget = true; _target = foreign;
            check(Support(true) == 26, "Foreign-war council support receives no liberation resolve");
            check((float)adjustedWill.Invoke(_clients, new object[] { _clan, foreign, 60f }) == 60,
                "Unrelated target leaves actual willingness untouched");
            _target = _client;
            check((float)adjustedWill.Invoke(_clients, new object[] { _clan, _client, 60f }) == 60,
                "Resolve is directional toward the client's suzerain only");
            _target = _suzerain;
            check(war.GetWarWill(_clan) == 60, "Repeated votes preserve stored War Will for peace and daily recovery");
            records.Clear();
            check((float)adjustedWill.Invoke(_clients, new object[] { _clan, _suzerain, 60f }) == 60,
                "Ending clientage immediately ends liberation resolve");
            records.Add(new ClientKingdomRecord(_client.StringId, _suzerain.StringId, 0, false, 0));

            var courtType = assembly.GetType("BellumCivile.CourtLiberationAssessment");
            _desire = 60;
            object preparations = Activator.CreateInstance(courtType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object[] { _client }, null);
            check((bool)AccessTools.Field(courtType, "Viable").GetValue(preparations),
                "NPC preparations evaluate projected desire 80 and War Will 60 with resolve");
            _desire = 40;
            preparations = Activator.CreateInstance(courtType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object[] { _client }, null);
            check(!(bool)AccessTools.Field(courtType, "Viable").GetValue(preparations),
                "Preparations reaching only desire 60 still require ordinary War Will");

            _desire = 80;
            harmony.Unpatch(AccessTools.PropertyGetter(typeof(Kingdom), "UnresolvedDecisions"),
                HarmonyPatchType.Prefix, harmony.Id);
            harmony.Unpatch(AccessTools.Method(typeof(Kingdom), "IsAtWarWith"),
                HarmonyPatchType.Prefix, harmony.Id);
            harmony.Unpatch(AccessTools.Method(warType, "IsRevampEnabled"),
                HarmonyPatchType.Prefix, harmony.Id);
            ClientLibertyTooltipTests.Run(check, war, _client, _suzerain, _clan, wills);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { previousCampaign });
        }
    }
}
