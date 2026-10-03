using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CrownPartitionBatchRecord
    {
        [SaveableField(1)] public string SourceRealmId;
        [SaveableField(2)] public string PredecessorId;
        [SaveableField(3)] public string RetainedHouseId;
        [SaveableField(4)] public string PrimaryCrownId;
        [SaveableField(5)] public Dictionary<string, string> Recipients;
        [SaveableField(6)] public Dictionary<string, string> Destinations;
        [SaveableField(7)] public Dictionary<string, string> Principals;
        [SaveableField(8)] public Dictionary<string, List<string>> Holdings;
        [SaveableField(9)] public List<RealmUnionTitleRecord> Titles;
        [SaveableField(10)] public Dictionary<string, string> PoliticalParentTargets;
        [SaveableField(11)] public bool HierarchyStarted;
        [SaveableField(12)] public bool HierarchyReturned;
        [SaveableField(13)] public bool HierarchyVerified;
        [SaveableField(14)] public bool CourtFinalizationStarted;
        [SaveableField(15)] public bool CourtFinalizationReturned;
        [SaveableField(16)] public Dictionary<string, string> OriginalAgreements;
        [SaveableField(17)] public bool ObligationsVerified;
        [SaveableField(18)] public List<string> AnnouncementsStarted = new List<string>();
        [SaveableField(19)] public List<string> AnnouncementsReturned = new List<string>();
        [SaveableField(20)] public bool Completed;

        internal bool TryAnnounce(string key, Action announce, out string reason)
        {
            reason = "invalid partition announcement receipt";
            if (!ObligationsVerified || string.IsNullOrWhiteSpace(key) || announce == null
                || AnnouncementsStarted == null || AnnouncementsReturned == null
                || AnnouncementsStarted.Distinct().Count() != AnnouncementsStarted.Count
                || AnnouncementsReturned.Distinct().Count() != AnnouncementsReturned.Count
                || AnnouncementsReturned.Except(AnnouncementsStarted).Any()) return false;
            if (AnnouncementsReturned.Contains(key)) { reason = null; return true; }
            if (AnnouncementsStarted.Contains(key))
            { reason = "partition announcement was interrupted and will not be replayed"; return false; }
            try
            {
                AnnouncementsStarted.Add(key);
                announce();
                AnnouncementsReturned.Add(key);
                reason = null;
                return true;
            }
            catch (Exception ex) { reason = "partition announcement interrupted: " + ex.Message; return false; }
        }

        internal static CrownPartitionBatchRecord Capture(string sourceRealm, string predecessor, CrownPartitionBatchPlan plan)
        {
            if (string.IsNullOrWhiteSpace(sourceRealm) || string.IsNullOrWhiteSpace(predecessor) || plan == null)
                throw new ArgumentException("A Crown batch requires its source realm, predecessor and validated plan.");
            return new CrownPartitionBatchRecord
            {
                SourceRealmId = sourceRealm, PredecessorId = predecessor,
                RetainedHouseId = plan.RetainedHouseId, PrimaryCrownId = plan.PrimaryCrownId,
                Recipients = plan.RecipientByCrown.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                Destinations = plan.DestinationCrownByHouse.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                Principals = plan.PrincipalTitleByHouse.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                Holdings = plan.HoldingsByHouse.ToDictionary(p => p.Key, p => p.Value.ToList(), StringComparer.Ordinal),
                Titles = plan.CopyTitleSnapshot()
            };
        }

        internal bool TryRestorePlan(out CrownPartitionBatchPlan plan, out string reason)
        {
            plan = null;
            reason = "incomplete saved Crown batch";
            if (string.IsNullOrWhiteSpace(SourceRealmId) || string.IsNullOrWhiteSpace(PredecessorId)
                || Recipients == null || Destinations == null || Principals == null || Holdings == null || Titles == null
                || Titles.Any(t => t == null) || Holdings.Any(p => p.Value == null)) return false;
            var titles = Titles.Select(t =>
            {
                var title = new FeudalTitleRecord(t.TitleId, t.TitleId, t.Rank, t.LegalHolderId,
                    t.ActualHolderId, t.LegalParentId, t.CapitalId, t.OriginRealmId, 0, 0);
                title.SetDeFactoParentTitle(t.ActualParentId);
                return title;
            }).ToList();
            var founders = new HashSet<string>(Recipients.Values, StringComparer.Ordinal);
            if (founders.Any(id => id == null || !Holdings.TryGetValue(id, out var land) || land.Count != 0))
            { reason = "reserved founder holdings differ from the pre-creation baseline"; return false; }
            var source = Holdings.Where(p => !founders.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
            if (!CrownPartitionBatchPlan.TryCreate(RetainedHouseId, PrimaryCrownId, Recipients, source, titles,
                out var restored, out reason)) return false;
            bool Same(IReadOnlyDictionary<string, string> expected, Dictionary<string, string> recorded) =>
                expected.Count == recorded.Count && expected.All(p => recorded.TryGetValue(p.Key, out string value) && value == p.Value);
            if (!Same(restored.DestinationCrownByHouse, Destinations) || !Same(restored.PrincipalTitleByHouse, Principals))
            { reason = "saved assignments disagree with the original Crown batch baseline"; return false; }
            plan = restored;
            reason = null;
            return true;
        }
    }
}
