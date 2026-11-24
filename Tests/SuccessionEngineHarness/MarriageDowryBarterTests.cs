using System;
using System.Linq;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.ViewModelCollection.Barter;

internal static partial class MarriageHouseholdTests
{
    private static void CheckDirectionalDowryBarter(Action<bool, string> check, Harmony harmony, Campaign campaign, Hero bride, Hero groom, Action reset)
    {
        var assembly = typeof(PlayerMarriageAgreement).Assembly;
        var barterPatch = assembly.GetType("BellumCivile.Patches.PlayerMarriageDowryBarterPatch");
        var guard = AccessTools.Method(assembly.GetType("BellumCivile.Patches.PlayerMarriageBarterGuard"), "Validate");
        var model = AccessTools.Property(typeof(GameModels), "DiplomacyModel");
        var oldModel = model.GetValue(campaign.Models);
        var player = groom.Clan; var other = bride.Clan;
        check(harmony.CreateClassProcessor(barterPatch).Patch().Count == 1
            && harmony.CreateClassProcessor(assembly.GetType("BellumCivile.Patches.MarriageDowryBarterItemPatch")).Patch().Count == 1,
            "Directional marriage terms and the pre-filled gold amount bind to the installed native barter entry points");
        try
        {
            model.SetValue(campaign.Models, new DefaultDiplomacyModel());
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
        }
        finally { model.SetValue(campaign.Models, oldModel); }
    }
}
