using System;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class CourtTermPreparationRecord
    {
        [SaveableField(1)] public string RealmId;
        [SaveableField(2)] public double StartDay;
        [SaveableField(3)] public double EndDay;
        [SaveableField(4)] public float TermDays;
        [SaveableField(5)] public int DeliberationDays;

        internal int NominationDays => CourtAgendaRules.NominationDays(TermDays);
        internal double LatestPreparationDay => Math.Max(StartDay, EndDay - NominationDays - 1 - DeliberationDays);
        internal bool HasPreparationWindow(double now) => now <= LatestPreparationDay + 1e-6
            || NominationDays + 1 + DeliberationDays > TermDays && Math.Floor(now) == Math.Floor(StartDay);
        internal bool IsCurrent(double now) => !string.IsNullOrEmpty(RealmId) && TermDays > 0
            && !float.IsInfinity(TermDays) && DeliberationDays >= 0
            && !double.IsInfinity(StartDay) && !double.IsInfinity(EndDay)
            && StartDay <= now && now < EndDay;
    }
}
