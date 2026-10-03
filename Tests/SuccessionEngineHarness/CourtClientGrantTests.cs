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
using TaleWorlds.CampaignSystem.Settlements;

internal static class CourtClientGrantTests
{
    private static bool _identity, _eligible, _spend, _throws;
    private static int _payments, _refunds, _transfers, _claimsRemoved;
    private static Clan _owner, _recipient;
    private static FeudalTitleRecord _title;
    private static FeudalTitleBehavior _titles;
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Identity(ref bool __result) { __result = _identity; return false; }
    private static bool Eligible(ref bool __result) { __result = _eligible; return false; }
    private static bool Spend(ref bool __result) { _payments++; __result = _spend; return false; }
    private static bool Refund() { _refunds++; return false; }
    private static bool Skip() => false;
    private static bool Zero(ref int __result) { __result = 0; return false; }
    private static bool Day(ref float __result) { __result = 10; return false; }
    private static bool Title(ref FeudalTitleRecord __result) { __result = _title; return false; }
    private static bool Titles(ref FeudalTitleBehavior __result) { __result = _titles; return false; }
    private static bool Owner(ref Clan __result) { __result = _owner; return false; }
    private static bool NativeTransfer()
    {
        _transfers++;
        if (_throws) throw new InvalidOperationException("Injected client grant failure");
        _owner = _recipient;
        return false;
    }
    private static bool RemoveClaim() { _claimsRemoved++; return false; }
    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(CourtAgendaRecord).Assembly;
        var rules = assembly.GetType("BellumCivile.CourtClientGrantRules");
        var fields = typeof(CourtClientGrantRecord).GetFields(BindingFlags.Instance | BindingFlags.Public);
        var ids = fields.Select(f => f.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SaveableFieldAttribute"))
            .Select(a => Convert.ToInt32(a.ConstructorArguments[0].Value)).ToList();
        check(fields.Length == 12 && ids.Distinct().Count() == fields.Length, "Client grant identities and execution receipts use unique saved fields");
        object Rule(string name, params object[] args) => AccessTools.Method(rules, name).Invoke(null, args);
        check((double)Rule("Admission", -1) == 0 && (double)Rule("Admission", 0) == .01, "Selfish rulers never volunteer client grants; neutral rulers have one-percent admission");
        check(Math.Abs((double)Rule("Admission", 1) - .03) < .00001 && (double)Rule("Admission", 2) == .05, "Generous rulers have three/five-percent admission before agenda competition");
        check((bool)Rule("NpcEligible", 6, true, true, true, true, 0, 0), "Surplus peaceful border grant is eligible");
        for (int gate = 0; gate < 7; gate++)
        {
            object[] args = { 6, true, true, true, true, 0, 0 };
            args[gate] = gate == 0 ? (object)5 : gate < 5 ? false : (object)(-1);
            check(!(bool)Rule("NpcEligible", args), "Client grant NPC gate " + gate + " independently blocks selection");
        }
        for (int existing = 0; existing <= 100; existing++)
            check((int)Rule("Reward", existing) == Math.Min(25, Math.Max(0, 50 - existing)), "Client gratitude cap at existing " + existing);

        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var h = new Harmony("bellum.test.client_grant");
        void Patch(MethodBase m, string name) => h.Patch(m, prefix: new HarmonyMethod(typeof(CourtClientGrantTests), name));
        var behaviorType = typeof(CourtAgendaBehavior);
        var titleSource = assembly.GetType("BellumCivile.CourtTitleGrantObjectiveSource");
        var source = assembly.GetType("BellumCivile.CourtClientGrantObjectiveSource");
        var budget = assembly.GetType("BellumCivile.NpcInfluenceBudgetService");
        CourtAgendaRecord New(CourtAgendaBehavior b, string legal = "crown")
        {
            _identity = _eligible = _spend = true; _throws = false;
            _payments = _refunds = _transfers = _claimsRemoved = 0;
            var crown = Blank<Clan>(); crown.StringId = "crown";
            _recipient = Blank<Clan>(); _recipient.StringId = "client_clan";
            var client = Blank<Kingdom>(); client.StringId = "client";
            _owner = crown;
            _titles = Blank<FeudalTitleBehavior>();
            _title = new FeudalTitleRecord("barony", "Barony", FeudalTitleType.Barony, legal, "crown", "", "", "", 0, 0);
            var a = new CourtAgendaRecord { Realm = Blank<Kingdom>(), Sponsor = crown, Manual = true,
                State = CourtAgendaState.Announced, ObjectiveData = new CourtObjectiveRecord { Kind = "court_client_land_grant" },
                ClientGrant = new CourtClientGrantRecord { Client = client, Grantor = crown, Recipient = _recipient,
                    Fief = Blank<Settlement>(), TitleId = "barony", OldLegal = legal, RelationApplied = true } };
            ((List<CourtAgendaRecord>)AccessTools.Field(behaviorType, "_agendas").GetValue(b)).Add(a);
            return a;
        }
        void Execute(CourtAgendaBehavior b, CourtAgendaRecord a) => AccessTools.Method(behaviorType, "AdvanceClientGrant").Invoke(b, new object[] { a });
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "IsPast"), nameof(Yes));
            Patch(AccessTools.Method(behaviorType, "ClientGrantIdentity"), nameof(Identity));
            Patch(AccessTools.Method(behaviorType, "ReportClientGrant"), nameof(Skip));
            Patch(AccessTools.Method(source, "Eligible"), nameof(Eligible));
            Patch(AccessTools.Method(titleSource, "Title"), nameof(Title));
            Patch(AccessTools.PropertyGetter(titleSource, "Titles"), nameof(Titles));
            Patch(AccessTools.Method(budget, "TrySpend"), nameof(Spend));
            Patch(AccessTools.Method(budget, "Refund"), nameof(Refund));
            Patch(AccessTools.PropertyGetter(typeof(Settlement), "OwnerClan"), nameof(Owner));
            Patch(AccessTools.Method(typeof(ChangeOwnerOfSettlementAction), "ApplyByDefault"), nameof(NativeTransfer));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "GetTitle"), nameof(Title));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "RebuildRuntimeIndexes"), nameof(Skip));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "ReconcileAllDeFactoParents"), nameof(Zero));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "QueueServiceReviewForTitleAndChildren"), nameof(Skip));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "RemoveRedundantClaimsForHolder"), nameof(Skip));
            Patch(AccessTools.Method(typeof(FeudalTitleBehavior), "RemoveClaimsForClanToTitle"), nameof(RemoveClaim));
            Patch(AccessTools.PropertyGetter(typeof(FeudalTitleBehavior), "CurrentDay"), nameof(Day));
            var b = new CourtAgendaBehavior(); var a = New(b); Execute(b, a); Execute(b, a);
            check(a.State == CourtAgendaState.Completed && _payments == 1 && _transfers == 1, "Client grant executes once without duplicate payment");
            check(_title.DeJureHolderClanId == "client_clan" && _title.DeFactoHolderClanId == "client_clan" && _claimsRemoved == 1,
                "Voluntary lawful grant transfers both rights and clears grantor claims");
            b = new CourtAgendaBehavior(); a = New(b, "foreign_owner"); Execute(b, a);
            check(a.State == CourtAgendaState.Completed && _title.DeJureHolderClanId == "foreign_owner" && _claimsRemoved == 0,
                "Practical-only client grant preserves third-party legal ownership");
            b = new CourtAgendaBehavior(); a = New(b); _identity = false; Execute(b, a);
            check(a.State == CourtAgendaState.Cancelled && _payments == 0 && _transfers == 0, "Changed ruler/clientage blocks payment and grant");
            b = new CourtAgendaBehavior(); a = New(b); _eligible = false; Execute(b, a);
            check(a.State == CourtAgendaState.Cancelled && _payments == 0, "Lost eligibility cancels client grant before payment");
            b = new CourtAgendaBehavior(); a = New(b); _spend = false; Execute(b, a);
            check(a.State == CourtAgendaState.Cancelled && _transfers == 0 && _refunds == 0, "Unaffordable grant never transfers or refunds unspent influence");
            b = new CourtAgendaBehavior(); a = New(b); _throws = true; Execute(b, a);
            check(a.IsOngoingObjective && a.ClientGrant.Paid && !BellumTreatyTransferContext.IsTreatyTransfer
                && AccessTools.Field(typeof(FeudalTitleBehavior), "_clientLandGrantInProgress").GetValue(_titles) == null,
                "Interrupted grant is recoverable and clears both transfer guards");
            _throws = false; AccessTools.Method(behaviorType, "MaintainClientGrants").Invoke(b, null);
            check(a.State == CourtAgendaState.Completed && _payments == 1 && _transfers == 2, "Daily recovery completes without second payment");
            b = new CourtAgendaBehavior(); a = New(b); _throws = true; Execute(b, a);
            _throws = false; _eligible = false; Execute(b, a);
            check(a.State == CourtAgendaState.Cancelled && _transfers == 1 && _refunds == 1,
                "A pending vote, occupation, siege or last-fief change also blocks a paid but untransferred retry");
            b = new CourtAgendaBehavior(); a = New(b); _throws = true;
            Execute(b, a); Execute(b, a); Execute(b, a); Execute(b, a);
            check(a.State == CourtAgendaState.Cancelled && _transfers == 3 && _refunds == 1, "Repeated failed grant stops after three attempts and refunds once");
            b = new CourtAgendaBehavior(); a = New(b); a.ClientGrant.TransferAttempted = a.ClientGrant.Paid = true; _owner = _recipient;
            Execute(b, a);
            check(a.State == CourtAgendaState.Completed && _transfers == 0 && _payments == 0 && _title.DeJureHolderClanId == "client_clan",
                "Interrupted post-owner-change grant repairs rights without transferring twice");
            b = new CourtAgendaBehavior(); a = New(b, "foreign_owner");
            a.ClientGrant.TransferAttempted = a.ClientGrant.Paid = true; _owner = _recipient;
            _title.SetDeJureHolder("new_legal_owner"); Execute(b, a);
            check(a.State == CourtAgendaState.Cancelled && _title.DeJureHolderClanId == "new_legal_owner" && _refunds == 0,
                "Recovery never overwrites intervening third-party legal rights or refunds delivered land");
        }
        finally { h.UnpatchAll(h.Id); }
    }
}
