using System;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        internal static bool CanUseExistingCrownHouse(Hero heir, Hero leader, Hero legalHead) =>
            heir != null && (heir == leader || heir == legalHead);

        private bool PrepareIncomingCrownHouse(CrownAccessionRecord record)
        {
            if (record.Realm.RulingClan != record.PreviousHouse && record.Realm.RulingClan != record.Cadet
                && record.Realm.RulingClan != record.Heir?.Clan
                && !(record.ForcedAbdication && record.Realm.RulingClan == record.SettlementRulingHouse))
                return DeferAbdication(record, "a different house has taken the Crown during settlement");
            // A partially delivered estate must finish even if the founder is already
            // leading the new clan. Do not recalculate its package on hourly retries.
            if (record.IncomingHousePrepared) return SettleCrownCadet(record);
            if (record.CreatesCadet && record.EndowmentSettled) return true;
            Hero heir = record.Heir;
            Clan source = heir.Clan;
            Hero legalHead = RegencyBehavior.Instance?.GetLegalClanHead(source) ?? source.Leader;
            if (CanUseExistingCrownHouse(heir, source.Leader, legalHead)) return true;
            if (heir == Hero.MainHero)
                return DeferAbdication(record, "waiting for the player household succession to finish");
            if (source.Leader == null || !source.Leader.IsAlive)
                return DeferAbdication(record, "waiting for the source household succession to finish");
            if (source.Kingdom == null || source.Kingdom.IsEliminated
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(source.Kingdom))
                return DeferAbdication(record, "waiting for the heir's household to return to a permanent realm");
            var partition = Campaign.Current.GetCampaignBehavior<PartitionSuccessionBehavior>();
            if (partition == null) return DeferAbdication(record, "partition service unavailable");
            if (source == record.PreviousHouse && record.Predecessor.IsDead
                && ((record.Started + CampaignTime.Hours(1f)).IsFuture
                    || BellumCivileOptions.EnablePartitionSuccession
                        && partition.HasPendingInheritance(source, record.Predecessor)))
                return DeferAbdication(record, "waiting for the deceased ruler's household inheritance");

            var draft = new CrownAccessionRecord
            {
                Realm = record.Realm, PreviousHouse = record.PreviousHouse, Heir = heir,
                IncomingSourceHouse = source, IncomingSourceHead = source.Leader
            };
            draft.Household = new[] { heir, heir.Spouse }
                .Where(h => h != null && h.IsAlive && h.Clan == source && h != Hero.MainHero
                    && h != source.Leader && h != legalHead)
                .Distinct().Select(h => h.StringId).ToList();
            // A death estate belongs to the existing household inheritance machinery.
            // An external accession can instead advance an entitled living-house share.
            if (!(source == record.PreviousHouse && record.Predecessor.IsDead)
                && !partition.HasCrossClanShare(heir)
                && legalHead == source.Leader && !HasInheritanceAdvance(source.Leader, heir))
                FillIncomingHouseEndowment(draft);
            draft.CadetName = partition.PreviewAbdicationCadetName(draft);
            record.IncomingSourceHouse = source;
            record.IncomingSourceRealm = source.Kingdom;
            record.IncomingSourceHead = source.Leader;
            record.HeirHouse = source;
            record.CreatesCadet = true;
            record.RequiresRegency = heir.Age < SuccessionLawHelper.GetAgeOfMajority();
            record.CadetId = "bc_partition_accession_" + Guid.NewGuid().ToString("N");
            record.CadetName = draft.CadetName;
            record.Household = draft.Household;
            record.EndowmentFiefs = draft.EndowmentFiefs;
            record.EndowmentTitles = draft.EndowmentTitles;
            record.EndowmentGold = draft.EndowmentGold;
            record.HasLivingEndowment = draft.HasLivingEndowment;
            record.IncomingHousePrepared = true;
            return SettleCrownCadet(record);
        }

        private static void FillIncomingHouseEndowment(CrownAccessionRecord record)
        {
            Clan source = record.EndowmentHouse;
            Hero donor = record.EndowmentDonor;
            var plan = FeudalInheritancePlanner.BuildPlan(source, donor,
                BellumCivileOptions.PartitionSuccessionMainHeirReservedFiefs);
            Hero primary = SuccessionLawHelper.GetLegalSuccessionLine(source, donor,
                SuccessionLawHelper.GetLawsForClan(source)).FirstOrDefault();
            var share = FeudalInheritancePlanner.GetLivingAccessionShare(plan, primary, record.Heir,
                BellumCivileOptions.EnablePartitionSuccession);
            if (share == null) return;
            record.HasLivingEndowment = true;
            int others = BellumCivileOptions.EnablePartitionSuccession
                ? Math.Min(plan.SecondaryPackages.Count, plan.NaturalHeirs.Count(h => h != primary)) : 0;
            record.EndowmentGold = CalculateAbdicationGold(donor.Gold, others);
            record.EndowmentFiefs = share.Fiefs.Select(f => f.Settlement.StringId)
                .OrderBy(id => id, StringComparer.Ordinal).ToList();
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            string crown = titles?.GetKingdomPoliticalTitle(record.Realm)?.TitleId;
            var sourceCrown = source.Kingdom?.RulingClan == source
                ? titles?.GetRealmSovereignTitle(source.Kingdom, FeudalHierarchyMode.DeFacto) : null;
            string sourcePoliticalCrown = source.Kingdom?.RulingClan == source
                ? titles?.GetKingdomPoliticalTitle(source.Kingdom)?.TitleId : null;
            record.EndowmentTitles = share.Titles.Where(t => t.TitleId != crown && t.TitleId != sourcePoliticalCrown
                    && (sourceCrown == null || t.TitleType < sourceCrown.TitleType))
                .Select(t => t.TitleId).OrderBy(id => id, StringComparer.Ordinal).ToList();
        }
    }
}
