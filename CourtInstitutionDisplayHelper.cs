using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class CourtInstitutionDisplayHelper
    {
        public static TextObject GetCourtFactionName(FactionType type, Kingdom kingdom)
        {
            string configured = ResolveConfiguredName(
                kingdom,
                style => GetValue(style.CourtFactionNames, type));
            return !string.IsNullOrWhiteSpace(configured)
                ? new TextObject(configured)
                : GetDefaultCourtFactionName(type);
        }

        public static TextObject GetCouncilOfficeName(PrivyCouncilOffice office, Kingdom kingdom)
        {
            string configured = ResolveConfiguredName(
                kingdom,
                style => GetValue(style.CouncilOfficeNames, office));
            return !string.IsNullOrWhiteSpace(configured)
                ? new TextObject(configured)
                : GetDefaultCouncilOfficeName(office);
        }

        public static TextObject GetPrivyCouncilName(Kingdom kingdom)
        {
            string configured = ResolveConfiguredName(
                kingdom,
                style => style.PrivyCouncilName);
            return !string.IsNullOrWhiteSpace(configured)
                ? new TextObject(configured)
                : GetDefaultPrivyCouncilName();
        }

        public static TextObject ApplyCourtFactionName(
            TextObject text,
            FactionType type,
            Kingdom kingdom)
        {
            if (text == null)
                return TextObject.GetEmpty();

            TextObject resolvedName = GetCourtFactionName(type, kingdom);
            text.SetTextVariable("FACTION_NAME", resolvedName);

            string defaultName = GetDefaultCourtFactionName(type).ToString();
            string customName = resolvedName.ToString();
            if (string.Equals(defaultName, customName, StringComparison.Ordinal))
                return text;

            string rendered = text.ToString();
            rendered = rendered.Replace(defaultName, customName);
            rendered = rendered.Replace(defaultName.ToLowerInvariant(), customName);
            rendered = rendered.Replace(defaultName.ToUpperInvariant(), customName);
            return new TextObject("{=!}" + rendered);
        }

        public static TextObject ApplyOfficeName(
            TextObject text,
            PrivyCouncilOffice office,
            Kingdom kingdom)
        {
            if (text == null)
                return TextObject.GetEmpty();

            TextObject resolvedOffice = GetCouncilOfficeName(office, kingdom);
            text.SetTextVariable("OFFICE", resolvedOffice);

            // Older localized incident strings name the office directly instead of using
            // {OFFICE}. Keep those translations compatible with custom court terminology.
            string defaultOffice = GetDefaultCouncilOfficeName(office).ToString();
            string customOffice = resolvedOffice.ToString();
            if (string.Equals(defaultOffice, customOffice, StringComparison.Ordinal))
                return text;

            string rendered = text.ToString();
            rendered = rendered.Replace(defaultOffice, customOffice);
            rendered = rendered.Replace(defaultOffice.ToLowerInvariant(), customOffice);
            rendered = rendered.Replace(defaultOffice.ToUpperInvariant(), customOffice);

            string genericAdvisor = new TextObject("{=BC_Council_GenericAdvisor}Advisor").ToString();
            if (office == PrivyCouncilOffice.FirstAdvisor || office == PrivyCouncilOffice.SecondAdvisor)
            {
                rendered = rendered.Replace(genericAdvisor, customOffice);
                rendered = rendered.Replace(genericAdvisor.ToLowerInvariant(), customOffice);
                rendered = rendered.Replace(genericAdvisor.ToUpperInvariant(), customOffice);
            }

            return new TextObject("{=!}" + rendered);
        }

        public static TextObject ApplyPrivyCouncilName(TextObject text, Kingdom kingdom)
        {
            if (text == null)
                return TextObject.GetEmpty();

            TextObject resolvedName = GetPrivyCouncilName(kingdom);
            text.SetTextVariable("COUNCIL_NAME", resolvedName);

            // Preserve older translations that wrote the institution name directly
            // before the configurable {COUNCIL_NAME} token was introduced.
            string defaultName = GetDefaultPrivyCouncilName().ToString();
            string customName = resolvedName.ToString();
            if (string.Equals(defaultName, customName, StringComparison.Ordinal))
                return text;

            string rendered = text.ToString();
            rendered = rendered.Replace(defaultName, customName);
            rendered = rendered.Replace(defaultName.ToLowerInvariant(), customName);
            rendered = rendered.Replace(defaultName.ToUpperInvariant(), customName);
            return new TextObject("{=!}" + rendered);
        }

        private static string ResolveConfiguredName(
            Kingdom kingdom,
            Func<FeudalTitleConfig.TitleStyle, string> selector)
        {
            if (selector == null)
                return string.Empty;

            Kingdom styleKingdom = ResolveStyleKingdom(kingdom);
            IReadOnlyList<FeudalTitleConfig.TitleStyle> styles = FeudalTitleConfig.Instance.Styles;

            string configured = styles
                .Where(style => style != null && MatchesRef(
                    style.KingdomRef,
                    styleKingdom?.StringId,
                    styleKingdom?.Name?.ToString()))
                .Select(selector)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            var rulerCulture = styleKingdom?.RulingClan?.Culture ?? styleKingdom?.Leader?.Culture;
            configured = styles
                .Where(style => style != null && MatchesRef(
                    style.RulerCultureRef,
                    rulerCulture?.StringId,
                    rulerCulture?.Name?.ToString()))
                .Select(selector)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            var realmCulture = styleKingdom?.Culture;
            return styles
                .Where(style => style != null && MatchesRef(
                    style.CultureRef,
                    realmCulture?.StringId,
                    realmCulture?.Name?.ToString()))
                .Select(selector)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? string.Empty;
        }

        private static Kingdom ResolveStyleKingdom(Kingdom kingdom)
        {
            if (kingdom == null || !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom))
                return kingdom;

            FactionObject civilWarFaction = Campaign.Current?
                .GetCampaignBehavior<FactionManagerBehavior>()?
                .GetFactionByRebelKingdom(kingdom);
            if (civilWarFaction?.ParentKingdom != null)
                return civilWarFaction.ParentKingdom;

            Kingdom feudParent = Campaign.Current?
                .GetCampaignBehavior<ClaimFeudWarBehavior>()?
                .GetParentKingdomForTemporaryRealm(kingdom);
            return feudParent ?? kingdom;
        }

        private static string GetValue<TKey>(IDictionary<TKey, string> values, TKey key)
        {
            return values != null && values.TryGetValue(key, out string value)
                ? value
                : string.Empty;
        }

        private static bool MatchesRef(string configuredRef, string id, string name)
        {
            return !string.IsNullOrWhiteSpace(configuredRef)
                && (string.Equals(configuredRef, id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(configuredRef, name, StringComparison.OrdinalIgnoreCase));
        }

        private static TextObject GetDefaultCourtFactionName(FactionType type)
        {
            switch (type)
            {
                case FactionType.Royalists: return new TextObject("{=BC_FacName_Royalists}Traditionalists");
                case FactionType.Glory: return new TextObject("{=BC_FacName_Glory}Glory");
                case FactionType.Nobility: return new TextObject("{=BC_FacName_Nobility}Nobility");
                case FactionType.Liberty: return new TextObject("{=BC_FacName_Liberty}Liberty");
                default: return new TextObject("{=BC_Ideology_CourtFaction}Courtly Faction");
            }
        }

        private static TextObject GetDefaultCouncilOfficeName(PrivyCouncilOffice office)
        {
            switch (office)
            {
                case PrivyCouncilOffice.Marshal: return new TextObject("{=BC_Council_Marshal}Marshal");
                case PrivyCouncilOffice.Chancellor: return new TextObject("{=BC_Council_Chancellor}Chancellor");
                case PrivyCouncilOffice.Seneschal: return new TextObject("{=BC_Council_Seneschal}Seneschal");
                case PrivyCouncilOffice.Spymaster: return new TextObject("{=BC_Council_Spymaster}Spymaster");
                case PrivyCouncilOffice.FirstAdvisor: return new TextObject("{=BC_Council_FirstAdvisor}First Advisor");
                default: return new TextObject("{=BC_Council_SecondAdvisor}Second Advisor");
            }
        }

        private static TextObject GetDefaultPrivyCouncilName()
        {
            return new TextObject("{=BC_Council_PrivyCouncil}Privy Council");
        }
    }
}
