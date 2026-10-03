using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class RoyalistClaimHelper
    {
        public static bool HasRestorationClaim(Clan clan, Kingdom kingdom, FactionManagerBehavior factionManager)
        {
            if (!IsValidClaimant(clan, kingdom))
                return false;

            if (HasKingdomTitleClaim(clan, kingdom))
                return true;

            return Campaign.Current?.GetCampaignBehavior<DynasticClaimBehavior>()
                ?.HasActiveClaim(clan, kingdom) == true;
        }

        public static bool HasKingdomTitleClaim(Clan clan, Kingdom kingdom, FeudalClaimStrength? strength = null)
        {
            if (!IsValidClaimant(clan, kingdom))
                return false;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalTitleRecord kingdomTitle = GetKingdomTitle(titleBehavior, kingdom);
            return titleBehavior != null
                && kingdomTitle != null
                && titleBehavior.HasActiveClaim(clan, kingdomTitle, strength);
        }

        public static bool TryGetThroneClaimStrength(Clan clan, Kingdom kingdom, FactionManagerBehavior factionManager, out FeudalClaimStrength strength)
        {
            strength = FeudalClaimStrength.Weak;
            if (!IsValidClaimant(clan, kingdom))
                return false;

            if (HasKingdomTitleClaim(clan, kingdom, FeudalClaimStrength.Strong))
            {
                strength = FeudalClaimStrength.Strong;
                return true;
            }

            if (HasKingdomTitleClaim(clan, kingdom, FeudalClaimStrength.Weak))
            {
                strength = FeudalClaimStrength.Weak;
                return true;
            }

            return Campaign.Current?.GetCampaignBehavior<DynasticClaimBehavior>()
                ?.HasActiveClaim(clan, kingdom) == true;
        }

        public static bool IsRoyalistSupporterOfClaimant(Clan supporter, Clan claimant, Kingdom kingdom, FactionManagerBehavior factionManager)
        {
            if (supporter == null || supporter.IsEliminated || claimant == null)
                return false;

            if (!HasRestorationClaim(claimant, kingdom, factionManager))
                return false;

            if (ShareKingdomTitleClaimOrigin(supporter, claimant, kingdom))
                return true;

            return Campaign.Current?.GetCampaignBehavior<DynasticClaimBehavior>()
                ?.IsDynasticClaimAlly(supporter, claimant, kingdom) == true;
        }

        private static bool IsValidClaimant(Clan clan, Kingdom kingdom)
        {
            return clan != null
                && kingdom != null
                && !clan.IsEliminated
                && !clan.IsUnderMercenaryService
                && clan.Kingdom == kingdom
                && kingdom.RulingClan != clan;
        }

        private static bool ShareKingdomTitleClaimOrigin(Clan supporter, Clan claimant, Kingdom kingdom)
        {
            if (!IsValidClaimant(supporter, kingdom) || !IsValidClaimant(claimant, kingdom))
                return false;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalTitleRecord kingdomTitle = GetKingdomTitle(titleBehavior, kingdom);
            if (titleBehavior == null || kingdomTitle == null || !titleBehavior.HasActiveClaim(claimant, kingdomTitle))
                return false;

            List<FeudalClaimRecord> claimantClaims = GetActiveKingdomClaims(titleBehavior, claimant, kingdomTitle).ToList();
            List<FeudalClaimRecord> supporterClaims = GetActiveKingdomClaims(titleBehavior, supporter, kingdomTitle).ToList();

            foreach (FeudalClaimRecord supporterClaim in supporterClaims)
            {
                if (supporterClaim == null || string.IsNullOrWhiteSpace(supporterClaim.OriginClanId))
                    continue;

                if (string.Equals(supporterClaim.OriginClanId, claimant.StringId, StringComparison.Ordinal))
                    return true;

                if (claimantClaims.Any(claim => claim != null
                    && !string.IsNullOrWhiteSpace(claim.OriginClanId)
                    && string.Equals(claim.OriginClanId, supporterClaim.OriginClanId, StringComparison.Ordinal)))
                    return true;
            }

            return false;
        }

        private static IEnumerable<FeudalClaimRecord> GetActiveKingdomClaims(FeudalTitleBehavior titleBehavior, Clan claimant, FeudalTitleRecord kingdomTitle)
        {
            if (titleBehavior == null || claimant == null || kingdomTitle == null)
                yield break;

            foreach (FeudalClaimRecord claim in titleBehavior.GetActiveClaims(claimant, kingdomTitle))
            {
                yield return claim;
            }
        }

        private static FeudalTitleRecord GetKingdomTitle(FeudalTitleBehavior titleBehavior, Kingdom kingdom)
        {
            return titleBehavior?.GetTitle(FeudalTitleBehavior.BuildKingdomTitleId(kingdom));
        }

    }
}
