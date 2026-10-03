using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;

internal static class CouncilRecoveryTests
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
    private static Clan _crown, _player, _holder;
    private static PrivyCouncilOfficeRecord _seat;
    private static List<Clan> _candidates;
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool Crown(ref Clan __result) { __result = _crown; return false; }
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Holder(ref Clan __result) { __result = _holder; return false; }
    private static bool Seat(ref PrivyCouncilOfficeRecord __result) { __result = _seat; return false; }
    private static bool Candidates(ref IReadOnlyList<Clan> __result) { __result = _candidates; return false; }
    private static bool Merit(ref float __result) { __result = 50; return false; }
    private static bool Decisions(ref MBReadOnlyList<KingdomDecision> __result)
    { __result = new MBReadOnlyList<KingdomDecision>(new List<KingdomDecision>()); return false; }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var old = Campaign.Current;
        var h = new Harmony("bellum.test.council_recovery");
        void Patch(MethodBase m, string name) => h.Patch(m, prefix: new HarmonyMethod(typeof(CouncilRecoveryTests), name));
        var type = typeof(CourtAgendaBehavior);
        var calendar = new CourtAgendaBehavior();
        var manager = new Manager();
        manager.AddBehavior(calendar); manager.AddBehavior(new PrivyCouncilBehavior());
        manager.AddBehavior(new CouncilAppointmentDeliberationBehavior());
        var campaign = Blank<Campaign>();
        var realm = Blank<Kingdom>(); realm.StringId = "council_recovery";
        _crown = Blank<Clan>(); _player = Blank<Clan>(); _holder = null;
        var original = Blank<Clan>(); original.StringId = "original";
        var replacement = Blank<Clan>(); replacement.StringId = "replacement";
        _seat = new PrivyCouncilOfficeRecord(realm.StringId, PrivyCouncilOffice.Marshal, 0);
        _candidates = new List<Clan> { replacement };
        var agenda = new CourtAgendaRecord { Realm = realm, Sponsor = _crown, State = CourtAgendaState.Deliberating,
            CouncilMotionId = "recovery", PreferredCouncilCandidate = original, PaidInfluence = 100,
            ObjectiveData = new CourtObjectiveRecord { Kind = "council_appointment", TargetId = "Marshal", ActionId = "fill" } };
        var agendas = (List<CourtAgendaRecord>)AccessTools.Field(type, "_agendas").GetValue(calendar);
        agendas.Add(agenda);
        string Reason() => (string)AccessTools.Method(type, "CouncilProceedingReason").Invoke(calendar,
            new object[] { "recovery", realm, PrivyCouncilOffice.Marshal, _crown });
        bool Settled() => (bool)AccessTools.Method(type, "IsCouncilOfficeSettled").Invoke(calendar,
            new object[] { realm, PrivyCouncilOffice.Marshal });
        try
        {
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { campaign });
            campaign.AddCampaignBehaviorManager(manager);
            Patch(AccessTools.Method(type, "ValidRealm"), nameof(Yes));
            Patch(AccessTools.Method(type, "Eligible"), nameof(Yes));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Crown));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "UnresolvedDecisions"), nameof(Decisions));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "IsFuture"), nameof(Yes));
            Patch(AccessTools.Method(typeof(PrivyCouncilBehavior), "GetOfficeRecord"), nameof(Seat));
            Patch(AccessTools.Method(typeof(PrivyCouncilBehavior), "GetOfficeHolder"), nameof(Holder));
            Patch(AccessTools.Method(typeof(PrivyCouncilBehavior), "IsOfficeUnlocked"), nameof(Yes));
            Patch(AccessTools.Method(typeof(PrivyCouncilBehavior), "GetAppointmentCandidatesForVote"), nameof(Candidates));
            Patch(AccessTools.Method(typeof(PrivyCouncilBehavior), "CalculateAppointmentMerit"), nameof(Merit));
            check(Reason() == null && agenda.PreferredCouncilCandidate == replacement && agenda.PaidInfluence == 100,
                "NPC Crown vacancy refreshes lost nominee without another payment");
            _candidates.Clear();
            check(Reason() == "council_waiting_for_candidates" && agenda.IsFiled,
                "Empty Crown shortlist retains paid proceeding for bounded waiting");
            _candidates.Add(replacement); agenda.PreferredCouncilCandidate = original; _player = _crown;
            check(Reason() == "council_nominee_ineligible" && agenda.PreferredCouncilCandidate == original,
                "Player Crown nominee is never silently replaced");
            _player = Blank<Clan>(); _holder = replacement;
            check(Reason() == "council_holder_changed", "Changed incumbent invalidates old vacancy authorization");
            _holder = null;
            var until = (Dictionary<string, CampaignTime>)AccessTools.Field(type, "_councilSettledUntil").GetValue(calendar);
            var days = (Dictionary<string, float>)AccessTools.Field(type, "_councilContestedDay").GetValue(calendar);
            string key = realm.StringId + "|Marshal";
            until[key] = default(CampaignTime); days[key] = 10;
            check(Settled() && until.ContainsKey(key), "Legacy cooldown remains untouched while proceeding is active");
            agenda.State = CourtAgendaState.Cancelled;
            check(!Settled() && !until.ContainsKey(key) && !days.ContainsKey(key),
                "Cancelled legacy vacancy clears stale cooldown and contest receipt");
            until[key] = default(CampaignTime); days[key] = 10; _seat.Appoint("incumbent", 5);
            check(Settled() && until.ContainsKey(key), "Occupied office retains completed-contest cooldown");
        }
        finally
        {
            h.UnpatchAll(h.Id);
            AccessTools.PropertySetter(typeof(Campaign), "Current").Invoke(null, new object[] { old });
        }
    }
}
