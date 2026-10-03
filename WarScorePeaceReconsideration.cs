using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public partial class WarScoreRecord
    {
        [SaveableField(32)] private float _peaceRetryDay;
        [SaveableField(33)] private float _peaceReconsiderationScore;
        [SaveableField(34)] private List<string> _refusalReactions;
        [SaveableField(37)] private float _peaceFeasibilityRetryDay;
        internal const int PeaceRetryDays = 7;

        internal bool CanReconsiderPeace(float day)
        {
            RefreshPeaceReconsideration();
            return day >= _peaceRetryDay && day >= _peaceFeasibilityRetryDay;
        }

        internal void RecordPeaceDeferral(float day)
        {
            RefreshPeaceReconsideration();
            _peaceFeasibilityRetryDay = day + PeaceRetryDays;
        }

        internal void RecordPeaceRejection(float day)
        {
            RefreshPeaceReconsideration();
            if (_refusalReactions == null) _refusalReactions = new List<string>();
            _peaceRetryDay = day + PeaceRetryDays;
        }

        private void RefreshPeaceReconsideration()
        {
            if (_refusalReactions == null || Math.Abs(Score - _peaceReconsiderationScore) >= 20f)
                ResetPeaceReconsideration();
        }

        private void ResetPeaceReconsideration()
        {
            _peaceRetryDay = 0;
            _peaceFeasibilityRetryDay = 0;
            _peaceReconsiderationScore = Score;
            if (_refusalReactions == null) _refusalReactions = new List<string>();
            else _refusalReactions.Clear();
        }

        internal bool TryRecordRefusalReaction(string ruler, string voter, IEnumerable<TreatyTermRecord> terms)
        {
            RefreshPeaceReconsideration();
            // Stable ordering and rounded financial bands prevent cosmetic redrafts from repeating a grievance.
            var signature = string.Join(";", (terms ?? Enumerable.Empty<TreatyTermRecord>()).Where(t => t != null)
                .Select(t => string.Join("|", (int)t.Type, t.FromKingdomId, t.ToKingdomId, t.ThirdKingdomId,
                    t.SettlementId, t.HeroId, t.SecondaryHeroId, t.ClanId, t.TitleId,
                    t.GoldAmount / 1000, t.DailyGold / 100, t.DurationDays, t.WasVoluntaryOffering))
                .OrderBy(s => s, StringComparer.Ordinal));
            string key = ruler + ":" + voter + ":" + signature;
            if (_refusalReactions.Contains(key)) return false;
            _refusalReactions.Add(key);
            return true;
        }
    }
}
