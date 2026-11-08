using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class RetainerMarriageTests
{
    private sealed class Manager : ICampaignBehaviorManager
    {
        internal readonly List<CampaignBehaviorBase> Items = new List<CampaignBehaviorBase>();
        public T GetBehavior<T>() => Items.OfType<T>().FirstOrDefault();
        public IEnumerable<T> GetBehaviors<T>() => Items.OfType<T>();
        public void AddBehavior(CampaignBehaviorBase b) => Items.Add(b);
        public void RemoveBehavior<T>() where T : CampaignBehaviorBase => Items.RemoveAll(b => b is T);
        public void ClearBehaviors() => Items.Clear();
        public void InitializeCampaignBehaviors(IEnumerable<CampaignBehaviorBase> b) => Items.AddRange(b);
        public void LoadBehaviorData() { }
        public void RegisterEvents() { }
    }

    private static readonly Dictionary<Hero, float> Ages = new Dictionary<Hero, float>();
    private static Hero _engaged, _barred, _main, _legalHead;
    private static Clan _player, _changedFrom;
    private static int _events, _clanEvents;
    private static CampaignEventDispatcher _dispatcher;
    private static IReadOnlyCollection<FeudalTitleRecord> _held;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static Type Type(string name) => typeof(BellumMarriageModel).Assembly.GetType("BellumCivile." + name, true);
    private static bool Age(Hero __instance, ref float __result) { __result = Ages[__instance]; return false; }
    private static bool Child(Hero __instance, ref bool __result) { __result = Ages[__instance] < 18; return false; }
    private static bool Engaged(Hero hero, ref bool __result) { __result = hero == _engaged; return false; }
    private static bool CanMarry(Hero hero, ref bool result) { _events++; if (hero == _barred) result = false; return false; }
    private static bool Changed(Hero hero, Clan oldClan) { _changedFrom = oldClan; _clanEvents++; return false; }
    private static bool Dispatcher(ref CampaignEventDispatcher __result) { __result = _dispatcher; return false; }
    private static bool Main(ref Hero __result) { __result = _main; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Zero(ref int __result) { __result = 0; return false; }
    private static bool Day(ref float __result) { __result = 100; return false; }
    private static bool Ambition(ref float __result) { __result = 1; return false; }
    private static bool NoCourt(ref Hero __result) { __result = null; return false; }
    private static bool Held(ref IReadOnlyCollection<FeudalTitleRecord> __result) { __result = _held; return false; }
    private static bool LegalHead(ref Hero __result) { if (_legalHead == null) return true; __result = _legalHead; return false; }
    private static bool Skip() => false;
    private static bool Name(Hero __instance, ref TextObject __result) { __result = new TextObject("{=!}" + __instance.StringId); return false; }

    internal static void Run(Action<bool, string> check)
    {
        var h = new Harmony("bellum.tests.retainer_marriage");
        var oldCampaign = Campaign.Current;
        var household = Type("HouseholdMembership");
        var policy = Type("MarriageParticipantPolicy");
        void Invalidate() => AccessTools.Method(household, "Invalidate").Invoke(null, new object[] { null });
        void Patch(MethodBase method, string prefix) => h.Patch(method, prefix: new HarmonyMethod(typeof(RetainerMarriageTests), prefix));
        Clan House(string id)
        {
            var c = Blank<Clan>(); c.StringId = id;
            foreach (string field in new[] { "_heroesCache", "_companionsCache", "_aliveLordsCache", "_deadLordsCache" })
                AccessTools.Field(typeof(Clan), field).SetValue(c, new MBList<Hero>());
            return c;
        }
        Hero Person(Clan c, string id, bool female = false, bool companion = false)
        {
            var hero = Blank<Hero>(); hero.StringId = id; hero.IsFemale = female; Ages[hero] = 30;
            AccessTools.Field(typeof(Hero), "_children").SetValue(hero, new MBList<Hero>());
            AccessTools.Field(typeof(Hero), "_exSpouses").SetValue(hero, new MBList<Hero>());
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(hero, Hero.CharacterStates.Active);
            AccessTools.PropertySetter(typeof(Hero), "Occupation").Invoke(hero,
                new object[] { companion ? Occupation.Wanderer : Occupation.Lord });
            var character = Blank<CharacterObject>();
            AccessTools.Field(typeof(CharacterObject), "_heroObject").SetValue(character, hero);
            AccessTools.Field(typeof(Hero), "_characterObject").SetValue(hero, character);
            if (companion) hero.CompanionOf = c; else hero.Clan = c;
            return hero;
        }
        bool Birthright(Hero hero, Clan c) => (bool)AccessTools.Method(typeof(FeudalTitleBehavior), "IsCloseBloodClaimantOfClan")
            .Invoke(null, new object[] { hero, c });
        float Cost(Hero member, Hero spouse) => (float)AccessTools.Method(policy, "RetainerCost")
            .Invoke(null, new object[] { member, spouse });
        try
        {
            var campaign = Blank<Campaign>(); var models = Blank<GameModels>();
            var model = new BellumMarriageModel();
            AccessTools.PropertySetter(typeof(GameModels), "MarriageModel").Invoke(models, new object[] { model });
            AccessTools.Field(typeof(Campaign), "_gameModels").SetValue(campaign, models);
            campaign.AddCampaignBehaviorManager(new Manager());
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            _dispatcher = Blank<CampaignEventDispatcher>(); _legalHead = _barred = _engaged = null;
            Patch(AccessTools.PropertyGetter(typeof(CampaignEventDispatcher), "Instance"), nameof(Dispatcher));
            Patch(AccessTools.Method(typeof(CampaignEventDispatcher), "OnHeroChangedClan"), nameof(Changed));
            Patch(AccessTools.Method(typeof(CampaignEventDispatcher), "CanHeroMarry"), nameof(CanMarry));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), nameof(Main));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Age"), nameof(Age));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Name"), nameof(Name));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsChild"), nameof(Child));
            Patch(AccessTools.Method(typeof(Romance), "GetCourtedHeroInOtherClan"), nameof(NoCourt));
            Patch(AccessTools.Method(typeof(Hero), "UpdateHomeSettlement"), nameof(Skip));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Skip));
            Patch(AccessTools.PropertyGetter(typeof(FeudalTitleBehavior), "CurrentDay"), nameof(Day));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetTitlesHeldByClan"), nameof(Held));
            Patch(AccessTools.Method(Type("MarriageHouseholdPolicy"), "LegalHead"), nameof(LegalHead));
            _player = House("retainer_player"); var other = House("retainer_other");
            _main = Person(_player, "player_head"); var head = Person(other, "other_head");
            AccessTools.Field(typeof(Clan), "_leader").SetValue(_player, _main);
            AccessTools.Field(typeof(Clan), "_leader").SetValue(other, head);
            var son = Person(_player, "son"); son.Father = _main;
            var daughter = Person(other, "daughter", true); daughter.Father = head;
            var companion = Person(_player, "companion", companion: true);
            var existingChild = Person(_player, "companion_child"); existingChild.Father = companion;
            var retainer = Person(other, "retainer", true);
            var retainerChild = Person(other, "retainer_child", true); retainerChild.Mother = retainer;
            var widow = Person(other, "widow", true); head.Spouse = widow;
            Invalidate();
            check(Cost(son, daughter) == 0 && Cost(son, retainer) == 25
                && Cost(companion, daughter) == 0 && Cost(companion, retainer) == 0,
                "Only the family side of a mixed match has status reluctance; retainer pairs have no blanket penalty");
            check(Cost(son, widow) == 0, "Married-in family receives the same priority as blood relatives");
            var helper = Type("Behaviors.BellumMarriageStrategyHelper");
            object health = AccessTools.Method(helper, "AssessHouseHealth").Invoke(null, new object[] { other });
            check((int)AccessTools.Field(health.GetType(), "Adults").GetValue(health) == 3,
                "Unrelated retainers do not conceal a ruling family's actual demographic health");
            check(model.IsSuitableForMarriage(companion) && companion.CanMarry(),
                "An enrolled player companion is eligible without becoming a noble");
            check(companion.IsWanderer && !companion.IsLord && companion.CompanionOf == _player,
                "Eligibility preserves native occupation and companion membership");
            check(model.IsCoupleSuitableForMarriage(companion, daughter)
                && model.IsCoupleSuitableForMarriage(daughter, companion),
                "Native couple validation supports a companion in either proposal orientation");
            var proposal = new SpecificMarriageProposalBehavior();
            var playerCandidates = (List<Hero>)AccessTools.Method(typeof(SpecificMarriageProposalBehavior), "GetPlayerCandidates")
                .Invoke(proposal, new object[] { other });
            check(playerCandidates.Contains(companion) && playerCandidates.IndexOf(son) < playerCandidates.IndexOf(companion),
                "The player's proposal dialogue includes companions after available family members");
            var otherCandidates = (List<Hero>)AccessTools.Method(typeof(SpecificMarriageProposalBehavior), "GetOtherCandidates")
                .Invoke(proposal, new object[] { companion, other });
            check(otherCandidates.IndexOf(daughter) >= 0 && otherCandidates.IndexOf(daughter) < otherCandidates.IndexOf(retainer),
                "The other house's proposal list also displays eligible family before retainers");
            List<Hero> Players() => (List<Hero>)AccessTools.Method(typeof(SpecificMarriageProposalBehavior), "GetPlayerCandidates")
                .Invoke(proposal, new object[] { other });
            List<Hero> Others(Hero hero) => (List<Hero>)AccessTools.Method(typeof(SpecificMarriageProposalBehavior), "GetOtherCandidates")
                .Invoke(proposal, new object[] { hero, other });
            string Reason(Hero hero)
            {
                object[] args = { hero, other, null, null };
                bool enabled = (bool)AccessTools.Method(typeof(SpecificMarriageProposalBehavior), "CanSelectPlayerCandidate").Invoke(null, args);
                check(!enabled && args[2] is TextObject, "A disabled candidate has an explanatory tooltip: " + hero.StringId);
                return ((TextObject)args[2]).GetID();
            }
            string PairReason(Hero hero, Hero partner)
            {
                object[] args = { hero, partner, other, null };
                bool enabled = (bool)AccessTools.Method(typeof(SpecificMarriageProposalBehavior), "CanSelectPair").Invoke(null, args);
                return enabled ? null : ((TextObject)args[3]).GetID();
            }
            var femaleCompanion = Person(_player, "female_companion", true, true);
            Invalidate();
            check(Players().Contains(femaleCompanion) && Reason(femaleCompanion) == "BC_Marriage_CandidateNoMatch"
                && Players().IndexOf(companion) < Players().IndexOf(femaleCompanion),
                "A companion without a compatible spouse remains visible after available candidates, with a no-match reason");
            var otherMale = Person(other, "other_male"); Invalidate();
            check(PairReason(femaleCompanion, otherMale) == null && Others(femaleCompanion).First() == otherMale,
                "A female companion becomes selectable when a valid groom exists, ahead of unavailable nobles");
            Ages[companion] = 12;
            check(Players().Contains(companion) && Reason(companion) == "BC_Marriage_CandidateMinor"
                && Players().IndexOf(femaleCompanion) < Players().IndexOf(companion),
                "A minor stays visible but disabled after an available companion");
            Ages[companion] = 30;
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(companion, Hero.CharacterStates.Prisoner);
            check(Reason(companion) == "BC_Marriage_CandidatePrisoner", "Captivity gets a specific player-candidate explanation");
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(companion, Hero.CharacterStates.Active);
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(daughter, Hero.CharacterStates.Prisoner);
            check(Others(companion).Contains(daughter) && PairReason(companion, daughter) == "BC_Marriage_CandidatePrisoner"
                && Others(companion).IndexOf(retainer) < Others(companion).IndexOf(daughter),
                "An imprisoned NPC noble remains visible below an available retainer with the correct tooltip");
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(daughter, Hero.CharacterStates.Active);
            companion.Spouse = femaleCompanion;
            check(Players().Contains(companion) && Reason(companion) == "BC_Marriage_CandidateMarried",
                "Already married household members remain visible with an explicit reason");
            companion.Spouse = null;
            _barred = retainer;
            check(PairReason(companion, retainer) == "BC_Marriage_CandidateUnavailable",
                "Another mod's individual marriage veto remains disabled without inventing a reason for it");
            _barred = null;
            check(PairReason(companion, otherMale) == "BC_Marriage_CandidateSameSex",
                "The opposite household explains an incompatible bride/groom selection");
            var deadMember = Person(_player, "dead_companion", companion: true);
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(deadMember, Hero.CharacterStates.Dead);
            Invalidate();
            check(!Players().Contains(deadMember), "Dead members are not offered as disabled marriage candidates");
            MarriageProposalDialogueTests.Run(check, proposal, companion, daughter, head);
            AccessTools.Field(typeof(Clan), "_kingdom").SetValue(_player, Blank<Kingdom>());
            object rejection = AccessTools.Method(helper, "GetOfferParticipantRejectionReason")
                .Invoke(null, new object[] { companion, true, false });
            AccessTools.Field(typeof(Clan), "_kingdom").SetValue(_player, null);
            check(Convert.ToInt32(rejection) == 0, "The automatic player-offer participant filter accepts an eligible companion");
            int before = _events; _barred = companion;
            check(!model.IsCoupleSuitableForMarriage(companion, daughter) && _events > before,
                "Companion marriage still honors another mod's CanHeroMarry veto");
            _barred = null;
            var offers = Blank<MarriageOfferCampaignBehavior>(); campaign.CampaignBehaviorManager.AddBehavior(offers);
            Patch(AccessTools.Method(typeof(MarriageOfferCampaignBehavior), "IsHeroEngaged"), nameof(Engaged));
            _engaged = companion;
            check(!model.IsSuitableForMarriage(companion), "An existing engagement still blocks companion proposals");
            check(Reason(companion) == "BC_Marriage_CandidateEngaged", "An engagement has its own candidate tooltip");
            _engaged = null;
            Ages[companion] = 12;
            check(!model.IsSuitableForMarriage(companion), "Companion eligibility does not bypass adulthood");
            Ages[companion] = 30;
            daughter.Father = companion;
            check(!model.IsCoupleSuitableForMarriage(companion, daughter), "Native blood-kinship rejection remains active");
            daughter.Father = head;
            var unattached = Person(null, "unattached", companion: true);
            check(!model.IsSuitableForMarriage(unattached), "Unrecruited wanderers are not enabled as house candidates");
            check(Birthright(daughter, other) && !Birthright(retainerChild, other) && !Birthright(widow, other),
                "Royal children carry birthrights; retainer children and married-in spouses do not gain the employer's claims");
            AccessTools.Field(typeof(Clan), "_leader").SetValue(other, retainer);
            _legalHead = daughter;
            check(!Birthright(retainerChild, other) && Birthright(daughter, other),
                "A regent's children do not acquire the legal ward's dynastic birthrights");
            _legalHead = null;
            AccessTools.Field(typeof(Clan), "_leader").SetValue(other, head);
            var titles = new FeudalTitleBehavior();
            var land = new FeudalTitleRecord("retainer_royal_title", "Royal title", FeudalTitleType.Kingdom,
                other.StringId, other.StringId, "", "", "realm", 0, 0);
            _held = new[] { land };
            titles.RegisterMarriageFounderBirthrightClaims(other, _player, retainerChild, "marriage_birthright");
            check(!titles.GetActiveClaims().Any(), "The real wedding birthright hook creates no employer-house claims for a retainer");
            titles.RegisterMarriageFounderBirthrightClaims(other, _player, daughter, "marriage_birthright");
            check(titles.GetActiveClaims().Single().CarrierHeroId == daughter.StringId,
                "The real wedding birthright hook still registers a legitimate royal child's claim");
            var ancestral = new FeudalTitleRecord("retainer_ancestral", "Ancestral title", FeudalTitleType.Barony,
                "third_house", "third_house", "", "", "realm", 0, 0);
            var personal = titles.RegisterClaim(other, ancestral, FeudalClaimStrength.Strong, "inherited_blood_claim",
                retainerChild, other, carrierHero: retainerChild);
            titles.RegisterMarriageFounderBirthrightClaims(other, _player, retainerChild, "marriage_birthright");
            check(personal.IsActive && personal.CarrierHeroId == retainerChild.StringId
                && personal.Strength == FeudalClaimStrength.Strong,
                "Blocking employer birthrights does not erase genuine personal ancestral claims");
            Patch(AccessTools.Method(helper, "CalculateTitleAmbitionMultiplier"), nameof(Ambition));
            var cacheType = helper.GetNestedType("MarriageTitleClaimCache", BindingFlags.NonPublic);
            var cache = AccessTools.Method(cacheType, "Build").Invoke(null, new object[] { null, 100 });
            var indexed = (Dictionary<string, FeudalTitleRecord>)AccessTools.Property(cacheType, "TitlesById").GetValue(cache);
            indexed[land.TitleId] = land; indexed[ancestral.TitleId] = ancestral;
            ((Dictionary<string, List<FeudalTitleRecord>>)AccessTools.Field(cacheType, "_heldTitlesByClan").GetValue(cache))
                [other.StringId] = new List<FeudalTitleRecord> { land };
            var reasons = new List<string>();
            float Forecast(Hero carrier) => (float)AccessTools.Method(helper, "CalculateDirectionalTitleClaimMarriageValue")
                .Invoke(null, new object[] { _player, _main, other, carrier, cache, reasons, 1f, true });
            check(Forecast(retainerChild) == 0 && Forecast(daughter) > 0,
                "The real claim forecast and real wedding hook agree on retainer versus royal birthrights");
            ((Dictionary<string, List<FeudalClaimRecord>>)AccessTools.Property(cacheType, "ClaimsByClan").GetValue(cache))
                [other.StringId] = new List<FeudalClaimRecord> { personal };
            reasons.Clear();
            check(Forecast(retainerChild) > 0 && reasons.Any(r => r.Contains("conditional inherited claim")),
                "A retainer's genuine personal ancestral claim retains its normal discounted descendant forecast");
            var realm = Blank<Kingdom>(); AccessTools.Field(typeof(Kingdom), "_rulingClan").SetValue(realm, other);
            bool Royal(Hero hero) => (bool)AccessTools.Method(Type("TreatyRoyalMarriageService"), "IsEligibleRoyalRelative")
                .Invoke(null, new object[] { hero, realm, new HashSet<Hero>() });
            check(Royal(daughter) && !Royal(retainerChild), "A treaty royal marriage requires actual royal blood, not employment");
            Patch(AccessTools.Method(typeof(DefaultMarriageModel), "GetEffectiveRelationIncrease"), nameof(Zero));
            Patch(AccessTools.Method(typeof(ChangeRelationAction), "ApplyRelationChangeBetweenHeroes"), nameof(Skip));
            Patch(AccessTools.Method(typeof(CampaignEventDispatcher), "OnBeforeHeroesMarried"), nameof(Skip));
            Patch(AccessTools.Method(typeof(Romance), "EndAllCourtships"), nameof(Skip));
            Patch(AccessTools.Method(typeof(ChangeRomanticStateAction), "Apply"), nameof(Skip));
            check(h.CreateClassProcessor(Type("Patches.CompanionMarriageTransferPatch")).Patch().Count == 1,
                "Installed game binds the wedding-only companion clan correction");
            var scope = Type("NpcMarriageClanContext");
            _clanEvents = 0;
            using ((IDisposable)Activator.CreateInstance(scope, BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { companion, daughter, other }, null)) MarriageAction.Apply(companion, daughter, false);
            check(companion.Spouse == daughter && daughter.Spouse == companion && companion.Clan == other
                && companion.CompanionOf == other && _changedFrom == _player && _clanEvents == 1,
                "A real native wedding moves a departing companion and reports the correct previous house once");
            check(!_player.Heroes.Contains(companion) && !_player.Companions.Contains(companion)
                && other.Heroes.Count(x => x == companion) == 1 && other.Companions.Contains(companion)
                && !other.AliveLords.Contains(companion) && companion.IsWanderer,
                "Wedding transfer leaves no stale old-clan membership or duplicate noble-roster entry");
            check(existingChild.Clan == _player, "A companion's existing children do not follow their parent's wedding transfer");
            Invalidate();
            check((bool)AccessTools.Method(household, "IsFamily").Invoke(null, new object[] { companion }),
                "A companion marrying into the leader's family is recognized as married-in family afterwards");
            var staying = Person(_player, "staying_companion", companion: true);
            var incoming = Person(other, "incoming_companion", true, true);
            using ((IDisposable)Activator.CreateInstance(scope, BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { staying, incoming, _player }, null)) MarriageAction.Apply(staying, incoming, false);
            check(staying.Spouse == incoming && staying.CompanionOf == _player && incoming.CompanionOf == _player
                && !other.Companions.Contains(incoming) && _player.Companions.Count(x => x == incoming) == 1,
                "Companion-to-companion marriage supports an incoming spouse while the player companion stays home");
        }
        finally
        {
            _legalHead = null; Invalidate(); h.UnpatchAll(h.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { oldCampaign });
        }
    }
}
