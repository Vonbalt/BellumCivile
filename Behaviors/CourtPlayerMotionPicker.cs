using System;
using System.Collections.Generic;
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
        private const string DebateKind = "court_tyranny_debate";

        private sealed class PlayerMotion
        {
            internal string Kind;
            internal CourtObjectiveCandidate Candidate;
            internal TextObject Label;
            internal int FilingCost;
        }

        private bool CanChooseTermBusiness(CourtAgendaRecord agenda) => ValidPlayerAgenda(agenda)
            && (agenda.SessionDate.IsFuture || agenda.CrisisInterventionPending) && !agenda.PlayerSelectionConfirmed && !agenda.ResultApplied
            && agenda.ObjectiveData?.Kind != CourtExecutiveRules.Decree && !IsRoyalPeace(agenda);

        private int ReplacementCost(CourtAgendaRecord agenda, bool crisis) => agenda.Faction == null ? 0 : BlockCost(agenda, crisis);

        private void ShowTermBusinessInquiry(CourtAgendaRecord agenda)
        {
            bool crown = agenda.Faction == null;
            bool crisis = agenda.CrisisInterventionPending;
            // A later crisis can interrupt a previously confirmed ordinary agenda.
            if (crisis) agenda.PlayerSelectionConfirmed = false;
            if (!CanChooseTermBusiness(agenda)) return;
            var identity = agenda.GetObjective();
            var originalState = agenda.State;
            TextObject motion = crisis ? new TextObject("{=BC_CourtDebateDescription}debate the perceived tyranny of the ruler")
                : agenda.IsPolicy ? (Policy(agenda) == null ? new TextObject("{=BC_CourtNoBusiness}conclude the term without a motion")
                    : new TextObject(agenda.Abolish ? "{=BC_CourtRepealObjective}Repeal {POLICY}" : "{=BC_CourtEnactObjective}Enact {POLICY}")
                        .SetTextVariable("POLICY", Policy(agenda).Name)) : ExecutiveObjectiveText(agenda);
            var body = crown
                ? new TextObject("{=BC_CrownTermInvitation}A new court term has begun in {REALM}. What business will you place before the court in the name of the Crown? The appointed session is {DATE}.")
                : new TextObject("{=BC_CourtTermProposal}The {FACTION_NAME} have gathered in court to debate matters of state. After long argument, they have settled upon the following business: {MOTION_DESC}. As their leader, you have final say on the matter. The appointed session is {DATE}.");
            body.SetTextVariable("REALM", agenda.Realm.Name).SetTextVariable("FACTION_NAME", AgendaOwnerName(agenda))
                .SetTextVariable("MOTION_DESC", motion).SetTextVariable("DATE", agenda.SessionDate.ToString());
            int price = ReplacementCost(agenda, crisis);
            var feeHint = new TextObject("{=BC_CourtChoiceCost}Cost: {COST} influence. Any later filing cost is separate.").SetTextVariable("COST", price).ToString();
            var choices = new List<InquiryElement>();
            if (crown)
            {
                choices.Add(new InquiryElement(2, new TextObject("{=BC_CrownChooseBusiness}Choose Crown business").ToString(), null, true, ""));
                choices.Add(new InquiryElement(1, new TextObject("{=BC_CrownNoInitiative}Undertake no initiative this term").ToString(), null, true, ""));
                if (!agenda.IsPolicy || Policy(agenda) != null)
                    choices.Insert(0, new InquiryElement(0, motion.ToString(), null, true, ""));
            }
            else
            {
                choices.Add(new InquiryElement(0, new TextObject("{=BC_CourtAgenda_Approve}Approve").ToString(), null, true,
                    new TextObject("{=BC_CourtAgenda_Approve_Desc}Approve the faction's proposed motion without spending influence.").ToString()));
                choices.Add(new InquiryElement(1, new TextObject("{=BC_CourtAgenda_Decline}Decline").ToString(), null,
                    Clan.PlayerClan.Influence >= price && (crisis || !agenda.IsPolicy || Policy(agenda) != null),
                    new TextObject("{=BC_CourtDeclineCost}Spend {COST} influence to overrule the faction. Its member houses will resent your decision ({RELATION} relation).")
                        .SetTextVariable("COST", price).SetTextVariable("RELATION", BellumCivileConstants.CourtAgendaDeclineRelationPenalty).ToString()));
                choices.Add(new InquiryElement(2, new TextObject("{=BC_CourtAgenda_Substitute}Substitute Motion").ToString(), null, agenda.SessionDate.IsFuture, feeHint));
            }
            _activePlayerInquiry = agenda;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject(crown ? "{=BC_CrownName}Crown" : "{=BC_CourtAgenda_Title}Faction Council").ToString(), body.ToString(), choices,
                false, 1, 1, new TextObject("{=BC_CourtConfirmAgenda}Confirm").ToString(), null,
                selected =>
                {
                    if (_activePlayerInquiry != agenda) return;
                    _activePlayerInquiry = null;
                    if (!CanChooseTermBusiness(agenda) || agenda.ObjectiveData != identity || agenda.State != originalState
                        || crisis != agenda.CrisisInterventionPending || ReplacementCost(agenda, crisis) != price || selected?.Count != 1) return;
                    ApplyPlayerAgendaChoice(agenda, (int)selected[0].Identifier, crisis);
                }, null), true);
        }

        private List<PlayerMotion> AvailablePlayerMotions(CourtAgendaRecord agenda, string onlyKind = null)
        {
            var context = new CourtTermContext(agenda.Realm, p => CanPropose(agenda.Realm, p), agenda);
            var owner = new CourtObjectiveOwner(agenda.Faction, agenda.Sponsor);
            var result = new List<PlayerMotion>();
            foreach (string kind in new[] { "policy", CourtClientGrantRules.Kind, CourtLiberationRules.Kind, CourtCouncilObjectiveSource.CouncilKind, CourtMandateRules.Kind, CourtExecutiveRules.Grant,
                CourtExecutiveRules.Revoke, CourtExecutiveRules.Treason, CourtActivityRules.Kind, CourtAppeasementRules.Kind, CourtPeaceRules.Kind, CourtCampaignRules.Kind, CourtSubjugationRules.Kind, CourtClaimRules.Kind, CourtDynasticRules.Kind, CourtProtectionRules.Kind, CourtTradeRules.Kind, CourtRallyRules.Kind, CourtTitleGrantRules.Grant, CourtTitleGrantRules.Petition })
            {
                if (onlyKind != null && kind != onlyKind) continue;
                if (agenda.Faction == null && CrownActionCoolingDown(agenda.Realm, kind)) continue;
                var source = (ICourtAgendaObjectiveSource)_objectiveSelector.FindSource(kind);
                foreach (var candidate in source.FindCandidates(context, owner))
                {
                    var evaluation = source.EvaluateCandidate(context, owner, candidate);
                    if (!evaluation.Eligible || kind == CourtActivityRules.Kind && !evaluation.Viable) continue;
                    TextObject label;
                    int filing = 0;
                    if (kind == "policy")
                    {
                        var policy = context.FindPolicy(candidate.TargetId);
                        bool repeal = candidate.ActionId == "repeal";
                        label = new TextObject(repeal ? "{=BC_CourtRepealObjective}Repeal {POLICY}" : "{=BC_CourtEnactObjective}Enact {POLICY}")
                            .SetTextVariable("POLICY", policy.Name);
                        filing = FilingCost(new KingdomPolicyDecision(agenda.Sponsor, policy, repeal), agenda.Faction);
                    }
                    else if (kind == CourtClientGrantRules.Kind)
                    {
                        label = ClientGrantLabel(CourtClientGrantObjectiveSource.Fief(candidate.TargetId), CourtClientGrantObjectiveSource.Client(candidate.ActionId));
                        filing = CourtClientGrantRules.Cost;
                    }
                    else if (kind == CourtActivityRules.Kind) label = CourtActivityCatalog.Find(candidate.TargetId).AgendaText;
                    else if (kind == CourtPeaceRules.Kind) label = PeaceLabel(CourtPeaceObjectiveSource.Target(candidate.TargetId));
                    else if (kind == CourtTradeRules.Kind) label = TradeLabel(CourtCampaignObjectiveSource.Target(candidate.TargetId));
                    else if (kind == CourtRallyRules.Kind) label = RallyLabel(CourtCampaignObjectiveSource.Target(candidate.TargetId));
                    else if (kind == CourtLiberationRules.Kind)
                    { label = LiberationLabel(CourtCampaignObjectiveSource.Target(candidate.TargetId)); filing = CrownInitiativeCost; }
                    else if (kind == CourtMandateRules.Kind)
                    { label = MandateLabel(candidate.TargetId); filing = CourtMandateObjectiveSource.Cost(agenda.Sponsor); }
                    else if (kind == CourtTitleGrantRules.Grant || kind == CourtTitleGrantRules.Petition)
                    {
                        label = TitleGrantLabel(candidate.TargetId, CourtTitleGrantObjectiveSource.ClanById(candidate.BeneficiaryId), kind == CourtTitleGrantRules.Petition);
                        filing = kind == CourtTitleGrantRules.Grant ? (int)FeudalTitlePlayerActionService.GrantInfluenceCost : 0;
                    }
                    else if (kind == CourtProtectionRules.Kind)
                    { label = ProtectionLabel(CourtCampaignObjectiveSource.Target(candidate.TargetId), CourtCampaignObjectiveSource.Target(candidate.ActionId)); filing = CrownInitiativeCost; }
                    else if (kind == CourtCampaignRules.Kind) label = CampaignLabel(CourtCampaignObjectiveSource.Target(candidate.TargetId));
                    else if (kind == CourtSubjugationRules.Kind) label = SubjugationLabel(CourtCampaignObjectiveSource.Target(candidate.TargetId));
                    else if (kind == CourtClaimRules.Kind) label = ClaimLabel(CourtClaimObjectiveSource.Fief(candidate.TargetId), CourtClaimObjectiveSource.Beneficiary(candidate.BeneficiaryId));
                    else if (kind == CourtDynasticRules.Kind) label = new TextObject("{=BC_CourtDynasticChoice}Propose {FIRST} and {SECOND} as a royal match with {REALM}")
                        .SetTextVariable("FIRST", CourtDynasticObjectiveSource.HeroById(candidate.ActionId)?.Name ?? TextObject.GetEmpty())
                        .SetTextVariable("SECOND", CourtDynasticObjectiveSource.HeroById(candidate.BeneficiaryId)?.Name ?? TextObject.GetEmpty())
                        .SetTextVariable("REALM", CourtCampaignObjectiveSource.Target(candidate.TargetId)?.Name ?? TextObject.GetEmpty());
                    else if (kind == CourtAppeasementRules.Kind)
                    {
                        var faction = CourtAppeasementObjectiveSource.Target(agenda.Realm, candidate.TargetId);
                        label = AppeasementLabel(faction);
                        filing = CourtAppeasementRules.Cost(MemberCount(faction));
                    }
                    else if (kind == CourtCouncilObjectiveSource.CouncilKind)
                    {
                        label = new TextObject("{=BC_CourtPickerCouncil}Nominate {CANDIDATE} as {OFFICE}")
                            .SetTextVariable("CANDIDATE", context.FindClan(candidate.BeneficiaryId).Leader.Name)
                            .SetTextVariable("OFFICE", PrivyCouncilBehavior.GetLocalizedOfficeName((PrivyCouncilOffice)Enum.Parse(typeof(PrivyCouncilOffice), candidate.TargetId), agenda.Realm));
                        filing = CourtCouncilObjectiveSource.Cost(agenda.Sponsor);
                    }
                    else
                    {
                        var preview = new CourtAgendaRecord { Realm = agenda.Realm, Sponsor = agenda.Sponsor,
                            ObjectiveData = new CourtObjectiveRecord { Kind = kind, TargetId = candidate.TargetId, ActionId = candidate.ActionId },
                            OriginalHolder = context.FindSettlement(candidate.TargetId)?.OwnerClan };
                        label = ExecutiveObjectiveText(preview);
                        filing = ExecutiveDecision(preview)?.GetProposalInfluenceCost() ?? 0;
                    }
                    result.Add(new PlayerMotion { Kind = kind, Candidate = candidate, Label = label, FilingCost = filing });
                }
            }
            if (Campaign.Current.GetCampaignBehavior<IdeologyBehavior>()?.CanSelectCourtChallenge(agenda.Faction, agenda.Realm) == true)
                result.Add(new PlayerMotion { Kind = DebateKind, Candidate = new CourtObjectiveCandidate(agenda.Realm.StringId, "debate"),
                    Label = new TextObject("{=BC_CourtPickerTyranny}Debate the ruler's tyranny") });
            return result;
        }

        private static bool SamePlayerMotion(PlayerMotion first, PlayerMotion second) => first.Kind == second.Kind
            && first.Candidate.TargetId == second.Candidate.TargetId && first.Candidate.ActionId == second.Candidate.ActionId
            && first.Candidate.BeneficiaryId == second.Candidate.BeneficiaryId;

        private static TextObject MotionCategory(string kind) => new TextObject(kind == "policy" ? "{=BC_CourtPickerLaws}Laws"
            : kind == NominationMotionKind ? "{=BC_NominationCategory}Court mandates"
            : kind == CourtMandateRules.Kind ? "{=BC_MandateCategory}Elective mandate reforms"
            : kind == CourtCouncilObjectiveSource.CouncilKind ? "{=BC_CourtPickerOffices}Council appointments"
            : kind == CourtActivityRules.Kind ? "{=BC_CourtPickerActivities}Faction activities"
            : kind == CourtAppeasementRules.Kind ? "{=BC_CourtPickerAccommodation}Court reconciliation"
            : kind == CourtLiberationRules.Kind || kind == CourtRallyRules.Kind || kind == CourtPeaceRules.Kind || kind == CourtCampaignRules.Kind || kind == CourtSubjugationRules.Kind || kind == CourtClaimRules.Kind || kind == CourtDynasticRules.Kind || kind == CourtProtectionRules.Kind || kind == CourtTradeRules.Kind ? "{=BC_CourtPickerForeignAffairs}Foreign affairs"
            : kind == DebateKind ? "{=BC_CourtPickerAuthority}Ruler's authority" : "{=BC_CourtPickerEstates}Estates and justice");

        private void ShowTermMotionPicker(CourtAgendaRecord agenda, bool crisis, string category = null)
        {
            if (!CanChooseTermBusiness(agenda) || !agenda.SessionDate.IsFuture) return;
            var identity = agenda.GetObjective();
            var state = agenda.State;
            var motions = AvailablePlayerMotions(agenda).Where(m => crisis ? m.Kind != DebateKind
                : m.Kind != identity.Kind || m.Candidate.TargetId != identity.TargetId || m.Candidate.ActionId != identity.ActionId
                    || m.Candidate.BeneficiaryId != (IsTitleGrant(agenda) ? agenda.TitleGrant?.Recipient?.StringId : IsDynastic(agenda) ? agenda.Dynastic?.Second?.StringId : IsClaimObjective(agenda) ? agenda.Claim?.Beneficiary?.StringId : agenda.PreferredCouncilCandidate?.StringId)).ToList();
            if (agenda.Faction != null)
            {
                foreach (string kind in new[] { "policy", CourtCouncilObjectiveSource.CouncilKind })
                {
                    bool available = AvailablePlayerMotions(agenda, kind).Any();
                    motions.RemoveAll(m => m.Kind == kind);
                    if (available) motions.Add(new PlayerMotion { Kind = NominationMotionKind,
                        Candidate = new CourtObjectiveCandidate(kind, "nominate"), Label = NominationLabel(kind) });
                }
            }
            int price = ReplacementCost(agenda, crisis);
            bool categories = category == null && motions.Count > 12;
            if (category != null) motions = motions.Where(m => MotionCategory(m.Kind).ToString() == category).ToList();
            var choices = categories ? motions.Where(m => m.Kind != NominationMotionKind).Select(m => MotionCategory(m.Kind).ToString()).Distinct()
                .Select(c => new InquiryElement(c, c, null, true, "")).ToList()
                : motions.Select(m => new InquiryElement(m, m.Label.ToString(), null, Clan.PlayerClan.Influence >= price,
                new TextObject(m.Kind == NominationMotionKind
                    ? "{=BC_NominationPickerHint}Selection: {COST} influence. Grants one time-limited mandate; the normal proposal cost is paid when you file through the kingdom menu."
                    : "{=BC_CourtPickerCosts}Selection: {COST} influence. Filing: {FILING} influence at the appointed session.")
                    .SetTextVariable("COST", price).SetTextVariable("FILING", m.FilingCost).ToString())).ToList();
            if (categories)
                choices.InsertRange(0, motions.Where(m => m.Kind == NominationMotionKind).Select(m => new InquiryElement(m,
                    m.Label.ToString(), null, Clan.PlayerClan.Influence >= price,
                    new TextObject("{=BC_NominationPickerHint}Selection: {COST} influence. Grants one time-limited mandate; the normal proposal cost is paid when you file through the kingdom menu.")
                        .SetTextVariable("COST", price).ToString())));
            choices.Add(new InquiryElement(MotionPickerBack, new TextObject("{=BC_CourtPickerBack}Back").ToString(), null, true, ""));
            _activePlayerInquiry = agenda;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject(agenda.Faction == null ? "{=BC_CrownChooseBusiness}Choose Crown business" : "{=BC_CourtAgenda_SubTitle}Substitute Motion").ToString(),
                new TextObject(motions.Count == 0 ? "{=BC_CourtAgenda_NoSubstitutes}No valid substitute motions are available."
                    : agenda.Faction == null ? "{=BC_CrownBusinessPrompt}Which business will you pursue in the name of the Crown?"
                    : "{=BC_CourtAgenda_SubDesc}Which motion will you force onto the {FACTION_NAME}'s agenda?")
                    .SetTextVariable("FACTION_NAME", AgendaOwnerName(agenda)).ToString(), choices, true, 1, 1,
                new TextObject("{=BC_CourtPickerContinue}Continue").ToString(), new TextObject("{=BC_CourtPickerBack}Back").ToString(),
                selected =>
                {
                    if (_activePlayerInquiry != agenda) return;
                    if (selected?.Count == 1 && ReferenceEquals(selected[0].Identifier, MotionPickerBack))
                    { ReturnFromMotionPicker(agenda, identity, state, crisis, null, category == null); return; }
                    _activePlayerInquiry = null;
                    if (!CanChooseTermBusiness(agenda) || identity != agenda.ObjectiveData || state != agenda.State
                        || crisis != agenda.CrisisInterventionPending || selected?.Count != 1) return;
                    if (categories && selected[0].Identifier is string selectedCategory)
                    { ShowTermMotionPicker(agenda, crisis, selectedCategory); return; }
                    ShowPlayerMotionConfirmation(agenda, (PlayerMotion)selected[0].Identifier, crisis, price, category);
                }, _ => ReturnFromMotionPicker(agenda, identity, state, crisis, null, category == null)), true);
        }

        private static readonly object MotionPickerBack = new object();

        private void ReturnFromMotionPicker(CourtAgendaRecord agenda, CourtObjectiveRecord identity,
            CourtAgendaState state, bool crisis, string category, bool toProposal = false)
        {
            if (_activePlayerInquiry != agenda) return;
            _activePlayerInquiry = null;
            if (!CanChooseTermBusiness(agenda) || agenda.ObjectiveData != identity || agenda.State != state
                || crisis != agenda.CrisisInterventionPending) return;
            if (toProposal) ShowTermBusinessInquiry(agenda);
            else ShowTermMotionPicker(agenda, crisis, category);
        }

        private void ShowPlayerMotionConfirmation(CourtAgendaRecord agenda, PlayerMotion motion, bool crisis, int price, string category)
        {
            if (motion.Kind == NominationMotionKind)
            { ShowNominationMandateConfirmation(agenda, motion.Candidate.TargetId, crisis, price, category); return; }
            var identity = agenda.GetObjective();
            var state = agenda.State;
            var session = (motion.Kind == CourtLiberationRules.Kind || motion.Kind == CourtRallyRules.Kind || motion.Kind == CourtPeaceRules.Kind || motion.Kind == CourtCampaignRules.Kind || motion.Kind == CourtSubjugationRules.Kind || motion.Kind == CourtClaimRules.Kind || motion.Kind == CourtDynasticRules.Kind || motion.Kind == CourtProtectionRules.Kind || motion.Kind == CourtTradeRules.Kind || motion.Kind == CourtTitleGrantRules.Grant || motion.Kind == CourtTitleGrantRules.Petition) && identity.HasTermSnapshot
                ? CampaignTime.Days((float)CourtAgendaRules.EarlyObjectiveSession(identity.SelectedDay, CourtAgendaRules.NominationDays(agenda.TermDays),
                    CampaignTime.Now.ToDays, agenda.SessionDate.ToDays)) : agenda.SessionDate;
            var body = new TextObject("{=BC_CourtPickerConfirm}{MOTION}\n\nSelection costs {COST} influence. Filing costs {FILING} influence at the appointed session on {DATE}.\n\n{DETAIL}")
                .SetTextVariable("MOTION", motion.Label).SetTextVariable("COST", price).SetTextVariable("FILING", motion.FilingCost)
                .SetTextVariable("DATE", session.ToString())
                .SetTextVariable("DETAIL", new TextObject(motion.Kind == DebateKind
                    ? "{=BC_CourtPickerDebateDetail}The faction will consider a grand coalition. Its supporters must still prove strong enough to challenge the Crown."
                    : motion.Kind == CourtMandateRules.Kind
                    ? "{=BC_MandateDetail}At the session, the faction files a proposal for the next adjacent mandate length. Deliberation allows you to discuss motives, persuade or bargain for votes. The Crown retains its normal choice in the ballot. Adoption brings +10 approval; rejection -10. The new law also alters the sitting mandate. Fixed terms retain their original start; replacing a lifetime mandate starts the fixed term upon adoption. An overdue mandate leads to an election after deliberation."
                    : motion.Kind == CourtRallyRules.Kind
                    ? "{=BC_RallyDetail}At the session, participating Glory houses gain up to +15 enthusiasm against this enemy for one season, capped by the term end. An NPC Crown favoring Glory shares their resolve. Victory by {UNTIL} brings +10 approval; defeat or inconclusive peace -10; an unfinished campaign -5. No additional filing cost or ballot; your decisions and forced settlements remain unchanged."
                    : motion.Kind == CourtLiberationRules.Kind
                    ? "{=BC_CourtLiberationDetail}At the session, preparations add +20 liberty desire to the realm's political houses until {UNTIL}. No war is declared by this motion. Readiness, enthusiasm, influence costs and the normal declaration vote still apply. The preparations end if the ruler or clientage changes. Beginning a liberation war completes the objective, not the struggle for independence. No faction approval reward or penalty applies."
                    : motion.Kind == CourtTitleGrantRules.Grant || motion.Kind == CourtTitleGrantRules.Petition
                    ? "{=BC_TitleGrantDetail}The Crown may grant this named higher title by {UNTIL}, spending 100 influence at execution. Only rights held by the Crown transfer; subordinate settlements do not, and the recipient remains a vassal. Crown selection authorizes its grant at the session; a Nobility petition still requires royal consent. Personal goodwill and recipient-faction approval apply once, with no jealousy penalty to other factions. An unfulfilled Nobility petition brings -10 approval."
                    : motion.Kind == CourtTradeRules.Kind
                    ? "{=BC_CourtTradeDetail}Seek a trade accord by {UNTIL}. Participating houses and an NPC Crown favoring Liberty receive +15 trade support and prefer this partner during ordinary proposal opportunities. No vote is filed by approving this agenda. Native proposal costs, foreign acceptance and your choices remain unchanged. Signing brings +10 approval, in addition to standing trade benefits; expiry or our deliberate offensive brings -10. Unavoidable invalidation cancels without blame."
                    : motion.Kind == CourtProtectionRules.Kind
                    ? "{=BC_ProtectionDetail}The Crown may send one appeal this term, offering voluntary clientage in exchange for intervention against the named threat. At the session you will confirm whether to send it. An accepted client follows its protector's foreign policy, joins its wars and settles unrelated independent wars and agreements. The prospective protector may refuse; no faction mood penalty applies."
                    : motion.Kind == CourtDynasticRules.Kind
                    ? "{=BC_CourtDynasticDetail}The named marriage must take place by {UNTIL}. Both houses retain their choice. An NPC ruler favoring Nobility receives +15 consideration for this match; participating houses receive +15 preference for an alliance with the named realm until term end, even after the wedding. Marriage alone fulfills the objective (+10 mood); expiry brings -10. No formal alliance, payment waiver or automatic player consent is included."
                    : motion.Kind == CourtClaimRules.Kind
                    ? "{=BC_CourtClaimDetail}Until {UNTIL}, participating houses and an NPC Crown favoring Nobility receive +15 support for war, a treaty securing this fief, and allocation to the named claimant. An aligned ruler also prefers a lawful treaty containing this fief (+80 package preference). Only settled ownership by the claimant fulfills the pledge (+10 mood); failure brings -10. A fief acquired within the term but awaiting allocation receives at most one additional deliberation window, with allocation support only. Your choices, committed votes, eligibility and treaty costs remain unchanged."
                    : motion.Kind == CourtSubjugationRules.Kind
                    ? "{=BC_CourtClientageDetail}Until {UNTIL}, participating houses and an NPC ruler favoring this faction receive +15 support for a lawful declaration and for a treaty granting this realm's clientage. An aligned ruler also receives +40 consideration for that demand and +80 preference for a validated treaty containing it. War, submission and council approval are not automatic; normal costs, truces and your choices remain unchanged. Actual clientage within the term brings +10 mood; failure brings -10. Enemy aggression or peace without submission does not end the objective. Structural impossibility cancels it without penalty."
                    : motion.Kind == CourtCampaignRules.Kind
                    ? "{=BC_CourtCampaignDetail}Until {UNTIL}, participating houses will weigh a lawful declaration against this realm more favorably (+15 war support). No declaration is filed automatically; War Will, influence costs, front restrictions and your own vote remain unchanged. A council-authorized offensive within the term brings +10 faction mood; failure brings -10. If the target attacks first, or unrelated events make the undertaking impossible, it ends without penalty. No filing payment or ballot is required to begin the initiative."
                    : motion.Kind == CourtPeaceRules.Kind
                    ? "{=BC_CourtPeaceDetail}Until {UNTIL}, participating houses will weigh peace with this realm more favorably (+15 acceptance), alongside their other interests. No parley opens automatically and your vote remains yours. Peace concluded within the term brings +10 faction mood; failure brings -10. Unrelated invalidation ends the undertaking without penalty. No filing payment or ballot is required to begin it."
                    : motion.Kind == CourtAppeasementRules.Kind
                    ? "{=BC_CrownReconcileDetail}At the appointed session, the Crown will spend {PRICE} influence to grant the faction +20 mood for {DAYS} days. No ballot is required. This accommodation cannot stack or be renewed while active; ruler replacement ends it. Membership or circumstances changing before the session withdraws the offer without a new price. Scheduled tyranny debates still require mood of at least -20 to withdraw; issued ultimata and rebellions cannot be bought off."
                    : motion.Kind == CourtActivityRules.Kind
                    ? "{=BC_CourtPickerActivityDetail}This undertaking requires no ballot. Its recipients will be chosen when the agenda is confirmed; changed mood, ruler or circumstances may prevent its fulfillment."
                    : "{=BC_CourtPickerVoteDetail}The motion will enter the existing deliberation and voting process. Approval of this agenda does not guarantee its passage.")
                    .SetTextVariable("PRICE", motion.FilingCost).SetTextVariable("DAYS", agenda.TermDays)
                    .SetTextVariable("UNTIL", CampaignTime.Days((float)identity.DeadlineDay).ToString()));
            _activePlayerInquiry = agenda;
            InformationManager.ShowInquiry(new InquiryData(motion.Label.ToString(), body.ToString(), true, true,
                new TextObject("{=BC_CourtConfirmAgenda}Confirm").ToString(), new TextObject("{=BC_CourtPickerBack}Back").ToString(), () =>
                {
                    if (_activePlayerInquiry != agenda) return;
                    _activePlayerInquiry = null;
                    if (!CanChooseTermBusiness(agenda) || !agenda.SessionDate.IsFuture || agenda.ObjectiveData != identity || agenda.State != state
                        || agenda.CrisisInterventionPending != crisis || ReplacementCost(agenda, crisis) != price) return;
                    var current = AvailablePlayerMotions(agenda).FirstOrDefault(m => SamePlayerMotion(m, motion));
                    if (current == null || current.FilingCost != motion.FilingCost) return;
                    // Prepare without mutating the live agenda or charging for a cancelled preview.
                    var prepared = new CourtAgendaRecord { Realm = agenda.Realm, Sponsor = agenda.Sponsor, Faction = agenda.Faction,
                        SessionDate = agenda.SessionDate, VoteDate = agenda.VoteDate, TermDays = agenda.TermDays, HasScheduleSnapshot = agenda.HasScheduleSnapshot,
                        ObjectiveData = new CourtObjectiveRecord { Kind = "policy" } };
                    if (identity.HasTermSnapshot) prepared.ObjectiveData.FreezeTerm(identity.SelectedDay, identity.DeadlineDay);
                    if (motion.Kind != DebateKind)
                        ((ICourtAgendaObjectiveSource)_objectiveSelector.FindSource(motion.Kind)).ApplySelection(prepared,
                            new CourtObjectiveChoice(motion.Kind, motion.Candidate, new CourtObjectiveEvaluation(true, true, new CourtObjectiveWeight(1), "player_selection")));
                    if (motion.Kind == CourtActivityRules.Kind && prepared.Activity == null) return;
                    agenda.PlayerSelectionConfirmed = true;
                    if (price > 0 && !NpcInfluenceBudgetService.TrySpend(Clan.PlayerClan, price,
                        crisis ? NpcInfluenceExpenseKind.CrownEmergency : NpcInfluenceExpenseKind.Discretionary, "court_term_substitute"))
                    { agenda.PlayerSelectionConfirmed = false; return; }
                    agenda.SubstitutionInfluencePaid = price;
                    agenda.ObjectiveData = prepared.ObjectiveData;
                    agenda.PolicyId = prepared.PolicyId;
                    agenda.Abolish = prepared.Abolish;
                    agenda.HasPolicyStanceSnapshot = prepared.HasPolicyStanceSnapshot;
                    agenda.SelectedPolicyStance = prepared.SelectedPolicyStance;
                    agenda.Activity = prepared.Activity;
                    agenda.Appeasement = prepared.Appeasement;
                    agenda.Peace = prepared.Peace;
                    agenda.Campaign = prepared.Campaign;
                    agenda.Subjugation = prepared.Subjugation;
                    agenda.Claim = prepared.Claim;
                    agenda.Dynastic = prepared.Dynastic;
                    agenda.Trade = prepared.Trade;
                    agenda.TitleGrant = prepared.TitleGrant;
                    agenda.Rally = prepared.Rally;
                    agenda.Mandate = prepared.Mandate;
                    agenda.Liberation = prepared.Liberation;
                    agenda.Protection = prepared.Protection;
                    agenda.SessionDate = prepared.SessionDate;
                    agenda.OriginalHolder = prepared.OriginalHolder;
                    agenda.PreferredCouncilCandidate = prepared.PreferredCouncilCandidate;
                    agenda.CouncilMotionId = prepared.CouncilMotionId;
                    agenda.CrisisInterventionPending = false;
                    agenda.OrdinaryCrisisRestrained |= crisis;
                    agenda.State = motion.Kind == DebateKind ? CourtAgendaState.Crisis : CourtAgendaState.Announced;
                    if (crisis) ApplyMemberMemory(agenda, BellumCivileConstants.CourtAgendaDeclineRelationPenalty,
                        RelationMemorySources.OverruledMyFaction, 7f);
                    BellumCivileLogger.Log($"Court player selection; realm={agenda.Realm.StringId}; owner={agenda.Faction?.Type.ToString() ?? "Crown"}; kind={motion.Kind}; target={motion.Candidate.TargetId}; selection_cost={price}; session={agenda.SessionDate.ToDays}.");
                    NotifyAgenda(agenda);
                }, () => ReturnFromMotionPicker(agenda, identity, state, crisis, category)), true);
        }
    }
}
