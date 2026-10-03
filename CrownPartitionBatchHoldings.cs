using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    internal static class CrownPartitionBatchHoldings
    {
        internal static bool TryProject(CrownPartitionBatchRecord batch,
            IReadOnlyList<CrownPartitionPromotionRecord> promotions,
            out Dictionary<string, string> realms, out Dictionary<string, List<string>> holdings, out string reason)
        {
            realms = null;
            holdings = null;
            if (!CrownPartitionBatchReconciliation.TryProjectTitles(batch, promotions, out var titles, out reason)) return false;
            var expectedRealms = batch.Holdings.Keys.ToDictionary(id => id, id => batch.SourceRealmId, StringComparer.Ordinal);
            var expectedHoldings = batch.Holdings.ToDictionary(p => p.Key, p => p.Value.ToList(), StringComparer.Ordinal);
            foreach (var p in promotions)
            {
                if (p.FounderInitializationCompleted && !p.FounderCreationStarted
                    || p.FounderVerified && !p.FounderInitializationCompleted
                    || p.FounderCreationStarted && (!p.FounderInitializationCompleted || !p.FounderVerified))
                    return Fail("founder creation is interrupted or inconsistent", out reason);
                if (!p.FounderCreationStarted)
                {
                    if (p.Founder != null) return Fail("unstarted founder already has a native clan", out reason);
                    expectedRealms.Remove(p.FounderId);
                    expectedHoldings.Remove(p.FounderId);
                }
                else if (p.Founder?.StringId != p.FounderId)
                    return Fail("initialized founder does not match its reserved identity", out reason);

                var expectedMovers = new HashSet<string>(batch.Destinations.Where(pair => pair.Value == p.CrownId).Select(pair => pair.Key));
                if (p.Houses == null || p.Houses.Count != expectedMovers.Count)
                    return Fail("house transfer manifest is incomplete", out reason);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var house in p.Houses)
                {
                    var receipt = house?.Transfer;
                    if (receipt == null) return Fail("house transfer receipt missing", out reason);
                    string id = receipt.Clan?.StringId ?? p.FounderId;
                    if (!seen.Add(id) || !expectedMovers.Contains(id)
                        || receipt.Clan == null && (id != p.FounderId || p.FounderCreationStarted)
                        || receipt.Clan != null && id == p.FounderId && receipt.Clan != p.Founder
                        || house.PrincipalTitleId != batch.Principals[id]
                        || receipt.EndMercenaryContract || receipt.Holdings == null
                        || receipt.Holdings.Count != batch.Holdings[id].Count
                        || !new HashSet<string>(batch.Holdings[id]).SetEquals(receipt.Holdings))
                        return Fail("house receipt disagrees with original membership or holdings", out reason);
                    if (house.MovementReturned && !receipt.ActionStarted
                        || receipt.ActionStarted && !house.MovementReturned
                        || receipt.ActionCompleted && !house.MovementReturned
                        || house.PostMoveBalancesCaptured != house.MovementReturned
                        || receipt.RestorationStarted && !receipt.ActionCompleted
                        || house.RestorationReturned != receipt.RestorationStarted
                        || receipt.RestorationCompleted && !house.RestorationReturned)
                        return Fail("house movement or restoration is interrupted or inconsistent", out reason);
                    if (house.MovementReturned)
                    {
                        if (!p.CrownRegistrationReturned || !p.TransferDiplomacyPrepared || !p.GovernmentVerified
                            || !p.HasVerifiedEstateReceipts() || !p.RealmVerified || !p.RealmInitializationCompleted
                            || p.Successor?.StringId != p.SuccessorId || !expectedRealms.ContainsKey(id))
                            return Fail("house moved before its successor was prepared", out reason);
                        expectedRealms[id] = p.SuccessorId;
                    }
                }
            }
            // Physical changes derive from completed fief receipts, independently of
            // the title registry which also describes legal-only ownership.
            foreach (var p in promotions)
                foreach (var receipt in p.EstateReceipts ?? Enumerable.Empty<CrownEstateDeliveryRecord>())
                    if (!receipt.IsTitle && receipt.Started && receipt.ActionReturned && receipt.Verified)
                    {
                        if (!expectedHoldings.TryGetValue(p.FounderId, out var inherited)
                            || !expectedHoldings[batch.RetainedHouseId].Remove(receipt.AssetId)
                            || inherited.Contains(receipt.AssetId))
                            return Fail("physical delivery has no unique source or initialized recipient", out reason);
                        inherited.Add(receipt.AssetId);
                    }
            var projectedOwners = titles.Values.Where(t => t.Rank == FeudalTitleType.Barony && !string.IsNullOrWhiteSpace(t.CapitalId))
                .ToDictionary(t => t.CapitalId, t => t.ActualHolderId, StringComparer.Ordinal);
            foreach (var entry in expectedHoldings)
                foreach (string settlement in entry.Value)
                    if (!projectedOwners.TryGetValue(settlement, out string holder) || holder != entry.Key)
                        return Fail("projected physical holdings disagree with projected title possession", out reason);
            realms = expectedRealms;
            holdings = expectedHoldings;
            reason = null;
            return true;
        }

        internal static bool TryVerify(CrownPartitionBatchRecord batch, IReadOnlyList<CrownPartitionPromotionRecord> promotions,
            IReadOnlyDictionary<string, string> currentRealms, IReadOnlyDictionary<string, string[]> currentHoldings,
            IReadOnlyDictionary<string, string> physicalOwners, out string reason)
        {
            if (!TryProject(batch, promotions, out var realms, out var holdings, out reason)) return false;
            return TryVerifyProjected(realms, holdings, currentRealms, currentHoldings, physicalOwners, out reason);
        }

        internal static bool TryVerifyProjected(IReadOnlyDictionary<string, string> realms,
            IReadOnlyDictionary<string, List<string>> holdings, IReadOnlyDictionary<string, string> currentRealms,
            IReadOnlyDictionary<string, string[]> currentHoldings, IReadOnlyDictionary<string, string> physicalOwners, out string reason)
        {
            if (realms == null || holdings == null || currentRealms == null || currentHoldings == null || physicalOwners == null
                || !new HashSet<string>(realms.Keys).SetEquals(currentRealms.Keys)
                || !new HashSet<string>(holdings.Keys).SetEquals(currentHoldings.Keys))
                return Fail("current house membership differs from the batch manifest", out reason);
            foreach (var entry in realms)
            {
                var land = currentHoldings[entry.Key];
                if (currentRealms[entry.Key] != entry.Value || land == null || land.Length != holdings[entry.Key].Count
                    || !new HashSet<string>(holdings[entry.Key]).SetEquals(land))
                    return Fail("house realm or holdings differ from its completed receipts: " + entry.Key, out reason);
                foreach (string id in land)
                    if (!physicalOwners.TryGetValue(id, out string owner) || owner != entry.Key)
                        return Fail("settlement owner differs from the recorded estate: " + id, out reason);
            }
            reason = null;
            return true;
        }

        private static bool Fail(string value, out string reason) { reason = value; return false; }
    }
}
