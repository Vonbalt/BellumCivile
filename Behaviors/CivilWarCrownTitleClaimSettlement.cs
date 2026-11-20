using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public partial class CivilWarResolutionBehavior
    {
        private readonly HashSet<FactionObject> _applyingCrownClaimTerms = new HashSet<FactionObject>();

        private bool RestoreCrownClaimOccupations(FactionObject faction, Kingdom rebel, string excludedHolding = null)
        {
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles == null || !_applyingCrownClaimTerms.Add(faction)) return false;
            try
            {
                foreach (var entry in faction.CrownClaimOccupiedFiefs())
                {
                    if (entry.SettlementId == excludedHolding) continue;
                    var holding = FeudalTitleBehavior.FindCrownClaimSettlement(entry.SettlementId);
                    Clan source = holding?.OwnerClan;
                    Clan owner = ResolveClanById(entry.OwnerClanId);
                    bool Participant(Clan clan) => clan?.Kingdom != null && (clan.Kingdom == faction.ParentKingdom || clan.Kingdom == rebel);
                    if (source == owner || !Participant(source) || !Participant(owner) || !NobleClanEligibilityHelper.IsLiveNobleClan(owner))
                    {
                        faction.NoteCrownClaimRestored(entry.SettlementId);
                        continue;
                    }
                    if (!titles.TransferChallengePossession(source, owner, holding)) return false;
                    FiefDeliberationBehavior.Current?.ClearFiefVoteStateForSettlement(holding, "Crown title claim restitution");
                    faction.NoteCrownClaimRestored(entry.SettlementId);
                }
                return true;
            }
            finally { _applyingCrownClaimTerms.Remove(faction); }
        }

        private bool ResolveCrownClaimRebelVictory(FactionObject faction, Kingdom rebel)
        {
            Kingdom realm = faction.ParentKingdom;
            if (realm.IsEliminated)
            {
                HandleDestroyedParentKingdom(realm);
                return false;
            }
            if (faction.PendingCrownClaimSettlement != CrownTitleClaimSettlement.None
                && faction.PendingCrownClaimSettlement != CrownTitleClaimSettlement.RebelVictory) return false;
            faction.PendingCrownClaimSettlement = CrownTitleClaimSettlement.RebelVictory;
            if (_applyingCrownClaimTerms.Contains(faction) || !FactionManagerBehavior.SuccessionOpportunityReady(realm)) return false;

            CrownTitleClaimState state = faction.CrownClaimState(rebel);
            if (state != CrownTitleClaimState.Valid || (CountStrongholds(realm) == 0 && CountStrongholds(rebel) == 0))
            {
                faction.PendingCrownClaimSettlement = CrownTitleClaimSettlement.WhitePeace;
                ResolveWhitePeace(faction, rebel, demandSatisfied: state == CrownTitleClaimState.Fulfilled,
                    cause: state == CrownTitleClaimState.Invalid ? CrownClaimEndedReason() : null);
                return false;
            }

            if (!PrepareMercenarySettlement(faction, rebel, MercenaryRebelVictory)) return false;
            var title = faction.CrownClaimTitle;
            string objective = title.TitleType == FeudalTitleType.Barony ? title.CapitalSettlementId : null;
            if (!RestoreCrownClaimOccupations(faction, rebel, objective)) return false;
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            if (!_applyingCrownClaimTerms.Add(faction)) return false;
            try
            {
                if (!titles.TryAwardCrownClaim(faction.Leader, faction.CrownClaimOriginalHolder, title)) return false;
            }
            finally { _applyingCrownClaimTerms.Remove(faction); }

            var result = ConflictOutcomeBehavior.Current?.BeginCivil(faction, rebel);
            Clan claimant = faction.Leader;
            List<Clan> winners = rebel.Clans.Where(c => c != null && !c.IsUnderMercenaryService).ToList();
            CaptureCurrentCivilWarFiefSnapshot(faction, rebel);
            Campaign.Current.GetCampaignBehavior<RebellionSummaryBehavior>()?.RecordOutcome(faction, RebellionSummaryOutcome.RebelVictory);
            CompleteCivilWarTracker(faction, rebel, "Crown title claim enforced");
            ApplyCivilWarPeaceIfNeeded(realm, rebel);
            Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.RemoveFaction(faction);
            foreach (Clan clan in winners.ToList())
                TransferCivilWarClan(clan, rebel, realm, faction);
            DrainAndDestroyRebelKingdom(rebel, realm, reunification: true);
            faction.RestoreCivilWarInfluenceSnapshots(winners.Concat(realm.Clans));
            ApplyCivilWarVictoryInfluenceRewards(winners, claimant, "Crown title claim enforced");
            PacifyIdeologies(realm, realm.RulingClan);
            faction.PendingCrownClaimSettlement = CrownTitleClaimSettlement.None;
            ConflictOutcomeBehavior.Current?.Publish(result, "crown_title_victory", ConflictOutcomeBehavior.ContinuingWars(realm));
            return true;
        }

        private static TextObject CrownClaimEndedReason()
            => new TextObject("{=BC_CrownTitleClaim_EndedReason}The claim against the Crown can no longer be enforced. The fighting ends without an award or post-war judgment.");

        private bool ReconcileCrownTitleClaim(FactionObject faction)
        {
            if (faction.Type != FactionType.CrownTitleClaim) return false;
            Kingdom rebel = faction.GetTrackedRebelKingdomIncludingEliminated();
            if (rebel == null || faction.ParentKingdom?.IsEliminated != false || _applyingCrownClaimTerms.Contains(faction)
                || faction.IsRebellionCreationPending || CivilWarConflictBehavior.IsFactionTransferPending(faction))
                return false;

            switch (faction.PendingCrownClaimSettlement)
            {
                case CrownTitleClaimSettlement.RebelVictory: ResolveCrownClaimRebelVictory(faction, rebel); break;
                case CrownTitleClaimSettlement.LoyalistVictory: ResolveLiegeVictoryInternal(faction, rebel, allowEliminatedRebelShell: true); break;
                case CrownTitleClaimSettlement.WhitePeace:
                    var current = faction.CrownClaimState(rebel);
                    ResolveWhitePeace(faction, rebel, demandSatisfied: current == CrownTitleClaimState.Fulfilled,
                        cause: current == CrownTitleClaimState.Invalid ? CrownClaimEndedReason() : null);
                    break;
                default:
                    if (rebel.IsEliminated || !FactionManagerBehavior.SuccessionOpportunityReady(faction.ParentKingdom)) break;
                    var state = faction.CrownClaimState(rebel);
                    if (state != CrownTitleClaimState.Valid)
                        ResolveWhitePeace(faction, rebel, demandSatisfied: state == CrownTitleClaimState.Fulfilled,
                            cause: state == CrownTitleClaimState.Invalid ? CrownClaimEndedReason() : null);
                    break;
            }
            return true;
        }

        private bool EndCrownTitleClaimWithoutParent(FactionObject faction, Kingdom rebel)
        {
            var result = ConflictOutcomeBehavior.Current?.BeginCivil(faction, rebel);
            ConflictOutcomeBehavior.Capture(result, "CAUSE", new TextObject("{=BC_CrownTitleClaim_RealmGone}The realm itself has fallen. The rebels' claim to {TITLE} brought no right to found a successor realm.")
                .SetTextVariable("TITLE", faction.CrownClaimTitleName()));
            if (!ReleaseMercenariesFromKingdom(rebel) || !_applyingCrownClaimTerms.Add(faction)) return false;
            try
            {
                RunWithRulerRepairSuppressed(rebel, () =>
                {
                    foreach (Clan clan in rebel.Clans.Where(c => c?.Kingdom == rebel && !c.IsEliminated).ToList())
                    {
                        if (clan != Clan.PlayerClan && Campaign.Current.GetCampaignBehavior<ExiledClanRecoveryBehavior>()
                            ?.ResolveClanExile(clan, faction.ParentKingdom, rebel, ExileCause.KingdomDestroyed) == true)
                            continue;
                        ChangeKingdomAction.ApplyByLeaveKingdom(clan, false);
                    }
                });
            }
            finally { _applyingCrownClaimTerms.Remove(faction); }
            if (rebel.Clans.Any(c => c?.Kingdom == rebel && !c.IsEliminated)) return false;
            CompleteCivilWarTracker(faction, rebel, "Crown title claim ended without parent realm");
            Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.RemoveFaction(faction);
            DrainAndDestroyRebelKingdom(rebel, null);
            ConflictOutcomeBehavior.Current?.Publish(result, "ended");
            return true;
        }
    }
}
