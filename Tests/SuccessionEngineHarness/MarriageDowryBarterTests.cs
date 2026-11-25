using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile;
using BellumCivile.Behaviors;
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
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static partial class MarriageHouseholdTests
{
    private static Hero _pricedDisplayHeir;
    private static Kingdom _pricedDisplayRealm;
    private static bool DowryHeirRealms(Hero hero, ref System.Collections.Generic.IEnumerable<Kingdom> __result)
    { __result = hero == _pricedDisplayHeir ? new[] { _pricedDisplayRealm } : Array.Empty<Kingdom>(); return false; }
    private static bool DowryDisplayHeir(Kingdom kingdom, ref Hero heir, ref bool __result)
    { heir = kingdom == _pricedDisplayRealm ? _pricedDisplayHeir : null; __result = heir != null; return false; }
    private static bool BarterFixtureName(ref TextObject __result)
    { __result = new TextObject("Barter fixture"); return false; }

    private static BarterVM MakeMarriageBarterVM(BarterData data)
    {
        // Build the native draft lists without constructing the portrait layer.
        var vm = Blank<BarterVM>();
        var other = Blank<CharacterObject>();
        AccessTools.Field(typeof(CharacterObject), "_heroObject").SetValue(other, data.OtherHero);
        void Set(string field, object value) => AccessTools.Field(typeof(BarterVM), field).SetValue(vm, value);
        Set("_barterData", data);
        Set("_otherCharacter", other);
        Set("_leftList", new Dictionary<BarterGroup, MBBindingList<BarterItemVM>>());
        Set("_rightList", new Dictionary<BarterGroup, MBBindingList<BarterItemVM>>());
        Set("_barterList", new List<Dictionary<BarterGroup, MBBindingList<BarterItemVM>>>());
        Set("_offerList", new List<MBBindingList<BarterItemVM>>());
        foreach (string side in new[] { "Left", "Right" })
            foreach (string group in new[] { "Fief", "Prisoner", "Item", "Other", "Diplomatic", "Gold", "Offer" })
                AccessTools.Property(typeof(BarterVM), side + group + "List").SetValue(vm, new MBBindingList<BarterItemVM>());
        AccessTools.Method(typeof(BarterVM), "InitBarterList").Invoke(vm, new object[] { data });
        return vm;
    }

    private static void CheckDirectionalDowryBarter(Action<bool, string> check, Harmony harmony, Campaign campaign, Hero bride, Hero groom, Action reset)
    {
        var assembly = typeof(PlayerMarriageAgreement).Assembly;
        var barterPatch = assembly.GetType("BellumCivile.Patches.PlayerMarriageDowryBarterPatch");
        var guard = AccessTools.Method(assembly.GetType("BellumCivile.Patches.PlayerMarriageBarterGuard"), "Validate");
        var model = AccessTools.Property(typeof(GameModels), "DiplomacyModel");
        var oldModel = model.GetValue(campaign.Models);
        var barterModel = AccessTools.Property(typeof(GameModels), "BarterModel");
        var oldBarterModel = barterModel.GetValue(campaign.Models);
        var manager = AccessTools.Field(typeof(Campaign), "<BarterManager>k__BackingField");
        var oldManager = manager.GetValue(campaign);
        var player = groom.Clan; var other = bride.Clan;
        check(harmony.CreateClassProcessor(barterPatch).Patch().Count == 1
            && harmony.CreateClassProcessor(assembly.GetType("BellumCivile.Patches.MarriageDowryBarterItemPatch")).Patch().Count == 1,
            "Directional marriage terms and the pre-filled gold amount bind to the installed native barter entry points");
        try
        {
            model.SetValue(campaign.Models, new DefaultDiplomacyModel());
            barterModel.SetValue(campaign.Models, new DefaultBarterModel());
            manager.SetValue(campaign, new BarterManager());
            harmony.Patch(AccessTools.Method(typeof(BarterVM), nameof(BarterVM.RefreshValues)),
                prefix: new HarmonyMethod(typeof(MarriageHouseholdTests), nameof(Skip)));
            foreach (var type in new[] { typeof(MarriageBarterable), typeof(ItemBarterable) })
            {
                foreach (string method in new[] { nameof(Barterable.GetEncyclopediaLink), nameof(Barterable.GetVisualIdentifier) })
                    harmony.Patch(AccessTools.Method(type, method),
                        prefix: new HarmonyMethod(typeof(MarriageHouseholdTests), nameof(Skip)));
                harmony.Patch(AccessTools.PropertyGetter(type, "Name"),
                    prefix: new HarmonyMethod(typeof(MarriageHouseholdTests), nameof(BarterFixtureName)));
            }
            foreach (string name in new[] { "MarriageBarterResetPatch", "MarriageBarterAutoOfferPatch" })
                check(harmony.CreateClassProcessor(assembly.GetType("BellumCivile.Patches." + name)).Patch().Count == 1,
                    name + " binds to its native barter button");
            var payments = assembly.GetType("BellumCivile.MarriagePayments");
            var quoteMethod = AccessTools.Method(payments, "Quote");
            var policyType = assembly.GetType("BellumCivile.MarriageHouseholdPolicy");
            object Policy() => Activator.CreateInstance(policyType, true);
            int Quote() => (int)quoteMethod.Invoke(null, new object[] { bride, groom, Policy() });
            check(bride.Clan.Tier == 4 && Quote() == 85000,
                "Equal-standing tier-four houses pay 40,000 base plus the Crown premium without stacking the clan heir premium");
            Outsiders.Add(bride);
            try { check(Quote() == 6000, "Retainer quotes stay affordable and unchanged at tier four"); }
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
                    "Equal-standing houses get the correct departure and heir base price in either direction");
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

            var settingsField = AccessTools.Field(assembly.GetType("BellumCivile.BellumCivileOptions"), "_settings");
            object previousSettings = settingsField.GetValue(null);
            var settingsType = assembly.GetType("BellumCivile.BellumCivileSettings");
            object settings = Activator.CreateInstance(settingsType, nonPublic: true);
            var costSetting = AccessTools.Property(settingsType, "MarriageCostPercent");
            try
            {
                settingsField.SetValue(null, settings);
                var originalQuote = Open(player, null, null, out var originalWedding);
                var originalGold = originalQuote.GetOfferedBarterables().OfType<GoldBarterable>().Single();
                check((int)costSetting.GetValue(settings) == 100 && originalGold.CurrentAmount == 85000,
                    "The default marriage price setting keeps the existing Crown quote");
                foreach (var (multiplier, expected) in new[] { (50, 42500), (150, 127500), (-1, 21250), (300, 170000) })
                {
                    costSetting.SetValue(settings, multiplier);
                    check(Quote() == expected, "New marriage quotes use and clamp the live cost multiplier: " + multiplier);
                }
                var adjusted = Open(other, null, null, out _);
                check(adjusted.GetOfferedBarterables().OfType<GoldBarterable>().Single().CurrentAmount == 80000,
                    "The multiplier also scales the NPC's dowry when the player sends a spouse");
                check(originalGold.CurrentAmount == 85000 && originalWedding.GetUnitValueForFaction(other) == -85000 && Valid(originalQuote),
                    "An open barter keeps its original price after changing the multiplier");
                Outsiders.Add(bride);
                try { check(Quote() == 12000, "Retainer quotes use the same multiplier"); }
                finally { Outsiders.Remove(bride); }
            }
            finally { settingsField.SetValue(null, previousSettings); }

            var ordinary = new BarterData(player.Leader, other.Leader, null, null);
            var ordinaryGold = new GoldBarterable(player.Leader, other.Leader, null, null, 100);
            ordinary.AddBarterable<GoldBarterGroup>(ordinaryGold);
            ordinaryGold.CurrentAmount = 20;
            bool UsesNativeAutoOffer() => (bool)AccessTools.Method(
                assembly.GetType("BellumCivile.Patches.MarriageBarterAutoOfferPatch"), "Prefix")
                .Invoke(null, new object[] { null, ordinary });
            AccessTools.Method(assembly.GetType("BellumCivile.Patches.MarriageBarterResetPatch"), "Prefix")
                .Invoke(null, new object[] { ordinary });
            check(UsesNativeAutoOffer() && ordinaryGold.CurrentAmount == 20,
                "Ordinary barter keeps native auto-offer and reset behavior");

            var cashOnly = Open(player, null, null, out _);
            var cashVM = MakeMarriageBarterVM(cashOnly);
            BarterItemVM GoldRow(BarterVM vm) => vm.LeftOfferList.Concat(vm.RightOfferList)
                .Single(row => row.Barterable is GoldBarterable);
            GoldRow(cashVM).CurrentOfferedAmount = 20000;
            check(cashVM.IsOfferDisabled, "A reduced marriage payment disables confirmation");
            cashVM.ExecuteAutoBalance();
            check(GoldRow(cashVM).CurrentOfferedAmount == 85000 && !cashVM.IsOfferDisabled,
                "Auto Offer tops up a fixed gold row and updates the confirmation button");
            GoldRow(cashVM).CurrentOfferedAmount = 120000;
            cashVM.ExecuteReset();
            check(GoldRow(cashVM).CurrentOfferedAmount == 85000 && Valid(cashOnly),
                "Reset restores the opening dowry without removing the wedding");

            var tier = AccessTools.Field(typeof(Clan), "_tier");
            var instance = AccessTools.PropertySetter(typeof(DynasticHeirBehavior), "Instance");
            var priorDynastic = DynasticHeirBehavior.Instance;
            var recognition = new Harmony("bellum.tests.dowry.recognized_heir");
            try
            {
                tier.SetValue(player, 1); tier.SetValue(other, 6);
                DowryRanks[player] = FeudalTitleType.Barony; DowryRanks[other] = FeudalTitleType.Kingdom;
                check(Quote() == 183750, "Tier-one baron marrying a tier-six Crown heir pays both bounded prestige gaps before the heir premium");

                var crown = Crowns.Single(entry => entry.Value == bride);
                Crowns.Remove(crown.Key);
                try
                {
                    check(Quote() == 158750, "The same prestige gap with a clan heir pays the smaller succession premium");
                    _pricedDisplayHeir = bride; _pricedDisplayRealm = crown.Key;
                    instance.Invoke(null, new object[] { new DynasticHeirBehavior() });
                    recognition.Patch(AccessTools.Method(typeof(DynasticHeirBehavior), "GetReigningHeirRealmsForDisplay"),
                        prefix: new HarmonyMethod(typeof(MarriageHouseholdTests), nameof(DowryHeirRealms)));
                    recognition.Patch(AccessTools.Method(typeof(DynasticHeirBehavior), "TryGetCachedReigningDynasticHeir"),
                        prefix: new HarmonyMethod(typeof(MarriageHouseholdTests), nameof(DowryDisplayHeir)));
                    check(Quote() == 183750 && (bool)AccessTools.Method(policyType, "MustRemain")
                        .Invoke(Policy(), new object[] { bride }),
                        "The recognized Crown heiress gets the Crown price even outside the legal-line snapshot");
                    var highGap = Open(player, null, null, out _);
                    check(highGap.GetOfferedBarterables().OfType<GoldBarterable>().Single().CurrentAmount == 183750,
                        "Direct barter actually opens with the prestige-adjusted Crown price");
                    var pricedWedding = highGap.GetOfferedBarterables().OfType<MarriageBarterable>().Single();
                    int crownReads = _crownReads;
                    check(Enumerable.Range(0, 20).All(_ => pricedWedding.GetUnitValueForFaction(other) == -183750)
                        && _crownReads == crownReads, "Barter redraws use only the prepared Bellum quote");
                }
                finally { Crowns[crown.Key] = bride; }

                var reversePayment = Open(other, null, null, out _);
                check(reversePayment.GetOfferedBarterables().OfType<GoldBarterable>().Single().CurrentAmount == 25000,
                    "The higher-ranking NPC house gets no marrying-up surcharge when it pays the player for a lower-ranking match");
                var betrothal = AccessTools.Method(assembly.GetType("BellumCivile.Behaviors.BetrothalBehavior"), "BetrothalDowry");
                check((int)betrothal.Invoke(null, new object[] { groom, bride, player, Policy() }) == 183750
                    && (int)betrothal.Invoke(null, new object[] { bride, groom, player, Policy() }) == 183750,
                    "Betrothal pricing uses the same receiving house regardless of proposal order");
            }
            finally
            {
                recognition.UnpatchAll(recognition.Id); instance.Invoke(null, new object[] { priorDynastic });
                _pricedDisplayHeir = null; _pricedDisplayRealm = null;
                tier.SetValue(player, 4); tier.SetValue(other, 4);
                DowryRanks.Clear();
            }

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
            var mixedVM = MakeMarriageBarterVM(mixedPayment);
            inventory.AddToCounts(equipment, -1);
            mixedVM.ExecuteAutoBalance();
            check(!valuable.IsOffered && GoldRow(mixedVM).CurrentOfferedAmount == 5000,
                "Auto Offer leaves an unfundable draft intact instead of adding goods no longer in stock");
            inventory.AddToCounts(equipment, 1);
            mixedVM.ExecuteAutoBalance();
            check(valuable.IsOffered && !mixedVM.IsOfferDisabled && mixedVM.RightOfferList.Any(row => row.Barterable == valuable),
                "Auto Offer adds sufficient valuables and shows them in the native offer list");
            GoldRow(mixedVM).CurrentOfferedAmount = 4000;
            mixedVM.ExecuteReset();
            check(!valuable.IsOffered && mixedVM.IsOfferDisabled && GoldRow(mixedVM).CurrentOfferedAmount == 5000,
                "Reset clears pledged goods and restores the original affordable cash draft");
            mixedVM.ExecuteAutoBalance();
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
            var goodsOnlyVM = MakeMarriageBarterVM(goodsOnly);
            goodsOnlyVM.ExecuteAutoBalance();
            check(goodsOnlyItem.IsOffered && !goodsOnlyVM.IsOfferDisabled && Valid(goodsOnly),
                "Auto Offer can fund the marriage entirely with goods when there is no gold row");
            goodsOnlyItem.CurrentAmount = 2;
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
            var cappedVM = MakeMarriageBarterVM(capped);
            GoldRow(cappedVM).CurrentOfferedAmount = 1000;
            cappedVM.ExecuteAutoBalance();
            check(cappedGold.CurrentAmount == 1000 && Valid(capped), "Auto Offer leaves a valid negotiated NPC bid alone");
            cappedVM.ExecuteReset();
            check(cappedGold.CurrentAmount == 8500 && !cappedVM.IsOfferDisabled, "Reset restores the NPC's original capped bid");
            weddingWithCap.Apply();
            check(Spouses[groom] == bride && player.Leader.Gold == 8500 && other.Leader.Gold == 0,
                "The negotiated NPC payment settles at the offered amount, without creating gold");
            player.Leader.Gold = other.Leader.Gold = 500000;
            reset();
        }
        finally
        {
            model.SetValue(campaign.Models, oldModel);
            barterModel.SetValue(campaign.Models, oldBarterModel);
            manager.SetValue(campaign, oldManager);
        }
    }
}
