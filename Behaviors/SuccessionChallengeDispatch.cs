using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class SuccessionChallengeBehavior
    {
        // Accepted Crown surrender has its own journal: it must install the challenger,
        // not resolve forced abdication to the first lawful heir instead.
        private void SettleChallengedCrown(SuccessionChallengeRecord record)
        {
            if (!_settling.Add(record)) return;
            try
            {
                if (record.Phase != SuccessionChallengePhase.CrownSettlement) return;
                var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
                var dynasty = Campaign.Current.GetCampaignBehavior<DynasticHeirBehavior>();
                var resolution = Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>();
                if (titles == null || dynasty == null || resolution == null) return;
                if (!CanSettleChallengedCrown(record))
                {
                    record.Failure = "Crown surrender participants or inheritance unavailable";
                    return;
                }
                if (!record.CrownTransferStarted)
                {
                    // Check every promised asset before the first household mutation.
                    if (record.CrownEstate != null && !CrownEstateAvailable(record.CrownEstate)) return;
                    record.CrownTransferStarted = true;
                }
                if (record.CrownEstate != null
                    && CrownAccessionBehavior.Instance?.SettleCrownCadet(record.CrownEstate) != true) return;
                Clan receivingHouse = record.Challenger.Clan;
                if (receivingHouse?.Leader != record.Challenger || receivingHouse.Kingdom != record.Realm) return;
                if (record.Realm.RulingClan != receivingHouse)
                {
                    resolution.ClearSuccessionStateForKingdom(record.Realm, "accepted dynastic Crown challenge");
                    dynasty.MarkAsUsurper(record.Realm);
                    record.Realm.RulingClan = receivingHouse;
                }
                if (record.Realm.RulingClan != receivingHouse || record.Realm.Leader != record.Challenger) return;
                // Recheck the actual title on recovery rather than trusting an earlier callback.
                if (titles.TrySetKingdomTitleRuler(record.Realm, receivingHouse, legalTransfer: true,
                    reason: "accepted dynastic Crown challenge") != true) return;
                var crown = titles.GetRealmSovereignTitle(record.Realm);
                if (crown == null || crown.DeJureHolderClanId != receivingHouse.StringId
                    || crown.DeFactoHolderClanId != receivingHouse.StringId) return;
                record.CrownTitleTransferred = true;
                dynasty.RefreshSuccessionAfterLawChange(record.Realm);
                HereditaryLoyaltyBehavior.Instance?.Observe(record.Realm, false);
                record.LoyaltyEnded = true;
                record.Phase = SuccessionChallengePhase.Settled;
                record.ReportPending = false;
                record.Failure = null;
                if (!record.CrownAnnounced)
                {
                    record.CrownAnnounced = true;
                    var text = new TextObject("{=BC_Challenge_CrownSurrender}{RULER} has yielded the Crown of {REALM} to {HEIR}, ending the dynastic challenge without civil war.");
                    SetSubjects(text, record);
                    BellumCivileNotifications.Show(text, BellumNotificationColors.Inheritance,
                        primaryKingdom: record.Realm, primaryClan: receivingHouse, isMajorEvent: true);
                }
            }
            catch (Exception ex)
            {
                record.Failure = "Crown surrender deferred: " + ex.Message;
                BellumCivileLogger.Log($"Succession Crown settlement pending; crisis={record.Id}; error={ex}");
            }
            finally { _settling.Remove(record); }
        }

        private static bool CanSettleChallengedCrown(SuccessionChallengeRecord record)
        {
            if (record.Realm?.IsEliminated != false || !Available(record.Challenger)
                || !CrownAccessionBehavior.IsHereditaryRealm(record.Realm)
                || CrownAccessionBehavior.Instance?.IsPending(record.Realm) == true) return false;
            if (!record.CrownTransferStarted && !ValidParticipants(record)) return false;
            Clan receiving = record.CrownEstate?.Cadet ?? record.OriginalHouse;
            bool installed = record.CrownTransferStarted && record.Realm.RulingClan == receiving
                && receiving?.Leader == record.Challenger;
            if (!installed && (record.Sovereign?.IsAlive != true || Sovereign(record.Realm) != record.Sovereign)) return false;
            if (record.Challenger.Clan?.Kingdom != record.Realm
                || (record.Challenger.Clan != record.OriginalHouse && record.Challenger.Clan != record.CrownEstate?.Cadet)) return false;
            if (record.CrownEstate == null) return record.OriginalHouse?.Leader == record.Challenger;
            if (record.CrownEstate.Heir != record.Challenger || record.CrownEstate.EndowmentDonor != record.Sovereign) return false;
            return record.CrownEstate.EndowmentSettled || CrownEstateAvailable(record.CrownEstate);
        }

        private static bool CrownEstateAvailable(CrownAccessionRecord estate)
        {
            Clan source = estate.EndowmentHouse;
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            return source?.IsEliminated == false && source.Leader == estate.EndowmentDonor
                && estate.EndowmentDonor?.IsAlive == true && titles != null
                && estate.Household.Select(CrownAccessionBehavior.ResolveAbdicationHero).All(h => Available(h)
                    && h != Hero.MainHero && (h.Clan == source || h.Clan == estate.Cadet))
                && estate.EndowmentFiefs.Except(estate.DeliveredFiefs).Select(Settlement.Find).All(s => s != null
                    && s.SiegeEvent == null && s.Party?.MapEvent == null && (s.OwnerClan == source || s.OwnerClan == estate.Cadet))
                && estate.EndowmentTitles.Except(estate.DeliveredTitles).Select(titles.GetTitle).All(t => t?.IsActive == true
                    && (t.DeJureHolderClanId == source.StringId || t.DeJureHolderClanId == estate.Cadet?.StringId));
        }

        private static double TransferableHeirPower(SuccessionChallengeRecord record, double loyalistPower)
        {
            var party = record.Challenger.PartyBelongedTo;
            if (record.Challenger.Clan != record.Realm.RulingClan
                || !PartitionSuccessionBehavior.CanRetainChallengeParty(record.Challenger, record.OriginalHouse, party)) return 0;
            return SuccessionChallengeRules.TransferablePower(party.Party.EstimatedStrength,
                RebellionPowerHelper.GetClanMilitaryStrength(record.OriginalHouse), loyalistPower);
        }

        private static bool HasCoalitionStronghold(SuccessionChallengeRecord record) =>
            record.Backers.Any(c => c.Fiefs.Any(f => f.IsTown || f.IsCastle))
            || record.MilitaryEstate?.EndowmentFiefs.Select(Settlement.Find).Any(s => s?.OwnerClan == record.OriginalHouse
                && (s.IsTown || s.IsCastle) && s.SiegeEvent == null && s.Party?.MapEvent == null) == true;

        private bool RefreshResponseBacking(SuccessionChallengeRecord record)
        {
            if (!PledgeParticipantsUnchanged(record)) return false;
            double backing = record.Backers.Sum(c => (double)Math.Max(0, RebellionPowerHelper.CalculateClanPower(c)));
            double loyal = record.Loyalists.Sum(c => (double)Math.Max(0, RebellionPowerHelper.CalculateClanPower(c)));
            double transferred = TransferableHeirPower(record, loyal);
            record.CreditedHeirPartyPower = transferred;
            record.BackingPower = backing + transferred;
            record.LoyalistPower = loyal - transferred;
            record.RequiredRatio = RebellionPowerHelper.CalculateRebellionPowerThreshold(record.Challenger);
            if (SuccessionChallengeRules.CanProceed(record.BackingPower, record.LoyalistPower, record.RequiredRatio,
                record.Challenger == Hero.MainHero, HasCoalitionStronghold(record))) return true;
            WithdrawForInsufficientBacking(record);
            return false;
        }

        private static void WithdrawForInsufficientBacking(SuccessionChallengeRecord record)
        {
            record.PersonalBlockedUntil = record.RealmBlockedUntil = Day + CampaignTime.Years(2).ToDays;
            record.Phase = SuccessionChallengePhase.Withdrawn;
            record.ReportPending = record.Realm == Clan.PlayerClan?.Kingdom;
        }
    }
}
