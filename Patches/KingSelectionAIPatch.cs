using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Library;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Patches
{
    public class SuccessionElectionProfile
    {
        public Hero ProtectedCandidate;
        public Clan SecondNominee;
        public Clan ThirdNominee;
        public Clan LegitimateDynasticClan;
        public bool ProtectedCandidateIsDynasticHeir;
        public bool ProtectedCandidateIsRightfulSovereign;
        public FeudalClaimStrength? ProtectedCandidateClaimStrength;
        public bool IsAbdication;
        public bool IsEmergency;
        public KingSelectionKingdomDecision SourceDecision;
        public Dictionary<FactionType, Clan> Endorsements = new Dictionary<FactionType, Clan>();
        public Dictionary<Clan, float> CandidatePopularity = new Dictionary<Clan, float>();
        public Dictionary<Clan, ElectionCandidateProfile> CandidateProfiles = new Dictionary<Clan, ElectionCandidateProfile>();
        public Clan ProtectedCandidateClan => ProtectedCandidate?.Clan;

        public IEnumerable<Clan> BallotClans => new[] { ProtectedCandidateClan, SecondNominee, ThirdNominee }
            .Where(clan => clan != null)
            .Distinct();
    }

    public class ElectionCandidateProfile
    {
        public Clan Clan;
        public float Popularity;
        public float Legitimacy;
        public float HierarchyStanding;
        public float ClanTierStanding;
        public float WealthStanding;
        public float InfluenceStanding;
        public float StrengthStanding;
        public bool IsDynasticHeir;
        public bool HasStrongClaim;
        public bool HasWeakClaim;

        public float GeneralStanding => MathF.Clamp(
            (Legitimacy + HierarchyStanding + ClanTierStanding + WealthStanding + InfluenceStanding + StrengthStanding) / 6f,
            0f,
            1f);
    }

    internal class RawElectionCandidateMetrics
    {
        public Clan Clan;
        public float Strength;
        public float Wealth;
        public float Influence;
        public float Popularity;
        public float HierarchyStanding;
        public float ClanTierStanding;
    }

    internal sealed class SuccessionNominationStanding
    {
        public Clan Candidate;
        public int NominationCount;
        public float NominationScore;
    }

    internal enum SuccessionPillar
    {
        Legitimacy,
        Hierarchy,
        ClanTier,
        Wealth,
        Influence,
        Strength
    }

    /// <summary>
    /// Why did I do this file?
    /// To overhaul the vanilla king succession AI. A lawful candidate is protected on the ballot while the other nominees
    /// emerge from realm-wide noble preferences for legitimacy, hierarchy, renown, wealth, influence, and military power.
    /// </summary>
    [HarmonyPatch(typeof(KingSelectionKingdomDecision))]
    public class KingSelectionAIPatch
    {
        public static Dictionary<Kingdom, SuccessionElectionProfile> ActiveElections = new Dictionary<Kingdom, SuccessionElectionProfile>();

        private static PropertyInfo _candidateClanProp;
        private static FieldInfo    _candidateClanField;
        private static bool _candidateReflectionInitialized = false;

        public static void ResetReflectionCache()
        {
            _candidateClanProp = null;
            _candidateClanField = null;
            _candidateReflectionInitialized = false;
        }

        [HarmonyPatch("DetermineInitialCandidates")]
        [HarmonyPrefix]
        public static bool DetermineInitialCandidatesPrefix(KingSelectionKingdomDecision __instance, ref IEnumerable<DecisionOutcome> __result)
        {
            Kingdom kingdom = __instance.Kingdom;
            Clan proposer = __instance.ProposerClan;
            Clan clanToExclude = typeof(KingSelectionKingdomDecision).GetField("_clanToExclude", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance) as Clan;
            bool isVoluntaryAbdication = clanToExclude != null && proposer == clanToExclude;
            SuccessionElectionProfile election = new SuccessionElectionProfile();
            election.SourceDecision = __instance;
            CrownAccessionRecord emergency = CrownAccessionBehavior.Instance?.GetEmergency(__instance);
            bool coalitionSuccession = CivilWarResolutionBehavior.Current?.IsCoalitionSuccession(kingdom) == true;
            election.IsEmergency = emergency != null || coalitionSuccession;

            // An abdication election is identified by its victorious proposer and excluded
            // former ruling clan. The former ruler may have died during the civil war; that
            // must not turn the settlement into an unrelated ordinary succession.
            if (election.IsEmergency)
            {
                // All three places are earned by nomination, including the first.
                election.ProtectedCandidate = null;
            }
            else if (clanToExclude != null
                && proposer != null
                && proposer != clanToExclude
                && IsEligibleElectionClan(proposer, kingdom))
            {
                election.IsAbdication = true;
                election.ProtectedCandidate = proposer.Leader;
            }
            else
            {
                election.IsAbdication = false;

                Hero slot1 = kingdom.RulingClan?.Leader ?? clanToExclude?.Leader ?? proposer?.Leader;

                var heirBehavior = Campaign.Current.GetCampaignBehavior<DynasticHeirBehavior>();
                if (heirBehavior != null && !heirBehavior.IsUsurper(kingdom) && kingdom.RulingClan != Clan.PlayerClan)
                {
                    Hero dynasticCandidate = heirBehavior.GetDynasticSuccessionCandidate(kingdom);
                    Hero rightfulSovereign = isVoluntaryAbdication
                        ? ResolveLivingRightfulSovereign(dynasticCandidate, clanToExclude, kingdom)
                        : null;

                    if (rightfulSovereign != null)
                    {
                        slot1 = rightfulSovereign;
                        election.LegitimateDynasticClan = rightfulSovereign.Clan;
                        election.ProtectedCandidateIsRightfulSovereign = true;
                    }
                    else if (dynasticCandidate?.Clan != null && dynasticCandidate.Clan.Kingdom == kingdom)
                    {
                        slot1 = dynasticCandidate;
                        election.LegitimateDynasticClan = dynasticCandidate.Clan;
                        election.ProtectedCandidateIsDynasticHeir = true;
                    }
                    else
                    {
                        Hero titleClaimCandidate = ResolveBestKingdomTitleClaimCandidate(kingdom, FeudalClaimStrength.Strong)
                            ?? ResolveBestKingdomTitleClaimCandidate(kingdom, FeudalClaimStrength.Weak);

                        if (titleClaimCandidate?.Clan != null && titleClaimCandidate.Clan.Kingdom == kingdom)
                        {
                            slot1 = titleClaimCandidate;
                            election.ProtectedCandidateClaimStrength = HasKingdomTitleClaim(titleClaimCandidate.Clan, kingdom, FeudalClaimStrength.Strong)
                                ? FeudalClaimStrength.Strong
                                : FeudalClaimStrength.Weak;
                        }
                    }
                }

                if (!election.ProtectedCandidateIsRightfulSovereign
                    && !election.ProtectedCandidateIsDynasticHeir
                    && !election.ProtectedCandidateClaimStrength.HasValue
                    && kingdom.RulingClan != Clan.PlayerClan)
                {
                    Hero titleClaimCandidate = ResolveBestKingdomTitleClaimCandidate(kingdom, FeudalClaimStrength.Strong)
                        ?? ResolveBestKingdomTitleClaimCandidate(kingdom, FeudalClaimStrength.Weak);

                    if (titleClaimCandidate?.Clan != null && titleClaimCandidate.Clan.Kingdom == kingdom)
                    {
                        slot1 = titleClaimCandidate;
                        election.ProtectedCandidateClaimStrength = HasKingdomTitleClaim(titleClaimCandidate.Clan, kingdom, FeudalClaimStrength.Strong)
                            ? FeudalClaimStrength.Strong
                            : FeudalClaimStrength.Weak;
                    }
                }

                election.ProtectedCandidate = slot1;
            }

            List<Clan> eligibleCandidates = coalitionSuccession
                ? CivilWarResolutionBehavior.Current.CoalitionCandidates(kingdom)
                : emergency != null
                ? CrownAccessionBehavior.Instance.EmergencyCandidates(emergency)
                : kingdom.Clans
                .Where(clan => clan != clanToExclude && IsEligibleElectionClan(clan, kingdom))
                .Distinct()
                .ToList();

            if (election.ProtectedCandidateClan != null
                && election.ProtectedCandidateClan != clanToExclude
                && IsEligibleElectionClan(election.ProtectedCandidateClan, kingdom)
                && !eligibleCandidates.Contains(election.ProtectedCandidateClan))
            {
                eligibleCandidates.Add(election.ProtectedCandidateClan);
            }

            CacheCandidatePopularity(kingdom, election, eligibleCandidates);
            CacheCandidateProfiles(kingdom, election, eligibleCandidates);
            SelectPoliticalNominees(kingdom, election, eligibleCandidates);
            if (coalitionSuccession)
            {
                var nominees = CivilWarResolutionBehavior.Current.PinCoalitionNominees(kingdom,
                    new[] { election.ProtectedCandidate, election.SecondNominee?.Leader, election.ThirdNominee?.Leader });
                election.ProtectedCandidate = nominees.ElementAtOrDefault(0);
                election.SecondNominee = nominees.ElementAtOrDefault(1)?.Clan;
                election.ThirdNominee = nominees.ElementAtOrDefault(2)?.Clan;
            }
            if (emergency != null)
            {
                if (emergency.FirstNominee == null)
                {
                    emergency.FirstNominee = election.ProtectedCandidate;
                    emergency.SecondNominee = election.SecondNominee?.Leader;
                    emergency.ThirdNominee = election.ThirdNominee?.Leader;
                }
                election.ProtectedCandidate = emergency.FirstNominee;
                election.SecondNominee = emergency.SecondNominee?.Clan;
                election.ThirdNominee = emergency.ThirdNominee?.Clan;
            }
            AssignDynamicEndorsements(election);

            ActiveElections[kingdom] = election;

            Type outcomeType = typeof(KingSelectionKingdomDecision).Assembly.GetType("TaleWorlds.CampaignSystem.Election.KingSelectionDecisionOutcome")
                            ?? typeof(KingSelectionKingdomDecision).GetNestedType("KingSelectionDecisionOutcome", BindingFlags.Public | BindingFlags.NonPublic) 
                            ?? typeof(KingSelectionKingdomDecision).Assembly.GetType("TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision+KingSelectionDecisionOutcome")
                            ?? typeof(KingSelectionKingdomDecision).Assembly.GetTypes().FirstOrDefault(t => t.Name == "KingSelectionDecisionOutcome");

            if (outcomeType == null)
            {
                BellumCivileNotifications.ShowPersonal("[BellumCivile] Error: Could not resolve KingSelectionDecisionOutcome type!", BellumNotificationColors.Danger);
            }

            List<DecisionOutcome> newOutcomes = new List<DecisionOutcome>();
            foreach (Hero rawCandidate in new Hero[] { election.ProtectedCandidate, election.SecondNominee?.Leader, election.ThirdNominee?.Leader })
            {
                Hero candidate = ResolveElectionHeroForClan(rawCandidate);

                if (candidate != null && outcomeType != null)
                {
                    ConstructorInfo ctorHero = outcomeType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(Hero) }, null);
                    ConstructorInfo ctorClan = outcomeType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(Clan) }, null);

                    try
                    {
                        if (ctorHero != null) newOutcomes.Add((DecisionOutcome)ctorHero.Invoke(new object[] { candidate }));
                        else if (ctorClan != null && candidate.Clan != null) newOutcomes.Add((DecisionOutcome)ctorClan.Invoke(new object[] { candidate.Clan }));
                        else newOutcomes.Add((DecisionOutcome)Activator.CreateInstance(outcomeType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { candidate }, null));
                    }
                    catch (Exception ex)
                    {
                        BellumCivileNotifications.ShowPersonal($"[BellumCivile] Error instantiating election candidate: {ex.Message}", BellumNotificationColors.Danger);
                    }
                }
            }

            __result = newOutcomes;
            return false; 
        }

        [HarmonyPatch("CalculateMeritOfOutcome")]
        [HarmonyPostfix]
        public static void CalculateMeritOfOutcomePostfix(KingSelectionKingdomDecision __instance, DecisionOutcome candidateOutcome, ref float __result)
        {
            if (candidateOutcome == null) return;
            RestoreEmergencyProfile(__instance);
            if (!ActiveElections.TryGetValue(__instance.Kingdom, out SuccessionElectionProfile election)) return;

            Clan candidateClan = GetCandidateClan(candidateOutcome);
            if (candidateClan == null) return;

            List<Clan> electorate = __instance.Kingdom.Clans
                .Where(clan => clan != Clan.PlayerClan && IsElectionVoter(clan, __instance.Kingdom, election))
                .ToList();
            if (electorate.Count == 0)
            {
                __result = election.CandidateProfiles.TryGetValue(candidateClan, out ElectionCandidateProfile profile)
                    ? profile.GeneralStanding * 100f
                    : 1f;
                return;
            }

            __result = electorate.Average(clan => MathF.Clamp(CalculateSupportScore(clan, candidateClan, election), 0f, 100f));
        }

        [HarmonyPatch("ApplyChosenOutcome")]
        [HarmonyPrefix]
        public static bool ApplyChosenOutcomePrefix(KingSelectionKingdomDecision __instance, DecisionOutcome chosenOutcome)
        {
            Kingdom kingdom = __instance?.Kingdom;
            Clan chosenClan = ResolveCandidateClan(chosenOutcome);
            var emergency = CrownAccessionBehavior.Instance?.GetEmergency(__instance);
            Hero chosenHero = (chosenOutcome as KingSelectionKingdomDecision.KingSelectionDecisionOutcome)?.King;
            if (CivilWarResolutionBehavior.Current?.IsCoalitionSuccession(kingdom) == true)
            {
                bool valid = chosenHero != null && chosenHero == chosenClan?.Leader
                    && CivilWarResolutionBehavior.Current.CoalitionCandidates(kingdom).Contains(chosenClan)
                    && CivilWarResolutionBehavior.Current.PinCoalitionNominees(kingdom, new Hero[0]).Contains(chosenHero);
                if (!valid) kingdom.RemoveDecision(__instance);
                return valid;
            }
            if (emergency != null && (emergency.OutcomeApplied
                || chosenHero == null || chosenHero != chosenClan?.Leader || !chosenHero.IsAlive
                || !emergency.Nominees.Contains(chosenHero)
                || !CrownAccessionBehavior.Instance.EmergencyCandidates(emergency).Contains(chosenClan)))
            {
                kingdom.RemoveDecision(__instance);
                return false;
            }

            if (kingdom == null)
                return true;

            if (chosenClan == null)
            {
                if (kingdom.RulingClan != Clan.PlayerClan)
                    return true;

                try
                {
                    kingdom.RemoveDecision(__instance);
                }
                catch
                {
                    // The decision may be running as an immediate election rather than from UnresolvedDecisions.
                }

                RulerTransitionDebugHelper.Report(
                    "blocked unresolved king-selection outcome over player ruler",
                    kingdom,
                    kingdom.RulingClan,
                    null,
                    $"proposer={__instance.ProposerClan?.StringId ?? "null"}; chosen outcome could not be resolved.",
                    requestInGameDisplay: true);

                return false;
            }

            Clan previousRuler = kingdom.RulingClan;
            bool wouldDeposePlayer = previousRuler == Clan.PlayerClan && chosenClan != Clan.PlayerClan;
            if (!wouldDeposePlayer)
            {
                RulerTransitionDebugHelper.Report(
                    "king-selection outcome",
                    kingdom,
                    previousRuler,
                    chosenClan,
                    $"proposer={__instance.ProposerClan?.StringId ?? "null"}; chosen={chosenClan.StringId}",
                    requestInGameDisplay: false);
                return true;
            }

            var resolutionBehavior = Campaign.Current?.GetCampaignBehavior<CivilWarResolutionBehavior>();
            bool authorized = resolutionBehavior != null
                           && resolutionBehavior.IsPlayerRulerReplacementElectionAuthorized(kingdom, __instance);
            bool playerDeathSuccession = CrownAccessionBehavior.Instance?.GetEmergency(__instance) != null
                || (resolutionBehavior != null
                    && resolutionBehavior.IsPlayerDeathSuccessionElectionAuthorized(kingdom, __instance));

            if (authorized || playerDeathSuccession)
            {
                RulerTransitionDebugHelper.Report(
                    playerDeathSuccession
                        ? "player-death succession king-selection outcome"
                        : "authorized king-selection outcome replacing player ruler",
                    kingdom,
                    previousRuler,
                    chosenClan,
                    $"proposer={__instance.ProposerClan?.StringId ?? "null"}; chosen={chosenClan.StringId}",
                    requestInGameDisplay: true);
                return true;
            }

            try
            {
                kingdom.RemoveDecision(__instance);
            }
            catch
            {
                // The decision may be running as an immediate election rather than from UnresolvedDecisions.
            }

            RulerTransitionDebugHelper.Report(
                "blocked unauthorized king-selection outcome",
                kingdom,
                previousRuler,
                chosenClan,
                $"proposer={__instance.ProposerClan?.StringId ?? "null"}; chosen={chosenClan.StringId}; no Bellum authorization was present.",
                requestInGameDisplay: true);

            return false;
        }

        [HarmonyPatch("ApplyChosenOutcome")]
        [HarmonyPostfix]
        public static void ApplyChosenOutcomePostfix(KingSelectionKingdomDecision __instance, DecisionOutcome chosenOutcome, bool __runOriginal)
        {
            if (!__runOriginal) return;
            Kingdom kingdom = __instance?.Kingdom;
            Clan chosenClan = ResolveCandidateClan(chosenOutcome);
            if (kingdom == null || chosenClan == null || kingdom.RulingClan != chosenClan)
                return;

            if (CivilWarResolutionBehavior.Current?.IsCoalitionSuccession(kingdom) == true)
            {
                CivilWarResolutionBehavior.Current.CompleteCoalitionSuccession(kingdom, chosenClan);
                return;
            }

            var rebelFaction = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionByRebelKingdom(kingdom);
            if (rebelFaction != null)
                CivilWarTransitionDiagnostics.Log("succession outcome applied", rebelFaction, kingdom,
                    $"chosen_house={chosenClan.StringId}; chosen_hero={chosenClan.Leader?.StringId}");

            if (CrownAccessionBehavior.Instance?.GetEmergency(__instance) != null)
                CrownAccessionBehavior.Instance.CompleteEmergency(__instance, chosenClan);
            else
                Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>()
                    ?.TrySetKingdomTitleRuler(kingdom, chosenClan, legalTransfer: true, reason: "king-selection outcome");

            Campaign.Current?.GetCampaignBehavior<CivilWarResolutionBehavior>()
                ?.CompletePlayerRulerReplacementElection(kingdom, __instance);
        }

        private static Hero ResolveLivingRightfulSovereign(
            Hero dynasticCandidate,
            Clan excludedClan,
            Kingdom kingdom)
        {
            FeudalTitleRecord crown = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>()?.GetKingdomPoliticalTitle(kingdom);
            Clan rightfulClan = Clan.All.FirstOrDefault(clan => clan != null && clan.StringId == crown?.DeJureHolderClanId);
            Hero rightfulRuler = rightfulClan?.Leader;

            if (rightfulClan == null
                || rightfulClan == excludedClan
                || rightfulClan.Kingdom != kingdom
                || rightfulClan.IsEliminated
                || dynasticCandidate?.Clan != rightfulClan
                || rightfulRuler == dynasticCandidate
                || !IsEligibleElectionClan(rightfulClan, kingdom)
                || !IsElectableRulerHero(rightfulRuler))
            {
                return null;
            }

            return rightfulRuler;
        }

        private static Hero ResolveBestKingdomTitleClaimCandidate(Kingdom kingdom, FeudalClaimStrength strength)
        {
            if (kingdom == null)
                return null;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalTitleRecord kingdomTitle = titleBehavior?.GetKingdomPoliticalTitle(kingdom);
            if (titleBehavior == null || kingdomTitle == null)
                return null;

            Clan claimant = kingdom.Clans
                .Where(clan => IsEligibleElectionClan(clan, kingdom)
                    && clan != kingdom.RulingClan
                    && titleBehavior.HasActiveClaim(clan, kingdomTitle, strength))
                .OrderByDescending(CalculateClaimantPoliticalMilitaryScore)
                .ThenByDescending(clan => clan.Tier)
                .ThenBy(clan => clan.StringId)
                .FirstOrDefault();

            if (claimant == null)
                return null;

            if (claimant == Clan.PlayerClan)
                return Clan.PlayerClan.Leader;

            return IsElectableRulerHero(claimant.Leader)
                ? claimant.Leader
                : ResolveHeirCandidate(claimant);
        }

        private static bool HasKingdomTitleClaim(Clan clan, Kingdom kingdom, FeudalClaimStrength strength)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalTitleRecord kingdomTitle = titleBehavior?.GetKingdomPoliticalTitle(kingdom);
            return titleBehavior != null
                && kingdomTitle != null
                && titleBehavior.HasActiveClaim(clan, kingdomTitle, strength);
        }

        private static float CalculateClaimantPoliticalMilitaryScore(Clan clan)
        {
            if (clan == null)
                return float.MinValue;

            float strength = Campaign.Current?.Models?.DiplomacyModel?.GetClanStrength(clan) ?? clan.CurrentTotalStrength;
            return strength + MathF.Max(0f, clan.Influence);
        }

        private static bool IsEligibleElectionClan(Clan clan, Kingdom kingdom)
        {
            if (!NobleClanEligibilityHelper.IsValidRulingClan(clan, kingdom))
                return false;

            SuccessionLawSet laws = SuccessionLawHelper.GetLawsForKingdom(kingdom);
            return SuccessionLawHelper.IsEligibleUnderGenderLaw(clan.Leader, laws);
        }

        private static bool IsElectionVoter(Clan clan, Kingdom kingdom, SuccessionElectionProfile election) =>
            election.IsEmergency ? NobleClanEligibilityHelper.IsValidRulingClan(clan, kingdom)
                : IsEligibleElectionClan(clan, kingdom);

        private static void RestoreEmergencyProfile(KingSelectionKingdomDecision decision)
        {
            if (CrownAccessionBehavior.Instance?.GetEmergency(decision) == null
                && CivilWarResolutionBehavior.Current?.IsCoalitionSuccession(decision.Kingdom) != true) return;
            if (ActiveElections.TryGetValue(decision.Kingdom, out var profile) && profile.SourceDecision == decision) return;
            IEnumerable<DecisionOutcome> ignored = null;
            DetermineInitialCandidatesPrefix(decision, ref ignored);
        }

        private static void SelectPoliticalNominees(
            Kingdom kingdom,
            SuccessionElectionProfile election,
            List<Clan> eligibleCandidates)
        {
            election.SecondNominee = null;
            election.ThirdNominee = null;
            if (kingdom == null || eligibleCandidates == null || eligibleCandidates.Count == 0)
                return;

            Dictionary<Clan, SuccessionNominationStanding> standings = eligibleCandidates
                .Where(candidate => candidate?.Leader != null && election.CandidateProfiles.ContainsKey(candidate))
                .ToDictionary(
                    candidate => candidate,
                    candidate => new SuccessionNominationStanding { Candidate = candidate });

            List<Clan> electorate = kingdom.Clans
                .Where(voter => voter != Clan.PlayerClan && IsElectionVoter(voter, kingdom, election))
                .ToList();

            foreach (Clan voter in electorate)
            {
                Clan nominee = standings.Keys
                    .OrderByDescending(candidate => CalculateNominationPreference(voter, candidate, election))
                    .ThenByDescending(candidate => election.CandidateProfiles[candidate].GeneralStanding)
                    .ThenByDescending(candidate => candidate.Tier)
                    .ThenBy(candidate => candidate.StringId)
                    .FirstOrDefault();

                if (nominee == null)
                    continue;

                SuccessionNominationStanding standing = standings[nominee];
                standing.NominationCount++;
                standing.NominationScore += CalculateNominationPreference(voter, nominee, election);
            }

            List<Clan> rankedNominees = standings.Values
                .Where(standing => standing.Candidate != election.ProtectedCandidateClan)
                .OrderByDescending(standing => standing.NominationCount)
                .ThenByDescending(standing => standing.NominationScore)
                .ThenByDescending(standing => election.CandidateProfiles[standing.Candidate].GeneralStanding)
                .ThenByDescending(standing => standing.Candidate.Tier)
                .ThenBy(standing => standing.Candidate.StringId)
                .Select(standing => standing.Candidate)
                .Take(election.IsEmergency ? 3 : 2)
                .ToList();

            if (election.IsEmergency)
                election.ProtectedCandidate = rankedNominees.ElementAtOrDefault(0)?.Leader;
            election.SecondNominee = rankedNominees.ElementAtOrDefault(election.IsEmergency ? 1 : 0);
            election.ThirdNominee = rankedNominees.ElementAtOrDefault(election.IsEmergency ? 2 : 1);
        }

        private static float CalculateNominationPreference(
            Clan voterClan,
            Clan candidateClan,
            SuccessionElectionProfile election)
        {
            if (voterClan?.Leader == null
                || candidateClan?.Leader == null
                || !election.CandidateProfiles.TryGetValue(candidateClan, out ElectionCandidateProfile profile))
            {
                return float.MinValue;
            }

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject voterFaction = factionManager?.GetIdeologicalFaction(voterClan);
            float affinity = voterFaction != null
                ? GetIdeologicalCandidateAffinity(voterFaction.Type, profile, election)
                : profile.GeneralStanding;

            float score = affinity * 100f;
            if (voterClan == candidateClan)
            {
                score += C.KingSelectNominationSelfBonus;
            }
            else
            {
                score += voterClan.Leader.GetRelation(candidateClan.Leader) * C.KingSelectNominationRelationScale;
            }

            if (voterFaction != null)
            {
                score += GetSupplementalIdeologyModifier(voterFaction.Type, profile);

            }

            score += GetTraitSupportModifier(voterClan, profile, endorsedClan: null);
            return score;
        }

        private static RawElectionCandidateMetrics BuildRawCandidateMetrics(Clan clan, Kingdom kingdom)
        {
            FeudalTitleType? highestTitle = GetSuccessionHierarchyRank(clan, kingdom);

            return new RawElectionCandidateMetrics
            {
                Clan = clan,
                Strength = Campaign.Current?.Models?.DiplomacyModel?.GetClanStrength(clan) ?? clan?.CurrentTotalStrength ?? 0f,
                Wealth = MathF.Max(0f, clan?.Gold ?? 0f),
                Influence = MathF.Max(0f, clan?.Influence ?? 0f),
                Popularity = clan?.Kingdom != null ? CalculateCandidatePopularity(clan.Kingdom, clan) : 0f,
                HierarchyStanding = GetTitleStanding(highestTitle),
                ClanTierStanding = MathF.Clamp((clan?.Tier ?? 0) / 6f, 0f, 1f)
            };
        }

        private static FeudalTitleType? GetSuccessionHierarchyRank(Clan clan, Kingdom kingdom)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titleBehavior == null || clan == null)
                return clan?.Fiefs?.Count > 0 ? FeudalTitleType.Barony : (FeudalTitleType?)null;

            string sovereignTitleId = titleBehavior.GetKingdomPoliticalTitle(kingdom)?.TitleId;
            FeudalTitleRecord highest = titleBehavior.GetTitlesHeldByClan(clan, deJure: true)
                .Where(title => title != null
                    && title.IsActive
                    && !string.Equals(title.TitleId, sovereignTitleId, StringComparison.Ordinal))
                .OrderByDescending(title => title.TitleType)
                .FirstOrDefault();

            if (highest != null)
                return highest.TitleType;

            return clan.Fiefs.Count > 0 ? FeudalTitleType.Barony : (FeudalTitleType?)null;
        }

        private static float NormalizeDiminishingMetric(float value, IEnumerable<float> values)
        {
            List<float> list = values?
                .Select(candidateValue => (float)Math.Log(1d + Math.Max(0d, candidateValue)))
                .ToList();
            if (list == null || list.Count == 0)
                return 0.5f;

            float transformedValue = (float)Math.Log(1d + Math.Max(0d, value));
            float minimum = list.Min();
            float maximum = list.Max();
            if (maximum - minimum < 0.01f)
                return 0.5f;

            return MathF.Clamp((transformedValue - minimum) / (maximum - minimum), 0f, 1f);
        }

        private static float GetTitleStanding(FeudalTitleType? titleType)
        {
            return titleType.HasValue
                ? MathF.Clamp(((int)titleType.Value + 1f) / ((int)FeudalTitleType.Empire + 1f), 0f, 1f)
                : 0f;
        }

        private static Hero ResolveElectionHeroForClan(Hero candidate)
        {
            Clan candidateClan = candidate?.Clan;
            if (candidateClan == null)
                return candidate;

            if (candidateClan == Clan.PlayerClan)
                return Clan.PlayerClan.Leader ?? candidate;

            return candidate;
        }

        private static Hero ResolveHeirCandidate(Clan claimantClan)
        {
            return SuccessionLawHelper.GetOrderedSuccessionLine(claimantClan)
                .FirstOrDefault(IsElectableRulerHero);
        }

        private static bool IsElectableRulerHero(Hero hero)
        {
            return hero != null
                && hero.IsAlive
                && !hero.IsDisabled
                && hero.Age >= SuccessionLawHelper.GetAgeOfMajority();
        }

        [HarmonyPatch("DetermineSupport")]
        [HarmonyPrefix]
        public static bool DetermineSupportPrefix(KingSelectionKingdomDecision __instance, Clan clan, DecisionOutcome possibleOutcome, ref float __result)
        {
            if (clan == null || clan.Leader == null || possibleOutcome == null) return true;
            RestoreEmergencyProfile(__instance);
            if (!ActiveElections.TryGetValue(__instance.Kingdom, out SuccessionElectionProfile election)) return true;
            if (!IsElectionVoter(clan, __instance.Kingdom, election)) return true;

            Clan candidateClan = GetCandidateClan(possibleOutcome);
            if (candidateClan == null || candidateClan.Leader == null) return true;

            __result = CalculateSupportScore(clan, candidateClan, election);
            return false;
        }

        internal static SuccessionElectionProfile BuildStandingProfile(Kingdom realm, IEnumerable<Clan> candidates)
        {
            var profile = new SuccessionElectionProfile();
            CacheCandidateProfiles(realm, profile, candidates);
            return profile;
        }

        internal static float CalculateSupportScore(Clan voterClan, Clan candidateClan, SuccessionElectionProfile election, bool standing = false)
        {
            if (!standing && voterClan == candidateClan)
                return C.KingSelectSelfVoteBonus;

            if (!election.CandidateProfiles.TryGetValue(candidateClan, out ElectionCandidateProfile candidateProfile))
                return voterClan.Leader.GetRelation(candidateClan.Leader) * C.KingSelectRelationScale;

            Hero voterHero = standing ? ElectiveSuccessionBehavior.LegalHead(voterClan) : voterClan.Leader;
            Hero candidateHero = standing ? ElectiveSuccessionBehavior.LegalHead(candidateClan) : candidateClan.Leader;
            int relation = voterClan == candidateClan ? 0 : voterHero.GetRelation(candidateHero);
            float score = C.KingSelectBaseSupport;
            score += relation * C.KingSelectRelationScale;
            score += (candidateProfile.GeneralStanding - 0.5f) * C.KingSelectPoliticalStandingRange;

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject voterFaction = factionManager?.GetIdeologicalFaction(voterClan);
            Clan rulingClan = voterClan.Kingdom?.RulingClan;

            Clan endorsedClan = null;
            if (voterFaction != null)
            {
                election.Endorsements.TryGetValue(voterFaction.Type, out endorsedClan);
                float ideologyAffinity = GetIdeologicalCandidateAffinity(voterFaction.Type, candidateProfile, election);
                score += (ideologyAffinity - 0.5f) * C.KingSelectIdeologyPreferenceRange;
                score += GetSupplementalIdeologyModifier(voterFaction.Type, candidateProfile);

                if (candidateClan == endorsedClan)
                    score += C.KingSelectFactionEndorsementBonus;


                if (relation > C.KingSelectPersonalLoveThreshold)
                    score += C.KingSelectPersonalLoveBonus;

            }

            score += GetTraitSupportModifier(voterClan, candidateProfile, endorsedClan, standing);
            return MathF.Clamp(score, C.KingSelectMinimumSupport, C.KingSelectMaximumSupport);
        }

        // Traits change what an individual lord values inside their broader ideological preference.
        private static float GetTraitSupportModifier(
            Clan voterClan,
            ElectionCandidateProfile candidateProfile,
            Clan endorsedClan, bool standing = false)
        {
            Hero voter = standing ? ElectiveSuccessionBehavior.LegalHead(voterClan) : voterClan?.Leader;
            Hero candidate = standing ? ElectiveSuccessionBehavior.LegalHead(candidateProfile?.Clan) : candidateProfile?.Clan?.Leader;
            if (voter == null || candidate == null)
                return 0f;

            float modifier = 0f;
            modifier += GetHonorDisciplineModifier(voter, candidateProfile.Clan, endorsedClan);

            int honor = voter.GetTraitLevel(DefaultTraits.Honor);
            modifier += honor * (candidateProfile.Legitimacy - 0.5f) * C.KingSelectHonorLegitimacyScale;

            int calculating = voter.GetTraitLevel(DefaultTraits.Calculating);
            float politicalResources = (candidateProfile.InfluenceStanding + candidateProfile.WealthStanding) * 0.5f;
            modifier += calculating * (politicalResources - 0.5f) * C.KingSelectCalculatingPoliticalScale;

            int valor = voter.GetTraitLevel(DefaultTraits.Valor);
            modifier += valor * (candidateProfile.StrengthStanding - 0.5f) * C.KingSelectValorStrengthScale;

            modifier += GetPersonalityAffinity(voter, candidate);
            return modifier;
        }

        private static float GetHonorDisciplineModifier(Hero voter, Clan candidateClan, Clan endorsedClan)
        {
            if (voter == null || candidateClan == null || endorsedClan == null)
                return 0f;

            int honor = voter.GetTraitLevel(DefaultTraits.Honor);
            float discipline = GetTwoStepTraitValue(honor, C.KingSelectHonorDisciplineStep1, C.KingSelectHonorDisciplineStep2);
            if (discipline == 0f)
                return 0f;

            return candidateClan == endorsedClan ? discipline : -discipline;
        }

        private static float GetPersonalityAffinity(Hero voter, Hero candidate)
        {
            return (
                (voter.GetTraitLevel(DefaultTraits.Calculating) * candidate.GetTraitLevel(DefaultTraits.Calculating))
                + (voter.GetTraitLevel(DefaultTraits.Generosity) * candidate.GetTraitLevel(DefaultTraits.Generosity))
                + (voter.GetTraitLevel(DefaultTraits.Honor) * candidate.GetTraitLevel(DefaultTraits.Honor))
                + (voter.GetTraitLevel(DefaultTraits.Mercy) * candidate.GetTraitLevel(DefaultTraits.Mercy))
                + (voter.GetTraitLevel(DefaultTraits.Valor) * candidate.GetTraitLevel(DefaultTraits.Valor)))
                * C.KingSelectPersonalityAffinityScale;
        }

        private static float GetTwoStepTraitValue(int traitLevel, float step1Value, float step2Value)
        {
            if (traitLevel >= 2)
                return step2Value;
            if (traitLevel == 1)
                return step1Value;
            if (traitLevel <= -2)
                return -step2Value;
            if (traitLevel == -1)
                return -step1Value;
            return 0f;
        }

        private static void CacheCandidatePopularity(
            Kingdom kingdom,
            SuccessionElectionProfile election,
            IEnumerable<Clan> candidates)
        {
            election.CandidatePopularity.Clear();
            foreach (Clan candidateClan in candidates.Where(candidate => candidate != null).Distinct())
            {
                election.CandidatePopularity[candidateClan] = CalculateCandidatePopularity(kingdom, candidateClan);
            }
        }

        private static void CacheCandidateProfiles(
            Kingdom kingdom,
            SuccessionElectionProfile election,
            IEnumerable<Clan> eligibleCandidates)
        {
            election.CandidateProfiles.Clear();
            List<Clan> candidates = eligibleCandidates
                .Where(candidate => candidate?.Leader != null)
                .Distinct()
                .ToList();
            if (candidates.Count == 0)
                return;

            List<RawElectionCandidateMetrics> rawMetrics = candidates
                .Select(candidate => BuildRawCandidateMetrics(candidate, kingdom))
                .ToList();
            foreach (RawElectionCandidateMetrics raw in rawMetrics)
            {
                Clan clan = raw.Clan;
                Hero leader = clan.Leader;
                bool strongClaim = HasKingdomTitleClaim(clan, kingdom, FeudalClaimStrength.Strong);
                bool weakClaim = !strongClaim && HasKingdomTitleClaim(clan, kingdom, FeudalClaimStrength.Weak);
                bool dynasticHeir = election.LegitimateDynasticClan == clan;

                float legitimacy = 0f;
                if (clan == kingdom.RulingClan && !election.IsEmergency)
                    legitimacy = MathF.Max(legitimacy, 0.55f);
                if (weakClaim)
                    legitimacy = MathF.Max(legitimacy, 0.65f);
                if (strongClaim)
                    legitimacy = MathF.Max(legitimacy, 0.85f);
                if (dynasticHeir)
                    legitimacy = 1f;

                float averageRelation = election.CandidatePopularity.TryGetValue(clan, out float popularity)
                    ? popularity
                    : raw.Popularity;
                float popularityScore =
                    (MathF.Clamp((averageRelation + 100f) / 200f, 0f, 1f) * 0.45f)
                    + (MathF.Clamp(leader.GetSkillValue(DefaultSkills.Charm) / 300f, 0f, 1f) * 0.20f)
                    + (NormalizeTrait(leader.GetTraitLevel(DefaultTraits.Generosity)) * 0.175f)
                    + (NormalizeTrait(leader.GetTraitLevel(DefaultTraits.Mercy)) * 0.175f);

                election.CandidateProfiles[clan] = new ElectionCandidateProfile
                {
                    Clan = clan,
                    Popularity = MathF.Clamp(popularityScore, 0f, 1f),
                    Legitimacy = MathF.Clamp(legitimacy, 0f, 1f),
                    HierarchyStanding = raw.HierarchyStanding,
                    ClanTierStanding = raw.ClanTierStanding,
                    WealthStanding = NormalizeDiminishingMetric(raw.Wealth, rawMetrics.Select(value => value.Wealth)),
                    InfluenceStanding = NormalizeDiminishingMetric(raw.Influence, rawMetrics.Select(value => value.Influence)),
                    StrengthStanding = NormalizeDiminishingMetric(raw.Strength, rawMetrics.Select(value => value.Strength)),
                    IsDynasticHeir = dynasticHeir,
                    HasStrongClaim = strongClaim,
                    HasWeakClaim = weakClaim
                };
            }
        }

        private static void AssignDynamicEndorsements(SuccessionElectionProfile election)
        {
            election.Endorsements.Clear();
            HashSet<Clan> ballotClans = new HashSet<Clan>(election.BallotClans);
            foreach (FactionType factionType in new[]
            {
                FactionType.Glory,
                FactionType.Nobility,
                FactionType.Liberty
            })
            {
                Clan endorsedClan = election.CandidateProfiles.Values
                    .Where(profile => ballotClans.Contains(profile.Clan))
                    .OrderByDescending(profile =>
                        GetIdeologicalCandidateAffinity(factionType, profile, election)
                        + (GetSupplementalIdeologyModifier(factionType, profile) / 100f))
                    .ThenBy(profile => GetBallotOrder(profile.Clan, election))
                    .ThenBy(profile => profile.Clan.StringId)
                    .Select(profile => profile.Clan)
                    .FirstOrDefault();

                if (endorsedClan != null)
                    election.Endorsements[factionType] = endorsedClan;
            }
        }

        private static float GetIdeologicalCandidateAffinity(
            FactionType factionType,
            ElectionCandidateProfile profile,
            SuccessionElectionProfile election)
        {
            if (profile == null)
                return 0f;

            SuccessionPillar major;
            SuccessionPillar minor;
            switch (factionType)
            {
                case FactionType.Royalists:
                    major = SuccessionPillar.Legitimacy;
                    minor = SuccessionPillar.ClanTier;
                    break;
                case FactionType.Nobility:
                    major = SuccessionPillar.Hierarchy;
                    minor = SuccessionPillar.Influence;
                    break;
                case FactionType.Glory:
                    major = SuccessionPillar.Strength;
                    minor = SuccessionPillar.ClanTier;
                    break;
                case FactionType.Liberty:
                    major = SuccessionPillar.Influence;
                    minor = SuccessionPillar.Wealth;
                    break;
                default:
                    return profile.GeneralStanding;
            }

            // Every faction gives 30% to its major preference, 20% to its minor
            // preference, and 12.5% to each of the remaining four pillars.
            float affinity = Enum.GetValues(typeof(SuccessionPillar))
                .Cast<SuccessionPillar>()
                .Sum(pillar => GetPillarValue(profile, pillar)
                    * BellumCivileConstants.KingSelectNeutralPillarWeight);
            affinity += GetPillarValue(profile, major)
                * (BellumCivileConstants.KingSelectMajorPillarWeight
                    - BellumCivileConstants.KingSelectNeutralPillarWeight);
            affinity += GetPillarValue(profile, minor)
                * (BellumCivileConstants.KingSelectMinorPillarWeight
                    - BellumCivileConstants.KingSelectNeutralPillarWeight);

            return MathF.Clamp(affinity, 0f, 1f);
        }

        private static float GetPillarValue(ElectionCandidateProfile profile, SuccessionPillar pillar)
        {
            switch (pillar)
            {
                case SuccessionPillar.Legitimacy: return profile.Legitimacy;
                case SuccessionPillar.Hierarchy: return profile.HierarchyStanding;
                case SuccessionPillar.ClanTier: return profile.ClanTierStanding;
                case SuccessionPillar.Wealth: return profile.WealthStanding;
                case SuccessionPillar.Influence: return profile.InfluenceStanding;
                case SuccessionPillar.Strength: return profile.StrengthStanding;
                default: return 0f;
            }
        }

        private static float GetSupplementalIdeologyModifier(
            FactionType factionType,
            ElectionCandidateProfile profile)
        {
            if (factionType != FactionType.Liberty || profile == null)
                return 0f;

            return (profile.Popularity - 0.5f) * C.KingSelectPopulistPopularityRange;
        }

        private static int GetBallotOrder(Clan clan, SuccessionElectionProfile election)
        {
            if (clan == election.ProtectedCandidateClan) return 0;
            if (clan == election.SecondNominee) return 1;
            if (clan == election.ThirdNominee) return 2;
            return 3;
        }

        private static float NormalizeTrait(int traitLevel)
        {
            return MathF.Clamp((traitLevel + 2f) / 4f, 0f, 1f);
        }

        private static float CalculateCandidatePopularity(Kingdom kingdom, Clan candidateClan)
        {
            if (kingdom == null || candidateClan?.Leader == null)
                return 0f;

            List<Clan> electorate = kingdom.Clans
                .Where(c => c != null
                    && c != candidateClan
                    && IsEligibleElectionClan(c, kingdom)
                    && !c.Leader.IsDead)
                .ToList();

            if (electorate.Count == 0)
                return 0f;

            return (float)electorate.Average(c => c.Leader.GetRelation(candidateClan.Leader));
        }

        private static Clan GetCandidateClan(DecisionOutcome possibleOutcome)
        {
            object candidateObj = GetCandidateObject(possibleOutcome);
            if (candidateObj is Clan clanObj) return clanObj;
            if (candidateObj is Hero heroObj) return heroObj.Clan;
            return null;
        }

        public static Clan ResolveCandidateClan(DecisionOutcome possibleOutcome)
        {
            return GetCandidateClan(possibleOutcome);
        }

        private static object GetCandidateObject(DecisionOutcome possibleOutcome)
        {
            if (possibleOutcome == null) return null;

            EnsureCandidateReflectionInitialized(possibleOutcome.GetType());

            if (_candidateClanProp != null) return _candidateClanProp.GetValue(possibleOutcome);
            if (_candidateClanField != null) return _candidateClanField.GetValue(possibleOutcome);

            return null;
        }

        private static void EnsureCandidateReflectionInitialized(Type type)
        {
            if (_candidateReflectionInitialized) return;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            Type searchType = type;
            while (searchType != null && _candidateClanProp == null)
            {
                _candidateClanProp = searchType.GetProperty("Clan", flags)
                                  ?? searchType.GetProperty("ClaimantClan", flags)
                                  ?? searchType.GetProperty("King", flags);
                searchType = searchType.BaseType;
            }

            if (_candidateClanProp == null)
            {
                searchType = type;
                while (searchType != null && _candidateClanField == null)
                {
                    _candidateClanField = searchType.GetField("Clan", flags)
                                       ?? searchType.GetField("ClaimantClan", flags)
                                       ?? searchType.GetField("King", flags)
                                       ?? searchType.GetField("_king", flags)
                                       ?? searchType.GetField("_clan", flags);
                    searchType = searchType.BaseType;
                }
            }

            _candidateReflectionInitialized = true;

            if (_candidateClanProp == null && _candidateClanField == null)
            {
                var clanMembers = new System.Text.StringBuilder();
                searchType = type;
                while (searchType != null && searchType != typeof(object))
                {
                    foreach (FieldInfo field in searchType.GetFields(flags))
                    {
                        if (field.FieldType == typeof(Clan) || field.FieldType == typeof(Hero))
                            clanMembers.Append($"  field: {searchType.Name}.{field.Name} ({field.FieldType.Name})\n");
                    }

                    foreach (PropertyInfo property in searchType.GetProperties(flags))
                    {
                        if (property.PropertyType == typeof(Clan) || property.PropertyType == typeof(Hero))
                            clanMembers.Append($"  prop:  {searchType.Name}.{property.Name} ({property.PropertyType.Name})\n");
                    }

                    searchType = searchType.BaseType;
                }

                BellumCivileNotifications.ShowPersonal(
                    $"[BellumCivile] KingSelectionAIPatch: Could not locate candidate hero/clan on '{type.Name}'.\nAvailable members:\n{clanMembers}",
                    BellumNotificationColors.Debug);
            }
        }
    }
}
