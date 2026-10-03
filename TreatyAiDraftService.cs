using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal enum TreatyNegotiationPosture
    {
        DefensiveConcessions,
        LimitedVictory,
        DecisiveVictory,
        TotalVictory
    }

    internal sealed class TreatyAiDraftResult
    {
        public IReadOnlyList<TreatyTermRecord> Terms { get; }
        public TreatyNegotiationPosture Posture { get; }
        public int TargetSpend { get; }
        public string Summary { get; }
        internal bool IsObjectiveAlternative { get; }

        public TreatyAiDraftResult(
            IEnumerable<TreatyTermRecord> terms,
            TreatyNegotiationPosture posture,
            int targetSpend,
            string summary, bool isObjectiveAlternative = false)
        {
            Terms = (terms ?? Enumerable.Empty<TreatyTermRecord>()).Where(term => term != null).ToList();
            Posture = posture;
            TargetSpend = Math.Max(0, targetSpend);
            Summary = summary ?? string.Empty;
            IsObjectiveAlternative = isObjectiveAlternative;
        }
    }

    internal sealed class TreatyAiTermCandidate
    {
        public TreatyTermRecord Term { get; }
        public float Priority { get; }
        public string Reason { get; }

        public int Cost => Math.Max(0, Term?.WarScoreCost ?? 0);

        public TreatyAiTermCandidate(TreatyTermRecord term, float priority, string reason)
        {
            Term = term;
            Priority = priority;
            Reason = reason ?? string.Empty;
        }
    }

    /// <summary>
    /// Creates AI treaty packages from the same validated candidates used by the player parley.
    /// The service deliberately owns selection only; ForeignTreatyBehavior remains responsible
    /// for council ratification and all treaty execution.
    /// </summary>
    internal static class TreatyAiDraftService
    {
        private const int CouncilAdjustmentStep = 10;
        private const float MinimumReciprocalDemandPriority = 45f;

        public static int GetCouncilAdjustmentStep(int budget)
        {
            return Math.Max(5, Math.Min(CouncilAdjustmentStep, Math.Max(1, budget / 5)));
        }

        public static TreatyNegotiationPosture DeterminePosture(
            TreatyProposalRecord proposal,
            Kingdom winner,
            Kingdom loser,
            Kingdom drafter)
        {
            int budget = Math.Max(0, proposal?.WarScoreBudget ?? 0);
            if (proposal?.IsForced == true || budget >= 90)
                return TreatyNegotiationPosture.TotalVictory;
            if (drafter == loser)
                return TreatyNegotiationPosture.DefensiveConcessions;
            if (budget >= 60)
                return TreatyNegotiationPosture.DecisiveVictory;
            return TreatyNegotiationPosture.LimitedVictory;
        }

        public static int CalculateDesiredSpend(
            TreatyProposalRecord proposal,
            Kingdom winner,
            Kingdom loser,
            Kingdom drafter, WarScoreRecord war = null)
        {
            int budget = Math.Max(0, proposal?.WarScoreBudget ?? 0);
            if (budget <= 0)
                return 0;

            TreatyNegotiationPosture posture = DeterminePosture(proposal, winner, loser, drafter);
            if (posture == TreatyNegotiationPosture.TotalVictory)
                return budget;

            if (drafter != loser)
                return budget;

            Hero ruler = (drafter ?? winner)?.RulingClan?.Leader;
            float share = posture == TreatyNegotiationPosture.DecisiveVictory ? 0.95f
                : posture == TreatyNegotiationPosture.DefensiveConcessions ? 0.85f
                : 0.85f;

            int generosity = ruler?.GetTraitLevel(DefaultTraits.Generosity) ?? 0;
            int calculating = ruler?.GetTraitLevel(DefaultTraits.Calculating) ?? 0;
            int mercy = ruler?.GetTraitLevel(DefaultTraits.Mercy) ?? 0;
            int valor = ruler?.GetTraitLevel(DefaultTraits.Valor) ?? 0;
            share += generosity * 0.02f + mercy * 0.03f;
            share -= calculating * 0.02f + valor * 0.025f;
            share += Math.Max(0f, 35f - CalculateRealmWarWill(drafter,war)) * 0.003f;

            share = Math.Max(0.75f, Math.Min(0.99f, share));
            return Math.Max(1, Math.Min(budget, (int)Math.Ceiling(budget * share)));
        }

        public static TreatyAiDraftResult BuildDraft(
            WarScoreRecord war,
            TreatyProposalRecord proposal,
            Kingdom winner,
            Kingdom loser,
            Kingdom drafter,
            int desiredSpend,
            bool allowReciprocalPrisonerOfferings = true)
            => BuildDraftCore(war, proposal, winner, loser, drafter, desiredSpend, allowReciprocalPrisonerOfferings, null);

        private static TreatyAiDraftResult BuildDraftCore(WarScoreRecord war, TreatyProposalRecord proposal,
            Kingdom winner, Kingdom loser, Kingdom drafter, int desiredSpend, bool allowReciprocalPrisonerOfferings,
            TreatyAiTermCandidate priorityClaim)
        {
            if (war == null || proposal == null || winner == null || loser == null)
            {
                return new TreatyAiDraftResult(
                    new[] { new TreatyTermRecord(TreatyTermType.WhitePeace, 0) },
                    TreatyNegotiationPosture.LimitedVictory,
                    0,
                    "invalid treaty context; white peace fallback");
            }

            int budget = Math.Max(0, proposal.WarScoreBudget);
            int targetSpend = Math.Max(0, Math.Min(budget, desiredSpend));
            TreatyNegotiationPosture posture = DeterminePosture(proposal, winner, loser, drafter);
            bool voluntaryOffering = drafter == loser;
            List<TreatyTermRecord> terms = new List<TreatyTermRecord>();
            List<string> reasons = new List<string>();
            int spent = 0;

            TreatyAiTermCandidate structural = priorityClaim != null ? null : GetStructuralSettlement(
                proposal, winner, loser, drafter, posture, targetSpend, voluntaryOffering,war);
            if (structural != null)
            {
                terms.Add(structural.Term);
                reasons.Add(structural.Reason);
                return new TreatyAiDraftResult(terms, posture, targetSpend,
                    $"{posture}; structural settlement: {structural.Reason}");
            }

            HashSet<string> bundledPrisoners = new HashSet<string>();
            List<TreatyAiTermCandidate> coreTerritory = new List<TreatyAiTermCandidate>();
            List<TreatyAiTermCandidate> optionalTerritory = new List<TreatyAiTermCandidate>();
            BuildTerritorialCandidates(war, winner, loser, posture, voluntaryOffering, coreTerritory, optionalTerritory);

            if (priorityClaim != null)
            {
                AddCandidates(new[] { priorityClaim }, terms, reasons, ref spent, targetSpend, budget, bundledPrisoners);
                coreTerritory.RemoveAll(c => c.Term.SettlementId == priorityClaim.Term.SettlementId);
                optionalTerritory.RemoveAll(c => c.Term.SettlementId == priorityClaim.Term.SettlementId);
            }

            bool noCoreTerritoryFits = !coreTerritory.Any(candidate => candidate?.Cost <= budget);
            AddCandidates(coreTerritory, terms, reasons, ref spent, targetSpend, budget, bundledPrisoners);

            List<TreatyAiTermCandidate> prisonerCandidates = BuildPrisonerCandidates(winner, loser, voluntaryOffering)
                .Where(candidate => !bundledPrisoners.Contains(candidate.Term.HeroId))
                .ToList();
            AddCandidates(prisonerCandidates, terms, reasons, ref spent, targetSpend, budget, bundledPrisoners);

            List<TreatyAiTermCandidate> politicalCandidates = BuildPoliticalCandidates(
                winner, loser, drafter, posture, voluntaryOffering, budget, noCoreTerritoryFits,war);
            AddCandidates(politicalCandidates, terms, reasons, ref spent, targetSpend, budget, bundledPrisoners);

            AddCandidates(optionalTerritory, terms, reasons, ref spent, targetSpend, budget, bundledPrisoners);
            AddWealthTerms(terms, reasons, winner, loser, drafter, targetSpend, budget, ref spent, voluntaryOffering);

            if (allowReciprocalPrisonerOfferings)
            {
                AddReciprocalPrisonerOfferings(
                    terms,
                    reasons,
                    coreTerritory
                        .Concat(prisonerCandidates)
                        .Concat(politicalCandidates)
                        .Concat(optionalTerritory),
                    winner,
                    loser,
                    targetSpend,
                    budget,
                    bundledPrisoners,
                    drafter == loser);
            }

            if (drafter == winner && terms.Any(t => t.Type == TreatyTermType.HostagePeace && t.FromKingdomId == loser.StringId)
                && GetTrait(winner.Leader, DefaultTraits.Honor) > 0 && GetTrait(winner.Leader, DefaultTraits.Mercy) > 0)
            {
                int durationDays = TreatyHostageTerms.DurationForDraft(terms);
                var pledge = TreatyHostageTerms.Available(winner, loser, durationDays).OrderByDescending(c => c.Tier).FirstOrDefault();
                if (pledge != null)
                {
                    var offer = TreatyHostageTerms.Create(winner, loser, pledge, true, durationDays);
                    if (CanCombineWithSelectedTerms(offer, terms))
                    { terms.Add(offer); reasons.Add("pledge our own kin in reciprocal good faith"); }
                }
            }
            if (terms.Count == 0)
            {
                terms.Add(new TreatyTermRecord(TreatyTermType.WhitePeace, 0));
                reasons.Add("no enforceable concession fit the available leverage");
            }

            return new TreatyAiDraftResult(
                terms,
                posture,
                targetSpend,
                $"{posture}; target={targetSpend}/{budget}; " + string.Join("; ", reasons));
        }

        private static TreatyAiTermCandidate GetStructuralSettlement(
            TreatyProposalRecord proposal,
            Kingdom winner,
            Kingdom loser,
            Kingdom drafter,
            TreatyNegotiationPosture posture,
            int targetSpend,
            bool voluntaryOffering, WarScoreRecord war)
        {
            Hero ruler = (drafter ?? winner)?.RulingClan?.Leader;

            if (posture == TreatyNegotiationPosture.TotalVictory
                && drafter != loser
                && TreatyRealmTransitionService.TryGetForceVassalizationCandidate(
                    winner, loser, out TreatyForceVassalizationCandidate force, out _)
                && force.WarScoreCost <= targetSpend)
            {
                float score = 65f
                    + (force.HasDeJureBasis ? 30f : 0f)
                    + (force.HasClaimBasis ? 20f : 0f)
                    + GetTrait(ruler, DefaultTraits.Calculating) * 10f
                    - GetTrait(ruler, DefaultTraits.Mercy) * 10f
                    + Math.Max(0f, -GetRulerRelation(winner, loser)) * 0.15f;
                if (score >= 100f)
                {
                    return new TreatyAiTermCandidate(
                        new TreatyTermRecord(
                            TreatyTermType.ForceVassalization,
                            force.WarScoreCost,
                            fromKingdomId: loser.StringId,
                            toKingdomId: winner.StringId,
                            titleId: force.DefeatedSovereignTitle.TitleId),
                        score,
                        force.HasDeJureBasis ? "restore de jure vassalage" : "enforce sovereign claim");
                }
            }

            return GetClientageSettlement(proposal, winner, loser, drafter, posture, targetSpend, voluntaryOffering, null,war);
        }

        internal static TreatyAiDraftResult BuildObjectiveDraft(WarScoreRecord war, TreatyProposalRecord proposal,
            Kingdom winner, Kingdom loser, Kingdom drafter, int desiredSpend, CourtTreatyDraftPreference preference)
        {
            if (war == null || proposal == null || winner == null || loser == null || drafter != winner
                || preference == null
                || preference.From != loser.StringId || preference.To != winner.StringId) return null;
            var posture = DeterminePosture(proposal, winner, loser, drafter);
            int spend = Math.Max(0, Math.Min(proposal.WarScoreBudget, desiredSpend));
            if (preference.Type == TreatyTermType.TransferFief && !string.IsNullOrEmpty(preference.SettlementId))
            {
                var core = new List<TreatyAiTermCandidate>();
                var optional = new List<TreatyAiTermCandidate>();
                BuildTerritorialCandidates(war, winner, loser, posture, false, core, optional);
                var claim = core.Concat(optional).FirstOrDefault(c => preference.Matches(c.Term) && c.Cost <= spend);
                if (claim == null) return null;
                var draft = BuildDraftCore(war, proposal, winner, loser, drafter, spend, true, claim);
                return preference.Score(draft.Terms) == 0 ? null : new TreatyAiDraftResult(draft.Terms, posture, spend,
                    draft.Summary + "; " + preference.Reason, isObjectiveAlternative: true);
            }
            if (preference.Type != TreatyTermType.MakeClientKingdom) return null;
            var candidate = GetClientageSettlement(proposal, winner, loser, drafter, posture, spend, false, preference,war);
            return candidate == null ? null : new TreatyAiDraftResult(new[] { candidate.Term }, posture, spend,
                $"{posture}; {candidate.Reason}; consideration_bonus={preference.ConsiderationBonus}", isObjectiveAlternative: true);
        }

        private static TreatyAiTermCandidate GetClientageSettlement(TreatyProposalRecord proposal,
            Kingdom winner, Kingdom loser, Kingdom drafter, TreatyNegotiationPosture posture, int targetSpend,
            bool voluntaryOffering, CourtTreatyDraftPreference preference, WarScoreRecord war)
        {
            Hero ruler = (drafter ?? winner)?.RulingClan?.Leader;
            bool mayOfferSubmission = drafter == loser
                && CalculateRealmWarWill(loser,war) <= 15f
                && (proposal?.WarScoreBudget ?? 0) >= 80;
            bool mayDemandSubmission = posture == TreatyNegotiationPosture.TotalVictory && drafter != loser;
            if ((mayOfferSubmission || mayDemandSubmission)
                && TreatyDraftService.TryGetClientKingdomCandidate(winner, loser, out TreatyClientKingdomCandidate client, out _)
                && client.WarScoreCost <= targetSpend)
            {
                float score = 45f
                    + GetTrait(ruler, DefaultTraits.Calculating) * 15f
                    + GetTrait(ruler, DefaultTraits.Valor) * 8f
                    - GetTrait(ruler, DefaultTraits.Mercy) * 12f
                    - GetTrait(ruler, DefaultTraits.Honor) * 5f
                    + Math.Max(0f, -GetRulerRelation(winner, loser)) * 0.25f;
                if (mayOfferSubmission)
                    score += 45f;
                var term = new TreatyTermRecord(TreatyTermType.MakeClientKingdom, client.WarScoreCost,
                    fromKingdomId: loser.StringId, toKingdomId: winner.StringId, wasVoluntaryOffering: voluntaryOffering);
                if (preference?.Matches(term) == true && drafter == winner)
                    score += preference.ConsiderationBonus;
                if (score >= 85f)
                {
                    return new TreatyAiTermCandidate(
                        term,
                        score,
                        preference?.Matches(term) == true ? preference.Reason
                            : mayOfferSubmission ? "offer clientage to preserve the defeated realm" : "impose client kingdom status");
                }
            }

            return null;
        }

        private static void BuildTerritorialCandidates(
            WarScoreRecord war,
            Kingdom winner,
            Kingdom loser,
            TreatyNegotiationPosture posture,
            bool voluntaryOffering,
            List<TreatyAiTermCandidate> core,
            List<TreatyAiTermCandidate> optional)
        {
            FeudalTitleBehavior titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            foreach (TreatyFiefTransferCandidate candidate in TreatyDraftService.GetAvailableFiefTransfers(war, loser, winner))
            {
                Settlement settlement = candidate.Settlement;
                WarScoreFiefSnapshotRecord snapshot = war.GetSnapshot(settlement.StringId);
                bool reclamation = snapshot?.OwnerKingdomId == winner.StringId && settlement.OwnerClan?.Kingdom == loser;
                bool border = IsBorderFief(titles, settlement, winner);
                float claimPressure = CalculateClaimPressure(titles, settlement, winner, out bool strongClaim, out bool weakClaim, out bool deJureRealm);
                bool coreObjective = candidate.IsOccupied || reclamation;
                bool optionalAllowed = posture == TreatyNegotiationPosture.TotalVictory
                    ? border || strongClaim || weakClaim || deJureRealm
                    : posture == TreatyNegotiationPosture.DecisiveVictory
                        && border && (strongClaim || weakClaim || deJureRealm);
                if (!coreObjective && !optionalAllowed)
                    continue;

                float priority = candidate.IsOccupied ? 105f : reclamation ? 115f : 20f;
                priority += settlement.IsTown ? 12f : 4f;
                priority += border ? 25f : 0f;
                priority += claimPressure;
                priority += deJureRealm ? 15f : 0f;
                priority -= candidate.WarScoreCost * (coreObjective ? 0.15f : 0.30f);
                string reason = reclamation ? $"recover {settlement.Name}"
                    : candidate.IsOccupied ? $"retain occupied {settlement.Name}"
                    : strongClaim ? $"enforce a strong claim to {settlement.Name}"
                    : weakClaim ? $"advance a weak claim to {settlement.Name}"
                    : deJureRealm ? $"recover de jure territory at {settlement.Name}"
                    : $"secure border territory at {settlement.Name}";
                TreatyTermRecord term = new TreatyTermRecord(
                    TreatyTermType.TransferFief,
                    candidate.WarScoreCost,
                    settlement.StringId,
                    fromKingdomId: loser.StringId,
                    toKingdomId: winner.StringId,
                    wasOccupiedAtDrafting: candidate.IsOccupied,
                    wasVoluntaryOffering: voluntaryOffering);
                TreatyAiTermCandidate scored = new TreatyAiTermCandidate(term, priority, reason);
                if (coreObjective)
                    core.Add(scored);
                else
                    optional.Add(scored);
            }
            foreach (var candidate in ClientWarTerritory.Candidates(war, loser, winner))
            {
                bool border = IsBorderFief(titles, candidate.Fief, candidate.Client);
                float claims = CalculateClaimPressure(titles, candidate.Fief, candidate.Client, out _, out _, out bool deJure);
                float priority = 85f + (candidate.Fief.IsTown ? 12f : 4f) + (border ? 25f : 0f)
                    + claims + (deJure ? 15f : 0f) - candidate.Cost * .15f;
                core.Add(new TreatyAiTermCandidate(candidate.Term(loser, winner, voluntaryOffering), priority,
                    $"retain {candidate.Fief.Name} for client {candidate.Client.Name}"));
            }
        }

        private static List<TreatyAiTermCandidate> BuildPrisonerCandidates(
            Kingdom winner,
            Kingdom loser,
            bool voluntaryOffering)
        {
            Hero heir = TreatyDraftReadScope.GetHeir(winner);
            return TreatyDraftService.GetAvailablePrisonerReleases(loser, winner)
                .Select(candidate =>
                {
                    float priority = candidate.Hero == winner.Leader ? 110f
                        : candidate.Hero == heir ? 90f
                        : candidate.Hero.Clan?.Leader == candidate.Hero ? 65f
                        : 35f;
                    priority += candidate.Hero.Clan?.CurrentTotalStrength / 250f ?? 0f;
                    return new TreatyAiTermCandidate(
                        new TreatyTermRecord(
                            TreatyTermType.ReleasePrisoner,
                            candidate.WarScoreCost,
                            fromKingdomId: loser.StringId,
                            toKingdomId: winner.StringId,
                            heroId: candidate.Hero.StringId,
                            wasVoluntaryOffering: voluntaryOffering),
                        priority - candidate.WarScoreCost * 0.2f,
                        $"release {candidate.Hero.Name}");
                })
                .ToList();
        }

        private static List<TreatyAiTermCandidate> BuildReciprocalPrisonerOfferingCandidates(
            Kingdom winner,
            Kingdom loser,
            bool reciprocalExchange)
        {
            Hero heir = TreatyDraftReadScope.GetHeir(loser);
            return TreatyDraftService.GetAvailablePrisonerReleases(winner, loser)
                .Select(candidate =>
                {
                    float priority = candidate.Hero == loser.Leader ? 120f
                        : candidate.Hero == heir ? 100f
                        : candidate.Hero.Clan?.Leader == candidate.Hero ? 75f
                        : 45f;
                    priority += candidate.Hero.Clan?.CurrentTotalStrength / 250f ?? 0f;
                    return new TreatyAiTermCandidate(
                        new TreatyTermRecord(
                            TreatyTermType.ReleasePrisoner,
                            candidate.WarScoreCost,
                            fromKingdomId: winner.StringId,
                            toKingdomId: loser.StringId,
                            heroId: candidate.Hero.StringId,
                            wasVoluntaryOffering: !reciprocalExchange),
                        priority,
                        $"return {candidate.Hero.Name} as a reciprocal concession");
                })
                .OrderByDescending(candidate => candidate.Priority)
                .ThenByDescending(candidate => candidate.Cost)
                .ToList();
        }

        private static List<TreatyAiTermCandidate> BuildPoliticalCandidates(
            Kingdom winner,
            Kingdom loser,
            Kingdom drafter,
            TreatyNegotiationPosture posture,
            bool voluntaryOffering,
            int budget,
            bool noCoreTerritoryFits, WarScoreRecord war)
        {
            List<TreatyAiTermCandidate> candidates = new List<TreatyAiTermCandidate>();
            Hero ruler = (drafter ?? winner)?.RulingClan?.Leader;
            int durationDays = BellumCivileOptions.HostagePactDurationDays;

            if (war?.ConflictType == WarScoreConflictType.ForeignWar)
                foreach (var hostage in TreatyHostageTerms.Available(loser, winner, durationDays))
                {
                    float priority = 28 + GetTrait(ruler, DefaultTraits.Calculating) * 6
                        + GetTrait(ruler, DefaultTraits.Honor) * 4 + GetTrait(ruler, DefaultTraits.Mercy) * 4
                        + (4 - hostage.Tier) * 2;
                    if (drafter == loser)
                        priority -= (float)HostagePactRules.GetHouseReluctance(hostage.Tier,
                            GetTrait(ruler, DefaultTraits.Mercy), ruler == null ? 0 : ruler.GetRelation(hostage.Hero)) * .5f;
                    if (priority >= 20)
                        candidates.Add(new TreatyAiTermCandidate(TreatyHostageTerms.Create(loser, winner, hostage, voluntaryOffering, durationDays),
                            priority, "secure the peace with a royal hostage"));
                }

            foreach (TreatyClaimRenunciationCandidate claim in TreatyDraftService.GetAvailableClaimRenunciations(loser, winner))
            {
                float priority = claim.Strength == FeudalClaimStrength.Strong ? 58f : 34f;
                priority += (int)claim.Title.TitleType * 7f;
                priority += claim.ClaimantClan.CurrentTotalStrength / 250f;
                priority += GetTrait(ruler, DefaultTraits.Calculating) * 6f + GetTrait(ruler, DefaultTraits.Honor) * 4f;
                candidates.Add(new TreatyAiTermCandidate(
                    new TreatyTermRecord(
                        TreatyTermType.RenounceClaim,
                        claim.WarScoreCost,
                        fromKingdomId: loser.StringId,
                        toKingdomId: winner.StringId,
                        clanId: claim.ClaimantClan.StringId,
                        titleId: claim.Title.TitleId,
                        wasVoluntaryOffering: voluntaryOffering),
                    priority - claim.WarScoreCost * 0.2f,
                    $"end {claim.ClaimantClan.Name}'s {claim.Strength.ToString().ToLowerInvariant()} claim to {claim.Title.Name}"));
            }

            bool allowStructuralPolitics = posture == TreatyNegotiationPosture.DecisiveVictory
                || posture == TreatyNegotiationPosture.TotalVictory;
            if (allowStructuralPolitics)
            {
                foreach (TreatyVassalReleaseCandidate release in TreatyRealmTransitionService.GetReleaseCandidates(loser))
                {
                    float priority = 35f
                        + GetTrait(ruler, DefaultTraits.Calculating) * 8f
                        + Math.Max(0f, GetLeaderRelation(winner, release.LeaderClan)) * 0.2f
                        - release.WarScoreCost * 0.15f;
                    if (release.LeaderClan.Culture == winner.Culture)
                        priority += 10f;
                    candidates.Add(new TreatyAiTermCandidate(
                        new TreatyTermRecord(
                            TreatyTermType.ReleaseVassal,
                            release.WarScoreCost,
                            fromKingdomId: loser.StringId,
                            toKingdomId: winner.StringId,
                            clanId: release.LeaderClan.StringId,
                            titleId: release.RootTitle.TitleId,
                            wasVoluntaryOffering: voluntaryOffering),
                        priority,
                        $"release {release.LeaderClan.Name} from the defeated realm"));
                }
            }

            if (allowStructuralPolitics || voluntaryOffering)
            {
                foreach (TreatyClientReleaseCandidate release in TreatyDraftService.GetAvailableClientReleases(loser, winner))
                {
                    float priority = 45f
                        + release.LibertyDesire * 0.35f
                        + GetTrait(ruler, DefaultTraits.Honor) * 8f
                        + GetTrait(ruler, DefaultTraits.Mercy) * 5f
                        + Math.Max(0f, GetRulerRelation(winner, release.ClientRealm)) * 0.2f
                        - release.WarScoreCost * 0.15f;
                    candidates.Add(new TreatyAiTermCandidate(
                        new TreatyTermRecord(
                            TreatyTermType.ReleaseClientState,
                            release.WarScoreCost,
                            fromKingdomId: loser.StringId,
                            toKingdomId: winner.StringId,
                            thirdKingdomId: release.ClientRealm.StringId,
                            wasVoluntaryOffering: voluntaryOffering),
                        priority,
                        $"release {release.ClientRealm.Name} from enemy clientage"));
                }
            }

            foreach (TreatyRebelDemandCandidate rebel in TreatyDraftService.GetAvailableRebelDemandEnforcements(loser, winner))
            {
                float priority = 55f
                    + Math.Max(0f, rebel.CivilWar.GetSelfRelativeScore(rebel.RebelRealm.StringId)) * 0.35f
                    + Math.Max(0f, GetLeaderRelation(winner, rebel.Faction.Leader)) * 0.25f
                    + GetTrait(ruler, DefaultTraits.Calculating) * 8f
                    + GetTrait(ruler, DefaultTraits.Mercy) * 4f
                    - rebel.WarScoreCost * 0.15f;
                candidates.Add(new TreatyAiTermCandidate(
                    new TreatyTermRecord(
                        TreatyTermType.EnforceRebelDemands,
                        rebel.WarScoreCost,
                        fromKingdomId: loser.StringId,
                        toKingdomId: winner.StringId,
                        thirdKingdomId: rebel.RebelRealm.StringId,
                        wasVoluntaryOffering: voluntaryOffering),
                    priority,
                    $"enforce the demands of {rebel.Faction.GetDisplayName()}"));
            }

            foreach (TreatyDiplomaticSeveranceCandidate severance in TreatyDraftService.GetAvailableDiplomaticSeverances(loser, winner))
            {
                float priority = ScoreDiplomaticSeverance(severance, winner, ruler);
                if (priority < 20f)
                    continue;
                candidates.Add(new TreatyAiTermCandidate(
                    new TreatyTermRecord(
                        severance.TermType,
                        severance.WarScoreCost,
                        fromKingdomId: loser.StringId,
                        toKingdomId: winner.StringId,
                        thirdKingdomId: severance.ThirdRealm.StringId,
                        wasVoluntaryOffering: voluntaryOffering),
                    priority,
                    severance.TermType == TreatyTermType.EndAlliance
                        ? $"break {loser.Name}'s alliance with {severance.ThirdRealm.Name}"
                        : $"end {loser.Name}'s trade agreement with {severance.ThirdRealm.Name}"));
            }

            if (!MarriageAllianceHelper.HasMarriageAlliance(winner.RulingClan, loser.RulingClan))
            {
                float dynasticNeed = BellumMarriageStrategyHelper.CalculateDynasticNeed(winner.RulingClan);
                float marriagePriority = 25f + dynasticNeed * 0.4f
                    + GetTrait(ruler, DefaultTraits.Calculating) * 8f
                    + GetTrait(ruler, DefaultTraits.Honor) * 7f
                    + GetTrait(ruler, DefaultTraits.Mercy) * 6f;
                TreatyRoyalMarriageCandidate marriage = marriagePriority >= 40f
                    ? TreatyRoyalMarriageService.GetCandidates(loser, winner).FirstOrDefault()
                    : null;
                if (marriage != null)
                {
                    candidates.Add(new TreatyAiTermCandidate(
                        new TreatyTermRecord(
                            TreatyTermType.ArrangeRoyalMarriage,
                            marriage.WarScoreCost,
                            fromKingdomId: loser.StringId,
                            toKingdomId: winner.StringId,
                            heroId: marriage.ConcedingSpouse.StringId,
                            secondaryHeroId: marriage.ReceivingSpouse.StringId,
                            wasVoluntaryOffering: voluntaryOffering),
                        marriagePriority,
                        "seal the settlement with a royal marriage"));
                }
            }

            TreatyAiTermCandidate prestige = BuildPrestigeCandidate(
                winner, loser, drafter, posture, voluntaryOffering, budget, noCoreTerritoryFits,war);
            if (prestige != null)
                candidates.Add(prestige);

            return candidates;
        }

        private static TreatyAiTermCandidate BuildPrestigeCandidate(
            Kingdom winner,
            Kingdom loser,
            Kingdom drafter,
            TreatyNegotiationPosture posture,
            bool voluntaryOffering,
            int budget,
            bool noCoreTerritoryFits, WarScoreRecord war)
        {
            Hero victor = winner?.RulingClan?.Leader;
            int relation = GetRulerRelation(winner, loser);
            int calculating = GetTrait(victor, DefaultTraits.Calculating);
            int honor = GetTrait(victor, DefaultTraits.Honor);
            int mercy = GetTrait(victor, DefaultTraits.Mercy);

            bool totalVictory = posture == TreatyNegotiationPosture.TotalVictory;
            bool decisiveVictory = posture == TreatyNegotiationPosture.DecisiveVictory || totalVictory;
            bool harshVictor = mercy < 0 || honor < 0;
            if (!voluntaryOffering && totalVictory && relation <= -10 && harshVictor)
            {
                float priority = 78f + Math.Max(0f, -relation) * 0.6f
                    + Math.Max(0, -mercy) * 12f + Math.Max(0, -honor) * 10f;
                return new TreatyAiTermCandidate(
                    new TreatyTermRecord(
                        TreatyTermType.HumiliateRuler,
                        C.TreatyHumiliateRulerCost,
                        fromKingdomId: loser.StringId,
                        toKingdomId: winner.StringId),
                    priority,
                    "humiliate a hated ruler after total victory");
            }

            bool wantsDiscredit = !voluntaryOffering && decisiveVictory
                && (relation < -20 || calculating > 0 || mercy < 0);
            if (wantsDiscredit)
            {
                float priority = 58f + Math.Max(0f, -relation) * 0.45f
                    + Math.Max(0, calculating) * 9f + Math.Max(0, -mercy) * 7f;
                return new TreatyAiTermCandidate(
                    new TreatyTermRecord(
                        TreatyTermType.DiscreditRuler,
                        C.TreatyDiscreditRulerCost,
                        fromKingdomId: loser.StringId,
                        toKingdomId: winner.StringId),
                    priority,
                    "discredit the defeated ruler after a decisive victory");
            }

            bool modestAdvantage = budget >= C.TreatyConcedeDefeatCost && budget <= 35;
            bool acceptsSymbolicSettlement = voluntaryOffering
                || CalculateRealmWarWill(drafter ?? winner,war) <= 35f
                || honor > 0
                || mercy > 0;
            if (modestAdvantage && noCoreTerritoryFits && acceptsSymbolicSettlement && relation >= -30)
            {
                float priority = 64f + Math.Max(0, honor) * 6f + Math.Max(0, mercy) * 6f;
                return new TreatyAiTermCandidate(
                    new TreatyTermRecord(
                        TreatyTermType.ConcedeDefeat,
                        C.TreatyConcedeDefeatCost,
                        fromKingdomId: loser.StringId,
                        toKingdomId: winner.StringId,
                        wasVoluntaryOffering: voluntaryOffering),
                    priority,
                    "secure a formal concession of defeat without harsher punishment");
            }

            return null;
        }

        private static void AddCandidates(
            IEnumerable<TreatyAiTermCandidate> candidates,
            List<TreatyTermRecord> terms,
            List<string> reasons,
            ref int spent,
            int targetSpend,
            int budget,
            HashSet<string> bundledPrisoners)
        {
            foreach (TreatyAiTermCandidate candidate in (candidates ?? Enumerable.Empty<TreatyAiTermCandidate>())
                .Where(candidate => candidate?.Term != null && candidate.Cost > 0)
                .OrderByDescending(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.Cost))
            {
                if (spent + candidate.Cost > targetSpend || spent + candidate.Cost > budget)
                    continue;
                if (!CanCombineWithSelectedTerms(candidate.Term, terms))
                    continue;

                terms.Add(candidate.Term);
                reasons.Add(candidate.Reason);
                spent += candidate.Cost;
                if (candidate.Term.Type == TreatyTermType.TransferFief)
                {
                    foreach (TreatyPrisonerReleaseCandidate prisoner in ResolveBundledPrisoners(candidate.Term))
                        bundledPrisoners.Add(prisoner.Hero.StringId);
                }
            }
        }

        private static void AddReciprocalPrisonerOfferings(
            List<TreatyTermRecord> terms,
            List<string> reasons,
            IEnumerable<TreatyAiTermCandidate> demandCandidates,
            Kingdom winner,
            Kingdom loser,
            int targetSpend,
            int budget,
            HashSet<string> bundledPrisoners,
            bool reciprocalExchangeOnly)
        {
            if (terms == null || reasons == null || winner == null || loser == null
                || targetSpend <= 0 || budget <= 0)
                return;

            TreatyWarScoreSummary startingAccounting = TreatyWarScoreAccounting.Calculate(
                terms,
                winner.StringId,
                budget);
            if (!reciprocalExchangeOnly
                && startingAccounting.OfferingCreditCap <= startingAccounting.AppliedOfferingCredit)
                return;

            List<TreatyAiTermCandidate> availableOfferings = BuildReciprocalPrisonerOfferingCandidates(
                    winner,
                    loser,
                    reciprocalExchangeOnly)
                .Where(candidate => candidate?.Term != null
                    && candidate.Cost > 0
                    && !bundledPrisoners.Contains(candidate.Term.HeroId ?? string.Empty)
                    && !ContainsEquivalentTerm(terms, candidate.Term))
                .ToList();
            if (availableOfferings.Count == 0)
                return;

            foreach (TreatyAiTermCandidate demand in (demandCandidates ?? Enumerable.Empty<TreatyAiTermCandidate>())
                .Where(candidate => candidate?.Term != null
                    && candidate.Cost > 0
                    && candidate.Priority >= MinimumReciprocalDemandPriority
                    && candidate.Term.ToKingdomId == winner.StringId)
                .Where(candidate => !reciprocalExchangeOnly
                    || TreatyWarScoreAccounting.IsReciprocalExchangeTerm(candidate.Term.Type))
                .OrderByDescending(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.Cost))
            {
                if (ContainsEquivalentTerm(terms, demand.Term)
                    || !CanCombineWithSelectedTerms(demand.Term, terms))
                    continue;

                List<TreatyTermRecord> trialTerms = new List<TreatyTermRecord>(terms) { demand.Term };
                List<TreatyAiTermCandidate> selectedOfferings = new List<TreatyAiTermCandidate>();
                TreatyWarScoreSummary trialAccounting = TreatyWarScoreAccounting.Calculate(
                    trialTerms,
                    winner.StringId,
                    budget);

                foreach (TreatyAiTermCandidate offering in availableOfferings)
                {
                    if (trialAccounting.UsedWarScore <= targetSpend)
                        break;

                    trialTerms.Add(offering.Term);
                    selectedOfferings.Add(offering);
                    trialAccounting = TreatyWarScoreAccounting.Calculate(
                        trialTerms,
                        winner.StringId,
                        budget);
                }

                if (trialAccounting.UsedWarScore > targetSpend || selectedOfferings.Count == 0)
                    continue;

                TreatyWarScoreSummary priorAccounting = TreatyWarScoreAccounting.Calculate(
                    terms,
                    winner.StringId,
                    budget);
                terms.Add(demand.Term);
                foreach (TreatyAiTermCandidate offering in selectedOfferings)
                    terms.Add(offering.Term);

                reasons.Add(demand.Reason);
                int addedCredit = Math.Max(0,
                    priorAccounting.UsedWarScore + demand.Cost - trialAccounting.UsedWarScore);
                reasons.Add(
                    $"returned {selectedOfferings.Count} enemy prisoner(s) for {addedCredit} exchange credit");

                if (demand.Term.Type == TreatyTermType.TransferFief)
                {
                    foreach (TreatyPrisonerReleaseCandidate prisoner in ResolveBundledPrisoners(demand.Term))
                        bundledPrisoners.Add(prisoner.Hero.StringId);
                }

                HashSet<string> selectedIds = new HashSet<string>(
                    selectedOfferings.Select(candidate => candidate.Term.HeroId ?? string.Empty));
                availableOfferings.RemoveAll(candidate => selectedIds.Contains(candidate.Term.HeroId ?? string.Empty));
                if (availableOfferings.Count == 0
                    || (!reciprocalExchangeOnly
                        && trialAccounting.AppliedOfferingCredit >= trialAccounting.OfferingCreditCap))
                    break;
            }
        }

        private static bool ContainsEquivalentTerm(
            IEnumerable<TreatyTermRecord> terms,
            TreatyTermRecord candidate)
        {
            if (candidate == null)
                return false;

            return (terms ?? Enumerable.Empty<TreatyTermRecord>()).Any(term => term != null
                && term.Type == candidate.Type
                && term.FromKingdomId == candidate.FromKingdomId
                && term.ToKingdomId == candidate.ToKingdomId
                && term.SettlementId == candidate.SettlementId
                && term.HeroId == candidate.HeroId
                && term.ClanId == candidate.ClanId
                && term.TitleId == candidate.TitleId
                && term.SecondaryHeroId == candidate.SecondaryHeroId
                && term.ThirdKingdomId == candidate.ThirdKingdomId);
        }

        private static bool CanCombineWithSelectedTerms(TreatyTermRecord candidate, IEnumerable<TreatyTermRecord> selected)
        {
            if (candidate == null)
                return false;

            List<TreatyTermRecord> existing = (selected ?? Enumerable.Empty<TreatyTermRecord>())
                .Where(term => term != null)
                .ToList();
            bool candidatePrestige = IsPrestigeTerm(candidate.Type);
            var combined = existing.Concat(new[] { candidate }).ToList();
            var hostageTerm = combined.FirstOrDefault(t => t.Type == TreatyTermType.HostagePeace);
            if (hostageTerm != null)
            {
                string winner = hostageTerm.WasVoluntaryOffering ? hostageTerm.FromKingdomId : hostageTerm.ToKingdomId;
                string loser = hostageTerm.WasVoluntaryOffering ? hostageTerm.ToKingdomId : hostageTerm.FromKingdomId;
                // Offers from the losing drafter also carry the voluntary flag. Either
                // orientation may fit here; final normalization enforces actual war roles.
                if (!TreatyHostageTerms.ValidShape(combined, winner, loser, out _)
                    && !TreatyHostageTerms.ValidShape(combined, loser, winner, out _)) return false;
            }
            if (ClientWarTerritory.IsTerritorial(candidate.Type) && existing.Any(t =>
                ClientWarTerritory.IsTerritorial(t.Type) && t.SettlementId == candidate.SettlementId
                || t.Type == TreatyTermType.ForceVassalization)) return false;
            if (candidate.Type == TreatyTermType.ForceVassalization && existing.Any(t => ClientWarTerritory.IsTerritorial(t.Type))) return false;
            if (candidate.Type == TreatyTermType.RecognizeClientOccupation && existing.Any(t =>
                t.Type == TreatyTermType.ReleaseClientState && t.ThirdKingdomId == candidate.ThirdKingdomId)) return false;
            if (candidate.Type == TreatyTermType.ReleaseClientState && existing.Any(t =>
                t.Type == TreatyTermType.RecognizeClientOccupation && t.ThirdKingdomId == candidate.ThirdKingdomId)) return false;
            if (candidatePrestige && existing.Any(term => IsPrestigeTerm(term.Type)))
                return false;

            bool candidateConcession = candidate.Type == TreatyTermType.ConcedeDefeat;
            bool candidateStructural = IsStructuralCapitulation(candidate.Type);
            if (candidateConcession && existing.Any(term => IsStructuralCapitulation(term.Type)))
                return false;
            if (candidateStructural && existing.Any(term => term.Type == TreatyTermType.ConcedeDefeat))
                return false;
            return true;
        }

        private static bool IsPrestigeTerm(TreatyTermType type)
        {
            return type == TreatyTermType.ConcedeDefeat
                || type == TreatyTermType.DiscreditRuler
                || type == TreatyTermType.HumiliateRuler;
        }

        private static bool IsStructuralCapitulation(TreatyTermType type)
        {
            return type == TreatyTermType.ForceVassalization
                || type == TreatyTermType.MakeClientKingdom
                || type == TreatyTermType.EnforceRebelDemands;
        }

        private static void AddWealthTerms(
            List<TreatyTermRecord> terms,
            List<string> reasons,
            Kingdom winner,
            Kingdom loser,
            Kingdom drafter,
            int targetSpend,
            int budget,
            ref int spent,
            bool voluntaryOffering)
        {
            int remaining = Math.Max(0, Math.Min(targetSpend, budget) - spent);
            if (remaining <= 0)
                return;

            long liquidWealth = TreatyDraftService.GetRemainingFinancialGold(loser, terms);
            int reparationsRate = BellumCivileOptions.TreatyReparationsGoldPerWarScore;
            long tributeRate = (long)BellumCivileOptions.TreatyDailyTributePerWarScore * C.TreatyTributeDurationDays;
            bool prefersTribute = GetTrait((drafter ?? winner)?.RulingClan?.Leader, DefaultTraits.Calculating) > 0;
            if (prefersTribute || reparationsRate <= 0 || (tributeRate > 0 && tributeRate < reparationsRate))
            {
                int tributeScore = GetAffordableTributeScore(liquidWealth, remaining, reparationsRate, tributeRate);
                if (tributeScore > 0)
                {
                    terms.Add(new TreatyTermRecord(
                        TreatyTermType.Tribute,
                        tributeScore,
                        dailyGold: TreatyTermCostModel.GetDailyTributeForWarScore(tributeScore),
                        durationDays: C.TreatyTributeDurationDays,
                        fromKingdomId: loser.StringId,
                        toKingdomId: winner.StringId,
                        wasVoluntaryOffering: voluntaryOffering));
                    reasons.Add("secure continuing tribute");
                    long gold = (long)TreatyTermCostModel.GetDailyTributeForWarScore(tributeScore) * C.TreatyTributeDurationDays;
                    spent += tributeScore;
                    remaining -= tributeScore;
                    liquidWealth = Math.Max(0, liquidWealth - gold);
                }
            }

            int reparationsScore = reparationsRate <= 0
                ? 0
                : (int)Math.Min(remaining, liquidWealth / reparationsRate);
            if (reparationsScore <= 0)
                return;

            terms.Add(new TreatyTermRecord(
                TreatyTermType.Reparations,
                reparationsScore,
                goldAmount: TreatyTermCostModel.GetReparationsForWarScore(reparationsScore),
                fromKingdomId: loser.StringId,
                toKingdomId: winner.StringId,
                wasVoluntaryOffering: voluntaryOffering));
            reasons.Add("extract immediate reparations");
            spent += reparationsScore;
        }

        internal static int GetAffordableTributeScore(long liquidWealth, int remaining,
            long reparationsRate, long tributeRate)
        {
            if (tributeRate <= 0)
                return 0;
            int attainable = TreatyDraftService.GetAffordableFinancialDemandValue(
                liquidWealth, remaining, reparationsRate, tributeRate);
            if (reparationsRate <= 0 || tributeRate <= reparationsRate)
                return attainable;

            // Prefer tribute only to the extent that its extra burden does not
            // sacrifice leverage that could instead be secured as reparations.
            long surplus = Math.Max(0L, liquidWealth) - attainable * reparationsRate;
            return (int)Math.Min(attainable, surplus / (tributeRate - reparationsRate));
        }

        private static IReadOnlyList<TreatyPrisonerReleaseCandidate> ResolveBundledPrisoners(TreatyTermRecord term)
        {
            Kingdom from = Kingdom.All.FirstOrDefault(kingdom => kingdom?.StringId == term?.FromKingdomId);
            Kingdom to = Kingdom.All.FirstOrDefault(kingdom => kingdom?.StringId == term?.ToKingdomId);
            if (from == null || to == null || string.IsNullOrWhiteSpace(term?.SettlementId))
                return new List<TreatyPrisonerReleaseCandidate>();
            return TreatyDraftService.GetAvailablePrisonerReleases(from, to)
                .Where(candidate => candidate.HoldingSettlement?.StringId == term.SettlementId)
                .ToList();
        }

        private static float CalculateClaimPressure(
            FeudalTitleBehavior titles,
            Settlement settlement,
            Kingdom winner,
            out bool strongClaim,
            out bool weakClaim,
            out bool deJureRealm)
        {
            strongClaim = false;
            weakClaim = false;
            deJureRealm = false;
            if (titles == null || settlement == null || winner?.Clans == null
                || !titles.TryGetBarony(settlement, out FeudalTitleRecord barony))
                return 0f;

            List<FeudalTitleRecord> chain = new List<FeudalTitleRecord>();
            HashSet<string> visited = new HashSet<string>();
            FeudalTitleRecord current = barony;
            while (current != null && visited.Add(current.TitleId))
            {
                chain.Add(current);
                current = titles.GetParentTitle(current, FeudalHierarchyMode.DeJure);
            }

            FeudalTitleRecord sovereign = titles.GetRealmSovereignTitle(winner, FeudalHierarchyMode.DeJure)
                ?? titles.GetKingdomPoliticalTitle(winner);
            deJureRealm = sovereign != null && chain.Any(title => title.TitleId == sovereign.TitleId);
            float pressure = 0f;
            foreach (Clan clan in winner.Clans.Where(IsEligibleClan))
            {
                float clanPressure = 0f;
                foreach (FeudalTitleRecord title in chain)
                {
                    if (titles.HasActiveClaim(clan, title, FeudalClaimStrength.Strong))
                    {
                        strongClaim = true;
                        clanPressure = Math.Max(clanPressure, 42f + (int)title.TitleType * 6f);
                    }
                    else if (titles.HasActiveClaim(clan, title, FeudalClaimStrength.Weak))
                    {
                        weakClaim = true;
                        clanPressure = Math.Max(clanPressure, 22f + (int)title.TitleType * 4f);
                    }

                    if (title.DeJureHolderClanId == clan.StringId)
                        clanPressure = Math.Max(clanPressure, 20f + (int)title.TitleType * 4f);
                }

                if (clanPressure > 0f)
                    pressure = Math.Max(pressure, clanPressure + Math.Min(15f, clan.CurrentTotalStrength / 250f));
            }

            return Math.Min(80f, pressure);
        }

        private static bool IsBorderFief(FeudalTitleBehavior titles, Settlement target, Kingdom recipient)
        {
            if (titles == null || target == null || recipient == null || !titles.TryGetBarony(target, out FeudalTitleRecord targetTitle))
                return false;

            foreach (Settlement settlement in Settlement.All.Where(settlement => settlement != null
                && settlement != target
                && (settlement.IsTown || settlement.IsCastle)
                && settlement.OwnerClan?.Kingdom == recipient))
            {
                if (titles.TryGetBarony(settlement, out FeudalTitleRecord heldTitle)
                    && titles.AreTitlesAdjacentForTitleLogic(targetTitle, heldTitle))
                    return true;
            }

            return false;
        }

        private static float ScoreDiplomaticSeverance(
            TreatyDiplomaticSeveranceCandidate candidate,
            Kingdom beneficiary,
            Hero ruler)
        {
            if (candidate?.ThirdRealm == null || beneficiary == null)
                return float.MinValue;

            bool alliance = candidate.TermType == TreatyTermType.EndAlliance;
            float score = alliance ? 28f : 8f;
            if (beneficiary.IsAtWarWith(candidate.ThirdRealm))
                score += alliance ? 45f : 20f;
            int relation = beneficiary.RulingClan?.Leader?.GetRelation(candidate.ThirdRealm.RulingClan?.Leader) ?? 0;
            score += Math.Max(-20f, Math.Min(30f, -relation * 0.5f));
            score += GetTrait(ruler, DefaultTraits.Calculating) * (alliance ? 6f : 10f);
            score += alliance ? GetTrait(ruler, DefaultTraits.Valor) * 5f : 0f;
            return score - candidate.WarScoreCost * 0.15f;
        }

        private static float CalculateRealmWarWill(Kingdom kingdom, WarScoreRecord war)
        {
            WarPeaceRevampBehavior behavior = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            if (behavior == null || kingdom?.Clans == null)
                return 50f;

            float weighted = 0f;
            float totalWeight = 0f;
            foreach (Clan clan in kingdom.Clans.Where(IsEligibleClan))
            {
                float weight = Math.Max(1f, clan.CurrentTotalStrength);
                weighted += CourtAgendaBehavior.EffectiveWarWill(clan,war,behavior.GetWarWill(clan)) * weight;
                totalWeight += weight;
            }
            return totalWeight > 0f ? weighted / totalWeight : 50f;
        }

        private static bool IsEligibleClan(Clan clan)
        {
            return clan != null && !clan.IsEliminated && !clan.IsUnderMercenaryService
                && (!clan.IsMinorFaction || clan == Clan.PlayerClan) && clan.Leader != null;
        }

        private static int GetTrait(Hero hero, TraitObject trait)
        {
            return hero?.GetTraitLevel(trait) ?? 0;
        }

        private static int GetRulerRelation(Kingdom first, Kingdom second)
        {
            return first?.RulingClan?.Leader?.GetRelation(second?.RulingClan?.Leader) ?? 0;
        }

        private static int GetLeaderRelation(Kingdom realm, Clan clan)
        {
            return realm?.RulingClan?.Leader?.GetRelation(clan?.Leader) ?? 0;
        }
    }
}
