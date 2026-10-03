using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class FeudalTitleBehavior
    {
        // Registration only changes the sovereign Crown's political root and shell
        // association. Descendant legal origins and inherited ownership stay intact.
        internal static bool MatchesCrownRegistrationTitle(CrownPartitionPromotionRecord journal,
            RealmUnionTitleRecord captured, FeudalTitleRecord current, bool registered)
            => MatchesCrownPromotionTitle(journal, captured, current, registered,
                registered && captured?.TitleId == journal?.CrownId ? string.Empty : captured?.ActualParentId);

        internal static bool MatchesCrownPromotionTitle(CrownPartitionPromotionRecord journal,
            RealmUnionTitleRecord captured, FeudalTitleRecord current, bool registered, string politicalParent)
        {
            if (journal?.RetainedHouse == null || captured == null || current?.IsActive != true
                || current.TitleId != captured.TitleId || current.TitleType != captured.Rank
                || current.CapitalSettlementId != captured.CapitalId
                || current.ParentTitleId != captured.LegalParentId
                || journal.EstateTitleIds == null || journal.EstateFiefIds == null) return false;
            bool inherited = journal.EstateTitleIds.Contains(captured.TitleId);
            bool physical = captured.Rank == FeudalTitleType.Barony && journal.EstateFiefIds.Contains(captured.CapitalId);
            string legal = inherited ? journal.FounderId : captured.LegalHolderId;
            string actual = physical || inherited && captured.ActualHolderId == journal.RetainedHouse.StringId
                ? journal.FounderId : captured.ActualHolderId;
            bool crown = registered && captured.TitleId == journal.CrownId;
            return current.DeJureHolderClanId == legal && current.DeFactoHolderClanId == actual
                && current.DeFactoParentTitleId == politicalParent
                && current.AssociatedKingdomId == (crown ? journal.SuccessorId : captured.OriginRealmId);
        }

        internal bool TryRegisterCrownPromotion(CrownPartitionPromotionRecord journal, out string reason)
        {
            reason = "Crown registration requires an initialized promotion and delivered estate";
            if (journal == null || journal.Completed || !journal.HasVerifiedEstateReceipts()
                || Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>()?.IsRegisteredCrownPromotion(journal) != true
                || !journal.TryRecoverFounder(journal.Founder) || !journal.TryRecoverRealm(journal.Successor)
                || journal.Parent.IsEliminated || journal.Parent.RulingClan != journal.RetainedHouse
                || journal.Titles == null || journal.Titles.Count == 0
                || journal.Titles.Any(t => t == null || string.IsNullOrWhiteSpace(t.TitleId))
                || journal.Titles.Select(t => t.TitleId).Distinct().Count() != journal.Titles.Count) return false;
            if (journal.CrownRegistrationReturned && !journal.CrownRegistrationStarted
                || journal.CrownRegistrationStarted && !journal.CrownRegistrationReturned)
            { reason = "Crown registration receipt is interrupted or inconsistent; registration will not be replayed"; return false; }
            EnsureCollectionsInitialized();
            var crown = GetTitle(journal.CrownId);
            var primary = GetRealmSovereignTitle(journal.Parent, FeudalHierarchyMode.DeFacto)
                ?? GetKingdomPoliticalTitle(journal.Parent);
            if (crown == null || primary == null || primary.TitleId == crown.TitleId
                || primary.TitleType != crown.TitleType || primary.DeFactoHolderClanId != journal.RetainedHouse.StringId
                || primary.DeJureHolderClanId != journal.RetainedHouse.StringId
                || crown.DeJureHolderClanId != journal.FounderId || crown.DeFactoHolderClanId != journal.FounderId
                || !journal.Titles.Any(t => t.TitleId == crown.TitleId))
            { reason = "primary or successor Crown ownership changed before registration"; return false; }
            bool registered = journal.CrownRegistrationReturned;
            var succession = Campaign.Current.GetCampaignBehavior<PartitionSuccessionBehavior>();
            var batch = succession.GetCrownPromotionBatch(journal);
            Dictionary<string, RealmUnionTitleRecord> afterRegistration = null;
            if (batch != null)
            {
                if (!succession.TryVerifyCrownBatchWorld(batch, out reason)) return false;
                if (!registered && !CrownPartitionBatchReconciliation.TryProjectCrownRegistration(
                    batch.CrownBatch, batch.CrownPromotions, journal, out afterRegistration, out reason)) return false;
            }
            else if (journal.Titles.Any(t => !MatchesCrownRegistrationTitle(journal, t, GetTitle(t.TitleId), registered)))
            { reason = "captured title rights or hierarchy changed before Crown registration verification"; return false; }
            bool hasMapping = _independentRealmSourceTitleByKingdomId.TryGetValue(journal.SuccessorId, out string mapped);
            if (registered ? !hasMapping || mapped != journal.CrownId : hasMapping)
            { reason = "successor Crown registry conflicts with its registration receipt"; return false; }
            if (!registered && GetTitle("bc_title_kingdom_" + journal.SuccessorId)?.IsActive == true)
            { reason = "successor already has a generated Crown; it must not be silently replaced"; return false; }
            if (Kingdom.All.Any(k => k != null && !k.IsEliminated && k != journal.Successor
                && GetIndependentRealmSovereignTitle(k)?.TitleId == journal.CrownId))
            { reason = "another live realm is still registered to the inherited Crown"; return false; }
            if (registered) { reason = null; return true; }
            try
            {
                journal.CrownRegistrationStarted = true;
                crown.SetDeFactoParentTitle(string.Empty);
                RegisterIndependentRealmShell(journal.Successor, crown, "journaled Crown partition");
                if (GetIndependentRealmSovereignTitle(journal.Successor) != crown
                    || (batch != null
                        ? !CrownPartitionBatchReconciliation.TryVerifyProjectedTitles(afterRegistration, GetAllTitles(), out reason)
                        : journal.Titles.Any(t => !MatchesCrownRegistrationTitle(journal, t, GetTitle(t.TitleId), true))))
                    throw new InvalidOperationException("registered Crown or preserved title state did not verify");
                journal.CrownRegistrationReturned = true;
                journal.PendingReason = null;
                reason = null;
                return true;
            }
            catch (Exception ex)
            {
                reason = "Crown registration interrupted: " + ex.Message;
                journal.PendingReason = reason;
                BellumCivileLogger.Log($"Crown partition {journal.CrownId}: {ex}");
                return false;
            }
        }
    }
}
