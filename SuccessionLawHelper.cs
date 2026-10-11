using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class SuccessionLawHelper
    {
        public static SuccessionLawSet GetLawsForClan(Clan clan)
        {
            return GetLawsForClan(clan, out _);
        }

        public static SuccessionLawSet GetLawsForClan(
            Clan clan,
            out SuccessionConfig.SuccessionRuleScope scope)
        {
            Behaviors.SuccessionLawBehavior behavior = Campaign.Current?
                .GetCampaignBehavior<Behaviors.SuccessionLawBehavior>();
            if (behavior != null)
                return behavior.GetLawsForClan(clan, out scope);

            return SuccessionConfig.Instance.GetDefaultLaws(
                clan?.Culture?.StringId,
                clan?.Kingdom?.StringId,
                out scope);
        }

        public static SuccessionLawSet GetLawsForKingdom(Kingdom kingdom)
        {
            Behaviors.SuccessionLawBehavior behavior = Campaign.Current?
                .GetCampaignBehavior<Behaviors.SuccessionLawBehavior>();
            return behavior != null
                ? behavior.GetLawsForKingdom(kingdom, out _)
                : SuccessionConfig.Instance.GetDefaultLaws(
                    kingdom?.Culture?.StringId,
                    kingdom?.StringId);
        }

        public static int GetSuccessionTypeForClan(Clan clan)
        {
            return SuccessionConfig.ToLegacyType(GetLawsForClan(clan));
        }

        public static int GetSuccessionTypeForClan(
            Clan clan,
            out SuccessionConfig.SuccessionRuleScope scope)
        {
            return SuccessionConfig.ToLegacyType(GetLawsForClan(clan, out scope));
        }

        public static bool CanConfirmSelectedPlayerHeir(
            Hero selectedHeir,
            out Hero legalHeir,
            out int successionType,
            out SuccessionConfig.SuccessionRuleScope scope,
            out bool usedFallbackCandidatePool)
        {
            SuccessionLawSet laws = GetLawsForClan(Clan.PlayerClan, out scope);
            successionType = SuccessionConfig.ToLegacyType(laws);
            legalHeir = null;
            usedFallbackCandidatePool = false;

            if (BellumIntegrationBehavior.IsBarred(selectedHeir)) return false;
            if (!BellumCivileOptions.EnforcePlayerSuccessionLaw)
                return true;

            if (selectedHeir == null)
                return true;

            IEnumerable<Hero> candidates = Clan.PlayerClan?.GetHeirApparents()?.Keys;
            if (!TryResolveLegalPlayerHeir(
                    Hero.MainHero,
                    candidates,
                    out legalHeir,
                    out _,
                    out usedFallbackCandidatePool))
            {
                return true;
            }

            return selectedHeir == legalHeir;
        }

        public static TextObject BuildInvalidPlayerHeirHint(Hero selectedHeir)
        {
            if (BellumIntegrationBehavior.IsBarred(selectedHeir))
                return new TextObject("{=BC_Integration_IllegitimateHeir}This person is excluded from hereditary inheritance by illegitimacy.");
            if (CanConfirmSelectedPlayerHeir(
                    selectedHeir,
                    out Hero legalHeir,
                    out _,
                    out SuccessionConfig.SuccessionRuleScope scope,
                    out _)
                || legalHeir == null)
            {
                return new TextObject("");
            }

            TextObject text = scope == SuccessionConfig.SuccessionRuleScope.Kingdom
                ? new TextObject("{=BC_PlayerSuccession_InvalidKingdomHeirHint}Under your realm's {GENDER_LAW} and {SUCCESSION_LAW} laws, {LEGAL_HEIR.NAME} is the rightful heir of your house.")
                : new TextObject("{=BC_PlayerSuccession_InvalidCultureHeirHint}Under your culture's {GENDER_LAW} and {SUCCESSION_LAW} customs, {LEGAL_HEIR.NAME} is the rightful heir of your house.");
            SetLawVariables(text, GetLawsForClan(Clan.PlayerClan));
            if (legalHeir.CharacterObject != null)
                StringHelpers.SetCharacterProperties("LEGAL_HEIR", legalHeir.CharacterObject, text);
            return text;
        }

        public static TextObject BuildLegalPlayerHeirConfirmation(
            Hero legalHeir,
            int successionType,
            SuccessionConfig.SuccessionRuleScope scope)
        {
            if (legalHeir == null)
                return new TextObject("");

            TextObject text = scope == SuccessionConfig.SuccessionRuleScope.Kingdom
                ? new TextObject("{=BC_PlayerSuccession_ConfirmKingdom}Under your realm's {GENDER_LAW} and {SUCCESSION_LAW} laws, {LEGAL_HEIR.NAME} is the rightful heir of your house. Do you acknowledge {POSSESSIVE} bloodright?")
                : new TextObject("{=BC_PlayerSuccession_ConfirmCulture}Under your culture's {GENDER_LAW} and {SUCCESSION_LAW} customs, {LEGAL_HEIR.NAME} is the rightful heir of your house. Do you acknowledge {POSSESSIVE} bloodright?");
            SetLawVariables(text, GetLawsForClan(Clan.PlayerClan));
            text.SetTextVariable("POSSESSIVE", legalHeir.IsFemale
                ? new TextObject("{=BC_Pronoun_Her}her")
                : new TextObject("{=BC_Pronoun_His}his"));
            if (legalHeir.CharacterObject != null)
                StringHelpers.SetCharacterProperties("LEGAL_HEIR", legalHeir.CharacterObject, text);
            return text;
        }

        public static TextObject GetGenderLawName(GenderSuccessionLaw law) => RealmLawRegistry.Instance.ForGender(law).Name;

        public static TextObject GetGenderLawDescription(GenderSuccessionLaw law) => RealmLawRegistry.Instance.ForGender(law).Description;

        public static bool IsEligibleUnderGenderLaw(Hero hero, GenderSuccessionLaw law)
        {
            if (hero == null)
                return false;

            switch (law)
            {
                case GenderSuccessionLaw.MaleOnly:
                    return !hero.IsFemale;
                case GenderSuccessionLaw.FemaleOnly:
                    return hero.IsFemale;
                default:
                    return true;
            }
        }

        public static bool IsEligibleUnderGenderLaw(Hero hero, SuccessionLawSet laws)
        {
            return IsEligibleUnderGenderLaw(hero, laws.GenderLaw);
        }

        public static bool IsEligibleUnderSuccessionLaws(
            Hero hero,
            Hero successionRoot,
            SuccessionLawSet laws)
        {
            if (!IsEligibleUnderGenderLaw(hero, laws))
                return false;

            return !IsDynasticBloodlineLaw(laws.SuccessionLaw)
                || hero == successionRoot
                || IsBloodRelative(hero, successionRoot);
        }

        public static bool IsDynasticBloodlineLaw(HouseSuccessionLaw law)
        {
            return law == HouseSuccessionLaw.Kinship
                || law == HouseSuccessionLaw.Tanistry;
        }

        public static TextObject GetSuccessionLawName(HouseSuccessionLaw law) => RealmLawRegistry.Instance.ForSuccession(law).Name;

        public static TextObject GetSuccessionLawName(int successionType)
        {
            return GetSuccessionLawName(SuccessionConfig.FromLegacyType(successionType).SuccessionLaw);
        }

        public static TextObject GetSuccessionLawDescription(HouseSuccessionLaw law) => RealmLawRegistry.Instance.ForSuccession(law).Description;

        public static List<Hero> GetOrderedSuccessionLine(Clan clan)
        {
            Hero successionRoot = RegencyBehavior.Instance?.GetLegalClanHead(clan)
                ?? clan?.Leader;
            if (successionRoot == null)
                return new List<Hero>();

            SuccessionLawSet laws = GetLawsForClan(clan);
            return GetOrderedSuccessionLine(clan, successionRoot, laws);
        }

        public static List<Hero> GetLegalSuccessionLine(Clan clan)
        {
            Hero successionRoot = RegencyBehavior.Instance?.GetLegalClanHead(clan)
                ?? clan?.Leader;
            if (successionRoot == null)
                return new List<Hero>();

            return GetLegalSuccessionLine(clan, successionRoot, GetLawsForClan(clan));
        }

        public static List<Hero> GetVisibleSuccessionLine(Clan clan)
        {
            List<Hero> line = GetLegalSuccessionLine(clan);
            Hero ward = RegencyBehavior.Instance?.GetWard(clan);
            if (ward != null && ward.IsAlive && ward.Clan == clan)
            {
                line.Remove(ward);
                line.Insert(0, ward);
            }

            return line;
        }

        public static List<Hero> GetLegalSuccessionLine(
            Clan clan,
            Hero successionRoot,
            SuccessionLawSet laws)
        {
            if (clan == null || successionRoot == null)
                return new List<Hero>();

            return OrderSuccessionCandidates(
                clan.Heroes.Where(hero => IsValidClanSuccessionCandidate(
                    hero,
                    clan,
                    successionRoot,
                    includeUnderage: true)),
                successionRoot,
                laws,
                includeUnderage: true);
        }

        public static List<Hero> GetOrderedSuccessionLine(
            Clan clan,
            Hero successionRoot,
            SuccessionLawSet laws)
        {
            if (clan == null || successionRoot == null)
                return new List<Hero>();

            return OrderSuccessionCandidates(
                clan.Heroes.Where(hero => IsValidClanSuccessionCandidate(hero, clan, successionRoot)),
                successionRoot,
                laws);
        }

        public static TextObject GetSuccessionRuleScopeName(SuccessionConfig.SuccessionRuleScope scope)
        {
            switch (scope)
            {
                case SuccessionConfig.SuccessionRuleScope.Kingdom:
                    return new TextObject("{=BC_SuccessionScope_Kingdom}By Realm");
                case SuccessionConfig.SuccessionRuleScope.Culture:
                    return new TextObject("{=BC_SuccessionScope_Culture}By Culture");
                default:
                    return new TextObject("{=BC_SuccessionScope_Default}Generic Custom");
            }
        }

        public static List<TooltipProperty> BuildSuccessionLineTooltipProperties(Clan clan)
        {
            SuccessionLawSet laws = GetLawsForClan(clan, out SuccessionConfig.SuccessionRuleScope scope);
            List<Hero> heirs = GetVisibleSuccessionLine(clan);
            List<TooltipProperty> properties = new List<TooltipProperty>
            {
                new TooltipProperty(new TextObject("{=BC_SuccessionLine_Title}Line of Succession").ToString(), string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.Title),
                new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator),
                new TooltipProperty(new TextObject("{=BC_SuccessionLine_Scope}Scope").ToString(), GetSuccessionRuleScopeName(scope).ToString(), 0),
                new TooltipProperty(new TextObject("{=BC_GenderLaw_Label}Gender Law").ToString(), GetGenderLawName(laws.GenderLaw).ToString(), 0),
                new TooltipProperty(new TextObject("{=BC_SuccessionLaw_Label}Succession Law").ToString(), GetSuccessionLawName(laws.SuccessionLaw).ToString(), 0),
                new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator)
            };

            if (heirs.Count == 0)
            {
                properties.Add(new TooltipProperty(new TextObject("{=BC_SuccessionLine_Heirs}Heirs").ToString(), new TextObject("{=BC_SuccessionLine_NoValidHeir}No valid heir").ToString(), 0));
                return properties;
            }

            foreach (var heir in heirs.Select((hero, index) => new { Hero = hero, Index = index + 1 }))
                properties.Add(new TooltipProperty(GetOrdinal(heir.Index), heir.Hero?.Name?.ToString() ?? heir.Hero?.StringId ?? "?", 0));
            return properties;
        }

        public static List<TooltipProperty> BuildSuccessionRemainderTooltipProperties(
            IEnumerable<Hero> heirs,
            int firstPosition)
        {
            List<Hero> remainingHeirs = heirs?
                .Where(hero => hero != null && hero.IsAlive)
                .ToList() ?? new List<Hero>();
            List<TooltipProperty> properties = new List<TooltipProperty>
            {
                new TooltipProperty(new TextObject("{=BC_SuccessionLine_Title}Line of Succession").ToString(), string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.Title),
                new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator)
            };

            if (remainingHeirs.Count == 0)
            {
                properties.Add(new TooltipProperty(new TextObject("{=BC_SuccessionLine_Heirs}Heirs").ToString(), new TextObject("{=BC_SuccessionLine_NoValidHeir}No valid heir").ToString(), 0));
                return properties;
            }

            int position = Math.Max(1, firstPosition);
            foreach (Hero heir in remainingHeirs)
            {
                properties.Add(new TooltipProperty(
                    GetOrdinal(position++),
                    heir.Name?.ToString() ?? heir.StringId ?? "?",
                    0));
            }

            return properties;
        }

        public static string BuildSuccessionLineTooltip(Clan clan)
        {
            SuccessionLawSet laws = GetLawsForClan(clan, out SuccessionConfig.SuccessionRuleScope scope);
            List<Hero> heirs = GetVisibleSuccessionLine(clan);
            string heirText = heirs.Count > 0
                ? string.Join("\n", heirs.Select((hero, index) => GetOrdinal(index + 1) + " " + hero.Name))
                : new TextObject("{=BC_SuccessionLine_NoValidHeir}No valid heir").ToString();
            return new TextObject("{=BC_SuccessionLine_Title}Line of Succession") + "\n"
                + GetSuccessionRuleScopeName(scope) + "\n"
                + GetGenderLawName(laws.GenderLaw) + " / " + GetSuccessionLawName(laws.SuccessionLaw)
                + "\n\n" + heirText;
        }

        public static bool TryResolveLegalPlayerHeir(
            Hero deadHero,
            IEnumerable<Hero> candidates,
            out Hero legalHeir,
            out int successionType,
            out bool usedFallbackCandidatePool)
        {
            legalHeir = null;
            SuccessionLawSet laws = GetLawsForClan(Clan.PlayerClan);
            successionType = SuccessionConfig.ToLegacyType(laws);
            usedFallbackCandidatePool = false;
            if (deadHero == null || Clan.PlayerClan == null || candidates == null)
                return false;

            List<Hero> validCandidates = candidates
                .Where(IsValidPlayerHeirCandidate)
                .Distinct()
                .ToList();
            if (validCandidates.Count == 0)
                return false;

            usedFallbackCandidatePool = !validCandidates.Any(hero => IsBloodRelative(hero, deadHero));
            legalHeir = OrderSuccessionCandidates(validCandidates, deadHero, laws).FirstOrDefault();
            return legalHeir != null;
        }

        public static List<Hero> OrderSuccessionCandidates(
            IEnumerable<Hero> candidates,
            Hero successionRoot,
            SuccessionLawSet laws,
            bool includeUnderage = false,
            bool applyInheritanceAdvances = true)
        {
            List<Hero> eligible = candidates?
                .Where(hero => IsBasicSuccessionCandidate(hero, successionRoot, includeUnderage)
                    && (!applyInheritanceAdvances || Behaviors.CrownAccessionBehavior.Instance?.HasInheritanceAdvance(successionRoot, hero) != true)
                    && IsEligibleUnderSuccessionLaws(hero, successionRoot, laws))
                .Distinct()
                .ToList() ?? new List<Hero>();
            if (eligible.Count <= 1)
                return eligible;

            switch (laws.SuccessionLaw)
            {
                case HouseSuccessionLaw.Primogeniture:
                    return OrderLinealSuccession(eligible, successionRoot, laws.GenderLaw, youngestFirst: false);
                case HouseSuccessionLaw.Ultimogeniture:
                    return OrderLinealSuccession(eligible, successionRoot, laws.GenderLaw, youngestFirst: true);
                case HouseSuccessionLaw.ElectiveSeniority:
                    return OrderHouseSeniority(eligible, successionRoot, laws.GenderLaw);
                case HouseSuccessionLaw.Seniority:
                    return OrderLateralSuccession(eligible, successionRoot, laws.GenderLaw);
                default:
                    return eligible
                        .OrderByDescending(hero => GetGenderPreferenceRank(hero, laws.GenderLaw))
                        .ThenByDescending(hero => CalculateElectiveMeritScore(hero, successionRoot, laws.SuccessionLaw))
                        .ThenBy(hero => hero.StringId ?? string.Empty, StringComparer.Ordinal)
                        .ToList();
            }
        }

        public static int CalculateBellumHeirScore(
            Hero candidateHeir,
            Hero deadHero,
            int successionType,
            ref Hero maxSkillHero)
        {
            return CalculateBellumHeirScore(
                candidateHeir,
                deadHero,
                SuccessionConfig.FromLegacyType(successionType),
                ref maxSkillHero);
        }

        public static int CalculateBellumHeirScore(
            Hero candidateHeir,
            Hero deadHero,
            SuccessionLawSet laws,
            ref Hero maxSkillHero)
        {
            if (candidateHeir == null || !candidateHeir.IsAlive || candidateHeir.IsDisabled || candidateHeir.Age < GetAgeOfMajority())
                return 0;
            if (deadHero?.Clan == null || candidateHeir.Clan == null)
                return 0;
            List<Hero> ordered = OrderSuccessionCandidates(
                candidateHeir.Clan.Heroes.Where(hero => IsValidClanSuccessionCandidate(hero, candidateHeir.Clan, deadHero)),
                deadHero,
                laws);
            int index = ordered.IndexOf(candidateHeir);
            return index < 0 ? int.MinValue / 4 : 1000000 - index;
        }

        private static List<Hero> OrderLinealSuccession(
            IReadOnlyCollection<Hero> candidates,
            Hero successionRoot,
            GenderSuccessionLaw genderLaw,
            bool youngestFirst)
        {
            HashSet<Hero> candidateSet = new HashSet<Hero>(candidates);
            List<Hero> ordered = new List<Hero>(candidateSet.Count);
            HashSet<Hero> added = new HashSet<Hero>();
            HashSet<Hero> traversed = new HashSet<Hero>();

            AppendDescendantBranches(
                successionRoot,
                candidateSet,
                ordered,
                added,
                traversed,
                genderLaw,
                youngestFirst,
                0);

            foreach (Hero sibling in OrderFamilyMembers(GetSiblings(successionRoot), genderLaw, youngestFirst))
            {
                AddCandidateIfEligible(sibling, candidateSet, ordered, added);
                AppendDescendantBranches(
                    sibling,
                    candidateSet,
                    ordered,
                    added,
                    traversed,
                    genderLaw,
                    youngestFirst,
                    0);
            }

            AppendRemainingByKinship(
                candidates,
                successionRoot,
                genderLaw,
                youngestFirst,
                ordered,
                added);
            return ordered;
        }

        private static List<Hero> OrderHouseSeniority(
            IReadOnlyCollection<Hero> candidates,
            Hero successionRoot,
            GenderSuccessionLaw genderLaw)
        {
            return candidates
                .OrderBy(hero => GetKinshipDistance(hero, successionRoot) == int.MaxValue ? 1 : 0)
                .ThenByDescending(hero => GetGenderPreferenceRank(hero, genderLaw))
                .ThenByDescending(hero => hero.Age)
                .ThenBy(hero => GetKinshipDistance(hero, successionRoot))
                .ThenBy(hero => hero.StringId ?? string.Empty, StringComparer.Ordinal)
                .ToList();
        }

        private static List<Hero> OrderLateralSuccession(
            IReadOnlyCollection<Hero> candidates,
            Hero successionRoot,
            GenderSuccessionLaw genderLaw)
        {
            HashSet<Hero> candidateSet = new HashSet<Hero>(candidates);
            List<Hero> ordered = new List<Hero>(candidateSet.Count);
            HashSet<Hero> added = new HashSet<Hero>();
            List<Hero> siblings = GetSiblings(successionRoot).ToList();

            foreach (Hero sibling in OrderFamilyMembers(siblings, genderLaw, youngestFirst: false))
                AddCandidateIfEligible(sibling, candidateSet, ordered, added);

            IEnumerable<Hero> nextGeneration = GetChildren(successionRoot)
                .Concat(siblings.SelectMany(GetChildren));
            foreach (Hero member in OrderFamilyMembers(nextGeneration, genderLaw, youngestFirst: false))
                AddCandidateIfEligible(member, candidateSet, ordered, added);

            AppendRemainingByKinship(
                candidates,
                successionRoot,
                genderLaw,
                youngestFirst: false,
                ordered,
                added);
            return ordered;
        }

        private static void AppendDescendantBranches(
            Hero parent,
            ISet<Hero> candidateSet,
            ICollection<Hero> ordered,
            ISet<Hero> added,
            ISet<Hero> traversed,
            GenderSuccessionLaw genderLaw,
            bool youngestFirst,
            int depth)
        {
            if (parent == null || depth >= 12 || !traversed.Add(parent))
                return;

            foreach (Hero child in OrderFamilyMembers(GetChildren(parent), genderLaw, youngestFirst))
            {
                AddCandidateIfEligible(child, candidateSet, ordered, added);
                AppendDescendantBranches(
                    child,
                    candidateSet,
                    ordered,
                    added,
                    traversed,
                    genderLaw,
                    youngestFirst,
                    depth + 1);
            }
        }

        private static void AppendRemainingByKinship(
            IEnumerable<Hero> candidates,
            Hero successionRoot,
            GenderSuccessionLaw genderLaw,
            bool youngestFirst,
            ICollection<Hero> ordered,
            ISet<Hero> added)
        {
            IEnumerable<Hero> remaining = candidates.Where(hero => hero != null && !added.Contains(hero));
            IOrderedEnumerable<Hero> ranked = remaining
                .OrderBy(hero => GetKinshipDistance(hero, successionRoot))
                .ThenByDescending(hero => GetGenderPreferenceRank(hero, genderLaw));
            ranked = youngestFirst
                ? ranked.ThenBy(hero => hero.Age)
                : ranked.ThenByDescending(hero => hero.Age);

            foreach (Hero hero in ranked.ThenBy(hero => hero.StringId ?? string.Empty, StringComparer.Ordinal))
            {
                if (added.Add(hero))
                    ordered.Add(hero);
            }
        }

        private static IEnumerable<Hero> OrderFamilyMembers(
            IEnumerable<Hero> family,
            GenderSuccessionLaw genderLaw,
            bool youngestFirst)
        {
            IEnumerable<Hero> distinct = family?.Where(hero => hero != null).Distinct()
                ?? Enumerable.Empty<Hero>();
            IOrderedEnumerable<Hero> ordered = distinct
                .OrderByDescending(hero => GetGenderPreferenceRank(hero, genderLaw));
            ordered = youngestFirst
                ? ordered.ThenBy(hero => hero.Age)
                : ordered.ThenByDescending(hero => hero.Age);
            return ordered.ThenBy(hero => hero.StringId ?? string.Empty, StringComparer.Ordinal);
        }

        private static void AddCandidateIfEligible(
            Hero hero,
            ISet<Hero> candidates,
            ICollection<Hero> ordered,
            ISet<Hero> added)
        {
            if (hero != null && candidates.Contains(hero) && added.Add(hero))
                ordered.Add(hero);
        }

        private static int CalculateElectiveMeritScore(
            Hero candidate,
            Hero successionRoot,
            HouseSuccessionLaw law)
        {
            switch (law)
            {
                case HouseSuccessionLaw.ShuraCouncil:
                    return CalculateShuraCouncilScore(candidate);
                case HouseSuccessionLaw.Tanistry:
                    return CalculateTanistryScore(candidate, successionRoot);
                case HouseSuccessionLaw.Kinship:
                    return CalculateBloodlineElectiveScore(candidate, successionRoot);
                case HouseSuccessionLaw.MilitaryAcclamation:
                    return CalculateMartialElectiveScore(candidate);
                default:
                    return 0;
            }
        }

        private static int CalculateShuraCouncilScore(Hero candidate)
        {
            int experienceYears = (int)Math.Min(40f, Math.Max(0f, candidate.Age - 18f));
            int wealth = Math.Min(500, Math.Max(0, candidate.Gold) / 1000);
            return candidate.GetSkillValue(DefaultSkills.Charm) * 8
                + candidate.GetSkillValue(DefaultSkills.Steward) * 7
                + candidate.GetSkillValue(DefaultSkills.Leadership) * 5
                + candidate.GetSkillValue(DefaultSkills.Trade) * 3
                + candidate.GetSkillValue(DefaultSkills.Tactics) * 2
                + experienceYears * 20
                + wealth
                + (candidate.GovernorOf != null ? 300 : 0);
        }

        private static int CalculateTanistryScore(Hero candidate, Hero successionRoot)
        {
            int bestMelee = Math.Max(
                candidate.GetSkillValue(DefaultSkills.OneHanded),
                Math.Max(
                    candidate.GetSkillValue(DefaultSkills.TwoHanded),
                    candidate.GetSkillValue(DefaultSkills.Polearm)));
            int bestRanged = Math.Max(
                candidate.GetSkillValue(DefaultSkills.Bow),
                Math.Max(
                    candidate.GetSkillValue(DefaultSkills.Crossbow),
                    candidate.GetSkillValue(DefaultSkills.Throwing)));
            return candidate.GetSkillValue(DefaultSkills.Leadership) * 7
                + candidate.GetSkillValue(DefaultSkills.Tactics) * 5
                + bestMelee * 6
                + bestRanged * 4
                + candidate.GetSkillValue(DefaultSkills.Athletics) * 3
                + candidate.GetSkillValue(DefaultSkills.Riding) * 3
                + GetTanistryKinshipBonus(candidate, successionRoot);
        }

        private static int CalculateBloodlineElectiveScore(Hero candidate, Hero successionRoot)
        {
            return candidate.GetSkillValue(DefaultSkills.Leadership) * 7
                + candidate.GetSkillValue(DefaultSkills.Charm) * 7
                + candidate.GetSkillValue(DefaultSkills.Steward) * 5
                + candidate.GetSkillValue(DefaultSkills.Tactics) * 3
                + GetBloodlineProximityBonus(candidate, successionRoot);
        }

        private static int CalculateMartialElectiveScore(Hero candidate)
        {
            int bestMelee = Math.Max(
                candidate.GetSkillValue(DefaultSkills.OneHanded),
                Math.Max(
                    candidate.GetSkillValue(DefaultSkills.TwoHanded),
                    candidate.GetSkillValue(DefaultSkills.Polearm)));
            int bestRanged = Math.Max(
                candidate.GetSkillValue(DefaultSkills.Bow),
                Math.Max(
                    candidate.GetSkillValue(DefaultSkills.Crossbow),
                    candidate.GetSkillValue(DefaultSkills.Throwing)));
            var party = candidate.PartyBelongedTo;
            bool leadsParty = party != null && party.LeaderHero == candidate;
            int command = leadsParty ? 300 : 0;
            if (leadsParty && party.Army != null && party.Army.LeaderParty == party)
                command += 700;

            return candidate.GetSkillValue(DefaultSkills.Leadership) * 10
                + candidate.GetSkillValue(DefaultSkills.Tactics) * 10
                + bestMelee * 3
                + bestRanged * 2
                + candidate.GetSkillValue(DefaultSkills.Riding) * 2
                + candidate.GetSkillValue(DefaultSkills.Athletics)
                + command;
        }

        private static int GetTanistryKinshipBonus(Hero candidate, Hero successionRoot)
        {
            if (candidate?.Father == successionRoot || candidate?.Mother == successionRoot)
                return 300;
            if (AreSiblings(candidate, successionRoot))
                return 200;

            int distance = GetKinshipDistance(candidate, successionRoot);
            if (distance <= 2) return 250;
            if (distance <= 3) return 150;
            return distance == int.MaxValue ? 0 : 100;
        }

        private static int GetBloodlineProximityBonus(Hero candidate, Hero successionRoot)
        {
            if (candidate?.Father == successionRoot || candidate?.Mother == successionRoot)
                return 2000;
            if (candidate == successionRoot?.Father || candidate == successionRoot?.Mother)
                return 1500;
            if (AreSiblings(candidate, successionRoot))
                return 1200;

            int distance = GetKinshipDistance(candidate, successionRoot);
            if (IsDescendantOf(candidate, successionRoot, 2)) return 1500;
            if (distance <= 3) return 800;
            return distance == int.MaxValue ? 0 : 400;
        }

        internal static int GetGenderPreferenceRank(Hero hero, GenderSuccessionLaw law)
        {
            if (hero == null)
                return 0;
            if (law == GenderSuccessionLaw.MalePreference)
                return hero.IsFemale ? 0 : 1;
            if (law == GenderSuccessionLaw.FemalePreference)
                return hero.IsFemale ? 1 : 0;
            return 0;
        }

        private static IEnumerable<Hero> GetChildren(Hero hero)
        {
            return hero?.Children?.Where(child => child != null) ?? Enumerable.Empty<Hero>();
        }

        private static IEnumerable<Hero> GetSiblings(Hero hero)
        {
            if (hero == null)
                return Enumerable.Empty<Hero>();

            return GetChildren(hero.Father)
                .Concat(GetChildren(hero.Mother))
                .Where(relative => relative != hero)
                .Distinct();
        }

        private static bool IsDescendantOf(Hero candidate, Hero ancestor, int maximumDepth)
        {
            if (candidate == null || ancestor == null || maximumDepth <= 0)
                return false;
            if (candidate.Father == ancestor || candidate.Mother == ancestor)
                return true;
            return IsDescendantOf(candidate.Father, ancestor, maximumDepth - 1)
                || IsDescendantOf(candidate.Mother, ancestor, maximumDepth - 1);
        }

        private static int GetKinshipDistance(Hero first, Hero second)
        {
            if (first == null || second == null)
                return int.MaxValue;
            if (first == second)
                return 0;

            Queue<Hero> queue = new Queue<Hero>();
            Dictionary<Hero, int> distances = new Dictionary<Hero, int>();
            queue.Enqueue(first);
            distances[first] = 0;
            while (queue.Count > 0)
            {
                Hero current = queue.Dequeue();
                int distance = distances[current];
                if (distance >= 12)
                    continue;

                foreach (Hero relative in GetChildren(current)
                    .Concat(new[] { current.Father, current.Mother })
                    .Where(relative => relative != null))
                {
                    if (distances.ContainsKey(relative))
                        continue;
                    if (relative == second)
                        return distance + 1;

                    distances[relative] = distance + 1;
                    queue.Enqueue(relative);
                }
            }

            return int.MaxValue;
        }

        internal static bool IsBasicSuccessionCandidate(
            Hero hero,
            Hero successionRoot,
            bool includeUnderage)
        {
            return hero != null
                && hero != successionRoot
                && !BellumIntegrationBehavior.IsBarred(hero)
                && hero.IsAlive
                && hero.DeathMark == TaleWorlds.CampaignSystem.Actions.KillCharacterAction.KillCharacterActionDetail.None
                && !hero.IsDisabled
                && !(RegencyBehavior.Instance?.IsGeneratedRegent(hero) ?? false)
                && (includeUnderage || hero.Age >= GetAgeOfMajority());
        }

        private static void SetLawVariables(TextObject text, SuccessionLawSet laws)
        {
            text.SetTextVariable("GENDER_LAW", GetGenderLawName(laws.GenderLaw));
            text.SetTextVariable("SUCCESSION_LAW", GetSuccessionLawName(laws.SuccessionLaw));
        }

        private static bool IsValidPlayerHeirCandidate(Hero hero)
        {
            return hero != null && hero.Clan == Clan.PlayerClan && hero != Hero.MainHero
                && !BellumIntegrationBehavior.IsBarred(hero)
                && hero.IsAlive && !hero.IsNotSpawned && !hero.IsDisabled
                && !hero.IsWanderer && !hero.IsNotable
                && hero.Age >= Campaign.Current.Models.AgeModel.HeroComesOfAge;
        }

        private static bool IsValidClanSuccessionCandidate(
            Hero hero,
            Clan clan,
            Hero successionRoot,
            bool includeUnderage = false)
        {
            return hero != null && clan != null && hero.Clan == clan && hero != successionRoot
                && !BellumIntegrationBehavior.IsBarred(hero)
                && hero.IsAlive
                && hero.DeathMark == TaleWorlds.CampaignSystem.Actions.KillCharacterAction.KillCharacterActionDetail.None
                && (!hero.IsNotSpawned || (includeUnderage && hero.IsChild))
                && !hero.IsDisabled
                && !(RegencyBehavior.Instance?.IsGeneratedRegent(hero) ?? false)
                && !hero.IsWanderer && !hero.IsNotable
                && (includeUnderage || hero.Age >= GetAgeOfMajority());
        }

        public static int GetAgeOfMajority()
        {
            return Campaign.Current?.Models?.AgeModel?.HeroComesOfAge ?? 18;
        }

        public static bool IsBloodRelative(Hero first, Hero second)
        {
            if (first == null || second == null) return false;
            if (first.Father == second || first.Mother == second || second.Father == first || second.Mother == first)
                return true;
            if (AreSiblings(first, second)) return true;
            HashSet<Hero> firstAncestors = GetAncestors(first, 4);
            HashSet<Hero> secondAncestors = GetAncestors(second, 4);
            return firstAncestors.Contains(second) || secondAncestors.Contains(first)
                || firstAncestors.Any(ancestor => ancestor != null && secondAncestors.Contains(ancestor));
        }

        private static HashSet<Hero> GetAncestors(Hero hero, int depth)
        {
            HashSet<Hero> ancestors = new HashSet<Hero>();
            AddAncestors(hero, depth, ancestors);
            return ancestors;
        }

        private static void AddAncestors(Hero hero, int depth, ISet<Hero> ancestors)
        {
            if (hero == null || depth <= 0) return;
            if (hero.Father != null && ancestors.Add(hero.Father)) AddAncestors(hero.Father, depth - 1, ancestors);
            if (hero.Mother != null && ancestors.Add(hero.Mother)) AddAncestors(hero.Mother, depth - 1, ancestors);
        }

        private static bool AreSiblings(Hero first, Hero second)
        {
            if (first == null || second == null) return false;
            return (first.Father != null && first.Father == second.Father)
                || (first.Mother != null && first.Mother == second.Mother);
        }

        private static string GetOrdinal(int number)
        {
            int lastTwoDigits = number % 100;
            TextObject suffix;
            if (lastTwoDigits >= 11 && lastTwoDigits <= 13)
                suffix = new TextObject("{=BC_OrdinalSuffix_Th}th");
            else if (number % 10 == 1)
                suffix = new TextObject("{=BC_OrdinalSuffix_St}st");
            else if (number % 10 == 2)
                suffix = new TextObject("{=BC_OrdinalSuffix_Nd}nd");
            else if (number % 10 == 3)
                suffix = new TextObject("{=BC_OrdinalSuffix_Rd}rd");
            else
                suffix = new TextObject("{=BC_OrdinalSuffix_Th}th");
            TextObject text = new TextObject("{=BC_OrdinalFormat}{NUMBER}{SUFFIX}");
            text.SetTextVariable("NUMBER", number);
            text.SetTextVariable("SUFFIX", suffix);
            return text.ToString();
        }
    }
}
