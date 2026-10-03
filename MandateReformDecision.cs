using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class MandateReformDecision : KingdomDecision
    {
        public sealed class ReformOutcome : DecisionOutcome
        {
            [SaveableField(100)] public bool Reform;
            [SaveableField(101)] public string Law;
            public ReformOutcome(bool reform, string law) { Reform = reform; Law = law; }
            public override TextObject GetDecisionTitle() => new TextObject(Reform ? "{=BC_MandateAdopt}Adopt {LAW}" : "{=BC_MandateRetain}Retain {LAW}")
                .SetTextVariable("LAW", RealmLawRegistry.Instance.Find(Law)?.Name ?? TextObject.GetEmpty());
            public override TextObject GetDecisionDescription() => new TextObject("{=BC_MandateFuture}This law also alters the sitting ruler's mandate. Fixed terms are counted from the mandate's start; a lifetime mandate converted to a fixed term is counted from adoption. If the new term has already elapsed, an election follows deliberation.");
            public override string GetDecisionLink() => null;
            public override ImageIdentifier GetDecisionImageIdentifier() => null;
        }
        [SaveableField(100)] internal string MotionId;
        [SaveableField(101)] internal string OldLaw;
        [SaveableField(102)] internal string NewLaw;
        [SaveableField(103)] internal int Direction;
        [SaveableField(104)] internal FactionType SponsorFaction;
        [SaveableField(105)] internal bool Attempted;
        [SaveableField(106)] internal bool Enacted;
        [SaveableField(107)] internal bool Resolved;
        public MandateReformDecision(Clan proposer, string oldLaw, string newLaw, int direction, FactionType sponsorFaction) : base(proposer)
        { OldLaw = oldLaw; NewLaw = newLaw; Direction = direction; SponsorFaction = sponsorFaction; }
        internal ReformOutcome Outcome(bool reform) => new ReformOutcome(reform, reform ? NewLaw : OldLaw);
        public override bool IsKingsVoteAllowed => false;
        public override bool IsAllowed() => CourtAgendaBehavior.Current?.ValidateMandateDecision(this) == true;
        public override int GetProposalInfluenceCost() => CourtMandateObjectiveSource.Cost(ProposerClan);
        public override TextObject GetGeneralTitle() => new TextObject("{=BC_MandateTitle}Reform of elective mandates");
        public override TextObject GetSupportTitle() => GetGeneralTitle();
        public override TextObject GetChooseTitle() => GetGeneralTitle();
        public override TextObject GetSupportDescription() => new TextObject("{=BC_MandateBallot}The court proposes replacing {OLD} with {NEW}. Shall the realm adopt this change?")
            .SetTextVariable("OLD", RealmLawRegistry.Instance.Find(OldLaw)?.Name ?? TextObject.GetEmpty())
            .SetTextVariable("NEW", RealmLawRegistry.Instance.Find(NewLaw)?.Name ?? TextObject.GetEmpty());
        public override TextObject GetChooseDescription() => GetSupportDescription();
        protected override bool CanProposerClanChangeOpinion() => true;
        public override float CalculateMeritOfOutcome(DecisionOutcome outcome) => 0;
        public override IEnumerable<DecisionOutcome> DetermineInitialCandidates() => new DecisionOutcome[] { Outcome(true), Outcome(false) };
        public override Clan DetermineChooser() => Kingdom?.RulingClan;
        internal float NaturalSupport(Clan voter)
        {
            if (!CourtAgendaBehavior.Eligible(voter, Kingdom)) return 0;
            var faction = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetIdeologicalFaction(voter);
            int lean = voter == Kingdom.RulingClan ? 0 : faction?.Type == FactionType.Nobility ? 20 : faction?.Type == FactionType.Liberty ? -20 : 0;
            bool aligned = voter == Kingdom.RulingClan && voter != Clan.PlayerClan
                && CourtAgendaBehavior.Current?.TitleFactionFavored(Kingdom, SponsorFaction) == true;
            return CourtMandateRules.Support(Direction, lean, voter.Leader.GetRelation(ProposerClan.Leader),
                voter.Leader.GetTraitLevel(DefaultTraits.Honor), aligned);
        }
        public override float DetermineSupport(Clan clan, DecisionOutcome outcome)
        {
            if (!(outcome is ReformOutcome choice)) return 0;
            var pledge = CourtAgendaBehavior.Current?.MandatePledge(MotionId, clan);
            if (pledge?.Committed == true) return choice.Reform == pledge.Reform ? 10000 : -100;
            float reform = NaturalSupport(clan);
            return choice.Reform ? reform : 100 - reform;
        }
        public override void DetermineSponsors(MBReadOnlyList<DecisionOutcome> outcomes)
        {
            foreach (var outcome in outcomes.OfType<ReformOutcome>())
            {
                var sponsor = outcome.Reform ? ProposerClan : Kingdom.Clans.Where(c => CourtAgendaBehavior.Eligible(c, Kingdom)
                    && c != ProposerClan && NaturalSupport(c) <= 50).OrderBy(c => NaturalSupport(c)).FirstOrDefault();
                if (sponsor != null) outcome.SetSponsor(sponsor);
            }
        }
        public override void ApplyChosenOutcome(DecisionOutcome outcome)
        {
            if (Attempted) return;
            if (!(outcome is ReformOutcome choice) || choice.Law != (choice.Reform ? NewLaw : OldLaw) || !IsAllowed())
            { CourtAgendaBehavior.Current?.CancelMandateDecision(this, "mandate_ballot_invalid"); return; }
            Attempted = true;
            Enacted = choice.Reform && CourtMandateObjectiveSource.Laws.TryApplyCourtMandate(this, out _);
            if (choice.Reform && !Enacted) CourtAgendaBehavior.Current?.CancelMandateDecision(this, "mandate_application_unverified");
            else { Resolved = true; CourtAgendaBehavior.Current?.ConcludeMandate(this, Enacted); }
        }
        public override TextObject GetSecondaryEffects() => Outcome(true).GetDecisionDescription();
        public override void ApplySecondaryEffects(MBReadOnlyList<DecisionOutcome> outcomes, DecisionOutcome chosen) { }
        public override TextObject GetChosenOutcomeText(DecisionOutcome outcome, SupportStatus status, bool isShortVersion = false) =>
            new TextObject(Enacted ? "{=BC_MandateEnacted}The court has adopted {LAW}, governing the present mandate as well as future elected rulers."
                : Resolved ? "{=BC_MandateRetained}The court has retained {LAW}." : "{=BC_MandateInvalid}The proposal could no longer be decided.")
                .SetTextVariable("LAW", RealmLawRegistry.Instance.Find(Enacted ? NewLaw : OldLaw)?.Name ?? TextObject.GetEmpty());
        public override DecisionOutcome GetQueriedDecisionOutcome(MBReadOnlyList<DecisionOutcome> outcomes) => outcomes.FirstOrDefault();
        protected override bool ShouldBeCancelledInternal() => !IsAllowed();
    }
}
