using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    public enum RebellionSummaryOutcome
    {
        LoyalistVictory,
        RebelVictory,
        WhitePeace,
        RecognizedIndependence,
        AcceptedUltimatum
    }

    public class RebellionSummaryBehavior : CampaignBehaviorBase
    {
        private int _summaryYearIndex = -1;
        private RebellionYearlySummary _yearlySummary = new RebellionYearlySummary();
        private readonly HashSet<string> _countedFormations = new HashSet<string>();
        private readonly HashSet<string> _countedUltimatums = new HashSet<string>();
        private readonly HashSet<string> _countedCivilWarStarts = new HashSet<string>();
        private readonly HashSet<string> _countedOutcomes = new HashSet<string>();

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        public void RecordFactionFormed(FactionObject faction)
        {
            if (!IsRebelFaction(faction)) return;

            FlushSummaryIfYearChanged();
            string key = BuildFactionKey(faction);
            if (!_countedFormations.Add(key)) return;

            _yearlySummary.RecordFactionFormed(faction);
        }

        public void RecordUltimatumIssued(FactionObject faction)
        {
            if (!IsRebelFaction(faction)) return;

            FlushSummaryIfYearChanged();
            string key = BuildFactionKey(faction);
            if (!_countedUltimatums.Add(key)) return;

            _yearlySummary.RecordUltimatumIssued(faction);
        }

        public void RecordCivilWarStarted(FactionObject faction, Kingdom rebelKingdom)
        {
            if (!IsRebelFaction(faction) || rebelKingdom == null) return;

            FlushSummaryIfYearChanged();
            string key = BuildFactionKey(faction);
            if (!_countedCivilWarStarts.Add(key)) return;

            _yearlySummary.RecordCivilWarStarted(faction, rebelKingdom);
        }

        public void RecordOutcome(FactionObject faction, RebellionSummaryOutcome outcome)
        {
            if (!IsRebelFaction(faction)) return;

            FlushSummaryIfYearChanged();
            string key = $"{BuildFactionKey(faction)}|{outcome}";
            if (!_countedOutcomes.Add(key)) return;

            _yearlySummary.RecordOutcome(faction, outcome);
        }

        public void RecordIntentThresholdCrossed(Clan clan, float score, float threshold)
        {
            if (clan == null) return;

            FlushSummaryIfYearChanged();
            _yearlySummary.RecordIntentThresholdCrossed();
            BellumCivileLogger.Log(
                $"Rebellious intent threshold crossed; clan={clan.StringId}; kingdom={clan.Kingdom?.StringId ?? "none"}; score={score:0.0}; threshold={threshold:0.0}.");
        }

        public void RecordFactionJoined(Clan clan, FactionObject faction)
        {
            if (clan == null || !IsRebelFaction(faction)) return;

            FlushSummaryIfYearChanged();
            _yearlySummary.RecordFactionJoined();
            BellumCivileLogger.Log(
                $"Rebel faction joined; clan={clan.StringId}; faction={faction.Name}; type={faction.Type}; age_days={faction.CreationDate.ElapsedDaysUntilNow:0.0}.");
        }

        public void RecordJoinRejection(string reason)
        {
            FlushSummaryIfYearChanged();
            _yearlySummary.RecordJoinRejection(reason);
        }

        public void RecordFactionLeft(Clan clan, FactionObject faction, string reason)
        {
            if (clan == null || !IsRebelFaction(faction)) return;

            FlushSummaryIfYearChanged();
            _yearlySummary.RecordFactionLeft(reason);
            BellumCivileLogger.Log(
                $"Rebel faction member left; clan={clan.StringId}; faction={faction.Name}; reason={reason ?? "unknown"}; score_review=pulse.");
        }

        public void RecordFactionDisbanded(FactionObject faction, string reason)
        {
            if (!IsRebelFaction(faction)) return;

            FlushSummaryIfYearChanged();
            _yearlySummary.RecordFactionDisbanded(
                reason,
                faction.CreationDate.ElapsedDaysUntilNow,
                faction.PeakDiscontent);
            BellumCivileLogger.Log(
                $"Rebel faction disbanded before war; faction={faction.Name}; type={faction.Type}; reason={reason ?? "unknown"}; age_days={faction.CreationDate.ElapsedDaysUntilNow:0.0}; peak_discontent={faction.PeakDiscontent:0.0}.");
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
            {
                BellumCivileDebug.TraceYearlyReport(
                    "rebellion",
                    _yearlySummary.Format(
                        _summaryYearIndex,
                        daysPerYear,
                        CountActiveRebelFactionsAtYearEnd(grandCoalitions: false),
                        CountActiveRebelFactionsAtYearEnd(grandCoalitions: true),
                        CountActiveCivilWarsAtYearEnd(grandCoalitions: false),
                        CountActiveCivilWarsAtYearEnd(grandCoalitions: true)));
            }

            _yearlySummary = new RebellionYearlySummary();
            _summaryYearIndex = currentYearIndex;
        }

        private static bool IsRebelFaction(FactionObject faction)
        {
            return faction != null && !faction.IsIdeology;
        }

        private static string BuildFactionKey(FactionObject faction)
        {
            if (faction == null) return string.Empty;

            string parentId = faction.ParentKingdom?.StringId ?? "no_parent";
            string leaderId = faction.Leader?.StringId ?? "no_leader";
            int createdDay = (int)faction.CreationDate.ToDays;
            return $"{parentId}|{faction.Type}|{leaderId}|{createdDay}|{faction.Name}";
        }

        private static int GetCampaignDaysInYear()
        {
            return Math.Max(1, CampaignTime.DaysInYear > 0 ? CampaignTime.DaysInYear : C.MarriageStrategyDaysPerYear);
        }

        private static int CountActiveRebelFactionsAtYearEnd(bool grandCoalitions)
        {
            var factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return 0;

            return GetAllActiveFactions(factionManager)
                .Count(f => f != null && !f.IsIdeology && f.IsGrandCoalition == grandCoalitions);
        }

        private static int CountActiveCivilWarsAtYearEnd(bool grandCoalitions)
        {
            var factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return 0;

            return GetAllActiveFactions(factionManager)
                .Count(f =>
                {
                    Kingdom rebelKingdom = f?.GetRebelKingdom();
                    return f != null
                        && !f.IsIdeology
                        && f.IsGrandCoalition == grandCoalitions
                        && f.ParentKingdom != null
                        && rebelKingdom != null
                        && !rebelKingdom.IsEliminated
                        && rebelKingdom.IsAtWarWith(f.ParentKingdom);
                });
        }

        private static IEnumerable<FactionObject> GetAllActiveFactions(FactionManagerBehavior factionManager)
        {
            return Kingdom.All
                .Where(k => k != null && !k.IsEliminated)
                .SelectMany(k => factionManager.GetFactionsInKingdom(k))
                .Distinct();
        }

        private sealed class RebellionYearlySummary
        {
            private readonly Dictionary<FactionType, int> _formedByType = new Dictionary<FactionType, int>();
            private readonly Dictionary<FactionType, int> _ultimatumsByType = new Dictionary<FactionType, int>();
            private readonly Dictionary<FactionType, int> _civilWarsByType = new Dictionary<FactionType, int>();
            private readonly Dictionary<RebellionSummaryOutcome, int> _ordinaryOutcomes = new Dictionary<RebellionSummaryOutcome, int>();
            private readonly Dictionary<RebellionSummaryOutcome, int> _grandCoalitionOutcomes = new Dictionary<RebellionSummaryOutcome, int>();
            private readonly Dictionary<string, int> _joinRejections = new Dictionary<string, int>();
            private readonly Dictionary<string, int> _leaveReasons = new Dictionary<string, int>();
            private readonly Dictionary<string, int> _disbandReasons = new Dictionary<string, int>();

            private int _formed;
            private int _ultimatums;
            private int _civilWars;
            private int _ordinaryFormed;
            private int _ordinaryUltimatums;
            private int _ordinaryCivilWars;
            private int _grandCoalitionsFormed;
            private int _grandCoalitionUltimatums;
            private int _grandCoalitionWars;
            private int _playerInvolvedCivilWars;
            private int _totalRebelMembersAtStart;
            private int _maxRebelMembersAtStart;
            private int _intentThresholdCrossings;
            private int _factionJoins;
            private int _factionLeaves;
            private int _preWarDisbands;
            private float _totalDisbandAgeDays;
            private float _totalDisbandPeakDiscontent;
            private float _maximumDisbandPeakDiscontent;

            public bool HasActivity => _formed > 0
                || _ultimatums > 0
                || _civilWars > 0
                || _intentThresholdCrossings > 0
                || _preWarDisbands > 0
                || _ordinaryOutcomes.Count > 0
                || _grandCoalitionOutcomes.Count > 0;

            public void RecordFactionFormed(FactionObject faction)
            {
                _formed++;
                Increment(_formedByType, faction.Type);
                if (faction.IsGrandCoalition)
                    _grandCoalitionsFormed++;
                else
                    _ordinaryFormed++;
            }

            public void RecordUltimatumIssued(FactionObject faction)
            {
                _ultimatums++;
                Increment(_ultimatumsByType, faction.Type);
                if (faction.IsGrandCoalition)
                    _grandCoalitionUltimatums++;
                else
                    _ordinaryUltimatums++;
            }

            public void RecordCivilWarStarted(FactionObject faction, Kingdom rebelKingdom)
            {
                _civilWars++;
                Increment(_civilWarsByType, faction.Type);

                if (faction.IsGrandCoalition)
                    _grandCoalitionWars++;
                else
                    _ordinaryCivilWars++;

                int rebelMembers = rebelKingdom.Clans.Count(c => c != null && !c.IsEliminated);
                _totalRebelMembersAtStart += rebelMembers;
                _maxRebelMembersAtStart = Math.Max(_maxRebelMembersAtStart, rebelMembers);

                if (Clan.PlayerClan?.Kingdom == rebelKingdom || Clan.PlayerClan?.Kingdom == faction.ParentKingdom || faction.Members.Contains(Clan.PlayerClan))
                    _playerInvolvedCivilWars++;
            }

            public void RecordOutcome(FactionObject faction, RebellionSummaryOutcome outcome)
            {
                Increment(faction.IsGrandCoalition ? _grandCoalitionOutcomes : _ordinaryOutcomes, outcome);
            }

            public void RecordIntentThresholdCrossed()
            {
                _intentThresholdCrossings++;
            }

            public void RecordFactionJoined()
            {
                _factionJoins++;
            }

            public void RecordJoinRejection(string reason)
            {
                Increment(_joinRejections, string.IsNullOrWhiteSpace(reason) ? "unknown" : reason);
            }

            public void RecordFactionLeft(string reason)
            {
                _factionLeaves++;
                Increment(_leaveReasons, string.IsNullOrWhiteSpace(reason) ? "unknown" : reason);
            }

            public void RecordFactionDisbanded(string reason, float ageDays, float peakDiscontent)
            {
                _preWarDisbands++;
                _totalDisbandAgeDays += Math.Max(0f, ageDays);
                _totalDisbandPeakDiscontent += Math.Max(0f, peakDiscontent);
                _maximumDisbandPeakDiscontent = Math.Max(_maximumDisbandPeakDiscontent, peakDiscontent);
                Increment(_disbandReasons, string.IsNullOrWhiteSpace(reason) ? "unknown" : reason);
            }

            public string Format(
                int yearIndex,
                int daysPerYear,
                int activeOrdinaryFactions,
                int activeGrandCoalitions,
                int activeOrdinaryWars,
                int activeGrandCoalitionWars)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"yearly rebellion summary: campaign year {yearIndex + 1} ({daysPerYear} days/year); controversy_model=privy_council_offices");
                sb.AppendLine($"ordinary_rebellions: formed={_ordinaryFormed}; ultimatums={_ordinaryUltimatums}; civil_wars_started={_ordinaryCivilWars}; {FormatOutcomes(_ordinaryOutcomes)}; active_factions_end_year={activeOrdinaryFactions}; active_wars_end_year={activeOrdinaryWars}");
                sb.AppendLine($"grand_coalitions: formed={_grandCoalitionsFormed}; ultimatums={_grandCoalitionUltimatums}; civil_wars_started={_grandCoalitionWars}; {FormatOutcomes(_grandCoalitionOutcomes)}; active_factions_end_year={activeGrandCoalitions}; active_wars_end_year={activeGrandCoalitionWars}");
                sb.AppendLine($"all_rebellions: formed={_formed} ({FormatByType(_formedByType)}); ultimatums={_ultimatums} ({FormatByType(_ultimatumsByType)}); civil_wars_started={_civilWars} ({FormatByType(_civilWarsByType)}); player_involved_civil_wars={_playerInvolvedCivilWars}; avg_rebel_clans_at_start={AverageRebelMembers():0.0}; largest_rebel_side={_maxRebelMembersAtStart}");
                sb.AppendLine($"conspiracy_lifecycle: intent_threshold_crossings={_intentThresholdCrossings}; joins={_factionJoins}; leaves={_factionLeaves} ({FormatStringCounts(_leaveReasons)}); join_rejections=({FormatStringCounts(_joinRejections)}); prewar_disbands={_preWarDisbands} ({FormatStringCounts(_disbandReasons)}); avg_disband_age_days={AverageDisbandAge():0.0}; avg_peak_discontent={AverageDisbandPeakDiscontent():0.0}; max_peak_discontent={_maximumDisbandPeakDiscontent:0.0}");
                return sb.ToString().TrimEnd();
            }

            private float AverageRebelMembers()
            {
                return _civilWars <= 0 ? 0f : _totalRebelMembersAtStart / (float)_civilWars;
            }

            private float AverageDisbandAge()
            {
                return _preWarDisbands <= 0 ? 0f : _totalDisbandAgeDays / _preWarDisbands;
            }

            private float AverageDisbandPeakDiscontent()
            {
                return _preWarDisbands <= 0 ? 0f : _totalDisbandPeakDiscontent / _preWarDisbands;
            }

            private static int GetOutcome(Dictionary<RebellionSummaryOutcome, int> outcomes, RebellionSummaryOutcome outcome)
            {
                return outcomes.TryGetValue(outcome, out int count) ? count : 0;
            }

            private static string FormatOutcomes(Dictionary<RebellionSummaryOutcome, int> outcomes)
            {
                return $"outcomes: loyalist_victory={GetOutcome(outcomes, RebellionSummaryOutcome.LoyalistVictory)}, rebel_victory={GetOutcome(outcomes, RebellionSummaryOutcome.RebelVictory)}, white_peace={GetOutcome(outcomes, RebellionSummaryOutcome.WhitePeace)}, recognized_independence={GetOutcome(outcomes, RebellionSummaryOutcome.RecognizedIndependence)}, accepted_ultimatums={GetOutcome(outcomes, RebellionSummaryOutcome.AcceptedUltimatum)}";
            }

            private static void Increment<TKey>(Dictionary<TKey, int> dictionary, TKey key)
            {
                dictionary[key] = dictionary.TryGetValue(key, out int count) ? count + 1 : 1;
            }

            private static string FormatByType(Dictionary<FactionType, int> counts)
            {
                if (counts.Count == 0) return "none";

                return string.Join(", ", counts
                    .OrderByDescending(kvp => kvp.Value)
                    .ThenBy(kvp => kvp.Key.ToString())
                    .Select(kvp => $"{FormatType(kvp.Key)}:{kvp.Value}"));
            }

            private static string FormatStringCounts(Dictionary<string, int> counts)
            {
                if (counts.Count == 0) return "none";

                return string.Join(", ", counts
                    .OrderByDescending(kvp => kvp.Value)
                    .ThenBy(kvp => kvp.Key)
                    .Select(kvp => $"{kvp.Key}:{kvp.Value}"));
            }

            private static string FormatType(FactionType type)
            {
                switch (type)
                {
                    case FactionType.Independence:
                        return "independence";
                    case FactionType.Abdication:
                        return "abdication";
                    case FactionType.InstallRuler:
                        return "install_ruler";
                    default:
                        return type.ToString().ToLowerInvariant();
                }
            }
        }
    }
}
