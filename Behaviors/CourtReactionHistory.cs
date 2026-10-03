using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private List<CourtAgendaRecord> _recentResults = new List<CourtAgendaRecord>();

        // History retains completed records, never reintroduces them into the scheduler.
        private void RecordResultHistory(CourtAgendaRecord agenda, float shock)
        {
            if (agenda.Faction == null || shock == 0 || _recentResults.Contains(agenda)) return;
            agenda.ResultVisibleUntil = CampaignTime.Now + CampaignTime.Days(Math.Abs(shock));
            _recentResults.Add(agenda);
        }

        internal static bool? ReactionApproval(CourtAgendaRecord agenda)
        {
            if (agenda?.ResultApplied != true) return null;
            if (agenda.IsPolicy)
                return agenda.State == CourtAgendaState.Passed ? true
                    : agenda.State == CourtAgendaState.Defeated ? (bool?)false : null;
            switch (agenda.ObjectiveData?.State)
            {
                case CourtObjectiveState.Succeeded: return true;
                case CourtObjectiveState.Failed:
                case CourtObjectiveState.Expired: return false;
                default: return null;
            }
        }

        private void SyncReactionHistory(IDataStore dataStore)
        {
            dataStore.SyncData("BC_CourtRecentResults", ref _recentResults);
            _recentResults = _recentResults ?? new List<CourtAgendaRecord>();
            if (dataStore.IsLoading)
                foreach (var agenda in _agendas.Concat(_rallies).Concat(_claimGrace).Distinct())
                    if (ReactionApproval(agenda).HasValue && agenda.ResultVisibleUntil != default(CampaignTime)
                        && !_recentResults.Contains(agenda)) _recentResults.Add(agenda);
        }

        public IEnumerable<CourtAgendaRecord> RecentResults(FactionObject faction) =>
            _recentResults.Where(a => a.Faction == faction && a.Realm == faction?.ParentKingdom
                && a.ResultVisibleUntil.IsFuture && ReactionApproval(a).HasValue).Distinct();

        public string ReactionSubject(CourtAgendaRecord agenda)
        {
            if (!agenda.IsPolicy) return ExecutiveObjectiveText(agenda).ToString();
            return Policy(agenda)?.Name?.ToString() ?? new TextObject("{=BC_CourtHistoryPolicy}Court policy").ToString();
        }
    }
}
