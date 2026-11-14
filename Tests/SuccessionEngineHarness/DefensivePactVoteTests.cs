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
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;

internal static class DefensivePactVoteTests
{
    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public bool IsSaving { get; set; }
        public bool IsLoading => !IsSaving;
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsSaving) Data[key] = data;
            else if (Data.TryGetValue(key, out object value)) data = (T)value;
            else return false;
            return true;
        }
    }

    private static Kingdom _member, _target;
    private static Clan _sponsor, _targetHouse;
    private static Hero _memberLeader, _targetLeader;
    private static DefensivePactBehavior _owner;
    private static object _assessment;
    private static MBList<KingdomDecision> _pending;
    private static string _mode;
    private static int _filings, _refunds, _shown;
    private static bool _ignoreInfluence;
    private static double _departureThreat;
    private static int _relation;
    private static KingdomDecision _previewDecision;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool IgnoreLog() => false;
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(0); return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result)
    { __result = __instance == _sponsor ? _member : _target; return false; }
    private static bool Crown(Kingdom __instance, ref Clan __result)
    { __result = __instance == _member ? _sponsor : _targetHouse; return false; }
    private static bool Leader(Clan __instance, ref Hero __result)
    { __result = __instance == _sponsor ? _memberLeader : _targetLeader; return false; }
    private static bool RealmLeader(Kingdom __instance, ref Hero __result)
    { __result = __instance == _member ? _memberLeader : _targetLeader; return false; }
    private static bool Relation(ref int __result) { __result = _relation; return false; }
    private static bool Pending(ref MBReadOnlyList<KingdomDecision> __result) { __result = _pending; return false; }
    private static bool Voters(ref MBReadOnlyList<Clan> __result) { __result = new MBList<Clan> { _sponsor }; return false; }
    private static bool VoteCost(ref int __result) { __result = 20; return false; }
    private static bool Owner(ref DefensivePactBehavior __result) { __result = _owner; return false; }
    private static bool Assessment(ref object __result) { __result = _assessment; return false; }
    private static bool DepartureContext(ref double threat, ref int threshold, ref bool __result)
    { threat = _departureThreat; threshold = 50; __result = true; return false; }
    private static bool Preview(KingdomDecision decision, Clan sponsor, ref KingdomElection.ElectionOutcomeSupport __result)
    { _previewDecision = decision; __result = KingdomElection.ElectionOutcomeSupport.GoodSupport; return false; }
    private static void SetInfluence(Clan clan, float amount) => AccessTools.Field(typeof(Clan), "_influence").SetValue(clan, amount);
    private static bool Refund(Clan clan, float amount) { _refunds++; SetInfluence(clan, clan.Influence + amount); return false; }
    private static bool File(Kingdom kingdom, KingdomDecision decision, bool ignoreInfluenceCost)
    {
        _filings++; _ignoreInfluence = ignoreInfluenceCost;
        if (!ignoreInfluenceCost) SetInfluence(decision.ProposerClan, decision.ProposerClan.Influence - decision.GetProposalInfluenceCost());
        if (_mode == "throw") throw new InvalidOperationException("fixture interrupted filing");
        if (_mode == "ambiguous") { SetInfluence(decision.ProposerClan, decision.ProposerClan.Influence - 7); throw new InvalidOperationException("fixture changed influence"); }
        if (_mode == "pending") _pending.Add(decision);
        if (_mode == "approve") decision.ApplyChosenOutcome(decision.DetermineInitialCandidates().First());
        if (_mode == "reject") decision.ApplyChosenOutcome(decision.DetermineInitialCandidates().Last());
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(DefensivePactBehavior).Assembly;
        string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../../"));
        string startup = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "SubModule.cs"));
        int pactRegistration = startup.IndexOf("campaignStarter.AddBehavior(new DefensivePactBehavior());", StringComparison.Ordinal);
        int threatRegistration = startup.IndexOf("campaignStarter.AddBehavior(new DefensivePactThreatBehavior());", StringComparison.Ordinal);
        int aiRegistration = startup.IndexOf("campaignStarter.AddBehavior(new DefensivePactAiBehavior());", StringComparison.Ordinal);
        check(pactRegistration >= 0 && pactRegistration < threatRegistration && threatRegistration < aiRegistration,
            "Campaign startup registers pact activation before threat recording and the AI scheduler");
        var harmony = new Harmony("bellum.test.defensive_pact_votes");
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        object originalTicks = ticks.GetValue(null);
        void Patch(MethodBase method, string prefix)
        {
            if (method == null) throw new InvalidOperationException("Missing vote fixture target: " + prefix);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(DefensivePactVoteTests), prefix));
        }
        _member = Blank<Kingdom>(); _member.StringId = "pact_member";
        _target = Blank<Kingdom>(); _target.StringId = "pact_target";
        _sponsor = Blank<Clan>(); _sponsor.StringId = "pact_sponsor";
        _targetHouse = Blank<Clan>(); _targetHouse.StringId = "pact_target_house";
        _memberLeader = Blank<Hero>(); _targetLeader = Blank<Hero>();
        _pending = new MBList<KingdomDecision>();
        Type assessmentType = assembly.GetType("BellumCivile.DefensivePactCampaignAssessment", true);
        object valid = Activator.CreateInstance(assessmentType, true);
        void Set(string property, object value) => AccessTools.Property(assessmentType, property).SetValue(valid, value);
        Set("Threat", 75d); Set("JoiningThreshold", 50); Set("Fiefs", 2);
        Set("MemberStrength", 40d); Set("TargetStrength", 100d); Set("RulerRelation", -10);
        Set("AtPeace", true); Set("IsEligible", true);
        _assessment = valid; _owner = new DefensivePactBehavior();
        var file = AccessTools.Method(typeof(DefensivePactBehavior), "TryFileJoinVote");
        var recover = AccessTools.Method(typeof(DefensivePactBehavior), "RecoverJoinVotes");
        var member = AccessTools.Method(typeof(DefensivePactBehavior), "IsMemberAgainst");
        bool FileVote() => (bool)file.Invoke(_owner, new object[] { _sponsor, _target, (Action<KingdomDecision>)(_ => _shown++) });
        bool Joined() => (bool)member.Invoke(_owner, new object[] { _member.StringId, _target.StringId });
        try
        {
            ticks.SetValue(null, 1L);
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.Method(typeof(CampaignTime), "HoursFromNow"), nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsUnderMercenaryService"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Yes));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Crown));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Leader"), nameof(RealmLeader));
            Patch(AccessTools.Method(typeof(Hero), "GetRelation", new[] { typeof(Hero) }), nameof(Relation));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "UnresolvedDecisions"), nameof(Pending));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.BellumKingdomVisibilityHelper"), "IsTemporaryBellumKingdom"), nameof(No));
            Patch(AccessTools.PropertyGetter(assembly.GetType("BellumCivile.BellumCivileOptions"), "EnableWarPeaceLogicRevamp"), nameof(Yes));
            Patch(AccessTools.Method(typeof(DefensivePactBehavior), "Assess"), nameof(Assessment));
            Patch(AccessTools.Method(typeof(DefensivePactBehavior), "TryGetDepartureContext"), nameof(DepartureContext));
            Patch(AccessTools.PropertyGetter(typeof(DefensivePactJoinDecision), "Owner"), nameof(Owner));
            Patch(AccessTools.Method(typeof(KingdomElection), "GetElectionOutcomeSupport"), nameof(Preview));
            Patch(AccessTools.Method(typeof(IdeologyBehavior), "AddDecisionAsModAction"), nameof(File));
            Patch(AccessTools.Method(typeof(ChangeClanInfluenceAction), "Apply"), nameof(Refund));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(IgnoreLog));

            _filings = _shown = _refunds = 0; _sponsor.Influence = 99; _mode = "pending";
            check(!FileVote() && _filings == 0 && _shown == 0, "A defensive pact vote is not filed without its 100 influence cost");
            _sponsor.Influence = 150;
            bool queuedOk = FileVote();
            check(queuedOk && _filings == 1 && !_ignoreInfluence && _sponsor.Influence == 50
                && _pending.Count == 1 && _shown == 1 && !Joined(),
                "A player-realm ballot charges once, enters the native queue and opens the vote without joining early");
            check(!FileVote() && _filings == 1 && _sponsor.Influence == 50,
                "A pending defensive-pact ballot cannot be filed or charged twice");
            var queued = (DefensivePactJoinDecision)_pending[0];
            var getPending = AccessTools.Method(typeof(DefensivePactBehavior), "GetPendingVote");
            check(ReferenceEquals(getPending.Invoke(_owner, new object[] { _member, _target }), queued)
                && getPending.Invoke(_owner, new object[] { _member, _member }) == null,
                "The diplomacy menu can reopen only the matching live defensive-pact ballot");
            var store = new Store { IsSaving = true }; _owner.SyncData(store);
            _owner = new DefensivePactBehavior(); store.IsSaving = false; _owner.SyncData(store);
            recover.Invoke(_owner, null);
            check(!Joined() && _pending.Contains(queued), "A queued native ballot survives load-time recovery without joining early");
            queued.ApplyChosenOutcome(queued.DetermineInitialCandidates().First());
            check(Joined() && getPending.Invoke(_owner, new object[] { _member, _target }) == null,
                "An approved native ballot joins once and is no longer offered for resolution");
            _pending.Remove(queued); recover.Invoke(_owner, null);
            check(Joined() && ((IList)AccessTools.Field(typeof(DefensivePactBehavior), "_joinVotes").GetValue(_owner)).Count == 0,
                "Completed ballots retire after native removal without undoing membership");

            _owner = new DefensivePactBehavior(); _pending.Clear(); _sponsor.Influence = 150; _mode = "reject";
            check(FileVote() && !Joined() && _sponsor.Influence == 50 && _shown == 1,
                "An immediately rejected NPC ballot ends without joining or opening player UI");
            _owner = new DefensivePactBehavior(); _sponsor.Influence = 150; _mode = "approve";
            check(FileVote() && Joined() && _sponsor.Influence == 50,
                "An immediately approved NPC ballot joins through native filing");

            _owner = new DefensivePactBehavior(); _pending.Clear(); _sponsor.Influence = 150; _mode = "pending";
            check(FileVote(), "A ballot can be filed for deferred post-vote verification");
            var deferred = (DefensivePactJoinDecision)_pending[0];
            _assessment = null;
            deferred.ApplyChosenOutcome(deferred.DetermineInitialCandidates().First());
            _pending.Remove(deferred); recover.Invoke(_owner, null);
            check(!Joined(), "Approval waits when current eligibility cannot be verified");
            _assessment = valid; recover.Invoke(_owner, null);
            check(Joined(), "Approved membership resumes after the assessment becomes available");

            _owner = new DefensivePactBehavior(); _pending.Clear(); _sponsor.Influence = 150; _mode = "pending";
            check(FileVote(), "A second ballot can test an eligibility change during recovery");
            deferred = (DefensivePactJoinDecision)_pending[0];
            _assessment = null;
            deferred.ApplyChosenOutcome(deferred.DetermineInitialCandidates().First());
            _pending.Remove(deferred);
            Set("IsEligible", false); _assessment = valid; recover.Invoke(_owner, null);
            check(!Joined() && ((IList)AccessTools.Field(typeof(DefensivePactBehavior), "_joinVotes").GetValue(_owner)).Count == 0,
                "An approved but newly ineligible realm does not join after recovery");
            Set("IsEligible", true);

            _owner = new DefensivePactBehavior(); _pending.Clear(); _sponsor.Influence = 150; _mode = "pending"; _refunds = 0;
            check(FileVote(), "A filed ballot can be checked for native cancellation");
            _pending.Clear(); recover.Invoke(_owner, null);
            check(!Joined() && _sponsor.Influence == 50 && _refunds == 0
                && ((IList)AccessTools.Field(typeof(DefensivePactBehavior), "_joinVotes").GetValue(_owner)).Count == 0,
                "A filed ballot removed by the game is retired without an incorrect refund");

            _owner = new DefensivePactBehavior(); _pending.Clear(); _sponsor.Influence = 150; _mode = "throw"; _refunds = 0;
            check(!FileVote() && _sponsor.Influence == 150 && _refunds == 1,
                "A verified native filing deduction is refunded once if no ballot exists");
            recover.Invoke(_owner, null);
            check(_refunds == 1, "Recovery does not repeat a failed-filing refund");
            _owner = new DefensivePactBehavior(); _sponsor.Influence = 150; _mode = "ambiguous"; _refunds = 0;
            check(!FileVote() && _sponsor.Influence == 43 && _refunds == 0,
                "A changed influence balance is not guessed or overwritten after interrupted filing");

            _owner = new DefensivePactBehavior(); _pending.Clear(); _sponsor.Influence = 150; _assessment = valid;
            _mode = "approve"; check(FileVote() && Joined(), "A fresh pact is ready for departure-vote tests");
            _sponsor.Influence = 150; _mode = "pending"; _departureThreat = 50; _relation = -10;
            var leave = AccessTools.Method(typeof(DefensivePactBehavior), "TryFileDepartureVote");
            bool FileDeparture() => (bool)leave.Invoke(_owner, new object[] { _sponsor, _target, null });
            check(FileDeparture() && _pending.Count == 1 && _sponsor.Influence == 50 && Joined(),
                "A departure proposal costs influence and does not remove membership before the vote");
            var leaving = (DefensivePactJoinDecision)_pending[0];
            var leaveStore = new Store { IsSaving = true }; _owner.SyncData(leaveStore);
            _owner = new DefensivePactBehavior(); leaveStore.IsSaving = false; _owner.SyncData(leaveStore);
            recover.Invoke(_owner, null);
            check(Joined() && (bool)AccessTools.Field(typeof(DefensivePactJoinDecision), "IsDeparture").GetValue(leaving)
                && ReferenceEquals(getPending.Invoke(_owner, new object[] { _member, _target }), leaving),
                "An unresolved departure ballot and its membership survive loading");
            var leaveChoices = leaving.DetermineInitialCandidates().ToArray();
            float highThreatSupport = leaving.DetermineSupport(_sponsor, leaveChoices[0]);
            _departureThreat = 10;
            float lowThreatSupport = leaving.DetermineSupport(_sponsor, leaveChoices[0]);
            _relation = 50;
            check(lowThreatSupport > highThreatSupport && leaving.DetermineSupport(_sponsor, leaveChoices[0]) > lowThreatSupport,
                "The actual departure ballot responds to current threat and each voter's relations");
            leaving.ApplyChosenOutcome(leaveChoices[1]);
            _pending.Remove(leaving); recover.Invoke(_owner, null);
            check(Joined(), "A rejected departure ballot preserves the pact");
            _sponsor.Influence = 150;
            check(FileDeparture(), "A new departure vote can be proposed after the rejected ballot concludes");
            leaving = (DefensivePactJoinDecision)_pending[0];
            leaving.ApplyChosenOutcome(leaving.DetermineInitialCandidates().First());
            _pending.Remove(leaving); recover.Invoke(_owner, null);
            check(!Joined(), "An approved departure ballot removes the membership exactly once");

            var previewType = assembly.GetType("BellumCivile.DefensivePactVotePreview", true);
            object[] previewArgs = { _owner, _sponsor, _target, false, KingdomElection.ElectionOutcomeSupport.LowSupport };
            check((bool)AccessTools.Method(previewType, "TryEvaluate").Invoke(null, previewArgs)
                && previewArgs[4].Equals(KingdomElection.ElectionOutcomeSupport.GoodSupport)
                && _previewDecision is DefensivePactJoinDecision,
                "Joining support preview uses the native election with a defensive-pact ballot");
            _owner = new DefensivePactBehavior(); _sponsor.Influence = 150; _mode = "approve";
            check(FileVote() && Joined(), "A fresh membership is ready for departure support preview");
            previewArgs = new object[] { _owner, _sponsor, _target, true, KingdomElection.ElectionOutcomeSupport.LowSupport };
            check((bool)AccessTools.Method(previewType, "TryEvaluate").Invoke(null, previewArgs)
                && (bool)AccessTools.Field(typeof(DefensivePactJoinDecision), "IsDeparture").GetValue(_previewDecision),
                "Leaving support preview uses the departure version of the same native ballot");
            harmony.Unpatch(AccessTools.Method(typeof(KingdomElection), "GetElectionOutcomeSupport"), HarmonyPatchType.Prefix, harmony.Id);
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Clans"), nameof(Voters));
            Patch(AccessTools.PropertyGetter(typeof(Supporter), "IsPlayer"), nameof(No));
            Patch(AccessTools.Method(typeof(KingdomDecision), "GetInfluenceCostOfSupport"), nameof(VoteCost));
            _sponsor.Influence = 1000; _relation = -10; _departureThreat = 10;
            object[] lowThreatPreview = { _owner, _sponsor, _target, true, KingdomElection.ElectionOutcomeSupport.LowSupport };
            bool lowThreatOk = (bool)AccessTools.Method(previewType, "TryEvaluate").Invoke(null, lowThreatPreview);
            _departureThreat = 90;
            object[] highThreatPreview = { _owner, _sponsor, _target, true, KingdomElection.ElectionOutcomeSupport.LowSupport };
            bool highThreatOk = (bool)AccessTools.Method(previewType, "TryEvaluate").Invoke(null, highThreatPreview);
            check(lowThreatOk && highThreatOk
                && (int)(KingdomElection.ElectionOutcomeSupport)lowThreatPreview[4] > (int)(KingdomElection.ElectionOutcomeSupport)highThreatPreview[4],
                "The real native council predicts more support for departure when the target's threat falls");
        }
        finally { harmony.UnpatchAll(harmony.Id); ticks.SetValue(null, originalTicks); _assessment = null; _owner = null; }
    }
}
