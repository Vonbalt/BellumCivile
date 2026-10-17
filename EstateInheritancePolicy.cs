using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class EstateInheritancePolicy
    {
        internal static Kingdom ResolvePermanentRealm(Clan clan)
        {
            var realm = clan?.Kingdom;
            if (realm == null || realm.IsEliminated) return null;
            if (!BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm)) return realm;
            return Kingdom.All.FirstOrDefault(k => !k.IsEliminated
                && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(k)
                && DynasticHeirBehavior.IsClanAttachedToSuccessionRealm(clan, k));
        }

        internal static bool IsDomesticHouse(Clan house, Kingdom realm) => house != null
            && !house.IsEliminated && !house.IsBanditFaction && !house.IsUnderMercenaryService
            && !NobleClanEligibilityHelper.IsNonPlayerMinorClan(house) && realm != null && !realm.IsEliminated
            && DynasticHeirBehavior.IsClanAttachedToSuccessionRealm(house, realm);

        internal static List<Hero> OrderDomesticHeirs(Clan source, Hero predecessor,
            Kingdom realm, SuccessionLawSet laws)
        {
            if (source == null || predecessor == null || realm == null) return new List<Hero>();
            return OrderDomesticCandidates(Clan.All.Where(c => IsDomesticHouse(c, realm)).SelectMany(c => c.Heroes),
                predecessor, realm, laws);
        }

        internal static List<Hero> OrderDomesticCandidates(IEnumerable<Hero> candidates, Hero predecessor,
            Kingdom realm, SuccessionLawSet laws) => SuccessionLawHelper.OrderSuccessionCandidates(
                candidates.Where(h => h != null && h != predecessor && !h.IsWanderer && !h.IsNotable
                    && IsDomesticHouse(h.Clan, realm) && SuccessionLawHelper.IsBloodRelative(h, predecessor)),
                predecessor, laws, includeUnderage: true).Where(RegencyPolicy.CanReceiveCrown).ToList();

        internal static bool IsSovereignEstateRoot(FeudalTitleBehavior titles, FeudalTitleRecord title,
            Clan source, string politicalCrownId, bool hereditary)
        {
            if (title?.IsActive != true || title.DeJureHolderClanId != source?.StringId
                || title.DeFactoHolderClanId != source.StringId) return false;
            if (title.TitleId == politicalCrownId) return hereditary;
            if (title.TitleType < FeudalTitleType.Kingdom || titles.IsLandlessHistoricalCrown(title)) return false;
            var seen = new HashSet<string>();
            for (var parent = titles.GetParentTitle(title, FeudalHierarchyMode.DeFacto);
                parent != null && seen.Add(parent.TitleId);
                parent = titles.GetParentTitle(parent, FeudalHierarchyMode.DeFacto))
                if (parent.IsActive && parent.TitleType > title.TitleType) return false;
            return true;
        }

        // The same read-only allocation is used before death and by encyclopedia previews.
        internal static CrossClanEstateRecord BuildProjection(Clan source, Hero predecessor = null)
        {
            var realm = ResolvePermanentRealm(source);
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            predecessor = predecessor ?? RegencyBehavior.Instance?.GetLegalClanHead(source) ?? source?.Leader;
            if (realm == null || titles == null || predecessor == null || source.IsEliminated) return null;
            var laws = SuccessionLawHelper.GetLawsForClan(source);
            var record = new CrossClanEstateRecord
            {
                Source = source, Deceased = predecessor, Realm = realm, PolicyVersion = 1,
                GenderLaw = laws.GenderLaw, SuccessionLaw = laws.SuccessionLaw,
                DomesticHeirs = OrderDomesticHeirs(source, predecessor, realm, laws)
            };
            var estateTitles = titles.GetTitlesHeldByClan(source, deJure: true)
                .Where(t => t.IsActive && !FeudalInheritancePlanner.IsAllocationCustodyTitle(t, titles, source)).ToList();
            var fiefs = source.Fiefs.Where(f => FeudalInheritancePlanner.IsHeritableFief(f, source)).ToList();
            bool ruling = realm.RulingClan == source;
            var primary = ruling ? titles.GetRealmSovereignTitle(realm, FeudalHierarchyMode.DeFacto)
                ?? titles.GetKingdomPoliticalTitle(realm) : null;
            record.PreferredPrimaryTitleId = primary?.TitleId;
            record.RealmCrownTitleId = ruling ? titles.GetKingdomPoliticalTitle(realm)?.TitleId : null;
            bool hereditary = ruling && CrownAccessionBehavior.IsHereditaryRealm(realm);
            var roots = estateTitles.Where(t => IsSovereignEstateRoot(titles, t, source,
                record.RealmCrownTitleId, hereditary)).ToList();
            var reservedTitles = new HashSet<string>(StringComparer.Ordinal);
            var reservedFiefs = new HashSet<string>(StringComparer.Ordinal);
            int reserve = Math.Max(1, BellumCivileOptions.PartitionSuccessionMainHeirReservedFiefs);
            bool partition = BellumCivileOptions.EnablePartitionSuccession;
            var unrestricted = PartitionSuccessionBehavior.OrderEstateHeirs(source.Heroes, predecessor, laws);
            if (source.Leader == predecessor && record.DomesticHeirs.Count > 0
                && (roots.Count == 0 || unrestricted.All(h => h.Clan == source))
                && record.DomesticHeirs.All(h => h.Clan == source))
            {
                // This estate will use the existing in-house partition executor. Preview its
                // original package allocation rather than redistributing a Crown's remainder.
                var local = FeudalInheritancePlanner.BuildPlan(source, predecessor, fiefs,
                    record.DomesticHeirs, estateTitles, reserve, primary?.TitleId ?? string.Empty);
                if (partition && hereditary && roots.Count > 1
                    && !FeudalInheritancePlanner.TryBuildCrownFirstTitlePackages(local, titles,
                        titles.GetAllTitles().ToList(), reserve, primary?.TitleId ?? string.Empty, out _)) return null;
                foreach (Hero heir in record.DomesticHeirs)
                {
                    var share = PartitionSuccessionBehavior.SnapshotEstateShare(local, record.DomesticHeirs[0], heir, partition);
                    if (share == null || share.Titles.Count == 0 && share.Fiefs.Count == 0) continue;
                    share.DomesticOnly = !roots.Any(t => share.Titles.Contains(t.TitleId));
                    share.ReceivesGold = true;
                    record.Shares.Add(share);
                }
                return HasCompleteManifest(record, estateTitles, fiefs.Select(f => f.Settlement.StringId)) ? record : null;
            }
            if (roots.Count > 0)
            {
                var heirs = unrestricted;
                // Political succession does not restore a property share already advanced,
                // or override the deceased house's own estate laws.
                var plan = FeudalInheritancePlanner.BuildPlan(source, predecessor, fiefs, heirs,
                    estateTitles, reserve, primary?.TitleId ?? string.Empty);
                if (partition && hereditary && roots.Count > 1
                    && !FeudalInheritancePlanner.TryBuildCrownFirstTitlePackages(plan, titles,
                        titles.GetAllTitles().ToList(), reserve, primary?.TitleId ?? string.Empty, out _)) return null;
                foreach (Hero heir in heirs)
                {
                    var share = PartitionSuccessionBehavior.SnapshotEstateShare(plan, heirs[0], heir, partition);
                    if (share == null) continue;
                    var inheritedRoots = roots.Where(t => share.Titles.Contains(t.TitleId)).ToList();
                    if (inheritedRoots.Count == 0) continue;
                    var controlledTree = new HashSet<string>(inheritedRoots
                        .SelectMany(t => titles.GetTitleAndDescendants(t, FeudalHierarchyMode.DeFacto))
                        .Where(t => t.IsActive).Select(t => t.TitleId));
                    var footprint = inheritedRoots.SelectMany(titles.GetTitleAndDescendants)
                        .Where(t => t.IsActive && controlledTree.Contains(t.TitleId))
                        .GroupBy(t => t.TitleId).Select(g => g.First()).ToList();
                    var ids = new HashSet<string>(footprint.Select(t => t.TitleId));
                    var settlements = new HashSet<string>(footprint.Where(t => t.TitleType == FeudalTitleType.Barony)
                        .Select(t => t.CapitalSettlementId));
                    // A primary share also contains loose estates outside the inherited Crown.
                    share.Titles.RemoveAll(id => !ids.Contains(id));
                    share.Fiefs.RemoveAll(id => !settlements.Contains(id));
                    share.RootTitleId = inheritedRoots.OrderByDescending(t => t.TitleType).First().TitleId;
                    share.PrimaryFiefId = share.Fiefs.FirstOrDefault();
                    record.Shares.Add(share);
                    reservedTitles.UnionWith(share.Titles);
                    reservedFiefs.UnionWith(share.Fiefs);
                }
            }
            var ordinaryTitles = estateTitles.Where(t => !reservedTitles.Contains(t.TitleId)
                && t.TitleId != record.RealmCrownTitleId).ToList();
            var ordinaryFiefs = fiefs.Where(f => !reservedFiefs.Contains(f.Settlement.StringId)).ToList();
            if (ordinaryTitles.Count > 0 || ordinaryFiefs.Count > 0)
            {
                if (record.DomesticHeirs.Count == 0)
                    record.Shares.Add(new CrossClanEstateShare
                    {
                        Heir = realm.RulingClan?.Leader, DomesticOnly = true, IsEscheat = true,
                        Titles = ordinaryTitles.Select(t => t.TitleId).ToList(),
                        Fiefs = PartitionSuccessionBehavior.SnapshotEstateFiefs(ordinaryFiefs),
                        PrimaryFiefId = ordinaryFiefs.FirstOrDefault()?.Settlement.StringId
                    });
                else
                {
                    var plan = FeudalInheritancePlanner.BuildPlan(source, predecessor, ordinaryFiefs,
                        record.DomesticHeirs, ordinaryTitles, reserve);
                    foreach (Hero heir in record.DomesticHeirs)
                    {
                        var share = PartitionSuccessionBehavior.SnapshotEstateShare(plan, record.DomesticHeirs[0], heir, partition);
                        if (share == null || share.Titles.Count == 0 && share.Fiefs.Count == 0) continue;
                        share.DomesticOnly = true;
                        record.Shares.Add(share);
                    }
                }
            }
            var paidHeirs = new HashSet<Hero>();
            foreach (var share in record.Shares)
                share.ReceivesGold = !share.IsEscheat && paidHeirs.Add(share.Heir);
            return HasCompleteManifest(record, estateTitles, fiefs.Select(f => f.Settlement.StringId)) ? record : null;
        }

        internal static bool HasCompleteManifest(CrossClanEstateRecord record,
            IEnumerable<FeudalTitleRecord> titles, IEnumerable<string> settlements)
        {
            var assignedTitles = record.Shares.SelectMany(s => s.Titles).ToList();
            var assignedFiefs = record.Shares.SelectMany(s => s.Fiefs).ToList();
            return assignedTitles.Distinct(StringComparer.Ordinal).Count() == assignedTitles.Count
                && assignedFiefs.Distinct(StringComparer.Ordinal).Count() == assignedFiefs.Count
                && new HashSet<string>(assignedTitles.Where(id => id != record.RealmCrownTitleId), StringComparer.Ordinal)
                    .SetEquals(titles.Where(t => t.TitleId != record.RealmCrownTitleId).Select(t => t.TitleId))
                && new HashSet<string>(assignedFiefs, StringComparer.Ordinal).SetEquals(settlements);
        }
    }
}
