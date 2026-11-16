using System;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.UI.VanillaTabs.Kingdoms.Hierarchy;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

internal static partial class LordshipTests
{
    private static void CheckActionHints(Action<bool, string> check, LordshipBehavior registry, Clan clan,
        Settlement occupied, Settlement vacant)
    {
        var originalPlayer = _player;
        var originalTitles = FeudalTitleBehavior.Instance;
        var parent = Blank<HierarchyTitleNodeVM>();
        AccessTools.Field(typeof(HierarchyTitleNodeVM), "<Name>k__BackingField").SetValue(parent, "Barony of Test");
        HierarchyLordshipNodeVM Node(Settlement village) => new HierarchyLordshipNodeVM(village, parent, null, null);
        try
        {
            var foreign = Node(occupied);
            check(!foreign.IsGrantEnabled && !foreign.IsRevocationEnabled
                && foreign.GrantHint.HintText.ToString().Contains("parent barony")
                && foreign.RevocationHint.HintText.ToString().Contains("parent barony"),
                "Foreign-owned lordships explain why neither grant nor revoke is available");
            _player = clan;
            var held = Node(occupied);
            check(!held.IsGrantEnabled && held.IsRevocationEnabled
                && held.GrantHint.HintText.ToString().Contains("already has a holder")
                && held.RevocationHint.HintText.ToString().Contains("decrease by 15")
                && held.RevocationHint.HintText.ToString().Contains("indenture"),
                "An occupied lordship explains the blocked grant and the available revocation's consequences");
            var open = Node(vacant);
            check(open.IsGrantEnabled && !open.IsRevocationEnabled
                && open.GrantHint.HintText.ToString().Contains("increase by 10")
                && open.GrantHint.HintText.ToString().Contains("landless vassal")
                && open.RevocationHint.HintText.ToString().Contains("no holder"),
                "A vacant lordship explains the available grant and why revocation is unavailable");
            var titles = new FeudalTitleBehavior();
            AccessTools.PropertySetter(typeof(FeudalTitleBehavior), "Instance").Invoke(null, new object[] { titles });
            var custody = (System.Collections.Generic.Dictionary<string, string>)AccessTools.Field(typeof(FeudalTitleBehavior), "_allocationCustodianByTitle").GetValue(titles);
            custody[FeudalTitleBehavior.BuildBaronyTitleId(Bounds[vacant.Village])] = clan.StringId;
            var pending = Node(vacant);
            check(!pending.IsGrantEnabled && pending.GrantHint.HintText.ToString().Contains("awaiting allocation"),
                "An awaiting-allocation barony blocks grants and explains the real restriction");
            AccessTools.PropertySetter(typeof(LordshipBehavior), "Instance").Invoke(null, new object[] { null });
            var unavailable = Node(vacant);
            check(!unavailable.IsGrantEnabled && !unavailable.IsRevocationEnabled
                && unavailable.GrantHint.HintText.ToString().Contains("not available at this time"),
                "Unavailable lordship services cannot leave actionable controls with misleading hints");
        }
        finally
        {
            _player = originalPlayer;
            AccessTools.PropertySetter(typeof(FeudalTitleBehavior), "Instance").Invoke(null, new object[] { originalTitles });
            AccessTools.PropertySetter(typeof(LordshipBehavior), "Instance").Invoke(null, new object[] { registry });
        }
    }
}
