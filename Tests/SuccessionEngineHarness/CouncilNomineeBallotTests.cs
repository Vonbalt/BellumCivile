using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;

internal static class CouncilNomineeBallotTests
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

    private static readonly Assembly Mod = typeof(CourtAgendaRecord).Assembly;
    private static readonly Type Nominations = Mod.GetType("BellumCivile.CouncilAppointmentNominationHelper", true);
    private static Kingdom _realm;
    private static Clan _crown, _player, _a, _b, _c, _nominee, _holder;
    private static Hero _leader;
    private static PerkObject _perk;
    private static List<Clan> _clans, _eligible;
    private static List<KingdomDecision> _decisions;
    private static float _influence;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static void Set(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Crown(ref Clan __result) { __result = _crown; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Realm(ref Kingdom __result) { __result = _realm; return false; }
    private static bool Head(ref Hero __result) { __result = _leader; return false; }
    private static bool Holder(ref Clan __result) { __result = _holder; return false; }
    private static bool Seat(ref PrivyCouncilOfficeRecord __result)
    { __result = new PrivyCouncilOfficeRecord(_realm.StringId, PrivyCouncilOffice.Marshal, 0); return false; }
    private static bool Candidates(ref IReadOnlyList<Clan> __result) { __result = _eligible; return false; }
    private static bool Clans(ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(_clans); return false; }
    private static bool All(ref MBReadOnlyList<Kingdom> __result)
    { __result = new MBReadOnlyList<Kingdom>(new List<Kingdom> { _realm }); return false; }
    private static bool Decisions(ref MBReadOnlyList<KingdomDecision> __result)
    { __result = new MBReadOnlyList<KingdomDecision>(_decisions); return false; }
    private static bool Enqueue(KingdomDecision decision) { _decisions.Add(decision); return false; }
    private static bool Balance(ref float __result) { __result = _influence; return false; }
    private static bool Perk(ref PerkObject __result) { __result = _perk; return false; }
    private static bool Zero(ref float __result) { __result = 0; return false; }
    private static bool Relation(ref int __result) { __result = 0; return false; }
    private static bool Rank(ref FeudalTitleType? __result) { __result = null; return false; }
    private static bool Now(ref CampaignTime __result) { __result = default(CampaignTime); return false; }
    private static bool Find(string clanId, ref Clan __result)
    { __result = _clans.FirstOrDefault(c => c.StringId == clanId); return false; }
    private static bool Merit(Clan candidate, ref float __result)
    { __result = candidate == _a ? 90 : candidate == _b ? 80 : candidate == _c ? 70 : 10; return false; }
    private static bool Support(Clan voter, Clan candidate, ref float __result)
    {
        Clan natural = voter == _b ? _b : voter == _c ? _c : _a;
        __result = candidate == natural ? 90 : 10;
        return false;
    }
    private static bool Skip() => false;

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("bellum.tests.council_nominee_ballot");
        var old = Campaign.Current;
        void Patch(MethodBase target, string prefix) => harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(CouncilNomineeBallotTests), prefix));
        Clan NewClan(string id) { var clan = Blank<Clan>(); clan.StringId = id; return clan; }
        _realm = Blank<Kingdom>(); _realm.StringId = "council_ballot";
        _crown = NewClan("crown"); _a = NewClan("a"); _b = NewClan("b"); _c = NewClan("c");
        _nominee = NewClan("named_nominee"); _player = _nominee; _holder = null;
        _leader = Blank<Hero>(); _perk = Blank<PerkObject>(); _influence = 1000;
        _clans = new List<Clan> { _crown, _a, _b, _c, _nominee };
        _eligible = new List<Clan> { _a, _b, _c, _nominee };
        _decisions = new List<KingdomDecision>();
        var manager = new Manager();
        var calendar = new CourtAgendaBehavior(); var council = new PrivyCouncilBehavior();
        var deliberation = new CouncilAppointmentDeliberationBehavior();
        manager.Items.AddRange(new CampaignBehaviorBase[] { calendar, council, deliberation });
        var campaign = Blank<Campaign>();
        var agenda = new CourtAgendaRecord { Realm = _realm, Sponsor = _crown, PreferredCouncilCandidate = _nominee,
            CouncilMotionId = "formal-motion", State = CourtAgendaState.Deliberating, PaidInfluence = 100,
            ObjectiveData = new CourtObjectiveRecord { Kind = "council_appointment", TargetId = "Marshal", ActionId = "fill" } };
        ((List<CourtAgendaRecord>)AccessTools.Field(typeof(CourtAgendaBehavior), "_agendas").GetValue(calendar)).Add(agenda);
        string pendingKey = _realm.StringId + "|" + (int)PrivyCouncilOffice.Marshal;
        var ids = (Dictionary<string, string>)AccessTools.Field(deliberation.GetType(), "_pendingCourtAgendaIds").GetValue(deliberation);
        ids[pendingKey] = agenda.CouncilMotionId;
        var faction = Blank<FactionObject>(); Set(faction, "_parentKingdom", _realm); Set(faction, "_type", FactionType.Glory);
        Set(faction, "_members", new List<Clan> { _a, _b }); faction.Leader = _a;
        List<Clan> Shortlist(IEnumerable<Clan> ranked, Clan nominee = null) => (List<Clan>)AccessTools.Method(Nominations, "BuildShortlist")
            .Invoke(null, new object[] { ranked, _eligible, nominee });
        Clan ReadNominee(string id, Kingdom realm, PrivyCouncilOffice office, Clan sponsor) =>
            (Clan)AccessTools.Method(calendar.GetType(), "GetCouncilProceedingNominee")
                .Invoke(calendar, new object[] { id, realm, office, sponsor });
        bool Fire(bool stored) => (bool)AccessTools.Method(deliberation.GetType(), "FireAppointmentDecision")
            .Invoke(deliberation, new object[] { _realm, PrivyCouncilOffice.Marshal, agenda.Sponsor, agenda.Faction, "test", stored });
        PrivyCouncilAppointmentDecision Restored(string id = "formal-motion")
        {
            var decision = new PrivyCouncilAppointmentDecision(agenda.Sponsor, PrivyCouncilOffice.Marshal, new[] { _a, _b, _c });
            Set(decision, "CourtAgendaId", id);
            return decision;
        }
        List<Clan> Ballot(PrivyCouncilAppointmentDecision d) => d.DetermineInitialCandidates()
            .Cast<PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome>().Select(o => o.CandidateClan).ToList();
        var namedOutcome = new PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome(_nominee);
        var otherOutcome = new PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome(_a);
        try
        {
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            campaign.AddCampaignBehaviorManager(manager);
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Head));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Influence"), nameof(Balance));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDead"), nameof(No));
            Patch(AccessTools.Method(typeof(Hero), "GetRelation", new[] { typeof(Hero) }), nameof(Relation));
            Patch(AccessTools.Method(typeof(Hero), "GetPerkValue"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Supporter), "IsPlayer"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(DefaultPerks.Charm), "FlexibleEthics"), nameof(Perk));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Crown));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Clans"), nameof(Clans));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(All));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "UnresolvedDecisions"), nameof(Decisions));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.Method(typeof(CampaignTime), "HoursFromNow"), nameof(Now));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "ValidRealm"), nameof(Yes));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "Eligible"), nameof(Yes));
            Patch(AccessTools.Method(Mod.GetType("BellumCivile.NobleClanEligibilityHelper"), "IsLiveNobleClan"), nameof(Yes));
            Patch(AccessTools.Method(Mod.GetType("BellumCivile.MarriageAllianceHelper"), "HasMarriageAlliance"), nameof(No));
            Patch(AccessTools.Method(Mod.GetType("BellumCivile.FeudalPoliticalWeightHelper"), "GetHighestHeldTitleRank"), nameof(Rank));
            Patch(AccessTools.Method(typeof(PrivyCouncilBehavior), "GetOfficeHolder"), nameof(Holder));
            Patch(AccessTools.Method(typeof(PrivyCouncilBehavior), "GetOfficeRecord"), nameof(Seat));
            Patch(AccessTools.Method(typeof(PrivyCouncilBehavior), "IsOfficeUnlocked"), nameof(Yes));
            Patch(AccessTools.Method(typeof(PrivyCouncilBehavior), "GetAppointmentCandidatesForVote"), nameof(Candidates));
            Patch(AccessTools.Method(typeof(PrivyCouncilBehavior), "CalculateAppointmentMerit"), nameof(Merit));
            Patch(AccessTools.Method(typeof(PrivyCouncilBehavior), "CalculateAppointmentSupport"), nameof(Support));
            Patch(AccessTools.Method(typeof(PrivyCouncilBehavior), "CalculateCompetence"), nameof(Zero));
            Patch(AccessTools.Method(deliberation.GetType(), "ResolveClan"), nameof(Find));
            Patch(AccessTools.Method(typeof(IdeologyBehavior), "AddDecisionAsModAction"), nameof(Enqueue));
            Patch(AccessTools.Method(Mod.GetType("BellumCivile.BellumCivileLogger"), "Log", new[] { typeof(string) }), nameof(Skip));
            harmony.CreateClassProcessor(Mod.GetType("BellumCivile.Patches.NpcKingdomDecisionSupportBudgetPatch")).Patch();

            check(Shortlist(new[] { _a, _b, _c }).SequenceEqual(new[] { _a, _b, _c }), "Unnamed ballots retain ordinary top-three nominations");
            check(Shortlist(new[] { _a, _b, _c }, _nominee).SequenceEqual(new[] { _nominee, _a, _b }),
                "Fourth-ranked formal nominee receives one slot alongside the two strongest alternatives");
            check(Shortlist(new[] { _a, _nominee, _b }, _nominee).SequenceEqual(new[] { _nominee, _a, _b }),
                "Nominee already shortlisted does not occupy two places");
            check(Shortlist(new[] { _a, _a, null, _b, _c }, _nominee).SequenceEqual(new[] { _nominee, _a, _b }),
                "Null and duplicate nominations cannot consume ballot places");
            check(Shortlist(new Clan[0], _nominee).SequenceEqual(new[] { _nominee, _a, _b }),
                "Empty nominations retain the eligible merit fallback around the formal nominee");
            check(!Shortlist(new[] { _a, _b, _c }, _crown).Contains(_crown), "Ineligible ruler cannot be inserted by sponsorship");
            _eligible.Remove(_nominee);
            check(!Shortlist(new[] { _a, _b, _c }, _nominee).Contains(_nominee), "Ineligible named nominee is never forced onto a ballot");
            _eligible.Add(_nominee);

            var contextType = Mod.GetType("BellumCivile.CourtCouncilSelectionContext");
            var context = Activator.CreateInstance(contextType, BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { _realm }, null);
            List<Clan> Forecast(Clan nominee) => (List<Clan>)AccessTools.Method(contextType, "Shortlist")
                .Invoke(context, new object[] { PrivyCouncilOffice.Marshal, nominee });
            check(Forecast(null).SequenceEqual(new[] { _a, _b, _c }), "AI fixture predicts a natural shortlist excluding the named nominee");
            check(Forecast(_nominee).SequenceEqual(new[] { _nominee, _a, _b }), "AI named-motion forecast reserves the same place as the ballot");
            check(Forecast(_c).SequenceEqual(new[] { _c, _a, _b }) && Forecast(null).SequenceEqual(new[] { _a, _b, _c }),
                "One sponsor's forecast cannot pollute another candidate's cached ranking");

            foreach (bool factionMotion in new[] { false, true })
            {
                agenda.Sponsor = factionMotion ? _a : _crown; agenda.Faction = factionMotion ? faction : null;
                foreach (bool stored in new[] { false, true })
                {
                    _decisions.Clear();
                    check(Fire(stored), "Authorized " + (factionMotion ? "faction" : "Crown") + " motion opens; stored nominations=" + stored);
                    var decision = (PrivyCouncilAppointmentDecision)_decisions.Single();
                    check(Ballot(decision).SequenceEqual(new[] { _nominee, _a, _b }), "Opened ballot retains nominee outside top-three nominations");
                    var native = new KingdomElection(decision).GetSortedDecisionOutcomes();
                    check(native.Count == 3 && native.Cast<PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome>()
                        .Any(o => o.CandidateClan == _nominee) && native.All(o => o.SupporterList.Count == 0),
                        "Native narrowing cannot drop the nominee or manufacture supporter votes");
                }
            }

            agenda.Sponsor = _crown; agenda.Faction = null;
            var restored = Restored();
            check(Ballot(restored).SequenceEqual(new[] { _nominee, _a, _b }), "Old saved shortlist is repaired from its existing agenda identity");
            check(Ballot(Restored(null)).SequenceEqual(new[] { _a, _b, _c }), "Legacy decision without an agenda does not invent sponsorship");
            check(Ballot(Restored("different-motion")).Count == 0, "Unrelated agenda ID cannot lend its nominee to a stale decision");
            check(ReadNominee(agenda.CouncilMotionId, _realm, PrivyCouncilOffice.Marshal, _a) == null
                && ReadNominee(agenda.CouncilMotionId, _realm, PrivyCouncilOffice.Spymaster, _crown) == null
                && ReadNominee(agenda.CouncilMotionId, Blank<Kingdom>(), PrivyCouncilOffice.Marshal, _crown) == null,
                "Nominee lookup requires the exact sponsor, realm, and office");

            check(restored.DetermineSupport(_crown, namedOutcome) == 100 && restored.DetermineSupport(_crown, otherOutcome) == -100,
                "NPC Crown favors its filed nominee over its previous natural preference");
            check(restored.DetermineSupport(_b, namedOutcome) == 10, "Other clans retain independent appointment preferences");
            var outcomes = new MBList<DecisionOutcome> { namedOutcome, otherOutcome };
            _influence = 500;
            check(restored.DetermineSupportOption(new Supporter(_crown), outcomes, out _, false) == null,
                "Crown sponsorship cannot spend a free vote from its protected reserve");
            _influence = 520;
            check(restored.DetermineSupportOption(new Supporter(_crown), outcomes, out var weight, false) == namedOutcome
                && weight == Supporter.SupportWeights.SlightlyFavor
                && restored.GetInfluenceCostOfSupport(_crown, weight) == 20,
                "Funded Crown support selects its nominee and retains the normal 20-influence minimum");
            _player = _crown;
            check(restored.DetermineSupport(_crown, namedOutcome) == 10, "Player sponsor is not assigned an automatic voting preference");
            _player = _nominee; agenda.Sponsor = _a; agenda.Faction = faction;
            var factionDecision = Restored();
            check(factionDecision.DetermineSupport(_a, namedOutcome) == 100, "NPC faction sponsor also favors its filed nominee");
            deliberation.SetCommittedCandidateVote(_realm, PrivyCouncilOffice.Marshal, _a, _b, "bribed");
            var bribedOutcome = new PrivyCouncilAppointmentDecision.PrivyCouncilAppointmentOutcome(_b);
            check(factionDecision.DetermineSupport(_a, bribedOutcome) == 10000
                && factionDecision.DetermineSupport(_a, namedOutcome) == -100,
                "An explicit negotiated pledge still overrides an NPC sponsor's normal preference");
            check(Ballot(factionDecision).Contains(_nominee), "Changing sponsor's vote does not erase the formal nominee's ballot access");

            _eligible.Remove(_nominee);
            check(Ballot(factionDecision).Count == 0 && !factionDecision.IsAllowed(), "Faction's ineligible named nominee cancels the contest, not a silent substitution");
            agenda.Sponsor = _crown; agenda.Faction = null; _player = _crown;
            check(Ballot(restored).Count == 0 && agenda.PreferredCouncilCandidate == _nominee,
                "Player Crown's ineligible nominee is not silently replaced");
            _player = _nominee;
            check(Ballot(restored).Contains(_a) && agenda.PreferredCouncilCandidate == _a && agenda.PaidInfluence == 100,
                "NPC vacancy recovery refreshes eligibility before constructing its ballot without another filing payment");
            _holder = _b;
            check(Ballot(restored).Count == 0, "Changed incumbent still invalidates the old vacancy motion");
            _holder = null; agenda.State = CourtAgendaState.Defeated;
            check(Ballot(restored).Count == 0, "Finished motion cannot reserve a place in a later ballot");

            var saved = typeof(PrivyCouncilAppointmentDecision).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .SelectMany(f => f.GetCustomAttributesData().Where(a => a.AttributeType.Name == "SaveableFieldAttribute"))
                .Select(a => Convert.ToInt32(a.ConstructorArguments[0].Value)).ToList();
            check(saved.SequenceEqual(Enumerable.Range(100, 8)), "Existing decision save fields and IDs remain unchanged");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { old });
        }
    }
}
