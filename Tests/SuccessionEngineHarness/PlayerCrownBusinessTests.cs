using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class PlayerCrownBusinessTests
{
    private static Clan _player, _ruler;
    private static CampaignTime _now;
    private static bool Player(ref Clan __result) { __result = _player; return false; }
    private static bool Ruler(ref Clan __result) { __result = _ruler; return false; }
    private static bool Now(ref CampaignTime __result) { __result = _now; return false; }
    private static bool Term(ref float __result) { __result = 84; return false; }
    private static bool Cancel(CourtAgendaRecord agenda, string reason) { agenda.SettleCancellation(reason); return false; }

    internal static void Run(Action<bool, string> check)
    {
        T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var harmony = new Harmony("bellum.test.player_crown_business");
        void Patch(MethodBase method, string prefix) => harmony.Patch(method, prefix: new HarmonyMethod(typeof(PlayerCrownBusinessTests), prefix));
        var court = new CourtAgendaBehavior();
        object Call(string name, params object[] args) => AccessTools.Method(typeof(CourtAgendaBehavior), name).Invoke(court, args);
        var agendas = (List<CourtAgendaRecord>)AccessTools.Field(typeof(CourtAgendaBehavior), "_agendas").GetValue(court);
        var dayTicks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var oldTicks = dayTicks.GetValue(null);
        try
        {
            dayTicks.SetValue(null, 1000L);
            _player = Blank<Clan>(); _ruler = _player; var realm = Blank<Kingdom>();
            _now = CampaignTime.Days(100);
            Patch(AccessTools.PropertyGetter(typeof(Clan), "PlayerClan"), nameof(Player));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(Ruler));
            Patch(AccessTools.PropertyGetter(typeof(CampaignTime), "Now"), nameof(Now));
            Patch(AccessTools.Method(typeof(CourtAgendaBehavior), "Cancel"), nameof(Cancel));
            var options = typeof(CourtAgendaBehavior).Assembly.GetType("BellumCivile.BellumCivileOptions");
            Patch(AccessTools.PropertyGetter(options, "CourtTermDays"), nameof(Term));
            var draft = (CourtAgendaRecord)Call("NewPlayerCrownBusiness", realm);
            double Option(string name) => Convert.ToDouble(AccessTools.PropertyGetter(options, name).Invoke(null, null));
            check(draft.Manual && draft.PlayerSelectionConfirmed && draft.HasScheduleSnapshot, "Direct Crown draft has a confirmed, saved schedule identity");
            check(draft.SessionDate.ToDays == 100 && draft.VoteDate.ToDays == 100 + Option("PoliticalDeliberationDays"),
                "Direct Crown vote is dated from the action, not the annual session");
            check(draft.ObjectiveData.DeadlineDay == 100 + Option("CourtTermDays"),
                "Direct initiative retains a bounded duration starting at selection");
            check(agendas.Count == 0, "Opening a preview creates no saved motion or reservation");
            check((bool)Call("IsDirectCrownBusiness", draft), "Direct Crown exemption recognizes the ruling player only");
            _ruler = Blank<Clan>();
            check(!(bool)Call("IsDirectCrownBusiness", draft), "Exemption ends when the player loses the Crown");
            _ruler = _player;
            check(!court.IsNominationOpen(realm), "Player Crown has no annual nomination window");
            var invitation = new CourtAgendaRecord { Realm = realm, Sponsor = _player, State = CourtAgendaState.AwaitingPlayerDecision };
            var selected = new CourtAgendaRecord { Realm = realm, Sponsor = _player, State = CourtAgendaState.Announced,
                PlayerSelectionConfirmed = true, SessionDate = CampaignTime.Days(150), VoteDate = CampaignTime.Days(157) };
            var filed = new CourtAgendaRecord { Realm = realm, Sponsor = _player, State = CourtAgendaState.Deliberating,
                SessionDate = CampaignTime.Days(90), VoteDate = CampaignTime.Days(105), PaidInfluence = 100 };
            var faction = new CourtAgendaRecord { Realm = realm, Sponsor = _player, Faction = Blank<FactionObject>(),
                State = CourtAgendaState.AwaitingPlayerDecision };
            agendas.AddRange(new[] { invitation, selected, filed, faction });
            Call("MigratePlayerCrownBusiness");
            check(invitation.State == CourtAgendaState.Cancelled, "Unanswered annual Crown invitation is retired");
            check(selected.Manual && selected.SessionDate.ToDays == 100 && selected.VoteDate.ToDays == 107,
                "Confirmed legacy Crown selection keeps its deliberation length and begins now");
            check(filed.Manual && filed.VoteDate.ToDays == 105 && filed.PaidInfluence == 100,
                "Migration preserves paid ballot deadline and payment");
            check(!faction.Manual && faction.State == CourtAgendaState.AwaitingPlayerDecision,
                "Faction leader invitations remain annual and unchanged");
            Call("MigratePlayerCrownBusiness");
            check(selected.VoteDate.ToDays == 107 && filed.VoteDate.ToDays == 105, "Migration is idempotent");
            var displayed = (CourtAgendaRecord[])Call("DisplayedPlayerCrownBusiness", realm);
            check(displayed.Length == 2 && displayed[0] == filed && displayed[1] == selected,
                "Crown overview includes concurrent business, prioritizing active ballots");
            var plannedCouncil = new CourtAgendaRecord { Realm = realm, Sponsor = _player, State = CourtAgendaState.Announced,
                ObjectiveData = new CourtObjectiveRecord { Kind = "council_appointment" } };
            agendas.Add(plannedCouncil);
            check(!(bool)Call("HasCouncilReservation", realm, draft), "A future annual council reservation cannot delay a direct royal appointment");
            check((bool)Call("HasCouncilReservation", realm, null), "Future council reservations still coordinate NPC agenda selection");
            plannedCouncil.State = CourtAgendaState.Deliberating;
            check((bool)Call("HasCouncilReservation", realm, draft), "A filed council proceeding still blocks a conflicting direct royal appointment");
        }
        finally { harmony.UnpatchAll(harmony.Id); dayTicks.SetValue(null, oldTicks); _player = _ruler = null; }
    }
}
