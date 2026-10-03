using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    private static Town _town;
    private static int _transfers, _peaceCalls;
    private static Type _contextType;
    private static string Pair(IFaction a, IFaction b) => string.CompareOrdinal(a.StringId, b.StringId) < 0 ? a.StringId + "|" + b.StringId : b.StringId + "|" + a.StringId;
    private static bool Instance(ref ClientKingdomBehavior __result) { __result = _clients; return false; }
    private static bool Resolve(string __0, ref Kingdom __result) { Realms.TryGetValue(__0 ?? "", out __result); return false; }
    private static bool ResolveClan(string __0, ref Clan __result) { Clans.TryGetValue(__0 ?? "", out __result); return false; }
    private static bool ResolveFief(string __0, ref Settlement __result) { __result = _settlements.FirstOrDefault(s => s.StringId == __0); return false; }
    private static bool AllFiefs(ref MBReadOnlyList<Settlement> __result) { __result = _settlements; return false; }
    private static bool AllClans(ref MBReadOnlyList<Clan> __result) { __result = new MBList<Clan>(Clans.Values); return false; }
    private static bool Name(ref TextObject __result) { __result = new TextObject("Fixture"); return false; }
    private static bool Owner(Settlement __instance, ref Clan __result) { __result = Owners[__instance]; return false; }
    private static bool Allegiance(Clan __instance, ref Kingdom __result) { __result = Allegiances[__instance]; return false; }
    private static bool Leader(Clan __instance, ref Hero __result) { __result = Leaders[__instance]; return false; }
    private static bool True(ref bool __result) { __result = true; return false; }
    private static bool False(ref bool __result) { __result = false; return false; }
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
            apply.Invoke(null, new object[] { war, Proposal(term), Realms["a"], Realms["b"], null });
            check(_transfers == 0 && Owners[fief] == Clans["ac_clan"], "Recognition retains owning client clan without transfer or redistribution");
            apply.Invoke(null, new object[] { war, Proposal(new TreatyTermRecord(TreatyTermType.WhitePeace, 0)), Realms["a"], Realms["b"], null });
            check(_transfers == 1 && Owners[fief] == Clans["b_clan"], "White peace restores client conquest to original owning house");
            Owners[fief] = Clans["ac_clan"];
            apply.Invoke(null, new object[] { Record("bc"), Proposal(), Realms["a"], Realms["b"], null });
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
