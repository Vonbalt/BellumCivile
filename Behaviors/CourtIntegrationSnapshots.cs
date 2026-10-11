using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public partial class CourtAgendaBehavior
    {
        private IEnumerable<CourtAgendaRecord> IntegrationRecords() =>
            _agendas.Concat(_recentResults).Concat(_rallies).Concat(_claimGrace).Where(a => a != null).Distinct();

        private void RestoreIntegrationIds()
        {
            foreach (var record in IntegrationRecords())
                if (string.IsNullOrEmpty(record.IntegrationId)) record.IntegrationId = Guid.NewGuid().ToString("N");
        }

        internal IReadOnlyList<CourtAgendaSnapshot> GetIntegrationSnapshots(Kingdom realm) =>
            IntegrationRecords().Where(a => realm != null && a.Realm == realm)
                .Select(a => new CourtAgendaSnapshot(a)).ToList().AsReadOnly();
    }
}
