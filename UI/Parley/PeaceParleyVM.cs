using System.Linq;
using System;
using System.Collections.Generic;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.Parley
{
    public sealed partial class PeaceParleyVM : ViewModel
    {
        private string _playerRealmName;
        private string _opponentRealmName;
        private string _warScoreText;
        private string _budgetText;
        private Color _budgetColor;
        private bool _isPlayerRuler;
        private bool _isPlayerVassal;
        private bool _isInfluenceSelectionDisabled;
        private bool _isVoteNaySelected;
        private bool _isVoteAbstainSelected;
        private bool _isVoteYaySelected;
        private bool _isCommit25Selected;
        private bool _isCommit75Selected;
        private bool _isCommit150Selected;
        private bool _isRejectDisabled;
        private bool _isSignDisabled;
        private HintViewModel _rejectTreatyHint;
        private HintViewModel _signTreatyHint;
        private HintViewModel _resetTreatyHint;
        private BasicTooltipViewModel _predictedOutcomeTooltip;
        private string _playerVoteText;
        private string _playerCommitmentText;
        private string _playerRatificationText;
        private string _opponentRatificationText;
        private string _predictedOutcomeText;
        private Color _playerRatificationColor;
        private Color _opponentRatificationColor;
        private Color _predictedOutcomeColor;
        private bool _isFiefsTabSelected = true;
        private bool _isPrisonersTabSelected;
        private bool _isPoliticsTabSelected;
        private bool _isWealthTabSelected;
        private bool _isDraftEditingDisabled;
        private string _reparationsText;
        private string _reparationsCostText;
        private string _tributeText;
        private string _tributeCostText;
        private HintViewModel _prisonersTabHint;
        private HintViewModel _politicsTabHint;
        private HintViewModel _draftTabDisabledHint;
        private int _draftEditorHeight;
        private int _upperPanelHeight;
        private int _lowerPanelHeight;
        private int _draftTermsHeight;
        private bool _isFiefDraftScrollbarVisible;
        private bool _isPrisonerDraftScrollbarVisible;
        private bool _isPoliticsDraftScrollbarVisible;
        private bool _isDraftTermsScrollbarVisible;
        private bool _isDemandsMode = true;
        private bool _isOfferingsMode;
        private readonly Action _closeAction;
        private readonly List<TreatyTermRecord> _initialDraftTerms;
        private TreatyProposalRecord _proposal;
        private const int WealthAdjustmentStep = 1;
        private const int WealthShiftAdjustmentStep = 5;

        public PeaceParleyVM(TreatyProposalRecord proposal, Action closeAction)
        {
            _closeAction = closeAction;
            _initialDraftTerms = CloneTerms(proposal?.Terms);
            PlayerCouncil = new MBBindingList<TreatyCouncilMemberVM>();
            OpponentCouncil = new MBBindingList<TreatyCouncilMemberVM>();
            DraftTerms = new MBBindingList<TreatyTermVM>();
            AvailableFiefTerms = new MBBindingList<TreatyFiefDraftOptionVM>();
            AvailablePrisonerTerms = new MBBindingList<TreatyPrisonerDraftOptionVM>();
            AvailablePoliticsTerms = new MBBindingList<TreatyClaimDraftOptionVM>();
            Refresh(proposal);
        }

        public void Refresh(TreatyProposalRecord proposal)
        {
            _proposal = proposal;
            ForeignTreatyBehavior treaties = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>();
            WarScoreRecord war = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(proposal?.WarKey);
            Kingdom winner = Kingdom.All.FirstOrDefault(kingdom => kingdom?.StringId == proposal?.WinnerKingdomId);
            Kingdom loser = Kingdom.All.FirstOrDefault(kingdom => kingdom?.StringId == proposal?.LoserKingdomId);
            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            bool playerWins = playerKingdom != null && playerKingdom == winner;
            Kingdom playerSide = playerWins ? winner : loser;
            Kingdom opponent = playerWins ? loser : winner;

            PlayerRealmName = ResolveRealmTitle(playerSide);
            OpponentRealmName = ResolveRealmTitle(opponent);
            int displayedWarScore = proposal == null
                ? 0
                : playerWins
                    ? proposal.WarScoreBudget
                    : -proposal.WarScoreBudget;
            WarScoreText = new TextObject("{=BC_Parley_WarScore}War Score: {VALUE}")
                .SetTextVariable("VALUE", displayedWarScore.ToString("+0;-0;0"))
                .ToString();
            bool isOverBudget = proposal?.IsOverBudget == true;
            BudgetText = new TextObject(isOverBudget
                ? "{=BC_Parley_DraftedTermsOverBudget}Drafted Terms ({USED} / {BUDGET} WS; +{EXCESS})"
                : "{=BC_Parley_DraftedTermsBudget}Drafted Terms ({USED} / {BUDGET} WS)")
                .SetTextVariable("USED", proposal?.UsedWarScore ?? 0)
                .SetTextVariable("BUDGET", proposal?.WarScoreBudget ?? 0)
                .SetTextVariable("EXCESS", proposal?.BudgetOverrun ?? 0)
                .ToString();
            BudgetColor = isOverBudget ? BuildOutcomeColor(false) : Color.ConvertStringToColor("#FFFFFFFF");
            IsPlayerRuler = playerKingdom?.RulingClan == Clan.PlayerClan;
            IsPlayerVassal = playerKingdom != null && !IsPlayerRuler;
            IsDraftEditingDisabled = !IsPlayerRuler;
            UpperPanelHeight = IsPlayerVassal ? 280 : 538;
            LowerPanelHeight = IsPlayerVassal ? 609 : 351;
            DraftEditorHeight = IsPlayerVassal ? 118 : 376;
            DraftTermsHeight = IsPlayerVassal ? 277 : 199;
            IsRejectDisabled = !IsPlayerRuler || proposal?.IsForced == true;
            TreatyCouncilVoteStance playerStance = proposal?.PlayerVoteStance ?? TreatyCouncilVoteStance.Abstain;
            IsInfluenceSelectionDisabled = !IsPlayerVassal || playerStance == TreatyCouncilVoteStance.Abstain;
            bool hasSubmittedVote = proposal?.PlayerVoteSubmitted == true;
            IsVoteNaySelected = hasSubmittedVote && playerStance == TreatyCouncilVoteStance.Nay;
            IsVoteAbstainSelected = hasSubmittedVote && playerStance == TreatyCouncilVoteStance.Abstain;
            IsVoteYaySelected = hasSubmittedVote && playerStance == TreatyCouncilVoteStance.Yay;
            int playerCommitment = proposal?.PlayerInfluenceCommitment ?? 0;
            bool hasCommittedVote = hasSubmittedVote && playerStance != TreatyCouncilVoteStance.Abstain;
            IsCommit25Selected = hasCommittedVote && playerCommitment == 25;
            IsCommit75Selected = hasCommittedVote && playerCommitment == 75;
            IsCommit150Selected = hasCommittedVote && playerCommitment == 150;
            PlayerVoteText = new TaleWorlds.Localization.TextObject("{=BC_Parley_YourVote}Your Vote: {VALUE}")
                .SetTextVariable("VALUE", proposal?.PlayerVoteSubmitted == true
                    ? BuildPlayerVoteText(playerStance)
                    : new TextObject("{=BC_Parley_VoteUndecided}UNDECIDED").ToString())
                .ToString();
            PlayerCommitmentText = new TaleWorlds.Localization.TextObject("{=BC_Parley_Commitment}Influence commitment: {VALUE}")
                .SetTextVariable("VALUE", proposal?.PlayerInfluenceCommitment ?? 0)
                .ToString();

            PlayerCouncil.Clear();
            OpponentCouncil.Clear();
            DraftTerms.Clear();
            AvailableFiefTerms.Clear();
            AvailablePrisonerTerms.Clear();
            AvailablePoliticsTerms.Clear();
            IsFiefDraftScrollbarVisible = false;
            IsPrisonerDraftScrollbarVisible = false;
            IsPoliticsDraftScrollbarVisible = false;
            IsDraftTermsScrollbarVisible = false;
            IsSignDisabled = true;
            if (treaties != null && war != null && proposal != null)
            {
                TreatyCouncilEvaluation playerCouncil = treaties.EvaluateCouncil(war, proposal, playerSide, playerWins);
                TreatyCouncilEvaluation opponentCouncil = treaties.EvaluateCouncil(war, proposal, opponent, !playerWins);
                // At terminal war score the settlement is compulsory for both courts: the defeated
                // realm capitulates and the victor ratifies the terms it has imposed.
                bool playerForced = proposal.IsForced && !isOverBudget;
                bool opponentForced = proposal.IsForced && !isOverBudget;
                bool playerAccepts = treaties.WouldCouncilAccept(playerSide, playerCouncil, playerForced, IsPlayerRuler, out bool playerUsesOverride);
                bool opponentAccepts = treaties.WouldCouncilAccept(opponent, opponentCouncil, opponentForced, playerRulerAuthorizesOverride: false, out bool opponentUsesOverride);

                PlayerRatificationText = BuildCouncilStatusText(playerSide, playerCouncil, playerForced, playerAccepts, playerUsesOverride);
                OpponentRatificationText = BuildCouncilStatusText(opponent, opponentCouncil, opponentForced, opponentAccepts, opponentUsesOverride);
                PlayerRatificationColor = BuildOutcomeColor(playerAccepts);
                OpponentRatificationColor = BuildOutcomeColor(opponentAccepts);
                string playerRulerCouncilReason = BuildRulerCouncilReason(
                    playerSide,
                    playerCouncil,
                    playerAccepts,
                    playerUsesOverride);
                string opponentRulerCouncilReason = BuildRulerCouncilReason(
                    opponent,
                    opponentCouncil,
                    opponentAccepts,
                    opponentUsesOverride);
                bool treatyWillPass = !isOverBudget && playerAccepts && opponentAccepts;
                PredictedOutcomeText = treatyWillPass
                    ? new TextObject("{=BC_Parley_PredictedRatified}Predicted Outcome: TREATY RATIFIED").ToString()
                    : new TextObject("{=BC_Parley_PredictedRejected}Predicted Outcome: TREATY REJECTED").ToString();
                PredictedOutcomeColor = BuildOutcomeColor(treatyWillPass);
                _predictedOutcomeTooltip = new BasicTooltipViewModel(() => BuildPredictedOutcomeTooltip(
                    PlayerRatificationText,
                    OpponentRatificationText));
                bool hasValidPlayerResponse = IsPlayerRuler
                    || (proposal.PlayerVoteSubmitted
                        && (playerStance == TreatyCouncilVoteStance.Abstain || proposal.PlayerInfluenceCommitment > 0));
                IsSignDisabled = isOverBudget || !hasValidPlayerResponse;
                foreach (TreatyCouncilMemberEvaluation member in OrderCouncilMembers(playerCouncil.Members, playerSide?.RulingClan))
                {
                    bool isRuler = member?.Clan == playerSide?.RulingClan;
                    int overrideCommitment = isRuler
                        ? GetRulerOverrideCommitment(playerCouncil, playerAccepts, playerUsesOverride)
                        : 0;
                    PlayerCouncil.Add(new TreatyCouncilMemberVM(
                        member,
                        isRuler,
                        playerAccepts,
                        overrideCommitment,
                        isRuler ? playerRulerCouncilReason : null));
                }
                foreach (TreatyCouncilMemberEvaluation member in OrderCouncilMembers(opponentCouncil.Members, opponent?.RulingClan))
                {
                    bool isRuler = member?.Clan == opponent?.RulingClan;
                    int overrideCommitment = isRuler
                        ? GetRulerOverrideCommitment(opponentCouncil, opponentAccepts, opponentUsesOverride)
                        : 0;
                    OpponentCouncil.Add(new TreatyCouncilMemberVM(
                        member,
                        isRuler,
                        opponentAccepts,
                        overrideCommitment,
                        isRuler ? opponentRulerCouncilReason : null));
                }
                IReadOnlyList<int> termImpacts = TreatyWarScoreAccounting.GetEffectiveTermImpacts(
                    proposal.Terms,
                    proposal.WinnerKingdomId,
                    proposal.WarScoreBudget);
                for (int index = 0; index < proposal.Terms.Count; index++)
                    DraftTerms.Add(new TreatyTermVM(
                        proposal.Terms[index],
                        proposal.WinnerKingdomId,
                        termImpacts[index]));

                Kingdom sourceRealm = IsDemandsMode ? opponent : playerSide;
                Kingdom receivingRealm = IsDemandsMode ? playerSide : opponent;
                HashSet<string> selectedFiefs = new HashSet<string>(proposal.Terms
                    .Where(term => term?.Type == TreatyTermType.TransferFief
                        && term.FromKingdomId == sourceRealm?.StringId
                        && term.ToKingdomId == receivingRealm?.StringId)
                    .Select(term => term.SettlementId));
                List<TreatyFiefTransferCandidate> availableFiefs = TreatyDraftService
                    .GetAvailableFiefTransfers(war, sourceRealm, receivingRealm)
                    .ToList();
                foreach (TreatyFiefTransferCandidate candidate in availableFiefs)
                {
                    AvailableFiefTerms.Add(new TreatyFiefDraftOptionVM(
                        candidate.Settlement,
                        candidate.WarScoreCost,
                        selectedFiefs.Contains(candidate.Settlement.StringId),
                        IsDraftEditingDisabled,
                        ToggleFiefTerm,
                        receivingRealm,
                        candidate.IsOccupied,
                        candidate.IncludedPrisoners.Count));
                }
                foreach (var candidate in ClientWarTerritory.Candidates(war, sourceRealm, receivingRealm))
                    AvailableFiefTerms.Add(new TreatyFiefDraftOptionVM(candidate.Fief, candidate.Cost,
                        proposal.Terms.Any(t => t.Type == TreatyTermType.RecognizeClientOccupation
                            && t.FromKingdomId == sourceRealm.StringId && t.ToKingdomId == receivingRealm.StringId && candidate.Matches(t)),
                        IsDraftEditingDisabled, ToggleClientOccupationTerm, candidate.Client, true, 0, candidate.Client));
                IsFiefDraftScrollbarVisible = AvailableFiefTerms.Sum(f => f.RowHeight) > DraftEditorHeight;

                HashSet<string> selectedPrisoners = new HashSet<string>(proposal.Terms
                    .Where(term => term?.Type == TreatyTermType.ReleasePrisoner
                        && term.FromKingdomId == sourceRealm?.StringId
                        && term.ToKingdomId == receivingRealm?.StringId)
                    .Select(term => term.HeroId));
                HashSet<string> prisonersIncludedWithFiefs = new HashSet<string>(availableFiefs
                    .Where(candidate => selectedFiefs.Contains(candidate.Settlement.StringId))
                    .SelectMany(candidate => candidate.IncludedPrisoners)
                    .Select(candidate => candidate.Hero.StringId));
                foreach (TreatyPrisonerReleaseCandidate candidate in TreatyDraftService.GetAvailablePrisonerReleases(sourceRealm, receivingRealm))
                {
                    AvailablePrisonerTerms.Add(new TreatyPrisonerDraftOptionVM(
                        candidate.Hero,
                        candidate.HoldingSettlement,
                        candidate.WarScoreCost,
                        selectedPrisoners.Contains(candidate.Hero.StringId),
                        IsDraftEditingDisabled,
                        TogglePrisonerTerm,
                        prisonersIncludedWithFiefs.Contains(candidate.Hero.StringId)));
                }
                IsPrisonerDraftScrollbarVisible = AvailablePrisonerTerms.Count * 38 > DraftEditorHeight;

                HashSet<string> selectedClaims = new HashSet<string>(proposal.Terms
                    .Where(term => term?.Type == TreatyTermType.RenounceClaim
                        && term.FromKingdomId == sourceRealm?.StringId
                        && term.ToKingdomId == receivingRealm?.StringId)
                    .Select(term => term.ClanId + "|" + term.TitleId));
                foreach (TreatyClaimRenunciationCandidate candidate in TreatyDraftService.GetAvailableClaimRenunciations(sourceRealm, receivingRealm))
                {
                    string key = candidate.ClaimantClan.StringId + "|" + candidate.Title.TitleId;
                    AvailablePoliticsTerms.Add(new TreatyClaimDraftOptionVM(
                        candidate,
                        selectedClaims.Contains(key),
                        IsDraftEditingDisabled,
                        ToggleClaimTerm));
                }
                TreatyTermRecord selectedRebuke = proposal.Terms.FirstOrDefault(term =>
                    (term?.Type == TreatyTermType.DiscreditRuler || term?.Type == TreatyTermType.HumiliateRuler)
                    && term.FromKingdomId == sourceRealm?.StringId
                    && term.ToKingdomId == receivingRealm?.StringId);
                Hero targetRuler = sourceRealm?.RulingClan?.Leader;
                if (targetRuler != null)
                {
                    AvailablePoliticsTerms.Add(new TreatyClaimDraftOptionVM(
                        "discredit",
                        new TextObject("{=BC_Parley_Politics_DiscreditOption}Discredit {RULER_NAME}")
                            .SetTextVariable("RULER_NAME", targetRuler.Name).ToString(),
                        BellumCivileConstants.TreatyDiscreditRulerCost,
                        selectedRebuke?.Type == TreatyTermType.DiscreditRuler,
                        IsDraftEditingDisabled,
                        ToggleRulerRebukeTerm,
                        new TextObject("{=BC_Parley_Politics_DiscreditHint}Publicly discredits {RULER_NAME}: 30 renown and 60 influence pass from the defeated ruling clan to the victorious ruling clan. Relations between the two rulers fall by 10. This replaces any other prestige concession in the treaty.")
                            .SetTextVariable("RULER_NAME", targetRuler.Name)));
                    AvailablePoliticsTerms.Add(new TreatyClaimDraftOptionVM(
                        "humiliate",
                        new TextObject("{=BC_Parley_Politics_HumiliateOption}Humiliate {RULER_NAME}")
                            .SetTextVariable("RULER_NAME", targetRuler.Name).ToString(),
                        BellumCivileConstants.TreatyHumiliateRulerCost,
                        selectedRebuke?.Type == TreatyTermType.HumiliateRuler,
                        IsDraftEditingDisabled,
                        ToggleRulerRebukeTerm,
                        new TextObject("{=BC_Parley_Politics_HumiliateHint}Publicly humiliates {RULER_NAME}: 50 renown and 100 influence pass from the defeated ruling clan to the victorious ruling clan. Relations between the two rulers fall by 25. This replaces any other prestige concession in the treaty.")
                            .SetTextVariable("RULER_NAME", targetRuler.Name)));
                }
                HashSet<string> selectedReleases = new HashSet<string>(proposal.Terms
                    .Where(term => term?.Type == TreatyTermType.ReleaseVassal
                        && term.FromKingdomId == sourceRealm?.StringId
                        && term.ToKingdomId == receivingRealm?.StringId)
                    .Select(term => term.ClanId + "|" + term.TitleId));
                foreach (TreatyVassalReleaseCandidate candidate in TreatyRealmTransitionService.GetReleaseCandidates(sourceRealm))
                {
                    string key = "release|" + candidate.LeaderClan.StringId + "|" + candidate.RootTitle.TitleId;
                    string selectionKey = candidate.LeaderClan.StringId + "|" + candidate.RootTitle.TitleId;
                    AvailablePoliticsTerms.Add(new TreatyClaimDraftOptionVM(
                        key,
                        new TextObject("{=BC_Parley_Politics_ReleaseVassalOption}Release {CLAN_NAME} as {TITLE_NAME}")
                            .SetTextVariable("CLAN_NAME", candidate.LeaderClan.Name)
                            .SetTextVariable("TITLE_NAME", new TextObject(FeudalTitleDisplayHelper.FormatTitleName(candidate.RootTitle, candidate.LeaderClan)))
                            .ToString(),
                        candidate.WarScoreCost,
                        selectedReleases.Contains(selectionKey),
                        IsDraftEditingDisabled,
                        ToggleStructuralPoliticalTerm,
                        new TextObject("{=BC_Parley_Politics_ReleaseVassalHint}Releases {CLAN_NAME} and its de facto title-vassal cluster from {REALM_NAME} as an independent realm. Existing de jure title bonds remain intact.")
                            .SetTextVariable("CLAN_NAME", candidate.LeaderClan.Name)
                            .SetTextVariable("REALM_NAME", sourceRealm.Name)));
                }
                if (TreatyRealmTransitionService.TryGetForceVassalizationCandidate(receivingRealm, sourceRealm,
                    out TreatyForceVassalizationCandidate forceCandidate, out _, proposal.Terms))
                {
                    bool selected = proposal.Terms.Any(term => term?.Type == TreatyTermType.ForceVassalization
                        && term.FromKingdomId == sourceRealm.StringId
                        && term.ToKingdomId == receivingRealm.StringId);
                    AvailablePoliticsTerms.Add(new TreatyClaimDraftOptionVM(
                        "force_vassalization",
                        new TextObject("{=BC_Parley_Politics_ForceVassalizationOption}Force the vassalization of {REALM_NAME}")
                            .SetTextVariable("REALM_NAME", sourceRealm.Name).ToString(),
                        forceCandidate.WarScoreCost,
                        selected,
                        IsDraftEditingDisabled,
                        ToggleStructuralPoliticalTerm,
                        new TextObject("{=BC_Parley_Politics_ForceVassalizationHint}Ends {REALM_NAME}'s independence and absorbs its clans as de facto vassals of {VICTOR_REALM}. De jure title ownership is not rewritten.")
                            .SetTextVariable("REALM_NAME", sourceRealm.Name)
                            .SetTextVariable("VICTOR_REALM", receivingRealm.Name)));
                }
                if (TreatyDraftService.TryGetClientKingdomCandidate(receivingRealm, sourceRealm,
                    out TreatyClientKingdomCandidate clientCandidate, out _, proposal.Terms))
                {
                    bool selected = proposal.Terms.Any(term => term?.Type == TreatyTermType.MakeClientKingdom
                        && term.FromKingdomId == sourceRealm.StringId
                        && term.ToKingdomId == receivingRealm.StringId);
                    TextObject clientLabel = IsOfferingsMode
                        ? new TextObject("{=BC_Parley_Politics_OfferClientKingdomOption}Enter the clientage of {REALM_NAME}")
                        : new TextObject("{=BC_Parley_Politics_MakeClientKingdomOption}Make {REALM_NAME} a client kingdom");
                    AvailablePoliticsTerms.Add(new TreatyClaimDraftOptionVM(
                        "make_client",
                        clientLabel.SetTextVariable("REALM_NAME", sourceRealm.Name).ToString(),
                        clientCandidate.WarScoreCost,
                        selected,
                        IsDraftEditingDisabled,
                        ToggleStructuralPoliticalTerm,
                        new TextObject(IsOfferingsMode
                                ? "{=BC_Parley_Politics_OfferClientKingdomHint}{REALM_NAME} offers to become a client of {SUZERAIN_REALM}, retaining its internal realm while surrendering independent foreign policy."
                                : "{=BC_Parley_Politics_MakeClientKingdomHint}Forces {REALM_NAME} into clientage under {SUZERAIN_REALM}. The client retains its internal realm but follows its suzerain's wars and cannot conduct independent alliances or trade agreements.")
                            .SetTextVariable("REALM_NAME", sourceRealm.Name)
                            .SetTextVariable("SUZERAIN_REALM", receivingRealm.Name)));
                }
                bool concedeSelected = proposal.Terms.Any(term => term?.Type == TreatyTermType.ConcedeDefeat);
                Kingdom treatyWinner = Kingdom.All.FirstOrDefault(kingdom => kingdom?.StringId == proposal.WinnerKingdomId);
                Kingdom treatyLoser = Kingdom.All.FirstOrDefault(kingdom => kingdom?.StringId == proposal.LoserKingdomId);
                if (sourceRealm == treatyLoser && receivingRealm == treatyWinner)
                {
                    AvailablePoliticsTerms.Add(new TreatyClaimDraftOptionVM(
                        "concede_defeat",
                        new TextObject("{=BC_Parley_Politics_ConcedeDefeatOption}Concede defeat").ToString(),
                        BellumCivileConstants.TreatyConcedeDefeatCost,
                        concedeSelected,
                        IsDraftEditingDisabled,
                        ToggleNewPoliticalTerm,
                        new TextObject("{=BC_Parley_Politics_ConcedeDefeatHint}Publicly acknowledges the opposing realm's victory. 15 renown and 30 influence pass from the defeated ruling clan to the victorious ruling clan. This is the mildest prestige concession and cannot be combined with discrediting or humiliating the defeated ruler.")));
                }
                foreach (TreatyClientReleaseCandidate candidate in TreatyDraftService.GetAvailableClientReleases(sourceRealm, receivingRealm))
                {
                    bool selected = proposal.Terms.Any(term => term?.Type == TreatyTermType.ReleaseClientState
                        && term.FromKingdomId == sourceRealm.StringId
                        && term.ToKingdomId == receivingRealm.StringId
                        && term.ThirdKingdomId == candidate.ClientRealm.StringId);
                    AvailablePoliticsTerms.Add(new TreatyClaimDraftOptionVM(
                        "release_client|" + candidate.ClientRealm.StringId,
                        new TextObject("{=BC_Parley_Politics_ReleaseClientOption}Release client state: {REALM_NAME}")
                            .SetTextVariable("REALM_NAME", candidate.ClientRealm.Name).ToString(),
                        candidate.WarScoreCost,
                        selected,
                        IsDraftEditingDisabled,
                        ToggleNewPoliticalTerm,
                        new TextObject("{=BC_Parley_Politics_ReleaseClientHint}Forces {SUZERAIN_REALM} to release {CLIENT_REALM} from clientage. The liberating ruler gains relations with the freed realm, while relations with its former suzerain deteriorate.")
                            .SetTextVariable("SUZERAIN_REALM", sourceRealm.Name)
                            .SetTextVariable("CLIENT_REALM", candidate.ClientRealm.Name)));
                }
                foreach (TreatyRebelDemandCandidate candidate in TreatyDraftService.GetAvailableRebelDemandEnforcements(sourceRealm, receivingRealm))
                {
                    bool selected = proposal.Terms.Any(term => term?.Type == TreatyTermType.EnforceRebelDemands
                        && term.FromKingdomId == sourceRealm.StringId
                        && term.ToKingdomId == receivingRealm.StringId
                        && term.ThirdKingdomId == candidate.RebelRealm.StringId);
                    AvailablePoliticsTerms.Add(new TreatyClaimDraftOptionVM(
                        "enforce_rebels|" + candidate.RebelRealm.StringId,
                        new TextObject("{=BC_Parley_Politics_EnforceRebelsOption}Enforce the demands of {FACTION_NAME}")
                            .SetTextVariable("FACTION_NAME", candidate.Faction.GetDisplayName()).ToString(),
                        candidate.WarScoreCost,
                        selected,
                        IsDraftEditingDisabled,
                        ToggleNewPoliticalTerm,
                        new TextObject("{=BC_Parley_Politics_EnforceRebelsHint}After the foreign peace is concluded, {PARENT_REALM} must capitulate to {FACTION_NAME}. The civil war then follows its normal rebel-victory resolution, including elections, restoration, independence, or tribunal where applicable.")
                            .SetTextVariable("PARENT_REALM", sourceRealm.Name)
                            .SetTextVariable("FACTION_NAME", candidate.Faction.GetDisplayName())));
                }
                AddHostageOption(sourceRealm, receivingRealm);
                TreatyTermRecord selectedMarriage = proposal.Terms.FirstOrDefault(term => term?.Type == TreatyTermType.ArrangeRoyalMarriage
                    && term.FromKingdomId == sourceRealm?.StringId
                    && term.ToKingdomId == receivingRealm?.StringId);
                IReadOnlyList<TreatyRoyalMarriageCandidate> marriageCandidates = TreatyRoyalMarriageService.GetCandidates(sourceRealm, receivingRealm);
                if (selectedMarriage != null || marriageCandidates.Count > 0)
                {
                    Hero concedingSpouse = Hero.AllAliveHeroes.FirstOrDefault(hero => hero?.StringId == selectedMarriage?.HeroId);
                    Hero receivingSpouse = Hero.AllAliveHeroes.FirstOrDefault(hero => hero?.StringId == selectedMarriage?.SecondaryHeroId);
                    string marriageName = selectedMarriage != null
                        ? new TextObject("{=BC_Parley_Politics_RoyalMarriageSelected}Royal marriage: {FIRST_NAME} and {SECOND_NAME}")
                            .SetTextVariable("FIRST_NAME", concedingSpouse?.Name ?? TextObject.GetEmpty())
                            .SetTextVariable("SECOND_NAME", receivingSpouse?.Name ?? TextObject.GetEmpty()).ToString()
                        : new TextObject("{=BC_Parley_Politics_RoyalMarriageOption}Arrange a royal marriage").ToString();
                    AvailablePoliticsTerms.Add(new TreatyClaimDraftOptionVM(
                        "royal_marriage",
                        marriageName,
                        BellumCivileConstants.TreatyRoyalMarriageCost,
                        selectedMarriage != null,
                        IsDraftEditingDisabled,
                        ToggleRoyalMarriageTerm,
                        new TextObject("{=BC_Parley_Politics_RoyalMarriageHint}Arranges a marriage between eligible relatives of both ruling houses. The conceding spouse joins the demanding ruler's clan when the treaty is ratified.")));
                }
                HashSet<string> selectedSeverances = new HashSet<string>(proposal.Terms
                    .Where(term => (term?.Type == TreatyTermType.EndTradeAgreement || term?.Type == TreatyTermType.EndAlliance)
                        && term.FromKingdomId == sourceRealm?.StringId
                        && term.ToKingdomId == receivingRealm?.StringId)
                    .Select(term => term.Type + "|" + term.ThirdKingdomId));
                foreach (TreatyDiplomaticSeveranceCandidate candidate in TreatyDraftService.GetAvailableDiplomaticSeverances(sourceRealm, receivingRealm))
                {
                    bool alliance = candidate.TermType == TreatyTermType.EndAlliance;
                    string key = (alliance ? "sever_alliance|" : "sever_trade|") + candidate.ThirdRealm.StringId;
                    string selectionKey = candidate.TermType + "|" + candidate.ThirdRealm.StringId;
                    TextObject label = alliance
                        ? new TextObject("{=BC_Parley_Politics_EndAllianceOption}End alliance with {REALM_NAME}")
                        : new TextObject("{=BC_Parley_Politics_EndTradeOption}End trade agreement with {REALM_NAME}");
                    label.SetTextVariable("REALM_NAME", candidate.ThirdRealm.Name);
                    AvailablePoliticsTerms.Add(new TreatyClaimDraftOptionVM(
                        key,
                        label.ToString(),
                        candidate.WarScoreCost,
                        selectedSeverances.Contains(selectionKey),
                        IsDraftEditingDisabled,
                        ToggleDiplomaticSeveranceTerm,
                        new TextObject(alliance
                                ? "{=BC_Parley_Politics_EndAllianceHint}Forces {SOURCE_REALM} to end its alliance with {THIRD_REALM}. This damages relations between those realms."
                                : "{=BC_Parley_Politics_EndTradeHint}Forces {SOURCE_REALM} to end its trade agreement with {THIRD_REALM}. This mildly damages relations between those realms.")
                            .SetTextVariable("SOURCE_REALM", sourceRealm.Name)
                            .SetTextVariable("THIRD_REALM", candidate.ThirdRealm.Name)));
                }
                IsPoliticsDraftScrollbarVisible = AvailablePoliticsTerms.Count * 38 > DraftEditorHeight - HostageControlsHeight;
                IsDraftTermsScrollbarVisible = DraftTerms.Count * 32 > DraftTermsHeight;

                int reparationsScore = proposal.Terms.FirstOrDefault(term => term?.Type == TreatyTermType.Reparations
                    && term.FromKingdomId == sourceRealm?.StringId && term.ToKingdomId == receivingRealm?.StringId)?.WarScoreCost ?? 0;
                int tributeScore = proposal.Terms.FirstOrDefault(term => term?.Type == TreatyTermType.Tribute
                    && term.FromKingdomId == sourceRealm?.StringId && term.ToKingdomId == receivingRealm?.StringId)?.WarScoreCost ?? 0;
                ReparationsText = new TextObject("{=BC_Parley_ReparationsDraft}Reparations: {GOLD} denars")
                    .SetTextVariable("GOLD", TreatyTermCostModel.GetReparationsForWarScore(reparationsScore))
                    .ToString();
                ReparationsCostText = BuildWealthCostText(reparationsScore);
                TributeText = new TextObject("{=BC_Parley_TributeDraft}Tribute: {GOLD} denars/day for {DAYS} days")
                    .SetTextVariable("GOLD", TreatyTermCostModel.GetDailyTributeForWarScore(tributeScore))
                    .SetTextVariable("DAYS", BellumCivileConstants.TreatyTributeDurationDays)
                    .ToString();
                TributeCostText = BuildWealthCostText(tributeScore);
            }

            RefreshHostageControls();
            PrisonersTabHint = new HintViewModel(IsDraftEditingDisabled
                ? new TextObject("{=BC_Parley_PrisonersRulerOnly}Only the ruler may alter prisoner terms in the treaty.")
                : TextObject.GetEmpty());
            PoliticsTabHint = new HintViewModel(IsDraftEditingDisabled
                ? new TextObject("{=BC_Parley_PoliticsRulerOnly}Only the ruler may alter political terms in the treaty.")
                : TextObject.GetEmpty());
            DraftTabDisabledHint = new HintViewModel(IsDraftEditingDisabled
                ? new TextObject("{=BC_Parley_RulerOnlyDrafting}Only the ruler may alter the terms of the treaty.")
                : TextObject.GetEmpty());

            RejectTreatyHint = new HintViewModel(IsRejectDisabled
                ? proposal?.IsForced == true
                    ? new TextObject("{=BC_Parley_ForcedTreatyRejectHint}Terminal war score compels both realms to conclude a treaty of peace.")
                    : new TextObject("{=BC_Parley_RejectTreatyDisabledHint}Only your ruler might outright reject a treaty of peace")
                : TextObject.GetEmpty());
            SignTreatyHint = new HintViewModel(isOverBudget
                ? new TextObject("{=BC_Parley_FailureWarScoreExceeded}These demands exceed the leverage won in this war. Reduce the demands or offer concessions before sealing the peace; even capitulation cannot compel these terms.")
                : IsSignDisabled
                ? new TextObject("{=BC_Parley_SignTreatyDisabledHint}You need to select an outcome and how much you want to support that outcome.")
                : new TextObject("{=BC_Parley_SignTreatyHint}Submit the drafted treaty to both councils. Committed influence will be spent even if ratification fails."));
            ResetTreatyHint = new HintViewModel(IsDraftEditingDisabled
                ? new TextObject("{=BC_Parley_RulerOnlyDrafting}Only the ruler may alter the terms of the treaty.")
                : new TextObject("{=BC_Parley_ResetTreatyHint}Restore the treaty to the terms first presented at this parley."));
        }

        public void ExecuteClose()
        {
            _closeAction?.Invoke();
        }

        public void ExecuteVoteYay() => SetPlayerVote(TreatyCouncilVoteStance.Yay, _proposal?.PlayerInfluenceCommitment ?? 0);
        public void ExecuteVoteNay() => SetPlayerVote(TreatyCouncilVoteStance.Nay, _proposal?.PlayerInfluenceCommitment ?? 0);
        public void ExecuteVoteAbstain() => SetPlayerVote(TreatyCouncilVoteStance.Abstain, 0);
        public void ExecuteCommit25() => SetPlayerVote(_proposal?.PlayerVoteStance ?? TreatyCouncilVoteStance.Abstain, 25);
        public void ExecuteCommit75() => SetPlayerVote(_proposal?.PlayerVoteStance ?? TreatyCouncilVoteStance.Abstain, 75);
        public void ExecuteCommit150() => SetPlayerVote(_proposal?.PlayerVoteStance ?? TreatyCouncilVoteStance.Abstain, 150);
        public void ExecuteBeginPredictedOutcomeHint() => _predictedOutcomeTooltip?.ExecuteBeginHint();
        public void ExecuteEndPredictedOutcomeHint() => _predictedOutcomeTooltip?.ExecuteEndHint();
        public void ExecuteSelectFiefsTab() { if (IsDraftEditingDisabled) return; IsFiefsTabSelected = true; IsPrisonersTabSelected = false; IsPoliticsTabSelected = false; IsWealthTabSelected = false; }
        public void ExecuteSelectPrisonersTab() { if (IsDraftEditingDisabled) return; IsFiefsTabSelected = false; IsPrisonersTabSelected = true; IsPoliticsTabSelected = false; IsWealthTabSelected = false; }
        public void ExecuteSelectPoliticsTab() { if (IsDraftEditingDisabled) return; IsFiefsTabSelected = false; IsPrisonersTabSelected = false; IsPoliticsTabSelected = true; IsWealthTabSelected = false; }
        public void ExecuteSelectWealthTab() { if (IsDraftEditingDisabled) return; IsFiefsTabSelected = false; IsPrisonersTabSelected = false; IsPoliticsTabSelected = false; IsWealthTabSelected = true; }
        public void ExecuteSelectDemandsMode()
        {
            if (IsDraftEditingDisabled)
                return;
            IsDemandsMode = true;
            IsOfferingsMode = false;
            Refresh(_proposal);
        }
        public void ExecuteSelectOfferingsMode()
        {
            if (IsDraftEditingDisabled)
                return;
            IsDemandsMode = false;
            IsOfferingsMode = true;
            Refresh(_proposal);
        }
        public void ExecuteDecreaseReparations() => AdjustWealthTerm(TreatyTermType.Reparations, increase: false);
        public void ExecuteIncreaseReparations() => AdjustWealthTerm(TreatyTermType.Reparations, increase: true);
        public void ExecuteDecreaseTribute() => AdjustWealthTerm(TreatyTermType.Tribute, increase: false);
        public void ExecuteIncreaseTribute() => AdjustWealthTerm(TreatyTermType.Tribute, increase: true);

        public void ExecuteRejectTreaty()
        {
            ForeignTreatyBehavior treaties = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>();
            string report = "the treaty could not be rejected";
            if (treaties != null && treaties.TryRejectPlayerParley(_proposal?.ProposalId, out report))
            {
                TreatyOutcomeInquiry outcome = BuildTreatyOutcomeInquiry(explicitRulerRejection: true);
                _closeAction?.Invoke();
                ShowTreatyOutcomeInquiry(outcome);
                return;
            }

            DisplayActionFailure(report);
        }

        public void ExecuteSignTreaty()
        {
            if (_proposal?.IsOverBudget == true)
            {
                Refresh(_proposal);
                return;
            }

            ForeignTreatyBehavior treaties = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>();
            string report = "the treaty could not be signed";
            if (treaties != null && treaties.TrySignPlayerParley(_proposal?.ProposalId, out report))
            {
                TreatyOutcomeInquiry outcome = BuildTreatyOutcomeInquiry(explicitRulerRejection: false);
                _closeAction?.Invoke();
                ShowTreatyOutcomeInquiry(outcome);
                return;
            }

            DisplayActionFailure(report);
        }

        public void ExecuteResetDraft()
        {
            if (IsDraftEditingDisabled || _proposal == null)
                return;

            IsDemandsMode = true;
            IsOfferingsMode = false;
            ReplaceDraft(CloneTerms(_initialDraftTerms));
        }

        private void SetPlayerVote(TreatyCouncilVoteStance stance, int commitment)
        {
            if (!IsPlayerVassal || _proposal == null)
                return;

            ForeignTreatyBehavior treaties = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>();
            string report = "the treaty vote could not be recorded";
            if (treaties != null && treaties.TrySetPlayerVote(_proposal.ProposalId, stance, commitment, out report))
            {
                Refresh(_proposal);
                return;
            }

            DisplayActionFailure(report);
        }

        private void ToggleFiefTerm(string settlementId)
        {
            if (IsDraftEditingDisabled || _proposal == null || string.IsNullOrWhiteSpace(settlementId))
                return;

            List<TreatyTermRecord> terms = GetEditableDemandTerms();
            if (!TryResolveCurrentDirection(out Kingdom fromKingdom, out Kingdom toKingdom))
                return;
            TreatyTermRecord existing = terms.FirstOrDefault(term => term.Type == TreatyTermType.TransferFief
                && term.SettlementId == settlementId
                && term.FromKingdomId == fromKingdom.StringId
                && term.ToKingdomId == toKingdom.StringId);
            if (existing != null)
                terms.Remove(existing);
            else
                terms.Add(new TreatyTermRecord(TreatyTermType.TransferFief, 0, settlementId,
                    fromKingdomId: fromKingdom.StringId,
                    toKingdomId: toKingdom.StringId,
                    wasVoluntaryOffering: IsOfferingsMode));
            ReplaceDraft(terms);
        }

        private void ToggleClientOccupationTerm(string settlementId)
        {
            if (IsDraftEditingDisabled || _proposal == null
                || !TryResolveCurrentDirection(out var from, out var to)) return;
            var terms = GetEditableDemandTerms();
            var existing = terms.FirstOrDefault(t => t.Type == TreatyTermType.RecognizeClientOccupation
                && t.SettlementId == settlementId && t.FromKingdomId == from.StringId && t.ToKingdomId == to.StringId);
            if (existing != null) terms.Remove(existing);
            else
            {
                var war = Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(_proposal.WarKey);
                var candidate = ClientWarTerritory.Candidates(war, from, to).FirstOrDefault(c => c.Fief.StringId == settlementId);
                if (candidate == null) return;
                terms.Add(candidate.Term(from, to, IsOfferingsMode));
            }
            ReplaceDraft(terms);
        }

        private void AdjustWealthTerm(TreatyTermType type, bool increase)
        {
            if (IsDraftEditingDisabled || _proposal == null)
                return;

            if ((type == TreatyTermType.Reparations && BellumCivileOptions.TreatyReparationsGoldPerWarScore <= 0)
                || (type == TreatyTermType.Tribute && BellumCivileOptions.TreatyDailyTributePerWarScore <= 0))
                return;

            List<TreatyTermRecord> terms = GetEditableDemandTerms();
            if (!TryResolveCurrentDirection(out Kingdom fromKingdom, out Kingdom toKingdom))
                return;
            TreatyTermRecord existing = terms.FirstOrDefault(term => term.Type == type
                && term.FromKingdomId == fromKingdom.StringId
                && term.ToKingdomId == toKingdom.StringId);
            int currentCost = existing?.WarScoreCost ?? 0;
            bool shiftDown = IsShiftDown();
            bool maximumAdjustment = IsControlDown();
            int nextCost;
            if (maximumAdjustment)
            {
                nextCost = increase
                    ? Math.Max(currentCost, GetMaximumWealthCost(existing, fromKingdom, toKingdom))
                    : 0;
            }
            else
            {
                int step = shiftDown ? WealthShiftAdjustmentStep : WealthAdjustmentStep;
                nextCost = Math.Max(0, currentCost + (increase ? step : -step));
            }
            if (existing != null)
                terms.Remove(existing);
            if (nextCost > 0)
                terms.Add(new TreatyTermRecord(type, nextCost,
                    fromKingdomId: fromKingdom.StringId,
                    toKingdomId: toKingdom.StringId,
                    wasVoluntaryOffering: IsOfferingsMode));
            ReplaceDraft(terms);
        }

        private int GetMaximumWealthCost(TreatyTermRecord existing, Kingdom fromKingdom, Kingdom toKingdom)
        {
            if (_proposal == null || fromKingdom == null || toKingdom == null)
                return existing?.WarScoreCost ?? 0;

            List<TreatyTermRecord> termsWithoutExisting = GetEditableDemandTerms();
            if (existing != null)
            {
                termsWithoutExisting.RemoveAll(term => term != null
                    && term.Type == existing.Type
                    && term.FromKingdomId == existing.FromKingdomId
                    && term.ToKingdomId == existing.ToKingdomId);
            }
            int usedWithoutThisTerm = TreatyWarScoreAccounting.Calculate(
                termsWithoutExisting,
                _proposal.WinnerKingdomId,
                _proposal.WarScoreBudget).UsedWarScore;
            if (toKingdom.StringId == _proposal.WinnerKingdomId)
                return Math.Max(0, _proposal.WarScoreBudget - usedWithoutThisTerm);

            if (!IsOfferingsMode)
                return existing?.WarScoreCost ?? 0;

            return Math.Max(existing?.WarScoreCost ?? 0, (int)BellumCivileConstants.WarScoreForcePeaceThreshold);
        }

        private static bool IsShiftDown()
        {
            return Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift);
        }

        private static bool IsControlDown()
        {
            return Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
        }

        private void TogglePrisonerTerm(string heroId)
        {
            if (IsDraftEditingDisabled || _proposal == null || string.IsNullOrWhiteSpace(heroId))
                return;

            List<TreatyTermRecord> terms = GetEditableDemandTerms();
            if (!TryResolveCurrentDirection(out Kingdom fromKingdom, out Kingdom toKingdom))
                return;

            WarScoreRecord war = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(_proposal.WarKey);
            Dictionary<string, TreatyFiefTransferCandidate> availableFiefs = TreatyDraftService
                .GetAvailableFiefTransfers(war, fromKingdom, toKingdom)
                .ToDictionary(candidate => candidate.Settlement.StringId);
            TreatyTermRecord bundledFiefTerm = terms.FirstOrDefault(term =>
            {
                if (term.Type != TreatyTermType.TransferFief
                    || term.FromKingdomId != fromKingdom.StringId
                    || term.ToKingdomId != toKingdom.StringId
                    || !availableFiefs.TryGetValue(term.SettlementId ?? string.Empty, out TreatyFiefTransferCandidate fief))
                    return false;

                return fief.IncludedPrisoners.Any(prisoner => prisoner.Hero.StringId == heroId);
            });
            if (bundledFiefTerm != null)
            {
                TreatyFiefTransferCandidate bundledFief = availableFiefs[bundledFiefTerm.SettlementId];
                terms.Remove(bundledFiefTerm);

                // Unchecking one bundled prisoner invalidates the fief package. Keep the
                // other previously selected releases as independent treaty terms.
                foreach (TreatyPrisonerReleaseCandidate prisoner in bundledFief.IncludedPrisoners
                    .Where(candidate => candidate.Hero.StringId != heroId))
                {
                    if (!terms.Any(term => term.Type == TreatyTermType.ReleasePrisoner
                        && term.HeroId == prisoner.Hero.StringId
                        && term.FromKingdomId == fromKingdom.StringId
                        && term.ToKingdomId == toKingdom.StringId))
                    {
                        terms.Add(new TreatyTermRecord(
                            TreatyTermType.ReleasePrisoner,
                            0,
                            fromKingdomId: fromKingdom.StringId,
                            toKingdomId: toKingdom.StringId,
                            heroId: prisoner.Hero.StringId,
                            wasVoluntaryOffering: IsOfferingsMode));
                    }
                }

                ReplaceDraft(terms);
                return;
            }

            TreatyTermRecord existing = terms.FirstOrDefault(term => term.Type == TreatyTermType.ReleasePrisoner
                && term.HeroId == heroId
                && term.FromKingdomId == fromKingdom.StringId
                && term.ToKingdomId == toKingdom.StringId);
            if (existing != null)
                terms.Remove(existing);
            else
                terms.Add(new TreatyTermRecord(TreatyTermType.ReleasePrisoner, 0,
                    fromKingdomId: fromKingdom.StringId,
                    toKingdomId: toKingdom.StringId,
                    heroId: heroId,
                    wasVoluntaryOffering: IsOfferingsMode));
            ReplaceDraft(terms);
        }

        private void ToggleClaimTerm(string key)
        {
            if (IsDraftEditingDisabled || _proposal == null || string.IsNullOrWhiteSpace(key))
                return;
            int separator = key.IndexOf('|');
            if (separator <= 0 || separator >= key.Length - 1)
                return;

            string clanId = key.Substring(0, separator);
            string titleId = key.Substring(separator + 1);
            List<TreatyTermRecord> terms = GetEditableDemandTerms();
            if (!TryResolveCurrentDirection(out Kingdom fromKingdom, out Kingdom toKingdom))
                return;
            TreatyTermRecord existing = terms.FirstOrDefault(term => term.Type == TreatyTermType.RenounceClaim
                && term.ClanId == clanId
                && term.TitleId == titleId
                && term.FromKingdomId == fromKingdom.StringId
                && term.ToKingdomId == toKingdom.StringId);
            if (existing != null)
                terms.Remove(existing);
            else
                terms.Add(new TreatyTermRecord(TreatyTermType.RenounceClaim, 0,
                    fromKingdomId: fromKingdom.StringId,
                    toKingdomId: toKingdom.StringId,
                    clanId: clanId,
                    titleId: titleId,
                    wasVoluntaryOffering: IsOfferingsMode));
            ReplaceDraft(terms);
        }

        private void ToggleRulerRebukeTerm(string key)
        {
            if (IsDraftEditingDisabled || _proposal == null || string.IsNullOrWhiteSpace(key))
                return;
            if (!TryResolveCurrentDirection(out Kingdom fromKingdom, out Kingdom toKingdom))
                return;

            TreatyTermType requestedType = key == "humiliate"
                ? TreatyTermType.HumiliateRuler
                : TreatyTermType.DiscreditRuler;
            List<TreatyTermRecord> terms = GetEditableDemandTerms();
            TreatyTermRecord existing = terms.FirstOrDefault(term =>
                term.Type == TreatyTermType.ConcedeDefeat
                    || term.Type == TreatyTermType.DiscreditRuler
                    || term.Type == TreatyTermType.HumiliateRuler);
            bool deselect = existing?.Type == requestedType
                && existing.FromKingdomId == fromKingdom.StringId
                && existing.ToKingdomId == toKingdom.StringId;
            if (existing != null)
                terms.Remove(existing);
            if (!deselect)
                terms.Add(new TreatyTermRecord(requestedType, 0,
                    fromKingdomId: fromKingdom.StringId,
                    toKingdomId: toKingdom.StringId,
                    wasVoluntaryOffering: IsOfferingsMode));
            ReplaceDraft(terms);
        }

        private void ToggleStructuralPoliticalTerm(string key)
        {
            if (IsDraftEditingDisabled || _proposal == null || string.IsNullOrWhiteSpace(key)
                || !TryResolveCurrentDirection(out Kingdom fromKingdom, out Kingdom toKingdom))
                return;

            List<TreatyTermRecord> terms = GetEditableDemandTerms();
            if (key == "force_vassalization")
            {
                TreatyTermRecord existingForce = terms.FirstOrDefault(term => term.Type == TreatyTermType.ForceVassalization
                    && term.FromKingdomId == fromKingdom.StringId && term.ToKingdomId == toKingdom.StringId);
                if (existingForce != null)
                    terms.Remove(existingForce);
                else
                {
                    terms.RemoveAll(term => term.Type == TreatyTermType.MakeClientKingdom);
                    terms.RemoveAll(term => term.Type == TreatyTermType.EnforceRebelDemands);
                    terms.RemoveAll(term => term.Type == TreatyTermType.ConcedeDefeat);
                    terms.Add(new TreatyTermRecord(TreatyTermType.ForceVassalization, 0,
                        fromKingdomId: fromKingdom.StringId,
                        toKingdomId: toKingdom.StringId,
                        wasVoluntaryOffering: IsOfferingsMode));
                }
                ReplaceDraft(terms);
                return;
            }
            if (key == "make_client")
            {
                TreatyTermRecord existingClient = terms.FirstOrDefault(term => term.Type == TreatyTermType.MakeClientKingdom
                    && term.FromKingdomId == fromKingdom.StringId && term.ToKingdomId == toKingdom.StringId);
                if (existingClient != null)
                    terms.Remove(existingClient);
                else
                {
                    terms.RemoveAll(term => term.Type == TreatyTermType.ForceVassalization);
                    terms.RemoveAll(term => term.Type == TreatyTermType.EnforceRebelDemands);
                    terms.RemoveAll(term => term.Type == TreatyTermType.ConcedeDefeat);
                    terms.Add(new TreatyTermRecord(TreatyTermType.MakeClientKingdom, 0,
                        fromKingdomId: fromKingdom.StringId,
                        toKingdomId: toKingdom.StringId,
                        wasVoluntaryOffering: IsOfferingsMode));
                }
                ReplaceDraft(terms);
                return;
            }

            string[] parts = key.Split('|');
            if (parts.Length != 3 || parts[0] != "release")
                return;
            TreatyTermRecord existing = terms.FirstOrDefault(term => term.Type == TreatyTermType.ReleaseVassal
                && term.ClanId == parts[1] && term.TitleId == parts[2]
                && term.FromKingdomId == fromKingdom.StringId && term.ToKingdomId == toKingdom.StringId);
            if (existing != null)
                terms.Remove(existing);
            else
                terms.Add(new TreatyTermRecord(TreatyTermType.ReleaseVassal, 0,
                    fromKingdomId: fromKingdom.StringId, toKingdomId: toKingdom.StringId,
                    clanId: parts[1],
                    titleId: parts[2],
                    wasVoluntaryOffering: IsOfferingsMode));
            ReplaceDraft(terms);
        }

        private void ToggleDiplomaticSeveranceTerm(string key)
        {
            if (IsDraftEditingDisabled || _proposal == null || string.IsNullOrWhiteSpace(key)
                || !TryResolveCurrentDirection(out Kingdom fromKingdom, out Kingdom toKingdom))
                return;

            string[] parts = key.Split('|');
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[1]))
                return;

            TreatyTermType type = parts[0] == "sever_alliance"
                ? TreatyTermType.EndAlliance
                : parts[0] == "sever_trade"
                    ? TreatyTermType.EndTradeAgreement
                    : (TreatyTermType)(-1);
            if (type != TreatyTermType.EndAlliance && type != TreatyTermType.EndTradeAgreement)
                return;

            List<TreatyTermRecord> terms = GetEditableDemandTerms();
            TreatyTermRecord existing = terms.FirstOrDefault(term => term.Type == type
                && term.FromKingdomId == fromKingdom.StringId
                && term.ToKingdomId == toKingdom.StringId
                && term.ThirdKingdomId == parts[1]);
            if (existing != null)
                terms.Remove(existing);
            else
                terms.Add(new TreatyTermRecord(type, 0,
                    fromKingdomId: fromKingdom.StringId,
                    toKingdomId: toKingdom.StringId,
                    thirdKingdomId: parts[1],
                    wasVoluntaryOffering: IsOfferingsMode));
            ReplaceDraft(terms);
        }

        private void ToggleNewPoliticalTerm(string key)
        {
            if (IsDraftEditingDisabled || _proposal == null || string.IsNullOrWhiteSpace(key)
                || !TryResolveCurrentDirection(out Kingdom fromKingdom, out Kingdom toKingdom))
                return;

            if (key == "concede_defeat")
            {
                List<TreatyTermRecord> concedeTerms = GetEditableDemandTerms();
                TreatyTermRecord concedeExisting = concedeTerms.FirstOrDefault(term => term.Type == TreatyTermType.ConcedeDefeat);
                if (concedeExisting != null)
                    concedeTerms.Remove(concedeExisting);
                else
                {
                    concedeTerms.RemoveAll(term => term.Type == TreatyTermType.DiscreditRuler
                        || term.Type == TreatyTermType.HumiliateRuler
                        || term.Type == TreatyTermType.ForceVassalization
                        || term.Type == TreatyTermType.MakeClientKingdom
                        || term.Type == TreatyTermType.EnforceRebelDemands);
                    concedeTerms.Add(new TreatyTermRecord(TreatyTermType.ConcedeDefeat, 0,
                        fromKingdomId: fromKingdom.StringId,
                        toKingdomId: toKingdom.StringId,
                        wasVoluntaryOffering: IsOfferingsMode));
                }
                ReplaceDraft(concedeTerms);
                return;
            }

            string[] parts = key.Split('|');
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[1]))
                return;
            TreatyTermType type = parts[0] == "release_client"
                ? TreatyTermType.ReleaseClientState
                : parts[0] == "enforce_rebels"
                    ? TreatyTermType.EnforceRebelDemands
                    : (TreatyTermType)(-1);
            if (type != TreatyTermType.ReleaseClientState && type != TreatyTermType.EnforceRebelDemands)
                return;

            List<TreatyTermRecord> terms = GetEditableDemandTerms();
            TreatyTermRecord existing = terms.FirstOrDefault(term => term.Type == type
                && term.FromKingdomId == fromKingdom.StringId
                && term.ToKingdomId == toKingdom.StringId
                && term.ThirdKingdomId == parts[1]);
            if (existing != null)
                terms.Remove(existing);
            else
            {
                if (type == TreatyTermType.EnforceRebelDemands)
                    terms.RemoveAll(term => term.Type == TreatyTermType.EnforceRebelDemands
                        || term.Type == TreatyTermType.ForceVassalization
                        || term.Type == TreatyTermType.MakeClientKingdom
                        || term.Type == TreatyTermType.ConcedeDefeat);
                terms.Add(new TreatyTermRecord(type, 0,
                    fromKingdomId: fromKingdom.StringId,
                    toKingdomId: toKingdom.StringId,
                    thirdKingdomId: parts[1],
                    wasVoluntaryOffering: IsOfferingsMode));
            }
            ReplaceDraft(terms);
        }

        private void ToggleRoyalMarriageTerm(string key)
        {
            if (IsDraftEditingDisabled || _proposal == null || key != "royal_marriage"
                || !TryResolveCurrentDirection(out Kingdom fromKingdom, out Kingdom toKingdom))
                return;

            List<TreatyTermRecord> terms = GetEditableDemandTerms();
            TreatyTermRecord existing = terms.FirstOrDefault(term => term.Type == TreatyTermType.ArrangeRoyalMarriage);
            if (existing != null
                && existing.FromKingdomId == fromKingdom.StringId
                && existing.ToKingdomId == toKingdom.StringId)
            {
                terms.Remove(existing);
                ReplaceDraft(terms);
                return;
            }
            if (existing != null)
                terms.Remove(existing);

            IReadOnlyList<TreatyRoyalMarriageCandidate> candidates = TreatyRoyalMarriageService.GetCandidates(fromKingdom, toKingdom);
            List<Hero> concedingRelatives = candidates.Select(candidate => candidate.ConcedingSpouse).Distinct().ToList();
            if (concedingRelatives.Count == 0)
                return;

            List<InquiryElement> choices = concedingRelatives.Select(hero => new InquiryElement(
                hero,
                hero.Name.ToString(),
                null,
                true,
                new TextObject("{=BC_Parley_RoyalMarriage_ConcedingHint}{HERO_NAME} will leave the {SOURCE_CLAN} and marry into the {RECEIVING_CLAN} if the treaty is ratified.")
                    .SetTextVariable("HERO_NAME", hero.Name)
                    .SetTextVariable("SOURCE_CLAN", fromKingdom.RulingClan.Name)
                    .SetTextVariable("RECEIVING_CLAN", toKingdom.RulingClan.Name).ToString())).ToList();
            MultiSelectionInquiryData inquiry = new MultiSelectionInquiryData(
                new TextObject("{=BC_Parley_RoyalMarriage_SelectConceding}Select the Conceding Royal Relative").ToString(),
                new TextObject("{=BC_Parley_RoyalMarriage_SelectConcedingDesc}Choose which member of the conceding royal house will marry into the demanding ruler's clan.").ToString(),
                choices,
                true,
                1,
                1,
                new TextObject("{=BC_UI_Continue}Continue").ToString(),
                new TextObject("{=BC_UI_Cancel}Cancel").ToString(),
                selected =>
                {
                    Hero sourceSpouse = selected?.FirstOrDefault()?.Identifier as Hero;
                    if (sourceSpouse != null)
                        ShowReceivingRoyalMarriageSelection(fromKingdom, toKingdom, sourceSpouse, candidates);
                },
                null);
            MBInformationManager.ShowMultiSelectionInquiry(inquiry, true);
        }

        private void ShowReceivingRoyalMarriageSelection(Kingdom fromKingdom, Kingdom toKingdom, Hero sourceSpouse,
            IReadOnlyList<TreatyRoyalMarriageCandidate> candidates)
        {
            List<TreatyRoyalMarriageCandidate> matches = candidates.Where(candidate => candidate.ConcedingSpouse == sourceSpouse).ToList();
            List<InquiryElement> choices = matches.Select(candidate => new InquiryElement(
                candidate,
                candidate.ReceivingSpouse.Name.ToString(),
                null,
                true,
                new TextObject("{=BC_Parley_RoyalMarriage_ReceivingHint}{HERO_NAME} will remain with the {RECEIVING_CLAN}, and {SOURCE_NAME} will join that clan after marriage.")
                    .SetTextVariable("HERO_NAME", candidate.ReceivingSpouse.Name)
                    .SetTextVariable("RECEIVING_CLAN", toKingdom.RulingClan.Name)
                    .SetTextVariable("SOURCE_NAME", sourceSpouse.Name).ToString())).ToList();
            MultiSelectionInquiryData inquiry = new MultiSelectionInquiryData(
                new TextObject("{=BC_Parley_RoyalMarriage_SelectReceiving}Select the Receiving Royal Relative").ToString(),
                new TextObject("{=BC_Parley_RoyalMarriage_SelectReceivingDesc}Choose the member of the demanding royal house who will receive the foreign spouse into their clan.").ToString(),
                choices,
                true,
                1,
                1,
                new TextObject("{=BC_UI_Confirm}Confirm").ToString(),
                new TextObject("{=BC_UI_Cancel}Cancel").ToString(),
                selected =>
                {
                    TreatyRoyalMarriageCandidate candidate = selected?.FirstOrDefault()?.Identifier as TreatyRoyalMarriageCandidate;
                    if (candidate == null)
                        return;
                    List<TreatyTermRecord> terms = GetEditableDemandTerms();
                    terms.RemoveAll(term => term.Type == TreatyTermType.ArrangeRoyalMarriage);
                    terms.Add(new TreatyTermRecord(TreatyTermType.ArrangeRoyalMarriage, candidate.WarScoreCost,
                        fromKingdomId: fromKingdom.StringId,
                        toKingdomId: toKingdom.StringId,
                        heroId: candidate.ConcedingSpouse.StringId,
                        secondaryHeroId: candidate.ReceivingSpouse.StringId,
                        wasVoluntaryOffering: IsOfferingsMode));
                    ReplaceDraft(terms);
                },
                null);
            MBInformationManager.ShowMultiSelectionInquiry(inquiry, true);
        }

        private bool TryResolveCurrentDirection(out Kingdom fromKingdom, out Kingdom toKingdom)
        {
            Kingdom winner = Kingdom.All.FirstOrDefault(kingdom => kingdom?.StringId == _proposal?.WinnerKingdomId);
            Kingdom loser = Kingdom.All.FirstOrDefault(kingdom => kingdom?.StringId == _proposal?.LoserKingdomId);
            Kingdom playerKingdom = WarPeaceRevampBehavior.GetPlayerPoliticalKingdom();
            Kingdom opponent = playerKingdom == winner ? loser : winner;
            fromKingdom = IsDemandsMode ? opponent : playerKingdom;
            toKingdom = IsDemandsMode ? playerKingdom : opponent;
            return fromKingdom != null && toKingdom != null;
        }

        private List<TreatyTermRecord> GetEditableDemandTerms()
        {
            return _proposal.Terms
                .Where(term => term != null
                    && term.Type != TreatyTermType.WhitePeace)
                .ToList();
        }

        private static List<TreatyTermRecord> CloneTerms(IEnumerable<TreatyTermRecord> terms)
        {
            return (terms ?? Enumerable.Empty<TreatyTermRecord>())
                .Where(term => term != null)
                .Select(term => new TreatyTermRecord(
                    term.Type,
                    term.WarScoreCost,
                    term.SettlementId,
                    term.GoldAmount,
                    term.DailyGold,
                    term.DurationDays,
                    term.FromKingdomId,
                    term.ToKingdomId,
                    term.WasOccupiedAtDrafting,
                    term.HeroId,
                    term.ClanId,
                    term.TitleId,
                    term.SecondaryHeroId,
                    term.ThirdKingdomId,
                    term.WasVoluntaryOffering,
                    term.HostageTier,
                    term.HostageReceivingClanId,
                    term.HostageDurationPriced))
                .ToList();
        }

        private void ReplaceDraft(List<TreatyTermRecord> terms)
        {
            ForeignTreatyBehavior treaties = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>();
            string report = "the treaty draft could not be changed";
            if (treaties != null && treaties.TryReplacePlayerDraft(_proposal.ProposalId, terms, out report))
            {
                Refresh(_proposal);
                return;
            }

            DisplayActionFailure(report);
        }

        private static void DisplayActionFailure(string report)
        {
            if (!string.IsNullOrWhiteSpace(report))
            {
                BellumCivileLogger.Log("Player treaty action rejected; reason=" + report + ".");
                InformationManager.DisplayMessage(new InformationMessage(LocalizeActionReport(report)));
            }
        }

        private static string LocalizeActionReport(string report)
        {
            if (string.IsNullOrWhiteSpace(report))
                return new TextObject("{=BC_Parley_ActionFailed}The treaty action could not be completed.").ToString();

            const string overridePrefix = "rejecting the council's ratified terms requires ";
            if (report.StartsWith(overridePrefix, StringComparison.Ordinal)
                && report.EndsWith(" influence", StringComparison.Ordinal))
            {
                string amount = report.Substring(overridePrefix.Length, report.Length - overridePrefix.Length - " influence".Length);
                return new TextObject("{=BC_Parley_RejectOverrideCost}Rejecting the council's ratified terms requires {COST} influence.")
                    .SetTextVariable("COST", amount)
                    .ToString();
            }

            string template;
            switch (report)
            {
                case "player treaty vote could not be recorded":
                case "the treaty vote could not be recorded":
                    template = "{=BC_Parley_FailureVoteRecord}The treaty vote could not be recorded.";
                    break;
                case "the ruling clan drafts the treaty rather than casting a vassal vote":
                    template = "{=BC_Parley_FailureRulerDoesNotVote}The ruling clan drafts the treaty rather than casting a vassal vote.";
                    break;
                case "the player is not a party to this parley":
                    template = "{=BC_Parley_FailureNotParty}You are not a party to this parley.";
                    break;
                case "invalid influence commitment":
                    template = "{=BC_Parley_FailureInvalidCommitment}That influence commitment is invalid.";
                    break;
                case "not enough influence for that commitment":
                    template = "{=BC_Parley_FailureInsufficientInfluence}You do not have enough influence for that commitment.";
                    break;
                case "the treaty could not be rejected":
                    template = "{=BC_Parley_FailureReject}The treaty could not be rejected.";
                    break;
                case "only the ruler may reject the treaty":
                    template = "{=BC_Parley_FailureRulerRejectOnly}Only the ruler may reject the treaty outright.";
                    break;
                case "terminal war score compels both realms to conclude the treaty":
                    template = "{=BC_Parley_FailureForcedTreaty}Terminal war score compels both realms to conclude the treaty.";
                    break;
                case "the treaty could not be signed":
                    template = "{=BC_Parley_FailureSign}The treaty could not be signed.";
                    break;
                case "choose a position before signing the treaty":
                    template = "{=BC_Parley_FailureChoosePosition}Choose a position before signing the treaty.";
                    break;
                case "a YAY or NAY vote requires an influence commitment":
                    template = "{=BC_Parley_FailureVoteNeedsInfluence}A YAY or NAY vote requires an influence commitment.";
                    break;
                case "treaty draft could not be changed":
                case "the treaty draft could not be changed":
                    template = "{=BC_Parley_FailureDraftChange}The treaty draft could not be changed.";
                    break;
                case "no pending treaty proposal":
                    template = "{=BC_Parley_FailureNoPendingProposal}There is no pending treaty proposal.";
                    break;
                case "only a ruler participating in the parley may draft these terms":
                    template = "{=BC_Parley_FailureRulerDraftOnly}Only a ruler participating in the parley may draft these terms.";
                    break;
                case "invalid treaty draft":
                    template = "{=BC_Parley_FailureInvalidDraft}The treaty draft is invalid.";
                    break;
                case "white peace cannot be combined with other demands":
                    template = "{=BC_Parley_FailureWhitePeaceCombined}White peace cannot be combined with other demands.";
                    break;
                case "the losing realm cannot impose demands upon the winning realm":
                    template = "{=BC_Parley_FailureReverseDemand}The losing realm cannot impose demands upon the winning realm.";
                    break;
                case "the losing realm may request only reciprocal prisoner or wealth exchanges":
                    template = "{=BC_Parley_FailureReverseStrategicDemand}The losing realm may request only reciprocal prisoner or wealth exchanges.";
                    break;
                case "reciprocal prisoner and wealth requests must be matched by equivalent exchanges":
                    template = "{=BC_Parley_FailureUnmatchedExchange}Prisoner and wealth requests from the losing realm must be matched by equivalent exchanges.";
                    break;
                case "a treaty may contain only one prestige concession or ruler rebuke":
                    template = "{=BC_Parley_FailureMultiplePrestigeTerms}A treaty may contain only one prestige concession or ruler rebuke.";
                    break;
                case "force vassalization cannot be combined with territorial transfers or released vassals":
                    template = "{=BC_Parley_FailureVassalizationCombined}Force vassalization cannot be combined with territorial transfers or released vassals.";
                    break;
                case "a treaty may establish only one client kingdom and cannot combine clientage with annexation":
                    template = "{=BC_Parley_FailureClientageCombined}A treaty may establish only one client kingdom and cannot combine clientage with annexation.";
                    break;
                case "enforced rebel demands cannot be combined with annexation or clientage of the parent realm":
                    template = "{=BC_Parley_FailureEnforcedRebellionCombined}Enforced rebel demands cannot be combined with annexation or clientage of the parent realm.";
                    break;
                case "conceding defeat cannot be combined with annexation, clientage, or enforced rebel capitulation":
                    template = "{=BC_Parley_FailureConcedeDefeatCombined}Conceding defeat cannot be combined with annexation, clientage, or enforced rebel capitulation.";
                    break;
                case "a treaty may contain only one arranged royal marriage":
                    template = "{=BC_Parley_FailureMultipleRoyalMarriages}A treaty may contain only one arranged royal marriage.";
                    break;
                case "the draft contains an invalid or duplicate territorial transfer":
                    template = "{=BC_Parley_FailureInvalidFiefTransfer}The draft contains an invalid or duplicate territorial transfer.";
                    break;
                case "the draft contains invalid or duplicate reparations":
                    template = "{=BC_Parley_FailureInvalidReparations}The draft contains invalid or duplicate reparations.";
                    break;
                case "the losing realm cannot finance those reparations":
                    template = "{=BC_Parley_FailureCannotFinanceReparations}The losing realm cannot finance those reparations.";
                    break;
                case "the draft contains invalid or duplicate tribute":
                    template = "{=BC_Parley_FailureInvalidTribute}The draft contains invalid or duplicate tribute.";
                    break;
                case "the losing realm cannot finance that tribute":
                    template = "{=BC_Parley_FailureCannotFinanceTribute}The losing realm cannot finance that tribute.";
                    break;
                case "the draft contains an invalid or duplicate prisoner release":
                    template = "{=BC_Parley_FailureInvalidPrisonerRelease}The draft contains an invalid or duplicate prisoner release.";
                    break;
                case "a prisoner named in the draft is no longer held by the releasing realm":
                    template = "{=BC_Parley_FailurePrisonerNoLongerHeld}A prisoner named in the draft is no longer held by the releasing realm.";
                    break;
                case "the draft contains an invalid claim renunciation":
                case "the draft contains a duplicate claim renunciation":
                case "a claim named in the draft is no longer active or applicable":
                    template = "{=BC_Parley_FailureInvalidClaimRenunciation}A claim renunciation in the draft is no longer valid or is duplicated.";
                    break;
                case "the draft contains an invalid or duplicate ruler rebuke":
                case "a ruler named in the political demand is no longer valid":
                    template = "{=BC_Parley_FailureInvalidRulerRebuke}A ruler rebuke in the draft is no longer valid or is duplicated.";
                    break;
                case "only the realm currently losing the war may concede defeat":
                    template = "{=BC_Parley_FailureInvalidConcedeDefeat}Only the realm currently losing the war may concede defeat.";
                    break;
                case "the draft contains an invalid or duplicate vassal release":
                case "the named vassal no longer forms a releasable non-de-jure title cluster":
                    template = "{=BC_Parley_FailureInvalidVassalRelease}A vassal release in the draft is no longer valid or is duplicated.";
                    break;
                case "a released vassal cluster cannot also contain a transferred fief":
                    template = "{=BC_Parley_FailureReleasedClusterTransfer}A released vassal cluster cannot also contain a transferred fief.";
                    break;
                case "the draft contains an invalid force-vassalization direction":
                    template = "{=BC_Parley_FailureInvalidVassalization}The force-vassalization direction is invalid.";
                    break;
                case "the draft contains an invalid client-kingdom direction":
                    template = "{=BC_Parley_FailureInvalidClientage}The client-kingdom direction is invalid.";
                    break;
                case "the draft contains an invalid or duplicate client release":
                case "the named realm is no longer a client of the conceding realm":
                    template = "{=BC_Parley_FailureInvalidClientRelease}A client-state release in the draft is no longer valid or is duplicated.";
                    break;
                case "the draft contains an invalid enforced-rebellion direction":
                case "the rebellion named in the treaty is no longer active":
                    template = "{=BC_Parley_FailureInvalidEnforcedRebellion}The enforced rebel demands in the draft are no longer valid.";
                    break;
                case "a peace pact permits only one hostage from each realm":
                    template = "{=BC_Parley_HostageDuplicate}Each realm may pledge only one hostage, and the same person cannot secure both sides.";
                    break;
                case "the draft contains an invalid hostage pledge":
                    template = "{=BC_Parley_HostageInvalid}The hostage pledge is no longer valid. Remove it and select a relative again.";
                    break;
                case "the same relative cannot be promised in marriage and as a hostage":
                    template = "{=BC_Parley_HostageMarriageConflict}The same relative cannot be promised both in marriage and as a hostage.";
                    break;
                case "hostage peace cannot accompany white peace, annexation, or enforced rebel demands":
                    template = "{=BC_Parley_HostageIncompatible}A hostage pledge cannot accompany white peace, forced clientage, annexation, or enforced rebel demands. Remove the conflicting clause first.";
                    break;
                case "hostage peace requires two established sovereign realms":
                    template = "{=BC_Parley_HostageRealms}A hostage pledge requires the war and peace revamp and two eligible realms, neither undergoing a transfer of sovereignty.";
                    break;
                case "hostage peace is only available in foreign wars":
                    template = "{=BC_Parley_HostageForeignOnly}Hostage peace is only available in foreign wars, not civil wars or private feuds.";
                    break;
                case "a ruling house has changed since the hostage was selected":
                    template = "{=BC_Parley_HostageHouseChanged}One of the ruling houses has changed. Remove the old pledge and select a hostage from the current ruling house.";
                    break;
                case "the selected relative is no longer available as a hostage":
                    template = "{=BC_Parley_HostageUnavailable}The selected relative is no longer eligible or available for delivery as a hostage. Select another relative.";
                    break;
                case "the hostage succession rank has changed; select the relative again":
                    template = "{=BC_Parley_HostageRankChanged}This relative's place in succession has changed. Select them again to update the pledge's war-score cost.";
                    break;
                case "the receiving ruling house has no town or castle free of siege for the hostage":
                    template = "{=BC_Parley_HostageNoHolding}The receiving ruling house must own a town or castle that is not under siege to hold the hostage.";
                    break;
                case "the treaty leaves no suitable holding in the receiving ruling house's possession":
                    template = "{=BC_Parley_HostageHoldingLost}No suitable town or castle will remain with the receiving ruling house after these terms are applied. Its available holdings would be returned or ceded under this peace.";
                    break;
                case "the draft contains an invalid royal marriage":
                case "one of the selected royal spouses is no longer eligible":
                    template = "{=BC_Parley_FailureInvalidRoyalMarriage}The arranged royal marriage is no longer valid.";
                    break;
                case "the draft contains an invalid diplomatic severance":
                case "the draft contains a duplicate diplomatic severance":
                    template = "{=BC_Parley_FailureInvalidSeverance}A diplomatic severance in the draft is invalid or duplicated.";
                    break;
                case "the draft contains a treaty term that is not implemented":
                    template = "{=BC_Parley_FailureUnimplementedTerm}The draft contains a treaty term that is not implemented.";
                    break;
                case "the draft exceeds the available war score":
                    template = "{=BC_Parley_FailureWarScoreExceeded}These demands exceed the leverage won in this war. Reduce the demands or offer concessions before sealing the peace; even capitulation cannot compel these terms.";
                    break;
                default:
                    template = "{=BC_Parley_ActionFailed}The treaty action could not be completed.";
                    break;
            }

            return new TextObject(template).ToString();
        }

        private TreatyOutcomeInquiry BuildTreatyOutcomeInquiry(bool explicitRulerRejection)
        {
            Kingdom winner = Kingdom.All.FirstOrDefault(kingdom => kingdom?.StringId == _proposal?.WinnerKingdomId);
            Kingdom loser = Kingdom.All.FirstOrDefault(kingdom => kingdom?.StringId == _proposal?.LoserKingdomId);
            TextObject firstRealm = winner?.Name ?? new TextObject(_proposal?.WinnerKingdomId ?? string.Empty);
            TextObject secondRealm = loser?.Name ?? new TextObject(_proposal?.LoserKingdomId ?? string.Empty);
            bool ratified = _proposal?.State == TreatyProposalState.Accepted
                || _proposal?.State == TreatyProposalState.Applied;

            TextObject title;
            TextObject description;
            if (ratified)
            {
                title = new TextObject("{=BC_Parley_OutcomeRatifiedTitle}Peace Treaty Ratified");
                description = new TextObject("{=BC_Parley_OutcomeRatifiedBody}The treaty between {FIRST_REALM} and {SECOND_REALM} has been ratified by both realms. Hostilities have ended and the agreed terms are now in force.");
            }
            else if (_proposal?.State == TreatyProposalState.Cancelled)
            {
                title = new TextObject("{=BC_Parley_OutcomeCancelledTitle}Peace Parley Closed");
                description = new TextObject("{=BC_Parley_OutcomeCancelledBody}The proposed treaty between {FIRST_REALM} and {SECOND_REALM} is no longer valid because the war or one of its parties has ceased to exist. The obsolete parley has been dismissed.");
            }
            else if (explicitRulerRejection)
            {
                title = new TextObject("{=BC_Parley_OutcomeRejectedTitle}Peace Treaty Rejected");
                description = new TextObject("{=BC_Parley_OutcomeRulerRejectedBody}You have rejected the proposed treaty between {FIRST_REALM} and {SECOND_REALM}. The parley has ended without agreement, and the war continues.");
            }
            else
            {
                title = new TextObject("{=BC_Parley_OutcomeRejectedTitle}Peace Treaty Rejected");
                description = new TextObject("{=BC_Parley_OutcomeCouncilRejectedBody}The proposed treaty between {FIRST_REALM} and {SECOND_REALM} failed to secure the assent of both realms. The parley has ended without agreement, and the war continues.");
            }

            description.SetTextVariable("FIRST_REALM", firstRealm);
            description.SetTextVariable("SECOND_REALM", secondRealm);
            return new TreatyOutcomeInquiry(title.ToString(), description.ToString());
        }

        private static void ShowTreatyOutcomeInquiry(TreatyOutcomeInquiry outcome)
        {
            if (outcome == null)
                return;

            InformationManager.ShowInquiry(new InquiryData(
                outcome.Title,
                outcome.Description,
                true,
                false,
                GameTexts.FindText("str_done").ToString(),
                string.Empty,
                null,
                null), true);
        }

        private sealed class TreatyOutcomeInquiry
        {
            public string Title { get; }
            public string Description { get; }

            public TreatyOutcomeInquiry(string title, string description)
            {
                Title = title ?? string.Empty;
                Description = description ?? string.Empty;
            }
        }

        private static string BuildPlayerVoteText(TreatyCouncilVoteStance stance)
        {
            switch (stance)
            {
                case TreatyCouncilVoteStance.Yay: return new TaleWorlds.Localization.TextObject("{=BC_Parley_VoteYay}YAY").ToString();
                case TreatyCouncilVoteStance.Nay: return new TaleWorlds.Localization.TextObject("{=BC_Parley_VoteNay}NAY").ToString();
                default: return new TaleWorlds.Localization.TextObject("{=BC_Parley_VoteAbstain}ABSTAIN").ToString();
            }
        }

        private static string ResolveRealmTitle(Kingdom kingdom)
        {
            if (kingdom == null)
                return string.Empty;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalTitleRecord sovereignTitle = titleBehavior?.GetRealmSovereignTitle(kingdom, FeudalHierarchyMode.DeFacto);
            if (sovereignTitle != null)
            {
                string titleName = FeudalTitleDisplayHelper.FormatTitleName(sovereignTitle, kingdom.RulingClan);
                if (!string.IsNullOrWhiteSpace(titleName))
                    return titleName;
            }

            string encyclopediaTitle = kingdom.EncyclopediaTitle?.ToString();
            return !string.IsNullOrWhiteSpace(encyclopediaTitle)
                ? encyclopediaTitle
                : kingdom.Name?.ToString() ?? string.Empty;
        }

        private static IEnumerable<TreatyCouncilMemberEvaluation> OrderCouncilMembers(
            IEnumerable<TreatyCouncilMemberEvaluation> members,
            Clan rulingClan)
        {
            return (members ?? Enumerable.Empty<TreatyCouncilMemberEvaluation>())
                .OrderByDescending(member => member?.Clan == rulingClan)
                .ThenByDescending(member => member?.Clan?.CurrentTotalStrength ?? 0f)
                .ThenBy(member => member?.Clan?.Name?.ToString() ?? string.Empty);
        }

        private static string BuildCouncilStatusText(
            Kingdom kingdom,
            TreatyCouncilEvaluation council,
            bool forced,
            bool accepts,
            bool usesOverride)
        {
            if (council == null)
                return string.Empty;

            string realmName = kingdom?.Name?.ToString() ?? string.Empty;
            if (council.IsBudgetBlocked)
            {
                return new TextObject("{=BC_Parley_CouncilBudgetBlockedMood}The terms exceed the leverage won in this war and cannot be sealed by {REALM}.")
                    .SetTextVariable("REALM", realmName)
                    .ToString();
            }

            if (forced)
            {
                return new TextObject("{=BC_Parley_CouncilForcedMood}The lords of {REALM} have no choice but to accept these terms.")
                    .SetTextVariable("REALM", realmName)
                    .ToString();
            }

            TreatyCouncilMemberEvaluation rulerPosition = council.Members
                .FirstOrDefault(member => member?.Clan == kingdom?.RulingClan);
            if (council.IsRatified
                && accepts
                && rulerPosition?.Stance == TreatyCouncilVoteStance.Nay)
            {
                return new TextObject("{=BC_Parley_CouncilCompelsRulerMood}The lords of {REALM} have carried the treaty despite their ruler's opposition.")
                    .SetTextVariable("REALM", realmName)
                    .ToString();
            }

            bool rulerOverruled = council.IsRatified != accepts;
            if (rulerOverruled || usesOverride)
            {
                string rulerTitle = ResolveRulerDisplayTitle(kingdom);
                TextObject text = accepts
                    ? new TextObject("{=BC_Parley_RulerOverrulesAcceptMood}The {RULER_TITLE} of {REALM} has overruled the realm's lords and accepted these terms.")
                    : new TextObject("{=BC_Parley_RulerOverrulesRejectMood}The {RULER_TITLE} of {REALM} has overruled the realm's lords and rejected these terms.");
                return text
                    .SetTextVariable("RULER_TITLE", rulerTitle)
                    .SetTextVariable("REALM", realmName)
                    .ToString();
            }

            if (council.IsRatified)
            {
                return new TextObject("{=BC_Parley_CouncilAcceptMood}The lords of {REALM} seem inclined to agree with these terms.")
                    .SetTextVariable("REALM", realmName)
                    .ToString();
            }

            if (council.NayInfluence > council.YayInfluence)
            {
                return new TextObject("{=BC_Parley_CouncilRejectMood}The lords of {REALM} deem these terms unacceptable.")
                    .SetTextVariable("REALM", realmName)
                    .ToString();
            }

            return new TextObject("{=BC_Parley_CouncilUncommittedMood}The lords of {REALM} remain uncommitted to these terms.")
                .SetTextVariable("REALM", realmName)
                .ToString();
        }

        private static string BuildRulerCouncilReason(
            Kingdom kingdom,
            TreatyCouncilEvaluation council,
            bool accepts,
            bool usesOverride)
        {
            Clan ruler = kingdom?.RulingClan;
            TreatyCouncilMemberEvaluation rulerPosition = council?.Members
                .FirstOrDefault(member => member?.Clan == ruler);
            if (ruler == null || rulerPosition == null || council.IsBudgetBlocked || council.IsSoleRulerDecision)
                return string.Empty;

            if (council.IsRatified && accepts && rulerPosition.Stance == TreatyCouncilVoteStance.Nay)
            {
                int requiredInfluence = Math.Max(0, council.RejectionOverrideCost)
                    + Math.Max(0, rulerPosition.InfluenceCommitment);
                if (ruler != Clan.PlayerClan && ruler.Influence < requiredInfluence)
                {
                    return new TextObject("{=BC_Parley_RulerLacksInfluenceToOppose}I lack the influence to oppose my vassals.")
                        .ToString();
                }

                return new TextObject("{=BC_Parley_RulerYieldsToCouncil}I will not defy my vassals over these terms.")
                    .ToString();
            }

            if (!council.IsRatified && accepts && usesOverride)
            {
                return new TextObject("{=BC_Parley_RulerOverridesForPeace}I will spend my influence to secure this peace despite my vassals.")
                    .ToString();
            }

            if (council.IsRatified && !accepts && usesOverride)
            {
                return new TextObject("{=BC_Parley_RulerOverridesAgainstPeace}I will spend my influence to reject these terms despite my vassals.")
                    .ToString();
            }

            return string.Empty;
        }

        private static string ResolveRulerDisplayTitle(Kingdom kingdom)
        {
            Hero ruler = kingdom?.RulingClan?.Leader;
            if (ruler != null && FeudalTitleDisplayHelper.TryGetHighestDisplayTitle(ruler, out string displayTitle)
                && !string.IsNullOrWhiteSpace(displayTitle))
            {
                return displayTitle;
            }

            return ruler?.IsFemale == true
                ? new TextObject("{=BC_TitleDisplay_Queen}Queen").ToString()
                : new TextObject("{=BC_TitleDisplay_King}King").ToString();
        }

        private static int GetRulerOverrideCommitment(
            TreatyCouncilEvaluation council,
            bool rulerAccepts,
            bool usesAcceptanceOverride)
        {
            if (council == null || council.IsBudgetBlocked)
                return 0;
            if (rulerAccepts)
                return usesAcceptanceOverride ? council.OverrideCost : 0;
            return council.IsRatified ? council.RejectionOverrideCost : 0;
        }

        private static Color BuildOutcomeColor(bool positive)
        {
            return positive
                ? Color.ConvertStringToColor("#82E06AFF")
                : Color.ConvertStringToColor("#E05252FF");
        }

        private static List<TooltipProperty> BuildPredictedOutcomeTooltip(
            string firstRealm,
            string secondRealm)
        {
            return new List<TooltipProperty>
            {
                new TooltipProperty(firstRealm ?? string.Empty, string.Empty, 0),
                new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator),
                new TooltipProperty(secondRealm ?? string.Empty, string.Empty, 0)
            };
        }

        private static string BuildWealthCostText(int cost)
        {
            return new TextObject("{=BC_Parley_WealthCost}{COST} WS")
                .SetTextVariable("COST", Math.Max(0, cost))
                .ToString();
        }

        [DataSourceProperty] public string ParleyTitleText => new TextObject("{=BC_Parley_Title}Peace Parley").ToString();
        [DataSourceProperty] public string VoteNayText => new TextObject("{=BC_Parley_VoteNay}NAY").ToString();
        [DataSourceProperty] public string VoteAbstainText => new TextObject("{=BC_Parley_VoteAbstain}ABSTAIN").ToString();
        [DataSourceProperty] public string VoteYayText => new TextObject("{=BC_Parley_VoteYay}YAY").ToString();
        [DataSourceProperty] public string RejectTreatyText => new TextObject("{=BC_Parley_RejectTreaty}Reject Treaty").ToString();
        [DataSourceProperty] public string SignTreatyText => new TextObject("{=BC_Parley_SignTreaty}Sign Treaty").ToString();
        [DataSourceProperty] public string FiefsTabText => new TextObject("{=BC_Parley_Tab_Fiefs}Fiefs").ToString();
        [DataSourceProperty] public string PrisonersTabText => new TextObject("{=BC_Parley_Tab_Prisoners}Prisoners").ToString();
        [DataSourceProperty] public string PoliticsTabText => new TextObject("{=BC_Parley_Tab_Politics}Politics").ToString();
        [DataSourceProperty] public string WealthTabText => new TextObject("{=BC_Parley_Tab_Wealth}Wealth").ToString();
        [DataSourceProperty] public string DemandsModeText => new TextObject("{=BC_Parley_Mode_Demands}Demands").ToString();
        [DataSourceProperty] public string OfferingsModeText => new TextObject("{=BC_Parley_Mode_Offerings}Offerings").ToString();

        [DataSourceProperty]
        public string PlayerRealmName { get => _playerRealmName; set { if (value != _playerRealmName) { _playerRealmName = value; OnPropertyChangedWithValue(value, "PlayerRealmName"); } } }
        [DataSourceProperty]
        public string OpponentRealmName { get => _opponentRealmName; set { if (value != _opponentRealmName) { _opponentRealmName = value; OnPropertyChangedWithValue(value, "OpponentRealmName"); } } }
        [DataSourceProperty]
        public string WarScoreText { get => _warScoreText; set { if (value != _warScoreText) { _warScoreText = value; OnPropertyChangedWithValue(value, "WarScoreText"); } } }
        [DataSourceProperty]
        public string BudgetText { get => _budgetText; set { if (value != _budgetText) { _budgetText = value; OnPropertyChangedWithValue(value, "BudgetText"); } } }
        [DataSourceProperty]
        public Color BudgetColor { get => _budgetColor; set { if (value != _budgetColor) { _budgetColor = value; OnPropertyChangedWithValue(value, "BudgetColor"); } } }
        [DataSourceProperty]
        public bool IsPlayerRuler { get => _isPlayerRuler; set { if (value != _isPlayerRuler) { _isPlayerRuler = value; OnPropertyChangedWithValue(value, "IsPlayerRuler"); } } }
        [DataSourceProperty]
        public bool IsPlayerVassal { get => _isPlayerVassal; set { if (value != _isPlayerVassal) { _isPlayerVassal = value; OnPropertyChangedWithValue(value, "IsPlayerVassal"); } } }
        [DataSourceProperty]
        public bool IsInfluenceSelectionDisabled { get => _isInfluenceSelectionDisabled; set { if (value != _isInfluenceSelectionDisabled) { _isInfluenceSelectionDisabled = value; OnPropertyChangedWithValue(value, "IsInfluenceSelectionDisabled"); } } }
        [DataSourceProperty]
        public bool IsVoteNaySelected { get => _isVoteNaySelected; set { if (value != _isVoteNaySelected) { _isVoteNaySelected = value; OnPropertyChangedWithValue(value, nameof(IsVoteNaySelected)); } } }
        [DataSourceProperty]
        public bool IsVoteAbstainSelected { get => _isVoteAbstainSelected; set { if (value != _isVoteAbstainSelected) { _isVoteAbstainSelected = value; OnPropertyChangedWithValue(value, nameof(IsVoteAbstainSelected)); } } }
        [DataSourceProperty]
        public bool IsVoteYaySelected { get => _isVoteYaySelected; set { if (value != _isVoteYaySelected) { _isVoteYaySelected = value; OnPropertyChangedWithValue(value, nameof(IsVoteYaySelected)); } } }
        [DataSourceProperty]
        public bool IsCommit25Selected { get => _isCommit25Selected; set { if (value != _isCommit25Selected) { _isCommit25Selected = value; OnPropertyChangedWithValue(value, nameof(IsCommit25Selected)); } } }
        [DataSourceProperty]
        public bool IsCommit75Selected { get => _isCommit75Selected; set { if (value != _isCommit75Selected) { _isCommit75Selected = value; OnPropertyChangedWithValue(value, nameof(IsCommit75Selected)); } } }
        [DataSourceProperty]
        public bool IsCommit150Selected { get => _isCommit150Selected; set { if (value != _isCommit150Selected) { _isCommit150Selected = value; OnPropertyChangedWithValue(value, nameof(IsCommit150Selected)); } } }
        [DataSourceProperty]
        public bool IsRejectDisabled { get => _isRejectDisabled; set { if (value != _isRejectDisabled) { _isRejectDisabled = value; OnPropertyChangedWithValue(value, "IsRejectDisabled"); } } }
        [DataSourceProperty]
        public bool IsSignDisabled { get => _isSignDisabled; set { if (value != _isSignDisabled) { _isSignDisabled = value; OnPropertyChangedWithValue(value, "IsSignDisabled"); } } }
        [DataSourceProperty]
        public HintViewModel RejectTreatyHint { get => _rejectTreatyHint; set { if (value != _rejectTreatyHint) { _rejectTreatyHint = value; OnPropertyChangedWithValue(value, "RejectTreatyHint"); } } }
        [DataSourceProperty]
        public HintViewModel SignTreatyHint { get => _signTreatyHint; set { if (value != _signTreatyHint) { _signTreatyHint = value; OnPropertyChangedWithValue(value, "SignTreatyHint"); } } }
        [DataSourceProperty]
        public HintViewModel ResetTreatyHint { get => _resetTreatyHint; set { if (value != _resetTreatyHint) { _resetTreatyHint = value; OnPropertyChangedWithValue(value, nameof(ResetTreatyHint)); } } }
        [DataSourceProperty]
        public string PlayerVoteText { get => _playerVoteText; set { if (value != _playerVoteText) { _playerVoteText = value; OnPropertyChangedWithValue(value, "PlayerVoteText"); } } }
        [DataSourceProperty]
        public string PlayerCommitmentText { get => _playerCommitmentText; set { if (value != _playerCommitmentText) { _playerCommitmentText = value; OnPropertyChangedWithValue(value, "PlayerCommitmentText"); } } }
        [DataSourceProperty]
        public string PlayerRatificationText { get => _playerRatificationText; set { if (value != _playerRatificationText) { _playerRatificationText = value; OnPropertyChangedWithValue(value, "PlayerRatificationText"); } } }
        [DataSourceProperty]
        public string OpponentRatificationText { get => _opponentRatificationText; set { if (value != _opponentRatificationText) { _opponentRatificationText = value; OnPropertyChangedWithValue(value, "OpponentRatificationText"); } } }
        [DataSourceProperty]
        public string PredictedOutcomeText { get => _predictedOutcomeText; set { if (value != _predictedOutcomeText) { _predictedOutcomeText = value; OnPropertyChangedWithValue(value, "PredictedOutcomeText"); } } }
        [DataSourceProperty]
        public Color PlayerRatificationColor { get => _playerRatificationColor; set { if (value != _playerRatificationColor) { _playerRatificationColor = value; OnPropertyChangedWithValue(value, "PlayerRatificationColor"); } } }
        [DataSourceProperty]
        public Color OpponentRatificationColor { get => _opponentRatificationColor; set { if (value != _opponentRatificationColor) { _opponentRatificationColor = value; OnPropertyChangedWithValue(value, "OpponentRatificationColor"); } } }
        [DataSourceProperty]
        public Color PredictedOutcomeColor { get => _predictedOutcomeColor; set { if (value != _predictedOutcomeColor) { _predictedOutcomeColor = value; OnPropertyChangedWithValue(value, "PredictedOutcomeColor"); } } }
        [DataSourceProperty]
        public bool IsFiefsTabSelected { get => _isFiefsTabSelected; set { if (value != _isFiefsTabSelected) { _isFiefsTabSelected = value; OnPropertyChangedWithValue(value, nameof(IsFiefsTabSelected)); } } }
        [DataSourceProperty]
        public bool IsPrisonersTabSelected { get => _isPrisonersTabSelected; set { if (value != _isPrisonersTabSelected) { _isPrisonersTabSelected = value; OnPropertyChangedWithValue(value, nameof(IsPrisonersTabSelected)); } } }
        [DataSourceProperty]
        public bool IsPoliticsTabSelected { get => _isPoliticsTabSelected; set { if (value != _isPoliticsTabSelected) { _isPoliticsTabSelected = value; OnPropertyChangedWithValue(value, nameof(IsPoliticsTabSelected)); } } }
        [DataSourceProperty]
        public bool IsWealthTabSelected { get => _isWealthTabSelected; set { if (value != _isWealthTabSelected) { _isWealthTabSelected = value; OnPropertyChangedWithValue(value, nameof(IsWealthTabSelected)); } } }
        [DataSourceProperty]
        public bool IsDraftEditingDisabled { get => _isDraftEditingDisabled; set { if (value != _isDraftEditingDisabled) { _isDraftEditingDisabled = value; OnPropertyChangedWithValue(value, nameof(IsDraftEditingDisabled)); } } }
        [DataSourceProperty]
        public string ReparationsText { get => _reparationsText; set { if (value != _reparationsText) { _reparationsText = value; OnPropertyChangedWithValue(value, nameof(ReparationsText)); } } }
        [DataSourceProperty]
        public string ReparationsCostText { get => _reparationsCostText; set { if (value != _reparationsCostText) { _reparationsCostText = value; OnPropertyChangedWithValue(value, nameof(ReparationsCostText)); } } }
        [DataSourceProperty]
        public string TributeText { get => _tributeText; set { if (value != _tributeText) { _tributeText = value; OnPropertyChangedWithValue(value, nameof(TributeText)); } } }
        [DataSourceProperty]
        public string TributeCostText { get => _tributeCostText; set { if (value != _tributeCostText) { _tributeCostText = value; OnPropertyChangedWithValue(value, nameof(TributeCostText)); } } }
        [DataSourceProperty]
        public HintViewModel PrisonersTabHint { get => _prisonersTabHint; set { if (value != _prisonersTabHint) { _prisonersTabHint = value; OnPropertyChangedWithValue(value, nameof(PrisonersTabHint)); } } }
        [DataSourceProperty]
        public HintViewModel PoliticsTabHint { get => _politicsTabHint; set { if (value != _politicsTabHint) { _politicsTabHint = value; OnPropertyChangedWithValue(value, nameof(PoliticsTabHint)); } } }
        [DataSourceProperty]
        public HintViewModel DraftTabDisabledHint { get => _draftTabDisabledHint; set { if (value != _draftTabDisabledHint) { _draftTabDisabledHint = value; OnPropertyChangedWithValue(value, nameof(DraftTabDisabledHint)); } } }
        [DataSourceProperty]
        public int DraftEditorHeight { get => _draftEditorHeight; set { if (value != _draftEditorHeight) { _draftEditorHeight = value; OnPropertyChangedWithValue(value, nameof(DraftEditorHeight)); } } }
        [DataSourceProperty]
        public int UpperPanelHeight { get => _upperPanelHeight; set { if (value != _upperPanelHeight) { _upperPanelHeight = value; OnPropertyChangedWithValue(value, nameof(UpperPanelHeight)); } } }
        [DataSourceProperty]
        public int LowerPanelHeight { get => _lowerPanelHeight; set { if (value != _lowerPanelHeight) { _lowerPanelHeight = value; OnPropertyChangedWithValue(value, nameof(LowerPanelHeight)); } } }
        [DataSourceProperty]
        public int DraftTermsHeight { get => _draftTermsHeight; set { if (value != _draftTermsHeight) { _draftTermsHeight = value; OnPropertyChangedWithValue(value, nameof(DraftTermsHeight)); } } }
        [DataSourceProperty]
        public bool IsFiefDraftScrollbarVisible { get => _isFiefDraftScrollbarVisible; set { if (value != _isFiefDraftScrollbarVisible) { _isFiefDraftScrollbarVisible = value; OnPropertyChangedWithValue(value, nameof(IsFiefDraftScrollbarVisible)); } } }
        [DataSourceProperty]
        public bool IsPrisonerDraftScrollbarVisible { get => _isPrisonerDraftScrollbarVisible; set { if (value != _isPrisonerDraftScrollbarVisible) { _isPrisonerDraftScrollbarVisible = value; OnPropertyChangedWithValue(value, nameof(IsPrisonerDraftScrollbarVisible)); } } }
        [DataSourceProperty]
        public bool IsPoliticsDraftScrollbarVisible { get => _isPoliticsDraftScrollbarVisible; set { if (value != _isPoliticsDraftScrollbarVisible) { _isPoliticsDraftScrollbarVisible = value; OnPropertyChangedWithValue(value, nameof(IsPoliticsDraftScrollbarVisible)); } } }
        [DataSourceProperty]
        public bool IsDraftTermsScrollbarVisible { get => _isDraftTermsScrollbarVisible; set { if (value != _isDraftTermsScrollbarVisible) { _isDraftTermsScrollbarVisible = value; OnPropertyChangedWithValue(value, nameof(IsDraftTermsScrollbarVisible)); } } }
        [DataSourceProperty]
        public bool IsDemandsMode { get => _isDemandsMode; set { if (value != _isDemandsMode) { _isDemandsMode = value; OnPropertyChangedWithValue(value, nameof(IsDemandsMode)); } } }
        [DataSourceProperty]
        public bool IsOfferingsMode { get => _isOfferingsMode; set { if (value != _isOfferingsMode) { _isOfferingsMode = value; OnPropertyChangedWithValue(value, nameof(IsOfferingsMode)); } } }
        [DataSourceProperty]
        public MBBindingList<TreatyCouncilMemberVM> PlayerCouncil { get; }
        [DataSourceProperty]
        public MBBindingList<TreatyCouncilMemberVM> OpponentCouncil { get; }
        [DataSourceProperty]
        public MBBindingList<TreatyTermVM> DraftTerms { get; }
        [DataSourceProperty]
        public MBBindingList<TreatyFiefDraftOptionVM> AvailableFiefTerms { get; }
        [DataSourceProperty]
        public MBBindingList<TreatyPrisonerDraftOptionVM> AvailablePrisonerTerms { get; }
        [DataSourceProperty]
        public MBBindingList<TreatyClaimDraftOptionVM> AvailablePoliticsTerms { get; }
    }
}
