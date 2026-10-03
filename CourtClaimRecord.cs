using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CourtClaimRecord
    {
        [SaveableField(1)] public Settlement Fief;
        [SaveableField(2)] public Clan Beneficiary;
        [SaveableField(3)] public Kingdom Target;
        [SaveableField(4)] public string ClaimTitleId;
        [SaveableField(5)] public List<Clan> Members = new List<Clan>();
        [SaveableField(6)] public bool Activated;
        [SaveableField(7)] public double ActivatedDay;
        [SaveableField(8)] public double AcquiredDay = -1;
        [SaveableField(9)] public double LastOwnerChangeDay = -1;
        [SaveableField(10)] public bool PendingAllocation;
        [SaveableField(11)] public int GraceDays;
        [SaveableField(12)] public bool GraceActive;
        [SaveableField(13)] public double GraceUntil;

        internal bool InTerm(double now, double deadline) => Activated && !GraceActive
            && CourtAgendaRules.ObjectiveInWindow(now, ActivatedDay, deadline);
        internal bool AllocationWindow(double now, double deadline) => Activated
            && (GraceActive ? CourtAgendaRules.ObjectiveInWindow(now, deadline, GraceUntil) : InTerm(now, deadline));
    }
}
