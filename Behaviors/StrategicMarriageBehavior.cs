using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Library;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Evaluates one distributed annual marriage opportunity per NPC house.
    /// Native automation is independently controlled by the exclusive-marriage setting.
    /// </summary>
    public partial class StrategicMarriageBehavior : CampaignBehaviorBase
    {
        private int _summaryYearIndex = -1;
        private int _lastPlayerOfferDay = -100000;
        private Dictionary<string, int> _lastHouseEvaluationYear = new Dictionary<string, int>();
        private Dictionary<string, int> _pendingMarriageYear = new Dictionary<string, int>();
        private Dictionary<string, int> _nextMarriageSearchDay = new Dictionary<string, int>();
        private Dictionary<string, int> _marriageSearchCount = new Dictionary<string, int>();
        private MarriageYearlySummary _yearlySummary = new MarriageYearlySummary();
        private static readonly MethodInfo IsThereActiveOfferGetter =
            AccessTools.PropertyGetter(typeof(MarriageOfferCampaignBehavior), "IsThereActiveMarriageOffer");
        private static readonly FieldInfo OfferedPlayer = AccessTools.Field(typeof(MarriageOfferCampaignBehavior), "_currentOfferedPlayerClanHero");
        private static readonly FieldInfo OfferedOther = AccessTools.Field(typeof(MarriageOfferCampaignBehavior), "_currentOfferedOtherClanHero");
        private static readonly FieldInfo LastNativeOffer = AccessTools.Field(typeof(MarriageOfferCampaignBehavior), "_lastMarriageOfferTime");

        internal static bool HasMarriageOfferFor(Hero hero)
        {
            var offers = Campaign.Current?.GetCampaignBehavior<MarriageOfferCampaignBehavior>();
            if (hero == null || offers == null || OfferedPlayer == null || OfferedOther == null) return true;
            return offers.IsHeroEngaged(hero) || OfferedPlayer.GetValue(offers) == hero || OfferedOther.GetValue(offers) == hero;
        }

        internal bool TrySendCourtMarriageOffer(BellumMarriageMatch match)
        {
            if (!CanSendPlayerOffer() || match?.Outcome?.StillMatches() != true
                || HasMarriageOfferFor(match.Suitor) || HasMarriageOfferFor(match.Candidate)) return false;
            TrySendPlayerMarriageOffer(match);
            return HasMarriageOfferFor(match.Suitor) && HasMarriageOfferFor(match.Candidate);
        }
        internal bool CanSendCourtMarriageOffer => CanSendPlayerOffer();

        internal bool HasCourtMarriageOpportunity(Clan house, double deadline)
        {
            if (house == Clan.PlayerClan) return true;
            if (!BellumCivileOptions.EnableBellumStrategicMarriageLogic || house == null) return false;
            return CampaignTime.Now.ToDays + 1 <= deadline;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, OnDailyTickClan);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_LastStrategicPlayerMarriageOfferDay", ref _lastPlayerOfferDay);
            dataStore.SyncData("BellumCivile_LastMarriageHouseEvaluationYear", ref _lastHouseEvaluationYear);
            if (_lastHouseEvaluationYear == null) _lastHouseEvaluationYear = new Dictionary<string, int>();
            dataStore.SyncData("BellumCivile_PendingMarriageYear", ref _pendingMarriageYear);
            dataStore.SyncData("BellumCivile_NextMarriageSearchDay", ref _nextMarriageSearchDay);
            dataStore.SyncData("BellumCivile_MarriageSearchCount", ref _marriageSearchCount);
            if (_pendingMarriageYear == null) _pendingMarriageYear = new Dictionary<string, int>();
            if (_nextMarriageSearchDay == null) _nextMarriageSearchDay = new Dictionary<string, int>();
            if (_marriageSearchCount == null) _marriageSearchCount = new Dictionary<string, int>();
            SyncMarriageProspects(dataStore);
        }

        private void OnDailyTickClan(Clan clan)
        {
            if (!BellumCivileOptions.EnableBellumStrategicMarriageLogic)
                return;

            FlushSummaryIfYearChanged();

            if (ProcessMarriageProspect(clan)) return;

            if (CourtAgendaBehavior.Current?.PursueDynasticMarriage(clan, this) == true) return;

            if (!ShouldEvaluateToday(clan))
                return;

            Hero hero = clan.Heroes.FirstOrDefault(BellumMarriageStrategyHelper.IsStrategicMarriageInitiator)
                ?? clan.Heroes.FirstOrDefault(BellumMarriageStrategyHelper.MarriageProspectEligible);
            if (hero == null) { _yearlySummary.AvailabilityDeferrals++; return; }
            float dynasticNeed = BellumMarriageStrategyHelper.CalculateDynasticNeed(clan);
            int daysPerYear = GetCampaignDaysInYear();
            int year = CurrentDay / daysPerYear;
            bool continuing = _pendingMarriageYear.TryGetValue(clan.StringId, out int pending) && pending == year;
            if (!continuing)
            {
                _lastHouseEvaluationYear[clan.StringId] = year;
                float chance = BellumMarriageStrategyHelper.CalculateAnnualMarriageChance(hero);
                float roll = MBRandom.RandomFloat;
                _yearlySummary.RecordEligibleInitiator(hero, dynasticNeed);
                TraceMarriage($"evaluating {HeroLabel(hero)}; calendarYearDays={daysPerYear}; dynasticNeed={dynasticNeed:0}; annualChance={chance:P0}; roll={roll:P0}.");
                if (chance <= 0f || roll > chance)
                {
                    _yearlySummary.ChanceRollFailed++;
                    CloseMarriageSearch(clan);
                    return;
                }
                _yearlySummary.MarriageAttemptsRolled++;
                _pendingMarriageYear[clan.StringId] = year;
                _marriageSearchCount[clan.StringId] = 0;
            }
            _marriageSearchCount.TryGetValue(clan.StringId, out int searches);
            _marriageSearchCount[clan.StringId] = searches + 1;
            _nextMarriageSearchDay[clan.StringId] = CurrentDay + 3;
            _yearlySummary.Searches++;
            FactionManagerBehavior factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            var evaluationStats = new BellumMarriageEvaluationStats();
            BellumMarriageMatch bestCandidate = BellumMarriageStrategyHelper.FindBestHouseMatch(clan, factionManager, CanSendPlayerOffer(), evaluationStats);
            _yearlySummary.Merge(evaluationStats);
            if (bestCandidate == null)
            {
                _yearlySummary.NoValidMatchFound++;
                if (evaluationStats.LegalPairs == 0) _yearlySummary.NoLegalPairSearches++;
                else _yearlySummary.RefusedPairSearches++;
                _yearlySummary.RecordEndangeredUnmatched(hero.Clan, dynasticNeed);
                TraceMarriage($"no marriage accepted; house={clan.StringId}; legal_pairs={evaluationStats.LegalPairs}; first_refused={evaluationStats.FirstHouseRefused}; second_refused={evaluationStats.SecondHouseRefused}; best_rejected={evaluationStats.BestRejectedScore}; temporary_participants={evaluationStats.TemporaryParticipants}.");
                var refused = evaluationStats.ClosestRefusedMatch;
                if (refused != null) TraceMarriage($"closest refused pair: {HeroLabel(refused.Suitor)} + {HeroLabel(refused.Candidate)}; reasons={FormatReasons(refused)}.");
                if (evaluationStats.LegalPairs > 0 || evaluationStats.TemporaryParticipants == 0) CloseMarriageSearch(clan);
                return;
            }

            TraceMarriage(
                $"best candidate for {HeroLabel(hero)} is {HeroLabel(bestCandidate.Candidate)}; score={bestCandidate.Score:0}; threshold={C.MarriageStrategyMinimumScore:0}; reasons={FormatReasons(bestCandidate)}.");

            BellumMarriageMatch match = bestCandidate.Score >= C.MarriageStrategyMinimumScore ? bestCandidate : null;
            if (match == null)
            {
                CloseMarriageSearch(clan);
                _yearlySummary.BestMatchBelowThreshold++;
                _yearlySummary.RecordRejectedScore(bestCandidate.Score);
                _yearlySummary.RecordEndangeredUnmatched(hero.Clan, dynasticNeed);
                TraceMarriage($"skipped {HeroLabel(hero)}: best score below threshold.");
                return;
            }

            if (!BellumMarriageStrategyHelper.MarriageParticipantReady(match.Suitor)
                || !BellumMarriageStrategyHelper.MarriageParticipantReady(match.Candidate))
            {
                QueueMarriageProspect(clan, match);
                CloseMarriageSearch(clan);
                return;
            }

            CompleteStrategicMatch(clan, match, hero, dynasticNeed);
        }

        private void CompleteStrategicMatch(Clan clan, BellumMarriageMatch match, Hero hero, float dynasticNeed)
        {
            if (!BellumMarriageStrategyHelper.MarriageParticipantReady(match.Suitor)
                || !BellumMarriageStrategyHelper.MarriageParticipantReady(match.Candidate)) return;
            if (match.Outcome?.StillMatches() != true
                || !Campaign.Current.Models.MarriageModel.IsCoupleSuitableForMarriage(match.Suitor, match.Candidate))
            {
                _yearlySummary.VanillaModelRejectedBellumMatch++;
                _yearlySummary.RecordRejectedScore(match.Score);
                _yearlySummary.RecordEndangeredUnmatched(hero.Clan, dynasticNeed);
                TraceMarriage($"skipped {HeroLabel(hero)} + {HeroLabel(match.Candidate)}: household terms or marriage eligibility changed before execution.");
                return;
            }

            if (match.Suitor.Clan == Clan.PlayerClan || match.Candidate.Clan == Clan.PlayerClan)
            {
                if (TrySendCourtMarriageOffer(match)) CloseMarriageSearch(clan);
                return;
            }

            CloseMarriageSearch(clan);
            try
            {
                using (new NpcMarriageClanContext(match.Suitor, match.Candidate, match.Outcome.Destination))
                    MarriageAction.Apply(match.Suitor, match.Candidate);
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Strategic marriage action failed; first={match.Suitor.StringId}; second={match.Candidate.StringId}; error={ex}");
                return;
            }
            if (match.Suitor.Spouse != match.Candidate || match.Candidate.Spouse != match.Suitor
                || match.Suitor.Clan != match.Outcome.Destination || match.Candidate.Clan != match.Outcome.Destination)
            {
                TraceMarriage("marriage outcome did not match its forecast; completion not recorded.");
                return;
            }
            _yearlySummary.RecordCompletedMarriage(match);
            BellumCivileLogger.Log(
                $"Strategic marriage: {match.Suitor.StringId} ({match.Outcome.FirstHouse.StringId}) + {match.Candidate.StringId} ({match.Outcome.SecondHouse.StringId}); score={match.Score:0}; reasons={string.Join(", ", match.Reasons)}.");
            TraceMarriage($"completed strategic marriage: {HeroLabel(match.Suitor)} + {HeroLabel(match.Candidate)}; score={match.Score:0}; reasons={FormatReasons(match)}.");

        }

        private bool TrySendPlayerMarriageOffer(BellumMarriageMatch match)
        {
            if (match?.Suitor?.Clan != Clan.PlayerClan && match?.Candidate?.Clan != Clan.PlayerClan)
                return false;

            float threshold = C.MarriageStrategyMinimumScore;
            if (match.Score < threshold)
            {
                _yearlySummary.BestMatchBelowThreshold++;
                _yearlySummary.RecordRejectedScore(match.Score);
                TraceMarriage($"player-clan match skipped: score={match.Score:0} threshold={threshold:0}; {HeroLabel(match.Suitor)} + {HeroLabel(match.Candidate)}; reasons={FormatReasons(match)}.");
                return true;
            }

            if (CurrentDay - _lastPlayerOfferDay < C.MarriagePlayerOfferCooldownDays)
            {
                TraceMarriage($"player-clan match held by global player-offer cooldown: {HeroLabel(match.Suitor)} + {HeroLabel(match.Candidate)}; score={match.Score:0}; reasons={FormatReasons(match)}.");
                return true;
            }

            MarriageOfferCampaignBehavior offerBehavior = Campaign.Current.CampaignBehaviorManager.GetBehavior<MarriageOfferCampaignBehavior>();
            if (offerBehavior == null)
            {
                TraceMarriage("player-clan match skipped: vanilla MarriageOfferCampaignBehavior was not available.");
                return true;
            }

            if (HasActiveMarriageOffer(offerBehavior))
            {
                TraceMarriage($"player-clan match held: another marriage offer is already active; {HeroLabel(match.Suitor)} + {HeroLabel(match.Candidate)}.");
                return true;
            }

            Hero playerClanHero = match.Suitor.Clan == Clan.PlayerClan ? match.Suitor : match.Candidate;
            Hero otherClanHero = match.Suitor.Clan == Clan.PlayerClan ? match.Candidate : match.Suitor;
            if (playerClanHero == null || otherClanHero == null || playerClanHero.Clan != Clan.PlayerClan || otherClanHero.Clan == Clan.PlayerClan)
                return true;

            if (offerBehavior.IsHeroEngaged(playerClanHero) || offerBehavior.IsHeroEngaged(otherClanHero))
            {
                TraceMarriage($"player-clan match skipped: one hero is already engaged; {HeroLabel(playerClanHero)} + {HeroLabel(otherClanHero)}.");
                return true;
            }

            if (!PlayerMarriageAgreementBehavior.TryCreateOffer(match)) return true;
            _lastPlayerOfferDay = CurrentDay;
            _yearlySummary.RecordPlayerOffer(match);
            BellumCivileLogger.Log(
                $"Strategic player marriage offer: player={playerClanHero.StringId} ({playerClanHero.Clan?.StringId}) other={otherClanHero.StringId} ({otherClanHero.Clan?.StringId}); score={match.Score:0}; reasons={string.Join(", ", match.Reasons)}.");
            TraceMarriage($"sending player offer: {HeroLabel(playerClanHero)} + {HeroLabel(otherClanHero)}; score={match.Score:0}; threshold={threshold:0}; reasons={FormatReasons(match)}.");

            return true;
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
                    "marriage",
                    _yearlySummary.Format(_summaryYearIndex, daysPerYear, CountRulingClansWithLowMarriageReserve()));

            _yearlySummary = new MarriageYearlySummary();
            _summaryYearIndex = currentYearIndex;
        }

        private bool ShouldEvaluateToday(Clan clan)
        {
            if (clan == null || clan == Clan.PlayerClan || clan.IsEliminated || clan.Kingdom == null
                || clan.IsMinorFaction || clan.IsClanTypeMercenary || clan.IsBanditFaction || clan.StringId == "neutral") return false;
            int days = GetCampaignDaysInYear();
            int year = CurrentDay / days;
            if (!MarriageMatchmakingRules.InWindow(CurrentDay, days, MarriageOutcome.AnnualOffset(clan.StringId, days)))
            { CloseMarriageSearch(clan); return false; }
            if (!_lastHouseEvaluationYear.TryGetValue(clan.StringId, out int previous) || previous != year) return true;
            return _pendingMarriageYear.TryGetValue(clan.StringId, out int pending) && pending == year
                && _marriageSearchCount.TryGetValue(clan.StringId, out int count) && count < 3
                && _nextMarriageSearchDay.TryGetValue(clan.StringId, out int next) && CurrentDay >= next;
        }

        private void CloseMarriageSearch(Clan clan)
        {
            _pendingMarriageYear.Remove(clan.StringId);
            _nextMarriageSearchDay.Remove(clan.StringId);
            _marriageSearchCount.Remove(clan.StringId);
        }

        internal void RecordCourtMarriageOpportunity(Clan house, BellumMarriageMatch match, bool married)
        {
            _lastHouseEvaluationYear[house.StringId] = CurrentDay / GetCampaignDaysInYear();
            CloseMarriageSearch(house);
            if (married) _yearlySummary.RecordCompletedMarriage(match);
        }

        private static int GetCampaignDaysInYear()
        {
            return Math.Max(1, CampaignTime.DaysInYear > 0 ? CampaignTime.DaysInYear : C.MarriageStrategyDaysPerYear);
        }

        private bool CanSendPlayerOffer()
        {
            if (CurrentDay - _lastPlayerOfferDay < C.MarriagePlayerOfferCooldownDays) return false;
            var offers = Campaign.Current.GetCampaignBehavior<MarriageOfferCampaignBehavior>();
            return offers != null && !HasActiveMarriageOffer(offers) && LastNativeOffer != null
                && ((CampaignTime)LastNativeOffer.GetValue(offers)).ElapsedDaysUntilNow >= C.MarriagePlayerOfferCooldownDays;
        }

        private static int CurrentDay => (int)CampaignTime.Now.ToDays;

        private static bool HasActiveMarriageOffer(MarriageOfferCampaignBehavior offerBehavior)
        {
            if (offerBehavior == null || IsThereActiveOfferGetter == null)
                return true;

            try
            {
                return IsThereActiveOfferGetter.Invoke(offerBehavior, null) is bool active && active;
            }
            catch
            {
                return true;
            }
        }

        private static void TraceMarriage(string message)
        {
            BellumCivileDebug.TraceIfEnabled("marriage", message, requestInGameDisplay: true);
        }

        private static string HeroLabel(Hero hero)
        {
            if (hero == null)
                return "null";

            return $"{hero.StringId}({hero.Name}, clan={hero.Clan?.StringId ?? "none"})";
        }

        private static string FormatReasons(BellumMarriageMatch match)
        {
            return match?.Reasons != null && match.Reasons.Count > 0
                ? string.Join(", ", match.Reasons)
                : "none";
        }

        private static int CountRulingClansWithLowMarriageReserve()
        {
            return Kingdom.All.Count(k => k != null
                                       && !k.IsEliminated
                                       && BellumMarriageStrategyHelper.MarriagePoliticalRealm(k.RulingClan) == k
                                       && CountUnmarriedAdultNobles(k.RulingClan) < C.MarriageRoyalMinimumSpareAdultsForForeign);
        }

        private static int CountUnmarriedAdultNobles(Clan clan)
        {
            if (clan == null)
                return 0;

            return clan.Heroes.Count(h => BellumMarriageStrategyHelper.HouseholdMarriageProspect(h)
                && (h == clan.Leader || SuccessionLawHelper.IsBloodRelative(h, clan.Leader)));
        }

        private sealed class MarriageYearlySummary
        {
            private readonly HashSet<string> _checkedClans = new HashSet<string>();
            private readonly HashSet<string> _endangeredUnmatchedClans = new HashSet<string>();

            public int EligibleUnmarriedNoblesFound;
            public int MarriageAttemptsRolled;
            public int Searches, AvailabilityDeferrals;
            public int NoLegalPairSearches, RefusedPairSearches;
            public int ChanceRollFailed;
            public int BellumMarriagesCompleted;
            public int PlayerMarriageOffersSent;
            public int VanillaModelRejectedBellumMatch;
            public int NoValidMatchFound;
            public int BestMatchBelowThreshold;
            public int DynasticSurvivalMarriages;
            public int SameRealmPoliticalMarriages;
            public int RoyalVassalPacificationMarriages;
            public int ForeignRoyalAllianceMarriages;
            public int SameCultureMarriages;
            public int CrossCultureMarriages;
            public int AcceptedScoreTotal;
            public float HighestAcceptedScore;
            public float HighestRejectedScore;
            public BellumMarriageEvaluationStats Rejections { get; } = new BellumMarriageEvaluationStats();

            public bool HasActivity => AvailabilityDeferrals > 0 || EligibleUnmarriedNoblesFound > 0
                                    || MarriageAttemptsRolled > 0
                                    || BellumMarriagesCompleted > 0
                                    || PlayerMarriageOffersSent > 0
                                    || NoValidMatchFound > 0
                                    || BestMatchBelowThreshold > 0;

            public void RecordEligibleInitiator(Hero hero, float dynasticNeed)
            {
                EligibleUnmarriedNoblesFound++;
                if (hero?.Clan != null)
                    _checkedClans.Add(hero.Clan.StringId);
            }

            public void Merge(BellumMarriageEvaluationStats stats)
            {
                Rejections.Merge(stats);
            }

            public void RecordCompletedMarriage(BellumMarriageMatch match)
            {
                if (match == null)
                    return;

                BellumMarriagesCompleted++;
                AcceptedScoreTotal += (int)match.Score;
                HighestAcceptedScore = MathF.Max(HighestAcceptedScore, match.Score);

                if (match.HasDynasticContinuity)
                    DynasticSurvivalMarriages++;

                if (match.IsDomestic)
                    SameRealmPoliticalMarriages++;

                if (match.HasRoyalPolitics)
                    RoyalVassalPacificationMarriages++;

                if (match.HasForeignPolitics)
                    ForeignRoyalAllianceMarriages++;

                if (match.Suitor?.Culture != null && match.Suitor.Culture == match.Candidate?.Culture)
                    SameCultureMarriages++;
                else
                    CrossCultureMarriages++;
            }

            public void RecordPlayerOffer(BellumMarriageMatch match)
            {
                PlayerMarriageOffersSent++;
                RecordAcceptedOrOfferedType(match);
            }

            private void RecordAcceptedOrOfferedType(BellumMarriageMatch match)
            {
                if (match == null)
                    return;

                if (match.HasDynasticContinuity)
                    DynasticSurvivalMarriages++;

                if (match.IsDomestic)
                    SameRealmPoliticalMarriages++;

                if (match.HasRoyalPolitics)
                    RoyalVassalPacificationMarriages++;

                if (match.HasForeignPolitics)
                    ForeignRoyalAllianceMarriages++;

                if (match.Suitor?.Culture != null && match.Suitor.Culture == match.Candidate?.Culture)
                    SameCultureMarriages++;
                else
                    CrossCultureMarriages++;
            }

            public void RecordRejectedScore(float score)
            {
                HighestRejectedScore = MathF.Max(HighestRejectedScore, score);
            }

            public void RecordEndangeredUnmatched(Clan clan, float dynasticNeed)
            {
                if (clan != null && dynasticNeed >= C.MarriageNeedNoFertileCouple)
                    _endangeredUnmatchedClans.Add(clan.StringId);
            }

            public string Format(int yearIndex, int daysPerYear, int rulingClansWithLowMarriageReserve)
            {
                int averageScore = BellumMarriagesCompleted > 0 ? AcceptedScoreTotal / BellumMarriagesCompleted : 0;
                return "yearly summary "
                    + $"year_index={yearIndex} days_per_year={daysPerYear} "
                    + $"checked_clans={_checkedClans.Count} initiating_heroes={EligibleUnmarriedNoblesFound} searches={Searches} availability_deferrals={AvailabilityDeferrals} "
                    + $"chance_failed={ChanceRollFailed} attempts={MarriageAttemptsRolled} completed={BellumMarriagesCompleted} player_offers={PlayerMarriageOffersSent} "
                    + $"types: dynastic={DynasticSurvivalMarriages} same_realm={SameRealmPoliticalMarriages} royal_pacification={RoyalVassalPacificationMarriages} foreign_alliance={ForeignRoyalAllianceMarriages} same_culture={SameCultureMarriages} cross_culture={CrossCultureMarriages}; "
                    + $"outcomes: no_match={NoValidMatchFound} no_legal_pair_searches={NoLegalPairSearches} house_refusal_searches={RefusedPairSearches} low_score={BestMatchBelowThreshold} vanilla_rejected={VanillaModelRejectedBellumMatch}; "
                    + $"search_evaluations: participants={Rejections.ParticipantChecks} eligible={Rejections.EligibleParticipants} temporary={Rejections.TemporaryParticipants} legal_pairs={Rejections.LegalPairs} accepted_pairs={Rejections.AcceptedPairs} first_refused={Rejections.FirstHouseRefused} second_refused={Rejections.SecondHouseRefused} best_rejected={(float.IsNegativeInfinity(Rejections.BestRejectedScore) ? "none" : Rejections.BestRejectedScore.ToString("0"))}; "
                    + $"rejections: already_married={Rejections.RejectedAlreadyMarried} too_young={Rejections.RejectedTooYoung} female_above_max={Rejections.RejectedFemaleAboveMaxAge} player_clan={Rejections.RejectedPlayerClan} minor_or_neutral={Rejections.RejectedMinorMercenaryOrNeutralClan} eliminated={Rejections.RejectedEliminatedClan} invalid={Rejections.RejectedInvalidOrInactiveHero} no_kingdom={Rejections.RejectedNoKingdom} war={Rejections.RejectedKingdomAtWar} war_kingdom_skips={Rejections.KingdomsSkippedAtWar} civil_war={Rejections.RejectedOppositeCivilWarSides} cannot_marry={Rejections.RejectedCannotMarry} vanilla_model={Rejections.RejectedVanillaModel} other={Rejections.RejectedOther}; "
                    + $"health: endangered_unmatched={_endangeredUnmatchedClans.Count} ruling_low_reserve={rulingClansWithLowMarriageReserve} avg_score={averageScore} highest_accepted={HighestAcceptedScore:0} highest_rejected={HighestRejectedScore:0}.";
            }
        }
    }
}
