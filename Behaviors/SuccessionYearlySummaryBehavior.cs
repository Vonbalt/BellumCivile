using System;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    public class SuccessionYearlySummaryBehavior : CampaignBehaviorBase
    {
        private int _summaryYearIndex = -1;
        private SuccessionYearlySummary _yearlySummary = new SuccessionYearlySummary();

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        public void RecordRoyalHeiressCadetBranch(Clan cadetClan, Clan dynastyClan, Kingdom kingdom)
        {
            FlushSummaryIfYearChanged();
            _yearlySummary.RecordRoyalHeiressCadetBranch(cadetClan, dynastyClan, kingdom);
        }

        public void RecordPartitionCadetBranch(Clan cadetClan, Clan parentClan, Kingdom kingdom, Town fief)
        {
            FlushSummaryIfYearChanged();
            _yearlySummary.RecordPartitionCadetBranch(cadetClan, parentClan, kingdom, fief);
        }

        public void RecordPartitionGoldTransfer(int amount)
        {
            if (amount <= 0)
                return;

            FlushSummaryIfYearChanged();
            _yearlySummary.RecordPartitionGoldTransfer(amount);
        }

        public void RecordSovereignPartition(Kingdom successorKingdom, FeudalTitleRecord sourceTitle)
        {
            if (successorKingdom == null || sourceTitle == null)
                return;

            FlushSummaryIfYearChanged();
            _yearlySummary.RecordSovereignPartition(successorKingdom, sourceTitle);
        }

        public void RecordRegencyStarted(bool generatedRegent)
        {
            FlushSummaryIfYearChanged();
            _yearlySummary.RecordRegencyStarted(generatedRegent);
        }

        public void RecordRegentReplaced(bool generatedRegent)
        {
            FlushSummaryIfYearChanged();
            _yearlySummary.RecordRegentReplaced(generatedRegent);
        }

        public void RecordRegencyEnded()
        {
            FlushSummaryIfYearChanged();
            _yearlySummary.RecordRegencyEnded();
        }

        private void OnDailyTick()
        {
            FlushSummaryIfYearChanged();
        }

        private void FlushSummaryIfYearChanged()
        {
            int daysPerYear = GetCampaignDaysInYear();
            int currentYearIndex = (int)(CampaignTime.Now.ToDays / daysPerYear);
            if (_summaryYearIndex < 0)
            {
                _summaryYearIndex = currentYearIndex;
                return;
            }

            if (currentYearIndex == _summaryYearIndex)
                return;

            if (_yearlySummary.HasActivity)
                BellumCivileDebug.TraceYearlyReport(
                    "succession",
                    _yearlySummary.Format(_summaryYearIndex, daysPerYear));

            _yearlySummary = new SuccessionYearlySummary();
            _summaryYearIndex = currentYearIndex;
        }

        private static int GetCampaignDaysInYear()
        {
            return Math.Max(1, CampaignTime.DaysInYear > 0 ? CampaignTime.DaysInYear : C.MarriageStrategyDaysPerYear);
        }

        private sealed class SuccessionYearlySummary
        {
            private int _royalHeiressCadets;
            private int _partitionCadets;
            private int _partitionFiefsTransferred;
            private int _partitionGoldTransferred;
            private int _sovereignPartitions;
            private int _regenciesStarted;
            private int _generatedRegents;
            private int _regentsReplaced;
            private int _regenciesEnded;
            private string _lastSovereignPartition = "none";
            private string _largestPartitionGrant = "none";
            private float _largestPartitionProsperity;

            public bool HasActivity => _royalHeiressCadets > 0
                || _partitionCadets > 0
                || _partitionGoldTransferred > 0
                || _sovereignPartitions > 0
                || _regenciesStarted > 0
                || _regentsReplaced > 0
                || _regenciesEnded > 0;

            public void RecordRoyalHeiressCadetBranch(Clan cadetClan, Clan dynastyClan, Kingdom kingdom)
            {
                if (cadetClan == null)
                    return;

                _royalHeiressCadets++;
            }

            public void RecordPartitionCadetBranch(Clan cadetClan, Clan parentClan, Kingdom kingdom, Town fief)
            {
                if (cadetClan == null)
                    return;

                _partitionCadets++;
                if (fief != null)
                    _partitionFiefsTransferred++;

                float prosperity = fief?.Prosperity ?? 0f;
                if (prosperity > _largestPartitionProsperity)
                {
                    _largestPartitionProsperity = prosperity;
                    _largestPartitionGrant = $"{cadetClan.StringId}->{fief.StringId}";
                }
            }

            public void RecordPartitionGoldTransfer(int amount)
            {
                _partitionGoldTransferred += Math.Max(0, amount);
            }

            public void RecordSovereignPartition(Kingdom successorKingdom, FeudalTitleRecord sourceTitle)
            {
                if (successorKingdom == null || sourceTitle == null)
                    return;

                _sovereignPartitions++;
                _lastSovereignPartition = $"{successorKingdom.StringId}:{sourceTitle.TitleId}";
            }

            public void RecordRegencyStarted(bool generatedRegent)
            {
                _regenciesStarted++;
                if (generatedRegent)
                    _generatedRegents++;
            }

            public void RecordRegentReplaced(bool generatedRegent)
            {
                _regentsReplaced++;
                if (generatedRegent)
                    _generatedRegents++;
            }

            public void RecordRegencyEnded()
            {
                _regenciesEnded++;
            }

            public string Format(int yearIndex, int daysPerYear)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"yearly succession summary: campaign year {yearIndex + 1} ({daysPerYear} days/year)");
                sb.AppendLine($"cadet_branches_created={_royalHeiressCadets + _partitionCadets}; royal_heiress={_royalHeiressCadets}; partition={_partitionCadets}");
                sb.AppendLine($"partition_fiefs_transferred={_partitionFiefsTransferred}; partition_gold_transferred={_partitionGoldTransferred}; largest_partition_grant={_largestPartitionGrant} prosperity={_largestPartitionProsperity:0}");
                sb.AppendLine($"coequal_sovereign_partitions={_sovereignPartitions}; latest={_lastSovereignPartition}");
                sb.AppendLine($"regencies_started={_regenciesStarted}; generated_regents={_generatedRegents}; regents_replaced={_regentsReplaced}; regencies_ended={_regenciesEnded}");
                return sb.ToString().TrimEnd();
            }
        }
    }
}
