using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class FeudalTitleDisplayHelper
    {
        private static readonly Dictionary<Clan, FeudalTitleRecord> HighestHeldTitleCache = new Dictionary<Clan, FeudalTitleRecord>();
        private static FeudalTitleBehavior _highestHeldTitleCacheBehavior;
        private static int _highestHeldTitleCacheRevision = -1;

        public sealed class HeldTitleDisplayEntry
        {
            public FeudalTitleRecord Title;
            public string Name;
            public string Status;
        }

        public sealed class ClaimDisplayEntry
        {
            public FeudalClaimRecord Claim;
            public FeudalTitleRecord Title;
            public string TitleName;
            public string Strength;
        }

        public static List<HeldTitleDisplayEntry> GetHeldTitleEntries(Clan clan)
        {
            FeudalTitleBehavior behavior = FeudalTitleBehavior.Instance;
            if (behavior == null || clan == null)
                return new List<HeldTitleDisplayEntry>();

            List<FeudalTitleRecord> titles = behavior.GetTitlesHeldByClan(clan, deJure: true)
                .Concat(behavior.GetTitlesHeldByClan(clan, deJure: false))
                .Where(title => title != null && title.IsActive)
                .GroupBy(title => title.TitleId)
                .Select(group => group.First())
                .OrderByDescending(title => title.TitleType)
                .ThenBy(title => title.Name ?? string.Empty)
                .ToList();

            return titles
                .Select(title => new HeldTitleDisplayEntry
                {
                    Title = title,
                    Name = FormatHeldTitleName(clan, title),
                    Status = GetHeldTitleStatus(clan, title)
                })
                .ToList();
        }

        public static List<ClaimDisplayEntry> GetClaimEntries(Clan clan)
        {
            FeudalTitleBehavior behavior = FeudalTitleBehavior.Instance;
            if (behavior == null || clan == null)
                return new List<ClaimDisplayEntry>();

            return behavior.GetActiveClaimsByClan(clan)
                .Select(claim => new { Claim = claim, Title = behavior.GetTitle(claim.TargetTitleId) })
                .Where(entry => entry.Claim != null && entry.Title != null && entry.Title.IsActive)
                .OrderByDescending(entry => entry.Claim.Strength)
                .ThenByDescending(entry => entry.Title.TitleType)
                .ThenBy(entry => entry.Title.Name ?? string.Empty)
                .Select(entry => new ClaimDisplayEntry
                {
                    Claim = entry.Claim,
                    Title = entry.Title,
                    TitleName = FormatTitleName(entry.Title, clan),
                    Strength = FormatClaimStrength(entry.Claim.Strength)
                })
                .ToList();
        }

        public static bool TryGetHighestDisplayTitle(Hero hero, out string displayTitle)
        {
            return TryGetDisplayStyle(hero, out displayTitle);
        }

        public static string FormatRulerPeaceName(Kingdom kingdom)
        {
            Clan rulingClan = kingdom?.RulingClan;
            Hero ruler = rulingClan?.Leader;
            FeudalTitleRecord sovereignTitle = GetSovereignTitleForKingdom(kingdom);
            string rulerTitle = sovereignTitle != null
                ? ResolveDisplayTitle(sovereignTitle, ruler?.IsFemale == true, rulingClan)
                : GetDefaultDisplayTitle(FeudalTitleType.Kingdom, ruler?.IsFemale == true).ToString();

            if (string.IsNullOrWhiteSpace(rulerTitle))
                rulerTitle = new TextObject("{=BC_TitleDisplay_King}King").ToString();

            TextObject text = new TextObject("{=BC_RoyalPeace_Title}{RULER_TITLE}'s Peace");
            text.SetTextVariable("RULER_TITLE", rulerTitle);
            return text.ToString();
        }

        public static string ResolveConciseSovereignTitleName(Kingdom kingdom)
        {
            if (kingdom == null)
                return string.Empty;

            FeudalTitleBehavior behavior = FeudalTitleBehavior.Instance
                ?? Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            Clan rulingClan = kingdom.RulingClan;
            FeudalTitleRecord sovereignTitle = behavior?.GetRealmSovereignTitle(
                kingdom,
                FeudalHierarchyMode.DeFacto);

            if (sovereignTitle != null
                && sovereignTitle.IsActive
                && rulingClan != null
                && string.Equals(
                    sovereignTitle.DeFactoHolderClanId,
                    rulingClan.StringId,
                    StringComparison.Ordinal))
            {
                string titleRoot = CleanTerritorialRoot(new TextObject(sovereignTitle.Name).ToString());
                if (!string.IsNullOrWhiteSpace(titleRoot))
                    return titleRoot;
            }

            return DynamicKingdomTitleNameHelper.GetNativeName(kingdom)
                ?? kingdom.Name?.ToString()
                ?? string.Empty;
        }

        public static bool TryGetDisplayStyle(Hero hero, out string displayTitle)
        {
            return TryGetDisplayStyle(hero, out displayTitle, out _, out _);
        }

        public static bool TryFormatHeroName(Hero hero, string heroName, out string formattedName)
        {
            formattedName = heroName;
            if (string.IsNullOrWhiteSpace(heroName)
                || !TryGetDisplayStyle(
                    hero,
                    out string displayTitle,
                    out FeudalTitleConfig.RankStyle rankStyle,
                    out FeudalTitleRecord sourceTitle)
                || string.IsNullOrWhiteSpace(displayTitle))
            {
                return false;
            }

            string heldTitle = displayTitle;
            if (sourceTitle != null)
            {
                string territory = CleanTerritorialRoot(LocalizeConfiguredText(sourceTitle.Name));
                if (!string.IsNullOrWhiteSpace(territory))
                    heldTitle = FormatHeldTitle(rankStyle?.HeldTitleFormat, displayTitle, territory);
            }

            string format = string.IsNullOrWhiteSpace(rankStyle?.HeroNameFormat)
                ? "{=BC_TitleDisplay_HeroNameFormat}{DISPLAY_TITLE} {HERO_NAME}"
                : rankStyle.HeroNameFormat;
            TextObject text = new TextObject(format);
            text.SetTextVariable("DISPLAY_TITLE", new TextObject("{=!}" + displayTitle));
            text.SetTextVariable("HELD_TITLE", new TextObject("{=!}" + heldTitle));
            text.SetTextVariable("HERO_NAME", new TextObject("{=!}" + heroName));
            formattedName = text.ToString().Trim();

            if (RegencyBehavior.Instance?.TryGetWardForRegent(hero, out Hero ward) == true)
            {
                TextObject regentName = new TextObject("{=BC_TitleDisplay_RegentSuffix}{FORMATTED_NAME}, regent to {WARD_NAME}");
                regentName.SetTextVariable("FORMATTED_NAME", new TextObject("{=!}" + formattedName));
                regentName.SetTextVariable("WARD_NAME", ward.FirstName ?? ward.Name);
                formattedName = regentName.ToString().Trim();
            }
            return !string.IsNullOrWhiteSpace(formattedName);
        }

        private static bool TryGetDisplayStyle(
            Hero hero,
            out string displayTitle,
            out FeudalTitleConfig.RankStyle rankStyle,
            out FeudalTitleRecord sourceTitle)
        {
            displayTitle = null;
            rankStyle = null;
            sourceTitle = null;
            if (TryGetMercenaryLeaderDisplayStyle(hero, out displayTitle, out rankStyle))
                return true;
            if (!IsEligibleStyledHero(hero))
                return false;

            // A regent administers the ward's estate but does not personally inherit its rank.
            if (RegencyBehavior.Instance?.IsActingRegent(hero) == true
                && TryGetRegentNobleDisplayStyle(hero, out displayTitle, out rankStyle))
            {
                return true;
            }

            if (TryGetHolderDisplayStyle(hero, out displayTitle, out rankStyle, out sourceTitle))
                return true;

            if (TryGetSpouseDisplayStyle(hero, out displayTitle, out rankStyle, out sourceTitle))
                return true;

            if (TryGetRegencyWardDisplayStyle(hero, out displayTitle, out rankStyle, out sourceTitle))
                return true;

            if (TryGetCrownHeirDisplayStyle(hero, out displayTitle, out rankStyle, out sourceTitle))
                return true;

            if (TryGetRulerChildDisplayStyle(hero, out displayTitle, out rankStyle, out sourceTitle))
                return true;

            if (TryGetWandererDisplayStyle(hero, out displayTitle, out rankStyle))
                return true;

            if (TryGetNobleDisplayStyle(hero, out displayTitle, out rankStyle))
                return true;

            displayTitle = null;
            rankStyle = null;
            return false;
        }

        public static string FormatTitleName(FeudalTitleRecord title)
        {
            return FormatTitleName(title, ResolveClan(title?.DeFactoHolderClanId) ?? ResolveClan(title?.DeJureHolderClanId));
        }

        public static string FormatTitleName(FeudalTitleRecord title, Clan styleClan)
        {
            if (title == null)
                return string.Empty;

            return FormatTitleName(title.TitleType, title.Name, styleClan, ResolveKingdom(title.AssociatedKingdomId), title.FallbackCultureRef);
        }

        public static string FormatTitleName(FeudalTitleType type, string titleRoot, Clan styleClan, Kingdom fallbackKingdom = null, string fallbackCultureRef = "")
        {
            string root = CleanTerritorialRoot(LocalizeConfiguredText(titleRoot));
            if (string.IsNullOrWhiteSpace(root))
                root = type.ToString();

            FeudalTitleConfig.RankStyle style = ResolveRankStyle(type, styleClan, fallbackKingdom, fallbackCultureRef);
            string titleNoun = ResolveLandedTitleNoun(type, style);
            if (string.IsNullOrWhiteSpace(titleNoun))
                return root;

            return FormatLandedTitle(style?.TitleFormat, titleNoun, root);
        }

        public static string GetLandedTitleNoun(FeudalTitleType type, Clan styleClan, Kingdom fallbackKingdom = null, string fallbackCultureRef = "")
        {
            FeudalTitleConfig.RankStyle style = ResolveRankStyle(type, styleClan, fallbackKingdom, fallbackCultureRef);
            return ResolveLandedTitleNoun(type, style);
        }

        internal static TextObject FormatRealmName(FeudalTitleType type, TextObject root, Clan styleClan,
            Kingdom kingdom, string fallbackCultureRef = "")
        {
            FeudalTitleConfig.RankStyle style = ResolveRankStyle(type, styleClan, kingdom, fallbackCultureRef);
            TextObject noun = !string.IsNullOrWhiteSpace(style?.TitleName)
                ? new TextObject(style.TitleName) : GetDefaultLandedTitleNoun(type);
            if (noun.IsEmpty()) return root;
            TextObject text = new TextObject(!string.IsNullOrWhiteSpace(style?.TitleFormat)
                ? style.TitleFormat : "{=BC_TitleDisplay_LandedTitle}{TITLE_NOUN} of {TITLE_NAME}");
            // Keep nested TextObjects, including translation IDs and grammatical variables.
            text.SetTextVariable("TITLE_NOUN", noun);
            text.SetTextVariable("TITLE_NAME", root);
            return text;
        }

        private static string FormatHeldTitleName(Clan clan, FeudalTitleRecord title)
        {
            if (title == null)
                return string.Empty;

            string rank = ResolveDisplayTitle(title, clan?.Leader?.IsFemale == true, clan);
            string legalName = FormatTitleName(title, clan);
            if (string.IsNullOrWhiteSpace(rank) || string.IsNullOrWhiteSpace(legalName))
                return legalName;

            string territory = CleanTerritorialRoot(LocalizeConfiguredText(title.Name));
            if (string.IsNullOrWhiteSpace(territory))
                return legalName;

            FeudalTitleConfig.RankStyle style = ResolveRankStyle(title.TitleType, clan, ResolveKingdom(title.AssociatedKingdomId), title.FallbackCultureRef);
            return FormatHeldTitle(style?.HeldTitleFormat, rank, territory);
        }

        private static string GetHeldTitleStatus(Clan clan, FeudalTitleRecord title)
        {
            if (clan == null || title == null)
                return string.Empty;

            bool deJure = string.Equals(title.DeJureHolderClanId, clan.StringId, StringComparison.Ordinal);
            bool deFacto = string.Equals(title.DeFactoHolderClanId, clan.StringId, StringComparison.Ordinal);

            if (deJure && deFacto)
                return string.Empty;
            if (deJure || deFacto)
                return GetTenureLabel(deJure, deFacto);
            return string.Empty;
        }

        internal static string GetTenureLabel(bool deJure, bool deFacto)
        {
            if (deJure && deFacto)
                return new TextObject("{=BC_TitleDisplay_FullTenure}De facto and de jure").ToString();
            if (deJure)
                return new TextObject("{=BC_TitleDisplay_DeJureOnly}de jure only").ToString();
            if (deFacto)
                return new TextObject("{=BC_TitleDisplay_DeFactoOnly}de facto only").ToString();
            return new TextObject("{=BC_Hierarchy_Vacant}Vacant").ToString();
        }

        internal static string GetHierarchyTenure(FeudalTitleRecord title)
        {
            bool hasPossessor = !string.IsNullOrWhiteSpace(title?.DeFactoHolderClanId);
            bool hasLegalHolder = !string.IsNullOrWhiteSpace(title?.DeJureHolderClanId);
            // Hierarchy portraits represent the possessor; absent one, describe the legal holder.
            return GetTenureLabel(hasLegalHolder && (!hasPossessor
                || string.Equals(title.DeJureHolderClanId, title.DeFactoHolderClanId, StringComparison.Ordinal)), hasPossessor);
        }

        private static string FormatClaimStrength(FeudalClaimStrength strength)
        {
            return strength == FeudalClaimStrength.Strong
                ? new TextObject("{=BC_TitleDisplay_StrongClaim}Strong").ToString()
                : new TextObject("{=BC_TitleDisplay_WeakClaim}Weak").ToString();
        }

        private static bool TryGetHolderDisplayStyle(
            Hero hero,
            out string displayTitle,
            out FeudalTitleConfig.RankStyle rankStyle,
            out FeudalTitleRecord sourceTitle)
        {
            displayTitle = null;
            rankStyle = null;
            sourceTitle = null;
            if (hero?.Clan == null || hero.Clan.Leader != hero)
                return false;

            FeudalTitleRecord title = GetHighestHeldTitle(hero.Clan);
            if (title == null)
                return false;

            displayTitle = ResolveDisplayTitle(title, hero.IsFemale, hero.Clan);
            rankStyle = ResolveRankStyle(title.TitleType, hero.Clan, ResolveKingdom(title.AssociatedKingdomId), title.FallbackCultureRef);
            sourceTitle = title;
            return !string.IsNullOrWhiteSpace(displayTitle);
        }

        private static bool TryGetSpouseDisplayStyle(
            Hero hero,
            out string displayTitle,
            out FeudalTitleConfig.RankStyle rankStyle,
            out FeudalTitleRecord sourceTitle)
        {
            displayTitle = null;
            rankStyle = null;
            sourceTitle = null;
            Hero spouse = hero?.Spouse;
            if (spouse?.Clan == null || spouse.Clan.Leader != spouse)
                return false;

            FeudalTitleRecord title = GetHighestHeldTitle(spouse.Clan);
            if (title == null)
                return false;

            displayTitle = ResolveSpouseTitle(title, hero.IsFemale, spouse.Clan);
            rankStyle = ResolveRankStyle(title.TitleType, spouse.Clan, ResolveKingdom(title.AssociatedKingdomId), title.FallbackCultureRef);
            sourceTitle = title;
            return !string.IsNullOrWhiteSpace(displayTitle);
        }

        private static bool TryGetCrownHeirDisplayStyle(
            Hero hero,
            out string displayTitle,
            out FeudalTitleConfig.RankStyle rankStyle,
            out FeudalTitleRecord sourceTitle)
        {
            displayTitle = null;
            rankStyle = null;
            sourceTitle = null;
            DynasticHeirBehavior behavior = DynasticHeirBehavior.Instance;
            if (behavior == null)
                return false;

            foreach (Kingdom kingdom in behavior.GetReigningHeirRealmsForDisplay(hero))
            {
                if (kingdom == null || kingdom.IsEliminated || kingdom.RulingClan == null)
                    continue;

                if (!behavior.TryGetCachedReigningDynasticHeir(kingdom, out Hero dynasticHeir) || dynasticHeir != hero)
                    continue;

                FeudalTitleRecord sovereignTitle = GetSovereignTitleForKingdom(kingdom);
                if (sovereignTitle == null)
                    continue;

                displayTitle = ResolveHeirTitle(sovereignTitle, hero.IsFemale, hero.Clan);
                rankStyle = ResolveRankStyle(
                    sovereignTitle.TitleType,
                    hero.Clan,
                    ResolveKingdom(sovereignTitle.AssociatedKingdomId),
                    sovereignTitle.FallbackCultureRef);
                sourceTitle = sovereignTitle;
                return !string.IsNullOrWhiteSpace(displayTitle);
            }

            return false;
        }

        private static bool TryGetRegencyWardDisplayStyle(
            Hero hero,
            out string displayTitle,
            out FeudalTitleConfig.RankStyle rankStyle,
            out FeudalTitleRecord sourceTitle)
        {
            displayTitle = null;
            rankStyle = null;
            sourceTitle = null;
            Clan clan = hero?.Clan;
            Kingdom kingdom = clan?.Kingdom;
            if (clan == null
                || kingdom == null
                || kingdom.IsEliminated
                || kingdom.RulingClan != clan
                || RegencyBehavior.Instance?.GetWard(clan) != hero)
            {
                return false;
            }

            FeudalTitleRecord sovereignTitle = GetSovereignTitleForKingdom(kingdom);
            if (sovereignTitle == null)
                return false;

            displayTitle = ResolveHeirTitle(sovereignTitle, hero.IsFemale, clan);
            rankStyle = ResolveRankStyle(
                sovereignTitle.TitleType,
                clan,
                ResolveKingdom(sovereignTitle.AssociatedKingdomId),
                sovereignTitle.FallbackCultureRef);
            sourceTitle = sovereignTitle;
            return !string.IsNullOrWhiteSpace(displayTitle);
        }

        private static bool TryGetRulerChildDisplayStyle(
            Hero hero,
            out string displayTitle,
            out FeudalTitleConfig.RankStyle rankStyle,
            out FeudalTitleRecord sourceTitle)
        {
            displayTitle = null;
            rankStyle = null;
            sourceTitle = null;
            if (hero?.Clan == null)
                return false;

            Kingdom kingdom = hero.Clan.Kingdom;
            Hero ruler = kingdom?.Leader;
            if (kingdom == null || kingdom.IsEliminated || ruler == null || hero.Clan != kingdom.RulingClan)
                return false;

            if (hero.Father != ruler && hero.Mother != ruler)
                return false;

            FeudalTitleRecord sovereignTitle = GetSovereignTitleForKingdom(kingdom);
            if (sovereignTitle == null)
                return false;

            displayTitle = ResolveChildTitle(sovereignTitle, hero.IsFemale, hero.Clan);
            rankStyle = ResolveRankStyle(
                sovereignTitle.TitleType,
                hero.Clan,
                ResolveKingdom(sovereignTitle.AssociatedKingdomId),
                sovereignTitle.FallbackCultureRef);
            sourceTitle = sovereignTitle;
            return !string.IsNullOrWhiteSpace(displayTitle);
        }

        private static bool TryGetNobleDisplayStyle(
            Hero hero,
            out string displayTitle,
            out FeudalTitleConfig.RankStyle rankStyle)
        {
            displayTitle = null;
            rankStyle = null;
            if (hero?.Clan?.Kingdom == null || hero.Clan.IsUnderMercenaryService || hero.Clan.IsEliminated)
                return false;

            // Companions are retainers of the clan, not members of its peerage.
            // A configured wanderer rank may style them through the dedicated path,
            // but they must never inherit the generic Lord/Lady fallback.
            if (IsWandererRetainer(hero))
                return false;

            bool isPlayerClan = hero.Clan == Clan.PlayerClan;
            if ((!hero.Clan.IsNoble && !isPlayerClan) || hero.IsChild)
                return false;

            if (!hero.IsLord && hero.Clan.Leader != hero && hero.Spouse?.Clan?.Leader != hero.Spouse)
                return false;

            FeudalTitleRecord sovereignTitle = GetSovereignTitleForKingdom(hero.Clan.Kingdom);
            bool landlessLeader = hero.Clan.Leader == hero && GetHighestHeldTitle(hero.Clan) == null;
            rankStyle = ResolveNobleRankStyle(
                sovereignTitle?.TitleType ?? FeudalTitleType.Kingdom,
                hero.Clan,
                ResolveKingdom(sovereignTitle?.AssociatedKingdomId),
                sovereignTitle?.FallbackCultureRef,
                landlessLeader);
            displayTitle = landlessLeader
                ? ResolveLandlessLeaderTitle(rankStyle, hero.IsFemale)
                : ResolveNobleTitle(rankStyle, hero.IsFemale);
            return !string.IsNullOrWhiteSpace(displayTitle);
        }

        private static bool TryGetRegentNobleDisplayStyle(
            Hero hero,
            out string displayTitle,
            out FeudalTitleConfig.RankStyle rankStyle)
        {
            displayTitle = null;
            rankStyle = null;
            Clan clan = hero?.Clan;
            if (clan == null || clan.IsEliminated || hero.IsChild)
                return false;

            Kingdom kingdom = clan.Kingdom;
            FeudalTitleRecord referenceTitle = kingdom != null
                ? GetSovereignTitleForKingdom(kingdom)
                : GetHighestHeldTitle(clan);
            rankStyle = ResolveNobleRankStyle(
                referenceTitle?.TitleType ?? FeudalTitleType.Kingdom,
                clan,
                ResolveKingdom(referenceTitle?.AssociatedKingdomId),
                referenceTitle?.FallbackCultureRef);
            displayTitle = ResolveNobleTitle(rankStyle, hero.IsFemale);
            return !string.IsNullOrWhiteSpace(displayTitle);
        }

        private static bool TryGetWandererDisplayStyle(
            Hero hero,
            out string displayTitle,
            out FeudalTitleConfig.RankStyle rankStyle)
        {
            displayTitle = null;
            rankStyle = null;
            if (!IsWandererRetainer(hero) || hero?.Clan?.Kingdom == null || hero.Clan.IsEliminated)
                return false;

            FeudalTitleRecord sovereignTitle = GetSovereignTitleForKingdom(hero.Clan.Kingdom);
            if (sovereignTitle == null)
                return false;

            FeudalTitleConfig.RankStyle style = ResolveRankStyle(
                sovereignTitle.TitleType,
                hero.Clan,
                ResolveKingdom(sovereignTitle.AssociatedKingdomId),
                sovereignTitle.FallbackCultureRef);
            string configuredText = hero.IsFemale
                ? FirstNonEmpty(style?.FemaleWandererRank, style?.WandererRank)
                : FirstNonEmpty(style?.WandererRank, style?.FemaleWandererRank);

            if (string.IsNullOrWhiteSpace(configuredText))
                return false;

            displayTitle = new TextObject(configuredText).ToString();
            rankStyle = style;
            return !string.IsNullOrWhiteSpace(displayTitle);
        }

        private static bool IsWandererRetainer(Hero hero)
        {
            return hero != null && (hero.CompanionOf != null || hero.IsWanderer);
        }

        private static FeudalTitleRecord GetHighestHeldTitle(Clan clan)
        {
            FeudalTitleBehavior behavior = FeudalTitleBehavior.Instance;
            if (behavior == null || clan == null)
                return null;

            if (_highestHeldTitleCacheBehavior != behavior || _highestHeldTitleCacheRevision != behavior.DisplayRevision)
            {
                HighestHeldTitleCache.Clear();
                _highestHeldTitleCacheBehavior = behavior;
                _highestHeldTitleCacheRevision = behavior.DisplayRevision;
            }

            if (HighestHeldTitleCache.TryGetValue(clan, out FeudalTitleRecord cached))
                return cached;

            FeudalTitleRecord highest = behavior.GetTitlesHeldByClan(clan, deJure: false)
                .Where(record => record != null && record.IsActive)
                .OrderByDescending(record => record.TitleType)
                .ThenBy(record => record.Name ?? string.Empty)
                .FirstOrDefault();

            HighestHeldTitleCache[clan] = highest;
            return highest;
        }

        private static FeudalTitleRecord GetSovereignTitleForKingdom(Kingdom kingdom)
        {
            FeudalTitleBehavior behavior = FeudalTitleBehavior.Instance;
            Clan rulingClan = kingdom?.RulingClan;
            if (behavior == null || rulingClan == null)
                return null;

            return behavior.GetRealmSovereignTitle(kingdom, FeudalHierarchyMode.DeFacto);
        }

        private static bool IsEligibleStyledHero(Hero hero)
        {
            if (hero?.Clan == null || !hero.IsAlive || hero.IsDisabled)
                return false;

            bool isPlayerClan = hero.Clan == Clan.PlayerClan;
            if (!isPlayerClan && (hero.IsWanderer || hero.IsNotable))
                return false;

            return !hero.Clan.IsMinorFaction || isPlayerClan;
        }

        internal static bool IsMercenaryLeader(Hero hero)
        {
            Clan clan = hero?.Clan;
            return clan != null && clan.Leader == hero && hero.IsAlive && !hero.IsDisabled
                && !hero.IsChild && !hero.IsNotable && !clan.IsEliminated && !clan.IsBanditFaction
                && (clan.IsMinorFaction || clan.IsUnderMercenaryService);
        }

        private static bool TryGetMercenaryLeaderDisplayStyle(Hero hero, out string displayTitle,
            out FeudalTitleConfig.RankStyle rankStyle)
        {
            displayTitle = null;
            rankStyle = null;
            if (!IsMercenaryLeader(hero)) return false;

            Clan clan = hero.Clan;
            // A company's cultural style remains available between contracts, without a sovereign title.
            rankStyle = FeudalTitleConfig.Instance.Styles
                .Where(style => style != null && MatchesRef(style.CultureRef, clan.Culture?.StringId, clan.Culture?.Name?.ToString()))
                .SelectMany(style => style.Ranks)
                .FirstOrDefault(rank => rank != null && rank.Tier == FeudalTitleType.Kingdom
                    && (!string.IsNullOrWhiteSpace(rank.MercenaryLeaderRank)
                        || !string.IsNullOrWhiteSpace(rank.FemaleMercenaryLeaderRank)))
                ?? ResolveRankStyle(FeudalTitleType.Kingdom, clan);
            displayTitle = ResolveMercenaryLeaderTitle(rankStyle, hero.IsFemale);
            return !string.IsNullOrWhiteSpace(displayTitle);
        }

        private static string ResolveMercenaryLeaderTitle(FeudalTitleConfig.RankStyle style, bool isFemale)
        {
            string configuredText = isFemale
                ? FirstNonEmpty(style?.FemaleMercenaryLeaderRank, style?.MercenaryLeaderRank)
                : FirstNonEmpty(style?.MercenaryLeaderRank, style?.FemaleMercenaryLeaderRank);
            return string.IsNullOrWhiteSpace(configuredText) ? null : new TextObject(configuredText).ToString();
        }

        private static FeudalTitleConfig.RankStyle ResolveRankStyle(FeudalTitleType type, Clan styleClan, Kingdom fallbackKingdom = null, string fallbackCultureRef = "")
        {
            Kingdom styleKingdom = ResolveStyleKingdom(styleClan, fallbackKingdom);
            string holderCultureId = styleClan?.Culture?.StringId;
            string holderCultureName = styleClan?.Culture?.Name?.ToString();

            FeudalTitleConfig.RankStyle kingdomStyle = ResolveKingdomRankStyle(type, styleKingdom);
            if (kingdomStyle != null)
                return kingdomStyle;

            CultureObject rulerCulture = styleKingdom?.RulingClan?.Culture ?? styleKingdom?.Leader?.Culture;
            FeudalTitleConfig.RankStyle rulerCultureStyle = FeudalTitleConfig.Instance.Styles
                .Where(style => style != null && MatchesRef(
                    style.RulerCultureRef,
                    rulerCulture?.StringId,
                    rulerCulture?.Name?.ToString()))
                .SelectMany(style => style.Ranks)
                .FirstOrDefault(rank => rank != null && rank.Tier == type);
            if (rulerCultureStyle != null)
                return rulerCultureStyle;

            FeudalTitleConfig.RankStyle holderCultureStyle = FeudalTitleConfig.Instance.Styles
                .Where(style => style != null && MatchesRef(style.CultureRef, holderCultureId, holderCultureName))
                .SelectMany(style => style.Ranks)
                .FirstOrDefault(rank => rank != null && rank.Tier == type);
            if (holderCultureStyle != null)
                return holderCultureStyle;

            // A living holder keeps the style of their realm or culture. Falling through to the
            // title's historical realm would make kingdomless nobles borrow conquered land styles.
            if (styleClan != null)
                return null;

            FeudalTitleConfig.RankStyle fallbackCultureStyle = FeudalTitleConfig.Instance.Styles
                .Where(style => style != null && MatchesRef(style.CultureRef, fallbackCultureRef, fallbackCultureRef))
                .SelectMany(style => style.Ranks)
                .FirstOrDefault(rank => rank != null && rank.Tier == type);
            if (fallbackCultureStyle != null)
                return fallbackCultureStyle;

            FeudalTitleConfig.RankStyle fallbackKingdomStyle = ResolveKingdomRankStyle(type, fallbackKingdom);
            if (fallbackKingdomStyle != null)
                return fallbackKingdomStyle;

            return FeudalTitleConfig.Instance.Styles
                .Where(style => style != null && MatchesRef(style.CultureRef, fallbackKingdom?.Culture?.StringId, fallbackKingdom?.Culture?.Name?.ToString()))
                .SelectMany(style => style.Ranks)
                .FirstOrDefault(rank => rank != null && rank.Tier == type);
        }

        private static FeudalTitleConfig.RankStyle ResolveKingdomRankStyle(FeudalTitleType type, Kingdom kingdom)
        {
            if (kingdom == null) return null;
            FeudalTitleConfig.RankStyle Find(string id, string name) => FeudalTitleConfig.Instance.Styles
                .Where(style => style != null && MatchesRef(style.KingdomRef, id, name))
                .SelectMany(style => style.Ranks).FirstOrDefault(rank => rank != null && rank.Tier == type);
            var direct = Find(kingdom.StringId, DynamicKingdomTitleNameHelper.GetNativeName(kingdom));
            if (direct != null) return direct;
            string source = FeudalTitleBehavior.Instance?.GetRealmTitleStyleSourceId(kingdom);
            if (string.IsNullOrEmpty(source) || source == kingdom.StringId) return null;
            return Find(source, null) ?? Find(source, DynamicKingdomTitleNameHelper.GetNativeName(ResolveKingdom(source)));
        }

        private static FeudalTitleConfig.RankStyle ResolveNobleRankStyle(
            FeudalTitleType type,
            Clan styleClan,
            Kingdom fallbackKingdom = null,
            string fallbackCultureRef = "",
            bool landlessLeader = false)
        {
            string holderCultureId = styleClan?.Culture?.StringId;
            string holderCultureName = styleClan?.Culture?.Name?.ToString();
            FeudalTitleConfig.RankStyle cultureStyle = FeudalTitleConfig.Instance.Styles
                .Where(style => style != null && MatchesRef(style.CultureRef, holderCultureId, holderCultureName))
                .SelectMany(style => style.Ranks)
                .FirstOrDefault(rank => rank != null
                    && rank.Tier == type
                    && (!string.IsNullOrWhiteSpace(rank.NobleRank)
                        || !string.IsNullOrWhiteSpace(rank.FemaleNobleRank)
                        || (landlessLeader && (!string.IsNullOrWhiteSpace(rank.LandlessLeaderRank)
                            || !string.IsNullOrWhiteSpace(rank.FemaleLandlessLeaderRank)))));

            // Generic peerage belongs to the noble's own culture. Realm and ruler-culture
            // styles remain valid fallbacks for presets that do not define cultural nouns.
            return cultureStyle ?? ResolveRankStyle(type, styleClan, fallbackKingdom, fallbackCultureRef);
        }

        private static Kingdom ResolveStyleKingdom(Clan styleClan, Kingdom fallbackKingdom)
        {
            Kingdom kingdom = styleClan?.Kingdom;
            if (kingdom == null)
                return styleClan == null ? fallbackKingdom : null;

            if (!BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom))
                return kingdom;

            FactionObject civilWarFaction = Campaign.Current?
                .GetCampaignBehavior<FactionManagerBehavior>()?
                .GetFactionByRebelKingdom(kingdom);
            if (civilWarFaction?.ParentKingdom != null)
                return civilWarFaction.ParentKingdom;

            Kingdom feudParent = Campaign.Current?
                .GetCampaignBehavior<ClaimFeudWarBehavior>()?
                .GetParentKingdomForTemporaryRealm(kingdom);
            if (feudParent != null)
                return feudParent;

            return fallbackKingdom != null && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(fallbackKingdom)
                ? fallbackKingdom
                : kingdom;
        }

        public static string ResolveDisplayTitle(FeudalTitleRecord title, bool isFemale, Clan styleClan = null)
        {
            FeudalTitleType type = title?.TitleType ?? FeudalTitleType.Barony;
            FeudalTitleConfig.RankStyle style = ResolveRankStyle(type, styleClan, ResolveKingdom(title?.AssociatedKingdomId), title?.FallbackCultureRef);
            string configuredText = isFemale
                ? FirstNonEmpty(style?.FemaleRank, style?.MaleRank)
                : FirstNonEmpty(style?.MaleRank, style?.FemaleRank);

            if (!string.IsNullOrWhiteSpace(configuredText))
                return new TextObject(configuredText).ToString();

            return GetDefaultDisplayTitle(type, isFemale).ToString();
        }

        private static string ResolveSpouseTitle(FeudalTitleRecord title, bool isFemale, Clan styleClan)
        {
            FeudalTitleType type = title?.TitleType ?? FeudalTitleType.Barony;
            FeudalTitleConfig.RankStyle style = ResolveRankStyle(type, styleClan, ResolveKingdom(title?.AssociatedKingdomId), title?.FallbackCultureRef);
            string configuredText = isFemale
                ? FirstNonEmpty(style?.FemaleSpouseRank, style?.FemaleRank, style?.SpouseRank, style?.MaleRank)
                : FirstNonEmpty(style?.SpouseRank, style?.MaleRank, style?.FemaleSpouseRank, style?.FemaleRank);

            if (!string.IsNullOrWhiteSpace(configuredText))
                return new TextObject(configuredText).ToString();

            return GetDefaultDisplayTitle(type, isFemale).ToString();
        }

        private static string ResolveHeirTitle(FeudalTitleRecord title, bool isFemale, Clan styleClan)
        {
            FeudalTitleConfig.RankStyle style = ResolveRankStyle(title?.TitleType ?? FeudalTitleType.Kingdom, styleClan, ResolveKingdom(title?.AssociatedKingdomId), title?.FallbackCultureRef);
            string configuredText = isFemale
                ? FirstNonEmpty(style?.FemaleHeirRank, style?.HeirRank)
                : FirstNonEmpty(style?.HeirRank, style?.FemaleHeirRank);

            if (!string.IsNullOrWhiteSpace(configuredText))
                return new TextObject(configuredText).ToString();

            return isFemale
                ? new TextObject("{=BC_TitleDisplay_CrownPrincess}Crown Princess").ToString()
                : new TextObject("{=BC_TitleDisplay_CrownPrince}Crown Prince").ToString();
        }

        private static string ResolveChildTitle(FeudalTitleRecord title, bool isFemale, Clan styleClan)
        {
            FeudalTitleConfig.RankStyle style = ResolveRankStyle(title?.TitleType ?? FeudalTitleType.Kingdom, styleClan, ResolveKingdom(title?.AssociatedKingdomId), title?.FallbackCultureRef);
            string configuredText = isFemale
                ? FirstNonEmpty(style?.FemaleChildRank, style?.ChildRank)
                : FirstNonEmpty(style?.ChildRank, style?.FemaleChildRank);

            if (!string.IsNullOrWhiteSpace(configuredText))
                return new TextObject(configuredText).ToString();

            return isFemale
                ? new TextObject("{=BC_TitleDisplay_Princess}Princess").ToString()
                : new TextObject("{=BC_TitleDisplay_Prince}Prince").ToString();
        }

        private static string ResolveNobleTitle(FeudalTitleConfig.RankStyle style, bool isFemale)
        {
            string configuredText = isFemale
                ? FirstNonEmpty(style?.FemaleNobleRank, style?.NobleRank)
                : FirstNonEmpty(style?.NobleRank, style?.FemaleNobleRank);

            if (!string.IsNullOrWhiteSpace(configuredText))
                return new TextObject(configuredText).ToString();

            return isFemale
                ? new TextObject("{=BC_TitleDisplay_Lady}Lady").ToString()
                : new TextObject("{=BC_TitleDisplay_Lord}Lord").ToString();
        }

        private static string ResolveLandlessLeaderTitle(FeudalTitleConfig.RankStyle style, bool isFemale)
        {
            string configuredText = isFemale
                ? FirstNonEmpty(style?.FemaleLandlessLeaderRank, style?.LandlessLeaderRank)
                : FirstNonEmpty(style?.LandlessLeaderRank, style?.FemaleLandlessLeaderRank);
            return string.IsNullOrWhiteSpace(configuredText)
                ? ResolveNobleTitle(style, isFemale)
                : new TextObject(configuredText).ToString();
        }

        private static TextObject GetDefaultDisplayTitle(FeudalTitleType type, bool isFemale)
        {
            switch (type)
            {
                case FeudalTitleType.Barony:
                    return isFemale ? new TextObject("{=BC_TitleDisplay_Baroness}Baroness") : new TextObject("{=BC_TitleDisplay_Baron}Baron");
                case FeudalTitleType.County:
                    return isFemale ? new TextObject("{=BC_TitleDisplay_Countess}Countess") : new TextObject("{=BC_TitleDisplay_Count}Count");
                case FeudalTitleType.Duchy:
                    return isFemale ? new TextObject("{=BC_TitleDisplay_Duchess}Duchess") : new TextObject("{=BC_TitleDisplay_Duke}Duke");
                case FeudalTitleType.Kingdom:
                    return isFemale ? new TextObject("{=BC_TitleDisplay_Queen}Queen") : new TextObject("{=BC_TitleDisplay_King}King");
                case FeudalTitleType.Empire:
                    return isFemale ? new TextObject("{=BC_TitleDisplay_Empress}Empress") : new TextObject("{=BC_TitleDisplay_Emperor}Emperor");
                default:
                    return new TextObject(string.Empty);
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private static string LocalizeConfiguredText(string configuredText)
        {
            return string.IsNullOrWhiteSpace(configuredText)
                ? string.Empty
                : new TextObject(configuredText).ToString();
        }

        private static string ResolveLandedTitleNoun(FeudalTitleType type, FeudalTitleConfig.RankStyle style)
        {
            if (!string.IsNullOrWhiteSpace(style?.TitleName))
                return new TextObject(style.TitleName).ToString();

            return GetDefaultLandedTitleNoun(type).ToString();
        }

        private static string FormatLandedTitle(string configuredFormat, string titleNoun, string titleName)
        {
            TextObject text = !string.IsNullOrWhiteSpace(configuredFormat)
                ? new TextObject(configuredFormat)
                : new TextObject("{=BC_TitleDisplay_LandedTitle}{TITLE_NOUN} of {TITLE_NAME}");
            text.SetTextVariable("TITLE_NOUN", titleNoun);
            text.SetTextVariable("TITLE_NAME", titleName);
            return text.ToString();
        }

        private static string FormatHeldTitle(string configuredFormat, string displayTitle, string titleName)
        {
            TextObject text = !string.IsNullOrWhiteSpace(configuredFormat)
                ? new TextObject(configuredFormat)
                : new TextObject("{=BC_TitleDisplay_HeldTitle}{DISPLAY_TITLE} of {TITLE_NAME}");
            text.SetTextVariable("DISPLAY_TITLE", displayTitle);
            text.SetTextVariable("TITLE_RANK", displayTitle);
            text.SetTextVariable("TITLE_NAME", titleName);
            return text.ToString();
        }

        private static TextObject GetDefaultLandedTitleNoun(FeudalTitleType type)
        {
            switch (type)
            {
                case FeudalTitleType.Barony:
                    return new TextObject("{=BC_TitleLanded_Barony}Barony");
                case FeudalTitleType.County:
                    return new TextObject("{=BC_TitleLanded_County}County");
                case FeudalTitleType.Duchy:
                    return new TextObject("{=BC_TitleLanded_Duchy}Duchy");
                case FeudalTitleType.Kingdom:
                    return new TextObject("{=BC_TitleLanded_Kingdom}Kingdom");
                case FeudalTitleType.Empire:
                    return new TextObject("{=BC_TitleLanded_Empire}Empire");
                default:
                    return new TextObject(string.Empty);
            }
        }

        private static bool MatchesRef(string configuredRef, string id, string name)
        {
            if (string.IsNullOrWhiteSpace(configuredRef))
                return false;

            return string.Equals(configuredRef, id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(configuredRef, name, StringComparison.OrdinalIgnoreCase);
        }

        public static string CleanTerritorialRoot(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            string cleaned = name.Trim();
            foreach (string prefix in new[] { "Barony of ", "County of ", "Duchy of ", "Kingdom of ", "Empire of " })
            {
                if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return cleaned.Substring(prefix.Length).Trim();
            }

            return cleaned;
        }

        private static Clan ResolveClan(string clanId)
        {
            return string.IsNullOrWhiteSpace(clanId)
                ? null
                : Clan.All.FirstOrDefault(clan => clan != null && string.Equals(clan.StringId, clanId, StringComparison.Ordinal));
        }

        private static Kingdom ResolveKingdom(string kingdomId)
        {
            return string.IsNullOrWhiteSpace(kingdomId)
                ? null
                : Kingdom.All.FirstOrDefault(kingdom => kingdom != null && string.Equals(kingdom.StringId, kingdomId, StringComparison.Ordinal));
        }
    }
}
