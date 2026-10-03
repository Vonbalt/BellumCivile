using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        internal bool TryAdvanceRealmUnion(CrownAccessionRecord accession, out string reason)
        {
            reason = "absorption sequence requires a registered journal";
            var journal = accession?.Union;
            if (journal == null || _accessions?.Contains(accession) != true) return false;
            string previousReason = journal.PendingReason;
            string details = null;
            bool Crown()
            {
                if (!journal.CrownVerified) return TryTransferRealmUnionCrown(accession, out details);
                if (!journal.CrownTransferStarted || !journal.CrownTransferReturned)
                { details = "verified Crown lacks native return receipts"; return false; }
                var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
                if (titles == null) { details = "title service unavailable"; return false; }
                return RealmUnionCrownTransfer.VerifyTitles(journal, journal.PreviousHouse?.StringId,
                    journal.SurvivingHouse?.StringId, titles.GetTitle, true, out details)
                    && titles.GetKingdomPoliticalTitle(journal.Destination)?.TitleId == journal.PrimaryCrownId
                    && titles.GetKingdomPoliticalTitle(journal.Source)?.TitleId == journal.InheritedCrownId;
            }
            bool Clans()
            {
                if (journal.Clans == null || journal.Clans.Count == 0)
                { details = "clan snapshot unavailable"; return false; }
                foreach (var clan in journal.Clans.ToList())
                    if (!TryMoveRealmUnionClan(accession, clan, out details)) return false;
                return true;
            }
            bool ready = journal.Completed || HasStartedRealmUnion(journal)
                || TryValidateRealmUnionBeforeFirstWrite(accession, out details);
            if (!ready) reason = "union snapshot needs revalidation before any native transfer";
            bool result = ready && RealmUnionSequence.Execute(accession,
                () => TryCaptureRealmUnionObligations(accession, out details),
                () => journal.CrownTransferStarted
                    ? TryVerifyRealmUnionObligations(accession, out details)
                    : TryInheritRealmUnionAgreements(accession, out details),
                Crown, Clans,
                () => TryRetireRealmUnionSource(accession, out details),
                () => TryFinishRealmUnion(accession, out details), out reason);
            if (!result)
            {
                if (!string.IsNullOrEmpty(details)) reason += ": " + details;
                journal.PendingReason = reason;
                if (previousReason != reason)
                    BellumCivileLogger.Log($"Realm union pending; source={journal.Source?.StringId}; destination={journal.Destination?.StringId}; reason={reason}.");
                // No native work exists to preserve. Reconsider a fresh snapshot next time
                // instead of trapping inheritance behind balances or diplomacy that aged.
                if (!HasStartedRealmUnion(journal) && !journal.Completed && accession.Union == journal)
                    accession.Union = null;
            }
            return result;
        }
    }
}
