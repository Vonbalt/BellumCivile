using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;

internal static class HostageTerminationTests
{
    private static readonly List<string> Events = new List<string>();
    private static Hero _player, _victim, _leader;
    private static bool Player(ref Hero __result) { __result = _player; return false; }
    private static bool Victim(ref Hero __result) { __result = _victim; return false; }
    private static bool Alive(ref bool __result) { __result = true; return false; }
    private static bool Dead(ref bool __result) { __result = true; return false; }
    private static bool Protected(Hero hero, ref bool __result) { __result = hero != null && hero == _victim; return false; }
    private static bool Relation(int relationChange, string sourceId) { Events.Add(sourceId + ":" + relationChange); return false; }
    private static bool Incident(int xpValue) { Events.Add("honor:" + xpValue); return false; }
    private static bool ExecutionXp() { Events.Add("honor:-1000"); return false; }
    private static bool Honor(ref TraitObject __result) { __result = null; return false; }
    private static bool Leader(ref Hero __result) { __result = _leader; return false; }
    private static bool Finish(TreatyHostageRecord record) { record.ActionCompleted = true; return false; }
    private static bool Respond() { Events.Add("response"); return false; }
    private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.hostage.termination");
        var behavior = typeof(HostagePactBehavior);
        void Patch(Type type, string name, string prefix) => harmony.Patch(AccessTools.Method(type, name),
            prefix: new HarmonyMethod(typeof(HostageTerminationTests), prefix));
        object Call(string method, params object[] args) => AccessTools.Method(behavior, method).Invoke(null, args);
        _player = Empty<Hero>(); _victim = Empty<Hero>(); _leader = Empty<Hero>();
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), prefix: new HarmonyMethod(typeof(HostageTerminationTests), "Player"));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), "OneToOneConversationHero"), prefix: new HarmonyMethod(typeof(HostageTerminationTests), "Victim"));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), prefix: new HarmonyMethod(typeof(HostageTerminationTests), "Alive"));
            harmony.Patch(AccessTools.PropertyGetter(typeof(DefaultTraits), "Honor"), prefix: new HarmonyMethod(typeof(HostageTerminationTests), "Honor"));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), prefix: new HarmonyMethod(typeof(HostageTerminationTests), "Leader"));
            Patch(typeof(RelationMemoryService), "ApplyChange", "Relation");
            Patch(typeof(TraitLevelingHelper), "OnIncidentResolved", "Incident");
            Patch(typeof(TraitLevelingHelper), "OnLordExecuted", "ExecutionXp");
            var assembly = behavior.Assembly;
            Patch(assembly.GetType("BellumCivile.HostageCustodyGuard"), "IsProtected", "Protected");
            var condition = AccessTools.Method(assembly.GetType("BellumCivile.Patches.HostageReleaseDialogueConditionPatch"), "Postfix");
            object[] conditionArgs = { true };
            condition.Invoke(null, conditionArgs);
            check(!(bool)conditionArgs[0], "Protected hostage hides ordinary release dialogue");
            var consequence = AccessTools.Method(assembly.GetType("BellumCivile.Patches.HostageReleaseDialogueConsequencePatch"), "Prefix");
            check(!(bool)consequence.Invoke(null, null), "Stale ordinary hostage release blocks native relations reward");
            _victim = null;
            check((bool)consequence.Invoke(null, null), "Ordinary prisoner release consequence is unchanged");
            _victim = Empty<Hero>();

            HostagePactRecord Make(HostagePactEndReason reason)
            {
                Events.Clear();
                return new HostagePactRecord {
                    Phase = HostagePactPhase.Resolving, EndReason = reason, TerminatingRuler = _player,
                    TerminatingHostage = _victim, FirstRealm = Empty<Kingdom>(), SecondRealm = Empty<Kingdom>(),
                    FirstHostage = new TreatyHostageRecord { Hero = _victim, SupplyingHouse = Empty<Clan>(), ReleaseSucceeded = true, ActionCompleted = true },
                    SecondHostage = new TreatyHostageRecord { Hero = Empty<Hero>() }
                };
            }
            var p = Make(HostagePactEndReason.EarlyRelease);
            Call("ApplyEarlyReleaseRewards", p); Call("ApplyEarlyReleaseRewards", p);
            check(Events.SequenceEqual(new[] { "released_treaty_hostage_early:5", "released_treaty_hostage_early:5", "honor:20" }),
                "Early release gratitude and +20 Honor are one-time receipts");
            p = Make(HostagePactEndReason.EarlyRelease); p.FirstHostage.ReleaseSucceeded = false;
            Call("ApplyEarlyReleaseRewards", p);
            check(Events.Count == 0 && !p.TerminationTraitApplied, "Blocked release awards no traits or gratitude");
            p = Make(HostagePactEndReason.Expired); Call("ApplyEarlyReleaseRewards", p);
            check(Events.Count == 0, "Automatic expiry awards no early-release reward");

            p = Make(HostagePactEndReason.Betrayal); p.FirstHostage.ExecutionSucceeded = true;
            Call("ApplyBetrayalConsequences", p, p.FirstHostage); Call("ApplyBetrayalConsequences", p, p.FirstHostage);
            check(Events.SequenceEqual(new[] { "betrayed_hostage_pledge:-30" }), "Native execution Honor penalty is not duplicated");
            p = Make(HostagePactEndReason.Betrayal); p.ExecutionNeedsHonorPenalty = true; p.FirstHostage.ExecutionSucceeded = true;
            Call("ApplyBetrayalConsequences", p, p.FirstHostage); Call("ApplyBetrayalConsequences", p, p.FirstHostage);
            check(Events.SequenceEqual(new[] { "betrayed_hostage_pledge:-30", "honor:-1000" }),
                "Dishonorable hostage still incurs one oathbreaking Honor penalty");
            p = Make(HostagePactEndReason.Betrayal); Call("ApplyBetrayalConsequences", p, p.FirstHostage);
            check(Events.Count == 0, "Unsuccessful execution causes no betrayal consequences");

            harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDead"), prefix: new HarmonyMethod(typeof(HostageTerminationTests), "Dead"));
            Patch(behavior, "FinishWarDisposition", "Finish");
            Patch(behavior, "ResolveWarHostage", "Respond");
            p = Make(HostagePactEndReason.Betrayal); p.ExecutionNeedsHonorPenalty = true; p.FirstHostage.ActionStarted = true;
            AccessTools.Method(behavior, "ResolveHostageBetrayal").Invoke(new HostagePactBehavior(), new object[] { p });
            check(Events.SequenceEqual(new[] { "betrayed_hostage_pledge:-30", "honor:-1000", "response" }),
                "Reciprocal ruler responds only after betrayal relations and Honor are applied");

            var active = new HostagePactRecord { Phase = HostagePactPhase.Active, FirstRealm = Empty<Kingdom>(), SecondRealm = Empty<Kingdom>(),
                FirstHostage = new TreatyHostageRecord(), SecondHostage = new TreatyHostageRecord() };
            object[] begin = { HostagePactEndReason.Betrayal, active.FirstRealm };
            check((bool)AccessTools.Method(typeof(HostagePactRecord), "TryBeginResolution").Invoke(active, begin)
                && active.SecondHostage.Outcome == HostageCustodyOutcome.Pending, "Betrayal never auto-releases reciprocal hostage");
            check(!(bool)AccessTools.Method(typeof(HostagePactRecord), "TryBeginResolution").Invoke(active,
                new object[] { HostagePactEndReason.HostageDied, null }), "Death callback cannot override saved betrayal");
            active = new HostagePactRecord { Phase = HostagePactPhase.Active,
                FirstHostage = new TreatyHostageRecord(), SecondHostage = new TreatyHostageRecord() };
            check((bool)AccessTools.Method(typeof(HostagePactRecord), "TryBeginResolution").Invoke(active,
                new object[] { HostagePactEndReason.EarlyRelease, null })
                && active.FirstHostage.Outcome == HostageCustodyOutcome.Release && active.SecondHostage.Outcome == HostageCustodyOutcome.Release,
                "Honorable termination returns both reciprocal hostages");
            check((int)HostagePactEndReason.InvalidCustody == 8 && (int)HostagePactEndReason.EarlyRelease == 9
                && (int)HostagePactEndReason.Betrayal == 10, "New pact ending reasons preserve old save IDs");
        }
        finally { harmony.UnpatchAll(harmony.Id); Events.Clear(); }
    }
}
