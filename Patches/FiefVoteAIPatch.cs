using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Patches
{
    /// <summary>
    /// Why did I do this file?
    /// To hijack the vanilla Bannerlord AI during fief elections. It forces lords to vote along ideological party lines, introduces faction rivalries, and allows rebellious lords to weaponize their voting power against the King (The Spite Vote).
    /// </summary>
    [HarmonyPatch(typeof(SettlementClaimantDecision), "DetermineSupport")]
    public class FiefVoteAIPatch
    {
        private static readonly Dictionary<System.Type, PropertyInfo> _clanPropCache = new Dictionary<System.Type, PropertyInfo>();
        private static readonly Dictionary<System.Type, FieldInfo> _clanFieldCache = new Dictionary<System.Type, FieldInfo>();
        private static readonly HashSet<System.Type> _clanReflectionInitialized = new HashSet<System.Type>();
        private static readonly HashSet<System.Type> _missingClanAccessorLogged = new HashSet<System.Type>();
        private static readonly HashSet<System.Type> _nullCandidateLogged = new HashSet<System.Type>();
        private static readonly HashSet<string> _unifiedScoreFailureLogged = new HashSet<string>();
        private static FieldInfo _capturerFieldInfo;
        private static PropertyInfo _capturerPropInfo;

        public static void ResetReflectionCache()
        {
            _clanPropCache.Clear();
            _clanFieldCache.Clear();
            _clanReflectionInitialized.Clear();
            _missingClanAccessorLogged.Clear();
            _nullCandidateLogged.Clear();
            _unifiedScoreFailureLogged.Clear();
            _capturerFieldInfo = null;
            _capturerPropInfo = null;
        }

        public static Clan GetCandidateClan(DecisionOutcome outcome)
        {
            if (outcome == null) return null;
            System.Type type = outcome.GetType();

            if (!_clanReflectionInitialized.Contains(type))
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                PropertyInfo clanPropInfo = null;
                FieldInfo clanFieldInfo = null;

                System.Type searchType = type;
                while (searchType != null && clanPropInfo == null)
                {
                    clanPropInfo = searchType.GetProperty("Clan", flags)
                                ?? searchType.GetProperty("ClaimantClan", flags);
                    searchType = searchType.BaseType;
                }

                if (clanPropInfo == null)
                {
                    searchType = type;
                    while (searchType != null && clanFieldInfo == null)
                    {
                        clanFieldInfo = searchType.GetField("Clan", flags)
                                     ?? searchType.GetField("ClaimantClan", flags)
                                     ?? searchType.GetField("_clan", flags)
                                     ?? searchType.GetField("_claimantClan", flags);
                        searchType = searchType.BaseType;
                    }
                }

                _clanReflectionInitialized.Add(type);
                if (clanPropInfo != null) _clanPropCache[type] = clanPropInfo;
                if (clanFieldInfo != null) _clanFieldCache[type] = clanFieldInfo;

                if (clanPropInfo == null && clanFieldInfo == null && !_missingClanAccessorLogged.Contains(type))
                {
                    var clanMembers = new System.Text.StringBuilder();
                    searchType = type;
                    while (searchType != null && searchType != typeof(object))
                    {
                        foreach (var f in searchType.GetFields(flags))
                            if (f.FieldType == typeof(Clan)) clanMembers.Append($"  field: {searchType.Name}.{f.Name}\n");
                        foreach (var p in searchType.GetProperties(flags))
                            if (p.PropertyType == typeof(Clan)) clanMembers.Append($"  prop:  {searchType.Name}.{p.Name}\n");
                        searchType = searchType.BaseType;
                    }
                    BellumCivileNotifications.ShowPersonal(
                        $"[BellumCivile] FiefVoteAIPatch: Could not locate candidate clan on '{type.Name}'. Ideological fief voting disabled.\nAvailable Clan members:\n{clanMembers}",
                        BellumNotificationColors.Debug);
                    _missingClanAccessorLogged.Add(type);
                }
            }

            if (_clanPropCache.TryGetValue(type, out PropertyInfo clanProp))
                return clanProp.GetValue(outcome) as Clan;
            if (_clanFieldCache.TryGetValue(type, out FieldInfo clanField))
                return clanField.GetValue(outcome) as Clan;
            return null;
        }

        public static void LogNullCandidateOutcomeType(System.Type outcomeType, string context)
        {
            if (outcomeType == null || _nullCandidateLogged.Contains(outcomeType)) return;

            BellumCivileLogger.Log($"{context}: Could not resolve candidate clan for outcome type '{outcomeType.FullName}'. Duplicate ideology candidates may slip through the ballot filter.");
            _nullCandidateLogged.Add(outcomeType);
        }

        public static bool Prefix(SettlementClaimantDecision __instance, Clan clan, DecisionOutcome possibleOutcome, ref float __result)
        {
            __result = 0f;

            if (clan == null || clan.Leader == null || __instance?.Settlement == null || possibleOutcome == null)
                return false;

            if (clan.IsUnderMercenaryService || (clan.IsMinorFaction && clan != Clan.PlayerClan))
                return false;

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null)
                return false;

            Clan candidateClan = GetCandidateClan(possibleOutcome);

            if (candidateClan == null || candidateClan.Leader == null)
                return false;

            Kingdom kingdom = clan.Kingdom ?? __instance.Kingdom;
            if (kingdom == null)
                return false;

            var fiefDelib = Campaign.Current.GetCampaignBehavior<FiefDeliberationBehavior>();
            string bribedCandidate = fiefDelib?.GetBribedCandidateVote(kingdom, __instance.Settlement, clan);
            if (!string.IsNullOrEmpty(bribedCandidate))
            {
                __result = candidateClan.StringId == bribedCandidate ? C.FiefBribeForcedSupportScore : 0f;
                return false;
            }

            int? rawBribedFaction = fiefDelib?.GetBribedVote(kingdom, __instance.Settlement, clan);
            if (rawBribedFaction.HasValue)
            {
                FactionObject bribedCandidateFaction = factionManager.GetIdeologicalFaction(candidateClan);
                bool preferred = bribedCandidateFaction != null
                    && bribedCandidateFaction.Type == (FactionType)rawBribedFaction.Value;
                __result = preferred ? C.FiefBribeForcedSupportScore : 0f;
                return false;
            }

            FiefNominationResult preference = FiefNominationHelper.ScoreCandidate(
                clan,
                candidateClan,
                kingdom,
                __instance.Settlement,
                ResolveCapturerClan(__instance),
                factionManager);

            if (preference != null)
            {
                __result = preference.Score;
                return false;
            }

            Kingdom alternateKingdom = __instance.Kingdom ?? candidateClan.Kingdom;
            if (alternateKingdom != null && alternateKingdom != kingdom)
            {
                preference = FiefNominationHelper.ScoreCandidate(
                    clan,
                    candidateClan,
                    alternateKingdom,
                    __instance.Settlement,
                    ResolveCapturerClan(__instance),
                    factionManager);

                if (preference != null)
                {
                    __result = preference.Score;
                    return false;
                }
            }

            LogUnifiedScoreFailure(clan, candidateClan, kingdom, __instance.Settlement);
            __result = 0f;
            return false;
        }

        private static void LogUnifiedScoreFailure(Clan voter, Clan candidate, Kingdom kingdom, Settlement settlement)
        {
            string key = $"{kingdom?.StringId ?? "null"}|{settlement?.StringId ?? "null"}|{voter?.StringId ?? "null"}|{candidate?.StringId ?? "null"}";
            if (!_unifiedScoreFailureLogged.Add(key))
                return;

            BellumCivileLogger.Log(
                $"Fief vote unified scoring failed; voter={voter?.StringId ?? "null"} candidate={candidate?.StringId ?? "null"} kingdom={kingdom?.StringId ?? "null"} settlement={settlement?.StringId ?? "null"}. Support set to 0 instead of falling back to legacy fief-vote formula.");
        }

        private static Clan ResolveCapturerClan(SettlementClaimantDecision decision)
        {
            if (decision == null)
                return null;

            if (_capturerFieldInfo == null && _capturerPropInfo == null)
            {
                var decisionType = typeof(SettlementClaimantDecision);
                const BindingFlags capFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                _capturerFieldInfo = decisionType.GetField("CapturerHero", capFlags)
                                  ?? decisionType.GetField("_capturerHero", capFlags)
                                  ?? decisionType.GetField("Capturer", capFlags)
                                  ?? decisionType.GetField("_capturer", capFlags);

                if (_capturerFieldInfo == null)
                    _capturerPropInfo = decisionType.GetProperty("CapturerHero", capFlags)
                                     ?? decisionType.GetProperty("Capturer", capFlags);
            }

            Hero capturer = _capturerFieldInfo != null
                ? _capturerFieldInfo.GetValue(decision) as Hero
                : _capturerPropInfo?.GetValue(decision) as Hero;

            return capturer?.Clan
                ?? decision.Settlement?.Town?.LastCapturedBy
                ?? decision.ProposerClan;
        }
    }

    /// <summary>
    /// Why did I do this class?
    /// Vanilla ranks candidates by strength, ruler privilege, capturer right, poverty, and geography.
    /// Bellum Civile replaces that with a kingdom-wide nomination pass: every valid lord evaluates
    /// every valid candidate through the same political scoring used by the final vote, then the
    /// strongest nominees make the ballot.
    /// </summary>
    [HarmonyPatch(typeof(KingdomDecision), "NarrowDownCandidates")]
    public class FiefCandidateDiversityPatch
    {
        private struct CandidateRecord
        {
            public DecisionOutcome Outcome;
            public Clan Clan;
            public float Score;
            public int NominationCount;
        }

        private static FieldInfo _capturerFieldInfo;
        private static PropertyInfo _capturerPropInfo;

        // What does this method do?
        // Owns the fief ballot instead of filtering vanilla's clan order. Every eligible clan is
        // scored through the same political rules, and the three houses with the strongest naturally
        // accumulated nominations make the ballot. No faction or claimant receives a reserved seat.
        public static bool Prefix(KingdomDecision __instance, MBList<DecisionOutcome> initialCandidates, int maxCandidateCount, ref MBList<DecisionOutcome> __result)
        {
            if (!(__instance is SettlementClaimantDecision fiefDecision))
                return true;

            if (initialCandidates == null || initialCandidates.Count <= 1)
                return true;

            var factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null)
                return true;

            Kingdom kingdom = fiefDecision.Kingdom;
            if (kingdom == null)
                return true;

            List<CandidateRecord> ranked = new List<CandidateRecord>();
            foreach (DecisionOutcome outcome in initialCandidates)
            {
                Clan candidateClan = FiefVoteAIPatch.GetCandidateClan(outcome);
                if (candidateClan == null)
                {
                    FiefVoteAIPatch.LogNullCandidateOutcomeType(outcome?.GetType(), "FiefCandidateDiversityPatch");
                    outcome.InitialMerit = C.FiefCandidateMinimumScore;
                    ranked.Add(new CandidateRecord
                    {
                        Outcome = outcome,
                        Clan = null,
                        Score = outcome.InitialMerit,
                    });
                    continue;
                }

                ranked.Add(new CandidateRecord
                {
                    Outcome = outcome,
                    Clan = candidateClan,
                    Score = C.FiefCandidateMinimumScore,
                });
            }

            Dictionary<string, CandidateRecord> candidateById = ranked
                .Where(c => c.Clan != null)
                .ToDictionary(c => c.Clan.StringId);

            HashSet<string> eligibleCandidateIds = new HashSet<string>(
                FiefNominationHelper.GetEligibleCandidates(kingdom, fiefDecision.Settlement, null)
                    .Select(clan => clan.StringId));
            candidateById = candidateById
                .Where(entry => eligibleCandidateIds.Contains(entry.Key))
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            if (candidateById.Count == 0)
                return true;

            List<CandidateRecord> voterRanked = BuildInstantNominationRanking(
                    fiefDecision,
                    kingdom,
                    candidateById.Values.Select(c => c.Clan),
                    factionManager,
                    int.MaxValue)
                .Select(entry =>
                {
                    if (!candidateById.TryGetValue(entry.CandidateId, out CandidateRecord record))
                        return default;

                    record.Score = entry.Score;
                    record.NominationCount = entry.Count;
                    return record;
                })
                .Where(c => c.Outcome != null && c.Clan != null)
                .ToList();

            List<CandidateRecord> selected = new List<CandidateRecord>();
            FiefDeliberationBehavior deliberation = FiefDeliberationBehavior.Current;
            deliberation?.RefreshPreliminaryNominationsForVote(
                kingdom,
                fiefDecision.Settlement,
                ResolveCapturerClan(fiefDecision),
                candidateById.Values.Select(candidate => candidate.Clan));
            List<FiefDeliberationBehavior.FiefNominationRankingEntry> nominations = deliberation
                ?.GetRankedNominations(
                    kingdom,
                    fiefDecision.Settlement,
                    candidateById.Values.Select(c => c.Clan),
                    maxCandidateCount)
                ?? new List<FiefDeliberationBehavior.FiefNominationRankingEntry>();

            if (nominations.Count == 0)
            {
                nominations = voterRanked
                    .Take(maxCandidateCount)
                    .Select(c => new FiefDeliberationBehavior.FiefNominationRankingEntry
                    {
                        CandidateId = c.Clan.StringId,
                        Count = c.NominationCount,
                        Score = c.Score
                    })
                    .ToList();
            }

            foreach (FiefDeliberationBehavior.FiefNominationRankingEntry nominationEntry in nominations)
            {
                if (selected.Count >= maxCandidateCount)
                    break;

                if (candidateById.TryGetValue(nominationEntry.CandidateId, out CandidateRecord nomination)
                    && nomination.Outcome != null)
                {
                    nomination.Score = nominationEntry.Score;
                    nomination.NominationCount = nominationEntry.Count;
                    nomination.Outcome.InitialMerit = MathF.Max(C.FiefCandidateMinimumScore, nomination.Score);
                    selected.Add(nomination);
                }
            }

            foreach (CandidateRecord candidate in voterRanked)
            {
                if (selected.Count >= maxCandidateCount)
                    break;

                if (!selected.Any(c => c.Outcome == candidate.Outcome))
                    selected.Add(candidate);
            }

            __result = new MBList<DecisionOutcome>();
            foreach (CandidateRecord candidate in selected
                .OrderByDescending(c => c.NominationCount)
                .ThenByDescending(c => c.Score))
            {
                candidate.Outcome.InitialMerit = MathF.Max(C.FiefCandidateMinimumScore, candidate.Score);
                __result.Add(candidate.Outcome);
            }

            return false;
        }

        private static List<FiefDeliberationBehavior.FiefNominationRankingEntry> BuildInstantNominationRanking(
            SettlementClaimantDecision decision,
            Kingdom kingdom,
            IEnumerable<Clan> candidatePool,
            FactionManagerBehavior factionManager,
            int maxCandidateCount)
        {
            List<Clan> candidates = candidatePool?
                .Where(c => c != null)
                .Distinct()
                .ToList() ?? new List<Clan>();

            if (decision?.Settlement == null || kingdom == null || candidates.Count == 0 || maxCandidateCount <= 0)
                return new List<FiefDeliberationBehavior.FiefNominationRankingEntry>();

            Clan capturerClan = ResolveCapturerClan(decision);
            Dictionary<string, int> nominationCounts = candidates.ToDictionary(c => c.StringId, _ => 0);
            Dictionary<string, float> nominationScores = candidates.ToDictionary(c => c.StringId, _ => 0f);

            foreach (Clan voter in Clan.All.Where(c => FiefNominationHelper.IsValidVoter(c, kingdom)))
            {
                FiefNominationResult best = null;

                foreach (Clan candidate in candidates)
                {
                    FiefNominationResult result = FiefNominationHelper.ScoreCandidate(
                        voter,
                        candidate,
                        kingdom,
                        decision.Settlement,
                        capturerClan,
                        factionManager);

                    if (result == null)
                        continue;

                    nominationScores[result.Candidate.StringId] += result.Score;

                    if (best == null || result.Score > best.Score)
                        best = result;
                }

                if (best?.Candidate == null)
                    continue;

                string candidateId = best.Candidate.StringId;
                nominationCounts[candidateId] = nominationCounts.TryGetValue(candidateId, out int count) ? count + 1 : 1;
            }

            return nominationCounts
                .OrderByDescending(kv => kv.Value)
                .ThenByDescending(kv => nominationScores.TryGetValue(kv.Key, out float score) ? score : 0f)
                .Take(maxCandidateCount)
                .Select(kv => new FiefDeliberationBehavior.FiefNominationRankingEntry
                {
                    CandidateId = kv.Key,
                    Count = kv.Value,
                    Score = nominationScores.TryGetValue(kv.Key, out float score) ? score : 0f
                })
                .ToList();
        }

        public static float CalculateBellumCandidateScore(
            SettlementClaimantDecision decision,
            Clan candidateClan,
            FactionObject candidateFaction,
            FactionManagerBehavior factionManager = null)
        {
            if (decision?.Settlement == null || candidateClan?.Leader == null)
                return C.FiefCandidateMinimumScore;

            Kingdom kingdom = candidateClan.Kingdom ?? decision.Kingdom;
            if (kingdom == null)
                return C.FiefCandidateMinimumScore;

            Clan capturerClan = ResolveCapturerClan(decision);
            float score = 0f;
            int voters = 0;
            FactionManagerBehavior activeFactionManager = factionManager
                ?? Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();

            foreach (Clan voter in Clan.All.Where(c => FiefNominationHelper.IsValidVoter(c, kingdom)))
            {
                FiefNominationResult result = FiefNominationHelper.ScoreCandidate(
                    voter,
                    candidateClan,
                    kingdom,
                    decision.Settlement,
                    capturerClan,
                    activeFactionManager);

                if (result == null)
                    continue;

                score += result.Score;
                voters++;
            }

            if (voters <= 0)
                return C.FiefCandidateMinimumScore;

            return MathF.Max(C.FiefCandidateMinimumScore, score / voters);
        }

        private static Clan ResolveCapturerClan(SettlementClaimantDecision decision)
        {
            if (decision == null)
                return null;

            if (_capturerFieldInfo == null && _capturerPropInfo == null)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                System.Type decisionType = typeof(SettlementClaimantDecision);

                _capturerFieldInfo = decisionType.GetField("_capturerHero", flags)
                                  ?? decisionType.GetField("CapturerHero", flags)
                                  ?? decisionType.GetField("_capturer", flags)
                                  ?? decisionType.GetField("Capturer", flags);

                if (_capturerFieldInfo == null)
                {
                    _capturerPropInfo = decisionType.GetProperty("CapturerHero", flags)
                                      ?? decisionType.GetProperty("Capturer", flags);
                }
            }

            Hero capturer = _capturerFieldInfo != null
                ? _capturerFieldInfo.GetValue(decision) as Hero
                : _capturerPropInfo?.GetValue(decision) as Hero;

            return capturer?.Clan
                ?? decision.Settlement?.Town?.LastCapturedBy
                ?? decision.ProposerClan;
        }

    }

    [HarmonyPatch(typeof(SettlementClaimantDecision), "CalculateMeritOfOutcome")]
    public class FiefCandidateMeritPatch
    {
        public static bool Prefix(SettlementClaimantDecision __instance, DecisionOutcome candidateOutcome, ref float __result)
        {
            Clan candidateClan = FiefVoteAIPatch.GetCandidateClan(candidateOutcome);
            if (candidateClan == null)
                return true;

            var factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject faction = factionManager?.GetIdeologicalFaction(candidateClan);
            __result = FiefCandidateDiversityPatch.CalculateBellumCandidateScore(__instance, candidateClan, faction, factionManager);
            return false;
        }
    }
}
