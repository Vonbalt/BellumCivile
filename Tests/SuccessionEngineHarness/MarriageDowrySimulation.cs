using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile.Behaviors;
using BellumCivile.Patches;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

// Installed native valuation with synthetic inputs, historical baseline and independent formula oracle.
internal static class MarriageDowrySimulation
{
    private sealed class Scenario
    {
        internal string Name;
        internal int PlayerTier = 4, NpcTier = 4, PersonalRelation, ClanRelation;
        internal int PlayerSeed = 25000, NpcSeed = 25000;
        internal float PlayerAge = 28, NpcAge = 28, Risk, CommandStrength = 600;
        internal bool Incoming, Royal, CrownHeir, ClanHeir, PersonalCourtship, Domestic, PlayerFemale;
        internal bool ContinuationBenefit = true, OutgoingViable = true, LastContinuation, CurrentLeader, AccessionPending;
        internal Scenario Copy(string name) { var result = (Scenario)MemberwiseClone(); result.Name = name; return result; }
    }

    private sealed class Manager : ICampaignBehaviorManager
    {
        internal CampaignBehaviorBase Behavior;
        public T GetBehavior<T>() => Behavior is T item ? item : default(T);
        public IEnumerable<T> GetBehaviors<T>() => Behavior is T item ? new[] { item } : Enumerable.Empty<T>();
        public void AddBehavior(CampaignBehaviorBase b) { Behavior = b; }
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase { }
        public void ClearBehaviors() { }
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> b) { }
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }

    private sealed class HouseholdModel : DefaultMarriageModel
    {
        public override Clan GetClanAfterMarriage(Hero firstHero, Hero secondHero) => _scenario.Incoming ? _npcHouse : _playerHouse;
    }

    private static Scenario _scenario;
    private static Hero _player, _npc, _main, _npcLeader, _otherRuler;
    private static Clan _playerHouse, _npcHouse;
    private static Kingdom _home, _foreign;
    private static MBReadOnlyList<Town> _fiefs;
    private static int _nativeCalls;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool ClanOf(Hero __instance, ref Clan __result)
    { __result = __instance == _npc || __instance == _npcLeader ? _npcHouse : _playerHouse; return false; }
    private static bool PlayerHero(ref Hero __result) { __result = _scenario.PersonalCourtship ? _player : _main; return false; }
    private static bool Leader(Clan __instance, ref Hero __result)
    { __result = __instance == _npcHouse ? _npcLeader : _main; return false; }
    private static bool Tier(Clan __instance, ref int __result)
    { __result = __instance == _npcHouse ? _scenario.NpcTier : _scenario.PlayerTier; return false; }
    private static bool Renown(ref float __result) { __result = 1000; return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result)
    { __result = __instance == _npcHouse && !_scenario.Domestic ? _foreign : _home; return false; }
    private static bool Ruler(Kingdom __instance, ref Hero __result)
    { __result = _scenario.Royal && (__instance == _foreign || _scenario.Domestic) ? _npcLeader : _otherRuler; return false; }
    private static bool Fiefs(ref MBReadOnlyList<Town> __result) { __result = _fiefs; return false; }
    private static bool Age(Hero __instance, ref float __result)
    { __result = __instance == _player ? _scenario.PlayerAge : _scenario.NpcAge; return false; }
    private static bool Female(Hero __instance, ref bool __result)
    { __result = __instance == _player ? _scenario.PlayerFemale : !_scenario.PlayerFemale; return false; }
    private static bool Seed(Hero __instance, ref int __result)
    { __result = __instance == _player ? _scenario.PlayerSeed : _scenario.NpcSeed; return false; }
    private static bool Personal(ref int __result) { __result = _scenario.PersonalRelation; return false; }
    private static bool Relation(ref int __result) { __result = _scenario.ClanRelation; return false; }
    private static bool Command(ref float __result) { __result = _scenario.CommandStrength; return false; }
    private static bool Heir(ref Hero __result) { __result = _scenario.CrownHeir ? _npc : null; return false; }

    private static int Native(Scenario scenario, bool reversed = false, bool currentPatch = false)
    {
        _scenario = scenario;
        var item = new MarriageBarterable(_main, null, reversed ? _npc : _player, reversed ? _player : _npc);
        _nativeCalls++;
        int value = item.GetUnitValueForFaction(_npcHouse);
        // Historical baseline remains here for comparison; it is no longer the production patch.
        if (currentPatch && !reversed)
            value *= 1 + Math.Max(0, scenario.NpcTier - scenario.PlayerTier) + (scenario.CrownHeir ? 1 : 0);
        return value;
    }

    private static double Debt(int value) => Math.Max(0d, -(double)value);
    private static double TierFactor(Scenario s) => 1 + .5 * Math.Min(4, Math.Max(0, s.NpcTier - s.PlayerTier));

    // Deliberate player negotiations may buy an heir exception, never a leader or last continuation.
    private static double? Proposed(Scenario s, int value)
    {
        if (s.AccessionPending || s.Incoming && s.PersonalCourtship
            || !s.Incoming && (s.LastContinuation || s.CurrentLeader)) return null;
        double risk = Math.Max(0, Math.Min(1, s.Risk));
        double ordinary = Debt(value) * TierFactor(s);
        if (s.Incoming) return Math.Ceiling(ordinary * (s.ContinuationBenefit ? 1 - .5 * risk : 1));
        double premium = s.CrownHeir ? 100000 : s.ClanHeir ? 50000 : 0;
        return Math.Ceiling(ordinary + premium + (s.OutgoingViable ? 50000 * risk : 0));
    }

    private static double? QuoteOffer(Scenario s, bool reversed)
    {
        _scenario = s;
        var offer = new MarriageBarterable(_main, null, reversed ? _npc : _player, reversed ? _player : _npc);
        Hero npcSpouse = offer.ProposingHero.Clan == _npcHouse ? offer.ProposingHero : offer.HeroBeingProposedTo;
        Hero playerSpouse = npcSpouse == offer.ProposingHero ? offer.HeroBeingProposedTo : offer.ProposingHero;
        // The prototype normalizes the evaluator, not the live barter item or campaign household.
        var canonical = new MarriageBarterable(offer.OriginalOwner, null, playerSpouse, npcSpouse);
        _nativeCalls++;
        return Proposed(s, canonical.GetUnitValueForFaction(_npcHouse));
    }

    private static string Price(double? value) => value.HasValue ? value.Value.ToString("N0", CultureInfo.InvariantCulture) : "REFUSED";

    internal static void Run(Action<bool, string> check)
    {
        _player = Blank<Hero>(); _npc = Blank<Hero>(); _main = Blank<Hero>();
        _npcLeader = Blank<Hero>(); _otherRuler = Blank<Hero>();
        _playerHouse = Blank<Clan>(); _npcHouse = Blank<Clan>();
        _home = Blank<Kingdom>(); _foreign = Blank<Kingdom>();
        _fiefs = new MBReadOnlyList<Town>(Enumerable.Range(0, 10).Select(_ => Blank<Town>()).ToList());
        var campaign = Blank<Campaign>(); var models = Blank<GameModels>();
        AccessTools.Field(typeof(Campaign), "_gameModels").SetValue(campaign, models);
        void Model(string property, object model) => AccessTools.PropertySetter(typeof(GameModels), property).Invoke(models, new[] { model });
        Model("MarriageModel", new HouseholdModel()); Model("AgeModel", new DefaultAgeModel());
        Model("DiplomacyModel", new DefaultDiplomacyModel());
        campaign.AddCampaignBehaviorManager(new Manager { Behavior = Blank<DynasticHeirBehavior>() });
        var previous = Campaign.Current;
        var harmony = new Harmony("bellum.simulation.player_marriage_dowry");
        void Patch(MethodBase target, string method) => harmony.Patch(target, prefix: new HarmonyMethod(typeof(MarriageDowrySimulation), method));
        try
        {
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(ClanOf));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), nameof(PlayerHero));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Age"), nameof(Age));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsFemale"), nameof(Female));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "RandomValue"), nameof(Seed));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Tier"), nameof(Tier));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Renown"), nameof(Renown));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Leader"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Fiefs"), nameof(Fiefs));
            Patch(AccessTools.Method(typeof(Hero), "GetRelation", new[] { typeof(Hero) }), nameof(Personal));
            Patch(AccessTools.Method(typeof(FactionManager), "GetRelationBetweenClans"), nameof(Relation));
            Patch(AccessTools.Method(typeof(DefaultDiplomacyModel), "GetHeroCommandingStrengthForClan"), nameof(Command));
            Patch(AccessTools.Method(typeof(DynasticHeirBehavior), "GetDynasticHeir"), nameof(Heir));

            Console.WriteLine("Installed native MarriageBarterable, historical Bellum baseline, approved formula and production pricing.");
            Console.WriteLine("Synthetic inputs: default age model; 28-year-old spouses; 600 command strength; 1,000 renown; native hero seeds 25,000; royal house has 10 fiefs.");
            Console.WriteLine("No weddings, gold transfers, savegame sampling, or whole-campaign balance simulation.");
            var ordinary = new Scenario { Name = "Ordinary, equal tiers, neutral relations" };
            var gap = ordinary.Copy("Ordinary, NPC two tiers higher"); gap.NpcTier = 6;
            var clan = ordinary.Copy("First clan heir leaves, equal tiers"); clan.ClanHeir = true;
            var crown = ordinary.Copy("Crown heir leaves, equal tiers"); crown.CrownHeir = crown.ClanHeir = crown.Royal = true;
            var crownGap = crown.Copy("Crown heir leaves, NPC two tiers higher"); crownGap.NpcTier = 6;
            var vulnerable = crownGap.Copy("Same Crown match, household risk 0.5"); vulnerable.Risk = .5f;
            var incoming = crown.Copy("Player joins Crown heir, household risk 0.5"); incoming.Incoming = true; incoming.Risk = .5f;
            var urgent = incoming.Copy("Player joins Crown heir, household risk 1"); urgent.Risk = 1;
            var friendly = crown.Copy("Crown heir leaves, +50 clan / +25 personal"); friendly.ClanRelation = 50; friendly.PersonalRelation = 25;
            var hostile = crown.Copy("Crown heir leaves, -50 clan / -25 personal"); hostile.ClanRelation = -50; hostile.PersonalRelation = -25;
            var lowTier = crown.Copy("Tier 0 player seeks tier 6 Crown heir"); lowTier.PlayerTier = 0; lowTier.NpcTier = 6;
            var noFuture = incoming.Copy("Incoming match adds no reproductive continuity"); noFuture.ContinuationBenefit = false;
            var last = crown.Copy("NPC losing last viable continuation"); last.LastContinuation = true;
            var own = crown.Copy("Main character proposes personally"); own.PersonalCourtship = true;
            var rows = new[] { ordinary, gap, clan, crown, crownGap, vulnerable, incoming, urgent, friendly, hostile, lowTier, noFuture, last, own };
            Console.WriteLine("Case | Native base debt | Historical Bellum | Approved");
            foreach (var row in rows)
            {
                // The old personal-courtship route reverses the two constructor arguments.
                int oldNative = Native(row, row.PersonalCourtship);
                int oldBellum = Native(row, row.PersonalCourtship, true);
                int normalized = Native(row);
                Console.WriteLine($"{row.Name} | {Price(Debt(oldNative))} | {Price(Debt(oldBellum))} | {Price(Proposed(row, normalized))}");
            }

            check(Native(ordinary) == -45000, "Real native method produces the neutral seeded base, not a fixed assumed 50,000");
            check(Native(gap, currentPatch: true) == Native(gap) * 3, "Current tier gap adds 100 percent per higher NPC tier");
            check(Native(crown, currentPatch: true) == Native(crown) * 2, "Current Crown heir premium doubles equal-tier native debt");
            check(Native(clan, currentPatch: true) == Native(clan), "Current patch has no ordinary first-clan-heir premium");
            check(Native(incoming, currentPatch: true) == Native(incoming) * 2, "Current premium charges even when the NPC Crown heir stays home");
            check(Native(own, true) == -1000 && Native(own, true, true) == -1000,
                "Native personal-courtship argument order uses the other valuation branch and misses NPC heir premium");
            check(Native(friendly) > 0 && Native(friendly, currentPatch: true) == 2 * Native(friendly),
                "Current patch amplifies positive goodwill; a multiplier alone leaves a friendly Crown heir with no compensation debt");
            check(Proposed(friendly, Native(friendly)) == 100000, "Proposed Crown departure premium survives a zero native base debt");
            check(Proposed(crown, Native(crown)) - Proposed(ordinary, Native(crown)) == 100000,
                "Crown and clan heir status use one Crown premium, not two stacked premiums");
            check(Proposed(urgent, Native(urgent)) == Math.Ceiling(Debt(Native(urgent)) * .5),
                "A useful incoming spouse receives the capped 50 percent continuity discount at maximum risk");
            check(Proposed(noFuture, Native(noFuture)) == Debt(Native(noFuture)),
                "No survival discount when an incoming match provides no reproductive continuity");
            check(Proposed(last, Native(last)) == null, "Last-continuation departure remains a refusal regardless of money");
            var leader = crown.Copy("leader"); leader.CurrentLeader = true;
            var pending = crown.Copy("pending"); pending.AccessionPending = true;
            check(Proposed(leader, Native(leader)) == null && Proposed(pending, Native(pending)) == null,
                "Current-leader departure and pending accession remain protected");
            var movePlayer = incoming.Copy("player cannot leave"); movePlayer.PersonalCourtship = true;
            check(Proposed(movePlayer, Native(movePlayer)) == null, "Personal courtship cannot move the player character into the NPC house");
            check(QuoteOffer(own, false) == QuoteOffer(own, true),
                "Canonical NPC-side valuation quotes the same personal match through either dialogue construction");
            var infertileOutgoing = ordinary.Copy("non-continuation departure");
            infertileOutgoing.Risk = 1; infertileOutgoing.OutgoingViable = false;
            check(Proposed(infertileOutgoing, Native(infertileOutgoing)) == Debt(Native(infertileOutgoing)),
                "Vulnerability does not charge a reproductive-reserve fee for a non-continuation member");

            var health = typeof(StrategicMarriageBehavior).Assembly.GetType("BellumCivile.Behaviors.MarriageHealthRules");
            float Risk(int adults, int children, int prospects, float coverage) => (float)AccessTools.Method(health, "Risk")
                .Invoke(null, new object[] { adults, children, prospects, coverage });
            check(Risk(4, 2, 2, 1) == 0 && Risk(3, 1, 1, 0) == .5f && Risk(2, 0, 1, 0) == 1,
                "Simulated risk 0/0.5/1 corresponds to existing healthy/vulnerable/urgent household rules");

            int cases = 0, monotonicFailures = 0, directionFailures = 0, sexFailures = 0, orientationFailures = 0, productionFailures = 0;
            var pricing = AccessTools.Method(typeof(BellumCivile.BellumMarriageModel).Assembly.GetType("BellumCivile.PlayerMarriagePricing"), "Calculate");
            int Production(Scenario s, int value) => (int)pricing.Invoke(null, new object[] {
                value, s.NpcTier - s.PlayerTier, s.Risk, !s.Incoming, s.CrownHeir, s.ClanHeir,
                s.Incoming ? s.ContinuationBenefit : s.OutgoingViable });
            double minimum = double.MaxValue, maximum = 0;
            foreach (int playerTier in new[] { 0, 2, 4, 6 })
            foreach (int npcTier in new[] { 2, 4, 6 })
            foreach (int clanRelation in new[] { -50, 0, 50, 100 })
            foreach (int personalRelation in new[] { -50, 0, 50 })
            foreach (int heir in new[] { 0, 1, 2 })
            foreach (float risk in new[] { 0f, .5f, 1f })
            foreach (bool joinsNpc in new[] { false, true })
            {
                var s = new Scenario { PlayerTier = playerTier, NpcTier = npcTier, ClanRelation = clanRelation,
                    PersonalRelation = personalRelation, ClanHeir = heir == 1, CrownHeir = heir == 2,
                    Royal = heir == 2, Risk = risk, Incoming = joinsNpc };
                int value = Native(s);
                double cost = Proposed(s, value).Value;
                if (Production(s, value) != cost) productionFailures++;
                minimum = Math.Min(minimum, cost); maximum = Math.Max(maximum, cost); cases++;
                var friendlier = s.Copy("friendlier"); friendlier.ClanRelation++;
                if (Proposed(friendlier, Native(friendlier)) > cost) monotonicFailures++;
                var changedSex = s.Copy("other sex"); changedSex.PlayerFemale = true;
                if (Proposed(changedSex, Native(changedSex)) != cost) sexFailures++;
                if (QuoteOffer(s, false) != cost || QuoteOffer(s, true) != cost) orientationFailures++;
                var moreRisk = s.Copy("greater risk"); moreRisk.Risk = Math.Min(1, s.Risk + .1f);
                double risky = Proposed(moreRisk, Native(moreRisk)).Value;
                if (joinsNpc ? risky > cost : risky < cost) directionFailures++;
                if (cost < 0 || double.IsInfinity(cost) || double.IsNaN(cost)) throw new Exception("Invalid quote");
            }
            Console.WriteLine($"Synthetic grid: {cases} cases; compensation range {Price(minimum)} to {Price(maximum)}; not a frequency-weighted campaign sample.");
            check(monotonicFailures == 0, "Better relations never increase proposed compensation across the grid");
            check(directionFailures == 0, "Household vulnerability increases departure cost and reduces useful incoming cost");
            check(sexFailures == 0, "Identical household terms cost the same for either spouse sex");
            check(orientationFailures == 0, "Both constructor argument orders produce the same normalized quote across the grid");
            check(productionFailures == 0, "Production pricing matches the independent approved formula in all 2,592 cases");
            check(Production(crown, int.MinValue) == int.MaxValue, "Production compensation saturates safely at integer bounds");

            var extreme = crown.Copy("tier cap"); extreme.PlayerTier = 0; extreme.NpcTier = 6;
            check(TierFactor(extreme) == 3, "Status surcharge caps at 3x; heir premium is not multiplied");
            check(Proposed(crown, int.MinValue).Value > 0, "Prototype uses wide arithmetic for extreme signed native values");
            foreach (int seed in new[] { 1, 10000, 25000, 49999 })
            {
                var seeded = crown.Copy("seed"); seeded.PlayerSeed = seeded.NpcSeed = seed;
                Console.WriteLine($"Native seed {seed}: Crown departure base={Price(Debt(Native(seeded)))}; proposed={Price(Proposed(seeded, Native(seeded)))}");
                check(Native(seeded) == Native(seeded), "Repeated quote keeps the hero's native deterministic variation");
            }
            var elderly = ordinary.Copy("older player spouse"); elderly.PlayerAge = 50;
            check(Debt(Native(elderly)) > Debt(Native(ordinary)), "Native age-dependent valuation remains in the baseline");
            var domestic = incoming.Copy("domestic"); domestic.Domestic = true;
            Console.WriteLine($"Incoming Crown match: foreign base={Price(Debt(Native(incoming)))}; domestic base={Price(Debt(Native(domestic)))} (native receiving-house/realm values).");
            Console.WriteLine($"Native valuation calls: {_nativeCalls}. Proposed cost calculations never call MarriageAction or mutate a clan.");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { previous });
        }
    }
}
