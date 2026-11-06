using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class HapLordshipIncomeTests
{
    private sealed class Manager : ICampaignBehaviorManager
    {
        public T GetBehavior<T>() => default;
        public IEnumerable<T> GetBehaviors<T>() => Enumerable.Empty<T>();
        public void AddBehavior(CampaignBehaviorBase b) { }
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase { }
        public void ClearBehaviors() { }
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> b) { }
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }

    private static readonly Dictionary<Clan, MBReadOnlyList<Town>> Fiefs = new Dictionary<Clan, MBReadOnlyList<Town>>();
    private static readonly Dictionary<Clan, MBReadOnlyList<Village>> Villages = new Dictionary<Clan, MBReadOnlyList<Village>>();
    private static readonly Dictionary<Town, MBReadOnlyList<Village>> BoundVillages = new Dictionary<Town, MBReadOnlyList<Village>>();
    private static readonly Dictionary<SettlementComponent, Settlement> Homes = new Dictionary<SettlementComponent, Settlement>();
    private static readonly Dictionary<Settlement, Clan> Owners = new Dictionary<Settlement, Clan>();
    private static readonly Dictionary<Village, Settlement> Bounds = new Dictionary<Village, Settlement>();
    private static readonly Dictionary<Hero, Clan> Houses = new Dictionary<Hero, Clan>();
    private static readonly Dictionary<Town, int> TownTaxes = new Dictionary<Town, int>();
    private static readonly Dictionary<Town, int> Tariffs = new Dictionary<Town, int>();
    private static readonly Dictionary<Village, int> Pools = new Dictionary<Village, int>();
    private static GameModels _models;
    private static DefaultSettlementTaxModel _tax;
    private static BellumFeudalClanFinanceModel _finance;
    private static Clan _source, _recipient;
    private static Kingdom _realm;
    private static Game _game;
    private static GameTextManager _texts;
    private static bool _contested, _failTown;
    private static Village _failVillage;
    private static float _rate;
    private static int _paid, _withdrawals, _errors, _collections;
    private static MethodInfo _settlementIncome;
    private static Town _workshopTown;
    private static Workshop _workshop;
    private static WorkshopType _workshopType;
    private static readonly Type Integration = typeof(LordshipBehavior).Assembly.GetType("BellumCivile.LordshipHapIncome", true);
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Call(Type type, string name, object target, params object[] args) => AccessTools.Method(type, name).Invoke(target, args);
    private static void Set(object target, string name, object value) => AccessTools.Field(target.GetType(), name).SetValue(target, value);

    private static bool ClanFiefs(Clan __instance, ref MBReadOnlyList<Town> __result) { __result = Fiefs[__instance]; return false; }
    private static bool ClanVillages(Clan __instance, ref MBReadOnlyList<Village> __result) { __result = Villages[__instance]; return false; }
    private static bool TownVillages(Town __instance, ref MBReadOnlyList<Village> __result) { __result = BoundVillages[__instance]; return false; }
    private static bool Component(SettlementComponent __instance, ref Settlement __result) { Homes.TryGetValue(__instance, out __result); return false; }
    private static bool Bound(Village __instance, ref Settlement __result) { Bounds.TryGetValue(__instance, out __result); return false; }
    private static bool Owner(Settlement __instance, ref Clan __result) { Owners.TryGetValue(__instance, out __result); return false; }
    private static bool Name(SettlementComponent __instance, ref TextObject __result) { __result = new TextObject("{=!}" + Homes[__instance].StringId); return false; }
    private static bool SettlementName(Settlement __instance, ref TextObject __result) { __result = new TextObject("{=!}" + __instance.StringId); return false; }
    private static bool HeroName(Hero __instance, ref TextObject __result) { __result = new TextObject("{=!}" + __instance.StringId); return false; }
    private static bool HeroClan(Hero __instance, ref Clan __result) { __result = Houses[__instance]; return false; }
    private static bool Player(ref Clan __result) { __result = _recipient; return false; }
    private static bool Models(ref GameModels __result) { __result = _models; return false; }
    private static bool Finance(ref TaleWorlds.CampaignSystem.ComponentInterfaces.ClanFinanceModel __result) { __result = _finance; return false; }
    private static bool TaxModel(ref TaleWorlds.CampaignSystem.ComponentInterfaces.SettlementTaxModel __result) { __result = _tax; return false; }
    private static bool GameCurrent(ref Game __result) { __result = _game; return false; }
    private static bool Texts(ref GameTextManager __result) { __result = _texts; return false; }
    private static bool FindText(string id, ref TextObject __result) { __result = new TextObject("{=!}" + id); return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(10); return false; }
    private static bool Policies(ref IList<PolicyObject> __result) { __result = new List<PolicyObject>(); return false; }
    private static bool TownTax(Town town, ref ExplainedNumber __result)
    { if (_failTown) throw new InvalidOperationException("test HAP failure before collection"); __result = new ExplainedNumber(TownTaxes[town]); return false; }
    private static bool Tariff(Town town, ref ExplainedNumber __result) { __result = new ExplainedNumber(Tariffs[town]); return false; }
    private static bool Project(ref int __result) { __result = 17; return false; }
    private static bool TaxRate(ref float __result) { __result = _rate; return false; }
    private static bool WorkshopTax(ref int __result) { __result = 50; return false; }
    private static bool VillageIncome(Village village, bool applyWithdrawals, ref int __result)
    {
        if (_failVillage == village && AccessTools.Field(Integration, "_current").GetValue(null) != null)
            throw new InvalidOperationException("test HAP failure after partial collection");
        __result = village.TradeTaxAccumulated;
        if (applyWithdrawals) { village.TradeTaxAccumulated -= __result; _withdrawals++; }
        return false;
    }
    private static bool ClanIncome(Clan clan, bool applyWithdrawals, bool includeDescriptions, bool includeDetails, ref ExplainedNumber __result)
    {
        __result = new ExplainedNumber(0f, includeDescriptions);
        var args = new object[] { clan, __result, applyWithdrawals, includeDetails };
        _settlementIncome.Invoke(Blank<DefaultClanFinanceModel>(), args);
        __result = (ExplainedNumber)args[1];
        return false;
    }
    private static bool Contested(Village village, ref bool __result, ref FeudalTitleRecord title) { __result = _contested; title = null; return false; }
    private static bool TownContested(ref bool __result, ref FeudalTitleRecord title) { __result = false; title = null; return false; }
    private static bool Gold(int __2) { _paid += __2; return false; }
    private static bool Log(string message) { if (message.Contains("AddSettlementIncome_Prefix ERROR")) _errors++; return false; }
    private static void CountCollection() => _collections++;
    private static void UncaughtFailure(Clan clan) { if (clan == _source) throw new InvalidOperationException("foreign HAP postfix failed"); }

    internal static void Run(Action<bool, string> check, string hapDll)
    {
        if (string.IsNullOrWhiteSpace(hapDll) || !File.Exists(hapDll)) throw new ArgumentException("Pass --hap-dll=<path to the supplied HAP DLL>.");
        var h = new Harmony("bellum.tests.hap-lordship-income");
        var hapHarmony = new Harmony("HarvestAndProduction");
        var oldCampaign = Campaign.Current;
        var oldRegistry = LordshipBehavior.Instance;
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay"); var oldTicks = ticks.GetValue(null);
        void Patch(Type type, string name, string handler, bool getter = true) => h.Patch(
            getter ? AccessTools.PropertyGetter(type, name) : AccessTools.Method(type, name), prefix: new HarmonyMethod(typeof(HapLordshipIncomeTests), handler));
        Clan ClanOf(string id)
        {
            var clan = Blank<Clan>(); clan.StringId = id;
            Fiefs[clan] = new MBReadOnlyList<Town>(); Villages[clan] = new MBReadOnlyList<Village>();
            var hero = Blank<Hero>(); hero.StringId = id + "_leader"; Set(hero, "_heroState", Hero.CharacterStates.Active);
            Houses[hero] = clan; Set(clan, "_leader", hero); Set(clan, "_kingdom", _realm); return clan;
        }
        Village Estate(string id, Town town)
        {
            var settlement = Blank<Settlement>(); settlement.StringId = id;
            var village = Blank<Village>(); Set(settlement, "Village", village);
            Homes[village] = settlement; Bounds[village] = Homes[town];
            Villages[_source].Add(village); BoundVillages[town].Add(village); Pools[village] = 100;
            return village;
        }
        void Reset()
        {
            _paid = _withdrawals = _errors = _collections = 0; _failTown = false; _failVillage = null;
            foreach (var pair in Pools) pair.Key.TradeTaxAccumulated = pair.Value;
        }
        try
        {
            check(Call(Integration, "Preview", null, Blank<Clan>()) == null, "Without HAP, the optional income adapter is inert");
            check(!typeof(LordshipBehavior).Assembly.GetReferencedAssemblies().Any(a => a.Name == "HarvestAndProduction"),
                "Lordship finance has no hard HAP assembly dependency");
            var assembly = Assembly.LoadFrom(hapDll);
            var patch = assembly.GetType("HarvestAndProduction.Patches.ClanFinanceModelPatch", true);
            var hap = AccessTools.Method(patch, "AddSettlementIncome_Prefix");
            _settlementIncome = AccessTools.Method(typeof(DefaultClanFinanceModel), "AddSettlementIncome");
            _realm = Blank<Kingdom>(); _realm.StringId = "hap_realm";
            _source = ClanOf("hap_source"); _recipient = ClanOf("hap_recipient"); Set(_realm, "_rulingClan", _source);
            _game = Blank<Game>(); _texts = Blank<GameTextManager>();
            _models = Blank<GameModels>(); _tax = Blank<DefaultSettlementTaxModel>();
            _finance = new BellumFeudalClanFinanceModel(Blank<DefaultClanFinanceModel>());
            var campaign = Blank<Campaign>(); AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            ticks.SetValue(null, 24000L); Patch(typeof(CampaignTime), "Now", nameof(Now));
            campaign.AddCampaignBehaviorManager(new Manager());
            var registry = new LordshipBehavior(); AccessTools.PropertySetter(typeof(LordshipBehavior), "Instance").Invoke(null, new object[] { registry });
            Set(registry, "_campaign", campaign); Set(registry, "_ready", true);
            Patch(typeof(Clan), "Fiefs", nameof(ClanFiefs)); Patch(typeof(Clan), "Villages", nameof(ClanVillages));
            Patch(typeof(Town), "Villages", nameof(TownVillages)); Patch(typeof(SettlementComponent), "Settlement", nameof(Component));
            Patch(typeof(Village), "Bound", nameof(Bound)); Patch(typeof(Settlement), "OwnerClan", nameof(Owner));
            Patch(typeof(SettlementComponent), "Name", nameof(Name)); Patch(typeof(Hero), "Name", nameof(HeroName));
            Patch(typeof(Settlement), "Name", nameof(SettlementName));
            Patch(typeof(Hero), "Clan", nameof(HeroClan)); Patch(typeof(Clan), "PlayerClan", nameof(Player));
            Patch(typeof(SettlementComponent), "IsTown", nameof(No)); Patch(typeof(Campaign), "Models", nameof(Models));
            Patch(typeof(GameModels), "ClanFinanceModel", nameof(Finance)); Patch(typeof(GameModels), "SettlementTaxModel", nameof(TaxModel));
            Patch(typeof(Game), "Current", nameof(GameCurrent)); Patch(typeof(Game), "GameTextManager", nameof(Texts));
            Patch(typeof(GameTextManager), "FindText", nameof(FindText), false); Patch(typeof(Kingdom), "ActivePolicies", nameof(Policies));
            Patch(typeof(DefaultSettlementTaxModel), "CalculateTownTax", nameof(TownTax), false);
            Patch(typeof(DefaultClanFinanceModel), "CalculateTownIncomeFromTariffs", nameof(Tariff), false);
            Patch(typeof(DefaultClanFinanceModel), "CalculateTownIncomeFromProjects", nameof(Project), false);
            Patch(typeof(DefaultClanFinanceModel), "CalculateVillageIncome", nameof(VillageIncome), false);
            Patch(typeof(DefaultClanFinanceModel), "CalculateClanIncome", nameof(ClanIncome), false);
            Patch(typeof(DefaultClanFinanceModel), "CalculateClanGoldChange", nameof(ClanIncome), false);
            Patch(typeof(GiveGoldAction), "ApplyBetweenCharacters", nameof(Gold), false);
            var penalty = typeof(LordshipBehavior).Assembly.GetType("BellumCivile.FeudalTitlePenaltyHelper", true);
            Patch(penalty, "IsBoundToContestedLegalTitle", nameof(Contested), false);
            h.Patch(AccessTools.Method(penalty, "IsContestedLegalTitle", new[] { typeof(Town), typeof(FeudalTitleRecord).MakeByRefType() }),
                prefix: new HarmonyMethod(typeof(HapLordshipIncomeTests), nameof(TownContested)));
            Patch(assembly.GetType("HarvestAndProduction.Main", true), "Log", nameof(Log), false);
            var observer = typeof(LordshipBehavior).Assembly.GetType("BellumCivile.Patches.LordshipVillageIncomePatch", true);
            h.Patch(AccessTools.Method(typeof(DefaultClanFinanceModel), "CalculateVillageIncome"),
                postfix: new HarmonyMethod(observer, "Postfix"));

            var villages = new List<Village>();
            for (int i = 0; i < 5; i++)
            {
                var settlement = Blank<Settlement>(); settlement.StringId = "hap_castle_" + i; Owners[settlement] = _source;
                var town = Blank<Town>(); Set(town, "_isCastle", true); Set(settlement, "Town", town); Homes[town] = settlement;
                Fiefs[_source].Add(town); BoundVillages[town] = new MBReadOnlyList<Village>(); TownTaxes[town] = 0; Tariffs[town] = 0;
                villages.Add(Estate("hap_village_" + i + "a", town)); villages.Add(Estate("hap_village_" + i + "b", town));
            }
            float actualRate = (float)Call(patch, "CalculateTaxInefficiencyRate", null, _source);
            check(Math.Abs(actualRate - 0.04f) < 0.00001f,
                $"The supplied HAP DLL applies its actual five-castle inefficiency rule (rate={actualRate}, towns={Fiefs[_source].Count(t => t.IsTown)})");
            Patch(patch, "CalculateTaxInefficiencyRate", nameof(TaxRate), false);
            Call(Integration, "Initialize", null);
            check(Call(Integration, "Preview", null, _source) == null, "Loading HAP without its active finance patch does not activate compatibility");
            hapHarmony.Patch(_settlementIncome, prefix: new HarmonyMethod(hap));
            Call(Integration, "Initialize", null); Call(Integration, "Initialize", null);
            check(Harmony.GetPatchInfo(hap).Transpilers.Count(p => p.owner == "com.bellumcivile.lordship.hap") == 1,
                "The supplied HAP finance method accepts the validated capture patch exactly once");
            var altered = PatchProcessor.GetOriginalInstructions(hap).Select(c => new CodeInstruction(c)).ToList();
            altered.RemoveAll(c => c.opcode == System.Reflection.Emit.OpCodes.Ldsfld && c.operand is FieldInfo f && f.Name == "_settlementIncomeText");
            bool rejected = false;
            try { Call(Integration, "Capture", null, altered); }
            catch (TargetInvocationException ex) { rejected = ex.InnerException is InvalidOperationException; }
            check(rejected, "An unrecognized HAP commit layout is rejected before installing a partial capture patch");
            altered = PatchProcessor.GetOriginalInstructions(hap).Select(c => new CodeInstruction(c)).ToList();
            foreach (var instruction in altered.Where(c => c.opcode == System.Reflection.Emit.OpCodes.Mul))
                instruction.opcode = System.Reflection.Emit.OpCodes.Add;
            rejected = false;
            try { Call(Integration, "Capture", null, altered); }
            catch (TargetInvocationException ex) { rejected = ex.InnerException is InvalidOperationException; }
            check(rejected, "An altered HAP tax formula cannot silently reuse the known version's net-income allocation");
            h.Patch(hap, prefix: new HarmonyMethod(typeof(HapLordshipIncomeTests), nameof(CountCollection)));
            var grants = (List<PersonalLordshipRecord>)AccessTools.Field(typeof(LordshipBehavior), "_grants").GetValue(registry);
            var credits = (List<LordshipIncomeCredit>)AccessTools.Field(typeof(LordshipBehavior), "_incomeCredits").GetValue(registry);
            var retainer = Blank<Hero>(); retainer.StringId = "hap_retainer"; Set(retainer, "_heroState", Hero.CharacterStates.Active); Houses[retainer] = _source;
            var grant = new PersonalLordshipRecord { Village = Homes[villages[0]], Parent = Bounds[villages[0]],
                GrantingClan = _source, Holder = retainer };
            grants.Add(grant); Call(typeof(LordshipBehavior), "Reindex", registry);
            check(grant.IsValid, "The finance fixture uses a valid personal lordship without changing village ownership");

            foreach (float rate in new[] { 0f, 0.04f, 0.2f, 0.5f })
            foreach (bool contested in new[] { false, true })
            foreach (bool descriptions in new[] { false, true })
            foreach (bool details in new[] { false, true })
            {
                _rate = rate; _contested = contested; Reset();
                var preview = _finance.CalculateClanIncome(_source, descriptions, false, details);
                check(_paid == 0 && _withdrawals == 0 && _errors == 0 && villages.All(v => v.TradeTaxAccumulated == Pools[v]),
                    $"HAP forecasts never withdraw or pay: rate={rate}, contested={contested}, descriptions={descriptions}, details={details}");
                int expected = 100 + (contested ? (int)Math.Round(-50d) : 0) - (int)(100 * rate);
                var settled = _finance.CalculateClanIncome(_source, descriptions, true, details);
                check(_errors == 0 && _paid == expected && _withdrawals == villages.Count && villages.All(v => v.TradeTaxAccumulated == 0),
                    "Real HAP collection pays only the net grant, with one withdrawal per village");
                int all = Fiefs[_source].Count * (200 - (int)(200 * rate) + 17) - (contested ? 50 * villages.Count : 0);
                check(settled.ResultNumber == preview.ResultNumber && settled.ResultNumber + _paid == all,
                    "Both HAP explanation branches conserve existing income and agree with the forecast");
            }

            _contested = false;
            foreach (int gross in new[] { 0, 1, 3, 25, 101, 1001 })
            foreach (float rate in new[] { 0f, 0.04f, 0.2f, 0.5f })
            {
                _rate = rate; Pools[villages[0]] = gross; Pools[villages[1]] = gross + 1;
                TownTaxes[Fiefs[_source][0]] = 137; Tariffs[Fiefs[_source][0]] = 23; Reset();
                int tax = (int)((160 + gross) * rate) - (int)(160 * rate);
                _finance.CalculateClanGoldChange(_source, false, true);
                check(_paid == Math.Max(0, gross - tax) && _withdrawals == villages.Count && _errors == 0,
                    $"Village shares preserve HAP per-fief rounding alongside taxes and tariffs: gross={gross}, rate={rate}");
            }
            foreach (var v in villages) Pools[v] = 100;
            TownTaxes[Fiefs[_source][0]] = Tariffs[Fiefs[_source][0]] = 0;

            _workshopTown = Fiefs[_source][0]; _workshop = Blank<Workshop>(); _workshopType = Blank<WorkshopType>();
            Set(_workshopTown, "_isCastle", false); TownTaxes[_workshopTown] = 137; Tariffs[_workshopTown] = 23;
            AccessTools.PropertySetter(typeof(Town), "Workshops").Invoke(_workshopTown, new object[] { new[] { _workshop } });
            AccessTools.PropertySetter(typeof(Workshop), "WorkshopType").Invoke(_workshop, new object[] { _workshopType });
            AccessTools.PropertySetter(typeof(Workshop), "Capital").Invoke(_workshop, new object[] { 20000 });
            Patch(assembly.GetType("HarvestAndProduction.Patches.WorkshopTaxPatch", true), "GetWorkshopTaxAmount", nameof(WorkshopTax), false);
            _rate = 0.2f; _contested = false; Reset();
            var workshopPreview = _finance.CalculateClanIncome(_source, true, false, true);
            check(_workshop.Capital == 20000 && _withdrawals == 0 && _paid == 0, "Town/workshop income previews remain side-effect-free");
            var workshopIncome = _finance.CalculateClanIncome(_source, true, true, true);
            check(_workshop.Capital == 19950 && _paid == 80 && workshopIncome.ResultNumber == 973 && workshopPreview.ResultNumber == workshopIncome.ResultNumber,
                $"Real HAP workshop taxes join the taxable base without being redirected, forgiven or withdrawn twice (workshop={_workshop.Capital}, paid={_paid}, income={workshopIncome.ResultNumber}, preview={workshopPreview.ResultNumber}, errors={_errors})");
            Set(_workshopTown, "_isCastle", true); TownTaxes[_workshopTown] = Tariffs[_workshopTown] = 0;

            foreach (var v in villages.Skip(1))
            {
                var holder = Blank<Hero>(); holder.StringId = v.Settlement.StringId + "_retainer";
                Set(holder, "_heroState", Hero.CharacterStates.Active); Houses[holder] = _source;
                grants.Add(new PersonalLordshipRecord { Village = v.Settlement, Parent = v.Bound, GrantingClan = _source, Holder = holder });
            }
            Call(typeof(LordshipBehavior), "Reindex", registry);
            foreach (bool contested in new[] { false, true })
            foreach (float rate in new[] { 0.04f, 0.5f })
            {
                _contested = contested; _rate = rate;
                foreach (var v in villages) Pools[v] = 101;
                Reset();
                var result = _finance.CalculateClanIncome(_source, true, true, true);
                int expected = Fiefs[_source].Count * (202 - (int)(202 * rate) - (contested ? 100 : 0) + 17);
                check(result.ResultNumber == 17 * Fiefs[_source].Count && result.ResultNumber + _paid == expected && _withdrawals == villages.Count,
                    $"Multiple simultaneous grants exactly share HAP's rounded village deductions: rate={rate}, contested={contested}");
            }
            grants.RemoveRange(1, grants.Count - 1); Call(typeof(LordshipBehavior), "Reindex", registry);
            foreach (var v in villages) Pools[v] = 100;

            grant.IsVassalGrant = true; grant.Holder = _recipient.Leader; grant.BeneficiaryClan = _recipient;
            Call(typeof(LordshipBehavior), "Reindex", registry); _rate = 0.2f; _contested = false; Reset();
            var incoming = _finance.CalculateClanIncome(_recipient, true, false, true);
            check(incoming.ResultNumber == 80 && credits.Count == 0 && _withdrawals == 0 && _paid == 0,
                "The beneficiary preview uses the same HAP net share without funding it");
            check(_collections == 2, "A beneficiary preview runs one own settlement calculation and one source snapshot");
            check(_finance.CalculateClanIncome(_recipient, false, true).ResultNumber == 0,
                "Forecasts cannot create an unfunded cross-clan payout");
            _finance.CalculateClanIncome(_source, false, true);
            check(_paid == 0 && credits.Count == 1 && credits[0].Amount == 80,
                "Successful source collection funds only the adjusted cross-clan receipt");
            check(_finance.CalculateClanIncome(_recipient, false, true).ResultNumber == 80 && credits.Count == 0,
                "The adjusted receipt is paid through clan finance exactly once");
            check(_finance.CalculateClanIncome(_recipient, false, true).ResultNumber == 0,
                "Replaying the beneficiary calculation cannot duplicate its funded receipt");
            h.Patch(hap, postfix: new HarmonyMethod(typeof(HapLordshipIncomeTests), nameof(UncaughtFailure)));
            check(_finance.CalculateClanIncome(_recipient, false, false).ResultNumber == 0,
                "A foreign exception outside HAP's own catch cannot crash the new beneficiary preview");
            h.Unpatch(hap, AccessTools.Method(typeof(HapLordshipIncomeTests), nameof(UncaughtFailure)));

            foreach (bool crossClan in new[] { false, true })
            foreach (bool partial in new[] { false, true })
            {
                grant.IsVassalGrant = crossClan; grant.Holder = crossClan ? _recipient.Leader : retainer;
                Call(typeof(LordshipBehavior), "Reindex", registry); Reset();
                if (partial) _failVillage = villages[1]; else _failTown = true;
                var result = _finance.CalculateClanIncome(_source, true, true, true);
                check(_errors == 1 && _paid == 0 && credits.Count == 0 && result.ResultNumber == 0,
                    $"HAP caught failure cannot pay cached revenue: cross-clan={crossClan}, partial={partial}");
                check(_withdrawals == (partial ? 1 : 0), "The safeguard never retries or repeats HAP's partially completed withdrawals");
                check(_finance.CalculateClanIncome(_recipient, false, false).ResultNumber == 0,
                    "A failed HAP collection is not advertised as a payable receipt");
            }
            Reset(); grant.IsVassalGrant = false; grant.Holder = retainer;
            Call(typeof(LordshipBehavior), "Reindex", registry); _contested = true;
            _finance.CalculateClanIncome(_source, false, true);
            check(_paid == 30 && _errors == 0, "After failure, a later successful pass pays the correct HAP-plus-contested net income");
            check(Owners[grant.Parent] == _source && Houses[grant.Holder] == _source,
                "HAP compatibility never rewrites parent ownership or household membership");
            grant.IsVassalGrant = true; grant.Holder = _recipient.Leader;
            Call(typeof(LordshipBehavior), "Reindex", registry); _contested = false; Reset();
            _finance.CalculateClanIncome(_source, false, true);
            Reset(); _failTown = true; _finance.CalculateClanIncome(_source, false, true);
            check(credits.Count == 1 && credits[0].Amount == 80 && _finance.CalculateClanIncome(_recipient, false, true).ResultNumber == 80,
                "A later failed collection neither duplicates nor erases an already funded receipt");
            Reset(); grant.IsVassalGrant = false; grant.Holder = retainer; Call(typeof(LordshipBehavior), "Reindex", registry);
            h.Unpatch(AccessTools.Method(typeof(DefaultClanFinanceModel), "CalculateVillageIncome"),
                AccessTools.Method(typeof(HapLordshipIncomeTests), nameof(VillageIncome)));
            foreach (var v in villages) Pools[v] = 1001;
            Reset();
            var native = Blank<DefaultClanFinanceModel>();
            int raw = native.CalculateVillageIncome(_source, villages[0], false);
            _finance.CalculateClanIncome(_source, false, true);
            check(raw > 0 && _paid == raw - (int)(raw * _rate) && villages.All(v => v.TradeTaxAccumulated == 1001 - raw),
                "Actual native village withdrawals, smoothing and HAP collection pay the same net share without a second withdrawal");
            hapHarmony.UnpatchAll(hapHarmony.Id);
            check(Call(Integration, "Preview", null, _source) == null,
                "If HAP's finance patch is removed, its preview adapter stops applying deductions");
            Reset(); _finance.CalculateClanIncome(_source, false, true);
            check(_paid == raw, "Without the HAP finance patch, existing native lordship payments remain unchanged");
            check(AccessTools.Field(Integration, "_current").GetValue(null) == null && AccessTools.Field(Integration, "_preview").GetValue(null) == null,
                "Successful, failed and nested preview calls leave no retained campaign collection context");
        }
        finally
        {
            h.UnpatchAll(h.Id); hapHarmony.UnpatchAll(hapHarmony.Id);
            new Harmony("com.bellumcivile.lordship.hap").UnpatchAll("com.bellumcivile.lordship.hap");
            AccessTools.Field(Integration, "_collect").SetValue(null, null); AccessTools.Field(Integration, "_rate").SetValue(null, null);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { oldCampaign });
            AccessTools.PropertySetter(typeof(LordshipBehavior), "Instance").Invoke(null, new object[] { oldRegistry });
            ticks.SetValue(null, oldTicks);
            Fiefs.Clear(); Villages.Clear(); BoundVillages.Clear(); Homes.Clear(); Owners.Clear(); Bounds.Clear(); Houses.Clear();
            TownTaxes.Clear(); Tariffs.Clear(); Pools.Clear();
        }
    }
}
