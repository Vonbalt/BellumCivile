using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile
{
    // Immutable facts for planning/tests. A campaign adapter must capture these before
    // movement; this helper neither changes allegiance nor authorizes native destruction.
    internal sealed class RealmUnionClanSnapshot
    {
        internal string ClanId { get; }
        internal string RealmId { get; }
        internal bool UnderMercenaryContract { get; }
        internal float Influence { get; }
        internal int Debt { get; }

        internal RealmUnionClanSnapshot(string clanId, string realmId, bool mercenary, float influence, int debt)
        {
            ClanId = clanId;
            RealmId = realmId;
            UnderMercenaryContract = mercenary;
            Influence = influence;
            Debt = debt;
        }
    }

    internal sealed class RealmUnionTransferPlan
    {
        internal string SourceRealmId { get; }
        internal string DestinationRealmId { get; }
        internal IReadOnlyList<RealmUnionClanSnapshot> Nobles { get; }
        internal IReadOnlyList<RealmUnionClanSnapshot> Mercenaries { get; }

        private RealmUnionTransferPlan(string source, string destination, List<RealmUnionClanSnapshot> clans)
        {
            SourceRealmId = source;
            DestinationRealmId = destination;
            Nobles = clans.Where(c => !c.UnderMercenaryContract).OrderBy(c => c.ClanId, StringComparer.Ordinal).ToList().AsReadOnly();
            Mercenaries = clans.Where(c => c.UnderMercenaryContract).OrderBy(c => c.ClanId, StringComparer.Ordinal).ToList().AsReadOnly();
        }

        internal static bool TryCreate(string source, string destination,
            IEnumerable<RealmUnionClanSnapshot> sourceClans, out RealmUnionTransferPlan plan, out string reason)
        {
            plan = null;
            reason = null;
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination) || source == destination)
            { reason = "two distinct realm identities are required"; return false; }
            if (sourceClans == null)
            { reason = "source membership snapshot is unavailable"; return false; }
            var clans = sourceClans.ToList();
            if (clans.Count == 0)
            { reason = "source membership snapshot is empty"; return false; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var clan in clans)
            {
                if (!Valid(clan) || clan.RealmId != source || !ids.Add(clan.ClanId))
                { reason = "source membership snapshot is invalid or ambiguous"; return false; }
            }
            if (!clans.Any(c => !c.UnderMercenaryContract))
            { reason = "source has no noble house to inherit"; return false; }
            plan = new RealmUnionTransferPlan(source, destination, clans);
            return true;
        }

        // Current facts must include every live clan still in the source realm as well
        // as every planned clan. Saved completion flags alone cannot prove emptiness.
        internal bool CanRetireSource(IEnumerable<RealmUnionClanSnapshot> currentClans,
            IEnumerable<string> restoredNobleReceipts, IEnumerable<string> endedContractReceipts,
            bool crownVerified, bool obligationsSettled, out string reason)
        {
            reason = null;
            if (currentClans == null || restoredNobleReceipts == null || endedContractReceipts == null)
            { reason = "current membership or completion receipts are unavailable"; return false; }
            var current = new Dictionary<string, RealmUnionClanSnapshot>(StringComparer.Ordinal);
            foreach (var clan in currentClans)
            {
                if (!Valid(clan) || current.ContainsKey(clan.ClanId))
                { reason = "current membership is invalid or ambiguous"; return false; }
                if (clan.RealmId == SourceRealmId)
                { reason = "a live clan remains in the source realm: " + clan.ClanId; return false; }
                current.Add(clan.ClanId, clan);
            }
            var restored = new HashSet<string>(restoredNobleReceipts, StringComparer.Ordinal);
            foreach (var noble in Nobles)
            {
                if (!current.TryGetValue(noble.ClanId, out var live) || live.UnderMercenaryContract
                    || live.RealmId != DestinationRealmId || !restored.Contains(noble.ClanId))
                { reason = "noble transfer is not verified: " + noble.ClanId; return false; }
                if (live.Influence != noble.Influence || live.Debt != noble.Debt)
                { reason = "noble political balances require reconciliation: " + noble.ClanId; return false; }
            }
            var ended = new HashSet<string>(endedContractReceipts, StringComparer.Ordinal);
            foreach (var mercenary in Mercenaries)
            {
                if (!current.TryGetValue(mercenary.ClanId, out var live) || live.UnderMercenaryContract
                    || !string.IsNullOrEmpty(live.RealmId) || !ended.Contains(mercenary.ClanId))
                { reason = "mercenary contract termination is not verified: " + mercenary.ClanId; return false; }
            }
            if (!crownVerified)
            { reason = "Crown ownership and hierarchy preservation are not verified"; return false; }
            if (!obligationsSettled)
            { reason = "diplomatic and political obligations remain unresolved"; return false; }
            return true;
        }

        private static bool Valid(RealmUnionClanSnapshot clan) => clan != null
            && !string.IsNullOrWhiteSpace(clan.ClanId)
            && !float.IsNaN(clan.Influence) && !float.IsInfinity(clan.Influence);
    }
}
