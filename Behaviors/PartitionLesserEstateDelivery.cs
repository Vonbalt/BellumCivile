using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        internal bool TryDeliverLesserPartitionShare(PendingPartitionSuccessionRecord pending,
            CrossClanEstateShare share, out string reason)
        {
            reason = "lesser share requires a registered estate and completed Crown hierarchy";
            if (pending?.CrownBatch?.HierarchyVerified != true || pending.EstateShares?.Contains(share) != true
                || share == null || share.Primary || pending.CrownBatch.Recipients.ContainsKey(share.RootTitleId ?? "")
                || pending.CrownBatch.CourtFinalizationStarted) return false;
            if (!TryVerifyCrownBatchWorld(pending, out reason)) return false;
            var source = ResolveClan(pending.ParentClanId);
            var realm = ResolveKingdom(pending.KingdomId);
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (source == null || realm?.RulingClan != source || titles == null || share.Heir?.IsAlive != true
                || share.Titles == null || share.Fiefs == null) return false;
            var snapshot = pending.CrownBatch.Titles.ToDictionary(t => t.TitleId, StringComparer.Ordinal);
            if (share.Titles.Any(id => !snapshot.TryGetValue(id, out var t) || t.Rank >= FeudalTitleType.Kingdom
                || t.LegalHolderId != source.StringId)) return false;
            try
            {
                if (!share.PartitionFounderStarted)
                {
                    if (!IsStillEligibleSecondaryHeir(share.Heir, source, ResolveHero(pending.DeadLeaderId))) return false;
                    var home = Settlement.Find(share.PrimaryFiefId);
                    if (home?.Town == null || home.OwnerClan != source || !share.Fiefs.Contains(home.StringId))
                    { reason = "lesser share has no available inherited residence"; return false; }
                    if (share.CadetPlan == null)
                    {
                        var otherHeirs = new HashSet<Hero>(pending.EstateShares.Where(s => s != share).Select(s => s.Heir));
                        var draft = new CrownAccessionRecord
                        {
                            Realm = realm, PreviousHouse = source, Predecessor = ResolveHero(pending.DeadLeaderId),
                            Heir = share.Heir, CadetId = BuildUniqueClanIdFromSeed(source, share.RootTitleId.Length == 0
                                ? home.StringId : share.RootTitleId, share.Heir), EndowmentFiefs = share.Fiefs.ToList(),
                            Household = new[] { share.Heir, share.Heir.Spouse }.Concat(share.Heir.Children)
                                .Where(h => h != null && h.IsAlive && h.Clan == source && h != source.Leader
                                    && h != Hero.MainHero && !otherHeirs.Contains(h))
                                .Distinct().Select(h => h.StringId).ToList()
                        };
                        draft.CadetName = PreviewAbdicationCadetName(draft);
                        share.CadetPlan = draft;
                    }
                    if (Clan.All.Any(c => c.StringId == share.CadetPlan.CadetId)
                        || !share.CadetPlan.Household.Contains(share.Heir.StringId)
                        || share.CadetPlan.Household.Select(ResolveHero).Any(h => h == null || !h.IsAlive || h.Clan != source
                            || h.IsPrisoner || h.IsTraveling || h.PartyBelongedTo?.MapEvent != null
                            || h.PartyBelongedTo?.SiegeEvent != null || h.PartyBelongedTo?.Army != null))
                    { reason = "lesser household is unavailable or its reserved identity is occupied"; return false; }
                    share.PartitionFounderStarted = true;
                    if (!PrepareAbdicationCadet(share.CadetPlan))
                        throw new InvalidOperationException("lesser household initialization did not complete");
                    share.Recipient = share.CadetPlan.Cadet;
                    titles.MoveCrownHeirClaims(share.Heir, source, share.Recipient);
                    share.PartitionFounderReturned = true;
                    if (!TryVerifyCrownBatchWorld(pending, out reason)) return false;
                }
                if (share.PartitionReceipts == null)
                    share.PartitionReceipts = share.Fiefs.Select(id => new CrownEstateDeliveryRecord { AssetId = id })
                        .Concat(share.Titles.OrderBy(id => snapshot[id].Rank).ThenBy(id => id, StringComparer.Ordinal)
                            .Select(id => new CrownEstateDeliveryRecord { AssetId = id, IsTitle = true })).ToList();
                foreach (var receipt in share.PartitionReceipts)
                {
                    bool ok;
                    if (!receipt.IsTitle)
                    {
                        var settlement = Settlement.Find(receipt.AssetId);
                        ok = receipt.TryDeliver(() => settlement?.OwnerClan == source && settlement.SiegeEvent == null
                                && settlement.Party?.MapEvent == null && !FiefDeliberationBehavior.IsAwaitingAllocation(settlement),
                            () => { if (!titles.TransferInheritedPossession(source, share.Recipient, settlement))
                                throw new InvalidOperationException("inherited possession transfer failed"); },
                            () => settlement?.OwnerClan == share.Recipient, out reason);
                    }
                    else
                    {
                        var title = titles.GetTitle(receipt.AssetId);
                        var before = snapshot[receipt.AssetId];
                        ok = receipt.TryDeliver(() => title?.DeJureHolderClanId == source.StringId,
                            () => titles.LegalizeTitleInheritance(share.Recipient, title, "journaled lesser partition",
                                preserveDeFacto: before.ActualHolderId != source.StringId),
                            () => title?.DeJureHolderClanId == share.Recipient.StringId
                                && title.DeFactoHolderClanId == (before.ActualHolderId == source.StringId
                                    ? share.Recipient.StringId : before.ActualHolderId), out reason);
                    }
                    if (!ok || !TryVerifyCrownBatchWorld(pending, out reason)) return false;
                }
                share.LandedSettled = true;
                share.Status = "landed share delivered; whole-estate rewards pending";
                reason = null;
                return true;
            }
            catch (Exception ex)
            {
                reason = "lesser share delivery interrupted: " + ex.Message;
                share.Status = reason;
                BellumCivileLogger.Log($"Partition lesser share {share.Heir.StringId}: {ex}");
                return false;
            }
        }
    }
}
