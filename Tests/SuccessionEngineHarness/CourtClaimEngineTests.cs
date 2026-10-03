using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

internal static class CourtClaimEngineTests
{
    private static readonly Assembly Mod = typeof(CourtAgendaRecord).Assembly;
    private static double _day;
    private static bool _pending, _settled, _validClaim, _support;
    private static Clan _owner, _claimant, _foreignClan;
    private static Kingdom _realm, _foreignRealm;
    private static bool _includeClaim;
    private static bool True(ref bool __result) { __result = true; return false; }
    private static bool Skip() => false;
    private static bool Now(ref CampaignTime __result) { __result = CampaignTime.Days((float)_day); return false; }
    private static bool Days(ref double __result) { __result = _day; return false; }
    private static bool Owner(ref Clan __result) { __result = _owner; return false; }
    private static bool Realm(Clan __instance, ref Kingdom __result) { __result = __instance == _foreignClan ? _foreignRealm : _realm; return false; }
    private static bool Pending(ref bool __result) { __result = _pending; return false; }
    private static bool Settled(ref bool __result) { __result = _settled && !_pending && _owner == _claimant; return false; }
    private static bool Valid(ref bool __result) { __result = _validClaim; return false; }
    private static bool Support(ref bool __result) { __result = _support; return false; }
    private static bool HeroClan(ref Clan __result) { __result = _foreignClan; return false; }
    private static bool Empty(MethodBase __originalMethod, ref object __result)
    {
        var item = ((MethodInfo)__originalMethod).ReturnType.GetGenericArguments()[0];
        __result = Activator.CreateInstance(typeof(List<>).MakeGenericType(item));
        return false;
    }
    private static bool NoStructural(ref object __result) { __result = null; return false; }
    private static bool Territory(object[] __args)
    {
        object Candidate(string id, float priority) => Activator.CreateInstance(Mod.GetType("BellumCivile.TreatyAiTermCandidate"),
            new TreatyTermRecord(TreatyTermType.TransferFief, 40, id, fromKingdomId: "enemy", toKingdomId: "realm"), priority, id);
        ((IList)__args[5]).Add(Candidate("ordinary", 150));
        if (_includeClaim) ((IList)__args[6]).Add(Candidate("named", 30));
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        _realm = Blank<Kingdom>(); _realm.StringId = "claim_realm";
        _foreignRealm = Blank<Kingdom>(); _foreignRealm.StringId = "claim_target";
        _claimant = Blank<Clan>(); _claimant.StringId = "beneficiary";
        _foreignClan = Blank<Clan>(); _foreignClan.StringId = "foreign_house";
        var ruler = Blank<Clan>(); ruler.StringId = "custodian";
        var fief = Blank<Settlement>(); fief.StringId = "claim_fief";
        var behaviorType = typeof(CourtAgendaBehavior);
        var maintain = AccessTools.Method(behaviorType, "MaintainClaimObjectives");
        var bonus = AccessTools.Method(behaviorType, "ClaimAllocationBonus");
        var wars = AccessTools.Method(behaviorType, "ClaimWarBonus");
        List<CourtAgendaRecord> Records(CourtAgendaBehavior b, string name) => (List<CourtAgendaRecord>)AccessTools.Field(behaviorType, name).GetValue(b);
        CourtAgendaRecord Add(CourtAgendaBehavior b)
        {
            _day = 80; _pending = true; _settled = false; _validClaim = _support = true; _owner = ruler;
            var faction = Blank<FactionObject>();
            var a = new CourtAgendaRecord { Realm = _realm, Faction = faction, State = CourtAgendaState.PursuingObjective,
                ObjectiveData = new CourtObjectiveRecord { Kind = "court_restore_claim" }, Claim = new CourtClaimRecord {
                    Fief = fief, Beneficiary = _claimant, Target = _foreignRealm, ClaimTitleId = "barony", Activated = true,
                    ActivatedDay = 20, AcquiredDay = 80, PendingAllocation = true, GraceDays = 3, LastOwnerChangeDay = 80 } };
            AccessTools.Method(typeof(CourtObjectiveRecord), "FreezeTerm").Invoke(a.ObjectiveData, new object[] { 10d, 84d });
            Records(b, "_agendas").Add(a);
            return a;
        }
        void Tick(CourtAgendaBehavior b) => maintain.Invoke(b, null);
        float Bonus(CourtAgendaBehavior b, Clan candidate = null, Settlement holding = null) => (float)bonus.Invoke(b,
            new object[] { _realm, holding ?? fief, candidate ?? _claimant, ruler });
        var harmony = new Harmony("bellum.test.court_claim");
        void Patch(MethodBase original, string method) => harmony.Patch(original, prefix: new HarmonyMethod(typeof(CourtClaimEngineTests), method));
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "ToDays"), nameof(Days));
            Patch(AccessTools.PropertyGetter(typeof(Settlement), "OwnerClan"), nameof(Owner));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Kingdom"), nameof(Realm));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "Clan"), nameof(HeroClan));
            Patch(AccessTools.Method(behaviorType, "ClaimOwnerValid"), nameof(True));
            Patch(AccessTools.Method(behaviorType, "ClaimBeneficiaryValid"), nameof(True));
            Patch(AccessTools.Method(behaviorType, "ReceivesPoliticalSupport"), nameof(Support));
            Patch(AccessTools.Method(behaviorType, "SettledClaimOwnership"), nameof(Settled));
            Patch(AccessTools.Method(behaviorType, "ReportClaim"), nameof(Skip));
            Patch(AccessTools.Method(Mod.GetType("BellumCivile.CourtClaimObjectiveSource"), "ValidClaim"), nameof(Valid));
            Patch(AccessTools.Method(Mod.GetType("BellumCivile.CourtCampaignObjectiveSource"), "ValidPair"), nameof(True));
            Patch(AccessTools.Method(typeof(FiefDeliberationBehavior), "IsAwaitingAllocation"), nameof(Pending));
            Patch(AccessTools.Method(typeof(WarPeaceRevampBehavior), "IsRevampEnabled"), nameof(True));
            Patch(AccessTools.Method(typeof(WarPeaceRevampBehavior), "CanSupportCourtCampaign"), nameof(True));

            var b = new CourtAgendaBehavior(); var a = Add(b);
            check(Bonus(b) == 15 && Bonus(b, ruler) == 0 && Bonus(b, holding: Blank<Settlement>()) == 0,
                $"Allocation bonus matches exact house and holding ({Bonus(b)}/{Bonus(b, ruler)}, time={CampaignTime.Now.ToDays}, ongoing={a.IsOngoingObjective})");
            _support = false;
            check(Bonus(b) == 0, "Nonparticipating voter receives no claim allocation bonus");
            _support = true; _owner = _foreignClan;
            check((float)wars.Invoke(b, new object[] { _realm, _foreignRealm, ruler }) == 15 && Bonus(b) == 0, "War assistance is distinct from domestic allocation");
            _owner = _claimant; _pending = true; _settled = true;
            Tick(b);
            check(!a.ResultApplied, "Capturer custody cannot satisfy a pledge while allocation is pending");
            _pending = false; _validClaim = false;
            Tick(b);
            check(a.State == CourtAgendaState.Completed && a.Faction.Mood == 10, "Dated settled ownership succeeds before a consumed claim is invalidated");
            Tick(b);
            check(a.Faction.Mood == 10, "Repeated maintenance cannot duplicate mood reward");

            b = new CourtAgendaBehavior(); a = Add(b); _day = 85;
            Tick(b);
            check(a.Claim.GraceActive && a.Claim.GraceUntil == 87 && Records(b, "_agendas").Count == 0
                && Records(b, "_claimGrace").Count == 1, "Pending allocation enters fixed grace outside next-term agenda list");
            check(Bonus(b) == 15 && (float)wars.Invoke(b, new object[] { _realm, _foreignRealm, ruler }) == 0, "Grace preserves allocation support only");
            Tick(b);
            check(a.Claim.GraceUntil == 87 && Records(b, "_claimGrace").Count == 1, "Retry does not extend or duplicate grace");
            _owner = _claimant;
            AccessTools.Method(behaviorType, "OnCourtClaimAllocationResolved").Invoke(b, new object[] { fief, _claimant });
            _day = 86; _pending = false; _settled = true; _validClaim = false;
            Tick(b);
            check(a.State == CourtAgendaState.Completed && Records(b, "_claimGrace").Count == 0, "Verified award completes grace once native decision cleanup finishes");

            b = new CourtAgendaBehavior(); a = Add(b); _day = 85; Tick(b);
            _pending = false; Tick(b);
            check(a.ObjectiveData.State == CourtObjectiveState.Expired && a.Faction.Mood == -10, "Award to another house ends grace without automatic revocation");

            b = new CourtAgendaBehavior(); a = Add(b); _day = 85;
            AccessTools.Method(behaviorType, "OnCourtClaimAllocationResolved").Invoke(b, new object[] { fief, ruler });
            _owner = _claimant; _pending = false; _settled = true; Tick(b);
            check(a.ObjectiveData.State == CourtObjectiveState.Expired, "Wrong-house award seals failure even before grace maintenance or a later same-hour transfer");

            b = new CourtAgendaBehavior(); a = Add(b); a.Claim.AcquiredDay = -1; a.Claim.PendingAllocation = false; _day = 85; Tick(b);
            check(a.ObjectiveData.State == CourtObjectiveState.Expired, "Post-term ownership observation cannot invent in-term conquest");

            b = new CourtAgendaBehavior(); a = Add(b); _day = 88; Tick(b);
            check(a.ObjectiveData.State == CourtObjectiveState.Expired && !a.Claim.GraceActive, "Delayed maintenance cannot grant a fresh grace window");

            b = new CourtAgendaBehavior(); a = Add(b); _day = 85; Tick(b);
            _owner = _foreignClan;
            AccessTools.Method(behaviorType, "OnCourtClaimOwnerChanged").Invoke(b, new object[] {
                fief, true, Blank<Hero>(), null, null, default(ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail) });
            _owner = _claimant; _pending = false; _settled = true; a.Claim.LastOwnerChangeDay = 86; _day = 86; Tick(b);
            check(a.ObjectiveData.State == CourtObjectiveState.Expired, "Loss during grace seals failure before a same-hour recapture can revive it");

            b = new CourtAgendaBehavior(); a = Add(b); _validClaim = false; Tick(b);
            check(a.ObjectiveData.State == CourtObjectiveState.Cancelled && a.Faction.Mood == 0, "Invalid unrelated claim cancels without punishment");
        }
        finally { harmony.UnpatchAll("bellum.test.court_claim"); }

        var preferenceType = Mod.GetType("BellumCivile.CourtTreatyDraftPreference");
        var preference = Activator.CreateInstance(preferenceType, BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { TreatyTermType.TransferFief, "enemy", "realm", 0f, 80f, "claim", "named" }, null);
        float Score(params TreatyTermRecord[] terms) => (float)AccessTools.Method(preferenceType, "Score").Invoke(preference, new object[] { terms });
        var match = new TreatyTermRecord(TreatyTermType.TransferFief, 20, "named", fromKingdomId: "enemy", toKingdomId: "realm");
        check(Score(match) == 80 && Score(match, match) == 80, "Named fief package preference applies once");
        check(Score(new TreatyTermRecord(TreatyTermType.TransferFief, 20, "other", fromKingdomId: "enemy", toKingdomId: "realm")) == 0,
            "Treaty with another fief does not receive named claim preference");
        check(Score(new TreatyTermRecord(TreatyTermType.TransferFief, 20, "named", fromKingdomId: "realm", toKingdomId: "enemy")) == 0,
            "Ceding the claimed fief to the enemy is not fulfillment");

        var service = Mod.GetType("BellumCivile.TreatyAiDraftService");
        _realm.StringId = "realm"; _foreignRealm.StringId = "enemy";
        var war = new WarScoreRecord("realm|enemy", "realm", "enemy", 10, null);
        var proposal = new TreatyProposalRecord(war.WarKey, "realm", "enemy", "realm", 20, 80, false);
        object Alternative(int spend) => AccessTools.Method(service, "BuildObjectiveDraft").Invoke(null,
            new object[] { war, proposal, _realm, _foreignRealm, _realm, spend, preference });
        IReadOnlyList<TreatyTermRecord> Terms(object draft) => (IReadOnlyList<TreatyTermRecord>)AccessTools.Property(draft.GetType(), "Terms").GetValue(draft);
        try
        {
            Patch(AccessTools.Method(service, "BuildTerritorialCandidates"), nameof(Territory));
            Patch(AccessTools.Method(service, "GetStructuralSettlement"), nameof(NoStructural));
            foreach (string method in new[] { "BuildPrisonerCandidates", "BuildPoliticalCandidates", "ResolveBundledPrisoners" })
                Patch(AccessTools.Method(service, method), nameof(Empty));
            foreach (string method in new[] { "AddWealthTerms", "AddReciprocalPrisonerOfferings" })
                Patch(AccessTools.Method(service, method), nameof(Skip));
            _includeClaim = true;
            var ordinary = AccessTools.Method(service, "BuildDraft").Invoke(null,
                new object[] { war, proposal, _realm, _foreignRealm, _realm, 40, true });
            check(Terms(ordinary).Single().SettlementId == "ordinary", "Ordinary draft retains its higher-priority territorial choice");
            var named = Alternative(40);
            check(Terms(named).Single().SettlementId == "named", "Objective draft seeds the lower-priority named fief within the same budget");
            check(Alternative(39) == null, "Named alternative cannot exceed desired spend");
            var full = Terms(Alternative(200));
            check(full.Count == 2 && full[0].SettlementId == "named" && full.Sum(t => t.WarScoreCost) == 80,
                "Named seed and remaining ordinary demands share the real budget without duplicate fiefs");
            _includeClaim = false;
            check(Alternative(80) == null, "Unavailable legal territorial candidate cannot be manufactured by the objective");
        }
        finally { harmony.UnpatchAll("bellum.test.court_claim"); }
    }
}
