using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        internal bool TryFinishRealmUnion(CrownAccessionRecord accession, out string reason)
        {
            reason = "union completion requires a registered journal";
            var journal = accession?.Union;
            if (journal == null || _accessions?.Contains(accession) != true) return false;
            string details = null;
            bool Cleanup()
            {
                var factions = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
                if (factions == null) return false;
                var retiredCourt = factions.GetFactionsInKingdom(journal.Source);
                if (retiredCourt.Any(f => !f.IsIdeology || CivilWarConflictBehavior.IsFactionTransferPending(f))) return false;
                foreach (var faction in retiredCourt) factions.RemoveFaction(faction);
                return factions.GetFactionsInKingdom(journal.Source).Count == 0;
            }
            bool result = RealmUnionCompletion.Execute(accession,
                () => TryValidateRealmUnionRetirementState(accession, true, out details), Cleanup,
                () =>
                {
                    var message = new TextObject("{=BC_RealmUnion_Inherited}{HEIR} has inherited the Crown of {SOURCE}. Its houses now stand united with {DESTINATION} under one sovereign, while the inherited Crown and its vassal titles remain distinct.");
                    message.SetTextVariable("HEIR", journal.Heir.Name);
                    message.SetTextVariable("SOURCE", journal.Source.Name);
                    message.SetTextVariable("DESTINATION", journal.Destination.Name);
                    BellumCivileLogger.Log($"Hereditary realm union completed; source={journal.Source.StringId}; destination={journal.Destination.StringId}; heir={journal.Heir.StringId}; crown={journal.InheritedCrownId}.");
                    BellumCivileNotifications.Show(message, BellumNotificationColors.Inheritance,
                        primaryKingdom: journal.Destination, secondaryKingdom: journal.Source,
                        primaryClan: journal.SurvivingHouse, secondaryClan: journal.PreviousHouse, isMajorEvent: true);
                }, out reason);
            if (!result)
            {
                if (!string.IsNullOrEmpty(details)) reason += ": " + details;
                journal.PendingReason = reason;
            }
            return result;
        }
    }
}
