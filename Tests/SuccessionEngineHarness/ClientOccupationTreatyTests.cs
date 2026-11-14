using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

internal static class ClientOccupationTreatyTests
{
    private static readonly Dictionary<string, Kingdom> Realms = new Dictionary<string, Kingdom>();
    private static readonly Dictionary<string, Clan> Clans = new Dictionary<string, Clan>();
    private static readonly Dictionary<Clan, Kingdom> Allegiances = new Dictionary<Clan, Kingdom>();
    private static readonly Dictionary<Settlement, Clan> Owners = new Dictionary<Settlement, Clan>();
    private static readonly Dictionary<Clan, Hero> Leaders = new Dictionary<Clan, Hero>();
    private static readonly HashSet<string> Hostilities = new HashSet<string>();
    private static ClientKingdomBehavior _clients;
    private static MBReadOnlyList<Settlement> _settlements;
    private static MBReadOnlyList<Town> _clanFiefs;
    private static Town _town;
    private static int _transfers, _peaceCalls, _declaredWars;
    private static int _realmListReads;
    private static float _rateDay;
    private static bool _allied;
    private static bool PlayerClan(ref Clan __result) { __result = Clans["b_clan"]; return false; }
    private static Type _contextType;
    private static string Pair(IFaction a, IFaction b) => string.CompareOrdinal(a.StringId, b.StringId) < 0 ? a.StringId + "|" + b.StringId : b.StringId + "|" + a.StringId;
    private static bool Instance(ref ClientKingdomBehavior __result) { __result = _clients; return false; }
    private static bool Resolve(string __0, ref Kingdom __result) { Realms.TryGetValue(__0 ?? "", out __result); return false; }
    private static bool ResolveClan(string __0, ref Clan __result) { Clans.TryGetValue(__0 ?? "", out __result); return false; }
    private static bool ResolveFief(string __0, ref Settlement __result) { __result = _settlements.FirstOrDefault(s => s.StringId == __0); return false; }
    private static bool AllFiefs(ref MBReadOnlyList<Settlement> __result) { __result = _settlements; return false; }
    private static bool ClanFiefs(ref MBReadOnlyList<Town> __result) { __result = _clanFiefs; return false; }
    private static bool AllClans(ref MBReadOnlyList<Clan> __result) { __result = new MBList<Clan>(Clans.Values); return false; }
    private static bool Name(ref TextObject __result) { __result = new TextObject("Fixture"); return false; }
    private static bool RealmStrength(Kingdom __instance, ref float __result)
    { __result = __instance.StringId == "a" ? 100f : 40f; return false; }
    private static bool NegativeRelation(ref int __result) { __result = -10; return false; }
    private static bool Allied(ref bool __result) { __result = _allied; return false; }
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days(_rateDay); return false; }
    private static bool Days(ref double __result) { __result = _rateDay; return false; }
    private static bool AllRealms(ref MBReadOnlyList<Kingdom> __result)
    { _realmListReads++; __result = new MBList<Kingdom>(Realms.Values); return false; }
    private static bool Owner(Settlement __instance, ref Clan __result) { __result = Owners[__instance]; return false; }
    private static bool Allegiance(Clan __instance, ref Kingdom __result) { __result = Allegiances[__instance]; return false; }
    private static bool Leader(Clan __instance, ref Hero __result) { __result = Leaders[__instance]; return false; }
    private static bool RulingClan(Kingdom __instance, ref Clan __result)
    { Clans.TryGetValue(__instance.StringId + "_clan", out __result); return false; }
    private static bool HeroClan(Hero __instance, ref Clan __result)
    { __result = Leaders.FirstOrDefault(p => p.Value == __instance).Key; return false; }
    private static bool True(ref bool __result) { __result = true; return false; }
    private static bool False(ref bool __result) { __result = false; return false; }
    private static bool SkipVoid() => false;
    private static bool Cost(ref int __result) { __result = 20; return false; }
    private static void RefreshCandidates() { }
    private static bool War(Kingdom __instance, IFaction __0, ref bool __result) { __result = Hostilities.Contains(Pair(__instance, __0)); return false; }
    private static bool NativeTransfer(object[] __args)
    {
        var hero = (Hero)__args[0]; var fief = (Settlement)__args[1];
        Owners[fief] = Leaders.First(p => p.Value == hero).Key; _transfers++; return false;
    }
    private static bool Peace(object[] __args)
    { Hostilities.Remove(Pair((IFaction)__args[0], (IFaction)__args[1])); _peaceCalls++; return false; }
    private static bool DeclareWar(object[] __args)
    { Hostilities.Add(Pair((IFaction)__args[0], (IFaction)__args[1])); _declaredWars++; return false; }
    private static void Context(object __instance, WarScoreRecord war)
    {
        if (war.ConflictType != WarScoreConflictType.ForeignWar) return;
        var add = AccessTools.Method(_contextType, "AddClients");
        add.Invoke(__instance, new object[] { _clients, Realms[war.AttackerKingdomId], Realms[war.DefenderKingdomId], 1 });
        add.Invoke(__instance, new object[] { _clients, Realms[war.DefenderKingdomId], Realms[war.AttackerKingdomId], -1 });
    }
    private static bool EmptyTransfers(ref object __result)
    {
        var item = typeof(WarScoreRecord).Assembly.GetType("BellumCivile.TreatyFiefTransferCandidate");
        __result = Activator.CreateInstance(typeof(List<>).MakeGenericType(item)); return false;
    }
    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var assembly = typeof(WarScoreRecord).Assembly;
        _contextType = assembly.GetType("BellumCivile.ClientWarTerritory");
        var draft = assembly.GetType("BellumCivile.TreatyDraftService");
        var h = new Harmony("bellum.test.client_occupation");
        void Patch(MethodBase method, string name) => h.Patch(method, prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), name));
        Realms.Clear(); Clans.Clear(); Allegiances.Clear(); Owners.Clear(); Leaders.Clear(); Hostilities.Clear();
        foreach (string id in new[] { "a", "b", "ac", "bc", "stranger" })
        {
            var realm = Blank<Kingdom>(); realm.StringId = id; Realms[id] = realm;
            var clan = Blank<Clan>(); clan.StringId = id + "_clan"; Clans[clan.StringId] = clan; Allegiances[clan] = realm;
            Leaders[clan] = Blank<Hero>();
        }
        var fief = Blank<Settlement>(); fief.StringId = "fief";
        _settlements = new MBList<Settlement> { fief }; _town = Blank<Town>();
        AccessTools.Field(typeof(Settlement), "Town").SetValue(fief, _town);
        _clients = new ClientKingdomBehavior();
        var records = (List<ClientKingdomRecord>)AccessTools.Field(typeof(ClientKingdomBehavior), "_clients").GetValue(_clients);
        records.Add(new ClientKingdomRecord("ac", "a", 0, false, 0));
        records.Add(new ClientKingdomRecord("bc", "b", 0, false, 0));
        foreach (var a in new[] { "a", "ac" }) foreach (var b in new[] { "b", "bc" }) Hostilities.Add(Pair(Realms[a], Realms[b]));
        WarScoreRecord Record(string owner = "b") => new WarScoreRecord("a|b", "a", "b", 0,
            new[] { new WarScoreFiefSnapshotRecord("fief", owner, owner + "_clan", true, false) });
        TreatyTermRecord Term(string client = "ac", string owner = "ac_clan", string from = "b", string to = "a") =>
            new TreatyTermRecord(TreatyTermType.RecognizeClientOccupation, 20, "fief", fromKingdomId: from,
                toKingdomId: to, wasOccupiedAtDrafting: true, clanId: owner, thirdKingdomId: client);
        TreatyProposalRecord Proposal(params TreatyTermRecord[] terms)
        { var p = new TreatyProposalRecord("a|b", "a", "b", "a", 0, 100, false); p.ReplaceTerms(terms); return p; }
        IList Candidates(WarScoreRecord war, string from = "b", string to = "a") => (IList)AccessTools.Method(_contextType, "Candidates")
            .Invoke(null, new object[] { war, Realms[from], Realms[to] });
        bool Normalize(WarScoreRecord war, TreatyTermRecord[] terms, bool repair, out List<TreatyTermRecord> normalized)
        {
            object[] args = { war, Proposal(terms), Realms["a"], Realms["b"], terms, null, null, repair, false };
            bool result = (bool)AccessTools.Method(draft, "TryValidateAndNormalize").Invoke(null, args);
            normalized = (List<TreatyTermRecord>)args[5]; return result;
        }
        try
        {
            // Exercise the earlier-fixture patch/unpatch order even when run alone.
            var candidates = AccessTools.Method(_contextType, "Candidates");
            Patch(candidates, nameof(RefreshCandidates));
            h.Unpatch(candidates, HarmonyPatchType.All, h.Id);
            Patch(AccessTools.PropertyGetter(typeof(ClientKingdomBehavior), "Instance"), nameof(Instance));
            Patch(AccessTools.Method(typeof(ClientKingdomBehavior), "ResolveKingdom"), nameof(Resolve));
            Patch(AccessTools.Method(typeof(ClientKingdomBehavior), "IsValidPermanentRealm"), nameof(True));
            Patch(AccessTools.Method(_contextType, "Realm"), nameof(Resolve));
            Patch(AccessTools.Method(draft, "ResolveKingdom"), nameof(Resolve));
            Patch(AccessTools.Method(draft, "GetAvailableFiefTransfers"), nameof(EmptyTransfers));
            h.Patch(AccessTools.Constructor(_contextType, new[] { typeof(WarScoreRecord) }), postfix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(Context)));
            Patch(AccessTools.PropertyGetter(typeof(Settlement), "All"), nameof(AllFiefs));
            Patch(AccessTools.PropertyGetter(typeof(Settlement), "Name"), nameof(Name));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "All"), nameof(AllClans));
            Patch(AccessTools.PropertyGetter(typeof(Settlement), "OwnerClan"), nameof(Owner));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Allegiance));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.Method(typeof(Kingdom), "IsAtWarWith"), nameof(War));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.BellumKingdomVisibilityHelper"), "IsTemporaryBellumKingdom"), nameof(False));
            Patch(AccessTools.Method(assembly.GetType("BellumCivile.TreatyTermCostModel"), "GetFiefTransferCost"), nameof(Cost));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "ResolveSettlement"), nameof(ResolveFief));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "ResolveKingdom"), nameof(Resolve));
            Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "ResolveClan"), nameof(ResolveClan));
            Patch(AccessTools.Method(typeof(WarScoreBehavior), "ResolveSettlement"), nameof(ResolveFief));
            Patch(AccessTools.Method(typeof(ChangeOwnerOfSettlementAction), "ApplyByDefault"), nameof(NativeTransfer));
            Patch(AccessTools.Method(typeof(MakePeaceAction), "Apply"), nameof(Peace));
            // Earlier fixtures may have compiled Candidates with native getters inlined.
            // Recompile its real body only after all campaign substitutes are installed.
            Patch(candidates, nameof(RefreshCandidates));
            Owners[fief] = Clans["ac_clan"];
            var war = Record(); var term = Term();
            var facts = assembly.GetType("BellumCivile.DefensivePactCampaignTreatyFacts", true);
            var sides = (IReadOnlyDictionary<string, int>)AccessTools.Method(facts, "CaptureSides")
                .Invoke(null, new object[] { Realms["a"], Realms["b"], _clients });
            check(sides.Count == 4 && sides["a"] == 1 && sides["ac"] == 1 && sides["b"] == -1 && sides["bc"] == -1,
                "Treaty threat recognizes both principals and their participating clients");
            Hostilities.Remove(Pair(Realms["ac"], Realms["b"]));
            sides = (IReadOnlyDictionary<string, int>)AccessTools.Method(facts, "CaptureSides")
                .Invoke(null, new object[] { Realms["a"], Realms["b"], _clients });
            check(!sides.ContainsKey("ac") && sides.ContainsKey("bc") && war.FiefSnapshots.Count == 1,
                "Treaty threat excludes clients outside the war without mutating war snapshots");
            Hostilities.Add(Pair(Realms["ac"], Realms["b"]));
            check((int)TreatyTermType.RecognizeClientOccupation == 17 && (int)TreatyTermType.EnforceRebelDemands == 16, "Client recognition appends saved enum without renumbering old terms");
            check(Candidates(war).Count == 1, "Participating client occupation is available");
            check(Normalize(war, new[] { term }, false, out var normalized) && normalized.Single().WarScoreCost == 20
                && normalized.Single().ToKingdomId == "a" && normalized.Single().ThirdKingdomId == "ac", "Recognition normalization preserves principal budget and client beneficiary");
            check(Proposal(term).UsedWarScore == 20, "Client recognition consumes overlord budget");
            var direct = new TreatyTermRecord(TreatyTermType.TransferFief, 30, "other", fromKingdomId: "b", toKingdomId: "a");
            check(Proposal(term, direct).UsedWarScore == 50, "Direct and client gains consume the same combined budget");
            var imported = new WarScoreRecord("ac|bc", "ac", "bc", 1,
                new[] { new WarScoreFiefSnapshotRecord("client_estate", "bc", "bc_clan", false, true),
                    new WarScoreFiefSnapshotRecord("fief", "ac", "ac_clan", true, false) });
            var context = AccessTools.Constructor(_contextType, new[] { typeof(WarScoreRecord) }).Invoke(new object[] { war });
            AccessTools.Method(_contextType, "ImportSnapshots").Invoke(context, new object[] { new[] { imported } });
            AccessTools.Method(_contextType, "ImportSnapshots").Invoke(context, new object[] { new[] { imported } });
            check(war.FiefSnapshots.Count == 2 && war.GetSnapshot("fief").OwnerKingdomId == "b",
                "Auxiliary snapshot import is idempotent and never overwrites principal history");
            check(war.GetSnapshot("client_estate").OwnerClanId == "bc_clan"
                && !ReferenceEquals(war.GetSnapshot("client_estate"), imported.GetSnapshot("client_estate")),
                "Imported client history preserves owner and uses independent saved snapshot records");
            var gifted = new WarScoreFiefSnapshotRecord("gift", "a", "a_clan", true, false);
            var grant = AccessTools.Method(typeof(WarScoreFiefSnapshotRecord), "RecordVoluntaryGrant");
            grant.Invoke(gifted, new object[] { "a", "a_clan", "ac", "ac_clan" });
            grant.Invoke(gifted, new object[] { "a", "a_clan", "stranger", "stranger_clan" });
            check(gifted.OwnerKingdomId == "ac" && gifted.OwnerClanId == "ac_clan" && gifted.OriginalOwnerKingdomId == "a",
                "Confirmed Crown grant changes restoration owner once without erasing historic realm origin");
            var foreignHistory = Record().GetSnapshot("fief");
            grant.Invoke(foreignHistory, new object[] { "a", "a_clan", "ac", "ac_clan" });
            check(foreignHistory.OwnerKingdomId == "b", "Crown grant cannot rewrite enemy wartime ownership");
            check(!Normalize(war, new[] { term, term }, false, out _), "Duplicate client recognition rejected");
            check(!Normalize(war, new[] { term, new TreatyTermRecord(TreatyTermType.ForceVassalization, 1) }, false, out _), "Annexation cannot double-dispose client-recognized territory");
            Owners[fief] = Clans["stranger_clan"];
            check(Candidates(war).Count == 0 && !Normalize(war, new[] { term }, false, out _), "Third-party owner makes recognition unavailable");
            check(Normalize(war, new[] { term }, true, out normalized) && normalized.Single().Type == TreatyTermType.WhitePeace, "Stale recognition removed through normal draft repair without substituting a recipient");
            Owners[fief] = Clans["ac_clan"];
            var ratifier = assembly.GetType("BellumCivile.TreatyRatificationService");
            object Reasons() => Activator.CreateInstance(typeof(List<>).MakeGenericType(assembly.GetType("BellumCivile.TreatyCouncilReason")));
            object[] utilityArgs = { Reasons(), 0f, war, term, Clans["a_clan"], true };
            AccessTools.Method(ratifier, "ApplyTermUtility").Invoke(null, utilityArgs);
            check((float)utilityArgs[1] == 3f, "Overlord clan receives modest strategic client benefit, not a personal claim reward");
            utilityArgs = new object[] { Reasons(), 0f, war, term, Clans["b_clan"], false };
            AccessTools.Method(ratifier, "ApplyTermUtility").Invoke(null, utilityArgs);
            check((float)utilityArgs[1] == -70f, "Dispossessed lord retains normal occupied-personal-fief opposition");
            var ai = assembly.GetType("BellumCivile.TreatyAiDraftService");
            var candidateType = assembly.GetType("BellumCivile.TreatyAiTermCandidate");
            var core = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(candidateType));
            var optional = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(candidateType));
            var posture = Enum.Parse(assembly.GetType("BellumCivile.TreatyNegotiationPosture"), "LimitedVictory");
            AccessTools.Method(ai, "BuildTerritorialCandidates").Invoke(null,
                new object[] { war, Realms["a"], Realms["b"], posture, false, core, optional });
            check(core.Count == 1 && ((TreatyTermRecord)AccessTools.Property(candidateType, "Term").GetValue(core[0])).Type == TreatyTermType.RecognizeClientOccupation,
                "NPC drafting includes client conquests among core territorial candidates");
            check(!((bool)AccessTools.Method(ai, "CanCombineWithSelectedTerms").Invoke(null, new object[] { term, new[] { term } })),
                "NPC drafting rejects duplicate territorial spending");
            Hostilities.Remove(Pair(Realms["ac"], Realms["b"]));
            check(Candidates(war).Count == 0, "Nonparticipating clients excluded");
            Hostilities.Add(Pair(Realms["ac"], Realms["b"]));
            records.RemoveAt(0);
            check(Candidates(war).Count == 0, "Released client loses eligibility even while still at war");
            records.Insert(0, new ClientKingdomRecord("ac", "a", 0, false, 0));
            Owners[fief] = Clans["bc_clan"];
            check(Candidates(Record("a"), "a", "b").Count == 1, "Enemy client occupations work symmetrically");
            Owners[fief] = Clans["ac_clan"];
            float score = (float)AccessTools.Method(typeof(WarScoreBehavior), "CalculateOccupationScore").Invoke(new WarScoreBehavior(), new object[] { war });
            check(score > 0, "Client capture contributes to main occupation score");
            check((bool)AccessTools.Method(typeof(WarScoreBehavior), "IsFullyOccupied").Invoke(null, new object[] { war, Realms["b"], Realms["a"] }), "Client capture satisfies mixed-side full occupation");
            Owners[fief] = Clans["bc_clan"];
            check((float)AccessTools.Method(typeof(WarScoreBehavior), "CalculateOccupationScore").Invoke(new WarScoreBehavior(), new object[] { Record("a") }) == -score,
                "Enemy client's occupation gives equal opposite score");
            var apply = AccessTools.Method(typeof(ForeignTreatyBehavior), "ApplyTerritorialTerms");
            _transfers = 0; Owners[fief] = Clans["ac_clan"];
            var delivered = new HashSet<TreatyTermRecord>();
            var recognitionProposal = Proposal(term);
            var basisType = assembly.GetType("BellumCivile.DefensivePactLegalBasis", true);
            var parameter = Expression.Parameter(typeof(TreatyTermRecord), "term");
            var basis = Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(TreatyTermRecord), basisType),
                Expression.Constant(Enum.Parse(basisType, "NotEstablished"), basisType), parameter).Compile();
            Func<string, string, bool?> sameSide = (x, y) => (x == "a" || x == "ac") == (y == "a" || y == "ac");
            var snapshotType = assembly.GetType("BellumCivile.DefensivePactTreatySnapshot", true);
            var journalType = assembly.GetType("BellumCivile.DefensivePactTreatyJournal", true);
            var snapshot = AccessTools.Method(snapshotType, "Capture").Invoke(null,
                new object[] { recognitionProposal, war.FiefSnapshots, sameSide, basis });
            var journal = AccessTools.Method(snapshotType, "CreateJournal").Invoke(snapshot, new object[] { recognitionProposal.ProposalId, 0d });
            var directHarmony = new Harmony("bellum.test.retained_direct_occupation");
            try
            {
                directHarmony.Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"),
                    prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(RulingClan)));
                directHarmony.Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"),
                    prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(HeroClan)));
                directHarmony.Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDead"),
                    prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(False)));
                directHarmony.Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "PopulateTreatyTransferGarrison"),
                    prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(SkipVoid)));
                directHarmony.Patch(AccessTools.Method(typeof(ForeignTreatyBehavior), "ReleasePrisonersBundledWithFief"),
                    prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(SkipVoid)));
                directHarmony.Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"),
                    prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(SkipVoid)));
                apply.Invoke(null, new object[] { war, recognitionProposal, Realms["a"], Realms["b"], null, delivered, journal });
                check(_transfers == 0 && Owners[fief] == Clans["ac_clan"] && delivered.Contains(term), "Recognition retains owning client clan and records delivery without transfer or redistribution");
                recognitionProposal.SetState(TreatyProposalState.Applied, "test", 0);
                AccessTools.Method(journalType, "CaptureRates").Invoke(journal, new object[] { 1d, 1d });
                bool sealedJournal = (bool)AccessTools.Method(journalType, "Seal").Invoke(journal, new object[] { recognitionProposal });
                var impact = AccessTools.Method(journalType, "EvaluateDelivered").Invoke(journal, null);
                var impacts = (IDictionary)AccessTools.Property(impact.GetType(), "Impacts").GetValue(impact);
                double gain = (double)AccessTools.Property(impacts["a"].GetType(), "NetChange").GetValue(impacts["a"]);
                check(sealedJournal && gain == 10, "Successful client recognition records its actual threat delivery in the settlement journal");
                delivered.Clear();
                apply.Invoke(null, new object[] { war, Proposal(new TreatyTermRecord(TreatyTermType.WhitePeace, 0)), Realms["a"], Realms["b"], null, delivered, null });
                check(_transfers == 1 && Owners[fief] == Clans["b_clan"] && delivered.Count == 0, "White peace restores client conquest without recording a territorial concession");
                Owners[fief] = Clans["a_clan"];
                var retained = new TreatyTermRecord(TreatyTermType.TransferFief, 20, "fief", fromKingdomId: "b", toKingdomId: "a");
                delivered.Clear();
                apply.Invoke(null, new object[] { war, Proposal(retained), Realms["a"], Realms["b"], null, delivered, null });
                check(Owners[fief] == Clans["a_clan"] && delivered.Contains(retained),
                    "An occupied direct fief retained by treaty records delivery without a second transfer");
                Owners[fief] = Clans["b_clan"];
                delivered.Clear();
                var directProposal = Proposal(retained);
                var directSnapshot = AccessTools.Method(snapshotType, "Capture").Invoke(null,
                    new object[] { directProposal, war.FiefSnapshots, sameSide, basis });
                var directJournal = AccessTools.Method(snapshotType, "CreateJournal").Invoke(directSnapshot, new object[] { directProposal.ProposalId, 0d });
                apply.Invoke(null, new object[] { war, directProposal, Realms["a"], Realms["b"], null, delivered, directJournal });
                check(Owners[fief] == Clans["a_clan"] && delivered.Contains(retained),
                    "A successful direct treaty transfer records delivery after the owner changes");
                directProposal.SetState(TreatyProposalState.Applied, "test", 0);
                AccessTools.Method(journalType, "CaptureRates").Invoke(directJournal, new object[] { 1d, 1d });
                bool directSealed = (bool)AccessTools.Method(journalType, "Seal").Invoke(directJournal, new object[] { directProposal });
                var directImpact = AccessTools.Method(journalType, "EvaluateDelivered").Invoke(directJournal, null);
                var directImpacts = (IDictionary)AccessTools.Property(directImpact.GetType(), "Impacts").GetValue(directImpact);
                check(directSealed && directImpacts.Count == 2, "A confirmed direct fief transfer reaches both sides of the threat journal");
                Owners[fief] = Clans["stranger_clan"];
                delivered.Clear();
                var staleProposal = Proposal(retained);
                var staleSnapshot = AccessTools.Method(snapshotType, "Capture").Invoke(null,
                    new object[] { staleProposal, war.FiefSnapshots, sameSide, basis });
                var staleJournal = AccessTools.Method(snapshotType, "CreateJournal").Invoke(staleSnapshot, new object[] { staleProposal.ProposalId, 0d });
                apply.Invoke(null, new object[] { war, staleProposal, Realms["a"], Realms["b"], null, delivered, staleJournal });
                check(Owners[fief] == Clans["stranger_clan"] && delivered.Count == 0,
                    "A stale direct transfer to a third-party fief is not recorded as delivered");
                staleProposal.SetState(TreatyProposalState.Applied, "test", 0);
                AccessTools.Method(journalType, "CaptureRates").Invoke(staleJournal, new object[] { 1d, 1d });
                bool staleSealed = (bool)AccessTools.Method(journalType, "Seal").Invoke(staleJournal, new object[] { staleProposal });
                var staleImpact = AccessTools.Method(journalType, "EvaluateDelivered").Invoke(staleJournal, null);
                var staleImpacts = (IDictionary)AccessTools.Property(staleImpact.GetType(), "Impacts").GetValue(staleImpact);
                check(staleSealed && staleImpacts.Count == 0, "A failed direct transfer contributes no journaled threat");
            }
            finally { directHarmony.UnpatchAll(directHarmony.Id); }
            var ratesHarmony = new Harmony("bellum.test.defensive_pact_campaign_rates");
            try
            {
                ratesHarmony.Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(Now)));
                ratesHarmony.Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "ToDays"), prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(Days)));
                ratesHarmony.Patch(AccessTools.PropertyGetter(typeof(Settlement), "IsTown"), prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(True)));
                ratesHarmony.Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(HeroClan)));
                ratesHarmony.Patch(AccessTools.PropertyGetter(typeof(Kingdom), "All"), prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(AllRealms)));
                ratesHarmony.Patch(AccessTools.PropertyGetter(typeof(Clan), "Fiefs"), prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(ClanFiefs)));
                AccessTools.Field(typeof(Kingdom), "_fiefsCache").SetValue(Realms["a"], new MBList<Town> { _town, _town });
                AccessTools.Field(typeof(Kingdom), "_fiefsCache").SetValue(Realms["b"], new MBList<Town> { _town });
                var rateBehavior = new DefensivePactThreatBehavior();
                var rateType = typeof(DefensivePactThreatBehavior);
                var addThreat = AccessTools.Method(rateType, "AdjustThreat");
                var getThreat = AccessTools.Method(rateType, "GetThreat");
                addThreat.Invoke(rateBehavior, new object[] { "a", 0d, 1d, 50d });
                addThreat.Invoke(rateBehavior, new object[] { "b", 0d, 1d, 50d });
                AccessTools.Field(rateType, "_ratesInitialized").SetValue(rateBehavior, true);
                _rateDay = 7; Owners[fief] = Clans["a_clan"];
                AccessTools.Method(rateType, "OnSettlementOwnerChanged").Invoke(rateBehavior,
                    new object[] { fief, false, Leaders[Clans["a_clan"]], Leaders[Clans["b_clan"]], null,
                        default(ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail) });
                double aAfter = (double)getThreat.Invoke(rateBehavior, new object[] { "a", 14d });
                double bAfter = (double)getThreat.Invoke(rateBehavior, new object[] { "b", 14d });
                check(Math.Abs(aAfter - (49d - 1d / 1.2d)) < .000001 && Math.Abs(bAfter - (49d - 1d / 1.1d)) < .000001,
                    "A real settlement-owner callback refreshes only both affected realms using cached fief counts");
                _realmListReads = 0;
                AccessTools.Method(rateType, "RefreshCampaignRateSettings").Invoke(rateBehavior, new object[] { 1d, false });
                check(_realmListReads == 0, "An unrelated MCM save does not scan kingdom lists");
                AccessTools.Method(rateType, "RefreshCampaignRateSettings").Invoke(rateBehavior, new object[] { 2d, false });
                aAfter = (double)getThreat.Invoke(rateBehavior, new object[] { "a", 14d });
                bAfter = (double)getThreat.Invoke(rateBehavior, new object[] { "b", 14d });
                check(_realmListReads == 1 && Math.Abs(aAfter - (49d - 2d / 1.2d)) < .000001
                    && Math.Abs(bAfter - (49d - 2d / 1.1d)) < .000001,
                    "A confirmed decay setting change refreshes live threat at the change time using one realm lookup");
                _rateDay = 14; _clanFiefs = new MBList<Town> { _town };
                AccessTools.Field(typeof(Kingdom), "_fiefsCache").SetValue(Realms["a"], new MBList<Town> { _town });
                AccessTools.Field(typeof(Kingdom), "_fiefsCache").SetValue(Realms["b"], new MBList<Town> { _town, _town });
                _realmListReads = 0;
                AccessTools.Method(rateType, "OnClanChangedKingdom").Invoke(rateBehavior,
                    new object[] { Clans["a_clan"], Realms["a"], Realms["b"], default(ChangeKingdomAction.ChangeKingdomActionDetail), false });
                double aLater = (double)getThreat.Invoke(rateBehavior, new object[] { "a", 21d });
                double bLater = (double)getThreat.Invoke(rateBehavior, new object[] { "b", 21d });
                check(_realmListReads == 0 && Math.Abs(aLater - (aAfter - 2d / 1.1d)) < .000001
                    && Math.Abs(bLater - (bAfter - 2d / 1.2d)) < .000001,
                    "A landed clan changing realms refreshes both rates without a global realm scan");
                var rules = assembly.GetType("BellumCivile.DefensivePactThreatRules", true);
                var chancellorEffect = AccessTools.Method(rules, "ChancellorEffect");
                var weeklyDecay = AccessTools.Method(rules, "WeeklyDecay");
                var refresh = AccessTools.Method(rateType, "RefreshRates");
                double CouncilRate(bool active) => (double)weeklyDecay.Invoke(null, new[] { (object)2d, 1,
                    chancellorEffect.Invoke(null, new object[] { active, 50d }) });
                refresh.Invoke(rateBehavior, new object[] { new[] { "a" }, 14d, (Func<string, double>)(_ => CouncilRate(true)) });
                aLater = (double)getThreat.Invoke(rateBehavior, new object[] { "a", 21d });
                check(Math.Abs(aLater - (aAfter - 3d / 1.1d)) < .000001,
                    "An active chancellor's competence increases only the size-adjusted decay from the assignment time");
                refresh.Invoke(rateBehavior, new object[] { new[] { "a" }, 21d, (Func<string, double>)(_ => CouncilRate(false)) });
                double aAfterSuspension = (double)getThreat.Invoke(rateBehavior, new object[] { "a", 28d });
                check(Math.Abs(aAfterSuspension - (aLater - 2d / 1.1d)) < .000001,
                    "Suspending the chancellor settles the bonus interval and restores ordinary decay");
                var breachBehavior = new DefensivePactThreatBehavior();
                AccessTools.Field(rateType, "_ratesInitialized").SetValue(breachBehavior, true);
                var recordBreach = AccessTools.Method(rateType, "RecordPactBreach");
                bool Breach(HostagePactRecord pledge, Kingdom aggressor) => (bool)recordBreach.Invoke(breachBehavior, new object[] { pledge, aggressor });
                _rateDay = 20;
                var brokenPact = new HostagePactRecord { Id = "broken", Phase = HostagePactPhase.Resolving,
                    EndReason = HostagePactEndReason.War, FirstRealm = Realms["a"], SecondRealm = Realms["b"], VoluntaryAggressor = Realms["a"] };
                check(Breach(brokenPact, Realms["a"]) && !Breach(brokenPact, Realms["a"])
                    && Math.Abs((double)getThreat.Invoke(breachBehavior, new object[] { "a", 20d }) - 25d) < .000001,
                    "A voluntary wartime pledge breach adds 25 threat exactly once");
                var betrayal = new HostagePactRecord { Id = "betrayal", Phase = HostagePactPhase.Resolving,
                    EndReason = HostagePactEndReason.Betrayal, FirstRealm = Realms["a"], SecondRealm = Realms["b"],
                    VoluntaryAggressor = Realms["a"], FirstHostage = new TreatyHostageRecord() };
                check(!Breach(betrayal, Realms["a"]), "A cancelled or unsuccessful hostage execution does not add threat");
                betrayal.FirstHostage.ExecutionSucceeded = true;
                check(Breach(betrayal, Realms["a"]) && !Breach(betrayal, Realms["a"])
                    && Math.Abs((double)getThreat.Invoke(breachBehavior, new object[] { "a", 20d }) - 50d) < .000001,
                    "A successful hostage betrayal adds 25 threat exactly once");
                var ordinaryEnd = new HostagePactRecord { Id = "ordinary", Phase = HostagePactPhase.Resolving,
                    EndReason = HostagePactEndReason.EarlyRelease, VoluntaryAggressor = Realms["a"] };
                check(!Breach(ordinaryEnd, Realms["a"]) && !Breach(brokenPact, Realms["b"]),
                    "A normal release or an uninvolved realm is not treated as a breach");
                var legalBasis = assembly.GetType("BellumCivile.DefensivePactLegalBasis", true);
                var recordWar = AccessTools.Method(rateType, "RecordUnjustifiedWar");
                bool WarThreat(string basis, double day, double start) => (bool)recordWar.Invoke(breachBehavior,
                    new[] { (object)Realms["a"], Realms["b"], day, start, Enum.Parse(legalBasis, basis) });
                check(!WarThreat("Established", 20, 20) && !WarThreat("Unknown", 20, 20)
                    && WarThreat("NotEstablished", 20, 20) && !WarThreat("NotEstablished", 20, 20)
                    && Math.Abs((double)getThreat.Invoke(breachBehavior, new object[] { "a", 20d }) - 60d) < .000001,
                    "An unjustified war adds 10 threat once and stacks with separately recorded pledge breaches");
                var exposed = AccessTools.Method(rateType, "RecordExposedSubterfuge");
                bool Exposure(Kingdom actor, Kingdom target, string mission) => (bool)exposed.Invoke(breachBehavior, new object[] { actor, target, mission });
                check(Exposure(Realms["a"], Realms["b"], "mission-1") && !Exposure(Realms["a"], Realms["b"], "mission-1")
                    && !Exposure(Realms["a"], Realms["a"], "mission-2")
                    && Math.Abs((double)getThreat.Invoke(breachBehavior, new object[] { "a", 20d }) - 62.5d) < .000001,
                    "An exposed foreign mission adds 2.5 threat once, while domestic interference adds none");
                check(!WarThreat("NotEstablished", 20, 21) && WarThreat("NotEstablished", 21, 21),
                    "A future war start is rejected and a later distinct war receives its own receipt");
                _allied = false;
                ratesHarmony.Patch(AccessTools.Method(typeof(Kingdom), "IsAllyWith"), prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(Allied)));
                ratesHarmony.Patch(AccessTools.Method(typeof(DeclareWarAction), "ApplyByDefault"), prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(DeclareWar)));
                ratesHarmony.Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(RulingClan)));
                ratesHarmony.Patch(AccessTools.PropertyGetter(typeof(Kingdom), "CurrentTotalStrength"), prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(RealmStrength)));
                ratesHarmony.Patch(AccessTools.PropertyGetter(typeof(Hero), "IsAlive"), prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(True)));
                ratesHarmony.Patch(AccessTools.Method(typeof(Hero), "GetRelation", new[] { typeof(Hero) }), prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(NegativeRelation)));
                var wars = new MBList<IFaction>();
                AccessTools.Field(typeof(Kingdom), "_factionsAtWarWith").SetValue(Realms["b"], wars);
                var assessmentType = assembly.GetType("BellumCivile.DefensivePactCampaignAssessment", true);
                var assess = AccessTools.Method(assessmentType, "Capture");
                var assessmentPacts = new DefensivePactBehavior();
                var assessmentThreat = new DefensivePactThreatBehavior();
                AccessTools.Field(rateType, "_ratesInitialized").SetValue(assessmentThreat, true);
                addThreat.Invoke(assessmentThreat, new object[] { "a", 0d, 1d, 75d });
                var hostageBehavior = new HostagePactBehavior();
                object Assess(int distance) => assess.Invoke(null, new object[] { Realms["b"], Realms["a"], distance, 0d,
                    assessmentPacts, assessmentThreat, hostageBehavior, _clients });
                bool Eligible(object assessment) => assessment != null && (bool)AccessTools.Property(assessmentType, "IsEligible").GetValue(assessment);
                check(Eligible(Assess(2)) && Eligible(Assess(3)) && !Eligible(Assess(0)) && !Eligible(Assess(-1)),
                    "Live assessment uses the frontier threshold and rejects unknown geography");
                wars.Add(Clans["stranger_clan"]);
                check(Eligible(Assess(2)), "Permanent bandit or minor-clan hostility does not block pact entry");
                wars.Add(Realms["stranger"]);
                check(!Eligible(Assess(2)), "A live war with another kingdom blocks pact entry");
                wars.Clear(); _allied = true;
                check(!Eligible(Assess(2)), "An existing alliance with the target blocks pact entry");
                _allied = false;
                var pledges = (List<HostagePactRecord>)AccessTools.Field(typeof(HostagePactBehavior), "_pacts").GetValue(hostageBehavior);
                pledges.Add(new HostagePactRecord { FirstRealm = Realms["a"], SecondRealm = Realms["b"], Phase = HostagePactPhase.Active });
                check(!Eligible(Assess(2)), "An existing hostage pledge with the target blocks pact entry");
                pledges.Clear();
                records.Add(new ClientKingdomRecord("b", "a", 0, false, 0));
                check(!Eligible(Assess(2)), "A target's client cannot join a defensive pact against its suzerain");
                records.RemoveAt(records.Count - 1);
                var pactBehavior = new DefensivePactBehavior();
                var pactBehaviorType = typeof(DefensivePactBehavior);
                var frontierType = assembly.GetType("BellumCivile.DefensivePactFrontierCache", true);
                var frontier = Activator.CreateInstance(frontierType, BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new object[] { new[] { "a", "b", "stranger" } }, null);
                AccessTools.Method(frontierType, "Step").Invoke(frontier,
                    new object[] { 10, (Func<string, string, bool?>)((first, second) => false) });
                AccessTools.Field(pactBehaviorType, "_frontiers").SetValue(pactBehavior, frontier);
                AccessTools.Method(pactBehaviorType, "OnFrontierOwnerChanged").Invoke(pactBehavior,
                    new object[] { fief, false, Leaders[Clans["a_clan"]], Leaders[Clans["b_clan"]], null,
                        default(ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail) });
                check((int)AccessTools.Property(frontierType, "PendingCount").GetValue(frontier) == 3,
                    "A fief transfer queues only frontier pairs touching the old or new realm");
                var join = AccessTools.Method(pactBehaviorType, "TryJoin");
                object[] JoinArguments(string member) => new object[] { member, "a", 0d, 75d, 50, 2, 40d, 100d, -10, true, false, false, false };
                check((bool)join.Invoke(pactBehavior, JoinArguments("b")) && (bool)join.Invoke(pactBehavior, JoinArguments("stranger")),
                    "Campaign fixture creates an eligible two-member defensive pact");
                AccessTools.Field(pactBehaviorType, "_frontierRealms").SetValue(pactBehavior, new Dictionary<string, Kingdom>(Realms));
                var estimate = AccessTools.Method(pactBehaviorType, "EstimateAdditionalDefensiveStrength");
                float Additional(double day) => (float)estimate.Invoke(pactBehavior,
                    new object[] { Realms["a"], Realms["b"], assessmentThreat, hostageBehavior, _clients, day });
                Hostilities.Remove(Pair(Realms["a"], Realms["b"]));
                Hostilities.Remove(Pair(Realms["a"], Realms["stranger"]));
                Hostilities.Add(Pair(Realms["b"], Realms["stranger"]));
                check(Math.Abs(Additional(0) - 40f) < .001f,
                    "Targeting accounts for another signatory even when the two pact members are at war");
                Hostilities.Add(Pair(Realms["a"], Realms["stranger"]));
                check(Additional(0) == 0f, "An existing enemy is not counted again as a new defensive-pact front");
                Hostilities.Remove(Pair(Realms["a"], Realms["stranger"]));
                check(Additional(400) == 0f, "A pact whose target membership has expired adds no targeting penalty");
                pledges.Add(new HostagePactRecord { FirstRealm = Realms["a"], SecondRealm = Realms["stranger"], Phase = HostagePactPhase.Active });
                check(Additional(0) == 0f, "A protected agreement prevents an ineligible signatory from inflating defensive strength");
                pledges.Clear();
                Hostilities.Add(Pair(Realms["a"], Realms["b"]));
                var maintenance = new DefensivePactBehavior();
                AccessTools.Field(pactBehaviorType, "_frontierRealms").SetValue(maintenance, new Dictionary<string, Kingdom>(Realms));
                var membersByRealm = (System.Collections.IDictionary)AccessTools.Field(pactBehaviorType, "_activeByMember").GetValue(maintenance);
                var checkMember = AccessTools.Method(pactBehaviorType, "CheckMembership");
                void Recheck(double day) => checkMember.Invoke(maintenance,
                    new object[] { "b", membersByRealm["b"], assessmentThreat, hostageBehavior, _clients, day });
                join.Invoke(maintenance, JoinArguments("b"));
                Hostilities.Add(Pair(Realms["b"], Realms["stranger"]));
                Recheck(0);
                check(membersByRealm.Contains("b"), "Membership maintenance does not eject a realm for another war");
                Recheck(400);
                check(!membersByRealm.Contains("b"), "Membership maintenance removes a realm below half its saved joining threshold");
                join.Invoke(maintenance, JoinArguments("b"));
                _allied = true;
                Recheck(0);
                _allied = false;
                check(!membersByRealm.Contains("b"), "Membership maintenance removes a realm that allies with the target");
                var previousCampaign = Campaign.Current;
                var playerHarmony = new Harmony("bellum.test.defensive_pact_player_membership");
                object previousNativeRealm = AccessTools.Field(typeof(Clan), "_kingdom").GetValue(Clans["b_clan"]);
                try
                {
                    playerHarmony.Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(PlayerClan)));
                    playerHarmony.Patch(AccessTools.PropertyGetter(typeof(Clan), "IsUnderMercenaryService"), prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(False)));
                    playerHarmony.Patch(AccessTools.Method(typeof(CivilWarConflictBehavior), "IsRealmTransferPending"), prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(False)));
                    var playerCampaign = Blank<Campaign>();
                    AccessTools.Property(typeof(Campaign), "PlayerDefaultFaction").SetValue(playerCampaign, Clans["b_clan"]);
                    AccessTools.Field(typeof(Clan), "_kingdom").SetValue(Clans["b_clan"], Realms["b"]);
                    AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { playerCampaign });
                    check(Campaign.Current != null && Clan.PlayerClan == Clans["b_clan"]
                        && Clan.PlayerClan.Kingdom == Realms["b"] && !Clan.PlayerClan.IsUnderMercenaryService,
                        "Player membership fixture resolves the player political realm");
                    var playerPact = new DefensivePactBehavior();
                    AccessTools.Field(pactBehaviorType, "_frontierRealms").SetValue(playerPact, new Dictionary<string, Kingdom>(Realms));
                    var playerMembers = (System.Collections.IDictionary)AccessTools.Field(pactBehaviorType, "_activeByMember").GetValue(playerPact);
                    join.Invoke(playerPact, JoinArguments("b"));
                    checkMember.Invoke(playerPact, new object[] { "b", playerMembers["b"], assessmentThreat, hostageBehavior, _clients, 400d });
                    check(playerMembers.Contains("b"), "The player realm stays in the pact after threat falls until a departure vote succeeds");
                    _allied = true;
                    checkMember.Invoke(playerPact, new object[] { "b", playerMembers["b"], assessmentThreat, hostageBehavior, _clients, 0d });
                    _allied = false;
                    check(!playerMembers.Contains("b"), "A conflicting alliance still ends the player realm's membership automatically");
                }
                finally
                {
                    _allied = false;
                    playerHarmony.UnpatchAll(playerHarmony.Id);
                    AccessTools.Field(typeof(Clan), "_kingdom").SetValue(Clans["b_clan"], previousNativeRealm);
                    AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { previousCampaign });
                }
                Hostilities.Remove(Pair(Realms["a"], Realms["stranger"]));
                Hostilities.Add(Pair(Realms["b"], Realms["stranger"]));
                _rateDay = 20; _declaredWars = 0;
                AccessTools.Method(pactBehaviorType, "OnWarDeclared").Invoke(pactBehavior,
                    new object[] { Realms["a"], Realms["b"], DeclareWarAction.DeclareWarDetail.CausedByKingdomDecision });
                AccessTools.Method(pactBehaviorType, "ProcessPendingCalls").Invoke(pactBehavior, null);
                AccessTools.Method(pactBehaviorType, "ProcessPendingCalls").Invoke(pactBehavior, null);
                check(_declaredWars == 1 && Hostilities.Contains(Pair(Realms["a"], Realms["stranger"]))
                    && Hostilities.Contains(Pair(Realms["a"], Realms["b"]))
                    && Hostilities.Contains(Pair(Realms["b"], Realms["stranger"])),
                    "A deliberate attack queues one separate war without ending wars between pact members");
                var endedWarPact = new DefensivePactBehavior();
                check((bool)join.Invoke(endedWarPact, JoinArguments("b"))
                    && (bool)join.Invoke(endedWarPact, JoinArguments("stranger")),
                    "A second pact can test a call delayed beyond the triggering war");
                Hostilities.Remove(Pair(Realms["a"], Realms["stranger"]));
                AccessTools.Method(pactBehaviorType, "OnWarDeclared").Invoke(endedWarPact,
                    new object[] { Realms["a"], Realms["b"], DeclareWarAction.DeclareWarDetail.CausedByKingdomDecision });
                Hostilities.Remove(Pair(Realms["a"], Realms["b"]));
                _declaredWars = 0;
                AccessTools.Method(pactBehaviorType, "ProcessPendingCalls").Invoke(endedWarPact, null);
                check(_declaredWars == 0 && !Hostilities.Contains(Pair(Realms["a"], Realms["stranger"])),
                    "A delayed defensive call cannot start a new war after the original attack has ended");
                Hostilities.Add(Pair(Realms["a"], Realms["b"]));
                var pausedPact = new DefensivePactBehavior();
                check((bool)join.Invoke(pausedPact, JoinArguments("b")) && (bool)join.Invoke(pausedPact, JoinArguments("stranger")),
                    "An existing pact can test the War and Peace setting");
                var toggleHarmony = new Harmony("bellum.test.defensive_pact_toggle");
                try
                {
                    var enabledGetter = AccessTools.PropertyGetter(assembly.GetType("BellumCivile.BellumCivileOptions"), "EnableWarPeaceLogicRevamp");
                    toggleHarmony.Patch(enabledGetter, prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(False)));
                    AccessTools.Method(pactBehaviorType, "OnWarDeclared").Invoke(pausedPact,
                        new object[] { Realms["a"], Realms["b"], DeclareWarAction.DeclareWarDetail.CausedByKingdomDecision });
                    check((bool)AccessTools.Method(pactBehaviorType, "IsMemberAgainst").Invoke(pausedPact, new object[] { "b", "a" })
                        && !Exposure(Realms["a"], Realms["b"], "disabled-mission"),
                        "Disabling the revamp prevents new pact activations and subterfuge threat");
                    toggleHarmony.UnpatchAll(toggleHarmony.Id);
                    AccessTools.Method(pactBehaviorType, "OnWarDeclared").Invoke(pausedPact,
                        new object[] { Realms["a"], Realms["b"], DeclareWarAction.DeclareWarDetail.CausedByKingdomDecision });
                    toggleHarmony.Patch(enabledGetter, prefix: new HarmonyMethod(typeof(ClientOccupationTreatyTests), nameof(False)));
                    _declaredWars = 0;
                    AccessTools.Method(pactBehaviorType, "ProcessPendingCalls").Invoke(pausedPact, null);
                    check(_declaredWars == 0 && !Hostilities.Contains(Pair(Realms["a"], Realms["stranger"])),
                        "Disabling the revamp pauses an already queued call to arms");
                    toggleHarmony.UnpatchAll(toggleHarmony.Id);
                    AccessTools.Method(pactBehaviorType, "ProcessPendingCalls").Invoke(pausedPact, null);
                    check(_declaredWars == 1 && Hostilities.Contains(Pair(Realms["a"], Realms["stranger"])),
                        "A paused call resumes after re-enabling while the triggering war is still active");
                }
                finally { toggleHarmony.UnpatchAll(toggleHarmony.Id); }
                Hostilities.Remove(Pair(Realms["a"], Realms["stranger"]));
                Hostilities.Remove(Pair(Realms["b"], Realms["stranger"]));
            }
            finally { ratesHarmony.UnpatchAll(ratesHarmony.Id); }
            Owners[fief] = Clans["ac_clan"];
            apply.Invoke(null, new object[] { Record("bc"), Proposal(), Realms["a"], Realms["b"], null, delivered, null });
            check(Owners[fief] == Clans["bc_clan"], "Omitted client-versus-client conquest restores original enemy-client house");
            _peaceCalls = 0; Hostilities.Remove(Pair(Realms["a"], Realms["b"]));
            AccessTools.Method(typeof(ClientKingdomBehavior), "OnMakePeace").Invoke(_clients,
                new object[] { Realms["a"], Realms["b"], default(MakePeaceAction.MakePeaceDetail) });
            check(_peaceCalls == 3 && Hostilities.Count == 0 && !_clients.IsSynchronizingDiplomacy, "Peace closes both auxiliary wars and client-client hostility without recursive propagation");
        }
        finally { h.UnpatchAll(h.Id); }
        check(!Harmony.GetAllPatchedMethods().Any(method => Harmony.GetPatchInfo(method)?.Owners.Contains(h.Id) == true),
            "Client occupation fixture removes all owned Harmony patches");
    }
}
