using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        internal static bool IsDirectCrownBusiness(CourtAgendaRecord agenda) => agenda?.Manual == true
            && agenda.Faction == null && agenda.Sponsor == Clan.PlayerClan && agenda.Realm?.RulingClan == Clan.PlayerClan;

        private CourtAgendaRecord[] DisplayedPlayerCrownBusiness(Kingdom realm) => _agendas
            .Where(a => a.Realm == realm && a.Faction == null && a.Sponsor == realm.RulingClan
                && (a.IsFiled || a.IsOngoingObjective || a.Manual && a.IsUnopened))
            .OrderBy(a => a.IsFiled ? 0 : 1).ThenBy(a => a.VoteDate.ToDays).ToArray();

        private string[] CrownAllocationDescriptions(Kingdom realm, CourtAgendaRecord[] proceedings)
        {
            var covered = new System.Collections.Generic.HashSet<string>(proceedings.Where(a => !a.IsPolicy)
                .Select(a => a.ObjectiveData?.TargetId));
            var lines = new System.Collections.Generic.List<string>();
            var pending = FiefDeliberationBehavior.Current?.PendingCrownAllocations(realm);
            if (pending != null)
                foreach (var item in pending.Where(p => !covered.Contains(p.settlement.StringId)))
                {
                    lines.Add(new TextObject("{=BC_CrownAllocationAgenda}Current agenda: Allocate {FIEF} (deliberating)\nVote scheduled for {DATE}")
                        .SetTextVariable("FIEF", item.settlement.Name).SetTextVariable("DATE", item.date.ToString()).ToString());
                    covered.Add(item.settlement.StringId);
                }
            foreach (var vote in realm.UnresolvedDecisions.OfType<SettlementClaimantDecision>()
                .Where(d => d.ProposerClan == realm.RulingClan && !covered.Contains(d.Settlement.StringId)))
                lines.Add(new TextObject("{=BC_CrownAllocationVoting}Current agenda: Allocate {FIEF} (voting)")
                    .SetTextVariable("FIEF", vote.Settlement.Name).ToString());
            return lines.ToArray();
        }

        public bool CanUseCrownActions(Kingdom realm) => ValidRealm(realm) && realm.RulingClan == Clan.PlayerClan
            && Eligible(Clan.PlayerClan, realm) && ElectiveSuccessionBehavior.Instance?.PendingDeposition(realm) == null;

        public TextObject CrownActionsHint(Kingdom realm) => new TextObject(realm?.RulingClan != Clan.PlayerClan
            ? "{=BC_CrownActionsRulerOnly}Only the ruler may exercise the Crown's prerogatives."
            : !CanUseCrownActions(realm) ? "{=BC_CrownActionsUnavailable}The Crown cannot undertake new business while its authority is unsettled."
            : "{=BC_CrownActionsHint}Undertake royal business. Each action retains its own cost, conditions and deliberation period; no annual nomination is required.");

        private CourtAgendaRecord NewPlayerCrownBusiness(Kingdom realm)
        {
            var agenda = new CourtAgendaRecord { Realm = realm, Sponsor = Clan.PlayerClan, Manual = true,
                State = CourtAgendaState.Announced, PlayerSelectionConfirmed = true, SessionDate = CampaignTime.Now };
            agenda.FreezeSchedule(BellumCivileOptions.CourtTermDays, BellumCivileOptions.PoliticalDeliberationDays);
            agenda.GetObjective().FreezeTerm(CampaignTime.Now.ToDays,
                CampaignTime.Now.ToDays + BellumCivileOptions.CourtTermDays);
            return agenda;
        }

        private void MigratePlayerCrownBusiness()
        {
            foreach (var agenda in _agendas.Where(a => !a.Manual && a.Faction == null
                && a.Realm?.RulingClan == Clan.PlayerClan && (a.IsUnopened || a.IsFiled || a.IsOngoingObjective)).ToList())
            {
                // Preserve paid proceedings and explicitly chosen projects, never an unanswered annual invitation.
                if (agenda.IsFiled || agenda.IsOngoingObjective || agenda.PlayerSelectionConfirmed)
                {
                    agenda.Manual = true;
                    if (agenda.IsUnopened)
                    {
                        var deliberation = agenda.VoteDate - agenda.SessionDate;
                        agenda.SessionDate = CampaignTime.Now;
                        agenda.VoteDate = CampaignTime.Now + deliberation;
                        agenda.State = CourtAgendaState.Announced;
                    }
                }
                else Cancel(agenda, "player_crown_now_self_directed");
            }
        }

        public void ShowCrownActions(Kingdom realm, Action refresh)
        {
            if (!CanUseCrownActions(realm)) return;
            var draft = NewPlayerCrownBusiness(realm);
            var kinds = new[] { CourtAppeasementRules.Kind, CourtLiberationRules.Kind, CourtProtectionRules.Kind, CourtClientGrantRules.Kind };
            var labels = new[] { "{=BC_CrownActionAppease}Appease a court faction", "{=BC_CrownActionLiberate}Prepare for liberation",
                "{=BC_CrownActionProtect}Seek foreign protection", "{=BC_CrownActionClientGrant}Grant land to a client state" };
            var hints = new[] {
                "{=BC_CrownActionAppeaseHint}Reconcile with a discontented court faction for 100 influence plus 25 per member house. Once used, no faction may be appeased again for one full court term. Accommodations cannot stack or refresh.",
                "{=BC_CrownActionLiberateHint}Prepare a client realm for liberation for 100 influence, at most once per full court term. The normal readiness requirements and declaration vote still apply.",
                "{=BC_CrownActionProtectHint}Offer clientage in exchange for intervention against an overwhelming enemy. Sending an appeal costs 100 influence, even if refused, and prevents another appeal for one full court term.",
                "{=BC_CrownActionClientGrantHint}Bestow a personally held town or castle on a client realm. Costs 100 influence. Your last fief, besieged or disputed land, wartime occupations and holdings awaiting distribution cannot be granted." };
            var choices = kinds.Select((kind, i) => new InquiryElement(kind, new TextObject(labels[i]).ToString(), null,
                AvailablePlayerMotions(draft, kind).Count > 0, CrownActionOptionHint(realm, kind, hints[i]).ToString())).ToList();
            choices.Add(RoyalPeaceAction(realm));
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=BC_CrownActions}Crown Actions").ToString(), CrownActionsHint(realm).ToString(), choices,
                true, 1, 1, new TextObject("{=BC_CrownActionSelect}Select").ToString(), new TextObject("{=BC_CourtAgenda_SubCancel}Cancel").ToString(),
                selected => { if (selected?.Count == 1 && CanUseCrownActions(realm))
                    if ((string)selected[0].Identifier == CourtRoyalPeaceRules.Kind) ShowPlayerRoyalPeace(realm, refresh);
                    else if ((string)selected[0].Identifier == CourtClientGrantRules.Kind) ShowClientGrantFiefs(realm, refresh);
                    else ShowPlayerCrownTargets(realm, (string)selected[0].Identifier, null, refresh); }, null), true);
        }

        private void ShowClientGrantFiefs(Kingdom realm, Action refresh)
        {
            if (!CanUseCrownActions(realm)) return;
            var choices = AvailablePlayerMotions(NewPlayerCrownBusiness(realm), CourtClientGrantRules.Kind)
                .GroupBy(m => m.Candidate.TargetId).Select(g => new InquiryElement(g.Key,
                    CourtClientGrantObjectiveSource.Fief(g.Key).Name.ToString(), null)).ToList();
            choices.Add(new InquiryElement("back", new TextObject("{=BC_CourtPickerBack}Back").ToString(), null));
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=BC_CrownActionClientGrant}Grant land to a client state").ToString(),
                new TextObject("{=BC_ClientGrantChooseFief}Which holding will you bestow upon a client realm?").ToString(), choices,
                true, 1, 1, new TextObject("{=BC_CourtPickerContinue}Continue").ToString(), new TextObject("{=BC_CourtAgenda_SubCancel}Cancel").ToString(),
                selected => { if (selected?.Count != 1 || !CanUseCrownActions(realm)) return;
                    string id = (string)selected[0].Identifier;
                    if (id == "back") ShowCrownActions(realm, refresh);
                    else ShowPlayerCrownTargets(realm, CourtClientGrantRules.Kind, id, refresh); }, null), true);
        }

        private void ShowPlayerCrownTargets(Kingdom realm, string kind, string targetId, Action refresh)
        {
            if (!CanUseCrownActions(realm)) return;
            var draft = NewPlayerCrownBusiness(realm);
            var motions = AvailablePlayerMotions(draft, kind).Where(m => targetId == null || m.Candidate.TargetId == targetId).ToList();
            var choices = motions.Select(m => new InquiryElement(m, m.Label.ToString(), null,
                Clan.PlayerClan.Influence >= m.FilingCost,
                new TextObject("{=BC_CrownActionCost}Cost: {COST} influence.").SetTextVariable("COST", m.FilingCost).ToString())).ToList();
            if (kind == CourtClientGrantRules.Kind)
                choices.Add(new InquiryElement("back", new TextObject("{=BC_CourtPickerBack}Back").ToString(), null));
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=BC_CrownActions}Crown Actions").ToString(),
                new TextObject("{=BC_CrownActionTarget}Whom will you name in this royal undertaking?").ToString(), choices,
                true, 1, 1, new TextObject("{=BC_CourtPickerReview}Review motion").ToString(), new TextObject("{=BC_CourtAgenda_SubCancel}Cancel").ToString(),
                selected => { if (selected?.Count != 1) return;
                    if (kind == CourtClientGrantRules.Kind && selected[0].Identifier is string) ShowClientGrantFiefs(realm, refresh);
                    else ConfirmPlayerCrownBusiness(realm, (PlayerMotion)selected[0].Identifier, refresh); }, null), true);
        }

        private void ConfirmPlayerCrownBusiness(Kingdom realm, PlayerMotion motion, Action refresh)
        {
            if (!CanUseCrownActions(realm)) return;
            var ruler = realm.Leader;
            string detail = motion.Kind == CourtClientGrantRules.Kind
                ? "{=BC_CrownDirectClientGrant}This permanently transfers the holding to the client's ruling house, without a distribution vote. Only legal rights you possess accompany the land; higher titles and historical boundaries remain unchanged. The client ruler gains +25 relations for ten years (modified by your memory-duration setting), capped at +50 from active grants."
                : motion.Kind == CourtAppeasementRules.Kind
                ? "{=BC_CrownDirectAppease}Grant this faction +20 mood for {DAYS} days. The accommodation cannot stack or be renewed while active, and ends upon ruler replacement. It cannot undo an issued ultimatum or open rebellion."
                : motion.Kind == CourtLiberationRules.Kind
                ? "{=BC_CrownDirectLiberation}Preparations add +20 liberty desire for {DAYS} days. They do not declare war: readiness, enthusiasm, influence costs and the normal vote still apply. A change of ruler or clientage ends the preparations."
                : motion.Kind == CourtProtectionRules.Kind
                ? "{=BC_CrownDirectProtection}Offer voluntary clientage in return for intervention against the named enemy. The protector may refuse. Acceptance binds your foreign policy to theirs, joins their wars and settles incompatible independent wars and agreements. A further appeal cannot be made for {DAYS} days."
                : "{=BC_CrownDirectVote}Deliberations begin now. The lords will vote on {DATE}; you may seek their support in the meantime. Filing does not guarantee approval.";
            var body = new TextObject("{=BC_CrownDirectConfirm}{MOTION}\n\nCost: {COST} influence.\n\n{DETAIL}")
                .SetTextVariable("MOTION", motion.Label).SetTextVariable("COST", motion.FilingCost)
                .SetTextVariable("DETAIL", new TextObject(detail).SetTextVariable("DAYS", BellumCivileOptions.CourtTermDays)
                    .SetTextVariable("DATE", (CampaignTime.Now + CampaignTime.Days(BellumCivileOptions.PoliticalDeliberationDays)).ToString()));
            InformationManager.ShowInquiry(new InquiryData(motion.Label.ToString(), body.ToString(), true, true,
                new TextObject("{=BC_CourtConfirmAgenda}Confirm").ToString(), new TextObject(motion.Kind == CourtClientGrantRules.Kind
                    ? "{=BC_CourtPickerBack}Back" : "{=BC_CourtAgenda_SubCancel}Cancel").ToString(),
                () => {
                    if (realm.Leader != ruler || !FilePlayerCrownBusiness(realm, motion))
                        BellumCivileNotifications.ShowPersonal(new TextObject("{=BC_CrownActionChanged}This undertaking can no longer proceed. Its conditions or cost have changed."), BellumNotificationColors.Warning);
                    refresh?.Invoke();
                }, motion.Kind == CourtClientGrantRules.Kind
                    ? (Action)(() => ShowPlayerCrownTargets(realm, motion.Kind, motion.Candidate.TargetId, refresh)) : null), true);
        }

        private bool FilePlayerCrownBusiness(Kingdom realm, PlayerMotion motion)
        {
            if (!CanUseCrownActions(realm)) return false;
            var agenda = NewPlayerCrownBusiness(realm);
            var current = AvailablePlayerMotions(agenda, motion.Kind).FirstOrDefault(m => SamePlayerMotion(m, motion));
            if (current == null || current.FilingCost != motion.FilingCost || Clan.PlayerClan.Influence < current.FilingCost) return false;
            ((ICourtAgendaObjectiveSource)_objectiveSelector.FindSource(motion.Kind)).ApplySelection(agenda,
                new CourtObjectiveChoice(motion.Kind, motion.Candidate, new CourtObjectiveEvaluation(true, true, new CourtObjectiveWeight(1), "direct_player_crown")));
            // Objective sources can choose a term session; direct royal action starts now instead.
            agenda.SessionDate = CampaignTime.Now - CampaignTime.Days(.001f);
            agenda.VoteDate = CampaignTime.Now + CampaignTime.Days(BellumCivileOptions.PoliticalDeliberationDays);
            _agendas.Add(agenda);
            Advance(agenda, Campaign.Current.GetCampaignBehavior<IdeologyBehavior>());
            if (IsProtection(agenda) && agenda.Protection?.Phase == CourtProtectionPhase.SenderDecision)
                SendProtection(agenda.Protection);
            NotifyAgenda(agenda);
            return agenda.IsFiled || agenda.IsOngoingObjective || agenda.State == CourtAgendaState.Passed || agenda.State == CourtAgendaState.Completed;
        }

        internal bool TryPlayerCouncilBusiness(Kingdom realm, PrivyCouncilOffice office, out TextObject reason, bool readOnly, Action refresh = null)
        {
            reason = CrownActionsHint(realm);
            if (!CanUseCrownActions(realm)) return false;
            var draft = NewPlayerCrownBusiness(realm);
            if (!AvailablePlayerMotions(draft, CourtCouncilObjectiveSource.CouncilKind).Any(m => m.Candidate.TargetId == office.ToString()))
            {
                reason = new TextObject("{=BC_CrownCouncilUnavailable}No eligible replacement is available, or another council appointment is already under deliberation.");
                return false;
            }
            int cost = CourtCouncilObjectiveSource.Cost(Clan.PlayerClan);
            reason = new TextObject("{=BC_CrownCouncilDirectHint}Propose an appointment to this office. Costs {COST} influence and begins a {DAYS}-day deliberation before the vote.")
                .SetTextVariable("COST", cost).SetTextVariable("DAYS", BellumCivileOptions.PoliticalDeliberationDays);
            if (Clan.PlayerClan.Influence < cost) { reason = new TextObject("{=BC_CrownActionCost}Cost: {COST} influence.").SetTextVariable("COST", cost); return false; }
            if (!readOnly) ShowPlayerCrownTargets(realm, CourtCouncilObjectiveSource.CouncilKind, office.ToString(), refresh);
            return true;
        }

        internal bool TryPlayerTreasonBusiness(Kingdom realm, Clan target, out TextObject reason)
        {
            reason = CrownActionsHint(realm);
            if (!CanUseCrownActions(realm)) return false;
            var motion = AvailablePlayerMotions(NewPlayerCrownBusiness(realm), CourtExecutiveRules.Treason)
                .FirstOrDefault(m => m.Candidate.TargetId == target?.StringId);
            if (motion == null) return false;
            reason = new TextObject("{=BC_CrownActionCost}Cost: {COST} influence.").SetTextVariable("COST", motion.FilingCost);
            return FilePlayerCrownBusiness(realm, motion);
        }
    }
}
