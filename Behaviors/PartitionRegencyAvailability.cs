using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        internal bool TryRetireUnstaffedMinorCadet(CrownAccessionRecord plan)
        {
            Clan cadet = plan?.Cadet;
            if (cadet == null && !string.IsNullOrWhiteSpace(plan?.CadetId))
                plan.Cadet = cadet = Clan.All.FirstOrDefault(c => c.StringId == plan.CadetId);
            if (cadet == null || cadet.IsEliminated || BellumCivileOptions.AllowNewRegencies
                || RegencyPolicy.HasContinuingRegency(plan.Heir)) return true;
            if (cadet.Leader?.IsAlive == true
                && cadet.Leader.Age >= SuccessionLawHelper.GetAgeOfMajority()) return true;

            // A ruling house cannot disappear until its outstanding Crown transfer finishes.
            if (cadet.Kingdom?.RulingClan == cadet && !cadet.Kingdom.IsEliminated)
            {
                if (CrownAccessionBehavior.Instance?.IsPending(cadet.Kingdom) != true) return false;
                CrownAccessionBehavior.Instance.KeepInterregnumHouse(cadet);
                return true;
            }
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles == null) return false;
            bool untouched = plan.DeliveredFiefs.Count == 0 && plan.DeliveredTitles.Count == 0
                && !plan.GoldDebited && cadet.Fiefs.Count == 0
                && titles.GetTitlesHeldByClan(cadet, deJure: true).Count == 0
                && titles.GetTitlesHeldByClan(cadet, deJure: false).Count == 0;
            Clan source = plan.EndowmentHouse;
            if (untouched && source != null && !source.IsEliminated)
            {
                // Interrupted founding can leave a minor as a provisional head before
                // a regent was appointed. Return that unendowed household, not its assets.
                var members = cadet.Heroes.Where(h => h.IsAlive).ToList();
                if (members.Any(h => h == Hero.MainHero || h.IsPrisoner || h.IsTraveling
                    || h.PartyBelongedTo?.MapEvent != null || h.PartyBelongedTo?.SiegeEvent != null)) return false;
                foreach (Hero member in members) TransferHeroToClan(member, cadet, source);
                cadet.SetLeader(null);
            }
            else if (cadet.Heroes.Any(h => h.IsAlive && h.Age >= SuccessionLawHelper.GetAgeOfMajority()))
            {
                if (RegencyPolicy.TryRestoreAdultLeader(cadet)) return true;
            }
            DestroyClanAction.Apply(cadet);
            BellumCivileLogger.Log($"Unstaffed minor cadet retired; clan={cadet.StringId}; household_returned={untouched}; new_regencies_disabled=true.");
            return cadet.IsEliminated;
        }

        private bool ReconcileEstateRegencyAvailability(CrossClanEstateRecord estate)
        {
            if (BellumCivileOptions.AllowNewRegencies) return true;
            var affected = estate.Shares.Where(s => !s.Completed && !s.IsEscheat && s.Heir != null
                && !RegencyPolicy.CanReceiveCrown(s.Heir) && !HasAdultEstateRecipient(s)).ToList();
            if (affected.Count == 0) return CloseExtinctRegencyReversions(estate);
            var pending = _pendingPartitions.FirstOrDefault(p => p.MixedEstate == estate);
            if (pending != null)
            {
                if (!CanRefreshUnstartedMixedEstate(pending)) return false;
                _pendingPartitions.Remove(pending);
            }
            foreach (var share in affected)
            {
                if (share.CadetPlan != null && !TryRetireUnstaffedMinorCadet(share.CadetPlan)) return false;
                Hero previous = share.Heir;
                var laws = new SuccessionLawSet(estate.GenderLaw, estate.SuccessionLaw);
                var candidates = OrderEstateHeirs((share.Recipient ?? previous.Clan)?.Heroes
                        ?? Enumerable.Empty<Hero>(), previous, laws)
                    .Concat(estate.DomesticHeirs ?? new List<Hero>())
                    .Concat(OrderEstateHeirs(estate.Source.Heroes, estate.Deceased, laws));
                Hero successor = candidates.FirstOrDefault(h => h != previous && h.IsAlive
                    && !h.IsDisabled && h.Clan != null && !h.Clan.IsEliminated
                    && RegencyPolicy.CanReceiveCrown(h)
                    && (!share.DomesticOnly || EstateInheritancePolicy.IsDomesticHouse(h.Clan, estate.Realm)));
                // Delivery receipts remain attached to the share. Only its undelivered
                // remainder follows a viable beneficiary or reverts to the Crown.
                share.Heir = successor;
                share.Recipient = null;
                share.CadetPlan = null;
                share.BeneficiaryReconciled = true;
                if (successor == null)
                {
                    share.IsEscheat = true;
                    share.DomesticOnly = true;
                    share.ReceivesGold = false;
                    if (share.GoldPayment != null && !share.GoldPayment.GoldDebited)
                        share.GoldPayment.EndowmentGold = 0;
                }
                ReportEstateShare(estate, share, "new regencies disabled; outstanding share follows "
                    + (successor?.StringId ?? "Crown reversion") + "; existing delivery receipts retained");
            }
            return CloseExtinctRegencyReversions(estate);
        }

        private static bool HasAdultEstateRecipient(CrossClanEstateShare share)
        {
            Clan house = share.Recipient;
            Hero head = house?.Leader;
            // The property belongs to an already established house. Completing this
            // delivery neither appoints a regent nor founds another minor-led cadet.
            return house != null && !house.IsEliminated && share.Heir?.IsAlive == true
                && share.Heir.Clan == house && head?.IsAlive == true
                && head.Age >= SuccessionLawHelper.GetAgeOfMajority()
                && head.DeathMark == KillCharacterAction.KillCharacterActionDetail.None;
        }

        private static bool CloseExtinctRegencyReversions(CrossClanEstateRecord estate)
        {
            Kingdom realm = CrownAccessionBehavior.Instance?.ResolveEstateRealm(estate.Realm) ?? estate.Realm;
            if (realm?.IsEliminated != true) return true;
            // With neither a supported heir nor a surviving Crown, native clan
            // extinction owns the remaining property. Do not retain a dead house forever.
            foreach (var share in estate.Shares.Where(s => !s.Completed && s.IsEscheat))
            {
                share.SupersededFiefs = share.SupersededFiefs ?? new List<string>();
                share.SupersededTitles = share.SupersededTitles ?? new List<string>();
                var payment = share.GoldPayment;
                if (payment?.GoldDebited == true && !payment.GoldCredited
                    && !CrownAccessionBehavior.DeliverAbdicationGold(payment)) return false;
                foreach (string id in share.Fiefs.Except(share.DeliveredFiefs).Except(share.SupersededFiefs).ToList())
                    share.SupersededFiefs.Add(id);
                foreach (string id in share.Titles.Except(share.DeliveredTitles).Except(share.SupersededTitles).ToList())
                    share.SupersededTitles.Add(id);
                share.Completed = true;
                ReportEstateShare(estate, share, "Crown reversion superseded by realm extinction; native clan cleanup owns remaining assets");
            }
            if (estate.Shares.All(s => s.Completed)) estate.Completed = true;
            return true;
        }
    }
}
