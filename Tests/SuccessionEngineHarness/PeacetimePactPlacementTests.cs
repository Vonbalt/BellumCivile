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
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Decisions;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Library;

internal static class PeacetimePactPlacementTests
{
    private sealed class MemoryDataStore : IDataStore
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
    private static Kingdom _first, _second;
    private static Clan _firstHouse, _secondHouse;
    private static Settlement _holding;
    private static IList _candidates;
    private static bool _allied, _allyOnSecondPlacement;
    private static bool _inCustody;
    private static Hero _drainPayerOnSecondPlacement;
    private static VoluntaryPactProposalBehavior _proposalOwner;
    private static Hero _firstLeader, _secondLeader;
    private static MBList<KingdomDecision> _nativePending;
    private static string _filingMode;
    private static int _filingCalls;
    private static bool _lastIgnoreInfluence;
    private static int _refundCalls;
    private static bool _neighbors;
    private static bool _playerTarget;
    private static float _testDay;
    private static int _placements, _recoveryCalls;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool MainHero(ref Hero __result) { __result = null; return false; }
    private static bool Allied(ref bool __result) { __result = _allied; return false; }
    private static bool House(Kingdom __instance, ref Clan __result)
    { __result = __instance == _first ? _firstHouse : _secondHouse; return false; }
    private static bool ClanRealm(Clan __instance, ref Kingdom __result)
    { __result = __instance == _firstHouse ? _first : _second; return false; }
    private static bool Holding(ref Settlement __result) { __result = _holding; return false; }
    private static bool Candidates(ref object __result) { __result = _candidates; return false; }
    private static bool Time(ref CampaignTime __result) { __result = CampaignTime.Days(_testDay); return false; }
    private static bool Place(TreatyHostageRecord record, ref bool __result)
    {
        record.CustodyEstablished = true;
        _placements++;
        if (_allyOnSecondPlacement && _placements == 2) _allied = true;
        if (_drainPayerOnSecondPlacement != null && _placements == 2) _drainPayerOnSecondPlacement.Gold = 0;
        __result = true;
        return false;
    }
    private static bool Recover() { _recoveryCalls++; return false; }
    private static bool Custody(ref bool __result) { __result = _inCustody; return false; }
    private static bool IgnoreLog() => false;
    private static bool IgnoreTradeEvent() => false;
    private static bool ProposalOwner(ref VoluntaryPactProposalBehavior __result) { __result = _proposalOwner; return false; }
    private static bool RealmLeader(Kingdom __instance, ref Hero __result)
    { __result = __instance == _first ? _firstLeader : _secondLeader; return false; }
    private static bool Enemies(ref MBReadOnlyList<IFaction> __result) { __result = null; return false; }
    private static bool NeutralRelation(ref int __result) { __result = 0; return false; }
    private static bool PendingDecisions(ref MBReadOnlyList<KingdomDecision> __result)
    { __result = _nativePending; return false; }
    private static bool FileDecision(Kingdom kingdom, KingdomDecision decision, bool ignoreInfluenceCost)
    {
        _filingCalls++; _lastIgnoreInfluence = ignoreInfluenceCost;
        if (_filingMode == "pending") _nativePending.Add(decision);
        if (_filingMode == "approve") decision.ApplyChosenOutcome(decision.DetermineInitialCandidates().First());
        if (_filingMode == "throw" || _filingMode == "mutatedThrow")
        {
            decision.ProposerClan.Influence -= decision.GetProposalInfluenceCost();
            if (_filingMode == "mutatedThrow") decision.ProposerClan.Influence -= 7;
            throw new InvalidOperationException("filing callback failed");
        }
        return false;
    }
    private static bool RefundInfluence(Clan clan, float amount)
    { _refundCalls++; clan.Influence += amount; return false; }
    private static bool Neighbors(ref bool __result) { __result = _neighbors; return false; }
    private static bool MinimumDuration(ref int __result) { __result = 50; return false; }
    private static bool PlayerTarget(ref bool __result) { __result = _playerTarget; return false; }

    internal static void Run(Action<bool, string> check)
    {
        Assembly mod = typeof(HostagePactRecord).Assembly;
        Type candidateType = mod.GetType("BellumCivile.TreatyHostageCandidate", true);
        var harmony = new Harmony("bellum.tests.peacetime_pact_placement");
        FieldInfo ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        object oldTicks = ticks.GetValue(null);
        void Patch(MethodBase method, string prefix)
        {
            if (method == null) throw new InvalidOperationException("Missing fixture target: " + prefix);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(PeacetimePactPlacementTests), prefix));
        }
        Hero firstHero = Blank<Hero>(), secondHero = Blank<Hero>();
        _firstLeader = Blank<Hero>(); _secondLeader = Blank<Hero>();
        _nativePending = new MBList<KingdomDecision>();
        _first = Blank<Kingdom>(); _first.StringId = "first";
        _second = Blank<Kingdom>(); _second.StringId = "second";
        _firstHouse = Blank<Clan>(); _firstHouse.StringId = "first_house";
        _secondHouse = Blank<Clan>(); _secondHouse.StringId = "second_house"; _holding = Blank<Settlement>();
        _candidates = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(candidateType));
        foreach (var item in new[] { new { Hero = firstHero, Tier = 1 }, new { Hero = secondHero, Tier = 4 } })
            _candidates.Add(Activator.CreateInstance(candidateType, BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { item.Hero, item.Tier }, null));
        try
        {
            ticks.SetValue(null, 1L);
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), nameof(MainHero));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Time));
            Patch(AccessTools.Method(typeof(CampaignTime), "HoursFromNow"), nameof(Time));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(House));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Leader"), nameof(RealmLeader));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "FactionsAtWarWith"), nameof(Enemies));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "UnresolvedDecisions"), nameof(PendingDecisions));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(ClanRealm));
            Patch(AccessTools.Method(typeof(Hero), "GetRelation", new[] { typeof(Hero) }), nameof(NeutralRelation));
            Patch(AccessTools.Method(typeof(Kingdom), "IsAllyWith"), nameof(Allied));
            Patch(AccessTools.Method(typeof(Kingdom), "IsAtWarWith"), nameof(No));
            Patch(AccessTools.PropertyGetter(mod.GetType("BellumCivile.BellumCivileOptions"), "EnableWarPeaceLogicRevamp"), nameof(Yes));
            Patch(AccessTools.PropertyGetter(mod.GetType("BellumCivile.BellumCivileOptions"), "HostagePactMinimumDurationDays"), nameof(MinimumDuration));
            Patch(AccessTools.Method(typeof(CivilWarConflictBehavior), "IsRealmTransferPending"), nameof(No));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.BellumKingdomVisibilityHelper"), "IsTemporaryBellumKingdom"), nameof(No));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.TreatyHostageEligibility"), "GetCandidates"), nameof(Candidates));
            Patch(AccessTools.Method(typeof(HostagePactBehavior), "SelectHolding"), nameof(Holding));
            Patch(AccessTools.Method(typeof(HostagePactBehavior), "Place"), nameof(Place));
            Patch(AccessTools.Method(typeof(HostagePactBehavior), "MaintainCustody"), nameof(Recover));
            Patch(AccessTools.Method(typeof(HostagePactBehavior), "InTreatyCustody"), nameof(Custody));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(IgnoreLog));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.VoluntaryPactPayment"), "PublishTrade"), nameof(IgnoreTradeEvent));
            Patch(AccessTools.Method(mod.GetType("BellumCivile.MarriageAllianceHelper"), "HasMarriageAlliance"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(VoluntaryPactDecision), "Owner"), nameof(ProposalOwner));
            Patch(AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "AreNeighbors"), nameof(Neighbors));
            Patch(AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "IsPlayerRulingHouse"), nameof(PlayerTarget));
            Patch(AccessTools.Method(typeof(IdeologyBehavior), "AddDecisionAsModAction"), nameof(FileDecision));
            Patch(AccessTools.Method(typeof(ChangeClanInfluenceAction), "Apply"), nameof(RefundInfluence));
            var warCache = new WarPeaceRevampBehavior();
            var cachedScore = AccessTools.Method(typeof(WarPeaceRevampBehavior), "TryGetCachedTargetScore");
            object[] cachedArgs = { _firstHouse, _second, 0f };
            check(!(bool)cachedScore.Invoke(warCache, cachedArgs), "A missing clan target cache is not populated by a vote preview");
            Type scoreType = mod.GetType("BellumCivile.WarPeace.WarTargetScore", true);
            IList scores = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(scoreType));
            object targetEntry = Activator.CreateInstance(scoreType, true);
            scoreType.GetProperty("TargetKingdom").SetValue(targetEntry, _second);
            scoreType.GetProperty("Score").SetValue(targetEntry, 80f);
            scores.Add(targetEntry);
            var cache = (IDictionary)AccessTools.Field(typeof(WarPeaceRevampBehavior), "_targetScoresByClanId").GetValue(warCache);
            cache[_firstHouse.StringId] = scores;
            check((bool)cachedScore.Invoke(warCache, cachedArgs) && (float)cachedArgs[2] == 80f,
                "An existing per-clan target preference is read without a global rescan");
            scoreType.GetProperty("Score").SetValue(targetEntry, float.NaN);
            check(!(bool)cachedScore.Invoke(warCache, cachedArgs), "Invalid cached war scores do not affect a ballot");
            var scheduler = new VoluntaryPactAiBehavior();
            Type dueType = typeof(VoluntaryPactAiBehavior).GetNestedType("DueRealm", BindingFlags.NonPublic);
            object schedule = AccessTools.Field(typeof(VoluntaryPactAiBehavior), "_schedule").GetValue(scheduler);
            MethodInfo addDue = schedule.GetType().GetMethod("Add");
            for (int i = 0; i < 55; i++)
            {
                var realm = Blank<Kingdom>(); realm.StringId = "realm_" + i.ToString("D2");
                object entry = Activator.CreateInstance(dueType, true);
                AccessTools.Field(dueType, "Realm").SetValue(entry, realm);
                AccessTools.Field(dueType, "Day").SetValue(entry, 100);
                addDue.Invoke(schedule, new[] { entry });
            }
            var takeDue = AccessTools.Method(typeof(VoluntaryPactAiBehavior), "TakeDue");
            var seenRealms = new HashSet<Kingdom>(); bool withinCap = true;
            for (int day = 100; day < 111; day++)
            {
                var due = ((IEnumerable)takeDue.Invoke(scheduler, new object[] { day })).Cast<Kingdom>().ToList();
                withinCap &= due.Count == 5;
                foreach (var realm in due) withinCap &= seenRealms.Add(realm);
            }
            check(withinCap && seenRealms.Count == 55, "A 55-realm backlog is processed at most five per day without repeats");
            check(!((IEnumerable)takeDue.Invoke(scheduler, new object[] { 129 })).Cast<Kingdom>().Any()
                && ((IEnumerable)takeDue.Invoke(scheduler, new object[] { 130 })).Cast<Kingdom>().Count() == 5,
                "Processed realms are not due again until 30 campaign days have passed");
            var offset = AccessTools.Method(typeof(VoluntaryPactAiBehavior), "InitialOffset");
            var offsets = Enumerable.Range(0, 55).Select(i => (int)offset.Invoke(null, new object[] { "realm_" + i })).ToArray();
            check(offsets.All(i => i >= 0 && i < 30) && offsets.Distinct().Count() > 1
                && offsets[0] == (int)offset.Invoke(null, new object[] { "realm_0" }),
                "New realms receive deterministic staggered offsets within 30 fixed days");
            var owner = new VoluntaryPactProposalBehavior();
            var track = AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "TryTrack");
            var resolve = AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "TryResolveVote");
            var choose = AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "TryChooseTargetHostage");
            var pending = AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "HasPendingPair");
            var tracked = new VoluntaryPactProposalRecord { Id = "tracked-proposal", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, DurationDays = 100, BaselineDays = 50,
                Phase = VoluntaryPactProposalPhase.SourceVote };
            tracked.Sponsor = _secondHouse;
            check(!(bool)track.Invoke(owner, new object[] { tracked }), "A proposer must belong to the source realm");
            tracked.Sponsor = _firstHouse; tracked.TargetHostage = secondHero;
            check(!(bool)track.Invoke(owner, new object[] { tracked }), "The target's hostage cannot be selected before its choice");
            tracked.TargetHostage = null;
            object secondCandidate = _candidates[1]; _candidates.RemoveAt(1);
            check(!(bool)track.Invoke(owner, new object[] { tracked }), "A proposal cannot start without an eligible hostage from each realm");
            _candidates.Add(secondCandidate);
            Type scoring = mod.GetType("BellumCivile.PeacetimeHostagePactScoring", true);
            var scoreMethod = AccessTools.Method(scoring, "TryScore");
            tracked.Compensation = 50000; tracked.DurationDays = 50;
            object[] sourceScore = { tracked, _first, 0f, 0f }, targetScore = { tracked, _second, 0f, 0f };
            check((bool)scoreMethod.Invoke(null, sourceScore) && (bool)scoreMethod.Invoke(null, targetScore)
                && Math.Abs((float)sourceScore[2] - 13f) < .001f && (float)targetScore[2] > 70f
                && tracked.TargetHostage == null,
                "Scoring compares each house's hostage tier, pays only the recipient and does not select its hostage");
            Hero targetHeir = Blank<Hero>();
            object heirCandidate = Activator.CreateInstance(candidateType, BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { targetHeir, 1 }, null);
            _candidates.Add(heirCandidate);
            tracked.TargetHostage = targetHeir;
            object[] heirScore = { tracked, _second, 0f, 0f };
            check((bool)scoreMethod.Invoke(null, heirScore) && (float)heirScore[2] < (float)targetScore[2],
                "After target selection the ballot scores the actual hostage rather than the earlier estimate");
            _firstLeader.Gold = 1000000;
            var choosePayment = AccessTools.Method(typeof(VoluntaryPactAiBehavior), "TryChoosePayment");
            object[] chosenOffer = { tracked, 0, 0f };
            check((bool)choosePayment.Invoke(null, chosenOffer) && (int)chosenOffer[1] == 100000
                && (float)chosenOffer[2] >= 50 && tracked.Compensation == 50000,
                "An NPC proposes the smallest affordable payment that reaches the target threshold without mutating the draft");
            _candidates.Remove(heirCandidate); tracked.TargetHostage = null;
            tracked.Compensation = 0; tracked.DurationDays = 100;
            check((bool)track.Invoke(owner, new object[] { tracked })
                && (bool)pending.Invoke(owner, new object[] { _second, _first })
                && !(bool)track.Invoke(owner, new object[] { tracked }),
                "Saved proposal owner tracks one pending proposal per realm pair in either direction");
            var store = new MemoryDataStore { IsSaving = true };
            owner.SyncData(store);
            owner = new VoluntaryPactProposalBehavior(); store.IsSaving = false; owner.SyncData(store);
            check((bool)pending.Invoke(owner, new object[] { _first, _second }),
                "A replacement campaign behavior reloads the tracked proposal through its save key");
            check(!(bool)resolve.Invoke(owner, new object[] { tracked.Id, _second, true })
                && (bool)resolve.Invoke(owner, new object[] { tracked.Id, _first, true })
                && !(bool)resolve.Invoke(owner, new object[] { tracked.Id, _first, true }),
                "Only the source realm can resolve its vote, and the result cannot be replayed");
            check(!(bool)choose.Invoke(owner, new object[] { tracked.Id, firstHero })
                && (bool)choose.Invoke(owner, new object[] { tracked.Id, secondHero })
                && !(bool)resolve.Invoke(owner, new object[] { tracked.Id, _first, true })
                && (bool)resolve.Invoke(owner, new object[] { tracked.Id, _second, false })
                && !(bool)pending.Invoke(owner, new object[] { _first, _second }),
                "The target supplies a different eligible hostage and alone resolves its vote");
            var outcomes = AccessTools.Field(typeof(VoluntaryPactProposalBehavior), "_outcomes");
            check(((ICollection)outcomes.GetValue(owner)).Count == 1, "A rejected target ballot queues its result once");
            var cooldown = AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "TryGetRefusalCooldown");
            object[] cooldownArgs = { _second, _first, 0 };
            check((bool)cooldown.Invoke(owner, cooldownArgs) && (int)cooldownArgs[2] == 90,
                "A rejected pledge starts a reciprocal 90-day cooldown");
            store.IsSaving = true; owner.SyncData(store);
            owner = new VoluntaryPactProposalBehavior(); store.IsSaving = false; owner.SyncData(store);
            check(((ICollection)outcomes.GetValue(owner)).Count == 1, "An unreported result is recovered after load");
            _testDay = 89;
            AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "QueueOutcome").Invoke(owner, new object[] { tracked });
            check((bool)cooldown.Invoke(owner, cooldownArgs) && (int)cooldownArgs[2] == 1,
                "A requeued result cannot extend the saved refusal cooldown");
            _testDay = 90;
            check(!(bool)cooldown.Invoke(owner, cooldownArgs), "The refusal cooldown expires after exactly 90 campaign days");
            _testDay = 0;
            AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "ReportOutcomes").Invoke(owner, null);
            check(tracked.OutcomeReported && ((ICollection)outcomes.GetValue(owner)).Count == 0,
                "A recovered result is marked reported and cannot repeatedly notify");
            var stale = new VoluntaryPactProposalRecord { Id = "stale-proposal", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, DurationDays = 100, BaselineDays = 50,
                Phase = VoluntaryPactProposalPhase.SourceVote };
            check((bool)track.Invoke(owner, new object[] { stale }), "A rejected proposal no longer blocks a later proposal");
            _allied = true;
            var cancelInvalid = AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "TryCancelInvalidBallot");
            check(!(bool)resolve.Invoke(owner, new object[] { stale.Id, _first, true })
                && (bool)cancelInvalid.Invoke(owner, new object[] { stale.Id })
                && stale.Phase == VoluntaryPactProposalPhase.Cancelled,
                "An alliance formed during a pending ballot invalidates the proposal without resolving its vote");
            _allied = false;
            var lostHostage = new VoluntaryPactProposalRecord { Id = "lost-hostage", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, DurationDays = 100, BaselineDays = 50,
                Phase = VoluntaryPactProposalPhase.SourceVote };
            check((bool)track.Invoke(owner, new object[] { lostHostage })
                && (bool)resolve.Invoke(owner, new object[] { lostHostage.Id, _first, true })
                && (bool)choose.Invoke(owner, new object[] { lostHostage.Id, secondHero }),
                "A valid proposal advances to the receiving realm's ballot");
            _candidates.Remove(secondCandidate);
            check(!(bool)resolve.Invoke(owner, new object[] { lostHostage.Id, _second, true })
                && (bool)cancelInvalid.Invoke(owner, new object[] { lostHostage.Id }),
                "A selected hostage becoming unavailable invalidates the pending ballot");
            _candidates.Add(secondCandidate);
            var queued = new VoluntaryPactProposalRecord { Id = "queued-delivery", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, DurationDays = 100, BaselineDays = 50,
                Phase = VoluntaryPactProposalPhase.SourceVote };
            check((bool)track.Invoke(owner, new object[] { queued })
                && (bool)resolve.Invoke(owner, new object[] { queued.Id, _first, true })
                && (bool)choose.Invoke(owner, new object[] { queued.Id, secondHero })
                && (bool)resolve.Invoke(owner, new object[] { queued.Id, _second, true }),
                "Approval by both realms queues delivery");
            FieldInfo queue = AccessTools.Field(typeof(VoluntaryPactProposalBehavior), "_deliveries");
            check(((ICollection)queue.GetValue(owner)).Count == 1, "Only pending deliveries enter the recovery queue");
            store.IsSaving = true; owner.SyncData(store);
            owner = new VoluntaryPactProposalBehavior(); store.IsSaving = false; owner.SyncData(store);
            check(((ICollection)queue.GetValue(owner)).Count == 1,
                "The delivery recovery queue is rebuilt from saved pending proposals");
            _proposalOwner = new VoluntaryPactProposalBehavior();
            var ballot = new VoluntaryPactProposalRecord { Id = "native-ballot", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, DurationDays = 100, BaselineDays = 50,
                Phase = VoluntaryPactProposalPhase.SourceVote };
            check((bool)track.Invoke(_proposalOwner, new object[] { ballot }), "Native ballot has a tracked source proposal");
            var build = AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "TryBuildDecision");
            object[] wrongStage = { ballot.Id, true, null }, sourceBuild = { ballot.Id, false, null };
            check(!(bool)build.Invoke(_proposalOwner, wrongStage) && (bool)build.Invoke(_proposalOwner, sourceBuild)
                && ballot.Phase == VoluntaryPactProposalPhase.SourceVote,
                "Building a source ballot calculates its score without filing it or advancing its phase");
            var sourceDecision = (VoluntaryPactDecision)sourceBuild[2];
            var sourceChoices = sourceDecision.DetermineInitialCandidates().ToList();
            check(sourceDecision.IsAllowed() && sourceDecision.GetProposalInfluenceCost() == 100
                && sourceDecision.IsKingsVoteAllowed && sourceChoices.Count == 2,
                "Source decision exposes two choices, native ruler override and the 100-influence cost");
            sourceDecision.ApplyChosenOutcome(sourceChoices[0]);
            sourceDecision.ApplyChosenOutcome(sourceChoices[0]);
            check(ballot.Phase == VoluntaryPactProposalPhase.TargetChoice && !sourceDecision.IsAllowed(),
                "The source decision records its result once and advances to the target's choice");
            var cancelled = AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "OnDecisionCancelled");
            cancelled.Invoke(_proposalOwner, new object[] { sourceDecision, false });
            check(ballot.Phase == VoluntaryPactProposalPhase.TargetChoice,
                "A late cancellation event for the source ballot cannot cancel the next stage");
            check((bool)choose.Invoke(_proposalOwner, new object[] { ballot.Id, secondHero }),
                "The receiving realm can select its own eligible hostage after the source vote");
            object[] targetBuild = { ballot.Id, true, null };
            check((bool)build.Invoke(_proposalOwner, targetBuild), "The receiving ballot is scored after its hostage is selected");
            var targetDecision = (VoluntaryPactDecision)targetBuild[2];
            check(targetDecision.IsAllowed() && targetDecision.GetProposalInfluenceCost() == 0,
                "The target decision is held by the receiving house without a second proposal cost");
            targetDecision.ApplyChosenOutcome(targetDecision.DetermineInitialCandidates().Last());
            check(ballot.Phase == VoluntaryPactProposalPhase.Cancelled && !targetDecision.IsAllowed(),
                "Rejecting the target decision cancels the proposal without delivering hostages");
            var withdrawn = new VoluntaryPactProposalRecord { Id = "withdrawn-ballot", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, DurationDays = 100, BaselineDays = 50,
                Phase = VoluntaryPactProposalPhase.SourceVote };
            check((bool)track.Invoke(_proposalOwner, new object[] { withdrawn }), "A cancelled vote can be tested on a new proposal");
            var withdrawnDecision = new VoluntaryPactDecision(_firstHouse, withdrawn.Id, false, 60f);
            cancelled.Invoke(_proposalOwner, new object[] { withdrawnDecision, false });
            check(withdrawn.Phase == VoluntaryPactProposalPhase.Cancelled
                && !(bool)pending.Invoke(_proposalOwner, new object[] { _first, _second }),
                "A native cancellation closes the matching pending proposal");
            var filing = new VoluntaryPactProposalRecord { Id = "filed-ballot", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, DurationDays = 100, BaselineDays = 50,
                Phase = VoluntaryPactProposalPhase.SourceVote };
            check((bool)track.Invoke(_proposalOwner, new object[] { filing }), "A valid proposal is ready for native filing");
            var file = AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "TryFileDecision");
            _filingCalls = 0; _filingMode = "pending"; _firstHouse.Influence = 99;
            check(!(bool)file.Invoke(_proposalOwner, new object[] { filing.Id, false })
                && _filingCalls == 0 && !filing.SourceFilingStarted,
                "Insufficient influence prevents filing before any native charge or receipt");
            _firstHouse.Influence = 150;
            check((bool)file.Invoke(_proposalOwner, new object[] { filing.Id, false })
                && _filingCalls == 1 && !_lastIgnoreInfluence && filing.SourceFilingStarted
                && filing.SourceBallotFiled && filing.SourceDecision != null && _nativePending.Contains(filing.SourceDecision),
                "The source decision is filed through the native influence-charging path once");
            check(!(bool)file.Invoke(_proposalOwner, new object[] { filing.Id, false }) && _filingCalls == 1,
                "A pending native decision cannot be filed or charged twice");
            var recoverVotes = AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "RecoverStartedVotes");
            recoverVotes.Invoke(_proposalOwner, new object[] { null });
            check(filing.Phase == VoluntaryPactProposalPhase.SourceVote,
                "Reload recovery retains a native ballot that is still pending");
            filing.SourceDecision.ApplyChosenOutcome(filing.SourceDecision.DetermineInitialCandidates().First());
            check((bool)choose.Invoke(_proposalOwner, new object[] { filing.Id, secondHero }),
                "A filed and approved source ballot advances to the target's choice");
            _filingMode = "approve";
            check((bool)file.Invoke(_proposalOwner, new object[] { filing.Id, true })
                && _filingCalls == 2 && _lastIgnoreInfluence && filing.TargetFilingStarted
                && filing.Phase == VoluntaryPactProposalPhase.Delivery,
                "An immediately resolved target ballot has no second filing charge");

            var interruptedVote = new VoluntaryPactProposalRecord { Id = "interrupted-vote", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, DurationDays = 100, BaselineDays = 50,
                Phase = VoluntaryPactProposalPhase.SourceVote, SourceFilingStarted = true };
            var recoveryOwner = new VoluntaryPactProposalBehavior();
            check((bool)track.Invoke(recoveryOwner, new object[] { interruptedVote }), "Interrupted vote fixture is tracked");
            recoverVotes.Invoke(recoveryOwner, new object[] { null });
            check(interruptedVote.Phase == VoluntaryPactProposalPhase.Cancelled
                && !(bool)file.Invoke(recoveryOwner, new object[] { interruptedVote.Id, false }),
                "A missing started ballot is cancelled on reload instead of recharging it");
            var failedFiling = new VoluntaryPactProposalRecord { Id = "failed-filing", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, DurationDays = 100, BaselineDays = 50,
                Phase = VoluntaryPactProposalPhase.SourceVote };
            _proposalOwner = new VoluntaryPactProposalBehavior();
            check((bool)track.Invoke(_proposalOwner, new object[] { failedFiling }), "Failed-filing fixture is tracked");
            _firstHouse.Influence = 150; _filingMode = "throw"; _refundCalls = 0;
            check(!(bool)file.Invoke(_proposalOwner, new object[] { failedFiling.Id, false })
                && failedFiling.Phase == VoluntaryPactProposalPhase.Cancelled && failedFiling.SourceFilingRefundStarted
                && _firstHouse.Influence == 150 && _refundCalls == 1,
                "A verified native deduction is refunded once when filing fails before a ballot exists");
            check(!(bool)file.Invoke(_proposalOwner, new object[] { failedFiling.Id, false }) && _refundCalls == 1,
                "A failed and refunded proposal cannot repeat either filing or refund");
            var disappeared = new VoluntaryPactProposalRecord { Id = "disappeared-ballot", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, DurationDays = 100, BaselineDays = 50,
                Phase = VoluntaryPactProposalPhase.SourceVote, SourceFilingStarted = true, SourceBallotFiled = true,
                SourceInfluenceBeforeFiling = 150 };
            disappeared.SourceDecision = new VoluntaryPactDecision(_firstHouse, disappeared.Id, false, 60f);
            var disappearedOwner = new VoluntaryPactProposalBehavior();
            check((bool)track.Invoke(disappearedOwner, new object[] { disappeared }), "Missing-filed-ballot fixture is tracked");
            _firstHouse.Influence = 50; _refundCalls = 0;
            recoverVotes.Invoke(disappearedOwner, new object[] { null });
            check(disappeared.Phase == VoluntaryPactProposalPhase.Cancelled && _firstHouse.Influence == 50 && _refundCalls == 0,
                "A confirmed filed ballot that later disappears is not incorrectly refunded");
            var orphaned = new VoluntaryPactProposalRecord { Id = "orphaned-ballot", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, DurationDays = 100, BaselineDays = 50,
                Phase = VoluntaryPactProposalPhase.SourceVote };
            _proposalOwner = new VoluntaryPactProposalBehavior(); _filingMode = "pending";
            _firstHouse.Influence = 150; _nativePending.Clear(); _refundCalls = 0;
            check((bool)track.Invoke(_proposalOwner, new object[] { orphaned })
                && (bool)file.Invoke(_proposalOwner, new object[] { orphaned.Id, false }),
                "A filed ballot is queued for bounded liveness checks");
            var reconcile = AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "ReconcileBallots");
            reconcile.Invoke(_proposalOwner, null);
            check(orphaned.Phase == VoluntaryPactProposalPhase.SourceVote,
                "Liveness checks preserve a ballot that remains in the native queue");
            _nativePending.Remove(orphaned.SourceDecision);
            reconcile.Invoke(_proposalOwner, null);
            check(orphaned.Phase == VoluntaryPactProposalPhase.Cancelled && _refundCalls == 0,
                "A removed native ballot is retired without refunding a filed decision");
            var ambiguousFiling = new VoluntaryPactProposalRecord { Id = "ambiguous-filing", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, DurationDays = 100, BaselineDays = 50,
                Phase = VoluntaryPactProposalPhase.SourceVote };
            _proposalOwner = new VoluntaryPactProposalBehavior();
            check((bool)track.Invoke(_proposalOwner, new object[] { ambiguousFiling }), "Ambiguous-filing fixture is tracked");
            _firstHouse.Influence = 150; _filingMode = "mutatedThrow"; _refundCalls = 0;
            check(!(bool)file.Invoke(_proposalOwner, new object[] { ambiguousFiling.Id, false })
                && _firstHouse.Influence == 43 && _refundCalls == 0 && !ambiguousFiling.SourceFilingRefundStarted,
                "A changed influence balance is not guessed or overwritten during failed-filing recovery");
            _proposalOwner = new VoluntaryPactProposalBehavior(); _filingMode = "pending"; _filingCalls = 0; _firstHouse.Influence = 150;
            var create = AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "TryCreateAndFile");
            object[] draft = { _first, _second, _firstHouse, firstHero, 100, 0, null };
            _neighbors = false;
            check(!(bool)create.Invoke(_proposalOwner, draft) && _filingCalls == 0,
                "A peacetime proposal cannot be filed between non-neighboring realms");
            _neighbors = true; draft[4] = 40;
            check(!(bool)create.Invoke(_proposalOwner, draft) && _filingCalls == 0,
                "A proposal below the configured duration minimum is not filed");
            draft[4] = 100; draft[5] = 1000; _firstLeader.Gold = 0;
            check(!(bool)create.Invoke(_proposalOwner, draft) && _filingCalls == 0,
                "An unaffordable compensation offer cannot file or charge influence");
            draft[5] = 0; _firstHouse.Influence = 99;
            check(!(bool)create.Invoke(_proposalOwner, draft) && _filingCalls == 0
                && !(bool)pending.Invoke(_proposalOwner, new object[] { _first, _second }),
                "An unaffordable filing leaves no pending pair");
            _firstHouse.Influence = 150;
            check((bool)create.Invoke(_proposalOwner, draft) && draft[6] is string createdId
                && _filingCalls == 1 && !_lastIgnoreInfluence
                && ((VoluntaryPactProposalRecord)AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "Find")
                    .Invoke(_proposalOwner, new object[] { createdId })).BaselineDays == 50,
                "A valid neighboring proposal snapshots the configured minimum and files through native influence handling");
            var npcTarget = new VoluntaryPactProposalRecord { Id = "npc-target", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, DurationDays = 100, BaselineDays = 50,
                Phase = VoluntaryPactProposalPhase.SourceVote };
            _proposalOwner = new VoluntaryPactProposalBehavior();
            check((bool)track.Invoke(_proposalOwner, new object[] { npcTarget })
                && (bool)resolve.Invoke(_proposalOwner, new object[] { npcTarget.Id, _first, true }),
                "An approved proposal waits for the receiving ruler to choose a hostage");
            store.IsSaving = true; _proposalOwner.SyncData(store);
            _proposalOwner = new VoluntaryPactProposalBehavior(); store.IsSaving = false; _proposalOwner.SyncData(store);
            var choiceQueue = AccessTools.Field(typeof(VoluntaryPactProposalBehavior), "_targetChoices");
            check(((ICollection)choiceQueue.GetValue(_proposalOwner)).Count == 1,
                "The receiving-house choice queue is rebuilt after a load");
            _secondHouse.Influence = -25; _filingMode = "approve"; _filingCalls = 0;
            AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "AdvanceTargetChoices").Invoke(_proposalOwner, null);
            check(npcTarget.TargetHostage == secondHero && npcTarget.Phase == VoluntaryPactProposalPhase.Delivery
                && _filingCalls == 1 && _lastIgnoreInfluence,
                "An NPC selects an eligible hostage and files its free ballot even with negative influence");
            var playerTarget = new VoluntaryPactProposalRecord { Id = "player-target", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, DurationDays = 100, BaselineDays = 50,
                Phase = VoluntaryPactProposalPhase.SourceVote };
            _proposalOwner = new VoluntaryPactProposalBehavior();
            check((bool)track.Invoke(_proposalOwner, new object[] { playerTarget })
                && (bool)resolve.Invoke(_proposalOwner, new object[] { playerTarget.Id, _first, true }),
                "A player-ruler offer reaches the target-choice phase");
            _playerTarget = true; _filingCalls = 0;
            AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "AdvanceTargetChoices").Invoke(_proposalOwner, null);
            check(playerTarget.Phase == VoluntaryPactProposalPhase.TargetChoice && playerTarget.TargetHostage == null && _filingCalls == 0,
                "A player ruler's hostage is never selected or voted on automatically");
            var applyPlayerChoice = AccessTools.Method(typeof(VoluntaryPactProposalBehavior), "ApplyPlayerTargetChoice");
            check(!(bool)applyPlayerChoice.Invoke(_proposalOwner, new object[] { playerTarget, firstHero })
                && (bool)applyPlayerChoice.Invoke(_proposalOwner, new object[] { playerTarget, secondHero })
                && playerTarget.Phase == VoluntaryPactProposalPhase.Delivery && _filingCalls == 1,
                "A player-selected eligible relative advances the free receiving ballot");
            _playerTarget = false;
            _testDay = 0;
            Type vm = typeof(KingdomDecisionsVM);
            Type uiPatch = mod.GetType("BellumCivile.Patches.VoluntaryPactDecisionUIPatch", true);
            MethodInfo factory = AccessTools.Method(vm, "GetDecisionItemBasedOnType");
            harmony.Patch(factory, prefix: new HarmonyMethod(AccessTools.Method(uiPatch, "Prefix")));
            check(Harmony.GetPatchInfo(factory).Prefixes.Any(p => p.owner == harmony.Id),
                "The custom native ballot UI adapter installs on the 1.4.8 decision factory");
            Type diplomacyPatch = mod.GetType("BellumCivile.Patches.VoluntaryPactDiplomacyPatch", true);
            MethodInfo peaceSelection = AccessTools.Method(typeof(KingdomDiplomacyVM), "OnSetPeaceItem");
            harmony.Patch(peaceSelection, postfix: new HarmonyMethod(AccessTools.Method(diplomacyPatch, "Postfix")));
            check(Harmony.GetPatchInfo(peaceSelection).Postfixes.Any(p => p.owner == harmony.Id),
                "The voluntary pledge action adapter installs on the 1.4.8 peace selection");
            var establish = AccessTools.Method(typeof(HostagePactBehavior), "TryEstablishVoluntary");
            bool Run(HostagePactBehavior b) => (bool)establish.Invoke(b, new object[] { _first, _second, firstHero, secondHero, 100, 50 });
            List<HostagePactRecord> Pacts(HostagePactBehavior b) => (List<HostagePactRecord>)AccessTools.Field(typeof(HostagePactBehavior), "_pacts").GetValue(b);

            _allied = _allyOnSecondPlacement = false; _placements = _recoveryCalls = 0;
            var success = new HostagePactBehavior();
            check(Run(success) && _placements == 2 && Pacts(success).Count == 1,
                "Voluntary placement stages exactly two hostages and creates one pact");
            var pact = Pacts(success).Single();
            check(pact.VoluntaryPeacetime && pact.Phase == HostagePactPhase.Active && pact.EndDay == 100
                && pact.FirstHostage.NegotiatedCost == 35 && pact.SecondHostage.NegotiatedCost == 17,
                "Voluntary placement snapshots origin, duration and both valid baseline prices");
            var active = AccessTools.Method(typeof(HostagePactBehavior), "HasActiveVoluntaryPact");
            bool Active(Kingdom a, Kingdom b) => (bool)active.Invoke(success, new object[] { a, b });
            _inCustody = true;
            check(Active(_first, _second) && Active(_second, _first), "Active voluntary pact is recognized in both realm directions");
            _allied = true;
            check(Active(_first, _second), "An existing pact remains active after the realms become allies");
            _allied = false; _inCustody = false;
            check(!Active(_first, _second), "Lost custody removes the voluntary alliance incentive");
            _inCustody = true; pact.VoluntaryPeacetime = false;
            check(!Active(_first, _second), "Old and wartime pacts never gain the voluntary alliance incentive");
            pact.VoluntaryPeacetime = true;
            check(!Run(success) && _placements == 2, "Existing pair prevents a duplicate handover");

            _allied = false; _allyOnSecondPlacement = true; _placements = _recoveryCalls = 0;
            var interrupted = new HostagePactBehavior();
            check(!Run(interrupted) && _placements == 2 && _recoveryCalls == 1
                && Pacts(interrupted).Single().Phase == HostagePactPhase.Resolving
                && Pacts(interrupted).Single().EndReason == HostagePactEndReason.InvalidCustody,
                "Alliance formed during placement aborts activation and schedules custody recovery");

            _allied = _allyOnSecondPlacement = false; _placements = _recoveryCalls = 0; _inCustody = true;
            var journal = new HostagePactBehavior();
            var proposal = new VoluntaryPactProposalRecord { Id = "saved-proposal", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, TargetHostage = secondHero, DurationDays = 100, BaselineDays = 50,
                Phase = VoluntaryPactProposalPhase.Delivery };
            var establishForProposal = AccessTools.Method(typeof(HostagePactBehavior), "TryEstablishVoluntaryForProposal");
            object[] attempt = { proposal, null };
            check((bool)establishForProposal.Invoke(journal, attempt) && proposal.HandoverStarted
                && ((HostagePactRecord)attempt[1]).VoluntaryProposalId == proposal.Id && _placements == 2,
                "Proposal ID is recorded on the pact before the mutual handover");
            object firstResult = attempt[1]; attempt[1] = null; _allied = true;
            check((bool)establishForProposal.Invoke(journal, attempt) && ReferenceEquals(firstResult, attempt[1]) && _placements == 2,
                "Reload-style recovery finds the completed pact without retaking hostages, even after alliance");
            _allied = false;
            var missing = new HostagePactBehavior(); attempt[1] = null;
            check(!(bool)establishForProposal.Invoke(missing, attempt) && _placements == 2,
                "An interrupted handover with no surviving receipt is never replayed");

            var abort = AccessTools.Method(typeof(HostagePactBehavior), "TryAbortUnpaidVoluntaryProposal");
            var savedPact = (HostagePactRecord)firstResult;
            proposal.PactId = savedPact.Id;
            proposal.GoldDebited = true;
            check(!(bool)abort.Invoke(journal, new object[] { proposal }) && savedPact.Phase == HostagePactPhase.Active,
                "A committed debit cannot roll back its associated hostage pact");
            proposal.GoldDebited = false;
            proposal.PactId = "other-pact";
            check(!(bool)abort.Invoke(journal, new object[] { proposal }) && savedPact.Phase == HostagePactPhase.Active,
                "An unpaid rollback cannot affect a different pact receipt");
            proposal.PactId = savedPact.Id;
            check(!(bool)abort.Invoke(journal, new object[] { proposal })
                && savedPact.Phase == HostagePactPhase.Resolving
                && savedPact.EndReason == HostagePactEndReason.InvalidCustody,
                "An unpaid pact is resolved and recovery must finish before cancellation");

            var delivery = AccessTools.Method(typeof(HostagePactBehavior), "DeliverVoluntaryProposal");
            var freeProposal = new VoluntaryPactProposalRecord { Id = "free-proposal", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, TargetHostage = secondHero, DurationDays = 100, BaselineDays = 50,
                Phase = VoluntaryPactProposalPhase.Delivery };
            var delivered = new HostagePactBehavior(); _placements = 0;
            check(delivery.Invoke(delivered, new object[] { freeProposal }).ToString() == "Completed"
                && freeProposal.GoldDebited && freeProposal.GoldCredited && _placements == 2,
                "Delivery completes a free pact only after both hostages are placed");
            check(delivery.Invoke(delivered, new object[] { freeProposal }).ToString() == "Completed" && _placements == 2,
                "Retrying a completed delivery does not retake the hostages");
            var poorProposal = new VoluntaryPactProposalRecord { Id = "poor-proposal", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, TargetHostage = secondHero, DurationDays = 100, BaselineDays = 50,
                Compensation = 5000, Payer = _firstLeader, Payee = _secondLeader, Phase = VoluntaryPactProposalPhase.Delivery };
            poorProposal.Payer.Gold = 0;
            check(delivery.Invoke(new HostagePactBehavior(), new object[] { poorProposal }).ToString() == "Cancelled"
                && poorProposal.Phase == VoluntaryPactProposalPhase.Cancelled && _placements == 2,
                "Delivery cancels an unaffordable proposal before taking either hostage");
            var paidProposal = new VoluntaryPactProposalRecord { Id = "paid-proposal", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, TargetHostage = secondHero, DurationDays = 100, BaselineDays = 50,
                Compensation = 5000, Payer = _firstLeader, Payee = _secondLeader, Phase = VoluntaryPactProposalPhase.Delivery };
            paidProposal.Payer.Gold = 8000; paidProposal.Payee.Gold = 300;
            var paidDelivery = new HostagePactBehavior();
            check(delivery.Invoke(paidDelivery, new object[] { paidProposal }).ToString() == "Completed"
                && paidProposal.Payer.Gold == 3000 && paidProposal.Payee.Gold == 5300 && _placements == 4,
                "Delivery exchanges both hostages before paying the agreed compensation");
            check(delivery.Invoke(paidDelivery, new object[] { paidProposal }).ToString() == "Completed"
                && paidProposal.Payer.Gold == 3000 && paidProposal.Payee.Gold == 5300 && _placements == 4,
                "Retrying a paid delivery cannot repeat its exchange or compensation");
            var oldPayer = Blank<Hero>(); oldPayer.Gold = 10000;
            var changedRuler = new VoluntaryPactProposalRecord { Id = "changed-ruler", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, TargetHostage = secondHero, DurationDays = 100, BaselineDays = 50,
                Compensation = 5000, Payer = oldPayer, Payee = _secondLeader, Phase = VoluntaryPactProposalPhase.Delivery };
            check(delivery.Invoke(new HostagePactBehavior(), new object[] { changedRuler }).ToString() == "Cancelled"
                && oldPayer.Gold == 10000 && _placements == 4,
                "An unpaid offer cannot debit a former ruler after leadership changes");
            var changedFunds = new VoluntaryPactProposalRecord { Id = "funds-changed", Source = _first, Target = _second,
                SourceHouse = _firstHouse, TargetHouse = _secondHouse, Sponsor = _firstHouse,
                SourceHostage = firstHero, TargetHostage = secondHero, DurationDays = 100, BaselineDays = 50,
                Compensation = 5000, Payer = _firstLeader, Payee = _secondLeader, Phase = VoluntaryPactProposalPhase.Delivery };
            changedFunds.Payer.Gold = 5000; changedFunds.Payee.Gold = 0; _drainPayerOnSecondPlacement = changedFunds.Payer; _placements = 0;
            var interruptedFunds = new HostagePactBehavior();
            check(delivery.Invoke(interruptedFunds, new object[] { changedFunds }).ToString() == "Deferred"
                && !changedFunds.GoldDebited && changedFunds.Payee.Gold == 0
                && Pacts(interruptedFunds).Single().Phase == HostagePactPhase.Resolving,
                "Funds lost during placement trigger recovery without a partial payment");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            ticks.SetValue(null, oldTicks);
            _first = _second = null; _firstHouse = _secondHouse = null; _holding = null; _candidates = null;
            _drainPayerOnSecondPlacement = null;
            _proposalOwner = null;
            _firstLeader = _secondLeader = null;
            _nativePending = null; _filingMode = null;
            _neighbors = false;
            _playerTarget = false;
        }
    }
}
