using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

internal static partial class LordshipTests
{
    private static Hero _main, _speaker;
    private static Kingdom _feudParent;
    private static readonly HashSet<Clan> Mercenaries = new HashSet<Clan>();
    private static readonly Dictionary<Hero, Settlement> Homes = new Dictionary<Hero, Settlement>();
    private static int _grantRoll, _grantRandomCalls, _grantRandomBound;
    private static bool GrantRoll(int __0, ref int __result)
    { _grantRandomCalls++; _grantRandomBound = __0; __result = _grantRoll; return false; }
    private static bool GrantIncome(Village __1, ref int __result) { __result = __1.TradeTaxAccumulated; return false; }
    private static bool MainHero(ref Hero __result) { __result = _main; return false; }
    private static bool Speaker(ref Hero __result) { __result = _speaker; return false; }
    private static bool Home(Hero __instance, ref Settlement __result) { Homes.TryGetValue(__instance, out __result); return false; }
    private static bool Notable(Hero __instance, ref bool __result) { __result = Homes.ContainsKey(__instance); return false; }
    private static bool Policies(ref IList<PolicyObject> __result) { __result = new List<PolicyObject>(); return false; }
    private static bool Mercenary(Clan __instance, ref bool __result) { __result = Mercenaries.Contains(__instance); return false; }
    private static bool FeudParent(string __0, ref Kingdom __result) { __result = __0 == _feudParent.StringId ? _feudParent : null; return false; }

    private static void CheckVassalGrants(Action<bool, string> check, Harmony harmony, LordshipBehavior registry,
        Clan source, Hero lord, Func<string, Clan> house, Func<Clan, string, Hero> person, Func<Clan, string, Settlement> villageOf)
    {
        void Patch(Type type, string name, string handler) => harmony.Patch(AccessTools.PropertyGetter(type, name),
            prefix: new HarmonyMethod(typeof(LordshipTests), handler));
        Patch(typeof(Hero), "OneToOneConversationHero", nameof(Speaker));
        Patch(typeof(Hero), "HomeSettlement", nameof(Home)); Patch(typeof(Kingdom), "ActivePolicies", nameof(Policies));
        Patch(typeof(Clan), "IsUnderMercenaryService", nameof(Mercenary));
        harmony.Unpatch(AccessTools.PropertyGetter(typeof(Hero), "IsNotable"), AccessTools.Method(typeof(LordshipTests), nameof(NotSpecial)));
        Patch(typeof(Hero), "IsNotable", nameof(Notable));
        void Realm(Clan clan, Kingdom realm) => AccessTools.Field(typeof(Clan), "_kingdom").SetValue(clan, realm);
        void Ruler(Kingdom realm, Clan clan) => AccessTools.Field(typeof(Kingdom), "_rulingClan").SetValue(realm, clan);
        var realmA = Blank<Kingdom>(); realmA.StringId = "vassal_test_realm";
        var rebels = Blank<Kingdom>(); rebels.StringId = "vassal_test_rebels_1";
        _main = person(_player, "vassal_player"); _speaker = lord;
        Governors.Clear(); Posts.Clear();
        AccessTools.Field(typeof(Clan), "_leader").SetValue(_player, _main);
        Realm(source, realmA); Ruler(realmA, source);
        var third = villageOf(source, "third");
        villageOf(source, "fourth");
        int tax = 1500;
        foreach (var village in Villages[source]) { village.TradeTaxAccumulated = tax; tax -= 400; }
        var native = Blank<DefaultClanFinanceModel>(); var model = new BellumFeudalClanFinanceModel(native);
        var incomeMethod = AccessTools.Method(typeof(DefaultClanFinanceModel), "CalculateVillageIncome");
        harmony.Unpatch(incomeMethod, AccessTools.Method(typeof(LordshipTests), nameof(Tax)));
        harmony.Patch(incomeMethod, prefix: new HarmonyMethod(typeof(LordshipTests), nameof(GrantIncome)));
        harmony.Patch(AccessTools.Method(typeof(MBRandom), "RandomInt", new[] { typeof(int) }),
            prefix: new HarmonyMethod(typeof(LordshipTests), nameof(GrantRoll)));
        _grantRoll = _grantRandomCalls = 0;
        var financeCode = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(BellumFeudalClanFinanceModel), "CalculateClanGoldChange")).ToList();
        int redirect = financeCode.FindIndex(i => i.operand is System.Reflection.MethodInfo m && m.Name == "Redirect");
        int payroll = financeCode.FindIndex(i => i.operand is System.Reflection.MethodInfo m && m.Name == "AddCouncilSalaryExpenses");
        check(redirect >= 0 && payroll > redirect, "The source removes endowed income before budgeting council wages");
        var expected = Villages[source].OrderBy(v => native.CalculateVillageIncome(source, v, false))
            .ThenBy(v => v.Settlement.StringId, StringComparer.Ordinal).Take(3).Select(v => v.Settlement).ToList();
        object Capture() => Call(typeof(LordshipBehavior), "CaptureVassalOath", registry);
        void Complete(object oath) => Call(typeof(LordshipBehavior), "CompleteVassalOath", registry, oath, native);
        var awarded = new HashSet<Settlement>();
        for (int roll = 0; roll < expected.Count; roll++)
        {
            _grantRoll = roll;
            var sample = Capture(); Realm(_player, realmA); Complete(sample);
            var sampled = registry.GetGrant(_main);
            check(sampled?.Village == expected[roll] && _grantRandomBound == 3,
                "Joining grant can select each of the three lowest-income vacant villages: " + roll);
            awarded.Add(sampled.Village);
            Call(typeof(LordshipBehavior), "ReturnVassalLordship", registry, _main, lord); Realm(_player, null);
        }
        check(awarded.Count == 3 && !awarded.Contains(Villages[source].OrderByDescending(v => v.TradeTaxAccumulated).First().Settlement),
            "Joining gifts vary without selecting the most valuable fourth candidate");
        _grantRoll = 0;
        int rewards = _rewards;
        int draws = _grantRandomCalls;
        var failedOath = Capture(); Complete(failedOath);
        check(registry.GetGrant(_main) == null && _rewards == rewards && _grantRandomCalls == draws,
            "An unsuccessful oath cannot grant land, relations or consume a random draw");
        var oath = Capture(); Realm(_player, realmA); _contested = false; Complete(oath);
        var grant = registry.GetGrant(_main);
        check(grant?.IsVassalGrant == true && expected.Contains(grant.Village) && grant.BeneficiaryClan == _player,
            "Successful oath endows one low-income spare village without changing the player's clan");
        var grantNotice = new TitleAcquiredMapNotification(null, grant, _main, 0);
        check(grantNotice.IsValid() && grantNotice.DescriptionText.ToString().Contains(LordshipBehavior.Name(grant.Village).ToString())
            && grantNotice.DescriptionText.ToString().Contains(_main.Name.ToString()),
            "The joining lordship notice names the actual village and player without implying ownership of its parent barony");
        var noticeStore = new MemoryStore { IsSaving = true }; registry.SyncData(noticeStore);
        noticeStore.IsSaving = false; var noticeLoad = new LordshipBehavior(); noticeLoad.SyncData(noticeStore);
        AccessTools.PropertySetter(typeof(LordshipBehavior), "Instance").Invoke(null, new object[] { noticeLoad });
        try
        {
            check(noticeLoad.GetGrant(_main) == null && grantNotice.IsValid(),
                "A saved lordship notice remains valid before the new session rebuilds its grant lookup indexes");
        }
        finally { AccessTools.PropertySetter(typeof(LordshipBehavior), "Instance").Invoke(null, new object[] { registry }); }
        check(grant.Parent.OwnerClan == source && !registry.CanGrant(source, third, _main, out _),
            "Cross-clan oath grants do not relax household grant ownership rules");
        Complete(oath);
        check(_rewards == rewards + 1 && Capture() == null && _grantRandomCalls == draws + 1,
            "Repeated oath callbacks and existing vassalage cannot reroll or award another gift");
        check(!(bool)Call(Type("HouseholdMembership"), "IsRetainer", null, _main), "Receiving a joining fief does not make the player a retainer");
        CheckLordshipVisuals(check, registry, grant);

        foreach (bool contested in new[] { false, true })
        {
            _contested = contested; _paid = 0;
            foreach (var village in Villages[source]) village.TradeTaxAccumulated = 1001;
            int gross = native.CalculateVillageIncome(source, grant.Village.Village, false);
            int net = (int)Call(Type("LordshipIncome"), "Net", null, gross, contested);
            var preview = model.CalculateClanGoldChange(_player, true, false);
            check(preview.ResultNumber == net && _paid == 0,
                "Player finance preview includes the adjusted lordship receipt without paying gold: " + contested);
            check(preview.GetLines().Any(line => line.Item1 == LordshipBehavior.Name(grant.Village).ToString() && line.Item2 == net),
                "The income tooltip identifies the village lordship and its adjusted amount: " + contested);
            check(model.CalculateClanGoldChange(_player, false, true).ResultNumber == 0,
                "An unfunded preview cannot mint income before the source collects taxes: " + contested);
            var sourceIncome = model.CalculateClanGoldChange(source, false, true);
            check(_paid == 0, "Cross-clan revenue is funded through finance, not a second direct hero payment: " + contested);
            var store = new MemoryStore { IsSaving = true }; registry.SyncData(store);
            store.IsSaving = false; var loaded = new LordshipBehavior(); loaded.SyncData(store);
            var credits = (List<LordshipIncomeCredit>)AccessTools.Field(typeof(LordshipBehavior), "_incomeCredits").GetValue(loaded);
            check(credits.Count == 1 && credits[0].Amount == net && credits[0].Recipient == _player,
                "Funded revenue remains payable after a data-store round trip: " + contested);
            var receipt = model.CalculateClanGoldChange(_player, false, true);
            check(receipt.ResultNumber == net && sourceIncome.ResultNumber + receipt.ResultNumber == net * Villages[source].Count,
                "Native tax collection and the player clan credit conserve adjusted income: " + contested);
            check(model.CalculateClanGoldChange(_player, false, true).ResultNumber == 0,
                "A funded receipt can only be collected once: " + contested);
        }

        _contested = false;
        var notable = person(source, "local_notable"); Homes[notable] = grant.Village; _speaker = notable;
        check((bool)Call(typeof(LordshipBehavior), "RecognizesPlayerLord", registry, notable),
            "Only a notable attached to the granted village recognizes its personal lord");
        var otherNotable = person(source, "other_notable"); Homes[otherNotable] = third == grant.Village ? Villages[source][0].Settlement : third;
        check(!(bool)Call(typeof(LordshipBehavior), "RecognizesPlayerLord", registry, otherNotable),
            "Holding one lordship does not establish authority over another village's notables");
        Call(typeof(LordshipBehavior), "MarkLordshipWelcome", registry);
        Call(typeof(LordshipBehavior), "MarkLordshipWelcome", registry);
        check(grant.WelcomedNotables.Count == 1 && grant.WelcomedNotables.Contains(notable),
            "A delivered welcome is recorded once per notable per grant");
        var greetingStore = new MemoryStore { IsSaving = true }; registry.SyncData(greetingStore);
        greetingStore.IsSaving = false; var greetingLoad = new LordshipBehavior(); greetingLoad.SyncData(greetingStore);
        var saved = (List<PersonalLordshipRecord>)AccessTools.Field(typeof(LordshipBehavior), "_grants").GetValue(greetingLoad);
        check(saved.Single().WelcomedNotables.Contains(notable), "Delivered village welcomes survive the data-store round trip");
        string original = "{=RVDojUOM}original";
        string own = (string)Call(Type("Patches.VassalLordshipPatches"), "VillageIntroductionFormat", null, original, grant.Village);
        check(own.StartsWith("{=M5iR1e5h}") && own.Contains("{PROSPERITY_INFO}"), "The menu reuses native own-fief wording and preserves prosperity variables");
        check((string)Call(Type("Patches.VassalLordshipPatches"), "VillageIntroductionFormat", null, original, otherNotable.HomeSettlement) == original,
            "An unrelated village keeps its original description");

        var otherHouse = house("new_ruling_house"); var successor = person(otherHouse, "new_ruler");
        AccessTools.Field(typeof(Clan), "_leader").SetValue(otherHouse, successor); Realm(otherHouse, realmA); Ruler(realmA, otherHouse);
        check(grant.IsValid && registry.GetGrant(_main) == grant, "A change of ruling house does not revoke an existing personal endowment");
        var privateRealm = Blank<Kingdom>(); privateRealm.StringId = "bc_feud_vassal_test";
        var feuds = new ClaimFeudWarBehavior(); var privateWar = Blank<ClaimFeudWarRecord>();
        AccessTools.Field(typeof(ClaimFeudWarRecord), "_isActive").SetValue(privateWar, true);
        AccessTools.Field(typeof(ClaimFeudWarRecord), "_parentKingdomId").SetValue(privateWar, realmA.StringId);
        AccessTools.Field(typeof(ClaimFeudWarRecord), "_claimantKingdomId").SetValue(privateWar, privateRealm.StringId);
        ((List<ClaimFeudWarRecord>)AccessTools.Field(typeof(ClaimFeudWarBehavior), "_wars").GetValue(feuds)).Add(privateWar);
        _feudParent = realmA;
        harmony.Patch(AccessTools.Method(typeof(ClaimFeudWarBehavior), "ResolveKingdom"), prefix: new HarmonyMethod(typeof(LordshipTests), nameof(FeudParent)));
        Realm(_player, privateRealm);
        check(Call(typeof(LordshipBehavior), "LordshipRealm", null, _player, feuds) == realmA,
            "An actual private-feud shell resolves to its parent rather than forfeiting a grant");
        Realm(_player, rebels);
        check(Call(typeof(LordshipBehavior), "LordshipRealm", null, _player, feuds) == rebels,
            "A crown rebellion remains a distinct allegiance and is not normalized back to the crown");
        Realm(_player, realmA);
        check(!(bool)Call(typeof(LordshipBehavior), "ReturnVassalLordship", registry, _main, successor), "An unrelated new monarch cannot accept another house's returned grant");
        Realm(_player, rebels);
        Call(typeof(LordshipBehavior), "Validate", registry);
        check(!grant.IsValid && registry.GetGrant(_main) == grant, "A partially completed group transfer reserves the grant until final realm validation");
        Realm(source, rebels); Call(typeof(LordshipBehavior), "ValidateVassalRealms", registry);
        check(grant.IsValid && registry.GetGrant(_main) == grant, "Both houses joining the same rebellion preserve their grant");
        Realm(_player, realmA); Call(typeof(LordshipBehavior), "ValidateVassalRealms", registry);
        check(registry.GetGrant(_main) == null, "Diverging into different realms permanently vacates the grant");
        Realm(source, realmA); Ruler(realmA, source); _speaker = lord;
        check(registry.GetGrant(_main) == null, "Returning to the same realm does not resurrect an expired grant");

        Realm(_player, null); var secondOath = Capture(); Realm(_player, realmA); Complete(secondOath);
        grant = registry.GetGrant(_main);
        check(grant != null && grant.WelcomedNotables.Count == 0, "A genuine later oath may receive a new spare grant and fresh welcome state");
        rewards = _rewards;
        Call(typeof(LordshipBehavior), "FundVassalIncome", registry, grant, 17);
        check((bool)Call(typeof(LordshipBehavior), "ReturnVassalLordship", registry, _main, lord) && registry.GetGrant(_main) == null && _rewards == rewards + 1,
            "Voluntary surrender vacates the grant and applies one personal relation penalty");
        check(!grantNotice.IsValid(), "A former lordship notice cannot survive surrender or refer to a replacement grant");
        check(!(bool)Call(typeof(LordshipBehavior), "ReturnVassalLordship", registry, _main, lord) && _rewards == rewards + 1,
            "A repeated surrender cannot duplicate the penalty");
        check(model.CalculateClanGoldChange(_player, false, true).ResultNumber == 17,
            "Returning a fief does not erase revenue already collected while its grant was valid");
        Mercenaries.Add(_player);
        var promotion = Capture();
        check(promotion != null && registry.GetGrant(_main) == null, "A mercenary may proceed to the same realm's vassal oath without a premature grant");
        Mercenaries.Remove(_player); Complete(promotion);
        check(registry.GetGrant(_main) != null, "Promotion from mercenary service receives the same joining package after becoming a vassal");
        AccessTools.Field(typeof(Hero), "_heroState").SetValue(_main, Hero.CharacterStates.Dead);
        Call(typeof(LordshipBehavior), "Validate", registry);
        check(registry.GetGrant(_main) == null, "The recipient's death ends the personal joining grant without inheritance");
        AccessTools.Field(typeof(Hero), "_heroState").SetValue(_main, Hero.CharacterStates.Active);
        foreach (var v in Villages[source])
            check(registry.TryGrant(source, v.Settlement, person(source, "retainer_" + v.Settlement.StringId), out _),
                "Existing household endowments occupy the available villages: " + v.Settlement.StringId);
        Realm(_player, null); var noLand = Capture(); Realm(_player, realmA); rewards = _rewards; Complete(noLand);
        check(registry.GetGrant(_main) == null && _rewards == rewards,
            "With no spare lordship, vassalage proceeds without displacing holders or awarding gift goodwill");
        var single = Villages[source][0].Settlement;
        check(registry.TryRevoke(source, single, out _), "One household estate can become vacant for the single-choice test");
        draws = _grantRandomCalls;
        Realm(_player, null); var singleOath = Capture(); Realm(_player, realmA); Complete(singleOath);
        check(registry.GetGrant(_main)?.Village == single && _grantRandomCalls == draws,
            "A single eligible vacancy is granted without a random draw or displacing another holder");
        Call(typeof(LordshipBehavior), "ReturnVassalLordship", registry, _main, lord);
        var next = Villages[source][1].Settlement;
        check(registry.TryRevoke(source, next, out _), "A second estate can enter the available gift pool");
        _grantRoll = 1;
        Realm(_player, null); var pairOath = Capture(); Realm(_player, realmA); Complete(pairOath);
        check(registry.GetGrant(_main)?.Village == next && _grantRandomBound == 2,
            "Two eligible vacancies both participate in the joining lottery");
        Call(typeof(LordshipBehavior), "ReturnVassalLordship", registry, _main, lord);
        single.Village.TradeTaxAccumulated = 0; next.Village.TradeTaxAccumulated = -10;
        draws = _grantRandomCalls; rewards = _rewards;
        Realm(_player, null); var noIncome = Capture(); Realm(_player, realmA); Complete(noIncome);
        check(registry.GetGrant(_main) == null && _grantRandomCalls == draws && _rewards == rewards,
            "Nonpositive-income vacancies never generate a gift, random draw or relation reward");
    }
}
