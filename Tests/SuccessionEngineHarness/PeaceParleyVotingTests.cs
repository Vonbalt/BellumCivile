using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.UI.Parley;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class PeaceParleyVotingTests
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
    private static Clan _player, _winnerRuler, _loserRuler, _winnerNoble, _loserNoble;
    private static readonly Dictionary<Clan, float> Balances = new Dictionary<Clan, float>();
    private static TreatyProposalRecord _proposal;
    private static bool _winnerAccepts, _loserAccepts, _storyBlocked, _realmsAvailable;
    private static int _closed, _applied, _consequences, _playerCharges, _evaluations;
    private static string _outcomeTitle, _outcomeBody;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Campaign(ref Campaign __result) { __result = _campaign; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _playerRealm; return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result)
    { __result = __instance == _winner ? _winnerRuler : _loserRuler; return false; }
    private static bool Resolve(string kingdomId, ref Kingdom __result)
    { __result = kingdomId == "voting_one" ? _winner : kingdomId == "voting_two" ? _loser : null; return false; }
    private static bool All(ref MBReadOnlyList<Kingdom> __result)
    { __result = new MBReadOnlyList<Kingdom>(new List<Kingdom> { _winner, _loser }); return false; }
    private static bool Name(Kingdom __instance, ref TextObject __result)
    { __result = new TextObject(__instance.StringId); return false; }
    private static bool Balance(Clan __instance, ref float __result)
    { __result = Balances[__instance]; return false; }
    private static bool Spend(Clan clan, float amount)
    {
        Balances[clan] += amount;
        if (clan == _player && amount < 0) _playerCharges++;
        return false;
    }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Zero(ref float __result) { __result = 0; return false; }
    private static bool Story(ref bool __result) { __result = _storyBlocked; return false; }
    private static bool Available(ref bool __result) { __result = _realmsAvailable; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(100); return false; }
    private static bool Days(ref double __result) { __result = 100; return false; }
    private static bool Draft(TreatyProposalRecord proposal)
    { proposal.ReplaceTerms(new[] { new TreatyTermRecord(TreatyTermType.WhitePeace, 0) }); return false; }
    private static bool Council(Kingdom kingdom, ref TreatyCouncilEvaluation __result)
    {
        _evaluations++;
        bool accepts = kingdom == _winner ? _winnerAccepts : _loserAccepts;
        var stance = accepts ? TreatyCouncilVoteStance.Yay : TreatyCouncilVoteStance.Nay;
        var clans = kingdom == _winner ? new[] { _winnerRuler, _winnerNoble } : new[] { _loserRuler, _loserNoble };
        var members = clans.Select(clan => new TreatyCouncilMemberEvaluation(clan, 45, Balances[clan],
            accepts ? 50 : -50, stance, 150, null)).ToList();
        if (kingdom == _playerRealm)
            members.Add(new TreatyCouncilMemberEvaluation(_player, 45, Balances[_player], 0,
                _proposal.PlayerVoteStance, _proposal.PlayerInfluenceCommitment, null));
        int yay = members.Where(m => m.Stance == TreatyCouncilVoteStance.Yay).Sum(m => m.InfluenceCommitment);
        int nay = members.Where(m => m.Stance == TreatyCouncilVoteStance.Nay).Sum(m => m.InfluenceCommitment);
        __result = new TreatyCouncilEvaluation(yay, nay, 0, Math.Max(0, nay - yay + 25),
            Math.Max(0, yay - nay + 25), members);
        return false;
    }
    private static bool Apply(WarScoreRecord war, TreatyProposalRecord proposal, ref bool __result)
    {
        _applied++;
        proposal.SetState(TreatyProposalState.Applied, "test settlement", 100);
        war.EndParley();
        __result = true;
        return false;
    }
    private static bool Consequences() { _consequences++; return false; }
    private static bool HideNotification() => false;
    private static bool Outcome(object outcome)
    {
        _outcomeTitle = (string)AccessTools.Property(outcome.GetType(), "Title").GetValue(outcome);
        _outcomeBody = (string)AccessTools.Property(outcome.GetType(), "Description").GetValue(outcome);
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.parley_voting");
        void Patch(MethodBase method, string name)
        {
            if (method == null) throw new MissingMethodException(name);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(PeaceParleyVotingTests), name));
        }
        var assembly = typeof(ForeignTreatyBehavior).Assembly;
        var treaties = new ForeignTreatyBehavior();
        var scores = new WarScoreBehavior();
        _campaign = Blank<Campaign>();
        var manager = new Manager(); manager.AddBehavior(treaties); manager.AddBehavior(scores);
        _campaign.AddCampaignBehaviorManager(manager);
        _winner = Blank<Kingdom>(); _winner.StringId = "voting_one";
        _loser = Blank<Kingdom>(); _loser.StringId = "voting_two";
        Clan House(string id) { var clan = Blank<Clan>(); clan.StringId = id; return clan; }
        _player = House("player"); _winnerRuler = House("ruler_one"); _loserRuler = House("ruler_two");
        _winnerNoble = House("noble_one"); _loserNoble = House("noble_two");
        var proposals = (List<TreatyProposalRecord>)AccessTools.Field(typeof(ForeignTreatyBehavior), "_proposals").GetValue(treaties);
        var wars = (List<WarScoreRecord>)AccessTools.Field(typeof(WarScoreBehavior), "_wars").GetValue(scores);
        WarScoreRecord Setup(bool playerWins = true)
        {
            _playerRealm = playerWins ? _winner : _loser;
            _closed = _applied = _consequences = _playerCharges = _evaluations = 0;
            _outcomeTitle = _outcomeBody = null;
            _storyBlocked = false; _realmsAvailable = true;
            _winnerAccepts = _loserAccepts = false;
            Balances.Clear();
            foreach (var clan in new[] { _player, _winnerRuler, _loserRuler, _winnerNoble, _loserNoble })
                Balances[clan] = 1000;
            // The existing 100-influence initiation charge has already been paid when voting begins.
            Balances[_player] = 900;
            var war = new WarScoreRecord("voting_one|voting_two", "voting_one", "voting_two", 100, null);
            war.BeginParley(false, 100);
            wars.Clear(); wars.Add(war);
            _proposal = new TreatyProposalRecord(war.WarKey, _winner.StringId, _loser.StringId,
                _winner.StringId, 100, 0, false);
            _proposal.AddTerm(new TreatyTermRecord(TreatyTermType.WhitePeace, 0));
            proposals.Clear(); proposals.Add(_proposal);
            return war;
        }
        PeaceParleyVM View()
        {
            var vm = Blank<PeaceParleyVM>();
            AccessTools.Field(typeof(PeaceParleyVM), "_proposal").SetValue(vm, _proposal);
            AccessTools.Field(typeof(PeaceParleyVM), "_closeAction").SetValue(vm, (Action)(() => _closed++));
            vm.IsPlayerVassal = true;
            return vm;
        }
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Campaign), "Current"), nameof(Campaign));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Influence"), nameof(Balance));
            Patch(AccessTools.Method(typeof(ChangeClanInfluenceAction), "Apply"), nameof(Spend));
            Patch(AccessTools.Method(typeof(WarPeaceRevampBehavior), "GetPlayerPoliticalKingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(All));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Name"), nameof(Name));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "ResolveKingdom"), nameof(Resolve));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "IsTreatyRealmAvailable"), nameof(Available));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "IsAiOnlyWar"), nameof(No));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "DraftAiTerms"), nameof(Draft));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "EvaluateCouncil"), nameof(Council));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "ApplyTreatyCore"), nameof(Apply));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "ApplyFinalCouncilConsequences"), nameof(Consequences));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "ShowTreatyRejectedMessage"), nameof(HideNotification));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.NpcInfluenceBudgetService", true), "GetProtectedReserve"), nameof(Zero));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.StorylineWarProtectionHelper", true), "IsPeaceBlocked"), nameof(Story));
            Patch(AccessTools.Method(typeof(WarScoreBehavior), "IsMutualWhitePeaceEligible"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "ToDays"), nameof(Days));
            Patch(AccessTools.Method(typeof(PeaceParleyVM), "ShowTreatyOutcomeInquiry"), nameof(Outcome));

            foreach (bool playerWins in new[] { false, true })
            foreach (bool winnerAccepts in new[] { false, true })
            foreach (bool loserAccepts in new[] { false, true })
            foreach (var stance in new[] { TreatyCouncilVoteStance.Yay, TreatyCouncilVoteStance.Nay, TreatyCouncilVoteStance.Abstain })
            foreach (int amount in stance == TreatyCouncilVoteStance.Abstain ? new[] { 0 } : new[] { 25, 75, 150 })
            {
                var war = Setup(playerWins);
                _winnerAccepts = winnerAccepts;
                _loserAccepts = loserAccepts;
                bool accept = winnerAccepts && loserAccepts;
                check(treaties.TrySetPlayerVote(_proposal.ProposalId, stance, amount, out _)
                    && Balances[_player] == 900, "Choosing a commitment records but does not spend it: " + stance + " " + amount);
                View().ExecuteSignTreaty();
                string label = "playerWins=" + playerWins + "; winnerAccepts=" + winnerAccepts
                    + "; loserAccepts=" + loserAccepts + "; vote=" + stance + "; amount=" + amount;
                check(_proposal.State == (accept ? TreatyProposalState.Applied : TreatyProposalState.Rejected)
                    && _closed == 1 && !war.ParleyPending && _evaluations == 2, "Manual white peace reaches both councils and closes normally: " + label);
                check(Balances[_player] == 900 - amount && _playerCharges == (amount == 0 ? 0 : 1)
                    && _proposal.PlayerInfluenceSpent == (amount > 0), "Actual commitment spending is exact, including rejection and free abstention: " + label);
                check(_applied == (accept ? 1 : 0) && _consequences == 1
                    && _outcomeTitle == (accept ? "Peace Treaty Ratified" : "Peace Treaty Rejected")
                    && !_outcomeBody.Contains("ceased to exist"), "Correct outcome replaces the false invalidation message: " + label);
                check(!treaties.TrySignPlayerParley(_proposal.ProposalId, out _)
                    && Balances[_player] == 900 - amount && _consequences == 1,
                    "Repeated finalization cannot charge or apply consequences twice: " + label);
            }

            foreach (string failure in new[] { "quest", "realm", "balance", "empty", "repaired" })
            {
                var war = Setup();
                treaties.TrySetPlayerVote(_proposal.ProposalId, TreatyCouncilVoteStance.Yay, 150, out _);
                if (failure == "quest") _storyBlocked = true;
                if (failure == "realm") _realmsAvailable = false;
                if (failure == "balance") AccessTools.Field(typeof(WarScoreRecord), "_score").SetValue(war, -20f);
                if (failure == "empty") _proposal.ReplaceTerms(Array.Empty<TreatyTermRecord>());
                if (failure == "repaired") _proposal.AddTerm(new TreatyTermRecord(TreatyTermType.ConcedeDefeat, 10));
                View().ExecuteSignTreaty();
                check(_proposal.State == TreatyProposalState.Cancelled && _closed == 1 && !war.ParleyPending
                    && Balances[_player] == 900 && _playerCharges == 0 && !_proposal.PlayerInfluenceSpent
                    && _evaluations == 0 && _applied == 0 && _consequences == 0,
                    "Real invalidation or unreviewed fallback stays free of vote and treaty effects: " + failure);
                check(_outcomeBody.Contains("ceased to exist") == (failure == "realm"),
                    "Cancellation wording only claims a lost war/realm for that actual failure: " + failure);
            }
            foreach (bool zeroCommitment in new[] { false, true })
            {
                var war = Setup();
                if (zeroCommitment) treaties.TrySetPlayerVote(_proposal.ProposalId, TreatyCouncilVoteStance.Yay, 0, out _);
                check(!treaties.TrySignPlayerParley(_proposal.ProposalId, out _)
                    && _proposal.State == TreatyProposalState.ParleyPending && war.ParleyPending
                    && Balances[_player] == 900 && _evaluations == 0,
                    "Manual exception still requires a submitted valid response: zeroCommitment=" + zeroCommitment);
            }
            var automaticWar = Setup();
            treaties.TrySetPlayerVote(_proposal.ProposalId, TreatyCouncilVoteStance.Yay, 150, out _);
            AccessTools.Method(typeof(ForeignTreatyBehavior), "ResolvePendingParley")
                .Invoke(treaties, new object[] { automaticWar, _proposal, true });
            check(_proposal.State == TreatyProposalState.Cancelled && _playerCharges == 0 && _evaluations == 0
                && !automaticWar.ParleyPending, "Automatic resolution cannot inherit the manual exception from a saved player vote");

            foreach (var reason in new[] {
                Tuple.Create("the council-controlled white peace no longer meets mutual peace eligibility", "automatic settlement"),
                Tuple.Create("the promised hostage could not be delivered", "hostage could not be delivered"),
                Tuple.Create("unknown future cancellation", "current circumstances") })
            {
                Setup();
                _proposal.SetState(TreatyProposalState.Cancelled, reason.Item1);
                object inquiry = AccessTools.Method(typeof(PeaceParleyVM), "BuildTreatyOutcomeInquiry").Invoke(View(), new object[] { false });
                string body = (string)AccessTools.Property(inquiry.GetType(), "Description").GetValue(inquiry);
                check(body.Contains(reason.Item2) && !body.Contains("ceased to exist"), "Cancellation reports its real reason safely: " + reason.Item1);
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); Balances.Clear(); _campaign = null; }
    }
}
