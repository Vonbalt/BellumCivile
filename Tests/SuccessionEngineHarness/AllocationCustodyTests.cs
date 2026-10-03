using System;
using System.Collections.Generic;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Actions;

internal static class AllocationCustodyTests
{
    internal static void Run(Action<bool, string> check)
    {
        var titles = new FeudalTitleBehavior();
        var type = typeof(FeudalTitleBehavior);
        var records = (Dictionary<string, string>)AccessTools.Field(type, "_allocationCustodianByTitle").GetValue(titles);
        var consume = AccessTools.Method(type, "ConsumeAllocationCustody");
        bool Consume(string oldClan, string newClan, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
            => (bool)consume.Invoke(titles, new object[] { "fief", oldClan, newClan, detail });
        var vote = ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByKingDecision;
        var transfer = ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.Default;
        records["fief"] = "crown";
        check(!Consume("traitor", "crown", transfer) && records.ContainsKey("fief"), "Confiscation into custody preserves the allocation marker");
        check(Consume("crown", "player", vote) && records.Count == 0, "Distribution consumes Crown custody and suppresses its displaced-owner claim");
        check(!Consume("player", "other", vote), "Later personal ownership transfer is not treated as custody");
        records["fief"] = "crown";
        check(Consume("crown", "crown", vote) && records.Count == 0, "An award retained by the Crown finalizes custody");
        records["fief"] = "crown";
        check(!Consume("crown", "crown", transfer) && records.Count == 1, "Same-clan leadership transfer does not finalize custody");
        check(Consume("crown", "enemy", ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege) && records.Count == 0,
            "Enemy conquest clears old custody without inventing custodian rights");
        records["fief"] = "old_crown";
        check(!Consume("unrelated", "recipient", vote) && records.Count == 0, "Stale custody cannot suppress an unrelated house's legitimate claim");
    }
}
