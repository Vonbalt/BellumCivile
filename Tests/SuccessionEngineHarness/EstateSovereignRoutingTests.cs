using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class EstateSovereignRoutingTests
{
    private static FeudalTitleRecord _parent;
    private static bool Parent(ref FeudalTitleRecord __result) { __result = _parent; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var source = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan)); source.StringId = "estate_source";
        var recipient = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan)); recipient.StringId = "estate_recipient";
        var record = new CrossClanEstateRecord { Source = source, RealmCrownTitleId = "political_crown" };
        var title = new FeudalTitleRecord("additional_crown", "Crown", FeudalTitleType.Kingdom,
            source.StringId, source.StringId, "", "", "", 0, 0);
        var share = new CrossClanEstateShare { Titles = new List<string> { title.TitleId } };
        var route = AccessTools.Method(typeof(PartitionSuccessionBehavior), "RequiresSovereignEstateExecutor");
        var receipt = AccessTools.Method(typeof(PartitionSuccessionBehavior), "ReconcileEstateTitleReceipt");
        var titles = (FeudalTitleBehavior)FormatterServices.GetUninitializedObject(typeof(FeudalTitleBehavior));
        bool Blocked() => (bool)route.Invoke(null, new object[] { record, titles, title });
        void Reconcile() => receipt.Invoke(null, new object[] { record, share, title, recipient });
        var h = new Harmony("bellum.test.estate_sovereign_routing");
        try
        {
            h.Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetParentTitle"),
                prefix: new HarmonyMethod(typeof(EstateSovereignRoutingTests), nameof(Parent)));
            _parent = null;
            check(Blocked(), "Fully controlled independent additional Crown retains sovereign safeguard");
            title.SetActive(false);
            check(!Blocked(), "Inactive higher title does not block an estate");
            Reconcile(); Reconcile();
            check(share.SupersededTitles.Count == 1 && share.DeliveredTitles.Count == 0,
                "Inactive-title reconciliation is idempotent and not false delivery");
            title.SetActive(true); share.SupersededTitles.Clear();
            title.SetDeJureHolder("third_party"); Reconcile();
            check(share.SupersededTitles.Contains(title.TitleId), "Later lawful third-party title ownership is preserved");
            share.SupersededTitles.Clear(); title.SetDeJureHolder(recipient.StringId); Reconcile(); Reconcile();
            check(share.DeliveredTitles.Count == 1, "Already inherited higher title produces exactly one delivery receipt");
            title.SetDeJureHolder(source.StringId); title.SetDeFactoHolder("occupier");
            check(!Blocked(), "Legal-only Crown can pass as property without transferring its current government");
            title.SetDeFactoHolder(source.StringId);
            _parent = new FeudalTitleRecord("empire", "Empire", FeudalTitleType.Empire,
                source.StringId, source.StringId, "", "", "", 0, 0);
            check(!Blocked(), "Kingdom bound beneath an active empire is not forced into independence");
            _parent = title;
            check(Blocked(), "Cyclic or equal-rank parent does not bypass the sovereign guard");
        }
        finally { h.UnpatchAll(h.Id); _parent = null; }
    }
}
