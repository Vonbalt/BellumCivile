using System;
using System.Linq;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.ViewModelCollection.Barter;
using TaleWorlds.Core;

internal static partial class MarriageHouseholdTests
{
    private static void CheckDirectionalDowryBarter(Action<bool, string> check, Harmony harmony, Campaign campaign, Hero bride, Hero groom, Action reset)
    {
        var assembly = typeof(PlayerMarriageAgreement).Assembly;
        var barterPatch = assembly.GetType("BellumCivile.Patches.PlayerMarriageDowryBarterPatch");
        var guard = AccessTools.Method(assembly.GetType("BellumCivile.Patches.PlayerMarriageBarterGuard"), "Validate");
        var model = AccessTools.Property(typeof(GameModels), "DiplomacyModel");
        var oldModel = model.GetValue(campaign.Models);
        var barterModel = AccessTools.Property(typeof(GameModels), "BarterModel");
        var oldBarterModel = barterModel.GetValue(campaign.Models);
        var player = groom.Clan; var other = bride.Clan;
        check(harmony.CreateClassProcessor(barterPatch).Patch().Count == 1
            && harmony.CreateClassProcessor(assembly.GetType("BellumCivile.Patches.MarriageDowryBarterItemPatch")).Patch().Count == 1,
            "Directional marriage terms and the pre-filled gold amount bind to the installed native barter entry points");
        try
        {
            model.SetValue(campaign.Models, new DefaultDiplomacyModel());
            barterModel.SetValue(campaign.Models, new DefaultBarterModel());
            var payments = assembly.GetType("BellumCivile.MarriagePayments");
            var quoteMethod = AccessTools.Method(payments, "Quote");
            int Quote(bool crown, bool heir) => (int)quoteMethod.Invoke(null, new object[] { bride, crown, heir });
            check(bride.Clan.Tier == 4 && Quote(false, false) == 40000 && Quote(false, true) == 60000
                && Quote(true, false) == 85000 && Quote(true, true) == 85000,
                "Tier-four nobles use the adjusted quote and the Crown premium replaces, rather than stacks with, the clan heir premium");
            Outsiders.Add(bride);
            try { check(Quote(false, false) == 6000, "Retainer quotes stay affordable and unchanged at tier four"); }
            finally { Outsiders.Remove(bride); }

            foreach (Clan destination in new[] { player, other })
            {
                Clan sending = destination == player ? other : player;
                var payer = destination.Leader; var payee = sending.Leader;
                var wedding = new MarriageBarterable(player.Leader, null, groom, bride);
                var data = new BarterData(player.Leader, other.Leader, null, null);
                data.AddBarterable<OtherBarterGroup>(wedding, isContextDependent: true);
                wedding.SetIsOffered(true);
                var terms = new PlayerMarriageAgreement { Player = groom, Other = bride, PlayerHouse = player, OtherHouse = other,
                    Destination = destination, Negotiated = true, StandingRequired = true };
                var pricing = assembly.GetType("BellumCivile.PlayerMarriagePricing");
                AccessTools.Method(pricing, "BindAgreement").Invoke(null, new object[] { wedding, terms });
                object[] args = { data };
                check((bool)AccessTools.Method(barterPatch, "Prefix").Invoke(null, args), "A funded dowry opens native barter");
                var funded = (BarterData)args[0];
                var gold = funded.GetOfferedBarterables().OfType<GoldBarterable>().Single();
                int quote = gold.CurrentAmount;
                check(quote == (destination == player ? 85000 : 40000),
                    "The adjusted barter price depends on who leaves, not on the standing of the spouse who stays");
                var itemView = new BarterItemVM(gold, null, null, isFixed: true);
                check(gold.OriginalOwner == payer && quote > 1 && itemView.CurrentOfferedAmount == quote && gold.CurrentAmount == quote
                    && new BarterManager().IsOfferAcceptable(funded, other.Leader, null),
                    "Native barter renders the full dowry on the receiving house's side for either household");

                int payerGold = payer.Gold; int payeeGold = payee.Gold;
                gold.Apply();
                check(payer.Gold == payerGold && payee.Gold == payeeGold, "A standalone barter gold item cannot settle a dowry");
                payer.Gold = quote - 1;
                check(!(bool)guard.Invoke(null, new object[] { funded.GetOfferedBarterables() }), "Final barter preflight rejects a newly broke payer");
                payer.Gold = payerGold;
                if (destination == other) gold.CurrentAmount = quote - 1;
                int agreed = gold.CurrentAmount;
                wedding.Apply(); wedding.Apply();
                check(payer.Gold == payerGold - agreed && payee.Gold == payeeGold + agreed
                    && groom.Clan == destination && bride.Clan == destination,
                    "The negotiated amount transfers exactly once after a valid wedding");
                reset();
            }

            BarterData Open(Clan destination, PartyBase playerParty, PartyBase otherParty, out MarriageBarterable wedding)
            {
                wedding = new MarriageBarterable(player.Leader, playerParty, groom, bride);
                var data = new BarterData(player.Leader, other.Leader, playerParty, otherParty);
                data.AddBarterable<OtherBarterGroup>(wedding, isContextDependent: true);
                wedding.SetIsOffered(true);
                var terms = new PlayerMarriageAgreement { Player = groom, Other = bride, PlayerHouse = player, OtherHouse = other,
                    Destination = destination, Negotiated = true, StandingRequired = true };
                AccessTools.Method(assembly.GetType("BellumCivile.PlayerMarriagePricing"), "BindAgreement")
                    .Invoke(null, new object[] { wedding, terms });
                object[] args = { data };
                check((bool)AccessTools.Method(barterPatch, "Prefix").Invoke(null, args), "A valid match can enter barter below the quoted cash price");
                return (BarterData)args[0];
            }
            bool Valid(BarterData data) => (bool)guard.Invoke(null, new object[] { data.GetOfferedBarterables() });

            var mainParty = Blank<PartyBase>();
            var inventory = new ItemRoster();
            AccessTools.Property(typeof(PartyBase), "ItemRoster").SetValue(mainParty, inventory);
            var jewel = Blank<ItemObject>();
            AccessTools.Property(typeof(ItemObject), "Value").SetValue(jewel, 200000);
            var equipment = new EquipmentElement(jewel);
            inventory.AddToCounts(equipment, 1);
            int otherGold = other.Leader.Gold;
            player.Leader.Gold = 5000;
            var mixedPayment = Open(player, mainParty, null, out var weddingWithGoods);
            var valuable = mixedPayment.GetBarterables().OfType<ItemBarterable>().Single();
            check(!Valid(mixedPayment) && !new BarterManager().IsOfferAcceptable(mixedPayment, other.Leader, null),
                "Without the valuables the short cash offer is invalid and the native button stays disabled");
            valuable.CurrentAmount = 1;
            valuable.SetIsOffered(true);
            valuable.Apply();
            check(Valid(mixedPayment) && new BarterManager().IsOfferAcceptable(mixedPayment, other.Leader, null)
                && inventory.GetItemNumber(jewel) == 1 && other.Leader.Gold == otherGold,
                "Native barter values the inventory pledge but cannot take it before the wedding");
            inventory.AddToCounts(equipment, -1);
            check(!Valid(mixedPayment), "A moved inventory stack is rejected before payment or wedding");
            inventory.AddToCounts(equipment, 1);
            var veto = new Harmony("bellum.tests.dowry.wedding_veto");
            try
            {
                veto.Patch(AccessTools.Method(typeof(MarriageAction), "ApplyInternal"),
                    prefix: new HarmonyMethod(typeof(MarriageHouseholdTests), nameof(Skip)));
                weddingWithGoods.Apply();
                check(player.Leader.Gold == 5000 && other.Leader.Gold == otherGold && inventory.GetItemNumber(jewel) == 1
                    && Spouses.Count == 0, "A wedding veto returns both cash and items to the player");
            }
            finally { veto.UnpatchAll(veto.Id); }
            weddingWithGoods.Apply(); weddingWithGoods.Apply();
            check(player.Leader.Gold == 0 && other.Leader.Gold == otherGold + 85000 && inventory.GetItemNumber(jewel) == 0
                && Spouses[groom] == bride,
                "A successful wedding combines cash and goods once, even when the lord has no field party");
            reset();

            var npcParty = Blank<PartyBase>();
            var npcInventory = new ItemRoster();
            AccessTools.Property(typeof(PartyBase), "ItemRoster").SetValue(npcParty, npcInventory);
            inventory.AddToCounts(equipment, 2);
            var goodsOnly = Open(player, mainParty, npcParty, out var weddingWithoutCash);
            var goodsOnlyItem = goodsOnly.GetBarterables().OfType<ItemBarterable>().Single();
            goodsOnlyItem.CurrentAmount = 2;
            goodsOnlyItem.SetIsOffered(true);
            check(!goodsOnly.GetBarterables().OfType<GoldBarterable>().Any() && Valid(goodsOnly),
                "A player with no gold can still make a fully funded barter using only valuables");
            weddingWithoutCash.Apply();
            check(Spouses[groom] == bride && npcInventory.GetItemNumber(jewel) == 2 && inventory.GetItemNumber(jewel) == 0,
                "A lord with a field party receives the actual inventory items");
            reset();

            other.Leader.Gold = 8500;
            var capped = Open(other, null, null, out var weddingWithCap);
            var cappedGold = capped.GetOfferedBarterables().OfType<GoldBarterable>().Single();
            check(cappedGold.MaxAmount == 8500 && cappedGold.CurrentAmount == 8500 && Valid(capped),
                "The receiving NPC can table and negotiate a smaller offer capped by its available treasury");
            weddingWithCap.Apply();
            check(Spouses[groom] == bride && player.Leader.Gold == 8500 && other.Leader.Gold == 0,
                "The negotiated NPC payment settles at the offered amount, without creating gold");
            player.Leader.Gold = other.Leader.Gold = 500000;
            reset();
        }
        finally { model.SetValue(campaign.Models, oldModel); barterModel.SetValue(campaign.Models, oldBarterModel); }
    }
}
