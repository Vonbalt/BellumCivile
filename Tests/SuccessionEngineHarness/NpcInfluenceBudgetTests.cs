using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.Library;

internal static class NpcInfluenceBudgetTests
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

    private static readonly Assembly Mod = typeof(FiefDeliberationBehavior).Assembly;
    private static readonly Type Service = Mod.GetType("BellumCivile.NpcInfluenceBudgetService", true);
    private static readonly Type Kind = Mod.GetType("BellumCivile.NpcInfluenceExpenseKind", true);
    private static Kingdom _realm, _clanRealm, _minor;
    private static Clan _npc, _player, _ruler;
    private static Hero _leader;
    private static PerkObject _perk;
    private static List<Kingdom> _realms;
    private static readonly HashSet<IFaction> Enemies = new HashSet<IFaction>();
    private static readonly HashSet<Kingdom> Eliminated = new HashSet<Kingdom>();
    private static float _balance;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static void Set(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    private static object Call(string name, params object[] args) => AccessTools.Method(Service, name).Invoke(null, args);
    private static float Reserve(Clan clan) => (float)Call("GetRoleReserve", clan);
    private static object Expense(string name) => Enum.Parse(Kind, name);
    private static object Assess(Clan clan, float cost, string kind, float? balance = null) => Call("Assess", clan, cost, Expense(kind), balance);
    private static T Read<T>(object value, string property) => (T)AccessTools.Property(value.GetType(), property).GetValue(value);
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _clanRealm; return false; }
    private static bool Ruler(ref Clan __result) { __result = _ruler; return false; }
    private static bool Head(ref Hero __result) { __result = _leader; return false; }
    private static bool Balance(ref float __result) { __result = _balance; return false; }
    private static bool All(ref MBReadOnlyList<Kingdom> __result)
    { __result = new MBReadOnlyList<Kingdom>(_realms); return false; }
    private static bool Clans(Kingdom __instance, ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(__instance == _realm ? new List<Clan> { _npc, _player } : new List<Clan>()); return false; }
    private static bool War(IFaction faction1, IFaction faction2, ref bool __result)
    { __result = faction1 == _clanRealm && Enemies.Contains(faction2); return false; }
    private static bool DeadRealm(Kingdom __instance, ref bool __result)
    { __result = Eliminated.Contains(__instance); return false; }
    private static bool MinorRealm(Kingdom __instance, ref bool __result)
    { __result = __instance == _minor; return false; }
    private static bool Perk(ref PerkObject __result) { __result = _perk; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Spend(Clan clan, float amount) { _balance += amount; return false; }

    private static LordPartyComponent Party(bool armyMember, bool leader)
    {
        var component = Blank<LordPartyComponent>(); var party = Blank<MobileParty>();
        Set(component, "<MobileParty>k__BackingField", party);
        if (armyMember)
        {
            var army = Blank<Army>(); Set(party, "_army", army);
            Set(army, "<LeaderParty>k__BackingField", leader ? party : Blank<MobileParty>());
        }
        return component;
    }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.npc_influence_budget");
        var previous = Campaign.Current;
        void Patch(MethodBase method, string name) => harmony.Patch(method, prefix: new HarmonyMethod(typeof(NpcInfluenceBudgetTests), name));
        Kingdom NewRealm(string id) { var realm = Blank<Kingdom>(); realm.StringId = id; return realm; }
        _realm = NewRealm("budget_realm"); _clanRealm = _realm;
        var enemy = NewRealm("foreign_one"); var enemy2 = NewRealm("foreign_two");
        var rebels = NewRealm("realm_rebels_test"); var feud = NewRealm("bc_feud_test"); _minor = NewRealm("minor");
        _realms = new List<Kingdom> { _realm, enemy, enemy2, rebels, feud, _minor };
        _npc = Blank<Clan>(); _npc.StringId = "budget_npc";
        _player = Blank<Clan>(); _player.StringId = "budget_player";
        _leader = Blank<Hero>(); _perk = Blank<PerkObject>();
        var otherRuler = Blank<Clan>();
        var parties = new MBList<WarPartyComponent>(); Set(_npc, "_warPartyComponentsCache", parties);
        var manager = new Manager(); var policies = new PolicyDeliberationBehavior(); manager.Items.Add(policies);
        var campaign = Blank<Campaign>(); Enemies.Clear(); Eliminated.Clear();
        try
        {
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            campaign.AddCampaignBehaviorManager(manager);
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Head));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Influence"), nameof(Balance));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(All));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Clans"), nameof(Clans));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(DeadRealm));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsMinorFaction"), nameof(MinorRealm));
            Patch(AccessTools.Method(typeof(FactionManager), "IsAtWarAgainstFaction"), nameof(War));
            Patch(AccessTools.Method(typeof(Hero), "GetPerkValue"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(DefaultPerks.Charm), "FlexibleEthics"), nameof(Perk));
            Patch(AccessTools.Method(typeof(ChangeClanInfluenceAction), "Apply"), nameof(Spend));

            foreach (bool ruler in new[] { false, true })
            foreach (bool war in new[] { false, true })
            foreach (bool army in new[] { false, true })
            {
                _ruler = ruler ? _npc : otherRuler;
                Enemies.Clear(); if (war) Enemies.Add(enemy);
                parties.Clear(); if (army) parties.Add(Party(true, true));
                float expected = 100 + (ruler ? 100 : 0) + (war ? 100 : 0) + (army ? 100 : 0);
                string label = $"ruler={ruler}, war={war}, army={army}";
                check(Reserve(_npc) == expected, "Additive role reserve: " + label);
                check(!Read<bool>(Assess(_npc, 20, "CouncilCommitment", expected + 19), "CanAfford")
                    && Read<bool>(Assess(_npc, 20, "CouncilCommitment", expected + 20), "CanAfford"),
                    "Minimum vote affordability boundary: " + label);
                check(Read<float>(Assess(_npc, 100, "Discretionary"), "RequiredInfluence") == expected + 100,
                    "100-influence political action keeps the full role reserve: " + label);
                check(Read<float>(Assess(_npc, 100, "CrownEmergency"), "ProtectedReserve") == 100
                    && Read<bool>(Assess(_npc, 100, "CrownEmergency", 200), "CanAfford")
                    && !Read<bool>(Assess(_npc, 100, "CrownEmergency", 199), "CanAfford"),
                    "Crown emergency uses the 100 floor independently of role: " + label);
                _balance = expected + 20;
                check((bool)Call("TrySpend", _npc, 20f, Expense("CouncilCommitment"), "test") && _balance == expected,
                    "Funded vote pays its cost and leaves the exact reserve: " + label);
                check(!(bool)Call("TrySpend", _npc, 20f, Expense("CouncilCommitment"), "test") && _balance == expected,
                    "Next unfunded vote cannot cross the reserve: " + label);
            }

            Enemies.Add(enemy2); parties.Add(Party(true, true));
            check(Reserve(_npc) == 400, "Multiple enemies and multiple armies do not stack additional bonuses");
            _ruler = otherRuler; Enemies.Clear(); parties.Clear();
            parties.Add(null); parties.Add(Blank<LordPartyComponent>());
            check(Reserve(_npc) == 100, "Missing party or component cannot be mistaken for army leadership");
            parties.Add(Party(false, false)); check(Reserve(_npc) == 100, "Independent war party is not an army");
            parties.Add(Party(true, false)); check(Reserve(_npc) == 100, "Serving in another clan's army adds no leader bonus");
            parties.Add(Party(true, true)); check(Reserve(_npc) == 200, "Actual army leadership adds its bonus even at peace");
            parties.Clear(); Enemies.Add(rebels); Enemies.Add(feud); Enemies.Add(_minor);
            check(Reserve(_npc) == 100, "Civil-war, feud and minor-faction opponents do not count as foreign wars");
            Enemies.Add(enemy); Eliminated.Add(enemy);
            check(Reserve(_npc) == 100, "Eliminated foreign opponent does not raise the reserve");
            Eliminated.Clear(); check(Reserve(_npc) == 200, "Ordinary clans receive the foreign-war bonus");
            _clanRealm = rebels; check(Reserve(_npc) == 100, "Temporary rebel realm retains the existing foreign-war exclusion");
            _clanRealm = null; check(Reserve(_npc) == 100, "Kingdomless NPC keeps only its base reserve");
            _clanRealm = _realm; _ruler = _npc; parties.Add(Party(true, true));
            check(Reserve(_player) == 0 && Reserve(null) == 0, "Player and missing clan have no NPC role reserve");
            check(Read<float>(Assess(_player, 500, "Discretionary", 500), "ProtectedReserve") == 0
                && Read<bool>(Assess(_player, 500, "Discretionary", 500), "CanAfford"),
                "Player may spend their actual balance without NPC reserve restrictions");
            check(Read<float>(Assess(_npc, 500, "Discretionary"), "ProtectedReserve") == 500
                && Read<float>(Assess(_npc, 500, "Discretionary"), "RequiredInfluence") == 1000,
                "Expensive discretionary actions retain the cost-based safeguard above 400");
            check(Read<float>(Assess(_npc, 50, "InvoluntaryLoss"), "ProtectedReserve") == 0,
                "Involuntary losses are not protected by role reserves");
            check(Read<float>(Assess(_npc, -20, "CouncilCommitment", -5), "RequestedCost") == 0
                && Read<float>(Assess(_npc, -20, "CouncilCommitment", -5), "CurrentInfluence") == 0,
                "Existing negative cost and balance normalization remains intact");

            var promisedPolicies = new List<PolicyObject>();
            for (int i = 0; i < 22; i++)
            {
                var policy = Blank<PolicyObject>(); policy.StringId = "promise_" + i; promisedPolicies.Add(policy);
                policies.SetBribedVote(_realm, policy, _npc, 500);
            }
            foreach (string kind in new[] { "CouncilCommitment", "Discretionary", "CrownEmergency" })
                check(Read<float>(Assess(_npc, 20, kind), "ProtectedReserve") == 440,
                    "Outstanding promises can exceed the 400 role ceiling: " + kind);
            foreach (var policy in promisedPolicies) policies.ClearBribedVotesForPolicy(_realm, policy);
            check(Read<float>(Assess(_npc, 20, "CouncilCommitment"), "ProtectedReserve") == 400,
                "Clearing promises immediately restores the additive role reserve");
            Enemies.Clear(); parties.Clear();
            check(Reserve(_npc) == 200, "Peace and army disbanding immediately release their bonuses");
            _ruler = otherRuler; check(Reserve(_npc) == 100, "Losing rulership immediately releases its bonus");

            _ruler = _npc; Enemies.Add(enemy); parties.Add(Party(true, true)); _balance = 350;
            var telemetry = new InfluenceBudgetTelemetryBehavior();
            string report = (string)AccessTools.Method(telemetry.GetType(), "BuildReport").Invoke(telemetry, new object[] { 1, 84 });
            check(report.Contains("below_clan_reserve=0") && report.Contains("below_role_reserve=1")
                && report.Contains("rulers_below_ruler_reserve=1"),
                "Yearly telemetry compares rulers against their actual additive reserve");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { previous });
        }
    }
}
