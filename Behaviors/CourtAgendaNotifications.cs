using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private void NotifyAgenda(CourtAgendaRecord agenda, bool crisisSetAside = false)
        {
            BellumCivileLogger.Log($"Court agenda updated; realm={agenda.Realm.StringId}; faction={agenda.Faction?.Type.ToString() ?? "Crown"}; state={agenda.State}; policy={agenda.PolicyId}; session={agenda.SessionDate.ToDays}; nominationDeadline={agenda.NominationDeadline.ToDays}; crisisRestrained={agenda.OrdinaryCrisisRestrained}.");
            if (!BellumCivileNotifications.ShouldPrepare(primaryKingdom: agenda.Realm)) return;
            var message = BuildAgendaNotice(agenda, crisisSetAside);
            BellumCivileNotifications.Show(message, BellumNotificationColors.Politics, primaryKingdom: agenda.Realm);
        }

        private static TextObject BuildAgendaNotice(CourtAgendaRecord agenda, bool crisisSetAside)
        {
            TextObject Notice(string text) => new TextObject(text)
                .SetTextVariable("OWNER", AgendaOwnerName(agenda)).SetTextVariable("REALM", agenda.Realm.Name);

            // A nomination mandate already sends its own letter with the filing deadline.
            if (agenda.State == CourtAgendaState.AwaitingNomination) return null;
            if (crisisSetAside)
                return Notice("{=BC_CourtNewsCrisisSetAside}The {OWNER} of {REALM} has set aside its challenge to the Crown for now.");
            if (agenda.CrisisInterventionPending || agenda.State == CourtAgendaState.Crisis)
                return Notice(agenda.CrisisInterventionPending
                    ? "{=BC_CourtNewsCrisisPending}The {OWNER} of {REALM} calls upon its leader to take a stand against the Crown."
                    : "{=BC_CourtNewsCrisis}The {OWNER} of {REALM} will meet on {DATE} to decide whether to challenge the Crown.")
                    .SetTextVariable("DATE", agenda.SessionDate.ToString());
            if (agenda.State == CourtAgendaState.NotProposed)
                return Notice("{=BC_CourtNewsNotProposed}The {OWNER} of {REALM} has brought no motion before the court.");

            if (IsProtection(agenda) && agenda.Protection?.Phase == CourtProtectionPhase.Offered)
                return ProtectionText(agenda.Protection, "{=BC_CourtNewsProtectionSent}{CLIENT} has sent an envoy to {PROTECTOR}, seeking aid against {THREAT}.");

            bool setAside = agenda.State == CourtAgendaState.Blocked || agenda.State == CourtAgendaState.Withdrawn
                || agenda.State == CourtAgendaState.Cancelled;
            bool proposed = agenda.State == CourtAgendaState.Announced || agenda.State == CourtAgendaState.AwaitingPlayerDecision;
            if (!setAside && !proposed && !agenda.IsFiled) return null;

            TextObject objective;
            if (agenda.IsPolicy)
            {
                var policy = Policy(agenda);
                if (policy == null) return null;
                objective = new TextObject(agenda.Abolish ? "{=BC_CourtRepealObjective}Repeal {POLICY}" : "{=BC_CourtEnactObjective}Enact {POLICY}")
                    .SetTextVariable("POLICY", policy.Name);
            }
            else objective = ExecutiveObjectiveText(agenda);

            if (setAside)
                return Notice("{=BC_CourtNewsSetAside}The {OWNER} of {REALM} has set aside this motion: {MOTION}.")
                    .SetTextVariable("MOTION", objective);
            if (agenda.IsFiled)
                return Notice("{=BC_CourtNewsFiled}The {OWNER} of {REALM} has brought a motion before the court: {MOTION}.")
                    .SetTextVariable("MOTION", objective);
            if (IsRally(agenda) && agenda.Rally?.Target != null)
                return Notice("{=BC_CourtNewsRally}The lords of {OWNER} in {REALM} urge the Crown to stand firm against {TARGET}. They seek victory before {DATE}.")
                    .SetTextVariable("TARGET", agenda.Rally.Target.Name)
                    .SetTextVariable("DATE", CampaignTime.Days((float)agenda.ObjectiveData.DeadlineDay).ToString());
            if (IsClaimObjective(agenda) && agenda.Claim?.Fief != null && agenda.Claim.Beneficiary != null)
                return Notice("{=BC_CourtNewsClaim}The lords of {OWNER} in {REALM} ask the Crown to honor {HOUSE}'s claim to {FIEF}.")
                    .SetTextVariable("HOUSE", agenda.Claim.Beneficiary.Name).SetTextVariable("FIEF", agenda.Claim.Fief.Name);
            if (IsTrade(agenda) && agenda.Trade?.Target != null)
                return Notice("{=BC_CourtNewsTrade}The lords of {OWNER} in {REALM} urge the Crown to seek a trade accord with {TARGET}.")
                    .SetTextVariable("TARGET", agenda.Trade.Target.Name);
            if (IsPeaceObjective(agenda) && agenda.Peace?.Target != null)
                return Notice("{=BC_CourtNewsPeace}The lords of {OWNER} in {REALM} call upon the Crown to seek peace with {TARGET}.")
                    .SetTextVariable("TARGET", agenda.Peace.Target.Name);
            if (IsCampaignObjective(agenda) && agenda.Campaign?.Target != null)
                return Notice("{=BC_CourtNewsCampaign}The lords of {OWNER} in {REALM} urge the Crown to take up arms against {TARGET}.")
                    .SetTextVariable("TARGET", agenda.Campaign.Target.Name);
            if (IsDynastic(agenda) && agenda.Dynastic?.First != null && agenda.Dynastic.Second != null)
                return Notice("{=BC_CourtNewsMarriage}The lords of {OWNER} in {REALM} put forward a match between {FIRST} and {SECOND}, hoping to draw the two houses closer.")
                    .SetTextVariable("FIRST", agenda.Dynastic.First.Name).SetTextVariable("SECOND", agenda.Dynastic.Second.Name);
            if (IsSubjugationObjective(agenda) && agenda.Subjugation?.Target != null)
                return Notice("{=BC_CourtNewsClientage}The lords of {OWNER} in {REALM} call upon the Crown to demand {TARGET}'s submission.")
                    .SetTextVariable("TARGET", agenda.Subjugation.Target.Name);

            return Notice("{=BC_CourtNewsProposed}The {OWNER} of {REALM} has proposed new business for the court: {MOTION}.")
                .SetTextVariable("MOTION", objective);
        }
    }
}
