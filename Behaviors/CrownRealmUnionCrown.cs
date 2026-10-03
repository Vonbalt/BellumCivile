using System;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        internal bool TryTransferRealmUnionCrown(CrownAccessionRecord accession, out string reason)
        {
            reason = "Crown transfer requires a registered absorption with reconciled obligations";
            var journal = accession?.Union;
            if (journal == null || accession.Completed || journal.Completed || journal.RetirementStarted
                || _accessions == null || !_accessions.Contains(accession) || !journal.ObligationsSettled
                || journal.Source == null || journal.Source.IsEliminated
                || journal.Destination == null || journal.Destination.IsEliminated || journal.Source == journal.Destination
                || journal.Source != accession.Realm || journal.Heir != accession.Heir
                || journal.Source.RulingClan != journal.PreviousHouse
                || journal.Destination.RulingClan != journal.SurvivingHouse
                || journal.SurvivingHouse?.Kingdom != journal.Destination
                || journal.Heir?.IsAlive != true || journal.Heir.IsDisabled || journal.Heir.Clan != journal.SurvivingHouse
                || (RegencyBehavior.Instance?.GetLegalClanHead(journal.SurvivingHouse) ?? journal.SurvivingHouse.Leader) != journal.Heir)
                return false;
            if (!TryVerifyRealmUnionObligations(accession, out reason)) return false;
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles == null) { reason = "title service unavailable"; return false; }
            string details = null;
            bool Verify(bool transferred) => RealmUnionCrownTransfer.VerifyTitles(journal,
                journal.PreviousHouse?.StringId, journal.SurvivingHouse.StringId, titles.GetTitle, transferred, out details)
                && titles.GetKingdomPoliticalTitle(journal.Destination)?.TitleId == journal.PrimaryCrownId
                && titles.GetKingdomPoliticalTitle(journal.Source)?.TitleId == journal.InheritedCrownId;
            bool result = RealmUnionCrownTransfer.Execute(journal, () => Verify(false),
                () => titles.ApplyRealmUnionCrownInheritance(journal), () => Verify(true), out reason);
            if (!result)
            {
                if (details != null) reason += ": " + details;
                journal.PendingReason = reason;
            }
            return result;
        }
    }
}
