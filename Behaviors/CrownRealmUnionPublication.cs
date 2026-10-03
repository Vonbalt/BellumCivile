using System;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        internal bool TryPublishRealmUnion(CrownAccessionRecord accession, out string reason)
        {
            reason = "union publication requires a registered unjournaled accession";
            if (accession == null || accession.Completed || accession.Union != null
                || _accessions?.Contains(accession) != true) return false;
            try
            {
                var source = accession.Realm;
                var destination = accession.Heir?.Clan?.Kingdom;
                if (source == null || destination == null || source == destination) return false;
                var partition = Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>();
                if (partition == null || partition.IsCrownPromotionRealmProtected(source)
                    || partition.IsCrownPromotionRealmProtected(destination))
                { reason = "partition service unavailable or a participating realm has an unfinished Crown promotion"; return false; }
                if (_accessions.Any(a => a != accession && a != null
                    && ((!a.Completed && (a.Realm == source || a.Realm == destination))
                        || a.Union != null && !a.Union.Completed
                            && (a.Union.Source == source || a.Union.Destination == source
                                || a.Union.Source == destination || a.Union.Destination == destination))))
                { reason = "another accession or union involves a participating realm"; return false; }
                if (!RealmUnionSnapshotService.TryCapture(accession, out var draft, out reason)) return false;
                string details = null;
                bool result = RealmUnionPublication.TryPublish(accession, draft,
                    () => TryCaptureRealmUnionObligations(accession, out details)
                        && TryPrepareRealmUnionAgreements(accession, out details), HasStartedRealmUnion, out reason);
                if (!result && !string.IsNullOrEmpty(details)) reason += ": " + details;
                return result;
            }
            catch (Exception ex)
            { reason = "union capture interrupted: " + ex.Message; return false; }
        }
    }
}
