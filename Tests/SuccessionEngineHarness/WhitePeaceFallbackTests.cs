using System;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class WhitePeaceFallbackTests
{
    private static Clan _player, _ruler;
    private static Kingdom _realm;
    private static bool Campaign(ref Campaign __result) { __result = null; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Ruler(ref Clan __result) { __result = _ruler; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _realm; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.test.white_fallback");
        var behavior = new ForeignTreatyBehavior();
        var validate = AccessTools.Method(typeof(ForeignTreatyBehavior), "TryValidateCouncilControlledWhitePeace");
        bool Valid(WarScoreRecord war, TreatyProposalRecord proposal)
            => (bool)validate.Invoke(behavior, new object[] { war, proposal, null });
        WarScoreRecord War() => new WarScoreRecord("a|b", "a", "b", 0, null);
        TreatyProposalRecord Proposal(bool forced = false)
            => new TreatyProposalRecord("a|b", "a", "b", "a", 0, 20, forced);
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(Campaign), "Current"), prefix: new HarmonyMethod(typeof(WhitePeaceFallbackTests), nameof(Campaign)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), prefix: new HarmonyMethod(typeof(WhitePeaceFallbackTests), nameof(Player)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), prefix: new HarmonyMethod(typeof(WhitePeaceFallbackTests), nameof(Ruler)));
            harmony.Patch(AccessTools.Method(typeof(WarPeaceRevampBehavior), "GetPlayerPoliticalKingdom"), prefix: new HarmonyMethod(typeof(WhitePeaceFallbackTests), nameof(Realm)));
            var war = War(); var proposal = Proposal(); war.BeginParley(false, 0);
            check(!Valid(war, proposal) && proposal.State == TreatyProposalState.Cancelled && !war.ParleyPending,
                "Empty automatic fallback fails closed and releases the parley when eligibility is unavailable");
            war = War(); proposal = Proposal(); war.BeginParley(false, 0);
            proposal.AddTerm(new TreatyTermRecord(TreatyTermType.WhitePeace, 0));
            check(!Valid(war, proposal) && proposal.State == TreatyProposalState.Cancelled && !war.ParleyPending,
                "Explicit automatic white fallback is cancelled rather than rejected");
            check(!proposal.PlayerInfluenceSpent && !war.ResolutionPending,
                "Eligibility cancellation does not spend player influence or begin settlement");
            war = War(); proposal = Proposal(true); war.BeginParley(true, 0);
            check(Valid(war, proposal) && war.ParleyPending && proposal.State == TreatyProposalState.ParleyPending,
                "Termless total victory is not stranded by voluntary white-peace restrictions");
            war = War(); proposal = Proposal();
            proposal.AddTerm(new TreatyTermRecord(TreatyTermType.Reparations, 1, goldAmount: 5000, fromKingdomId: "b", toKingdomId: "a"));
            check(Valid(war, proposal), "Nonwhite terms retain ordinary treaty utility and validation");
            _player = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
            _realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
            _realm.StringId = "a"; _ruler = _player;
            proposal = Proposal();
            check(Valid(War(), proposal), "Participating player ruler retains manual white-peace negotiation");
            _ruler = null; proposal = Proposal();
            check(!Valid(War(), proposal), "Player-vassal entry routing does not bypass NPC council white-peace eligibility");
            _ruler = _player; _realm.StringId = "unrelated"; proposal = Proposal();
            check(!Valid(War(), proposal), "Unrelated player ruler cannot authorize another realm's white fallback");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            _player = _ruler = null; _realm = null;
        }
    }
}
