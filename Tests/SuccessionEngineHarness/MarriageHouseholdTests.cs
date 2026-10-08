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
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Library;

internal static partial class MarriageHouseholdTests
{
    private static readonly Dictionary<Hero, Clan> Houses = new Dictionary<Hero, Clan>();
    private static readonly Dictionary<Hero, Hero> Spouses = new Dictionary<Hero, Hero>();
    private static readonly Dictionary<Hero, float> Ages = new Dictionary<Hero, float>();
    private static readonly HashSet<Hero> Women = new HashSet<Hero>(), Dead = new HashSet<Hero>(), Outsiders = new HashSet<Hero>();
    private static readonly Dictionary<Clan, Hero> Leaders = new Dictionary<Clan, Hero>(), Heirs = new Dictionary<Clan, Hero>();
    private static readonly Dictionary<Kingdom, Hero> Crowns = new Dictionary<Kingdom, Hero>();
    private static readonly Dictionary<Kingdom, Clan> Rulers = new Dictionary<Kingdom, Clan>();
    private static MBReadOnlyList<Kingdom> _realms;
    private static Clan _player, _callbackHouse;
    private static bool _enabled = true;
    private static int _clanReads, _crownReads, _rosterReads, _moves;
    private static BellumMarriageModel _model;
    private static CampaignEventDispatcher _events;

    private static bool House(Hero __instance, ref Clan __result) { __result = Houses[__instance]; return false; }
    private static bool Spouse(Hero __instance, ref Hero __result) { Spouses.TryGetValue(__instance, out __result); return false; }
    private static bool SetSpouse(Hero __instance, Hero value) { Spouses[__instance] = value; return false; }
    private static bool Female(Hero __instance, ref bool __result) { __result = Women.Contains(__instance); return false; }
    private static bool Alive(Hero __instance, ref bool __result) { __result = !Dead.Contains(__instance); return false; }
    private static bool Child(Hero __instance, ref bool __result) { __result = Ages[__instance] < 18; return false; }
    private static bool Age(Hero __instance, ref float __result) { __result = Ages[__instance]; return false; }
    private static bool Leader(Clan __instance, ref Hero __result) { Leaders.TryGetValue(__instance, out __result); return false; }
    private static bool Heroes(Clan __instance, ref MBReadOnlyList<Hero> __result)
    { _rosterReads++; __result = new MBReadOnlyList<Hero>(Houses.Where(p => p.Value == __instance).Select(p => p.Key).ToList()); return false; }
    private static bool ClanLine(Clan clan, ref List<Hero> __result)
    { _clanReads++; __result = Heirs.TryGetValue(clan, out Hero heir) ? new List<Hero> { heir } : new List<Hero>(); return false; }
    private static bool CrownLine(Kingdom realm, ref List<Hero> __result)
    { _crownReads++; __result = Crowns.TryGetValue(realm, out Hero heir) ? new List<Hero> { heir } : new List<Hero>(); return false; }
    private static bool Hereditary(Kingdom realm, ref bool __result) { __result = Crowns.ContainsKey(realm); return false; }
    private static bool Realms(ref MBReadOnlyList<Kingdom> __result) { __result = _realms; return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result) { __result = Rulers[__instance]; return false; }
    private static bool Realm(Clan clan, ref Kingdom __result) { __result = Rulers.First(p => p.Value == clan).Key; return false; }
    private static bool Blood(Hero first, ref bool __result) { __result = !Outsiders.Contains(first); return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool PlayerLeader(ref Hero __result) { __result = _player == null ? null : Leaders[_player]; return false; }
    private static bool Enabled(ref bool __result) { __result = _enabled; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Majority(ref int __result) { __result = 18; return false; }
    private static bool Zero(ref int __result) { __result = 0; return false; }
    private static bool NoPolitics(ref float __result) { __result = 0; return false; }
    private static float _firstScore = 110, _secondScore = 120;
    private static bool Evaluate(Hero member, ref float __result)
    { __result = Women.Contains(member) ? _firstScore : _secondScore; return false; }
    private static bool NoEstates(ref List<Hero> __result) { __result = new List<Hero>(); return false; }
    private static bool NoRegency(ref RegencyBehavior __result) { __result = null; return false; }
    private static bool NoAccession(ref CrownAccessionBehavior __result) { __result = null; return false; }
    private static bool Dispatcher(ref CampaignEventDispatcher __result) { __result = _events; return false; }
    private static bool BeforeWedding(Hero __0, Hero __1)
    { _callbackHouse = _model.GetClanAfterMarriage(__0, __1); return false; }
    private static bool Move(Hero hero, Clan clanAfterMarriage) { Houses[hero] = clanAfterMarriage; _moves++; return false; }
    private static bool Skip() => false;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(BellumMarriageModel).Assembly;
        var policyType = assembly.GetType("BellumCivile.MarriageHouseholdPolicy");
        var scopeType = assembly.GetType("BellumCivile.NpcMarriageClanContext");
        var nativeScopeType = assembly.GetType("BellumCivile.NativeNpcMarriageHouseholdScope");
        var treatyType = assembly.GetType("BellumCivile.TreatyMarriageClanContext");
        var helper = assembly.GetType("BellumCivile.Behaviors.BellumMarriageStrategyHelper");
        var laws = assembly.GetType("BellumCivile.SuccessionLawHelper");
        var oldCampaign = Campaign.Current;
        Houses.Clear(); Spouses.Clear(); Ages.Clear(); Women.Clear(); Dead.Clear(); Outsiders.Clear();
        Leaders.Clear(); Heirs.Clear(); Crowns.Clear(); Rulers.Clear(); _player = null; _enabled = true;
        _clanReads = _crownReads = _rosterReads = _moves = 0;
        var a = Blank<Clan>(); a.StringId = "heiress_house";
        var b = Blank<Clan>(); b.StringId = "groom_house";
        Hero Hero(Clan house, string id, bool woman = false, float age = 30)
        {
            var hero = Blank<Hero>(); hero.StringId = id;
            Houses[hero] = house; Ages[hero] = age;
            if (woman) Women.Add(hero);
            return hero;
        }
        var bride = Hero(a, "heiress", true);
        var groom = Hero(b, "groom");
        Leaders[a] = Hero(a, "bride_parent", age: 60);
        Leaders[b] = Hero(b, "groom_parent", age: 60);
        var spare = Hero(b, "groom_spare");
        var home = Blank<Kingdom>(); var foreign = Blank<Kingdom>(); var third = Blank<Kingdom>();
        Rulers[home] = a; Rulers[foreign] = b;
        var thirdHouse = Blank<Clan>(); Rulers[third] = thirdHouse;
        _realms = new MBReadOnlyList<Kingdom>(new List<Kingdom> { home, foreign, third });
        _model = new BellumMarriageModel(); _events = Blank<CampaignEventDispatcher>();
        var campaign = Blank<Campaign>(); var models = Blank<GameModels>();
        AccessTools.PropertySetter(typeof(GameModels), "MarriageModel").Invoke(models, new object[] { _model });
        AccessTools.Field(typeof(Campaign), "_gameModels").SetValue(campaign, models);
        object Policy() => Activator.CreateInstance(policyType, true);
        Clan Choose(object policy, Hero first = null, Hero second = null, Clan ordinary = null)
        {
            var args = new object[] { first ?? bride, second ?? groom, ordinary ?? b, null };
            return (bool)AccessTools.Method(policyType, "TryChoose").Invoke(policy, args) ? (Clan)args[3] : null;
        }
        bool Last(Hero hero) => (bool)AccessTools.Method(policyType, "IsLastContinuation").Invoke(Policy(), new object[] { hero });
        bool Matches(Clan destination) => (bool)AccessTools.Method(policyType, "Matches").Invoke(null, new object[] { bride, groom, destination });
        IDisposable Scope(Clan destination) => (IDisposable)Activator.CreateInstance(scopeType,
            BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { bride, groom, destination }, null);
        var harmony = new Harmony("bellum.test.marriage_households");
        void Patch(MethodBase target, string method) => harmony.Patch(target, prefix: new HarmonyMethod(typeof(MarriageHouseholdTests), method));
        try
        {
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(House));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), nameof(PlayerLeader));
            Patch(AccessTools.Method(typeof(FactionManager), "IsAtWarAgainstFaction"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Spouse"), nameof(Spouse));
            Patch(AccessTools.PropertySetter(typeof(Hero), "Spouse"), nameof(SetSpouse));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsFemale"), nameof(Female));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Age"), nameof(Age));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Alive));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsChild"), nameof(Child));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDisabled"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsLord"), nameof(Yes));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsHumanPlayerCharacter"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Heroes"), nameof(Heroes));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(Realms));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(RegencyBehavior), "Instance"), nameof(NoRegency));
            Patch(AccessTools.PropertyGetter(typeof(CrownAccessionBehavior), "Instance"), nameof(NoAccession));
            Patch(AccessTools.Method(typeof(CrownAccessionBehavior), "IsHereditaryRealm"), nameof(Hereditary));
            Patch(AccessTools.Method(helper, "MarriagePoliticalRealm"), nameof(Realm));
            Patch(AccessTools.Method(laws, "GetLegalSuccessionLine", new[] { typeof(Clan) }), nameof(ClanLine));
            Patch(AccessTools.Method(laws, "IsBloodRelative"), nameof(Blood));
            Patch(AccessTools.Method(laws, "GetAgeOfMajority"), nameof(Majority));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.HereditaryRealmSuccession"), "GetLine"), nameof(CrownLine));
            Patch(AccessTools.PropertyGetter(assembly.GetType("BellumCivile.BellumCivileOptions"), "EnableBellumStrategicMarriageLogic"), nameof(Enabled));
            Patch(AccessTools.Method(typeof(DefaultMarriageModel), "IsCoupleSuitableForMarriage"), nameof(Yes));

            Heirs[a] = bride;
            check(Choose(Policy()) == a && Choose(Policy(), groom, bride) == a,
                "Lawful clan heiress receives husband in either proposal orientation");
            Heirs[b] = groom;
            check(Choose(Policy()) == null, "A protected groom cannot be traded away instead");
            Heirs[b] = spare;
            var minor = Hero(a, "first_minor_heir", age: 12);
            Heirs[a] = minor;
            check(Choose(Policy()) == b, "Adult woman behind a minor lawful heir is not promoted to primary heiress");
            Crowns[third] = bride;
            check(Choose(Policy()) == a, "Heiress to a third realm is protected even when neither house rules that realm");
            Crowns.Clear(); Heirs.Remove(a);
            check(Choose(Policy()) == b, "An elective Crown alone does not invent a hereditary heiress");
            Heirs[a] = bride;
            check(Choose(Policy()) == a, "An elective realm still protects the lawful clan heiress");

            var policy = Policy();
            int cr = _clanReads, rr = _rosterReads, kr = _crownReads;
            for (int i = 0; i < 100; i++) Choose(policy);
            check(_clanReads - cr == 2 && _rosterReads - rr == 1 && _crownReads == kr,
                "Repeated pairs use one clan-line lookup per house and one donor roster scan");
            Crowns[home] = bride;
            policy = Policy(); kr = _crownReads;
            for (int i = 0; i < 100; i++) Choose(policy);
            check(_crownReads - kr == 1, "Hereditary Crown line is evaluated once per snapshot, not per candidate");

            Dead.Add(spare); Dead.Add(Leaders[b]);
            check(Last(groom) && Choose(Policy()) == null, "Last viable bloodline member is protected regardless of strategic score");
            var outsider = Hero(b, "outsider"); Outsiders.Add(outsider);
            check(Last(groom), "Unrelated clan member does not pretend to preserve the ruling bloodline");
            var child = Hero(b, "remaining_child", age: 10);
            check(!Last(groom) && Choose(Policy()) == a, "Remaining blood child permits a willing spare's departure");
            Dead.Add(child); Dead.Remove(spare); Ages[spare] = 20;
            check(!Last(groom), "Young adult below matchmaking age still represents future family continuity");
            Women.Add(spare); Ages[spare] = 50;
            check(Last(groom), "An unmarried woman beyond reproductive matchmaking age is not a continuation option");
            Women.Remove(spare); Ages[spare] = 60;
            var spouse = Hero(b, "spare_spouse", true, 30); Outsiders.Add(spouse);
            Spouses[spare] = spouse; Spouses[spouse] = spare;
            check(!Last(groom), "An existing reproductive couple with a blood parent preserves continuity");
            Dead.Add(spouse);
            check(Last(groom), "Dead spouse does not count as a reproductive household");
            Spouses.Clear(); Dead.Clear(); Ages[spare] = 30;

            check(Matches(a) && !Matches(b), "Saved destination validates against current heiress rules");
            var contextType = helper.GetNestedType("EvaluationContext", BindingFlags.NonPublic);
            var context = FormatterServices.GetUninitializedObject(contextType);
            AccessTools.Field(contextType, "_households").SetValue(context, Policy());
            AccessTools.Field(contextType, "CrownHeirs").SetValue(context, new HashSet<Hero> { bride });
            AccessTools.Field(contextType, "Realms").SetValue(context, new Dictionary<Clan, Kingdom> { [a] = home, [b] = foreign });
            Patch(AccessTools.Method(contextType, "ParentalEstateProspects"), nameof(NoEstates));
            Patch(AccessTools.Method(contextType, "FertileHousehold"), nameof(Yes));
            Patch(AccessTools.Method(contextType, "PoliticalValue"), nameof(NoPolitics));
            Patch(AccessTools.Method(helper, "EvaluateHouse"), nameof(Evaluate));
            object Match() => AccessTools.Method(helper, "EvaluateOutcome").Invoke(null, new[] { bride, groom, context, (object)true });
            var match = Match();
            var outcome = AccessTools.Property(match.GetType(), "Outcome").GetValue(match);
            check(AccessTools.Property(outcome.GetType(), "Destination").GetValue(outcome) == a
                && (bool)AccessTools.Method(outcome.GetType(), "StillMatches").Invoke(outcome, null),
                "Actual matchmaking forecast and pre-wedding validation agree on the matrilineal destination");
            _secondScore = 94;
            check(Match() == null, "Matrilineal household never bypasses the other house's 95 acceptance floor");
            _secondScore = 120;
            AccessTools.PropertySetter(typeof(GameModels), "MarriageModel").Invoke(models, new object[] { new DefaultMarriageModel() });
            check(!Matches(a), "Incompatible replacement marriage model is rejected before it can move the wrong spouse");
            AccessTools.PropertySetter(typeof(GameModels), "MarriageModel").Invoke(models, new object[] { _model });
            Heirs[b] = groom;
            check(!Matches(a), "A newly inherited groom protection invalidates an already planned wedding");
            Heirs[b] = spare; Heirs.Remove(a); Crowns.Clear();
            check(!Matches(a) && Matches(b), "Changed succession cannot silently flip the saved marriage terms");
            Heirs[a] = bride;
            _player = a;
            check(Choose(Policy()) == b && Matches(b), "Player may consent to a nonleader heir leaving in an incoming offer");
            _player = b;
            check(Choose(Policy()) == a && Matches(a), "Incoming offer retains the NPC heiress and sends the player's groom instead");
            _player = null; _enabled = false;
            check(Choose(Policy()) == b, "Disabling strategic marriage preserves ordinary placement");
            _enabled = true;

            using (Scope(a))
            {
                check(_model.GetClanAfterMarriage(bride, groom) == a && _model.GetClanAfterMarriage(groom, bride) == a,
                    "Wedding context is pair-specific and order-independent");
                Action forced = () => check(_model.GetClanAfterMarriage(bride, groom) == b && Choose(Policy()) == b,
                    "Explicit treaty destination takes precedence over voluntary heiress retention");
                AccessTools.Method(treatyType, "Run").Invoke(null, new object[] { bride, groom, b, forced });
                using (Scope(b)) check(_model.GetClanAfterMarriage(bride, groom) == b, "Nested wedding context uses inner agreement");
                check(_model.GetClanAfterMarriage(bride, groom) == a, "Nested wedding/treaty contexts restore outer agreement");
            }
            try { using (Scope(a)) throw new InvalidOperationException("fixture callback"); }
            catch (InvalidOperationException) { }
            check(_model.GetClanAfterMarriage(bride, groom) == b, "Exception unwinding cannot leak a household override into later marriages");

            using ((IDisposable)Activator.CreateInstance(nativeScopeType, true))
            {
                check(_model.IsCoupleSuitableForMarriage(bride, groom) && _model.GetClanAfterMarriage(bride, groom) == a,
                    "Optional native NPC matchmaking applies the same household policy");
            }
            Heirs[b] = groom;
            using ((IDisposable)Activator.CreateInstance(nativeScopeType, true))
                check(!_model.IsCoupleSuitableForMarriage(bride, groom), "Optional native NPC path cannot bypass groom retention");
            Heirs[b] = spare;
            check(_model.GetClanAfterMarriage(bride, groom) == b, "Outside NPC scope the ordinary model remains unchanged");

            Patch(AccessTools.Method(typeof(DefaultMarriageModel), "GetEffectiveRelationIncrease"), nameof(Zero));
            Patch(AccessTools.Method(typeof(ChangeRelationAction), "ApplyRelationChangeBetweenHeroes"), nameof(Skip));
            Patch(AccessTools.PropertyGetter(typeof(CampaignEventDispatcher), "Instance"), nameof(Dispatcher));
            Patch(AccessTools.Method(typeof(CampaignEventDispatcher), "OnBeforeHeroesMarried"), nameof(BeforeWedding));
            Patch(AccessTools.Method(typeof(MarriageAction), "HandleClanChangeAfterMarriageForHero"), nameof(Move));
            Patch(AccessTools.Method(typeof(Romance), "EndAllCourtships"), nameof(Skip));
            Patch(AccessTools.Method(typeof(ChangeRomanticStateAction), "Apply"), nameof(Skip));
            using (Scope(a)) MarriageAction.Apply(bride, groom, false);
            check(Spouses[bride] == groom && Spouses[groom] == bride && Houses[groom] == a && _moves == 1,
                "Real native wedding orchestration moves the husband to the accepted clan");
            check(_callbackHouse == a, "Before-wedding listeners observe the same receiving house as native transfer");
            check(Houses[child] == b, "Existing child stays in the original house");

            var patch = assembly.GetType("BellumCivile.Patches.BlockVanillaNpcMarriagePatch");
            check(harmony.CreateClassProcessor(patch).Patch().Count > 0,
                "Installed native marriage scheduler accepts household-scope prefix/finalizer signatures");
            RunPlayer(check, harmony, campaign, bride, groom, a, b, spare);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { oldCampaign });
        }
    }
}
