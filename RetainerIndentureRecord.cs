using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class RetainerIndentureRecord
    {
        [SaveableField(1)] public Hero Holder;
        [SaveableField(2)] public Clan Employer;
        [SaveableField(3)] public double EndDay;
        [SaveableField(4)] public bool RenewalNotified;

        internal bool HasEmployer => Employer != null && !Employer.IsEliminated && Holder?.IsAlive == true
            && Holder.Clan == Employer;
        internal bool ActiveAt(double day) => HasEmployer && Employer == Clan.PlayerClan && EndDay > day
            && !double.IsNaN(EndDay) && !double.IsInfinity(EndDay);
    }
}
