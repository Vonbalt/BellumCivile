using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal readonly struct IndependentKingdomProfile
    {
        public IndependentKingdomProfile(
            TextObject name,
            TextObject encyclopediaTitle,
            TextObject rulerTitle,
            TextObject encyclopediaText,
            CultureObject culture,
            string sourceTitleId = "",
            FeudalTitleType? sourceTitleType = null)
        {
            Name = name;
            EncyclopediaTitle = encyclopediaTitle;
            RulerTitle = rulerTitle;
            EncyclopediaText = encyclopediaText;
            Culture = culture;
            SourceTitleId = sourceTitleId ?? string.Empty;
            SourceTitleType = sourceTitleType;
        }

        public TextObject Name { get; }
        public TextObject EncyclopediaTitle { get; }
        public TextObject RulerTitle { get; }
        public TextObject EncyclopediaText { get; }
        public CultureObject Culture { get; }
        public string SourceTitleId { get; }
        public FeudalTitleType? SourceTitleType { get; }
        public bool UsesExistingSourceTitle => !string.IsNullOrWhiteSpace(SourceTitleId);
    }

    internal static class IndependentKingdomProfileHelper
    {
        public static IndependentKingdomProfile Create(Clan leaderClan, Kingdom parentKingdom)
        {
            CultureObject culture = leaderClan?.Culture ?? parentKingdom?.Culture;
            FeudalTitleRecord sourceTitle = ResolveSourceTitle(leaderClan, parentKingdom);
            if (sourceTitle != null)
                return CreateTitleBasedProfile(leaderClan, parentKingdom, culture, sourceTitle);

            string cultureId = culture?.StringId?.ToLower() ?? string.Empty;
            TextObject name;
            TextObject rulerTitle;

            switch (cultureId)
            {
                case "vlandia":
                    name = new TextObject("{=BC_Resolution_VlandiaIndep}Duchy of {CLAN_NAME}");
                    rulerTitle = new TextObject("{=BC_Resolution_Title_Duke}Duke");
                    break;
                case "battania":
                    name = new TextObject("{=BC_Resolution_BattaniaIndep}Chiefdom of {CLAN_NAME}");
                    rulerTitle = new TextObject("{=BC_Resolution_Title_Chief}Chief");
                    break;
                case "sturgia":
                    name = new TextObject("{=BC_Resolution_SturgiaIndep}Principality of {CLAN_NAME}");
                    rulerTitle = new TextObject("{=BC_Resolution_Title_Prince}Prince");
                    break;
                case "nord":
                    name = new TextObject("{=BC_Resolution_NordIndep}Jarldom of {CLAN_NAME}");
                    rulerTitle = new TextObject("{=BC_Resolution_Title_Jarl}Jarl");
                    break;
                case "empire":
                    name = new TextObject("{=BC_Resolution_EmpireIndep}Despotate of {CLAN_NAME}");
                    rulerTitle = new TextObject("{=BC_Resolution_Title_Despot}Despot");
                    break;
                case "aserai":
                    name = new TextObject("{=BC_Resolution_AseraiIndep}Emirate of {CLAN_NAME}");
                    rulerTitle = new TextObject("{=BC_Resolution_Title_Emir}Emir");
                    break;
                case "khuzait":
                    name = new TextObject("{=BC_Resolution_KhuzaitIndep}{CLAN_NAME} Horde");
                    rulerTitle = new TextObject("{=BC_Resolution_Title_Khan}Khan");
                    break;
                default:
                    name = new TextObject("{=BC_Resolution_DefaultIndep}{CLAN_NAME} Confederacy");
                    rulerTitle = new TextObject("{=BC_Resolution_Title_LordProtector}Lord Protector");
                    break;
            }

            name.SetTextVariable("CLAN_NAME", leaderClan?.Name ?? new TextObject("{=BC_Resolution_UnknownClan}Unknown Clan"));

            TextObject encyclopediaText = new TextObject("{=BC_Resolution_IndepRealmText}Once a part of {PARENT_KINGDOM}, the {NEW_KINGDOM} successfully fought a war for independence.");
            encyclopediaText.SetTextVariable("PARENT_KINGDOM", parentKingdom?.Name ?? new TextObject("{=BC_Resolution_FormerRealm}a former realm"));
            encyclopediaText.SetTextVariable("NEW_KINGDOM", name);

            return new IndependentKingdomProfile(name, name, rulerTitle, encyclopediaText, culture);
        }

        private static IndependentKingdomProfile CreateTitleBasedProfile(Clan leaderClan, Kingdom parentKingdom, CultureObject culture, FeudalTitleRecord sourceTitle)
        {
            TextObject name = BuildTitleBasedRealmName(leaderClan, sourceTitle);
            TextObject rulerTitle = new TextObject("{=!}" + FeudalTitleDisplayHelper.ResolveDisplayTitle(sourceTitle, leaderClan?.Leader?.IsFemale == true, leaderClan));

            TextObject encyclopediaText = new TextObject("{=BC_Resolution_IndepRealmText}Once a part of {PARENT_KINGDOM}, the {NEW_KINGDOM} successfully fought a war for independence.");
            encyclopediaText.SetTextVariable("PARENT_KINGDOM", parentKingdom?.Name ?? new TextObject("{=BC_Resolution_FormerRealm}a former realm"));
            encyclopediaText.SetTextVariable("NEW_KINGDOM", name);

            return new IndependentKingdomProfile(name, name, rulerTitle, encyclopediaText, culture, sourceTitle.TitleId, sourceTitle.TitleType);
        }

        private static TextObject BuildTitleBasedRealmName(Clan leaderClan, FeudalTitleRecord sourceTitle)
        {
            string name = FeudalTitleDisplayHelper.FormatTitleName(sourceTitle, leaderClan);
            if (!string.IsNullOrWhiteSpace(name))
                return new TextObject("{=!}" + name);

            TextObject fallback = new TextObject("{=BC_Resolution_DefaultIndep}{CLAN_NAME} Confederacy");
            fallback.SetTextVariable("CLAN_NAME", leaderClan?.Name ?? new TextObject("{=BC_Resolution_UnknownClan}Unknown Clan"));
            return fallback;
        }

        private static FeudalTitleRecord ResolveSourceTitle(Clan leaderClan, Kingdom parentKingdom)
        {
            FeudalTitleBehavior titleBehavior = FeudalTitleBehavior.Instance;
            if (titleBehavior == null || leaderClan == null)
                return null;

            string parentKingdomTitleId = FeudalTitleBehavior.BuildKingdomTitleId(parentKingdom);
            return titleBehavior.GetTitlesHeldByClan(leaderClan, deJure: true)
                .Concat(titleBehavior.GetTitlesHeldByClan(leaderClan, deJure: false))
                .Where(title => title != null
                    && title.IsActive
                    && !string.Equals(title.TitleId, parentKingdomTitleId, System.StringComparison.OrdinalIgnoreCase))
                .GroupBy(title => title.TitleId)
                .Select(group => group.First())
                .OrderByDescending(title => title.TitleType)
                .ThenByDescending(title => string.Equals(title.DeJureHolderClanId, leaderClan.StringId, System.StringComparison.Ordinal))
                .ThenBy(title => title.Name ?? string.Empty)
                .FirstOrDefault();
        }
    }
}
