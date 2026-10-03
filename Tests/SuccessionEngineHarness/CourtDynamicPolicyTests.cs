using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.Patches;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class CourtDynamicPolicyTests
{
    private static FactionObject _faction;
    private static Kingdom _realm;
    private static Clan _ruler, _proposer;
    private static Hero _hero;
    private static PolicyObject _policy;
    private static int _honor;
    private static PolicyDeliberationBehavior _deliberation;
    private static MBList<KingdomDecision> _decisions;
    private static bool Realm(ref Kingdom __result) { __result = _realm; return false; }
    private static bool Ruler(ref Clan __result) { __result = _ruler; return false; }
    private static bool Proposer(ref Clan __result) { __result = _proposer; return false; }
    private static bool Leader(ref Hero __result) { __result = _hero; return false; }
    private static bool Faction(ref FactionObject __result) { __result = _faction; return false; }
    private static bool Honor(ref int __result) { __result = _honor; return false; }
    private static bool Trait(ref TraitObject __result) { __result = null; return false; }
    private static bool Relation(ref int __result) { __result = 0; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Skip() => false;
    private static bool Deliberation(ref PolicyDeliberationBehavior __result) { __result = _deliberation; return false; }
    private static bool Decisions(ref MBReadOnlyList<KingdomDecision> __result) { __result = _decisions; return false; }
    private static bool Policies(ref MBReadOnlyList<PolicyObject> __result) { __result = new MBList<PolicyObject> { _policy }; return false; }
    private static bool Name(ref TextObject __result) { __result = new TextObject("Fixture"); return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(100); return false; }
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(CourtAgendaRecord).Assembly;
        var configType = assembly.GetType("BellumCivile.IdeologyPolicyAgendaConfig");
        var singleton = AccessTools.Field(configType, "_instance");
        var oldConfig = singleton.GetValue(null);
        var harmony = new Harmony("bellum.test.dynamic_policy");
        void Patch(MethodBase target, string name)
        {
            if (target.DeclaringType != target.ReflectedType)
                target = AccessTools.DeclaredMethod(target.DeclaringType, target.Name, target.GetParameters().Select(p => p.ParameterType).ToArray());
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(CourtDynamicPolicyTests), name));
        }
        try
        {
            var config = Activator.CreateInstance(configType, true);
            using (var stream = assembly.GetManifestResourceStream("BellumCivile.ModuleData.bellum_policy_agendas.xml"))
                AccessTools.Method(configType, "Read").Invoke(config, new object[] { stream, false });
            singleton.SetValue(null, config);
            var crowns = (HashSet<string>)AccessTools.Field(configType, "_crown").GetValue(config);
            var stances = (Dictionary<FactionType, Dictionary<string, CourtPolicyStance>>)AccessTools.Field(configType, "_stances").GetValue(config);
            var all = crowns.Concat(stances.Values.SelectMany(s => s.Keys)).Distinct().ToArray();
            check(all.Length == 43 && crowns.Count == 14, "Production policy roster has 43 policies and fourteen Crown laws");
            check(!all.Contains("policy_land_grants_for_veteran")
                && stances[FactionType.Glory]["policy_land_grands_for_veteran"] == CourtPolicyStance.Support,
                "Glory veteran grants use the native ID");
            check(stances[FactionType.Glory]["policy_serfdom"] == CourtPolicyStance.Oppose
                && stances[FactionType.Glory]["policy_senate"] == CourtPolicyStance.Oppose,
                "Glory opposes serfdom and entrenched Senate privilege");
            var mixin = assembly.GetType("BellumCivile.ViewModelMixin.KingdomPolicyFactionAgendaMixin");
            var card = AccessTools.Method(mixin, "PolicyStance");
            var capture = AccessTools.Method(typeof(CourtAgendaRecord), "CapturePolicyStance");
            var lost = AccessTools.Method(typeof(CourtAgendaRecord), "HasLostPolicyMandate");
            var snapshotMethod = AccessTools.Method(typeof(PolicyVoteResolutionPatch), "CaptureStances");
            foreach (var type in new[] { FactionType.Nobility, FactionType.Glory, FactionType.Liberty })
            foreach (var id in all.Concat(new[] { "unknown_mod_policy" }))
            {
                var policy = Blank<PolicyObject>(); policy.StringId = id;
                var interest = IdeologyPolicyRoster.GetStance(type, policy);
                foreach (float mood in new[] { -100f, 20f, 20.001f, 59.999f, 60f, 100f })
                {
                    var expected = !crowns.Contains(id) ? interest : mood <= 20 ? CourtPolicyStance.Oppose
                        : mood >= 60 ? CourtPolicyStance.Support : CourtPolicyStance.Neutral;
                    check(IdeologyPolicyRoster.GetEffectiveStance(type, policy, mood) == expected,
                        "Effective stance follows shared threshold: " + type + "/" + id + "/" + mood);
                    check((CourtPolicyStance)card.Invoke(null, new object[] { policy, false, (FactionType?)type, mood }) == expected,
                        "Card uses exact effective stance");
                    check(IdeologyPolicyRoster.GetStance(type, policy) == interest,
                        "Changing mood cannot mutate lasting policy interest");
                }
            }
            _faction = Blank<FactionObject>();
            AccessTools.Field(typeof(FactionObject), "_type").SetValue(_faction, FactionType.Glory);
            _policy = Blank<PolicyObject>(); _policy.StringId = "policy_royal_guard";
            var agenda = new CourtAgendaRecord { Faction = _faction, PolicyId = _policy.StringId, State = CourtAgendaState.Deliberating };
            _faction.Mood = 60;
            capture.Invoke(agenda, null);
            var snapshot = (Dictionary<FactionObject, CourtPolicyStance>)snapshotMethod.Invoke(null,
                new object[] { new[] { _faction }, _policy });
            _faction.Mood = 20;
            capture.Invoke(agenda, null);
            check(agenda.HasPolicyStanceSnapshot && agenda.SelectedPolicyStance == 1,
                "Selection stance is saved once, not overwritten as mood changes");
            check(snapshot[_faction] == CourtPolicyStance.Support
                && IdeologyPolicyRoster.GetEffectiveStance(_faction, _policy) == CourtPolicyStance.Oppose,
                "Pre-result snapshot survives mood shocks without retroactive betrayal");
            check((bool)lost.Invoke(agenda, new object[] { CourtPolicyStance.Oppose })
                && (bool)lost.Invoke(agenda, new object[] { CourtPolicyStance.Neutral })
                && !(bool)lost.Invoke(agenda, new object[] { CourtPolicyStance.Support }),
                "An aligned enactment loses its mandate only when support disappears");
            agenda.Abolish = true; agenda.SelectedPolicyStance = -1;
            check((bool)lost.Invoke(agenda, new object[] { CourtPolicyStance.Neutral })
                && !(bool)lost.Invoke(agenda, new object[] { CourtPolicyStance.Oppose }),
                "Repeal commitments invert the required stance");
            agenda.Abolish = false;
            check(!(bool)lost.Invoke(agenda, new object[] { CourtPolicyStance.Oppose }),
                "Deliberate against-faction nominations are not mistaken for changed mandates");
            var legacy = new CourtAgendaRecord { Faction = _faction, PolicyId = _policy.StringId };
            check(!(bool)lost.Invoke(legacy, new object[] { CourtPolicyStance.Oppose }), "Legacy records invent no previous commitment");
            capture.Invoke(legacy, null);
            check(legacy.HasPolicyStanceSnapshot && legacy.SelectedPolicyStance == -1, "Legacy adoption captures current stance conservatively");

            _realm = Blank<Kingdom>(); _ruler = Blank<Clan>(); _hero = Blank<Hero>();
            var voter = Blank<Clan>();
            AccessTools.Field(typeof(FactionObject), "_leader").SetValue(_faction, voter);
            var manager = new FactionManagerBehavior();
            var decision = Blank<KingdomPolicyDecision>();
            AccessTools.Field(typeof(KingdomPolicyDecision), "Policy").SetValue(decision, _policy);
            Patch(AccessTools.PropertyGetter(typeof(KingdomDecision), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(KingdomDecision), "ProposerClan"), nameof(Proposer));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.Method(typeof(FactionManagerBehavior), "GetIdeologicalFaction"), nameof(Faction));
            Patch(AccessTools.Method(typeof(Hero), "GetTraitLevel"), nameof(Honor));
            Patch(AccessTools.PropertyGetter(typeof(DefaultTraits), "Honor"), nameof(Trait));
            Patch(AccessTools.Method(typeof(Hero), "GetRelation", new[] { typeof(Hero) }), nameof(Relation));
            Patch(AccessTools.Method(typeof(PolicyDeliberationBehavior), "ResolvePolicyAbolish"), nameof(No));
            foreach (float mood in new[] { 20f, 21f, 59f, 60f })
            foreach (int honor in new[] { -1, 0, 1 })
            {
                _faction.Mood = mood; _honor = honor;
                int stance = (int)IdeologyPolicyRoster.GetEffectiveStance(_faction, _policy);
                float conviction = 40 * stance * (honor < 0 ? .75f : honor > 0 ? 1.25f : 1f);
                _proposer = voter;
                check(Math.Abs(PolicyVoteAIPatch.CalculateSupportScore(decision, voter, true, manager) - conviction) < .001f
                    && Math.Abs(PolicyVoteAIPatch.CalculateSupportScore(decision, voter, false, manager) + conviction) < .001f,
                    "Actual vote score and repeal inversion use effective stance and honor");
                _proposer = _ruler;
                check(Math.Abs(PolicyVoteAIPatch.CalculateSupportScore(decision, voter, true, manager) - conviction - mood * .15f) < .001f,
                    "Actual Crown-proposer social modifier remains separate from ideological stance");
            }

            _realm.StringId = "dynamic_realm"; voter.StringId = "dynamic_sponsor"; _proposer = voter;
            AccessTools.Field(typeof(FactionObject), "_parentKingdom").SetValue(_faction, _realm);
            _deliberation = new PolicyDeliberationBehavior(); _decisions = new MBList<KingdomDecision>();
            Patch(AccessTools.PropertyGetter(typeof(PolicyDeliberationBehavior), "Current"), nameof(Deliberation));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "UnresolvedDecisions"), nameof(Decisions));
            Patch(AccessTools.PropertyGetter(typeof(PolicyObject), "All"), nameof(Policies));
            Patch(AccessTools.PropertyGetter(typeof(PolicyObject), "Name"), nameof(Name));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Name"), nameof(Name));
            Patch(AccessTools.Method(typeof(FactionObject), "GetDisplayName"), nameof(Name));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "RecordResultHistory"), nameof(Skip));
            foreach (var method in assembly.GetType("BellumCivile.BellumCivileNotifications").GetMethods().Where(m => m.Name == "Show")) Patch(method, nameof(Skip));
            var court = new CourtAgendaBehavior();
            var records = (List<CourtAgendaRecord>)AccessTools.Field(typeof(CourtAgendaBehavior), "_agendas").GetValue(court);
            var withdraw = AccessTools.Method(typeof(CourtAgendaBehavior), "TryWithdrawChangedPolicyMotion");
            var dates = (Dictionary<string, CampaignTime>)AccessTools.Field(typeof(PolicyDeliberationBehavior), "_pendingVoteDate").GetValue(_deliberation);
            var sponsors = (Dictionary<string, string>)AccessTools.Field(typeof(PolicyDeliberationBehavior), "_pendingFactionLeaderClan").GetValue(_deliberation);
            string key = _realm.StringId + "|" + _policy.StringId;
            foreach (var state in new[] { CourtAgendaState.Announced, CourtAgendaState.Deliberating, CourtAgendaState.Voting })
            {
                records.Clear(); dates.Clear(); sponsors.Clear(); _decisions.Clear();
                var motion = new CourtAgendaRecord { Realm = _realm, Faction = _faction, Sponsor = voter,
                    PolicyId = _policy.StringId, State = state, PaidInfluence = state == CourtAgendaState.Announced ? 0 : 100,
                    HasPolicyStanceSnapshot = true, SelectedPolicyStance = 1, HasScheduleSnapshot = true, TermDays = 84 };
                records.Add(motion); _faction.Mood = 59;
                if (state == CourtAgendaState.Deliberating) { dates[key] = CampaignTime.Days(120); sponsors[key] = voter.StringId; }
                if (state == CourtAgendaState.Voting) _decisions.Add(decision);
                bool withdrawn = (bool)withdraw.Invoke(court, new object[] { _realm, _policy, voter });
                check(withdrawn == (state != CourtAgendaState.Voting), "Changed stance withdraws before filing/ballot, but never interrupts a live ballot");
                if (withdrawn)
                {
                    check(motion.State == CourtAgendaState.Cancelled && !motion.ResultApplied && _faction.Mood == 59
                        && motion.CancellationReason == "faction_policy_stance_changed" && !dates.ContainsKey(key),
                        "Withdrawal clears the real deliberation queue without result shock or neglect state");
                    check(!(bool)withdraw.Invoke(court, new object[] { _realm, _policy, voter }), "Withdrawal is idempotent");
                }
                else
                {
                    var before = new Dictionary<FactionObject, CourtPolicyStance> { [_faction] = CourtPolicyStance.Neutral };
                    court.Conclude(_realm, _policy, voter, false, false, before);
                    check(motion.State == CourtAgendaState.Defeated && motion.ResultApplied && _faction.Mood == 59,
                        "Concluding a ballot after the mandate changed does not punish the obsolete objective");
                    court.Conclude(_realm, _policy, voter, false, true, before);
                    check(motion.State == CourtAgendaState.Defeated && _faction.Mood == 59, "Repeated resolution cannot replay or reverse consequences");
                }
            }
            records.Clear();
            var valid = new CourtAgendaRecord { Realm = _realm, Faction = _faction, Sponsor = voter, PolicyId = _policy.StringId,
                State = CourtAgendaState.Voting, HasPolicyStanceSnapshot = true, SelectedPolicyStance = 1,
                HasScheduleSnapshot = true, TermDays = 84 };
            records.Add(valid); _faction.Mood = 60;
            var supportSnapshot = new Dictionary<FactionObject, CourtPolicyStance> { [_faction] = CourtPolicyStance.Support };
            court.Conclude(_realm, _policy, voter, false, false, supportSnapshot);
            check(_faction.Mood == 50 && supportSnapshot[_faction] == CourtPolicyStance.Support,
                "Normal defeat still applies -10 without rewriting the stance used to judge voters");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            singleton.SetValue(null, oldConfig);
            _faction = null; _realm = null; _ruler = _proposer = null; _hero = null; _policy = null;
            _deliberation = null; _decisions = null;
        }
        check(!Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m)?.Owners.Contains(harmony.Id) == true),
            "Dynamic policy fixture cleans up all engine substitutions");
    }
}
