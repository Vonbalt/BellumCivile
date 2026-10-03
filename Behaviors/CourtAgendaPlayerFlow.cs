using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private CourtAgendaRecord _activePlayerInquiry;
        private float _inquiryCheckDelay;

        internal void ReviewInheritedFactionAgenda(FactionObject faction)
        {
            if (faction == null || !faction.IsIdeology || faction.Leader != Clan.PlayerClan
                || !ValidRealm(faction.ParentKingdom) || MemberCount(faction) < 2
                || !CourtMembershipEligibility.CanBelong(Clan.PlayerClan, faction.ParentKingdom)) return;
            foreach (var agenda in _agendas.Where(a => a.Faction == faction && a.Realm == faction.ParentKingdom))
            {
                if (!TryHandOverUnstartedAgenda(agenda, Clan.PlayerClan, agenda.SessionDate.IsPast,
                    faction.Mood <= -60 && !agenda.OrdinaryCrisisRestrained)) continue;
                BellumCivileLogger.Log($"Court agenda handed to new player leader; realm={agenda.Realm.StringId}; faction={faction.Type}; policy={agenda.PolicyId}; session={agenda.SessionDate.ToDays}.");
            }
        }

        internal static bool TryHandOverUnstartedAgenda(CourtAgendaRecord agenda, Clan player, bool sessionPassed, bool crisis)
        {
            if (agenda == null || player == null || agenda.Faction == null || agenda.Manual
                || agenda.Sponsor == player || sessionPassed || agenda.PaidInfluence != 0
                || agenda.SubstitutionInfluencePaid != 0 || agenda.PaymentSettled || agenda.ResultApplied || agenda.EventApplied
                || (agenda.State != CourtAgendaState.Announced && agenda.State != CourtAgendaState.Crisis
                    && agenda.State != CourtAgendaState.NotProposed)) return false;
            bool wasCrisis = agenda.State == CourtAgendaState.Crisis;
            agenda.Sponsor = player;
            agenda.State = CourtAgendaState.AwaitingPlayerDecision;
            agenda.PlayerSelectionConfirmed = false;
            agenda.CrisisInterventionPending = wasCrisis || crisis;
            return true;
        }

        public CourtAgendaRecord GetPlayerTermAgenda(Kingdom realm) => realm?.RulingClan == Clan.PlayerClan
            ? GetAgenda(realm, null)
            : _agendas.LastOrDefault(a => !a.Manual && a.Realm == realm && a.Faction?.Leader == Clan.PlayerClan);

        public bool IsNominationOpen(Kingdom realm) => IsMandateOpen(realm, "policy");

        private bool IsMandateOpen(Kingdom realm, string kind)
        {
            if (realm?.RulingClan == Clan.PlayerClan) return false;
            var agenda = GetPlayerTermAgenda(realm);
            return agenda != null && CourtAgendaRules.NominationAuthorized(agenda.State, agenda.NominationKind, kind,
                CampaignTime.Now.ToDays, agenda.NominationDeadline.ToDays) && agenda.Sponsor == Clan.PlayerClan
                && !agenda.CrisisInterventionPending && ValidPlayerAgenda(agenda);
        }

        private bool ValidPlayerAgenda(CourtAgendaRecord agenda) => agenda != null && _agendas.Contains(agenda)
            && ValidRealm(agenda.Realm) && Eligible(agenda.Sponsor, agenda.Realm) && agenda.IsUnopened
            && (agenda.Faction == null ? agenda.Sponsor == Clan.PlayerClan && agenda.Realm.RulingClan == Clan.PlayerClan
                : agenda.Faction.ParentKingdom == agenda.Realm && agenda.Faction.Leader == Clan.PlayerClan
                    && CourtMembershipEligibility.CanBelong(Clan.PlayerClan, agenda.Realm) && MemberCount(agenda.Faction) >= 2);

        private void ProcessPlayerInquiry(float dt)
        {
            _inquiryCheckDelay -= dt;
            if (_inquiryCheckDelay > 0) return;
            _inquiryCheckDelay = 1f;
            ReconcileCrowns();
            // Also recover an unstarted NPC agenda from a save made after accepting leadership.
            foreach (var faction in _agendas.Select(a => a.Faction).Where(f => f != null && f.Leader == Clan.PlayerClan).Distinct().ToList())
                ReviewInheritedFactionAgenda(faction);
            var mapState = Game.Current?.GameStateManager?.ActiveState as MapState;
            if (InformationManager.IsAnyInquiryActive() || mapState == null || mapState.AtMenu
                || mapState.MapConversationActive || mapState.NextIncident != null
                || Campaign.Current?.CurrentMenuContext != null
                || Campaign.Current?.ConversationManager?.IsConversationFlowActive == true
                || Hero.MainHero?.IsPrisoner == true || PlayerEncounter.Current != null) return;
            // Pending decisions live in saved agenda records, not in the popup callback.
            _activePlayerInquiry = null;
            if (ProcessProtectionInquiry()) return;
            if (ProcessTitlePetitionInquiry()) return;
            var agenda = _agendas.Where(ValidPlayerAgenda)
                .Where(a => a.Faction != null)
                .Where(a => a.CrisisInterventionPending || a.State == CourtAgendaState.AwaitingPlayerDecision)
                .OrderByDescending(a => a.CrisisInterventionPending).ThenBy(a => a.SessionDate.ToDays).FirstOrDefault();
            if (agenda == null) { ProcessDynasticInquiry(); return; }
            if (!UpdatePlayerAgenda(agenda))
            {
                if (!agenda.CrisisInterventionPending) return;
            }
            if (!ValidPlayerAgenda(agenda)) return;
            ShowPlayerAgendaInquiry(agenda);
        }

        private int BlockCost(CourtAgendaRecord agenda, bool crisis) => crisis
            ? IdeologyBehavior.CalculateRebellionSuppressionCost(agenda.Faction.Mood)
            : Campaign.Current.GetCampaignBehavior<PrivyCouncilBehavior>()?
                .GetFactionMotionInfluenceCost(Clan.PlayerClan, C.CourtAgendaInfluenceCost) ?? C.CourtAgendaInfluenceCost;

        private void ShowPlayerAgendaInquiry(CourtAgendaRecord agenda) => ShowTermBusinessInquiry(agenda);

        private void ApplyPlayerAgendaChoice(CourtAgendaRecord agenda, int choice, bool crisis)
        {
            if (choice == 2) { ShowTermMotionPicker(agenda, crisis); return; }
            if (agenda.Faction == null)
            {
                agenda.State = choice == 1 ? CourtAgendaState.Blocked : CourtAgendaState.Announced;
                agenda.PlayerSelectionConfirmed = true;
                NotifyAgenda(agenda);
                return;
            }
            if (choice == 1)
            {
                if (!NpcInfluenceBudgetService.TrySpend(Clan.PlayerClan, BlockCost(agenda, crisis),
                    crisis ? NpcInfluenceExpenseKind.CrownEmergency : NpcInfluenceExpenseKind.Discretionary,
                    crisis ? "court_term_crisis_restraint" : "court_term_agenda_block")) return;
                agenda.CrisisInterventionPending = false;
                agenda.OrdinaryCrisisRestrained |= crisis;
                if (!crisis) agenda.State = CourtAgendaState.Blocked;
                else if (agenda.State == CourtAgendaState.AwaitingPlayerDecision)
                    agenda.State = agenda.IsPolicy && Policy(agenda) == null ? CourtAgendaState.NotProposed : CourtAgendaState.Announced;
                ApplyMemberMemory(agenda, C.CourtAgendaDeclineRelationPenalty, RelationMemorySources.OverruledMyFaction, 7f);
            }
            else if (crisis)
            {
                agenda.CrisisInterventionPending = false;
                agenda.State = CourtAgendaState.Crisis;
            }
            else agenda.State = agenda.IsPolicy && Policy(agenda) == null ? CourtAgendaState.NotProposed : CourtAgendaState.Announced;
            agenda.PlayerSelectionConfirmed = true;
            NotifyAgenda(agenda);
        }

        private bool UpdatePlayerAgenda(CourtAgendaRecord agenda)
        {
            if (TryWithdrawChangedPolicyAgenda(agenda)) return false;
            if (agenda.State == CourtAgendaState.AwaitingNomination && !ValidPlayerAgenda(agenda))
            {
                Cancel(agenda, "nomination_authority_lost");
                return false;
            }
            if (agenda.Faction?.Leader != Clan.PlayerClan && !(agenda.Faction == null && agenda.Sponsor == Clan.PlayerClan))
            {
                agenda.CrisisInterventionPending = false;
                if (agenda.State == CourtAgendaState.AwaitingPlayerDecision || agenda.State == CourtAgendaState.AwaitingNomination)
                    agenda.State = agenda.IsPolicy && Policy(agenda) == null ? CourtAgendaState.NotProposed : CourtAgendaState.Announced;
                return agenda.IsUnopened;
            }
            if (agenda.CrisisInterventionPending) return false;
            if (agenda.State == CourtAgendaState.AwaitingPlayerDecision && agenda.SessionDate.IsPast)
            {
                agenda.State = CourtAgendaState.NotProposed;
                NotifyAgenda(agenda);
                return false;
            }
            if (agenda.State != CourtAgendaState.AwaitingNomination) return true;
            double remaining = agenda.NominationDeadline.ToDays - CampaignTime.Now.ToDays;
            if (remaining <= 0)
            {
                agenda.State = CourtAgendaState.NominationExpired;
                ApplyMemberMemory(agenda, C.CourtAgendaBrokenPromiseRelationPenalty, RelationMemorySources.UnfulfilledFactionAgenda, C.CourtAgendaBrokenPromiseMemoryYears);
                var message = new TextObject("{=BC_CourtMandateExpired}The houses of the {FACTION} in {REALM} waited in vain for the proposal you promised. Your mandate has expired, and they resent your unfulfilled pledge ({RELATION} relation).");
                message.SetTextVariable("FACTION", AgendaOwnerName(agenda));
                message.SetTextVariable("REALM", agenda.Realm.Name);
                message.SetTextVariable("RELATION", C.CourtAgendaBrokenPromiseRelationPenalty);
                BellumCivileNotifications.Show(message, BellumNotificationColors.Danger, primaryKingdom: agenda.Realm, isPersonal: true);
                return false;
            }
            if (!agenda.NominationReminderSent && remaining <= Math.Max(1, agenda.NominationWindowDays / 4f))
            {
                agenda.NominationReminderSent = true;
                var message = new TextObject("{=BC_CourtMandateReminder}The {FACTION} of {REALM} reminds you of the business entrusted to you. Bring your proposal before the court by {DATE}, lest their confidence in you be misplaced.");
                message.SetTextVariable("FACTION", AgendaOwnerName(agenda));
                message.SetTextVariable("REALM", agenda.Realm.Name);
                message.SetTextVariable("DATE", agenda.NominationDeadline.ToString());
                BellumCivileNotifications.Show(message, BellumNotificationColors.Politics, primaryKingdom: agenda.Realm, isPersonal: true);
            }
            return true;
        }

        public bool TryNominatePolicy(Kingdom realm, PolicyObject policy, bool abolish)
        {
            if (!IsNominationOpen(realm) || !CanPropose(realm, policy) || realm.ActivePolicies.Contains(policy) != abolish) return false;
            var agenda = GetPlayerTermAgenda(realm);
            return FileMandateMotion(agenda, new PlayerMotion { Kind = "policy",
                Candidate = new CourtObjectiveCandidate(policy.StringId, abolish ? "repeal" : "enact") });
        }

        private static void ApplyMemberMemory(CourtAgendaRecord agenda, int amount, string source, float years)
        {
            if (agenda.Faction == null) return;
            foreach (Clan clan in agenda.Faction.Members.Where(c => c != Clan.PlayerClan && Eligible(c, agenda.Realm)).Distinct().ToList())
                RelationMemoryService.ApplyChange(clan.Leader, Hero.MainHero, amount, false, source, years,
                    RelationMemoryScope.House, agenda.Faction.GetDisplayName().ToString());
        }

        private void NotifyAgenda(CourtAgendaRecord agenda)
        {
            BellumCivileLogger.Log($"Player court agenda updated; realm={agenda.Realm.StringId}; faction={agenda.Faction?.Type.ToString() ?? "Crown"}; state={agenda.State}; policy={agenda.PolicyId}; session={agenda.SessionDate.ToDays}; nominationDeadline={agenda.NominationDeadline.ToDays}; crisisRestrained={agenda.OrdinaryCrisisRestrained}.");
            var message = new TextObject("{=BC_CourtAgendaUpdated}Court of {REALM}:\n{AGENDA}");
            message.SetTextVariable("REALM", agenda.Realm.Name);
            message.SetTextVariable("AGENDA", DescribeAgenda(agenda.Realm, agenda.Faction));
            BellumCivileNotifications.Show(message, BellumNotificationColors.Politics, primaryKingdom: agenda.Realm);
        }
    }
}
