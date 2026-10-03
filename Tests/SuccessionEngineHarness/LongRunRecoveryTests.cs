using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

internal static class LongRunRecoveryTests
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

    private static Kingdom _realm, _historical;
    private static Clan _winner, _ruler;
    private static Hero _hero;
    private static FeudalTitleRecord _crown, _political;
    private static bool _destroyed;
    private static IReadOnlyCollection<FeudalTitleRecord> _cluster;
    private static Settlement _fief;
    private static Hero _ward;
    private static int _regencyStarts, _regentReplacements;
    private static bool _install;
    private static bool All(ref MBReadOnlyList<Kingdom> __result)
    { __result = new MBReadOnlyList<Kingdom>(new List<Kingdom> { _realm, _historical }); return false; }
    private static bool Eliminated(Kingdom __instance, ref bool __result)
    { __result = __instance == _historical && _destroyed; return false; }
    private static bool Ruler(ref Clan __result) { __result = _ruler; return false; }
    private static bool Head(ref Hero __result) { __result = _hero; return false; }
    private static bool Alive(ref bool __result) { __result = true; return false; }
    private static bool Title(ref FeudalTitleRecord __result) { __result = _political; return false; }
    private static bool Cluster(ref IReadOnlyCollection<FeudalTitleRecord> __result) { __result = _cluster; return false; }
    private static bool Find(ref Settlement __result) { __result = _fief; return false; }
    private static bool Owner(ref Clan __result) { __result = _winner; return false; }
    private static bool Now(ref CampaignTime __result) { __result = default(CampaignTime); return false; }
    private static bool Laws(ref SuccessionLawSet __result) { __result = new SuccessionLawSet(); return false; }
    private static bool Line(ref List<Hero> __result) { __result = new List<Hero>(); return false; }
    private static bool Regent(ref bool generated, ref Hero __result) { generated = true; __result = _hero; return false; }
    private static bool Resolve(string heroId, ref Hero __result)
    { __result = heroId == _ward.StringId ? _ward : heroId == _hero.StringId ? _hero : null; return false; }
    private static bool Install(ref bool __result) { __result = _install; return false; }
    private static bool Start() { _regencyStarts++; return false; }
    private static bool Replace() { _regentReplacements++; return false; }
    private static bool Skip() => false;

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var old = Campaign.Current;
        var harmony = new Harmony("bellum.test.long_run_recovery");
        void Patch(MethodBase method, string prefix) => harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(LongRunRecoveryTests), prefix));
        var campaign = Blank<Campaign>();
        var manager = new Manager();
        var titles = Blank<FeudalTitleBehavior>();
        var challenges = new SuccessionChallengeBehavior();
        manager.AddBehavior(titles); manager.AddBehavior(challenges);
        _realm = Blank<Kingdom>(); _realm.StringId = "survivor";
        _historical = Blank<Kingdom>(); _historical.StringId = "historical";
        _winner = Blank<Clan>(); _winner.StringId = "original_winner";
        var later = Blank<Clan>(); later.StringId = "later_ruler";
        _hero = Blank<Hero>(); _ruler = _winner;
        var faction = Blank<FactionObject>(); faction.Leader = later;
        var record = new SuccessionChallengeRecord { WarFaction = faction, OutcomeVictor = _winner,
            OutcomeRealm = _realm, WarOutcome = SuccessionChallengeOutcome.Victory };
        AccessTools.Field(typeof(SuccessionChallengeBehavior), "_records").SetValue(challenges,
            new List<SuccessionChallengeRecord> { record });
        _crown = new FeudalTitleRecord("crown", "Crown", FeudalTitleType.Kingdom,
            _winner.StringId, _winner.StringId, "", "", "", 0, 0);
        _political = _crown;
        bool Legalize() => (bool)AccessTools.Method(typeof(SuccessionChallengeBehavior), "LegalizeChallengeVictory")
            .Invoke(challenges, new object[] { faction, _realm });
        bool Dormant() => (bool)AccessTools.Method(typeof(FeudalTitleBehavior), "IsLandlessHistoricalCrown")
            .Invoke(titles, new object[] { _crown });
        try
        {
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            campaign.AddCampaignBehaviorManager(manager);
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Head));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), nameof(Alive));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetKingdomPoliticalTitle"), nameof(Title));
            check(Legalize() && record.EstateResolved && record.WarCrownTransferred,
                "Challenge victory completes for the frozen winner after faction leadership changes");
            _ruler = later; _crown.SetDeFactoHolder(later.StringId); _crown.SetDeJureHolder(later.StringId);
            check(Legalize() && _ruler == later, "Completed victory does not restore an earlier ruler");
            record.EstateResolved = false;
            check(Legalize() && _ruler == later, "Crown receipt permits remaining estate cleanup after a later succession");
            record.WarCrownTransferred = false; record.EstateResolved = false;
            check(!Legalize(), "Uncommitted victory cannot pretend another house's Crown is delivered");
            record.RestoredRealmReady = true;
            check(Legalize() && record.WarCrownTransferred, "Old restored-realm receipt migrates without reinstalling its founder");
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetDeFactoSovereignClan"), nameof(Ruler));
            var resolution = new CivilWarResolutionBehavior();
            var tribunal = AccessTools.Method(typeof(CivilWarResolutionBehavior), "TryQueueChallengeTribunal");
            check((bool)tribunal.Invoke(resolution, new object[] { faction, _realm }) && record.TribunalQueued,
                "Superseded historical victory cannot put the current reigning house on trial");

            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), nameof(All));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "IsEliminated"), nameof(Eliminated));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetTitleAndDescendants",
                new[] { typeof(FeudalTitleRecord), typeof(FeudalHierarchyMode) }), nameof(Cluster));
            Patch(AccessTools.Method(typeof(Settlement), "Find"), nameof(Find));
            Patch(AccessTools.PropertyGetter(typeof(Settlement), "OwnerClan"), nameof(Owner));
            _crown.SetAssociatedKingdom(_historical.StringId);
            _cluster = new[] { _crown }; _destroyed = true; _political = null;
            check(Dormant(), "Destroyed landless Crown is inherited as a dignity, not a new government");
            _political = _crown;
            check(!Dormant(), "Active successor political Crown keeps sovereign protection");
            _political = null; _destroyed = false;
            check(!Dormant(), "Living historical realm keeps sovereign protection");
            _destroyed = true;
            var barony = new FeudalTitleRecord("barony", "Barony", FeudalTitleType.Barony,
                _winner.StringId, _winner.StringId, "", "", "", 0, 0);
            _cluster = new[] { _crown, barony }; _fief = Blank<Settlement>();
            check(!Dormant(), "Crown with real controlled territory is not treated as a landless dignity");
            _fief = null;
            check(!Dormant(), "Unresolved settlement reference cannot bypass sovereign protection");

            var regencies = new RegencyBehavior();
            manager.AddBehavior(new SuccessionYearlySummaryBehavior());
            _ward = Blank<Hero>(); _ward.StringId = "ward";
            _hero.StringId = "regent";
            var predecessor = Blank<Hero>(); predecessor.StringId = "predecessor";
            _regencyStarts = _regentReplacements = 0; _install = true;
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.Method(typeof(RegencyBehavior), "CanUseRegency"), nameof(Alive));
            Patch(AccessTools.Method(typeof(RegencyBehavior), "IsUnderageWard"), nameof(Alive));
            Patch(AccessTools.Method(typeof(RegencyBehavior), "ResolveHero"), nameof(Resolve));
            Patch(AccessTools.Method(typeof(RegencyBehavior), "SelectRegent"), nameof(Regent));
            Patch(AccessTools.Method(typeof(RegencyBehavior), "TryInstallClanLeader"), nameof(Install));
            Patch(AccessTools.Method(typeof(RegencyBehavior), "ShowRegencyStarted"), nameof(Skip));
            Patch(AccessTools.Method(typeof(RegencyBehavior), "DiscardUnusedGeneratedRegent"), nameof(Skip));
            var successionLaws = typeof(RegencyBehavior).Assembly.GetType("BellumCivile.SuccessionLawHelper");
            Patch(AccessTools.Method(successionLaws, "GetLawsForClan", new[] { typeof(Clan) }), nameof(Laws));
            Patch(AccessTools.Method(successionLaws, "GetLegalSuccessionLine",
                new[] { typeof(Clan), typeof(Hero), typeof(SuccessionLawSet) }), nameof(Line));
            Patch(AccessTools.Method(typeof(SuccessionYearlySummaryBehavior), "RecordRegencyStarted"), nameof(Start));
            Patch(AccessTools.Method(typeof(SuccessionYearlySummaryBehavior), "RecordRegentReplaced"), nameof(Replace));
            check(regencies.EnsureCrownHeirRegency(_winner, _ward, predecessor) && _regencyStarts == 1,
                "Crown-heir regency records its successful opening");
            check(regencies.EnsureCrownHeirRegency(_winner, _ward, predecessor) && _regencyStarts == 1,
                "Crown recovery retry does not count the same regency twice");
            _hero = Blank<Hero>(); _hero.StringId = "replacement";
            check(regencies.EnsureCrownHeirRegency(_winner, _ward, predecessor)
                && _regencyStarts == 1 && _regentReplacements == 1,
                "Replacing an existing Crown regent records replacement, not another opening");
            _install = false;
            check(!new RegencyBehavior().EnsureCrownHeirRegency(_winner, _ward, predecessor) && _regencyStarts == 1,
                "Failed regent installation does not increase telemetry");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { old });
        }

        var proposal = new TreatyProposalRecord("war", "a", "b", "a", 23, 20, false);
        bool InYear(float start, float end) => (bool)AccessTools.Method(typeof(WarPeaceYearlySummaryBehavior), "ResolvedInPeriod")
            .Invoke(null, new object[] { proposal, start, end });
        proposal.SetState(TreatyProposalState.Applied, "signed", 25);
        check(!InYear(0, 24) && InYear(24, 48), "Treaty signed next year counts in its resolution year");
        proposal.SetState(TreatyProposalState.Applied, "retry", 49);
        check(proposal.ResolvedDay == 25 && !InYear(48, 72), "Duplicate terminal callback does not move a treaty between years");
        var legacy = new TreatyProposalRecord("old", "a", "b", "a", 0, 0, false);
        legacy.SetState(TreatyProposalState.Applied);
        check(legacy.ResolvedDay == -1, "Legacy terminal proposal does not invent a resolution timestamp");
        foreach (Type type in new[] { typeof(TreatyProposalRecord), typeof(SuccessionChallengeRecord) })
        {
            var ids = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(f => f.GetCustomAttributesData().Where(a => a.AttributeType.Name == "SaveableFieldAttribute"))
                .Select(a => Convert.ToInt32(a.ConstructorArguments[0].Value)).ToList();
            check(ids.Distinct().Count() == ids.Count, type.Name + " keeps unique save field IDs");
        }
    }
}
