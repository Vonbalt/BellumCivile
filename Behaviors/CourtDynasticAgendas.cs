using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private static bool IsDynastic(CourtAgendaRecord a) => a?.ObjectiveData?.Kind == CourtDynasticRules.Kind;
        private static TextObject DynasticLabel(Kingdom target) => new TextObject("{=BC_CourtDynasticLabel}Strengthen ties with {REALM} through marriage")
            .SetTextVariable("REALM", target?.Name ?? TextObject.GetEmpty());
        private static bool DynasticOwnerValid(CourtAgendaRecord a) => a?.Dynastic != null && a.Faction?.Type == FactionType.Nobility
            && a.Faction.ParentKingdom == a.Realm && CourtDynasticObjectiveSource.RealmPair(a.Realm, a.Dynastic.Target)
            && a.Realm.RulingClan == a.Dynastic.OurHouse && a.Dynastic.Target.RulingClan == a.Dynastic.TheirHouse
            && Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionsInKingdom(a.Realm).Contains(a.Faction) == true;
        private static bool DynasticWindow(CourtAgendaRecord a) => a.ObjectiveData.HasTermSnapshot
            && a.Dynastic.InTerm(CampaignTime.Now.ToDays, a.ObjectiveData.DeadlineDay);
        private static bool DynasticUnfinished(CourtAgendaRecord a) => IsDynastic(a) && a.Dynastic != null && !a.ResultApplied
            && (a.State == CourtAgendaState.Announced || a.IsOngoingObjective);
        private static bool DynasticMarried(CourtDynasticRecord p) => p.First?.IsAlive == true && p.Second?.IsAlive == true
            && p.First.Spouse == p.Second && p.Second.Spouse == p.First && p.First.Clan == p.Destination && p.Second.Clan == p.Destination;
        private static bool DynasticIdentity(CourtDynasticRecord p) => p.First?.IsAlive == true && p.Second?.IsAlive == true
            && p.First.Clan == p.OurHouse && p.Second.Clan == p.TheirHouse && p.First.Spouse == null && p.Second.Spouse == null
            && Campaign.Current.Models.MarriageModel.GetClanAfterMarriage(p.First, p.Second) == p.Destination;

        internal bool HasDynasticReservation(Hero first, Hero second, CourtAgendaRecord excluded = null) => _agendas.Any(a => a != excluded && IsDynastic(a) && !a.ResultApplied
            && (a.IsUnopened || a.IsOngoingObjective) && a.Dynastic != null && a.ObjectiveData.HasTermSnapshot
            && CampaignTime.Now.ToDays <= a.ObjectiveData.DeadlineDay
            && (a.Dynastic.First == first || a.Dynastic.Second == first || a.Dynastic.First == second || a.Dynastic.Second == second));

        internal float DynasticMarriageBonus(Hero member, Hero spouse)
        {
            return _agendas.Any(a => DynasticUnfinished(a) && a.IsOngoingObjective && DynasticOwnerValid(a) && DynasticWindow(a)
                && a.Dynastic.Matches(member, spouse) && member?.Clan == a.Dynastic.OurHouse
                && CourtDynasticRules.MayConsider(a.Dynastic.Response) && member.Clan != Clan.PlayerClan
                && GetFavoredBloc(a.Realm) == FactionType.Nobility) ? CourtDynasticRules.MarriageBonus : 0;
        }

        internal float DynasticAllianceBonus(Kingdom realm, Kingdom target, Clan voter)
        {
            return _agendas.Any(a => IsDynastic(a) && a.Realm == realm && a.Dynastic?.Target == target
                && (a.IsOngoingObjective || a.State == CourtAgendaState.Completed) && DynasticOwnerValid(a) && DynasticWindow(a)
                && (a.State == CourtAgendaState.Completed ? DynasticMarried(a.Dynastic) : DynasticIdentity(a.Dynastic))
                && ReceivesPoliticalSupport(a, voter, a.Dynastic.Members)) ? CourtDynasticRules.AllianceBonus : 0;
        }

        internal bool HasDynasticAllianceContext(Kingdom first, Kingdom second) => _agendas.Any(a => IsDynastic(a)
            && ((a.Realm == first && a.Dynastic?.Target == second) || (a.Realm == second && a.Dynastic?.Target == first))
            && (a.IsOngoingObjective || a.State == CourtAgendaState.Completed) && DynasticOwnerValid(a) && DynasticWindow(a));

        private CourtAgendaRecord NpcDynasticAgenda(Clan clan) => _agendas.FirstOrDefault(a => DynasticUnfinished(a)
            && a.IsOngoingObjective && a.Dynastic.OurHouse == clan && clan != Clan.PlayerClan && !a.Dynastic.NpcAttempted
            && CourtDynasticRules.MayConsider(a.Dynastic.Response) && DynasticOwnerValid(a) && DynasticWindow(a));

        internal void OnCourtMarriageCompleted(Hero first, Hero second)
        {
            foreach (var a in _agendas.Where(a => DynasticUnfinished(a) && a.Dynastic?.Matches(first, second) == true).ToList())
                if (DynasticMarried(a.Dynastic))
                {
                    a.Dynastic.MarriageDay = CampaignTime.Now.ToDays;
                    a.Dynastic.MarriageOutcome = "married";
                    if (DynasticOwnerValid(a) && CourtAgendaRules.ObjectiveInWindow(a.Dynastic.MarriageDay,
                        a.ObjectiveData.SelectedDay, a.ObjectiveData.DeadlineDay)) FinishDynastic(a, CourtObjectiveState.Succeeded, "named_marriage_completed");
                }
        }

        private void MaintainDynasticObjectives()
        {
            foreach (var completed in _agendas.Where(a => IsDynastic(a) && a.State == CourtAgendaState.Completed && a.Dynastic != null))
            {
                completed.Dynastic.Members.RemoveAll(c => !Eligible(c, completed.Realm) || !completed.Faction.Members.Contains(c));
                PursueDynasticAlliance(completed, CampaignTime.Now.ToDays);
            }
            foreach (var a in _agendas.Where(DynasticUnfinished).ToList())
            {
                if (!a.ObjectiveData.HasTermSnapshot || !DynasticOwnerValid(a))
                { FinishDynastic(a, CourtObjectiveState.Cancelled, "royal_houses_changed"); continue; }
                var p = a.Dynastic;
                double now = CampaignTime.Now.ToDays;
                if (DynasticMarried(p))
                {
                    // Recovery may observe completion in time, but never invent an earlier wedding after expiry.
                    if (p.MarriageDay < 0 && now <= a.ObjectiveData.DeadlineDay) p.MarriageDay = now;
                    if (CourtAgendaRules.ObjectiveInWindow(p.MarriageDay, a.ObjectiveData.SelectedDay, a.ObjectiveData.DeadlineDay))
                    { FinishDynastic(a, CourtObjectiveState.Succeeded, "marriage_recovered"); continue; }
                }
                if (now > a.ObjectiveData.DeadlineDay)
                { FinishDynastic(a, CourtObjectiveState.Expired, "term_expired"); continue; }
                if (!DynasticIdentity(p))
                { FinishDynastic(a, CourtObjectiveState.Cancelled, "couple_or_household_changed"); continue; }
                p.Members.RemoveAll(c => !Eligible(c, a.Realm) || !a.Faction.Members.Contains(c));
                if (p.Response == CourtDynasticResponse.Approaching && now >= p.ReplyDay)
                {
                    if (!BellumMarriageStrategyHelper.CourtMarriageParticipant(p.First)
                        || !BellumMarriageStrategyHelper.CourtMarriageParticipant(p.Second)) continue;
                    var match = BellumMarriageStrategyHelper.EvaluateCourtMarriage(p.First, p.Second, true);
                    if (match == null)
                    {
                        p.Response = CourtDynasticResponse.ForeignRefused;
                        ReportDynastic(a, new TextObject("{=BC_CourtDynasticRefused}{RULER} has declined the proposed match between {FIRST} and {SECOND}. The hoped-for union has not been agreed."));
                    }
                    else p.Response = CourtDynasticResponse.ReplyReady;
                }
            }
        }

        private void AdvanceDynasticObjective(CourtAgendaRecord a)
        {
            if (a.ResultApplied || a.IsOngoingObjective || !a.SessionDate.IsPast) return;
            MaintainDynasticObjectives();
            if (a.ResultApplied) return;
            a.Dynastic.Members = a.Faction.Members.Where(c => Eligible(c, a.Realm) && c != a.Realm.RulingClan).Distinct().ToList();
            a.Dynastic.Activated = true;
            a.Dynastic.ActivatedDay = CampaignTime.Now.ToDays;
            a.ObjectiveData.Activate();
            a.State = CourtAgendaState.PursuingObjective;
            ReportDynastic(a, new TextObject("{=BC_CourtDynasticBegun}The {FACTION} of {REALM} urges a marriage between {FIRST} and {SECOND}, hoping to draw the two sovereign houses closer. Its lords speak in favor of an alliance with {TARGET} during this court term."));
        }

        private static TextObject DynasticText(CourtAgendaRecord a, TextObject text) => text
            .SetTextVariable("FACTION", a.Faction?.GetDisplayName() ?? TextObject.GetEmpty())
            .SetTextVariable("REALM", a.Realm?.Name ?? TextObject.GetEmpty())
            .SetTextVariable("TARGET", a.Dynastic?.Target?.Name ?? TextObject.GetEmpty())
            .SetTextVariable("RULER", a.Dynastic?.TheirHouse?.Leader?.Name ?? TextObject.GetEmpty())
            .SetTextVariable("FIRST", a.Dynastic?.First?.Name ?? TextObject.GetEmpty())
            .SetTextVariable("SECOND", a.Dynastic?.Second?.Name ?? TextObject.GetEmpty())
            .SetTextVariable("HOUSE", a.Dynastic?.Destination?.Name ?? TextObject.GetEmpty())
            .SetTextVariable("DATE", CampaignTime.Days((float)a.ObjectiveData.DeadlineDay).ToString());

        private static void ReportDynastic(CourtAgendaRecord a, TextObject text, CourtObjectiveState? result = null, float mood = 0)
        {
            text = DynasticText(a, text);
            if (result.HasValue) text = CourtObjectiveReports.WithMood(text, a.Faction?.GetDisplayName() ?? TextObject.GetEmpty(), mood, result.Value);
            BellumCivileNotifications.Show(text, CourtObjectiveReports.Color(result), primaryKingdom: a.Realm);
        }

        private void FinishDynastic(CourtAgendaRecord a, CourtObjectiveState result, string reason)
        {
            if (a.ResultApplied) return;
            bool success = result == CourtObjectiveState.Succeeded, failed = result == CourtObjectiveState.Expired;
            if (!a.ObjectiveData.Finish(result, success ? CourtObjectiveCredit.Sponsor : CourtObjectiveCredit.None, reason)
                || !a.ObjectiveData.TryClaimResult()) return;
            a.ResultApplied = a.PaymentSettled = true;
            a.State = success ? CourtAgendaState.Completed : failed ? CourtAgendaState.NotProposed : CourtAgendaState.Cancelled;
            float shock = success ? BellumCivileConstants.CourtAgendaSuccessShock : failed ? BellumCivileConstants.CourtAgendaFailureShock : 0;
            float actual = 0;
            if (DynasticOwnerValid(a) && shock != 0)
            {
                float before = a.Faction.Mood;
                a.Faction.Mood = Math.Max(-100, Math.Min(100, before + shock));
                actual = a.Faction.Mood - before;
                RecordResultHistory(a, shock);
            }
            ReportDynastic(a, new TextObject(success
                ? "{=BC_CourtDynasticSucceeded}{FIRST} and {SECOND} have wed, binding the sovereign houses of {REALM} and {TARGET} more closely. The {FACTION} praises the union and continues to favor an alliance until {DATE}."
                : failed ? "{=BC_CourtDynasticFailed}The court term has ended without the hoped-for marriage between {FIRST} and {SECOND}. The {FACTION} voices its disappointment that this opportunity to strengthen the royal house has passed."
                : "{=BC_CourtDynasticCancelled}Changed circumstances have overtaken the proposed union of {FIRST} and {SECOND}. The {FACTION} sets the matter aside without blame."), result, actual);
            BellumCivileLogger.Log($"Court royal marriage concluded; realm={a.Realm?.StringId}; target={a.Dynastic?.Target?.StringId}; result={result}; actual_mood={actual}; reason={reason}.");
        }

        private static TextObject DynasticStatus(CourtAgendaRecord a) => DynasticText(a, new TextObject(a.State == CourtAgendaState.Completed
            ? "{=BC_CourtDynasticComplete}The named marriage is complete. Participating houses retain +15 preference for an alliance with {TARGET} until {DATE}; an NPC Crown shares it while favoring Nobility. Your vote remains yours."
            : a.ResultApplied ? "{=BC_CourtDynasticClosed}This marriage initiative has ended."
            : "{=BC_CourtDynasticStatus}Proposed couple: {FIRST} and {SECOND}. Household: {HOUSE}. Marriage by {DATE} fulfills the objective (+10 mood); expiry brings -10. An aligned NPC ruler receives +15 marriage consideration; participating houses receive +15 alliance preference this term. Both houses must consent. A formal alliance is not required."));

        internal TextObject DynasticHint(Kingdom realm, FactionObject faction)
        {
            var a = GetDisplayedAgenda(realm, faction);
            return IsDynastic(a) && !a.CrisisInterventionPending && (a.State == CourtAgendaState.Announced || a.IsOngoingObjective || a.ResultApplied) ? DynasticStatus(a) : null;
        }
    }
}
