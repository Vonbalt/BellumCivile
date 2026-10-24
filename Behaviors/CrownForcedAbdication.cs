using System;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        public static bool IsHereditaryRealm(Kingdom realm) => realm != null
            && SuccessionRealmRules.Classify(SuccessionLawHelper.GetLawsForKingdom(realm).SuccessionLaw)
                == RealmSuccessionSystem.Hereditary;

        public bool BeginForcedAbdication(Kingdom realm, Clan formerHouse, Hero monarch, SuccessionLawSet laws,
            string causeId, Hero lawfulHeir = null)
        {
            if (realm == null || realm.IsEliminated || formerHouse == null || monarch == null
                || string.IsNullOrEmpty(causeId)
                || SuccessionRealmRules.Classify(laws.SuccessionLaw) != RealmSuccessionSystem.Hereditary) return false;
            var existing = _accessions.FirstOrDefault(r => r.Realm == realm && r.ForcedCauseId == causeId);
            if (existing != null) { Resolve(existing); return true; }
            if (IsPending(realm)) return false;
            var record = new CrownAccessionRecord
            {
                Realm = realm, PreviousHouse = formerHouse, Predecessor = monarch, Heir = lawfulHeir,
                ForcedCauseId = causeId,
                SettlementRulingHouse = realm.RulingClan, ForcedAbdication = true,
                HouseLaw = laws.SuccessionLaw, GenderLaw = laws.GenderLaw, Started = CampaignTime.Now,
                CadetId = "bc_partition_abdication_" + Guid.NewGuid().ToString("N")
            };
            _accessions.Add(record);
            Resolve(record);
            return true;
        }

        private bool PrepareForcedAbdication(CrownAccessionRecord record)
        {
            bool repair = record.ForcedPrepared && record.CreatesCadet && record.Heir != null
                && record.Household?.Contains(record.Heir.StringId) != true;
            if (record.ForcedPrepared && !repair) return true;
            if (repair && (HasCommittedHouseholdSettlement(record) || record.IncomingHousePrepared))
                return DeferAbdication(record, "the prepared household lacks its founder after settlement began; existing delivery receipts retained");
            if (record.PreviousHouse.Kingdom != record.Realm || record.PreviousHouse.IsEliminated)
                return DeferAbdication(record, "the former house has not rejoined the settlement realm");
            if (record.Predecessor.IsDead)
                return DeferAbdication(record, "the targeted monarch died before the forced settlement was prepared");
            var laws = new SuccessionLawSet(record.GenderLaw, record.HouseLaw);
            Hero heir = record.Heir;
            if (repair && (heir.IsAlive != true || heir.IsDisabled || !RegencyPolicy.CanReceiveCrown(heir)))
                return DeferAbdication(record, "the recorded successor is unavailable for household repair");
            if (!repair)
                record.Heir = heir != null && RegencyPolicy.CanReceiveCrown(heir) ? heir
                    : HereditaryRealmSuccession.GetLine(record.Realm, record.Predecessor, laws).FirstOrDefault();
            record.Emergency = record.Heir == null;
            record.HeirHouse = record.Heir?.Clan;
            record.CreatesCadet = record.HeirHouse == record.PreviousHouse;
            record.RequiresRegency = record.Heir != null && record.Heir.Age < SuccessionLawHelper.GetAgeOfMajority();
            if (record.CreatesCadet)
            {
                // Do not freeze a plan while a regent still leads the source house.
                // The existing transfer guard requires the predecessor to resume leadership first.
                if (record.PreviousHouse.Leader != record.Predecessor || record.Heir == record.Predecessor
                    || record.Heir == Hero.MainHero)
                    return DeferAbdication(record, "the source house is not ready to release the successor's household");
                record.ForcedPrepared = false;
                record.Household = new[] { record.Heir, record.Heir.Spouse }
                    .Where(h => h != null && h.IsAlive && h.Clan == record.PreviousHouse
                        && h != record.Predecessor && h != record.PreviousHouse.Leader && h != Hero.MainHero)
                    .Distinct().Select(h => h.StringId).ToList();
                if (!record.Household.Contains(record.Heir.StringId))
                    return DeferAbdication(record, "the founding heir is unavailable for the new household");
                FillAbdicationEndowment(record);
                var partition = Campaign.Current.GetCampaignBehavior<PartitionSuccessionBehavior>();
                if (partition == null) return DeferAbdication(record, "partition service unavailable");
                record.CadetName = partition.PreviewAbdicationCadetName(record);
            }
            record.ForcedPrepared = true;
            if (repair)
                BellumCivileLogger.Log($"Forced abdication household repaired; realm={record.Realm.StringId}; heir={record.Heir.StringId}; cadet={record.CadetId}; no_transfers_replayed=true.");
            return true;
        }

        public bool HasInheritanceAdvance(Hero predecessor, Hero heir) => predecessor != null && heir != null
            && (SuccessionChallengeBehavior.Instance?.HasInheritanceAdvance(predecessor, heir) == true
                || _accessions.Any(r => (r.IncomingHousePrepared
                    ? r.HasLivingEndowment && (r.DeliveredGold > 0 || r.DeliveredFiefs.Count > 0 || r.DeliveredTitles.Count > 0)
                    : r.IsAbdication)
                && r.EndowmentDonor == predecessor && r.Heir == heir
                && r.CreatesCadet && r.EndowmentSettled));

        internal System.Collections.Generic.IEnumerable<Hero> GetAbdicatedMonarchs(Kingdom realm) =>
            _accessions.Where(r => r.Realm == realm && r.IsAbdication && (r.Completed || r.EndowmentSettled))
                .Select(r => r.Predecessor).Where(h => h != null);

        public bool IsAbdicationSatisfied(FactionObject faction) => faction?.IsHereditaryAbdication == true
            && !IsPending(faction.ParentKingdom)
            && _accessions.Any(r => r.Completed && r.TitleTransferred && !r.RegentReplacement
                && r.Realm == faction.ParentKingdom && r.Predecessor == faction.AbdicationMonarch
                && r.Started.ToDays >= faction.CreationDate.ToDays);

        public bool HasCompletedForcedAccession(Kingdom realm, string causeId) => !string.IsNullOrEmpty(causeId)
            && _accessions.Any(r => r.Completed && r.TitleTransferred && !r.RegentReplacement
                && r.Realm == realm && r.ForcedCauseId == causeId);

        private void SettleSatisfiedAbdicationFactions()
        {
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager == null) return;
            foreach (Kingdom realm in Kingdom.All.Where(k => !k.IsEliminated).ToList())
            {
                foreach (var faction in manager.GetFactionsInKingdom(realm)
                    .Where(IsAbdicationSatisfied).ToList())
                {
                    Kingdom rebel = faction.GetTrackedRebelKingdomIncludingEliminated();
                    if (rebel != null && !rebel.IsEliminated)
                        Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>()?.ResolveWhitePeace(faction, rebel, demandSatisfied: true);
                    else manager.RemoveFaction(faction);
                }
            }
        }

        internal static int CalculateAbdicationGold(int treasury, int otherBeneficiaries) =>
            Math.Max(0, treasury) / (Math.Max(0, otherBeneficiaries) + 2);

        internal static bool DeliverAbdicationGold(CrownAccessionRecord record)
        {
            if (record.GoldCredited) return true;
            if (record.EndowmentGold == 0)
            {
                record.GoldDebited = record.GoldCredited = true;
                return true;
            }
            Hero donor = record.EndowmentDonor;
            if (record.GoldRecipient == null) record.GoldRecipient = record.Cadet?.Leader;
            if (donor == null || record.GoldRecipient == null || record.GoldRecipient == donor) return false;
            // Native ChangeHeroGold has no campaign callbacks. Save each leg before
            // publishing the trade event, whose listeners may throw or trigger saves.
            if (!record.GoldDebited)
            {
                record.DeliveredGold = Math.Min(Math.Min(Math.Max(0, donor.Gold), record.EndowmentGold),
                    int.MaxValue - record.GoldRecipient.Gold);
                donor.ChangeHeroGold(-record.DeliveredGold);
                record.GoldDebited = true;
            }
            record.GoldRecipient.ChangeHeroGold(record.DeliveredGold);
            record.GoldCredited = true;
            CampaignEventDispatcher.Instance.OnHeroOrPartyTradedGold(
                (donor, null), (record.GoldRecipient, null),
                (record.DeliveredGold, "bc_abdication_inheritance"), false);
            return true;
        }
    }
}
