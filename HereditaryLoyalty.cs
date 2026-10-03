using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class HereditaryLoyaltyMemory
    {
        [SaveableField(1)] public Kingdom Realm;
        [SaveableField(2)] public Hero Sovereign;
        [SaveableField(3)] public CampaignTime ReignStart;
        [SaveableField(4)] public bool InitialRuler;
        [SaveableField(5)] public double YearDays;
        [SaveableField(6)] public CampaignTime ShockUntil;
        [SaveableField(7)] public int ShockPoints;
        [SaveableField(8)] public List<Hero> ShockSubjects = new List<Hero>();

        internal double ReignYearsAt(double day) => Math.Floor(Math.Max(0, day - ReignStart.ToDays) / Math.Max(1, YearDays));
        internal int ShockFor(Hero subject, double day) => subject != Sovereign && day < ShockUntil.ToDays
            && ShockSubjects.Contains(subject) ? ShockPoints : 0;
    }

    public sealed class HereditaryLoyaltyAssessment
    {
        public double Honor { get; internal set; }
        public double Mercy { get; internal set; }
        public double PersonalityCap { get; internal set; }
        public double Relations { get; internal set; }
        public double Position { get; internal set; }
        public double Reign { get; internal set; }
        public double Shock { get; internal set; }
        public double Controversy { get; internal set; }
        public bool MinorityRegency { get; internal set; }
        public double Inheritance { get; internal set; }
        public double Concession { get; internal set; }
        public double Submission { get; internal set; }
        public double TestAdjustment { get; internal set; }
        public string InheritanceStatus { get; internal set; }
        public double Raw => 80 + Honor + Mercy + PersonalityCap + Relations + Position + Reign + Shock + Controversy + Inheritance + Concession + Submission + TestAdjustment;
        public double Total => Math.Max(0, Math.Min(100, Raw));
        public bool IsDisloyal => Total < 25;
    }

    internal static class HereditaryLoyaltyRules
    {
        internal static HereditaryLoyaltyAssessment Assess(int honor, int mercy, bool crownHeir,
            bool initialRuler, double reignYears, int shock, double controversy, bool minorityRegency,
            bool dependentAdult, bool hasLandedShare, double dependencyYears, int royalFiefs, int relation = 0)
        {
            double h = Math.Max(-2, Math.Min(2, honor)) * 10;
            double m = Math.Max(-2, Math.Min(2, mercy)) * 5;
            double waiting = Math.Max(0, Math.Floor(dependencyYears));
            return new HereditaryLoyaltyAssessment
            {
                Honor = h, Mercy = m, PersonalityCap = Math.Max(-25, Math.Min(25, h + m)) - h - m,
                Relations = Math.Max(-100, Math.Min(100, relation)) * 0.25,
                Position = crownHeir ? 10 : -10,
                Reign = Math.Min(15, (initialRuler ? 0 : -15) + Math.Max(0, Math.Floor(reignYears))),
                Shock = -Math.Max(0, shock),
                Controversy = -Math.Max(0, controversy) * (minorityRegency ? 2 : 1),
                MinorityRegency = minorityRegency,
                Inheritance = !dependentAdult ? 0 : hasLandedShare
                    ? -Math.Min(waiting, Math.Min(Math.Max(0, royalFiefs) * 0.5, 10)) : -Math.Min(5 + waiting, 15),
                InheritanceStatus = !dependentAdult ? "none" : hasLandedShare ? "waiting" : "excluded"
            };
        }
    }
}
