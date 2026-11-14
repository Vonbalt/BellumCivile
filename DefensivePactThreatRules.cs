using System;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    // Pure threat arithmetic; the caller owns persistence and rate-change notifications.
    internal static class DefensivePactThreatRules
    {
        internal const double MaximumThreat = 100d;
        internal const double DaysPerInterval = 7d;
        internal const double MaximumTreatyGain = 50d;
        internal const string ChancellorAssignmentId = "chancellor_reassure_realms";

        internal static double ChancellorEffect(bool active, double competence)
        {
            if (!active || !Finite(competence)) return 0d;
            return Math.Max(0d, Math.Min(1d, competence / 100d));
        }

        internal static double WeeklyDecay(double baseDecay, int fiefs, double chancellorEffect)
        {
            if (!Finite(baseDecay) || baseDecay < 0 || fiefs < 0 || !Finite(chancellorEffect)
                || chancellorEffect < 0 || chancellorEffect > 1)
                throw new ArgumentOutOfRangeException();
            return baseDecay * (1d + chancellorEffect) / Math.Min(4d, 1d + fiefs * .1d);
        }

        internal static double ValueAt(double value, double sinceDay, double weeklyDecay, double now)
        {
            if (!Finite(value) || value < 0 || value > MaximumThreat || !Finite(sinceDay) || sinceDay < 0
                || !Finite(now) || now < sinceDay || !Finite(weeklyDecay) || weeklyDecay < 0)
                throw new ArgumentOutOfRangeException();
            return Math.Max(0d, value - (now - sinceDay) * weeklyDecay / DaysPerInterval);
        }

        internal static double ApplyDelta(double current, double delta)
        {
            if (!Finite(current) || current < 0 || current > MaximumThreat || !Finite(delta))
                throw new ArgumentOutOfRangeException();
            return Math.Max(0d, Math.Min(MaximumThreat, current + delta));
        }

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    // Never updates during reads: writes settle the previous rate before changing it.
    internal sealed class DefensivePactThreatLedger
    {
        [SaveableField(1)] private string _realmId;
        [SaveableField(2)] private double _value;
        [SaveableField(3)] private double _updatedDay;
        [SaveableField(4)] private double _weeklyDecay;

        internal string RealmId => _realmId;
        internal double Value => _value;
        internal double UpdatedDay => _updatedDay;
        internal double WeeklyDecay => _weeklyDecay;

        internal DefensivePactThreatLedger(string realmId, double value, double day, double weeklyDecay)
        {
            if (string.IsNullOrWhiteSpace(realmId)) throw new ArgumentException(nameof(realmId));
            DefensivePactThreatRules.ValueAt(value, day, weeklyDecay, day);
            _realmId = realmId;
            _value = value;
            _updatedDay = day;
            _weeklyDecay = weeklyDecay;
        }

        internal double Peek(double now) => DefensivePactThreatRules.ValueAt(_value, _updatedDay, _weeklyDecay, now);

        internal void ChangeRate(double now, double weeklyDecay)
        {
            double current = Peek(now);
            DefensivePactThreatRules.ValueAt(current, now, weeklyDecay, now);
            _value = current;
            _updatedDay = now;
            _weeklyDecay = weeklyDecay;
        }

        internal void Adjust(double now, double delta)
        {
            double current = Peek(now);
            double adjusted = DefensivePactThreatRules.ApplyDelta(current, delta);
            _value = adjusted;
            _updatedDay = now;
        }
    }
}
