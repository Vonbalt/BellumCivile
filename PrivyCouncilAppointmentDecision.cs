using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    public sealed class PrivyCouncilAppointmentDecision : KingdomDecision
    {
        public sealed class PrivyCouncilAppointmentOutcome : DecisionOutcome
        {
            [SaveableField(100)]
            private readonly Clan _candidateClan;

            public Clan CandidateClan => _candidateClan;

            public PrivyCouncilAppointmentOutcome(Clan candidateClan)
            {
                _candidateClan = candidateClan;
            }

            public override TextObject GetDecisionTitle()
            {
                return _candidateClan?.Leader?.Name ?? _candidateClan?.Name ?? TextObject.GetEmpty();
            }

            public override TextObject GetDecisionDescription()
            {
                TextObject description = new TextObject("{=BC_Council_AppointmentCandidate}{CANDIDATE_NAME} should hold this office on the {COUNCIL_NAME}.");
                description.SetTextVariable("CANDIDATE_NAME", _candidateClan?.Leader?.Name ?? _candidateClan?.Name ?? TextObject.GetEmpty());
                return CourtInstitutionDisplayHelper.ApplyPrivyCouncilName(description, _candidateClan?.Kingdom);
            }

            public override string GetDecisionLink()
            {
                return null;
            }

            public override ImageIdentifier GetDecisionImageIdentifier()
            {
                return null;
            }
        }

        [SaveableField(100)]
        private readonly PrivyCouncilOffice _office;

        [SaveableField(101)]
        private readonly List<Clan> _shortlistedClans;

        [SaveableField(102)]
        private readonly Clan _incumbentClan;

        [SaveableField(103)]
        private readonly float _incumbentControversy;

        [SaveableField(104)] private Clan _appliedCandidate;
        [SaveableField(105)] private bool _aftermathApplied;
        [SaveableField(106)] private bool _outcomeAttempted;
        [SaveableField(107)] internal string CourtAgendaId;

        internal Clan AppliedCandidate => _appliedCandidate;

        public PrivyCouncilOffice Office => _office;

        public override bool IsKingsVoteAllowed => false;

        public PrivyCouncilAppointmentDecision(Clan proposerClan, PrivyCouncilOffice office)
            : this(proposerClan, office, null)
        {
        }

        public PrivyCouncilAppointmentDecision(
            Clan proposerClan,
            PrivyCouncilOffice office,
            IEnumerable<Clan> shortlistedClans)
            : base(proposerClan)
        {
            _office = office;
            _shortlistedClans = shortlistedClans?
                .Where(clan => clan != null)
                .Distinct()
                .Take(3)
                .ToList() ?? new List<Clan>();
            PrivyCouncilBehavior behavior = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            _incumbentClan = behavior?.GetOfficeHolder(proposerClan?.Kingdom, office);
            _incumbentControversy = behavior?.GetOfficeRecord(proposerClan?.Kingdom, office)?.Controversy ?? 0f;
        }

        public override bool IsAllowed()
        {
            PrivyCouncilBehavior behavior = GetBehavior();
            return behavior != null
                && Kingdom != null
                && Kingdom.RulingClan != null
                && (string.IsNullOrEmpty(CourtAgendaId) || CourtAgendaBehavior.Current?
                    .ValidateCouncilProceeding(CourtAgendaId, Kingdom, _office, ProposerClan) == true)
                && behavior.IsOfficeUnlocked(Kingdom, _office)
                && GetValidShortlist(behavior).Count > 0;
        }

        public override int GetProposalInfluenceCost()
        {
            return GetBehavior()?.GetFactionMotionInfluenceCost(ProposerClan, PrivyCouncilBehavior.AppointmentProposalInfluenceCost)
                ?? PrivyCouncilBehavior.AppointmentProposalInfluenceCost;
        }

        public override TextObject GetGeneralTitle()
        {
            return BuildTitle("{=BC_Council_AppointmentGeneral}Appointment of the {OFFICE}");
        }

        public override TextObject GetSupportTitle()
        {
            return BuildTitle("{=BC_Council_AppointmentSupportTitle}Vote for the next {OFFICE} of {KINGDOM_NAME}");
        }

        public override TextObject GetChooseTitle()
        {
            return BuildTitle("{=BC_Council_AppointmentChooseTitle}Choose the next {OFFICE} of {KINGDOM_NAME}");
        }

        public override TextObject GetSupportDescription()
        {
            return BuildTitle("{=BC_Council_AppointmentSupportDescription}The nobility will decide who should serve as {OFFICE}. You may commit your influence to a candidate.");
        }

        public override TextObject GetChooseDescription()
        {
            return BuildTitle("{=BC_Council_AppointmentChooseDescription}The council has presented its candidates for {OFFICE}. The ruler may confirm its choice or overrule it.");
        }

        protected override bool CanProposerClanChangeOpinion()
        {
            return true;
        }

        public override float CalculateMeritOfOutcome(DecisionOutcome candidateOutcome)
        {
            PrivyCouncilAppointmentOutcome outcome = candidateOutcome as PrivyCouncilAppointmentOutcome;
            return GetBehavior()?.CalculateAppointmentMerit(Kingdom, outcome?.CandidateClan, _office) ?? -100f;
        }

        public override IEnumerable<DecisionOutcome> DetermineInitialCandidates()
        {
            PrivyCouncilBehavior behavior = GetBehavior();
            if (behavior == null || Kingdom == null)
                return Enumerable.Empty<DecisionOutcome>();

            return GetValidShortlist(behavior)
                .Select(candidate => (DecisionOutcome)new PrivyCouncilAppointmentOutcome(candidate))
                .ToList();
        }

        public override Clan DetermineChooser()
        {
            return Kingdom?.RulingClan;
        }

        public override float DetermineSupport(Clan clan, DecisionOutcome possibleOutcome)
        {
            PrivyCouncilAppointmentOutcome outcome = possibleOutcome as PrivyCouncilAppointmentOutcome;
            CouncilAppointmentDeliberationBehavior deliberation = Campaign.Current?
                .GetCampaignBehavior<CouncilAppointmentDeliberationBehavior>();
            string committedCandidate = deliberation?.GetCommittedCandidateVote(Kingdom, _office, clan);
            if (!string.IsNullOrEmpty(committedCandidate))
                return outcome?.CandidateClan?.StringId == committedCandidate ? 10000f : -100f;

            return GetBehavior()?.CalculateAppointmentSupport(Kingdom, clan, outcome?.CandidateClan, _office) ?? -100f;
        }

        public override void DetermineSponsors(MBReadOnlyList<DecisionOutcome> possibleOutcomes)
        {
            foreach (PrivyCouncilAppointmentOutcome outcome in possibleOutcomes.OfType<PrivyCouncilAppointmentOutcome>())
                outcome.SetSponsor(outcome.CandidateClan);
        }

        public override void ApplyChosenOutcome(DecisionOutcome chosenOutcome)
        {
            if (_outcomeAttempted) return;
            _outcomeAttempted = true;
            PrivyCouncilAppointmentOutcome outcome = chosenOutcome as PrivyCouncilAppointmentOutcome;
            var council = GetBehavior();
            if (outcome?.CandidateClan == null
                || council?.TryAppointOffice(Kingdom, _office, outcome.CandidateClan, showNotification: false) != true
                || council.GetOfficeHolder(Kingdom, _office) != outcome.CandidateClan)
            {
                BellumCivileLogger.Log($"Privy council appointment could not be applied; kingdom={Kingdom?.StringId ?? "null"}; office={_office}; candidate={outcome?.CandidateClan?.StringId ?? "null"}.");
                return;
            }
            _appliedCandidate = outcome.CandidateClan;
        }

        public override TextObject GetSecondaryEffects()
        {
            return new TextObject("{=BC_Council_AppointmentSecondary}The appointed councilor assumes responsibility for the office and its controversies.");
        }

        public override void ApplySecondaryEffects(MBReadOnlyList<DecisionOutcome> possibleOutcomes, DecisionOutcome chosenOutcome)
        {
            PrivyCouncilAppointmentOutcome chosen = chosenOutcome as PrivyCouncilAppointmentOutcome;
            if (chosen?.CandidateClan == null || Kingdom?.RulingClan?.Leader == null)
                return;

            PrivyCouncilBehavior council = GetBehavior();
            if (_aftermathApplied || _appliedCandidate != chosen.CandidateClan
                || council?.GetOfficeHolder(Kingdom, _office) != _appliedCandidate) return;
            _aftermathApplied = true;
            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            IdeologyEventShockBehavior shocks = Campaign.Current?.GetCampaignBehavior<IdeologyEventShockBehavior>();
            List<PrivyCouncilAppointmentOutcome> outcomes = possibleOutcomes?
                .OfType<PrivyCouncilAppointmentOutcome>()
                .Where(outcome => outcome?.CandidateClan != null)
                .ToList() ?? new List<PrivyCouncilAppointmentOutcome>();
            PrivyCouncilAppointmentOutcome popular = outcomes
                .OrderByDescending(outcome => outcome.TotalSupportPoints)
                .ThenBy(outcome => outcome.CandidateClan.StringId)
                .FirstOrDefault();
            bool rulerOverride = popular != null && popular != chosen;
            bool retained = chosen.CandidateClan == _incumbentClan;

            ApplyPersonalAftermath(council, outcomes, chosen, rulerOverride, retained);

            Dictionary<FactionObject, PrivyCouncilAppointmentOutcome> preferences =
                BuildFactionPreferences(outcomes, factionManager);
            FactionObject winningFaction = factionManager?.GetIdeologicalFaction(chosen.CandidateClan);
            List<FactionObject> displeasedFactions = new List<FactionObject>();

            if (winningFaction != null)
            {
                shocks?.RecordCouncilAppointmentReaction(
                    winningFaction,
                    retained ? CouncilAppointmentReaction.IncumbentRetained : CouncilAppointmentReaction.MemberAppointed,
                    retained ? C.CouncilRetentionFactionMood : C.CouncilAppointmentFactionMood);
            }

            foreach (KeyValuePair<FactionObject, PrivyCouncilAppointmentOutcome> preference in preferences)
            {
                FactionObject faction = preference.Key;
                if (faction == null || faction == winningFaction || preference.Value == chosen)
                    continue;

                bool directlyOverruled = rulerOverride && preference.Value == popular;
                shocks?.RecordCouncilAppointmentReaction(
                    faction,
                    directlyOverruled ? CouncilAppointmentReaction.RulerOverride : CouncilAppointmentReaction.CandidatePassedOver,
                    directlyOverruled ? C.CouncilOverrideFactionMood : C.CouncilPassedOverFactionMood);
                displeasedFactions.Add(faction);
            }

            ShowPoliticalOutcome(chosen.CandidateClan, winningFaction, displeasedFactions, rulerOverride, retained);
        }

        public override TextObject GetChosenOutcomeText(
            DecisionOutcome chosenOutcome,
            SupportStatus supportStatus,
            bool isShortVersion = false)
        {
            PrivyCouncilAppointmentOutcome outcome = chosenOutcome as PrivyCouncilAppointmentOutcome;
            bool retained = outcome?.CandidateClan != null
                && _incumbentClan == outcome.CandidateClan;
            TextObject result = retained
                ? BuildTitle("{=BC_Council_AppointmentRetained}The council of {KINGDOM_NAME} has retained {CANDIDATE_NAME} as {OFFICE}.")
                : BuildTitle("{=BC_Council_AppointmentChosen}The council of {KINGDOM_NAME} has appointed {CANDIDATE_NAME} as {OFFICE}.");
            result.SetTextVariable("CANDIDATE_NAME", outcome?.CandidateClan?.Leader?.Name ?? outcome?.CandidateClan?.Name ?? TextObject.GetEmpty());
            return result;
        }

        public override DecisionOutcome GetQueriedDecisionOutcome(MBReadOnlyList<DecisionOutcome> possibleOutcomes)
        {
            return possibleOutcomes?.OrderByDescending(outcome => outcome.Merit).FirstOrDefault();
        }

        protected override bool ShouldBeCancelledInternal()
        {
            return !IsAllowed();
        }

        private TextObject BuildTitle(string template)
        {
            TextObject text = new TextObject(template);
            text.SetTextVariable("OFFICE", PrivyCouncilBehavior.GetOfficeName(_office, Kingdom));
            text.SetTextVariable("KINGDOM_NAME", Kingdom?.Name ?? TextObject.GetEmpty());
            return text;
        }

        private static PrivyCouncilBehavior GetBehavior()
        {
            return Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
        }

        private List<Clan> GetValidShortlist(PrivyCouncilBehavior behavior)
        {
            if (behavior == null || Kingdom == null)
                return new List<Clan>();

            IReadOnlyList<Clan> validCandidates = behavior.GetAppointmentCandidatesForVote(Kingdom, _office);
            IEnumerable<Clan> source = _shortlistedClans != null && _shortlistedClans.Count > 0
                ? _shortlistedClans
                : validCandidates;
            return source
                .Where(candidate => candidate != null
                    && candidate != Kingdom.RulingClan
                    && validCandidates.Contains(candidate))
                .Distinct()
                .Take(3)
                .ToList();
        }

        private void ApplyPersonalAftermath(
            PrivyCouncilBehavior council,
            IEnumerable<PrivyCouncilAppointmentOutcome> outcomes,
            PrivyCouncilAppointmentOutcome chosen,
            bool rulerOverride,
            bool retained)
        {
            Hero ruler = Kingdom.RulingClan.Leader;
            Hero candidate = chosen.CandidateClan.Leader;
            if (candidate != null && candidate != ruler)
            {
                int gain = retained ? C.CouncilRetentionRelationGain : C.CouncilAppointmentRelationGain;
                RelationMemoryService.ApplyChange(
                    candidate,
                    ruler,
                    gain,
                    candidate == Hero.MainHero || ruler == Hero.MainHero,
                    RelationMemorySources.AppointedMeToCouncil,
                    10f,
                    RelationMemoryScope.Personal,
                    PrivyCouncilBehavior.GetLocalizedOfficeName(_office, Kingdom).ToString());
            }

            if (!retained && _incumbentClan != null)
                council?.ApplyDismissalAftermath(Kingdom, _incumbentClan, _incumbentControversy, _office);

            if (!rulerOverride)
                return;

            HashSet<Clan> penalized = new HashSet<Clan>();
            foreach (PrivyCouncilAppointmentOutcome outcome in outcomes.Where(outcome => outcome != chosen))
            {
                foreach (Supporter supporter in outcome.SupporterList ?? new MBList<Supporter>())
                {
                    Clan voter = supporter?.Clan;
                    if (voter == null
                        || voter == Kingdom.RulingClan
                        || voter.Leader == null
                        || supporter.SupportWeight <= Supporter.SupportWeights.StayNeutral
                        || !penalized.Add(voter))
                    {
                        continue;
                    }

                    RelationMemoryService.ApplyChange(
                        voter.Leader,
                        ruler,
                        C.CouncilOverrideRelationPenalty,
                        voter.Leader == Hero.MainHero || ruler == Hero.MainHero,
                        RelationMemorySources.OverruledMyCouncilVote,
                        7f,
                        RelationMemoryScope.Personal,
                        PrivyCouncilBehavior.GetLocalizedOfficeName(_office, Kingdom).ToString());
                }
            }
        }

        private static Dictionary<FactionObject, PrivyCouncilAppointmentOutcome> BuildFactionPreferences(
            IEnumerable<PrivyCouncilAppointmentOutcome> outcomes,
            FactionManagerBehavior factionManager)
        {
            Dictionary<FactionObject, Dictionary<PrivyCouncilAppointmentOutcome, int>> supportByFaction =
                new Dictionary<FactionObject, Dictionary<PrivyCouncilAppointmentOutcome, int>>();

            foreach (PrivyCouncilAppointmentOutcome outcome in outcomes)
            {
                if (outcome?.SupporterList == null)
                    continue;

                foreach (Supporter supporter in outcome.SupporterList)
                {
                    Clan supporterClan = supporter?.Clan;
                    if (supporterClan == null)
                        continue;

                    int commitment = WarDeclarationCouncilService.GetCommitment(supporter.SupportWeight);
                    FactionObject faction = factionManager?.GetIdeologicalFaction(supporterClan);
                    if (commitment <= 0 || faction == null)
                        continue;

                    if (!supportByFaction.TryGetValue(faction, out Dictionary<PrivyCouncilAppointmentOutcome, int> totals))
                    {
                        totals = new Dictionary<PrivyCouncilAppointmentOutcome, int>();
                        supportByFaction[faction] = totals;
                    }

                    totals.TryGetValue(outcome, out int current);
                    totals[outcome] = current + commitment;
                }
            }

            return supportByFaction.ToDictionary(
                pair => pair.Key,
                pair => pair.Value
                    .OrderByDescending(total => total.Value)
                    .ThenByDescending(total => total.Key.TotalSupportPoints)
                    .ThenBy(total => total.Key.CandidateClan.StringId)
                    .Select(total => total.Key)
                    .FirstOrDefault());
        }

        private void ShowPoliticalOutcome(
            Clan candidate,
            FactionObject winningFaction,
            IReadOnlyCollection<FactionObject> displeasedFactions,
            bool rulerOverride,
            bool retained)
        {
            TextObject message;
            if (rulerOverride)
            {
                message = new TextObject("{=BC_Council_AppointmentOverrideMsg}{RULER_NAME} has overruled the council and appointed {CANDIDATE_NAME} as {OFFICE}.");
                message.SetTextVariable("RULER_NAME", Kingdom.RulingClan.Leader.Name);
            }
            else if (retained)
            {
                message = new TextObject("{=BC_Council_AppointmentRetainedMsg}The council has retained {CANDIDATE_NAME} as {OFFICE}.");
            }
            else if (winningFaction != null)
            {
                message = new TextObject("{=BC_Council_AppointmentFactionMsg}The {FACTION_NAME} celebrate the appointment of {CANDIDATE_NAME} as {OFFICE}.");
                message.SetTextVariable("FACTION_NAME", winningFaction.GetDisplayName());
            }
            else
            {
                message = new TextObject("{=BC_Council_Appointed}{CANDIDATE_NAME} has been appointed {OFFICE} on the {COUNCIL_NAME} of {KINGDOM_NAME}.");
                message.SetTextVariable("KINGDOM_NAME", Kingdom.Name);
            }

            message.SetTextVariable("CANDIDATE_NAME", candidate.Leader?.Name ?? candidate.Name);
            message.SetTextVariable("OFFICE", PrivyCouncilBehavior.GetOfficeName(_office, Kingdom));
            message = CourtInstitutionDisplayHelper.ApplyPrivyCouncilName(message, Kingdom);
            BellumCivileNotifications.Show(
                message,
                rulerOverride ? BellumNotificationColors.Warning : BellumNotificationColors.Politics,
                primaryKingdom: Kingdom,
                primaryClan: candidate,
                isPersonal: Kingdom == Clan.PlayerClan?.Kingdom);

            if (displeasedFactions == null || displeasedFactions.Count == 0)
                return;

            TextObject reaction = rulerOverride
                ? new TextObject("{=BC_Council_AppointmentOverrideReaction}The {FACTION_NAMES} leave court angered that their preferred candidate was denied the office.")
                : new TextObject("{=BC_Council_AppointmentPassedOverReaction}The {FACTION_NAMES} are displeased that their preferred candidates were passed over.");
            reaction.SetTextVariable("FACTION_NAMES", new TextObject("{=!}" + JoinFactionNames(displeasedFactions)));
            BellumCivileNotifications.Show(
                reaction,
                BellumNotificationColors.Warning,
                primaryKingdom: Kingdom,
                isPersonal: Kingdom == Clan.PlayerClan?.Kingdom);
        }

        private static string JoinFactionNames(IEnumerable<FactionObject> factions)
        {
            List<string> names = factions
                .Where(faction => faction != null)
                .Select(faction => faction.GetDisplayName().ToString())
                .Distinct()
                .ToList();
            if (names.Count <= 1)
                return names.FirstOrDefault() ?? string.Empty;
            if (names.Count == 2)
                return names[0] + " and " + names[1];
            return string.Join(", ", names.Take(names.Count - 1)) + ", and " + names[names.Count - 1];
        }
    }
}
