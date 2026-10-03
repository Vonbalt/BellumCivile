using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal sealed class TreatyFiefTransferCandidate
    {
        public Settlement Settlement { get; }
        public bool IsOccupied { get; }
        public int LandWarScoreCost { get; }
        public IReadOnlyList<TreatyPrisonerReleaseCandidate> IncludedPrisoners { get; }
        public int PrisonerWarScoreCost => IncludedPrisoners.Sum(candidate => candidate.WarScoreCost);
        public int WarScoreCost { get; }

        public TreatyFiefTransferCandidate(
            Settlement settlement,
            bool isOccupied,
            int landWarScoreCost,
            IReadOnlyList<TreatyPrisonerReleaseCandidate> includedPrisoners)
        {
            Settlement = settlement;
            IsOccupied = isOccupied;
            LandWarScoreCost = landWarScoreCost;
            IncludedPrisoners = includedPrisoners ?? new List<TreatyPrisonerReleaseCandidate>();
            WarScoreCost = LandWarScoreCost + PrisonerWarScoreCost;
        }
    }

    internal sealed class TreatyPrisonerReleaseCandidate
    {
        public Hero Hero { get; }
        public Settlement HoldingSettlement { get; }
        public int WarScoreCost { get; }

        public TreatyPrisonerReleaseCandidate(Hero hero, Settlement holdingSettlement, int warScoreCost)
        {
            Hero = hero;
            HoldingSettlement = holdingSettlement;
            WarScoreCost = warScoreCost;
        }
    }

    internal sealed class TreatyClaimRenunciationCandidate
    {
        public Clan ClaimantClan { get; }
        public FeudalTitleRecord Title { get; }
        public FeudalClaimStrength Strength { get; }
        public int WarScoreCost { get; }

        public TreatyClaimRenunciationCandidate(Clan claimantClan, FeudalTitleRecord title, FeudalClaimStrength strength, int warScoreCost)
        {
            ClaimantClan = claimantClan;
            Title = title;
            Strength = strength;
            WarScoreCost = warScoreCost;
        }
    }

    internal sealed class TreatyDiplomaticSeveranceCandidate
    {
        public TreatyTermType TermType { get; }
        public Kingdom SourceRealm { get; }
        public Kingdom ThirdRealm { get; }
        public int WarScoreCost { get; }

        public TreatyDiplomaticSeveranceCandidate(
            TreatyTermType termType,
            Kingdom sourceRealm,
            Kingdom thirdRealm,
            int warScoreCost)
        {
            TermType = termType;
            SourceRealm = sourceRealm;
            ThirdRealm = thirdRealm;
            WarScoreCost = warScoreCost;
        }
    }

    internal sealed class TreatyClientKingdomCandidate
    {
        public Kingdom ClientRealm { get; }
        public Kingdom SuzerainRealm { get; }
        public int FiefCount { get; }
        public int WarScoreCost { get; }

        public TreatyClientKingdomCandidate(Kingdom clientRealm, Kingdom suzerainRealm, int fiefCount, int warScoreCost)
        {
            ClientRealm = clientRealm;
            SuzerainRealm = suzerainRealm;
            FiefCount = fiefCount;
            WarScoreCost = warScoreCost;
        }
    }

    internal sealed class TreatyClientReleaseCandidate
    {
        public Kingdom ClientRealm { get; }
        public Kingdom SuzerainRealm { get; }
        public int WarScoreCost { get; }
        public float LibertyDesire { get; }

        public TreatyClientReleaseCandidate(Kingdom clientRealm, Kingdom suzerainRealm, int warScoreCost, float libertyDesire)
        {
            ClientRealm = clientRealm;
            SuzerainRealm = suzerainRealm;
            WarScoreCost = warScoreCost;
            LibertyDesire = libertyDesire;
        }
    }

    internal sealed class TreatyRebelDemandCandidate
    {
        public FactionObject Faction { get; }
        public Kingdom RebelRealm { get; }
        public Kingdom ParentRealm { get; }
        public WarScoreRecord CivilWar { get; }
        public int WarScoreCost { get; }

        public TreatyRebelDemandCandidate(FactionObject faction, Kingdom rebelRealm, Kingdom parentRealm,
            WarScoreRecord civilWar, int warScoreCost)
        {
            Faction = faction;
            RebelRealm = rebelRealm;
            ParentRealm = parentRealm;
            CivilWar = civilWar;
            WarScoreCost = warScoreCost;
        }
    }

    internal static class TreatyDraftService
    {
        public static IReadOnlyList<TreatyClientReleaseCandidate> GetAvailableClientReleases(
            Kingdom sourceRealm,
            Kingdom beneficiaryRealm)
        {
            ClientKingdomBehavior clients = ClientKingdomBehavior.Instance;
            if (clients == null || !IsValidPermanentRealm(sourceRealm) || !IsValidPermanentRealm(beneficiaryRealm)
                || sourceRealm == beneficiaryRealm)
                return new List<TreatyClientReleaseCandidate>();

            return clients.GetClients(sourceRealm)
                .Where(IsValidPermanentRealm)
                .Select(client => new TreatyClientReleaseCandidate(
                    client,
                    sourceRealm,
                    TreatyTermCostModel.GetClientReleaseCost(client),
                    clients.BuildLibertyAssessment(client)?.RealmLibertyDesire ?? 0f))
                .OrderByDescending(candidate => candidate.LibertyDesire)
                .ThenBy(candidate => candidate.ClientRealm.Name?.ToString())
                .ToList();
        }

        public static IReadOnlyList<TreatyRebelDemandCandidate> GetAvailableRebelDemandEnforcements(
            Kingdom sourceRealm,
            Kingdom beneficiaryRealm)
        {
            FactionManagerBehavior factions = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            WarScoreBehavior warScores = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>();
            if (factions == null || warScores == null || !IsValidPermanentRealm(sourceRealm)
                || !IsValidPermanentRealm(beneficiaryRealm) || sourceRealm == beneficiaryRealm)
                return new List<TreatyRebelDemandCandidate>();

            return factions.GetFactionsInKingdom(sourceRealm)
                .Where(faction => faction != null && !faction.IsIdeology)
                .Select(faction => new { Faction = faction, Rebel = faction.GetRebelKingdom() })
                .Where(entry => entry.Rebel != null && !entry.Rebel.IsEliminated
                    && entry.Rebel.IsAtWarWith(sourceRealm))
                .Select(entry => new
                {
                    entry.Faction,
                    entry.Rebel,
                    War = warScores.GetActiveWar(entry.Rebel, sourceRealm)
                })
                .Where(entry => entry.War?.ConflictType == WarScoreConflictType.CivilWar)
                .Select(entry => new TreatyRebelDemandCandidate(
                    entry.Faction,
                    entry.Rebel,
                    sourceRealm,
                    entry.War,
                    TreatyTermCostModel.GetEnforceRebelDemandsCost(entry.Faction, entry.Rebel, entry.War)))
                .OrderBy(candidate => candidate.WarScoreCost)
                .ThenBy(candidate => candidate.Faction.GetDisplayName().ToString())
                .ToList();
        }

        public static IReadOnlyList<TreatyDiplomaticSeveranceCandidate> GetAvailableDiplomaticSeverances(
            Kingdom sourceRealm,
            Kingdom beneficiaryRealm)
        {
            if (!IsValidPermanentRealm(sourceRealm) || !IsValidPermanentRealm(beneficiaryRealm) || sourceRealm == beneficiaryRealm)
                return new List<TreatyDiplomaticSeveranceCandidate>();

            List<TreatyDiplomaticSeveranceCandidate> candidates = new List<TreatyDiplomaticSeveranceCandidate>();
            ITradeAgreementsCampaignBehavior trade = Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
            IAllianceCampaignBehavior alliances = Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();

            foreach (Kingdom thirdRealm in Kingdom.All.Where(IsValidPermanentRealm)
                .Where(kingdom => kingdom != sourceRealm && kingdom != beneficiaryRealm))
            {
                if (ClientKingdomBehavior.Instance?.IsProtectedClientPair(sourceRealm, thirdRealm) == true)
                    continue;

                if (trade?.HasTradeAgreement(sourceRealm, thirdRealm, out _) == true)
                {
                    candidates.Add(new TreatyDiplomaticSeveranceCandidate(
                        TreatyTermType.EndTradeAgreement,
                        sourceRealm,
                        thirdRealm,
                        C.TreatyEndTradeAgreementCost));
                }

                if (alliances?.IsAllyWithKingdom(sourceRealm, thirdRealm) == true)
                {
                    candidates.Add(new TreatyDiplomaticSeveranceCandidate(
                        TreatyTermType.EndAlliance,
                        sourceRealm,
                        thirdRealm,
                        C.TreatyEndAllianceCost));
                }
            }

            return candidates
                .OrderByDescending(candidate => candidate.TermType == TreatyTermType.EndAlliance)
                .ThenBy(candidate => candidate.ThirdRealm.Name?.ToString())
                .ToList();
        }

        public static IReadOnlyList<TreatyFiefTransferCandidate> GetAvailableFiefTransfers(
            WarScoreRecord war,
            Kingdom fromKingdom,
            Kingdom toKingdom)
        {
            return TreatyDraftReadScope.Read(war, fromKingdom, toKingdom,
                () => BuildAvailableFiefTransfers(war, fromKingdom, toKingdom));
        }

        private static IReadOnlyList<TreatyFiefTransferCandidate> BuildAvailableFiefTransfers(
            WarScoreRecord war, Kingdom fromKingdom, Kingdom toKingdom)
        {
            if (war?.FiefSnapshots == null || fromKingdom == null || toKingdom == null)
                return new List<TreatyFiefTransferCandidate>();

            var territory = new ClientWarTerritory(war);
            Dictionary<string, WarScoreFiefSnapshotRecord> snapshots = war.FiefSnapshots
                .Where(snapshot => snapshot != null && !string.IsNullOrWhiteSpace(snapshot.SettlementId))
                .GroupBy(snapshot => snapshot.SettlementId)
                .ToDictionary(group => group.Key, group => group.First());

            Dictionary<string, List<TreatyPrisonerReleaseCandidate>> prisonersByDungeon = GetAvailablePrisonerReleases(fromKingdom, toKingdom)
                .Where(candidate => RetainedTreatyPrisonerBehavior.IsHeldInSettlementDungeon(candidate.Hero, candidate.HoldingSettlement))
                .GroupBy(candidate => candidate.HoldingSettlement.StringId)
                .ToDictionary(group => group.Key, group => group.ToList());

            return Settlement.All
                .Where(settlement => settlement != null && (settlement.IsTown || settlement.IsCastle))
                .Select(settlement =>
                {
                    snapshots.TryGetValue(settlement.StringId, out WarScoreFiefSnapshotRecord snapshot);
                    Kingdom current = settlement.OwnerClan?.Kingdom;
                    bool fromSide = snapshot != null && territory.Side(snapshot.OwnerKingdomId) != 0
                        && territory.Side(snapshot.OwnerKingdomId) == territory.Side(fromKingdom.StringId);
                    bool belongedToSource = fromSide || current == fromKingdom;
                    bool occupiedByRecipient = fromSide && current == toKingdom;
                    return new { Settlement = settlement, BelongedToSource = belongedToSource, Occupied = occupiedByRecipient, Current = current };
                })
                .Where(entry => entry.BelongedToSource && (entry.Current == fromKingdom || entry.Current == toKingdom))
                .Select(entry =>
                {
                    prisonersByDungeon.TryGetValue(entry.Settlement.StringId, out List<TreatyPrisonerReleaseCandidate> includedPrisoners);
                    return new TreatyFiefTransferCandidate(
                        entry.Settlement,
                        entry.Occupied,
                        TreatyTermCostModel.GetFiefTransferCost(entry.Settlement, toKingdom, entry.Occupied),
                        includedPrisoners);
                })
                .Distinct()
                .OrderByDescending(candidate => candidate.IsOccupied)
                .ThenByDescending(candidate => candidate.Settlement.IsTown)
                .ThenBy(candidate => candidate.Settlement.Name?.ToString())
                .ToList();
        }

        public static IReadOnlyList<Settlement> GetAvailableOccupiedFiefs(WarScoreRecord war, Kingdom winner, Kingdom loser)
        {
            return GetAvailableFiefTransfers(war, loser, winner)
                .Where(candidate => candidate.IsOccupied)
                .Select(candidate => candidate.Settlement)
                .ToList();
        }

        public static IReadOnlyList<TreatyPrisonerReleaseCandidate> GetAvailablePrisonerReleases(
            Kingdom captorKingdom,
            Kingdom captiveKingdom)
        {
            return TreatyDraftReadScope.Read(captorKingdom, captiveKingdom, null,
                () => BuildAvailablePrisonerReleases(captorKingdom, captiveKingdom));
        }

        private static IReadOnlyList<TreatyPrisonerReleaseCandidate> BuildAvailablePrisonerReleases(
            Kingdom captorKingdom, Kingdom captiveKingdom)
        {
            if (captorKingdom == null || captiveKingdom == null)
                return new List<TreatyPrisonerReleaseCandidate>();

            Hero recognizedHeir = TreatyDraftReadScope.GetHeir(captiveKingdom);
            return Hero.AllAliveHeroes
                .Where(hero => hero != null
                    && hero != Hero.MainHero
                    && hero.IsPrisoner
                    && hero.Clan?.Kingdom == captiveKingdom
                    && RetainedTreatyPrisonerBehavior.GetCaptorKingdom(hero) == captorKingdom)
                .Select(hero => new TreatyPrisonerReleaseCandidate(
                    hero,
                    RetainedTreatyPrisonerBehavior.GetHoldingSettlement(hero),
                    TreatyTermCostModel.GetPrisonerReleaseCost(hero, recognizedHeir)))
                .OrderByDescending(candidate => candidate.Hero == captiveKingdom.Leader)
                .ThenByDescending(candidate => candidate.Hero == recognizedHeir)
                .ThenByDescending(candidate => candidate.Hero.Clan?.Leader == candidate.Hero)
                .ThenByDescending(candidate => candidate.WarScoreCost)
                .ThenBy(candidate => candidate.Hero.Name?.ToString())
                .ToList();
        }

        public static IReadOnlyList<TreatyClaimRenunciationCandidate> GetAvailableClaimRenunciations(
            Kingdom claimantKingdom,
            Kingdom protectedKingdom)
        {
            FeudalTitleBehavior titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalTitleRecord protectedRoot = titles?.GetKingdomPoliticalTitle(protectedKingdom);
            if (titles == null || claimantKingdom == null || protectedKingdom == null || protectedRoot == null)
                return new List<TreatyClaimRenunciationCandidate>();

            return claimantKingdom.Clans
                .Where(clan => clan != null && !clan.IsEliminated && !clan.IsUnderMercenaryService)
                .SelectMany(clan => titles.GetActiveClaimsByClan(clan)
                    .Where(claim => claim != null)
                    .GroupBy(claim => claim.TargetTitleId)
                    .Select(group => new
                    {
                        Clan = clan,
                        Claim = group.OrderByDescending(claim => claim.Strength).First(),
                        Title = titles.GetTitle(group.Key)
                    }))
                .Where(entry => entry.Title != null
                    && entry.Title.IsActive
                    && entry.Title.DeFactoHolderClanId != entry.Clan.StringId
                    && IsDeJureDescendantOf(titles, entry.Title, protectedRoot))
                .Select(entry => new TreatyClaimRenunciationCandidate(
                    entry.Clan,
                    entry.Title,
                    entry.Claim.Strength,
                    TreatyPoliticalCostModel.GetClaimRenunciationCost(entry.Title, entry.Claim.Strength)))
                .OrderByDescending(candidate => candidate.Strength)
                .ThenByDescending(candidate => candidate.Title.TitleType)
                .ThenBy(candidate => candidate.ClaimantClan.Name?.ToString())
                .ThenBy(candidate => candidate.Title.Name)
                .ToList();
        }

        public static bool TryGetClientKingdomCandidate(
            Kingdom suzerainRealm,
            Kingdom clientRealm,
            out TreatyClientKingdomCandidate candidate,
            out string reason,
            IEnumerable<TreatyTermRecord> projectedTerms = null)
        {
            candidate = null;
            reason = "the client-kingdom system is unavailable";
            ClientKingdomBehavior clients = ClientKingdomBehavior.Instance;
            if (clients == null || !clients.CanEstablishClientKingdom(clientRealm, suzerainRealm, out reason))
                return false;

            int fiefCount = Settlement.All.Count(settlement => settlement != null
                && (settlement.IsTown || settlement.IsCastle)
                && settlement.OwnerClan?.Kingdom == clientRealm);
            int cost = TreatyTermCostModel.GetProjectedRealmStructuralCost(clientRealm, projectedTerms);
            candidate = new TreatyClientKingdomCandidate(clientRealm, suzerainRealm, fiefCount, cost);
            reason = "client kingdom status is available";
            return true;
        }

        public static bool TryValidateAndNormalize(
            WarScoreRecord war,
            TreatyProposalRecord proposal,
            Kingdom winner,
            Kingdom loser,
            IEnumerable<TreatyTermRecord> requestedTerms,
            out List<TreatyTermRecord> normalized,
            out string report,
            bool repairStateDrift = false,
            bool allowOverBudget = false)
        {
            normalized = new List<TreatyTermRecord>();
            report = "invalid treaty draft";
            if (war == null || proposal == null || winner == null || loser == null)
                return false;

            bool repaired = false;

            List<TreatyTermRecord> requested = requestedTerms?.Where(term => term != null).ToList()
                ?? new List<TreatyTermRecord>();
            if (requested.Count == 0)
            {
                normalized.Add(new TreatyTermRecord(TreatyTermType.WhitePeace, 0));
                report = "white peace draft";
                return true;
            }

            int whitePeaceCount = requested.Count(term => term.Type == TreatyTermType.WhitePeace);
            if (!TreatyHostageTerms.ValidShape(requested, winner.StringId, loser.StringId, out string hostageReason))
            { report = hostageReason; return false; }
            if (whitePeaceCount > 0)
            {
                if (whitePeaceCount != 1 || requested.Count != 1)
                {
                    report = "white peace cannot be combined with other demands";
                    return false;
                }

                normalized.Add(new TreatyTermRecord(TreatyTermType.WhitePeace, 0));
                report = "white peace draft";
                return true;
            }

            bool IsUnsupportedReverseDemand(TreatyTermRecord term)
            {
                return term != null
                    && term.FromKingdomId == winner.StringId
                    && term.ToKingdomId == loser.StringId
                    && !term.WasVoluntaryOffering
                    && !TreatyWarScoreAccounting.IsReciprocalExchangeTerm(term.Type);
            }

            if (requested.Any(IsUnsupportedReverseDemand))
            {
                if (!repairStateDrift)
                {
                    report = "the losing realm may request only reciprocal prisoner or wealth exchanges";
                    return false;
                }

                requested.RemoveAll(term => IsUnsupportedReverseDemand(term));
                repaired = true;
            }

            int prestigePunishmentCount = requested.Count(term =>
                term.Type == TreatyTermType.ConcedeDefeat
                || term.Type == TreatyTermType.DiscreditRuler
                || term.Type == TreatyTermType.HumiliateRuler);
            if (prestigePunishmentCount > 1)
            {
                report = "a treaty may contain only one prestige concession or ruler rebuke";
                return false;
            }

            int forceVassalizationCount = requested.Count(term => term.Type == TreatyTermType.ForceVassalization);
            if (forceVassalizationCount > 1
                || (forceVassalizationCount == 1 && requested.Any(term => ClientWarTerritory.IsTerritorial(term.Type) || term.Type == TreatyTermType.ReleaseVassal)))
            {
                report = "force vassalization cannot be combined with territorial transfers or released vassals";
                return false;
            }
            int clientKingdomCount = requested.Count(term => term.Type == TreatyTermType.MakeClientKingdom);
            if (clientKingdomCount > 1 || (clientKingdomCount == 1 && forceVassalizationCount == 1))
            {
                report = "a treaty may establish only one client kingdom and cannot combine clientage with annexation";
                return false;
            }
            int enforceRebelCount = requested.Count(term => term.Type == TreatyTermType.EnforceRebelDemands);
            if (enforceRebelCount > 1
                || (enforceRebelCount == 1 && (forceVassalizationCount > 0 || clientKingdomCount > 0)))
            {
                report = "enforced rebel demands cannot be combined with annexation or clientage of the parent realm";
                return false;
            }
            int concedeDefeatCount = requested.Count(term => term.Type == TreatyTermType.ConcedeDefeat);
            if (concedeDefeatCount > 0
                && (forceVassalizationCount > 0 || clientKingdomCount > 0 || enforceRebelCount > 0))
            {
                report = "conceding defeat cannot be combined with annexation, clientage, or enforced rebel capitulation";
                return false;
            }
            if (requested.Count(term => term.Type == TreatyTermType.ArrangeRoyalMarriage) > 1)
            {
                report = "a treaty may contain only one arranged royal marriage";
                return false;
            }

            Dictionary<string, TreatyFiefTransferCandidate> availableWinnerDemands = GetAvailableFiefTransfers(war, loser, winner)
                .ToDictionary(candidate => candidate.Settlement.StringId);
            Dictionary<string, TreatyFiefTransferCandidate> availableWinnerOfferings = GetAvailableFiefTransfers(war, winner, loser)
                .ToDictionary(candidate => candidate.Settlement.StringId);
            var clientDemands = ClientWarTerritory.Candidates(war, loser, winner);
            var clientOfferings = ClientWarTerritory.Candidates(war, winner, loser);
            HashSet<string> bundledPrisonerIds = new HashSet<string>(requested
                .Where(term => term.Type == TreatyTermType.TransferFief)
                .SelectMany(term =>
                {
                    bool winnerDemand = term.FromKingdomId == loser.StringId && term.ToKingdomId == winner.StringId;
                    bool winnerOffering = term.FromKingdomId == winner.StringId && term.ToKingdomId == loser.StringId;
                    TreatyFiefTransferCandidate candidate = null;
                    if (winnerDemand)
                        availableWinnerDemands.TryGetValue(term.SettlementId ?? string.Empty, out candidate);
                    else if (winnerOffering)
                        availableWinnerOfferings.TryGetValue(term.SettlementId ?? string.Empty, out candidate);
                    return candidate?.IncludedPrisoners.Select(prisoner => prisoner.Hero.StringId)
                        ?? Enumerable.Empty<string>();
                }));
            HashSet<string> usedFiefs = new HashSet<string>();
            HashSet<string> usedPrisoners = new HashSet<string>();
            HashSet<string> usedClaims = new HashSet<string>();
            HashSet<string> usedRulerRebukes = new HashSet<string>();
            HashSet<string> usedReleasedVassals = new HashSet<string>();
            HashSet<string> usedDiplomaticSeverances = new HashSet<string>();
            HashSet<string> usedClientReleases = new HashSet<string>();
            HashSet<string> usedFinancialTerms = new HashSet<string>();
            Dictionary<string, long> financialBurden = new Dictionary<string, long>();

            foreach (TreatyTermRecord term in requested)
            {
                switch (term.Type)
                {
                    case TreatyTermType.HostagePeace:
                    {
                        Kingdom supplier = ResolveKingdom(term.FromKingdomId);
                        Kingdom receiver = ResolveKingdom(term.ToKingdomId);
                        if (war.ConflictType != WarScoreConflictType.ForeignWar)
                        { report = "hostage peace is only available in foreign wars"; return false; }
                        if (!TreatyHostageTerms.CheckDeliverable(term, supplier, receiver, out report)) return false;
                        if (TreatyHostageTerms.SettlementHolding(receiver.RulingClan, war, requested) == null)
                        { report = "the treaty leaves no suitable holding in the receiving ruling house's possession"; return false; }
                        normalized.Add(term);
                        break;
                    }
                    case TreatyTermType.RecognizeClientOccupation:
                    {
                        if (!IsOpposingDirection(term, winner, loser) || string.IsNullOrEmpty(term.SettlementId)
                            || !usedFiefs.Add(term.SettlementId)
                            || requested.Any(t => t.Type == TreatyTermType.ReleaseClientState && t.ThirdKingdomId == term.ThirdKingdomId))
                        {
                            report = "the draft contains an invalid or duplicate territorial transfer";
                            return false;
                        }
                        var candidates = term.ToKingdomId == winner.StringId ? clientDemands : clientOfferings;
                        var clientCandidate = candidates.FirstOrDefault(c => c.Matches(term));
                        if (clientCandidate == null)
                        {
                            if (repairStateDrift) { repaired = true; continue; }
                            report = "the draft contains an invalid or duplicate territorial transfer";
                            return false;
                        }
                        normalized.Add(clientCandidate.Term(ResolveKingdom(term.FromKingdomId), ResolveKingdom(term.ToKingdomId), term.WasVoluntaryOffering));
                        break;
                    }
                    case TreatyTermType.TransferFief:
                    {
                        bool winnerDemand = term.FromKingdomId == loser.StringId && term.ToKingdomId == winner.StringId;
                        bool winnerOffering = term.FromKingdomId == winner.StringId && term.ToKingdomId == loser.StringId;
                        TreatyFiefTransferCandidate candidate = null;
                        if (winnerDemand)
                            availableWinnerDemands.TryGetValue(term.SettlementId ?? string.Empty, out candidate);
                        else if (winnerOffering)
                            availableWinnerOfferings.TryGetValue(term.SettlementId ?? string.Empty, out candidate);
                        if (!IsOpposingDirection(term, winner, loser)
                            || string.IsNullOrWhiteSpace(term.SettlementId)
                            || !usedFiefs.Add(term.SettlementId))
                        {
                            report = "the draft contains an invalid or duplicate territorial transfer";
                            return false;
                        }
                        if (candidate == null)
                        {
                            if (repairStateDrift)
                            {
                                repaired = true;
                                continue;
                            }

                            report = "the draft contains an invalid or duplicate territorial transfer";
                            return false;
                        }

                        normalized.Add(new TreatyTermRecord(
                            TreatyTermType.TransferFief,
                            candidate.WarScoreCost,
                            candidate.Settlement.StringId,
                            fromKingdomId: term.FromKingdomId,
                            toKingdomId: term.ToKingdomId,
                            wasOccupiedAtDrafting: candidate.IsOccupied,
                            wasVoluntaryOffering: term.WasVoluntaryOffering));
                        break;
                    }
                    case TreatyTermType.Reparations:
                    {
                        if (!IsOpposingDirection(term, winner, loser) || term.WarScoreCost <= 0
                            || !usedFinancialTerms.Add(term.Type + "|" + term.FromKingdomId + "|" + term.ToKingdomId))
                        {
                            report = "the draft contains invalid or duplicate reparations";
                            return false;
                        }

                        int cost = term.WarScoreCost;
                        Kingdom payer = ResolveKingdom(term.FromKingdomId);
                        int availableGold = CalculateCollectiveAvailableGold(payer);
                        int gold = TreatyTermCostModel.GetReparationsForWarScore(cost);
                        if (gold <= 0)
                        {
                            repaired = true;
                            continue;
                        }
                        long alreadyOwed = financialBurden.TryGetValue(term.FromKingdomId, out long prior) ? prior : 0L;
                        if (alreadyOwed + gold > availableGold)
                        {
                            if (repairStateDrift)
                            {
                                repaired = true;
                                continue;
                            }

                            report = "the losing realm cannot finance those reparations";
                            return false;
                        }

                        normalized.Add(new TreatyTermRecord(TreatyTermType.Reparations, cost, goldAmount: gold,
                            fromKingdomId: term.FromKingdomId,
                            toKingdomId: term.ToKingdomId,
                            wasVoluntaryOffering: term.WasVoluntaryOffering));
                        financialBurden[term.FromKingdomId] = alreadyOwed + gold;
                        break;
                    }
                    case TreatyTermType.Tribute:
                    {
                        if (!IsOpposingDirection(term, winner, loser) || term.WarScoreCost <= 0
                            || !usedFinancialTerms.Add(term.Type + "|" + term.FromKingdomId + "|" + term.ToKingdomId))
                        {
                            report = "the draft contains invalid or duplicate tribute";
                            return false;
                        }

                        Kingdom payer = ResolveKingdom(term.FromKingdomId);
                        int availableGold = CalculateCollectiveAvailableGold(payer);
                        int cost = term.WarScoreCost;
                        int dailyGold = TreatyTermCostModel.GetDailyTributeForWarScore(cost);
                        if (dailyGold <= 0)
                        {
                            repaired = true;
                            continue;
                        }
                        long tributeBurden = (long)dailyGold * C.TreatyTributeDurationDays;
                        long alreadyOwed = financialBurden.TryGetValue(term.FromKingdomId, out long prior) ? prior : 0L;
                        if (alreadyOwed + tributeBurden > availableGold)
                        {
                            if (repairStateDrift)
                            {
                                repaired = true;
                                continue;
                            }

                            report = "the losing realm cannot finance that tribute";
                            return false;
                        }

                        normalized.Add(new TreatyTermRecord(
                            TreatyTermType.Tribute,
                            cost,
                            dailyGold: dailyGold,
                            durationDays: C.TreatyTributeDurationDays,
                            fromKingdomId: term.FromKingdomId,
                            toKingdomId: term.ToKingdomId,
                            wasVoluntaryOffering: term.WasVoluntaryOffering));
                        financialBurden[term.FromKingdomId] = alreadyOwed + tributeBurden;
                        break;
                    }
                    case TreatyTermType.ReleasePrisoner:
                    {
                        // A ceded dungeon releases the receiving realm's nobles as part of the fief package.
                        if (bundledPrisonerIds.Contains(term.HeroId ?? string.Empty))
                            break;

                        if (!IsOpposingDirection(term, winner, loser)
                            || string.IsNullOrWhiteSpace(term.HeroId)
                            || !usedPrisoners.Add(term.HeroId))
                        {
                            report = "the draft contains an invalid or duplicate prisoner release";
                            return false;
                        }

                        Kingdom captor = ResolveKingdom(term.FromKingdomId);
                        Kingdom captiveRealm = ResolveKingdom(term.ToKingdomId);
                        TreatyPrisonerReleaseCandidate candidate = GetAvailablePrisonerReleases(captor, captiveRealm)
                            .FirstOrDefault(entry => entry.Hero.StringId == term.HeroId);
                        if (candidate == null)
                        {
                            if (repairStateDrift)
                            {
                                repaired = true;
                                continue;
                            }

                            report = "a prisoner named in the draft is no longer held by the releasing realm";
                            return false;
                        }

                        normalized.Add(new TreatyTermRecord(
                            TreatyTermType.ReleasePrisoner,
                            candidate.WarScoreCost,
                            fromKingdomId: term.FromKingdomId,
                            toKingdomId: term.ToKingdomId,
                            heroId: candidate.Hero.StringId,
                            wasVoluntaryOffering: term.WasVoluntaryOffering));
                        break;
                    }
                    case TreatyTermType.RenounceClaim:
                    {
                        if (!IsOpposingDirection(term, winner, loser)
                            || string.IsNullOrWhiteSpace(term.ClanId)
                            || string.IsNullOrWhiteSpace(term.TitleId))
                        {
                            report = "the draft contains an invalid claim renunciation";
                            return false;
                        }

                        string key = term.ClanId + "|" + term.TitleId;
                        if (!usedClaims.Add(key))
                        {
                            report = "the draft contains a duplicate claim renunciation";
                            return false;
                        }

                        Kingdom claimantRealm = ResolveKingdom(term.FromKingdomId);
                        Kingdom protectedRealm = ResolveKingdom(term.ToKingdomId);
                        TreatyClaimRenunciationCandidate candidate = GetAvailableClaimRenunciations(claimantRealm, protectedRealm)
                            .FirstOrDefault(entry => entry.ClaimantClan.StringId == term.ClanId
                                && entry.Title.TitleId == term.TitleId);
                        if (candidate == null)
                        {
                            if (repairStateDrift)
                            {
                                repaired = true;
                                continue;
                            }

                            report = "a claim named in the draft is no longer active or applicable";
                            return false;
                        }

                        normalized.Add(new TreatyTermRecord(
                            TreatyTermType.RenounceClaim,
                            candidate.WarScoreCost,
                            fromKingdomId: term.FromKingdomId,
                            toKingdomId: term.ToKingdomId,
                            clanId: candidate.ClaimantClan.StringId,
                            titleId: candidate.Title.TitleId,
                            wasVoluntaryOffering: term.WasVoluntaryOffering));
                        break;
                    }
                    case TreatyTermType.DiscreditRuler:
                    case TreatyTermType.HumiliateRuler:
                    {
                        if (!IsOpposingDirection(term, winner, loser)
                            || !usedRulerRebukes.Add(term.FromKingdomId ?? string.Empty))
                        {
                            report = "the draft contains an invalid or duplicate ruler rebuke";
                            return false;
                        }

                        Kingdom targetRealm = ResolveKingdom(term.FromKingdomId);
                        Kingdom imposingRealm = ResolveKingdom(term.ToKingdomId);
                        if (targetRealm?.RulingClan?.Leader == null || imposingRealm?.RulingClan?.Leader == null)
                        {
                            if (repairStateDrift)
                            {
                                repaired = true;
                                continue;
                            }

                            report = "a ruler named in the political demand is no longer valid";
                            return false;
                        }

                        int cost = term.Type == TreatyTermType.HumiliateRuler
                            ? C.TreatyHumiliateRulerCost
                            : C.TreatyDiscreditRulerCost;
                        normalized.Add(new TreatyTermRecord(
                            term.Type,
                            cost,
                            fromKingdomId: targetRealm.StringId,
                            toKingdomId: imposingRealm.StringId,
                            wasVoluntaryOffering: term.WasVoluntaryOffering));
                        break;
                    }
                    case TreatyTermType.ConcedeDefeat:
                    {
                        if (term.FromKingdomId != loser.StringId || term.ToKingdomId != winner.StringId)
                        {
                            report = "only the realm currently losing the war may concede defeat";
                            return false;
                        }

                        normalized.Add(new TreatyTermRecord(
                            TreatyTermType.ConcedeDefeat,
                            C.TreatyConcedeDefeatCost,
                            fromKingdomId: loser.StringId,
                            toKingdomId: winner.StringId,
                            wasVoluntaryOffering: term.WasVoluntaryOffering));
                        break;
                    }
                    case TreatyTermType.ReleaseVassal:
                    {
                        if (!IsOpposingDirection(term, winner, loser)
                            || string.IsNullOrWhiteSpace(term.ClanId)
                            || string.IsNullOrWhiteSpace(term.TitleId)
                            || !usedReleasedVassals.Add(term.ClanId + "|" + term.TitleId))
                        {
                            report = "the draft contains an invalid or duplicate vassal release";
                            return false;
                        }

                        Kingdom sourceRealm = ResolveKingdom(term.FromKingdomId);
                        TreatyVassalReleaseCandidate candidate = TreatyRealmTransitionService.GetReleaseCandidates(sourceRealm)
                            .FirstOrDefault(entry => entry.LeaderClan.StringId == term.ClanId
                                && entry.RootTitle.TitleId == term.TitleId);
                        if (candidate == null)
                        {
                            if (repairStateDrift)
                            {
                                repaired = true;
                                continue;
                            }

                            report = "the named vassal no longer forms a releasable non-de-jure title cluster";
                            return false;
                        }

                        HashSet<string> releasedSettlements = new HashSet<string>(Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>()
                            .GetTitleAndDescendants(candidate.RootTitle, FeudalHierarchyMode.DeFacto)
                            .Where(title => title?.TitleType == FeudalTitleType.Barony && !string.IsNullOrWhiteSpace(title.CapitalSettlementId))
                            .Select(title => title.CapitalSettlementId));
                        if (requested.Any(other => ClientWarTerritory.IsTerritorial(other.Type) && releasedSettlements.Contains(other.SettlementId)))
                        {
                            report = "a released vassal cluster cannot also contain a transferred fief";
                            return false;
                        }

                        normalized.Add(new TreatyTermRecord(TreatyTermType.ReleaseVassal, candidate.WarScoreCost,
                            fromKingdomId: term.FromKingdomId,
                            toKingdomId: term.ToKingdomId,
                            clanId: candidate.LeaderClan.StringId,
                            titleId: candidate.RootTitle.TitleId,
                            wasVoluntaryOffering: term.WasVoluntaryOffering));
                        break;
                    }
                    case TreatyTermType.ForceVassalization:
                    {
                        if (!IsOpposingDirection(term, winner, loser))
                        {
                            report = "the draft contains an invalid force-vassalization direction";
                            return false;
                        }

                        Kingdom defeatedRealm = ResolveKingdom(term.FromKingdomId);
                        Kingdom victorRealm = ResolveKingdom(term.ToKingdomId);
                        if (!TreatyRealmTransitionService.TryGetForceVassalizationCandidate(victorRealm, defeatedRealm,
                            out TreatyForceVassalizationCandidate candidate, out string eligibilityReason, requested))
                        {
                            if (repairStateDrift)
                            {
                                repaired = true;
                                continue;
                            }

                            report = eligibilityReason;
                            return false;
                        }

                        normalized.Add(new TreatyTermRecord(TreatyTermType.ForceVassalization, candidate.WarScoreCost,
                            fromKingdomId: defeatedRealm.StringId,
                            toKingdomId: victorRealm.StringId,
                            titleId: candidate.DefeatedSovereignTitle.TitleId,
                            wasVoluntaryOffering: term.WasVoluntaryOffering));
                        break;
                    }
                    case TreatyTermType.MakeClientKingdom:
                    {
                        if (!IsOpposingDirection(term, winner, loser))
                        {
                            report = "the draft contains an invalid client-kingdom direction";
                            return false;
                        }

                        Kingdom clientRealm = ResolveKingdom(term.FromKingdomId);
                        Kingdom suzerainRealm = ResolveKingdom(term.ToKingdomId);
                        if (!TryGetClientKingdomCandidate(suzerainRealm, clientRealm,
                            out TreatyClientKingdomCandidate candidate, out string eligibilityReason, requested))
                        {
                            if (repairStateDrift)
                            {
                                repaired = true;
                                continue;
                            }

                            report = eligibilityReason;
                            return false;
                        }

                        normalized.Add(new TreatyTermRecord(
                            TreatyTermType.MakeClientKingdom,
                            candidate.WarScoreCost,
                            fromKingdomId: clientRealm.StringId,
                            toKingdomId: suzerainRealm.StringId,
                            wasVoluntaryOffering: term.WasVoluntaryOffering));
                        break;
                    }
                    case TreatyTermType.ReleaseClientState:
                    {
                        if (!IsOpposingDirection(term, winner, loser)
                            || string.IsNullOrWhiteSpace(term.ThirdKingdomId)
                            || !usedClientReleases.Add(term.FromKingdomId + "|" + term.ThirdKingdomId))
                        {
                            report = "the draft contains an invalid or duplicate client release";
                            return false;
                        }

                        Kingdom sourceRealm = ResolveKingdom(term.FromKingdomId);
                        Kingdom beneficiaryRealm = ResolveKingdom(term.ToKingdomId);
                        TreatyClientReleaseCandidate candidate = GetAvailableClientReleases(sourceRealm, beneficiaryRealm)
                            .FirstOrDefault(entry => entry.ClientRealm.StringId == term.ThirdKingdomId);
                        if (candidate == null)
                        {
                            if (repairStateDrift)
                            {
                                repaired = true;
                                continue;
                            }
                            report = "the named realm is no longer a client of the conceding realm";
                            return false;
                        }

                        normalized.Add(new TreatyTermRecord(
                            TreatyTermType.ReleaseClientState,
                            candidate.WarScoreCost,
                            fromKingdomId: sourceRealm.StringId,
                            toKingdomId: beneficiaryRealm.StringId,
                            thirdKingdomId: candidate.ClientRealm.StringId,
                            wasVoluntaryOffering: term.WasVoluntaryOffering));
                        break;
                    }
                    case TreatyTermType.EnforceRebelDemands:
                    {
                        if (term.FromKingdomId != loser.StringId || term.ToKingdomId != winner.StringId
                            || string.IsNullOrWhiteSpace(term.ThirdKingdomId))
                        {
                            report = "the draft contains an invalid enforced-rebellion direction";
                            return false;
                        }

                        TreatyRebelDemandCandidate candidate = GetAvailableRebelDemandEnforcements(loser, winner)
                            .FirstOrDefault(entry => entry.RebelRealm.StringId == term.ThirdKingdomId);
                        if (candidate == null)
                        {
                            if (repairStateDrift)
                            {
                                repaired = true;
                                continue;
                            }
                            report = "the rebellion named in the treaty is no longer active";
                            return false;
                        }

                        normalized.Add(new TreatyTermRecord(
                            TreatyTermType.EnforceRebelDemands,
                            candidate.WarScoreCost,
                            fromKingdomId: loser.StringId,
                            toKingdomId: winner.StringId,
                            thirdKingdomId: candidate.RebelRealm.StringId,
                            wasVoluntaryOffering: term.WasVoluntaryOffering));
                        break;
                    }
                    case TreatyTermType.ArrangeRoyalMarriage:
                    {
                        if (!IsOpposingDirection(term, winner, loser)
                            || string.IsNullOrWhiteSpace(term.HeroId)
                            || string.IsNullOrWhiteSpace(term.SecondaryHeroId))
                        {
                            report = "the draft contains an invalid royal marriage";
                            return false;
                        }

                        Kingdom concedingRealm = ResolveKingdom(term.FromKingdomId);
                        Kingdom receivingRealm = ResolveKingdom(term.ToKingdomId);
                        if (!TreatyRoyalMarriageService.TryResolveCandidate(concedingRealm, receivingRealm,
                            term.HeroId, term.SecondaryHeroId, out TreatyRoyalMarriageCandidate candidate))
                        {
                            if (repairStateDrift)
                            {
                                repaired = true;
                                continue;
                            }

                            report = "one of the selected royal spouses is no longer eligible";
                            return false;
                        }

                        normalized.Add(new TreatyTermRecord(TreatyTermType.ArrangeRoyalMarriage,
                            C.TreatyRoyalMarriageCost,
                            fromKingdomId: concedingRealm.StringId,
                            toKingdomId: receivingRealm.StringId,
                            heroId: candidate.ConcedingSpouse.StringId,
                            secondaryHeroId: candidate.ReceivingSpouse.StringId,
                            wasVoluntaryOffering: term.WasVoluntaryOffering));
                        break;
                    }
                    case TreatyTermType.EndTradeAgreement:
                    case TreatyTermType.EndAlliance:
                    {
                        if (!IsOpposingDirection(term, winner, loser)
                            || string.IsNullOrWhiteSpace(term.ThirdKingdomId))
                        {
                            report = "the draft contains an invalid diplomatic severance";
                            return false;
                        }

                        string severanceKey = term.Type + "|" + term.FromKingdomId + "|" + term.ThirdKingdomId;
                        if (!usedDiplomaticSeverances.Add(severanceKey))
                        {
                            report = "the draft contains a duplicate diplomatic severance";
                            return false;
                        }

                        Kingdom sourceRealm = ResolveKingdom(term.FromKingdomId);
                        Kingdom beneficiaryRealm = ResolveKingdom(term.ToKingdomId);
                        TreatyDiplomaticSeveranceCandidate candidate = GetAvailableDiplomaticSeverances(sourceRealm, beneficiaryRealm)
                            .FirstOrDefault(entry => entry.TermType == term.Type
                                && entry.ThirdRealm.StringId == term.ThirdKingdomId);
                        if (candidate == null)
                        {
                            // Agreements may expire or be broken while a player leaves a parley open.
                            // Dropping that stale clause is safer than invalidating every other term.
                            repaired = true;
                            continue;
                        }

                        normalized.Add(new TreatyTermRecord(
                            candidate.TermType,
                            candidate.WarScoreCost,
                            fromKingdomId: sourceRealm.StringId,
                            toKingdomId: beneficiaryRealm.StringId,
                            thirdKingdomId: candidate.ThirdRealm.StringId,
                            wasVoluntaryOffering: term.WasVoluntaryOffering));
                        break;
                    }
                    default:
                        report = "the draft contains a treaty term that is not implemented";
                        return false;
                }
            }

            TreatyWarScoreSummary accounting = TreatyWarScoreAccounting.Calculate(
                normalized,
                winner.StringId,
                proposal.WarScoreBudget);
            if (accounting.UnmatchedReciprocalExchangeValue > 0)
            {
                if (!repairStateDrift)
                {
                    report = "reciprocal prisoner and wealth requests must be matched by equivalent exchanges";
                    return false;
                }

                RemoveUnmatchedReciprocalExchanges(normalized, winner.StringId, proposal.WarScoreBudget);
                repaired = true;
                accounting = TreatyWarScoreAccounting.Calculate(
                    normalized,
                    winner.StringId,
                    proposal.WarScoreBudget);
            }

            if (normalized.Count == 0)
            {
                normalized.Add(new TreatyTermRecord(TreatyTermType.WhitePeace, 0));
                repaired = repaired || requested.Count > 0;
            }

            accounting = TreatyWarScoreAccounting.Calculate(
                normalized,
                winner.StringId,
                proposal.WarScoreBudget);
            if (accounting.UnmatchedReciprocalExchangeValue > 0)
            {
                report = "reciprocal prisoner and wealth requests must be matched by equivalent exchanges";
                return false;
            }
            if (!allowOverBudget && accounting.UsedWarScore > proposal.WarScoreBudget)
            {
                report = "the draft exceeds the available war score";
                return false;
            }
            report = repaired ? "stale treaty terms repaired" : "treaty draft validated";
            return true;
        }

        public static bool HasActionableWinnerDemand(
            WarScoreRecord war,
            TreatyProposalRecord proposal,
            Kingdom winner,
            Kingdom loser,
            int availableWarScore)
            => GetActionableWinnerDemandCap(war, proposal, winner, loser, availableWarScore) > 0;

        // This is an upper bound, not a package optimizer: retain the existing
        // nonfinancial availability policy, but never inflate financial-only leverage.
        public static int GetActionableWinnerDemandCap(
            WarScoreRecord war,
            TreatyProposalRecord proposal,
            Kingdom winner,
            Kingdom loser,
            int availableWarScore)
        {
            if (war == null || proposal == null || winner == null || loser == null || availableWarScore <= 0)
                return 0;
            if (HasActionableNonfinancialWinnerDemand(war, proposal, winner, loser, availableWarScore))
                return availableWarScore;

            long remainingGold = GetRemainingFinancialGold(loser, proposal.Terms);
            return GetAffordableFinancialDemandValue(remainingGold, availableWarScore,
                BellumCivileOptions.TreatyReparationsGoldPerWarScore,
                (long)BellumCivileOptions.TreatyDailyTributePerWarScore * C.TreatyTributeDurationDays);
        }

        internal static long GetRemainingFinancialGold(Kingdom payer, IEnumerable<TreatyTermRecord> terms)
        {
            long committedBurden = (terms ?? Enumerable.Empty<TreatyTermRecord>())
                .Where(term => term != null && term.FromKingdomId == payer?.StringId
                    && (term.Type == TreatyTermType.Reparations || term.Type == TreatyTermType.Tribute))
                .Sum(term => (long)Math.Max(0, term.GoldAmount)
                    + (long)Math.Max(0, term.DailyGold) * Math.Max(0, term.DurationDays));
            return Math.Max(0L, (long)CalculateCollectiveAvailableGold(payer) - committedBurden);
        }

        internal static int GetAffordableFinancialDemandValue(long remainingGold, int availableWarScore,
            long reparationsRate, long tributeRate)
        {
            long cheapestRate = reparationsRate > 0 && tributeRate > 0 ? Math.Min(reparationsRate, tributeRate)
                : reparationsRate > 0 ? reparationsRate : tributeRate;
            return cheapestRate <= 0 ? 0
                : (int)Math.Min(Math.Max(0, availableWarScore), Math.Max(0L, remainingGold) / cheapestRate);
        }

        private static bool HasActionableNonfinancialWinnerDemand(
            WarScoreRecord war,
            TreatyProposalRecord proposal,
            Kingdom winner,
            Kingdom loser,
            int availableWarScore)
        {
            if (war == null || proposal == null || winner == null || loser == null || availableWarScore <= 0)
                return false;

            HashSet<string> selectedFiefs = new HashSet<string>((proposal.Terms ?? Enumerable.Empty<TreatyTermRecord>())
                .Where(term => term != null && ClientWarTerritory.IsTerritorial(term.Type) && term.ToKingdomId == winner.StringId)
                .Select(term => term.SettlementId ?? string.Empty));
            if (GetAvailableFiefTransfers(war, loser, winner)
                .Any(candidate => candidate != null
                    && candidate.WarScoreCost <= availableWarScore
                    && !selectedFiefs.Contains(candidate.Settlement.StringId)))
                return true;

            if (ClientWarTerritory.Candidates(war, loser, winner).Any(c => c.Cost <= availableWarScore && !selectedFiefs.Contains(c.Fief.StringId)))
                return true;
            HashSet<string> selectedPrisoners = new HashSet<string>((proposal.Terms ?? Enumerable.Empty<TreatyTermRecord>())
                .Where(term => term?.Type == TreatyTermType.ReleasePrisoner && term.ToKingdomId == winner.StringId)
                .Select(term => term.HeroId ?? string.Empty));
            if (GetAvailablePrisonerReleases(loser, winner)
                .Any(candidate => candidate != null
                    && candidate.WarScoreCost <= availableWarScore
                    && !selectedPrisoners.Contains(candidate.Hero.StringId)))
                return true;

            HashSet<string> selectedClaims = new HashSet<string>((proposal.Terms ?? Enumerable.Empty<TreatyTermRecord>())
                .Where(term => term?.Type == TreatyTermType.RenounceClaim && term.ToKingdomId == winner.StringId)
                .Select(term => (term.ClanId ?? string.Empty) + "|" + (term.TitleId ?? string.Empty)));
            if (GetAvailableClaimRenunciations(loser, winner)
                .Any(candidate => candidate != null
                    && candidate.WarScoreCost <= availableWarScore
                    && !selectedClaims.Contains(candidate.ClaimantClan.StringId + "|" + candidate.Title.TitleId)))
                return true;

            HashSet<string> selectedVassalReleases = new HashSet<string>((proposal.Terms ?? Enumerable.Empty<TreatyTermRecord>())
                .Where(term => term?.Type == TreatyTermType.ReleaseVassal && term.ToKingdomId == winner.StringId)
                .Select(term => (term.ClanId ?? string.Empty) + "|" + (term.TitleId ?? string.Empty)));
            if (TreatyRealmTransitionService.GetReleaseCandidates(loser)
                .Any(candidate => candidate != null
                    && candidate.WarScoreCost <= availableWarScore
                    && !selectedVassalReleases.Contains(candidate.LeaderClan.StringId + "|" + candidate.RootTitle.TitleId)))
                return true;

            HashSet<string> selectedClientReleases = new HashSet<string>((proposal.Terms ?? Enumerable.Empty<TreatyTermRecord>())
                .Where(term => term?.Type == TreatyTermType.ReleaseClientState && term.ToKingdomId == winner.StringId)
                .Select(term => term.ThirdKingdomId ?? string.Empty));
            if (GetAvailableClientReleases(loser, winner)
                .Any(candidate => candidate != null
                    && candidate.WarScoreCost <= availableWarScore
                    && !selectedClientReleases.Contains(candidate.ClientRealm.StringId)))
                return true;

            HashSet<string> selectedRebelDemands = new HashSet<string>((proposal.Terms ?? Enumerable.Empty<TreatyTermRecord>())
                .Where(term => term?.Type == TreatyTermType.EnforceRebelDemands && term.ToKingdomId == winner.StringId)
                .Select(term => term.ThirdKingdomId ?? string.Empty));
            if (GetAvailableRebelDemandEnforcements(loser, winner)
                .Any(candidate => candidate != null
                    && candidate.WarScoreCost <= availableWarScore
                    && !selectedRebelDemands.Contains(candidate.RebelRealm.StringId)))
                return true;

            HashSet<string> selectedSeverances = new HashSet<string>((proposal.Terms ?? Enumerable.Empty<TreatyTermRecord>())
                .Where(term => term != null
                    && (term.Type == TreatyTermType.EndTradeAgreement || term.Type == TreatyTermType.EndAlliance)
                    && term.ToKingdomId == winner.StringId)
                .Select(term => term.Type + "|" + (term.FromKingdomId ?? string.Empty) + "|" + (term.ThirdKingdomId ?? string.Empty)));
            if (GetAvailableDiplomaticSeverances(loser, winner)
                .Any(candidate => candidate != null
                    && candidate.WarScoreCost <= availableWarScore
                    && !selectedSeverances.Contains(candidate.TermType + "|" + loser.StringId + "|" + candidate.ThirdRealm.StringId)))
                return true;

            bool hasPrestigeTerm = proposal.Terms.Any(term => term != null
                && (term.Type == TreatyTermType.ConcedeDefeat
                    || term.Type == TreatyTermType.DiscreditRuler
                    || term.Type == TreatyTermType.HumiliateRuler));
            if (!hasPrestigeTerm && availableWarScore >= C.TreatyConcedeDefeatCost)
                return true;

            return false;
        }

        private static void RemoveUnmatchedReciprocalExchanges(
            List<TreatyTermRecord> terms,
            string winnerKingdomId,
            int warScoreBudget)
        {
            if (terms == null)
                return;

            for (int index = terms.Count - 1; index >= 0; index--)
            {
                TreatyWarScoreSummary accounting = TreatyWarScoreAccounting.Calculate(
                    terms,
                    winnerKingdomId,
                    warScoreBudget);
                if (accounting.UnmatchedReciprocalExchangeValue <= 0)
                    break;

                TreatyTermRecord term = terms[index];
                if (term == null
                    || term.FromKingdomId != winnerKingdomId
                    || term.WasVoluntaryOffering
                    || !TreatyWarScoreAccounting.IsReciprocalExchangeTerm(term.Type))
                {
                    continue;
                }

                terms.RemoveAt(index);
            }
        }

        private static bool IsOpposingDirection(TreatyTermRecord term, Kingdom winner, Kingdom loser)
        {
            return term != null && ((term.FromKingdomId == winner.StringId && term.ToKingdomId == loser.StringId)
                || (term.FromKingdomId == loser.StringId && term.ToKingdomId == winner.StringId));
        }

        private static bool IsValidPermanentRealm(Kingdom kingdom)
        {
            return kingdom != null
                && !kingdom.IsEliminated
                && kingdom.RulingClan?.Leader != null
                && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom);
        }

        private static bool IsDeJureDescendantOf(FeudalTitleBehavior titles, FeudalTitleRecord title, FeudalTitleRecord root)
        {
            if (titles == null || title == null || root == null)
                return false;

            HashSet<string> visited = new HashSet<string>();
            FeudalTitleRecord current = title;
            while (current != null && visited.Add(current.TitleId))
            {
                if (current.TitleId == root.TitleId)
                    return true;
                current = string.IsNullOrWhiteSpace(current.ParentTitleId) ? null : titles.GetTitle(current.ParentTitleId);
            }
            return false;
        }

        public static int CalculateCollectiveAvailableGold(Kingdom kingdom)
        {
            return GetEligibleClans(kingdom).Sum(clan => Math.Max(0, (clan.Leader?.Gold ?? 0) - C.TreatyClanGoldReserve));
        }

        private static IEnumerable<Clan> GetEligibleClans(Kingdom kingdom)
        {
            return kingdom?.Clans?.Where(clan => clan != null
                && !clan.IsEliminated
                && clan.Leader != null
                && !clan.IsUnderMercenaryService) ?? Enumerable.Empty<Clan>();
        }

        private static Settlement ResolveSettlement(string settlementId)
        {
            return Settlement.All.FirstOrDefault(settlement => settlement != null && settlement.StringId == settlementId);
        }

        private static Kingdom ResolveKingdom(string kingdomId)
        {
            return Kingdom.All.FirstOrDefault(kingdom => kingdom != null && kingdom.StringId == kingdomId);
        }
    }
}
