using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class PeaceParleyExplanationTests
{
    private static Clan _member, _ruler, _player, _other;
    private static bool _sole;
    private static float _utility, _reserve, _liveInfluence, _memberBudgetBalance;
    private static bool NoCampaign(ref Campaign __result) { __result = null; return false; }
    private static bool NoCourt(ref CourtAgendaBehavior __result) { __result = null; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Ruler(ref Clan __result) { __result = _ruler; return false; }
    private static bool Eligible(ref IEnumerable<Clan> __result)
    { __result = _sole ? new[] { _member } : _member == _ruler ? new[] { _ruler, _other } : new[] { _ruler, _member }; return false; }
    private static bool Influence(ref float __result) { __result = _liveInfluence; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(100); return false; }
    private static bool Days(ref double __result) { __result = 100; return false; }
    private static bool Utility(List<TreatyCouncilReason> reasons, ref float utility)
    {
        reasons.Clear();
        if (_utility != 0) reasons.Add(new TreatyCouncilReason("Fixture treaty motive", _utility));
        utility = _utility;
        return false;
    }
    private static bool Budget(Clan clan, float cost, float? balanceOverride, MethodBase __originalMethod, ref object __result)
    {
        float balance = balanceOverride ?? _liveInfluence;
        if (clan == _member) _memberBudgetBalance = balance;
        __result = Activator.CreateInstance(((MethodInfo)__originalMethod).ReturnType, clan, balance, cost, _reserve);
        return false;
    }
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(TreatyCouncilMemberEvaluation).Assembly;
        var serviceType = assembly.GetType("BellumCivile.TreatyRatificationService", true);
        var snapshotType = assembly.GetType("BellumCivile.TreatyCouncilSnapshot", true);
        var snapshotRowType = assembly.GetType("BellumCivile.TreatyCouncilClanSnapshot", true);
        var snapshotConstructor = snapshotType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var voteHelper = assembly.GetType("BellumCivile.PoliticalInfluenceVoteHelper", true);
        var originalCommitment = voteHelper.GetMethods().Single(method => method.Name == "GetCommitment"
            && method.GetParameters().Length == 4 && method.GetParameters()[1].ParameterType == typeof(Clan));
        var fundedCommitment = voteHelper.GetMethods().Single(method => method.Name == "GetCommitment"
            && method.GetParameters().Length == 3 && method.GetParameters()[1].ParameterType == typeof(float));
        var evaluate = AccessTools.Method(serviceType, "EvaluateCouncil");
        var service = Activator.CreateInstance(serviceType, true);
        var kingdom = Blank<Kingdom>(); kingdom.StringId = "explanation_realm";
        var npc = Blank<Clan>(); npc.StringId = "explanation_noble";
        var crown = Blank<Clan>(); crown.StringId = "explanation_crown";
        var player = Blank<Clan>(); player.StringId = "explanation_player";
        var war = new WarScoreRecord("explanation_war", kingdom.StringId, "opponent", 0, null);
        var harmony = new Harmony("bellum.test.parley_explanations");
        void Patch(MethodBase method, string name)
        {
            if (method == null) throw new MissingMethodException(name);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(PeaceParleyExplanationTests), name));
        }
        TreatyCouncilMemberEvaluation Evaluate(float influence, TreatyProposalRecord proposal = null)
        {
            var rows = Array.CreateInstance(snapshotRowType, 1);
            rows.SetValue(Activator.CreateInstance(snapshotRowType, _member.StringId, 45f, 100f, influence), 0);
            object snapshot = snapshotConstructor.Invoke(new object[] { rows });
            proposal = proposal ?? new TreatyProposalRecord(war.WarKey, kingdom.StringId, "opponent", kingdom.StringId, 100, 0, false);
            proposal.AddTerm(new TreatyTermRecord(TreatyTermType.WhitePeace, 0));
            var council = (TreatyCouncilEvaluation)evaluate.Invoke(service, new[] { war, proposal, kingdom, (object)true, snapshot });
            return council.Members.Single(member => member.Clan == _member);
        }
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Campaign), "Current"), nameof(NoCampaign));
            Patch(AccessTools.PropertyGetter(typeof(CourtAgendaBehavior), "Current"), nameof(NoCourt));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Influence"), nameof(Influence));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "ToDays"), nameof(Days));
            Patch(AccessTools.Method(serviceType, "GetEligibleClans"), nameof(Eligible));
            Patch(AccessTools.Method(serviceType, "ApplyRealmOverextension"), nameof(Utility));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.NpcInfluenceBudgetService", true), "Assess"), nameof(Budget));
            _member = npc; _ruler = crown; _player = player; _other = npc; _sole = false;
            _liveInfluence = 1000;
            foreach (float utility in new[] { -100f, -31f, -30f, -10.001f, -10f, -9.001f, -9f, -1f, 0f, 9.999f, 10f, 10.999f, 11f, 30.999f, 31f, 100f })
            foreach (float influence in new[] { -1f, 0f, 24.999f, 25f, 74.999f, 75f, 149.999f, 150f, 1000f })
            foreach (float reserve in new[] { 0f, 100f, 200f, 440f })
            {
                _utility = utility; _reserve = reserve;
                float snapshotBalance = Math.Max(0, influence);
                object[] previousArguments = { utility, npc, null, snapshotBalance };
                int expectedCommitment = (int)originalCommitment.Invoke(null, previousArguments);
                var expectedStance = (TreatyCouncilVoteStance)previousArguments[2];
                object[] fundedArguments = { utility, 150f, null };
                fundedCommitment.Invoke(null, fundedArguments);
                var intendedStance = (TreatyCouncilVoteStance)fundedArguments[2];
                var expectedReason = expectedStance != TreatyCouncilVoteStance.Abstain ? TreatyCouncilAbstentionReason.None
                    : intendedStance == TreatyCouncilVoteStance.Abstain ? TreatyCouncilAbstentionReason.NeutralAssessment
                    : snapshotBalance < 25 ? TreatyCouncilAbstentionReason.InsufficientInfluence
                    : TreatyCouncilAbstentionReason.ProtectedInfluence;
                var actual = Evaluate(influence);
                string label = "utility=" + utility + "; influence=" + influence + "; reserve=" + reserve;
                check(actual.Stance == expectedStance && actual.InfluenceCommitment == expectedCommitment
                    && actual.Utility == utility && actual.Reasons.Sum(reason => reason.Amount) == utility,
                    "Explanation metadata preserves the previous vote, exact commitment and every utility point: " + label);
                check(actual.AbstentionReason == expectedReason && actual.Influence == snapshotBalance
                    && _memberBudgetBalance == snapshotBalance,
                    "Cause follows the actual tiers and snapshotted balance, with neutrality before affordability: " + label);
            }

            _reserve = 0;
            foreach (var boundary in new[] { Tuple.Create(10f, TreatyCouncilVoteStance.Abstain),
                Tuple.Create(11f, TreatyCouncilVoteStance.Yay), Tuple.Create(-9f, TreatyCouncilVoteStance.Abstain),
                Tuple.Create(-10f, TreatyCouncilVoteStance.Nay) })
            {
                _utility = boundary.Item1;
                check(Evaluate(1000).Stance == boundary.Item2, "Existing shared commitment boundary remains unchanged at " + boundary.Item1);
            }

            _member = player;
            foreach (float utility in new[] { -100f, 0f, 100f })
            foreach (bool submitted in new[] { false, true })
            {
                _utility = utility;
                var proposal = new TreatyProposalRecord(war.WarKey, kingdom.StringId, "opponent", kingdom.StringId, 100, 0, false);
                if (submitted) proposal.SetPlayerVote(TreatyCouncilVoteStance.Abstain, 0);
                var actual = Evaluate(0, proposal);
                check(actual.Stance == TreatyCouncilVoteStance.Abstain && actual.InfluenceCommitment == 0
                    && actual.AbstentionReason == (submitted ? TreatyCouncilAbstentionReason.PlayerAbstained : TreatyCouncilAbstentionReason.PlayerUndecided)
                    && Math.Abs(actual.Reasons.Sum(reason => reason.Amount) - actual.Utility) < .001f,
                    "Player abstention is distinct from an unsubmitted response without adding utility: submitted=" + submitted + "; utility=" + utility);
            }
            foreach (var stance in new[] { TreatyCouncilVoteStance.Yay, TreatyCouncilVoteStance.Nay })
            foreach (int amount in new[] { 0, 25, 75, 150 })
            {
                var proposal = new TreatyProposalRecord(war.WarKey, kingdom.StringId, "opponent", kingdom.StringId, 100, 0, false);
                proposal.SetPlayerVote(stance, amount);
                var actual = Evaluate(1000, proposal);
                check(actual.Stance == stance && actual.InfluenceCommitment == amount
                    && actual.AbstentionReason == TreatyCouncilAbstentionReason.None,
                    "Player choices retain their selected stance, including an incomplete zero-commitment vote: " + stance + " " + amount);
            }

            foreach (bool isPlayer in new[] { false, true })
            foreach (float utility in new[] { -30f, -10f, 0f, 9f, 10f, 30f })
            {
                _member = isPlayer ? player : npc; _ruler = _member; _sole = true;
                _utility = utility; _reserve = 440;
                var actual = Evaluate(0);
                check(actual.AbstentionReason == TreatyCouncilAbstentionReason.None && actual.InfluenceCommitment == 0
                    && actual.Stance == (utility >= 10f ? TreatyCouncilVoteStance.Yay
                        : utility <= -10f ? TreatyCouncilVoteStance.Nay : TreatyCouncilVoteStance.Abstain),
                    "Sole rulers retain their direct-decision thresholds without a fabricated financial abstention: player=" + isPlayer + "; utility=" + utility);
            }
            _member = player; _ruler = player; _sole = false;
            check(Evaluate(0).AbstentionReason == TreatyCouncilAbstentionReason.None,
                "Player sovereign ordinary abstention is not confused with an unsubmitted vassal vote");
            check(!typeof(TreatyCouncilMemberEvaluation).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Any(field => field.GetCustomAttributesData().Any(attribute => attribute.AttributeType.Name.StartsWith("Saveable"))),
                "Explanation metadata remains runtime-only with no save schema changes");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
