using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.UI.Parley;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class PeaceParleyBudgetRecoveryTests
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

    private static Campaign _campaign;
    private static Kingdom _winner, _loser, _playerRealm;
    private static Clan _player, _winnerRuler, _loserRuler;
    private static readonly Dictionary<string, Hero> Prisoners = new Dictionary<string, Hero>();
    private static bool _repriced;
    private static string _draftMode;
    private static int _drafts, _effects, _closed;
    private static string _outcome;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Campaign(ref Campaign __result) { __result = _campaign; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _playerRealm; return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result)
    { __result = __instance == _winner ? _winnerRuler : _loserRuler; return false; }
    private static bool Resolve(string kingdomId, ref Kingdom __result)
    { __result = kingdomId == "battania" ? _winner : kingdomId == "aserai" ? _loser : null; return false; }
    private static bool All(ref MBReadOnlyList<Kingdom> __result)
    { __result = new MBReadOnlyList<Kingdom>(new List<Kingdom> { _winner, _loser }); return false; }
    private static bool Name(Kingdom __instance, ref TextObject __result)
    { __result = new TextObject(__instance.StringId); return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(100); return false; }
    private static bool Days(ref double __result) { __result = 100; return false; }
    private static bool EmptyCandidates(MethodBase __originalMethod, ref object __result)
    {
        Type item = ((MethodInfo)__originalMethod).ReturnType.GetGenericArguments()[0];
        __result = Activator.CreateInstance(typeof(List<>).MakeGenericType(item));
        return false;
    }
    private static bool Captives(Kingdom __0, MethodBase __originalMethod, ref object __result)
    {
        Type item = ((MethodInfo)__originalMethod).ReturnType.GetGenericArguments()[0];
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(item));
        string prefix = __0 == _loser ? "demand" : "offering";
        foreach (string suffix in new[] { "10", "5a", "5b" })
        {
            int cost = suffix == "10" && !(prefix == "offering" && _repriced) ? 10 : 5;
            list.Add(Activator.CreateInstance(item, Prisoners[prefix + suffix], null, cost));
        }
        __result = list;
        return false;
    }
    private static bool Draft(TreatyProposalRecord proposal)
    {
        _drafts++;
        if (_draftMode == "throw") throw new InvalidOperationException("budget recovery fixture");
        if (_draftMode == "invalid") proposal.ReplaceTerms(ScreenshotTerms());
        else if (_draftMode == "white") proposal.ReplaceTerms(new[] { new TreatyTermRecord(TreatyTermType.WhitePeace, 0) });
        else proposal.ReplaceTerms(new[] { new TreatyTermRecord(TreatyTermType.ConcedeDefeat, 10,
            fromKingdomId: "aserai", toKingdomId: "battania") });
        return false;
    }
    private static bool SideEffect()
    { _effects++; throw new InvalidOperationException("Invalid-budget parley reached a settlement side effect"); }
    private static bool Outcome(object outcome)
    {
        _outcome = (string)AccessTools.Property(outcome.GetType(), "Description").GetValue(outcome);
        return false;
    }

    private static List<TreatyTermRecord> ScreenshotTerms()
    {
        var terms = new List<TreatyTermRecord>();
        foreach (string suffix in new[] { "10", "5a", "5b" })
            terms.Add(new TreatyTermRecord(TreatyTermType.ReleasePrisoner, suffix == "10" ? 10 : 5,
                fromKingdomId: "aserai", toKingdomId: "battania", heroId: "demand" + suffix));
        terms.Add(new TreatyTermRecord(TreatyTermType.ConcedeDefeat, 10, fromKingdomId: "aserai", toKingdomId: "battania"));
        foreach (string suffix in new[] { "10", "5a", "5b" })
            terms.Add(new TreatyTermRecord(TreatyTermType.ReleasePrisoner, suffix == "10" ? 10 : 5,
                fromKingdomId: "battania", toKingdomId: "aserai", heroId: "offering" + suffix, wasVoluntaryOffering: true));
        return terms;
    }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.parley_budget_recovery");
        void Patch(MethodBase method, string name)
        {
            if (method == null) throw new MissingMethodException(name);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(PeaceParleyBudgetRecoveryTests), name));
        }
        var assembly = typeof(ForeignTreatyBehavior).Assembly;
        var service = assembly.GetType("BellumCivile.TreatyDraftService", true);
        var treaties = new ForeignTreatyBehavior();
        var scores = new WarScoreBehavior();
        _winner = Blank<Kingdom>(); _winner.StringId = "battania";
        _loser = Blank<Kingdom>(); _loser.StringId = "aserai";
        _player = Blank<Clan>(); _winnerRuler = Blank<Clan>(); _loserRuler = Blank<Clan>();
        _playerRealm = _winner;
        _campaign = Blank<Campaign>();
        var manager = new Manager(); manager.AddBehavior(treaties); manager.AddBehavior(scores);
        _campaign.AddCampaignBehaviorManager(manager);
        foreach (string prefix in new[] { "demand", "offering" })
        foreach (string suffix in new[] { "10", "5a", "5b" })
        { var hero = Blank<Hero>(); hero.StringId = prefix + suffix; Prisoners[hero.StringId] = hero; }
        var prepare = AccessTools.Method(typeof(ForeignTreatyBehavior), "TryPreparePendingProposalDraft");
        var proposals = (List<TreatyProposalRecord>)AccessTools.Field(typeof(ForeignTreatyBehavior), "_proposals").GetValue(treaties);
        var wars = (List<WarScoreRecord>)AccessTools.Field(typeof(WarScoreBehavior), "_wars").GetValue(scores);
        TreatyProposalRecord Setup(bool forced = false)
        {
            _drafts = _effects = _closed = 0; _outcome = null;
            var war = new WarScoreRecord("battania|aserai", "battania", "aserai", 0, null);
            AccessTools.Field(typeof(WarScoreRecord), "_score").SetValue(war, 20f);
            war.BeginParley(forced, 0);
            wars.Clear(); wars.Add(war);
            var proposal = new TreatyProposalRecord(war.WarKey, "battania", "aserai", "battania", 0, 20, forced);
            proposal.ReplaceTerms(ScreenshotTerms());
            proposals.Clear(); proposals.Add(proposal);
            return proposal;
        }
        bool Prepare(TreatyProposalRecord proposal, bool rebuild = true)
            => (bool)prepare.Invoke(treaties, new object[] { wars.Single(), proposal, null, rebuild });
        PeaceParleyVM View(TreatyProposalRecord proposal, bool ruler = false)
        {
            var vm = Blank<PeaceParleyVM>();
            AccessTools.Field(typeof(PeaceParleyVM), "_proposal").SetValue(vm, proposal);
            AccessTools.Field(typeof(PeaceParleyVM), "_closeAction").SetValue(vm, (Action)(() => _closed++));
            vm.IsPlayerRuler = ruler; vm.IsPlayerVassal = !ruler;
            return vm;
        }
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Campaign), "Current"), nameof(Campaign));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.Method(typeof(WarPeaceRevampBehavior), "GetPlayerPoliticalKingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(All));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Name"), nameof(Name));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "ResolveKingdom"), nameof(Resolve));
            Patch(AccessTools.Method(service, "ResolveKingdom"), nameof(Resolve));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "IsTreatyRealmAvailable"), nameof(Yes));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.StorylineWarProtectionHelper", true), "IsPeaceBlocked"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "ToDays"), nameof(Days));
            Patch(AccessTools.Method(service, "GetAvailableFiefTransfers"), nameof(EmptyCandidates));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.ClientWarTerritory", true), "Candidates"), nameof(EmptyCandidates));
            Patch(AccessTools.Method(service, "GetAvailablePrisonerReleases"), nameof(Captives));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "DraftAiTerms"), nameof(Draft));
            Patch(AccessTools.Method(typeof(WarScoreBehavior), "IsMutualWhitePeaceEligible"), nameof(No));
            Patch(AccessTools.Method(typeof(PeaceParleyVM), "ShowTreatyOutcomeInquiry"), nameof(Outcome));
            foreach (string name in new[] { "SpendCouncilCommitments", "SpendRulerOverride", "ApplyTreaty", "ApplyFinalCouncilConsequences" })
                Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), name), nameof(SideEffect));

            var initial = Setup(); _repriced = false; _draftMode = "valid";
            check(initial.UsedWarScore == 20 && Prepare(initial) && _drafts == 0 && initial.UsedWarScore == 20,
                "Valid reciprocal package survives real validation without an unnecessary AI rewrite");
            _repriced = true;
            check(Prepare(initial, false) && initial.UsedWarScore == 23 && initial.IsOverBudget,
                "Real pending-draft normalization reproduces the screenshot after a prisoner loses value");
            var impacts = (IEnumerable)AccessTools.Method(assembly.GetType("BellumCivile.TreatyWarScoreAccounting", true), "GetEffectiveTermImpacts")
                .Invoke(null, new object[] { initial.Terms, "battania", 20 });
            check(impacts.Cast<int>().SequenceEqual(new[] { 10, 5, 5, 10, -2, -2, -3 }),
                "Normalized screenshot has the exact displayed term impacts and three-point excess");
            initial.SetPlayerVote(TreatyCouncilVoteStance.Yay, 75);
            check(Prepare(initial) && !initial.IsOverBudget && initial.UsedWarScore == 10 && _drafts == 1,
                "NPC-controlled stale draft is rebuilt once and strictly validated within its original budget");
            check(!initial.PlayerVoteSubmitted && initial.PlayerInfluenceCommitment == 0
                && initial.PlayerVoteStance == TreatyCouncilVoteStance.Abstain && _effects == 0,
                "Rebuilt package clears the old vote without spending influence or applying terms");
            check(initial.WarScoreBudget == 20 && initial.State == TreatyProposalState.ParleyPending && wars.Single().ParleyPending,
                "Budget repair keeps the original leverage and a reviewable pending parley");

            foreach (string mode in new[] { "invalid", "throw" })
            {
                _draftMode = mode;
                var failed = Setup();
                check(!Prepare(failed) && _drafts == 1 && failed.State == TreatyProposalState.Cancelled
                    && failed.ResolutionNote == "the draft exceeds the available war score" && !wars.Single().ParleyPending,
                    "Failed AI budget repair cancels safely without retrying indefinitely: " + mode);
                check(_effects == 0 && !failed.PlayerInfluenceSpent && wars.Single().IsActive,
                    "Failed rebuild has no political, payment or settlement side effects: " + mode);
            }
            _draftMode = "white";
            var white = Setup();
            check(!Prepare(white) && white.State == TreatyProposalState.Cancelled,
                "Rebuild cannot substitute an ineligible ordinary white peace");
            var forcedWhite = Setup(true);
            check(Prepare(forcedWhite) && forcedWhite.UsedWarScore == 0 && wars.Single().ParleyForced,
                "Forced settlement retains its existing termless-capitulation fallback");

            _draftMode = "throw";
            foreach (bool forced in new[] { false, true })
            foreach (TreatyCouncilVoteStance stance in Enum.GetValues(typeof(TreatyCouncilVoteStance)))
            {
                var fallback = Setup(forced);
                fallback.SetPlayerVote(stance, stance == TreatyCouncilVoteStance.Abstain ? 0 : 75);
                View(fallback).ExecuteSignTreaty();
                check(_closed == 1 && fallback.State == TreatyProposalState.Cancelled && fallback.IsOverBudget
                    && _drafts == 0 && !wars.Single().ParleyPending,
                    "Vassal can leave a late invalid draft without a silent redraft: " + stance + "; forced=" + forced);
                check(_effects == 0 && !fallback.PlayerInfluenceSpent && !wars.Single().ResolutionPending && wars.Single().IsActive,
                    "Invalid vassal submission cannot spend influence, change relations or apply terms: " + stance + "; forced=" + forced);
                check(_outcome != null && _outcome.Contains("exceeds the available war score")
                    && _outcome.Contains("without spending influence") && _outcome.Contains("war continues")
                    && !_outcome.Contains("ceased to exist"), "Closed parley explains the budget failure accurately");
                check(!treaties.TrySignPlayerParley(fallback.ProposalId, out _) && _effects == 0,
                    "Repeated submission cannot reopen or apply a cancelled invalid proposal");
            }
            foreach (bool missingVote in new[] { true, false })
            {
                var invalidVote = Setup();
                if (!missingVote) invalidVote.SetPlayerVote(TreatyCouncilVoteStance.Yay, 0);
                check(!treaties.TrySignPlayerParley(invalidVote.ProposalId, out _)
                    && invalidVote.State == TreatyProposalState.ParleyPending && wars.Single().ParleyPending && _effects == 0,
                    "Budget fallback still requires a submitted valid response: missing=" + missingVote);
            }
            var restoredPrice = Setup();
            Prepare(restoredPrice, false);
            restoredPrice.SetPlayerVote(TreatyCouncilVoteStance.Abstain, 0);
            _repriced = false;
            View(restoredPrice).ExecuteSignTreaty();
            check(_closed == 1 && restoredPrice.State == TreatyProposalState.Cancelled && _effects == 0
                && !restoredPrice.PlayerInfluenceSpent, "Clicking the invalid-draft exit never unexpectedly seals a repriced treaty");
            _repriced = true;
            foreach (bool playerWins in new[] { false, true })
            {
                Clan previous = playerWins ? _winnerRuler : _loserRuler;
                if (playerWins) _winnerRuler = _player; else _loserRuler = _player;
                _playerRealm = playerWins ? _winner : _loser;
                var manual = Setup();
                check(Prepare(manual) && manual.IsOverBudget && _drafts == 0,
                    "Actual ruling authority preserves manual drafts even with an NPC drafter: playerWins=" + playerWins);
                check(!treaties.TrySignPlayerParley(manual.ProposalId, out _)
                    && manual.State == TreatyProposalState.ParleyPending && _effects == 0,
                    "Player ruler must correct the draft and cannot use the vassal escape route: playerWins=" + playerWins);
                if (playerWins) _winnerRuler = previous; else _loserRuler = previous;
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); Prisoners.Clear(); _campaign = null; }
    }
}
