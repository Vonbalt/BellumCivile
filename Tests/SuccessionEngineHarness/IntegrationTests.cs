using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.Patches;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

internal static class IntegrationTests
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
    private sealed class Store : IDataStore
    {
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public bool IsLoading { get; set; }
        public bool IsSaving => !IsLoading;
        public bool SyncData<T>(string key, ref T data)
        {
            if (IsSaving) Data[key] = data is IList ? Activator.CreateInstance(data.GetType(), data) : (object)data;
            else data = Data.TryGetValue(key, out var value) ? (T)value : default;
            return true;
        }
    }
    private static readonly Assembly Mod = typeof(BellumIntegration).Assembly;
    private static readonly Dictionary<Hero, float> Ages = new Dictionary<Hero, float>();
    private static readonly Dictionary<Clan, Hero> Leaders = new Dictionary<Clan, Hero>();
    private static readonly List<Hero> Heroes = new List<Hero>();
    private static readonly List<Clan> Clans = new List<Clan>();
    private static Clan _player, _house, _foreign;
    private static Hero _main, _speaker, _notable;
    private static Kingdom _realm, _other;
    private static PartyBase _prison;
    private static bool _prisoner, _enabled = true, _pending = true;
    private static float _influence = 40, _day = 10, _captured = 5;
    private static int _relation = 30;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static object Field(object o, string name) => AccessTools.Field(o.GetType(), name).GetValue(o);
    private static void Set(object o, string name, object value) => AccessTools.Field(o.GetType(), name).SetValue(o, value);
    private static object Call(object o, string name, params object[] args) => AccessTools.Method(o.GetType(), name).Invoke(o, args);
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Skip() => false;
    private static bool Enabled(ref bool __result) { __result = _enabled; return false; }
    private static bool Pending(ref bool __result) { __result = _pending; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool MainHero(ref Hero __result) { __result = _main; return false; }
    private static bool Leader(Clan __instance, ref Hero __result) { Leaders.TryGetValue(__instance, out __result); return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result) { __result = __instance == _foreign ? _other : _realm; return false; }
    private static bool Ruler(Kingdom __instance, ref Clan __result) { __result = __instance == _other ? _foreign : _house; return false; }
    private static bool Age(Hero __instance, ref float __result) { __result = Ages[__instance]; return false; }
    private static bool Child(Hero __instance, ref bool __result) { __result = Ages[__instance] < 18; return false; }
    private static bool Notable(Hero __instance, ref bool __result) { __result = __instance == _notable; return false; }
    private static bool CouncilCandidate(Clan candidate, ref object __result)
    {
        __result = candidate == _player
            ? Activator.CreateInstance(Mod.GetType("BellumCivile.CouncilAppointmentNominationResult"), true) : null;
        return false;
    }
    private static bool Relation(ref int __result) { __result = _relation; return false; }
    private static bool Balance(ref float __result) { __result = _influence; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(_day); return false; }
    private static bool Captured(ref CampaignTime __result) { __result = CampaignTime.Days(_captured); return false; }
    private static bool Prisoner(ref bool __result) { __result = _prisoner; return false; }
    private static bool Prison(ref PartyBase __result) { __result = _prison; return false; }
    private static bool AtWar(ref bool __result) { __result = false; return false; }
    private static bool MapFaction(ref IFaction __result) { __result = _realm; return false; }
    private static bool Perk(ref PerkObject __result) { __result = null; return false; }
    private static bool NoDecisions(ref MBReadOnlyList<KingdomDecision> __result)
    { __result = new MBReadOnlyList<KingdomDecision>(new List<KingdomDecision>()); return false; }
    private static bool FindHero(string heroId, ref Hero __result) { __result = Heroes.FirstOrDefault(h => h.StringId == heroId); return false; }
    private static bool FindClan(string clanId, ref Clan __result) { __result = Clans.FirstOrDefault(c => c.StringId == clanId); return false; }
    private static bool FindRealm(string kingdomId, ref Kingdom __result) { __result = kingdomId == _realm.StringId ? _realm : _other; return false; }

    internal static void Run(Action<bool, string> check)
    {
        var previous = Campaign.Current;
        var previousRelations = DynamicRelationBehavior.Instance;
        var dayTicks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var previousTicks = dayTicks.GetValue(null);
        var harmony = new Harmony("bellum.tests.integration");
        void Patch(MethodBase method, string prefix) => harmony.Patch(method, prefix: new HarmonyMethod(typeof(IntegrationTests), prefix));
        void Getter(Type type, string name, string prefix) => Patch(AccessTools.PropertyGetter(type, name), prefix);
        var manager = new Manager();
        var integration = new BellumIntegrationBehavior();
        var court = new CourtAgendaBehavior();
        var lobby = new ElectionLobbyingBehavior();
        var elections = new ElectiveSuccessionBehavior();
        var policies = new PolicyDeliberationBehavior();
        var fiefs = new FiefDeliberationBehavior();
        var expulsions = new ExpulsionDeliberationBehavior();
        var council = new CouncilAppointmentDeliberationBehavior();
        var hostages = new HostagePactBehavior();
        manager.Items.AddRange(new CampaignBehaviorBase[] { integration, court, lobby, elections, policies, fiefs, expulsions, council, hostages });
        Clan House(string id) { var clan = Blank<Clan>(); clan.StringId = id; Clans.Add(clan); return clan; }
        Hero Person(string id, float age, Clan clan)
        {
            var hero = Blank<Hero>(); hero.StringId = id; Ages[hero] = age; Heroes.Add(hero);
            Set(hero, "_clan", clan); Set(hero, "_children", new MBList<Hero>()); Set(hero, "_heroState", Hero.CharacterStates.Active);
            return hero;
        }
        try
        {
            Ages.Clear(); Leaders.Clear(); Heroes.Clear(); Clans.Clear();
            dayTicks.SetValue(null, 1000L);
            _player = House("player"); _house = House("house"); _foreign = House("foreign");
            _realm = Blank<Kingdom>(); _realm.StringId = "realm"; _other = Blank<Kingdom>(); _other.StringId = "other";
            _main = Person("player", 40, _player); _speaker = Person("speaker", 50, _house);
            var son = Person("son", 25, _house); var daughter = Person("daughter", 22, _foreign);
            var grandchild = Person("grandchild", 2, _house);
            son.Father = daughter.Father = _speaker; grandchild.Father = son;
            Leaders[_player] = _main; Leaders[_house] = _speaker; Leaders[_foreign] = daughter;
            var campaign = Blank<Campaign>();
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            campaign.AddCampaignBehaviorManager(manager);
            Getter(typeof(Clan), "PlayerClan", nameof(Player)); Getter(typeof(Hero), "MainHero", nameof(MainHero));
            Getter(typeof(Clan), "Leader", nameof(Leader)); Getter(typeof(Clan), "Kingdom", nameof(Realm));
            Getter(typeof(Clan), "Influence", nameof(Balance)); Getter(typeof(Kingdom), "RulingClan", nameof(Ruler));
            Getter(typeof(Kingdom), "IsEliminated", nameof(No)); Getter(typeof(Kingdom), "UnresolvedDecisions", nameof(NoDecisions));
            Getter(typeof(Hero), "Age", nameof(Age)); Getter(typeof(Hero), "IsDisabled", nameof(No));
            Getter(typeof(Hero), "IsChild", nameof(Child));
            Getter(typeof(Hero), "IsNotable", nameof(Notable)); Getter(typeof(Hero), "IsLord", nameof(Yes));
            Getter(typeof(CampaignTime), "Now", nameof(Now)); Getter(typeof(Hero), "IsPrisoner", nameof(Prisoner));
            Getter(typeof(Hero), "CaptivityStartTime", nameof(Captured)); Getter(typeof(Hero), "PartyBelongedToAsPrisoner", nameof(Prison));
            Getter(typeof(Settlement), "Party", nameof(Prison)); Getter(typeof(Settlement), "MapFaction", nameof(MapFaction));
            Getter(typeof(DefaultPerks.Charm), "FlexibleEthics", nameof(Perk)); Patch(AccessTools.Method(typeof(Hero), "GetPerkValue"), nameof(No));
            Patch(AccessTools.Method(typeof(Hero), "GetRelation", new[] { typeof(Hero) }), nameof(Relation));
            Patch(AccessTools.Method(typeof(Kingdom), "IsAtWarWith"), nameof(AtWar));
            var options = Mod.GetType("BellumCivile.BellumCivileOptions");
            Getter(options, "EnableWarPeaceLogicRevamp", nameof(Enabled)); Getter(options, "EnableDynamicRelationDrift", nameof(Enabled));
            Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), nameof(Skip));
            Patch(AccessTools.Method(typeof(ElectiveSuccessionBehavior), "Maintain"), nameof(Skip));
            Patch(AccessTools.Method(typeof(ElectiveSuccessionBehavior), "UsesElection"), nameof(Yes));
            Patch(AccessTools.Method(typeof(ElectiveSuccessionBehavior), "InvalidateRelations"), nameof(Skip));
            Patch(AccessTools.Method(typeof(PolicyDeliberationBehavior), "HasPendingVoteForPolicy"), nameof(Pending));
            Patch(AccessTools.Method(typeof(FiefDeliberationBehavior), "HasPendingFiefVoteForSettlement"), nameof(Pending));
            Patch(AccessTools.Method(typeof(ExpulsionDeliberationBehavior), "HasPendingExpulsionForTarget"), nameof(Pending));
            Patch(AccessTools.Method(typeof(CouncilAppointmentDeliberationBehavior), "HasPendingAppointment",
                new[] { typeof(Kingdom), typeof(PrivyCouncilOffice) }), nameof(Pending));
            Patch(AccessTools.Method(Mod.GetType("BellumCivile.CouncilAppointmentNominationHelper"), "ScoreCandidate",
                new[] { typeof(Clan), typeof(Clan), typeof(Kingdom), typeof(PrivyCouncilOffice), typeof(PrivyCouncilBehavior) }), nameof(CouncilCandidate));
            Patch(AccessTools.Method(typeof(PartitionSuccessionBehavior), "ResolveHero"), nameof(FindHero));
            Patch(AccessTools.Method(typeof(PartitionSuccessionBehavior), "ResolveClan"), nameof(FindClan));
            Patch(AccessTools.Method(typeof(PartitionSuccessionBehavior), "ResolveKingdom"), nameof(FindRealm));

            bool offThread = Task.Run(() => { try { BellumIntegration.IsIllegitimate(son); return false; } catch (InvalidOperationException) { return true; } }).Result;
            check(offThread, "Integration rejects background calls before touching campaign state");
            integration.SyncData(new Store { IsLoading = true });
            check(!BellumIntegration.IsIllegitimate(son), "Legacy saves start without legitimacy exclusions");
            var helper = Mod.GetType("BellumCivile.SuccessionLawHelper");
            List<Hero> Line(IEnumerable<Hero> candidates, Hero root) => (List<Hero>)AccessTools.Method(helper, "OrderSuccessionCandidates")
                .Invoke(null, new object[] { candidates, root, new SuccessionLawSet(GenderSuccessionLaw.Equal, HouseSuccessionLaw.Primogeniture), true, true });
            check(Line(new[] { son, daughter, grandchild }, _speaker).First() == son, "Unmarked hereditary ordering unchanged");
            check(BellumIntegration.MarkIllegitimate(son) && BellumIntegration.MarkIllegitimate(son), "Marking is idempotent");
            check(Line(new[] { son, daughter, grandchild }, _speaker).SequenceEqual(new[] { grandchild, daughter }), "Individual exclusion preserves descendant branch and foreign daughter");
            check(Line(new[] { son }, _speaker).Count == 0, "Sole excluded heir is not restored by hereditary fallback");
            check(!BellumIntegration.IsIllegitimate(grandchild) && grandchild.Father == son && son.Father == _speaker,
                "Marking changes neither descendant status nor native parentage");
            var mother = Person("mother", 65, _house); daughter.Mother = mother;
            BellumIntegration.MarkIllegitimate(daughter);
            check(Line(new[] { daughter }, mother).Count == 0, "Maternal hereditary entitlement follows the same exclusion");
            check((bool)AccessTools.Method(typeof(ElectiveSuccessionBehavior), "Candidate").Invoke(null,
                new object[] { daughter, _other, GenderSuccessionLaw.Equal, null }), "Illegitimacy does not remove elective candidacy from an existing house leader");
            BellumIntegration.Legitimize(daughter);
            var inherited = new FeudalClaimRecord("blood", "house", "title", FeudalClaimStrength.Strong,
                "marriage_birthright", son.StringId, "origin", 0, -1, 1, son.StringId);
            var fabricated = new FeudalClaimRecord("fabricated", "house", "title", FeudalClaimStrength.Weak,
                "fabricated", son.StringId, "origin", 0, -1, 0, son.StringId);
            bool Claim(FeudalClaimRecord r) => (bool)AccessTools.Method(typeof(FeudalTitleBehavior), "IsClaimCurrentlyActive").Invoke(null, new object[] { r });
            check(!Claim(inherited) && Claim(fabricated) && inherited.IsActive, "Blood claims are suspended without erasing history or fabricated claims");
            var nativeCandidates = new Dictionary<Hero, int> { [son] = 100, [daughter] = 10 };
            PlayerRegencyCandidatePatch.Postfix(_house, ref nativeCandidates);
            check(!nativeCandidates.ContainsKey(son) && nativeCandidates.ContainsKey(daughter), "Native clan heir list cannot reintroduce excluded candidate");
            var saved = new Store(); integration.SyncData(saved);
            manager.Items.Remove(integration); integration = new BellumIntegrationBehavior(); manager.Items.Insert(0, integration);
            saved.IsLoading = true; integration.SyncData(saved);
            check(BellumIntegration.IsIllegitimate(son), "Legitimacy marker survives behavior save/load contract");
            check(BellumIntegration.Legitimize(son) && BellumIntegration.Legitimize(son)
                && Line(new[] { son, daughter }, _speaker).First() == son && Claim(inherited), "Legitimation restores future inheritance and suspended blood claims");
            check(!BellumIntegration.MarkIllegitimate(null), "Null legitimacy mutation is rejected");

            var allocatedCrown = new CrownAccessionRecord { Realm = _realm, Predecessor = _speaker, Heir = son };
            var allocatedShare = new CrossClanEstateShare { Heir = son, Recipient = _house };
            BellumIntegration.MarkIllegitimate(son);
            check(allocatedCrown.Heir == son && allocatedShare.Heir == son && _house.Leader == _speaker,
                "Marking does not rewrite allocated Crown/property shares or existing house leadership");
            BellumIntegration.Legitimize(son);

            var relations = new DynamicRelationBehavior(); manager.Items.Add(relations);
            check(BellumIntegration.ManagesRelationPair(_speaker, son), "Relation ownership query delegates Bellum's eligible household pairs");
            _notable = Person("notable", 40, null);
            check(BellumIntegration.ManagesRelationPair(_speaker, _notable), "Public relation query includes eligible notables without noble clans");
            _enabled = false;
            check(!BellumIntegration.ManagesRelationPair(_speaker, son), "Disabled relationship memory does not claim another mod's decay");
            _enabled = true;
            check(!BellumIntegration.ManagesRelationPair(son, son) && !BellumIntegration.ManagesRelationPair(null, son),
                "Relation ownership rejects self-pairs and null targets");
            manager.Items.Remove(relations);

            var agenda = new CourtAgendaRecord { Realm = _realm, Sponsor = _house, PolicyId = "policy", SessionDate = CampaignTime.Days(12) };
            ((List<CourtAgendaRecord>)Field(court, "_agendas")).Add(agenda);
            ((List<CourtAgendaRecord>)Field(court, "_recentResults")).Add(agenda);
            var snap = BellumIntegration.GetCourtAgendas(_realm).Single();
            agenda.SessionDate = CampaignTime.Days(15); agenda.State = CourtAgendaState.Passed;
            var after = BellumIntegration.GetCourtAgendas(_realm).Single();
            check(after.Id == snap.Id && after.SessionDay != snap.SessionDay && snap.State == CourtAgendaState.Announced,
                "Agenda IDs survive rescheduling and snapshots do not mutate with source records");
            Set(agenda, "IntegrationId", null); Call(court, "RestoreIntegrationIds");
            string restoredId = BellumIntegration.GetCourtAgendas(_realm).Single().Id; Call(court, "RestoreIntegrationIds");
            check(!string.IsNullOrEmpty(restoredId) && BellumIntegration.GetCourtAgendas(_realm).Single().Id == restoredId,
                "Legacy agenda ID backfill is stable and idempotent");

            var policy = Blank<PolicyObject>(); policy.StringId = "policy";
            _pending = false;
            check(!BellumIntegration.TryPledgePolicyVote(_realm, _house, policy, true, out _), "Closed policy ballot cannot receive an integration pledge");
            _pending = true; _influence = 40;
            check(BellumIntegration.TryPledgePolicyVote(_realm, _house, policy, true, out _) && policies.GetBribedVote(_realm, policy, _house) == 500,
                "Checked policy pledge uses the existing reservation setter");
            check(!BellumIntegration.TryPledgePolicyVote(_realm, _house, policy, false, out _), "Checked pledge cannot overwrite an existing commitment");
            var fief = Blank<Settlement>(); fief.StringId = "fief";
            _influence = 39;
            check(!BellumIntegration.TryPledgeFiefVote(_realm, _house, fief, _player, out _), "Reservation prevents overcommitting influence across ballot types");
            _influence = 40;
            check(BellumIntegration.TryPledgeFiefVote(_realm, _house, fief, _player, out _), "Fief pledge succeeds with enough unreserved influence");
            check(!BellumIntegration.TryPledgeExpulsionVote(_realm, _house, _house, true, out _), "Ruling house cannot be targeted by expulsion pledge");
            check(!BellumIntegration.TryPledgePolicyVote(_realm, _player, policy, true, out _), "Integration does not replace player-controlled votes");
            _influence = 60;
            check(BellumIntegration.TryPledgeExpulsionVote(_realm, _house, _player, false, out _)
                && expulsions.GetBribedVote(_realm, _player, _house) == -500, "Checked expulsion pledge records requested opposition");
            _influence = 79;
            check(!BellumIntegration.TryPledgeCouncilVote(_realm, _house, PrivyCouncilOffice.Marshal, _player, out _),
                "Council pledges respect reservations across all three other ballot types");
            _influence = 80;
            check(BellumIntegration.TryPledgeCouncilVote(_realm, _house, PrivyCouncilOffice.Marshal, _player, out _)
                && !BellumIntegration.TryPledgeCouncilVote(_realm, _house, PrivyCouncilOffice.Marshal, _player, out _),
                "Checked council pledge commits once without overwriting an existing promise");

            var vote = new ElectiveCommitment { Clan = _house, Speaker = _speaker, Source = "natural", Weight = 1,
                Preferences = new List<ElectivePreference> { new ElectivePreference { Candidate = _main, Score = 10 } } };
            var ballot = new ElectiveSuccessionRecord { Realm = _realm, Votes = new List<ElectiveCommitment> { vote } };
            ((List<ElectiveSuccessionRecord>)Field(elections, "_elections")).Add(ballot);
            var terms = (Dictionary<string, CampaignTime>)Field(court, "_nextTerms"); terms[_realm.StringId] = CampaignTime.Days(100);
            _relation = 29;
            check(!BellumIntegration.CanBeginElectionPersuasion(_realm, _speaker, _main, out _), "External appeal shares relation threshold");
            _relation = 30;
            check(BellumIntegration.CanBeginElectionPersuasion(_realm, _speaker, _main, out _)
                && BellumIntegration.CanBeginElectionPersuasion(_realm, _speaker, _main, out _), "Eligibility checks do not spend persuasion attempts");
            check(BellumIntegration.TryBeginElectionPersuasion(_realm, _speaker, _main, out var failed, out _)
                && !BellumIntegration.CompleteElectionPersuasion(failed, false), "Failed appeal does not record a promise");
            check(!BellumIntegration.CanBeginElectionPersuasion(_realm, _speaker, _main, out _), "Failed appeal consumes shared court-term allowance");
            var attemptSave = new Store(); lobby.SyncData(attemptSave); attemptSave.IsLoading = true;
            manager.Items.Remove(lobby); lobby = new ElectionLobbyingBehavior(); lobby.SyncData(attemptSave); manager.Items.Add(lobby);
            check(!BellumIntegration.CanBeginElectionPersuasion(_realm, _speaker, _main, out _), "Shared attempt allowance survives save/load");
            _day = 101; terms[_realm.StringId] = CampaignTime.Days(200);
            check(BellumIntegration.TryBeginElectionPersuasion(_realm, _speaker, _main, out var stale, out _), "Next court term permits another appeal");
            ballot.MandateNumber++;
            check(!BellumIntegration.CompleteElectionPersuasion(stale, true) && vote.Source == "natural", "Changed electoral mandate rejects stale chat result");
            _day = 201; terms[_realm.StringId] = CampaignTime.Days(300);
            check(BellumIntegration.TryBeginElectionPersuasion(_realm, _speaker, _main, out var accepted, out _)
                && BellumIntegration.CompleteElectionPersuasion(accepted, true) && vote.Nominee == _main, "Successful checked appeal records standing election promise");
            check(!BellumIntegration.CompleteElectionPersuasion(accepted, true), "Completed attempt cannot be replayed");
            vote.Source = "natural"; _day = 301; terms[_realm.StringId] = CampaignTime.Days(400);
            check(BellumIntegration.TryBeginElectionPersuasion(_realm, _speaker, _main, out var expired, out _), "New term starts a fresh attempt");
            _day = 400;
            check(!BellumIntegration.CompleteElectionPersuasion(expired, true) && vote.Source == "natural", "Court-term boundary rejects delayed successful result");
            _day = 401; terms[_realm.StringId] = CampaignTime.Days(500);
            BellumIntegration.TryBeginElectionPersuasion(_realm, _speaker, _main, out var closed, out _);
            ballot.Frozen = true;
            check(!BellumIntegration.CompleteElectionPersuasion(closed, true), "Frozen election rejects a chat result from before voting closed");
            ballot.Frozen = false;

            _day = 10; _captured = 5; _prisoner = true; _prison = Blank<PartyBase>();
            var holding = Blank<Settlement>(); holding.StringId = "holding";
            var slot = new TreatyHostageRecord { Hero = son, SupplyingHouse = _house, ReceivingHouse = _foreign,
                Holding = holding, CustodyEstablished = true, CaptivityStartDay = 5 };
            var pact = new HostagePactRecord { Id = "pact", FirstRealm = _realm, SecondRealm = _other,
                FirstHouse = _house, SecondHouse = _foreign, FirstHostage = slot, Phase = HostagePactPhase.Active, EndDay = 50 };
            ((List<HostagePactRecord>)Field(hostages, "_pacts")).Add(pact);
            check(BellumIntegration.GetHostageStatus(son)?.Status == HostageStatus.PeacePledge, "Actual custody and current agreement produce peace-pledge status");
            _day = 51;
            check(BellumIntegration.GetHostageStatus(son)?.Status == HostageStatus.AwaitingDisposition, "Expired agreement is not described as an active peace pledge before maintenance runs");
            pact.Phase = HostagePactPhase.Ended; slot.ActionCompleted = true; slot.Outcome = HostageCustodyOutcome.Retain;
            check(BellumIntegration.GetHostageStatus(son)?.Status == HostageStatus.OrdinaryPrisoner
                && BellumIntegration.GetHostageStatus(son)?.Protected == false, "Retained prisoner is distinguished from protected treaty hostage");
            _captured = 20;
            check(BellumIntegration.GetHostageStatus(son) == null, "Recapture does not inherit obsolete hostage status");
            _prisoner = false;

            var partition = new PartitionSuccessionBehavior();
            var pending = new PendingPartitionSuccessionRecord(_speaker.StringId, _house.StringId, _realm.StringId,
                "fief", "son|daughter", CampaignTime.Now) {
                CrownBatch = new CrownPartitionBatchRecord(),
                EstateShares = new List<CrossClanEstateShare> {
                    new CrossClanEstateShare { Heir = son, Recipient = _house, Primary = true },
                    new CrossClanEstateShare { Heir = daughter, RootTitleId = "secondary" }
                }, CrownPromotions = new List<CrownPartitionPromotionRecord> {
                    new CrownPartitionPromotionRecord { CrownId = "secondary", Founder = _foreign, Heir = daughter }
                }
            };
            Call(partition, "PublishCrownPartition", pending);
            check(BellumIntegration.GetRecentPartitions().Count == 0, "Incomplete Crown batch never publishes a completed inheritance");
            Set(pending.CrownBatch, "Completed", true);
            Call(partition, "PublishCrownPartition", pending); Call(partition, "PublishCrownPartition", pending);
            check(BellumIntegration.GetRecentPartitions().Single().Recipients.Any(r => r.House == _foreign && r.Realm == _other),
                "Committed Crown notification resolves promoted recipient, not incomplete share reference");
            int received = 0;
            BellumIntegration.PartitionCompleted += s => throw new Exception("subscriber failure");
            BellumIntegration.PartitionCompleted += s => received++;
            Call(integration, "DispatchPartitions", 0f); Call(integration, "DispatchPartitions", 0f);
            check(received == 1, "Subscriber failure cannot interrupt other listeners or replay committed inheritance");
            var resultSave = new Store(); integration.SyncData(resultSave);
            manager.Items.Remove(integration); integration = new BellumIntegrationBehavior(); manager.Items.Insert(0, integration);
            resultSave.IsLoading = true; integration.SyncData(resultSave);
            BellumIntegration.PartitionCompleted += s => received++;
            Call(integration, "DispatchPartitions", 0f);
            check(received == 1 && BellumIntegration.GetRecentPartitions().Count == 1, "Completed notification history survives reload without redelivery");
            var snapshot = BellumIntegration.GetRecentPartitions().Single(); pending.EstateShares[0].Heir = daughter;
            check(snapshot.Recipients.First().Heir == son, "Partition snapshots cannot be changed by mutating inheritance records");

            CrossClanEstateRecord Estate(Hero deceased) => new CrossClanEstateRecord { Source = _house, Deceased = deceased,
                Realm = _realm, Completed = true, Shares = new List<CrossClanEstateShare> {
                    new CrossClanEstateShare { Heir = son, Recipient = _house, Primary = true, Completed = true },
                    new CrossClanEstateShare { Heir = daughter, Recipient = _foreign, Completed = true }
                } };
            var cross = Estate(son);
            Call(partition, "PublishCrossClanPartition", cross);
            var undispatched = new Store(); integration.SyncData(undispatched);
            manager.Items.Remove(integration); integration = new BellumIntegrationBehavior(); manager.Items.Insert(0, integration);
            undispatched.IsLoading = true; integration.SyncData(undispatched);
            BellumIntegration.PartitionCompleted += s => received++;
            Call(integration, "DispatchPartitions", 0f);
            check(received == 2, "Save before dispatch preserves a pending committed notification");
            Call(partition, "PublishCrossClanPartition", cross); Call(integration, "DispatchPartitions", 0f);
            check(received == 2, "Cross-clan estate receipt prevents duplicate notification after resume");
            for (int i = 0; i < 105; i++) Call(partition, "PublishCrossClanPartition", Estate(Person("ancestor" + i, 70, _house)));
            Call(integration, "DispatchPartitions", 0f);
            check(BellumIntegration.GetRecentPartitions().Count == 100, "Recent partition history stays bounded after event delivery");
            check(!(bool)Field(integration, "_partitionDispatchPending"), "Idle integration ticks do not scan partition history");
            check(BellumIntegration.GetCourtAgendas(null).Count == 0 && BellumIntegration.GetHostageStatus(null) == null, "Null read targets are harmless");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            AccessTools.Field(typeof(DynamicRelationBehavior), "<Instance>k__BackingField").SetValue(null, previousRelations);
            dayTicks.SetValue(null, previousTicks);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { previous });
        }
    }
}
