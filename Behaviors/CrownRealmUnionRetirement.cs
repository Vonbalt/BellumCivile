using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        private RealmUnionRecord _realmUnionRetirementAuthorization;

        internal bool TryConsumeRealmUnionRetirementAuthorization(Kingdom realm)
        {
            var journal = _realmUnionRetirementAuthorization;
            if (journal == null || realm == null || realm != journal.Source) return false;
            _realmUnionRetirementAuthorization = null;
            return !journal.Completed && journal.RetirementStarted && !journal.RetirementReturned && !journal.SourceRetired
                && !realm.IsEliminated && _accessions?.Any(a => a?.Union == journal && !a.Completed) == true
                && !_accessions.Any(a => a?.Union != null && a.Union != journal && !a.Union.Completed
                    && (a.Union.Source == realm || a.Union.Destination == realm))
                && !realm.Clans.Any() && !Clan.All.Any(c => c.Kingdom == realm)
                && !realm.Settlements.Any() && !Settlement.All.Any(s => s.OwnerClan?.Kingdom == realm)
                && !realm.Armies.Any();
        }

        internal bool TryRetireRealmUnionSource(CrownAccessionRecord accession, out string reason)
        {
            reason = "retirement requires a registered absorption and no active destruction scope";
            var journal = accession?.Union;
            if (journal == null || accession.Completed || journal.Completed || _accessions?.Contains(accession) != true
                || _realmUnionRetirementAuthorization != null) return false;
            string details = null;
            bool result = RealmUnionRetirement.Execute(journal,
                () => TryValidateRealmUnionForRetirement(accession, out details),
                () =>
                {
                    _realmUnionRetirementAuthorization = journal;
                    try { DestroyKingdomAction.Apply(journal.Source); }
                    finally { _realmUnionRetirementAuthorization = null; }
                },
                () => TryValidateRealmUnionRetirementState(accession, true, out details), out reason);
            if (!result)
            {
                if (!string.IsNullOrEmpty(details)) reason += ": " + details;
                journal.PendingReason = reason;
            }
            return result;
        }
    }
}
