using System.Collections.Generic;
using Bannerlord.UIExtenderEx.Attributes;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Items;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI
{
    internal sealed class EncyclopediaClanSupportItemVM : ViewModel
    {
        private readonly EncyclopediaFactionVM _native;
        private readonly BasicTooltipViewModel _hint;

        internal EncyclopediaClanSupportItemVM(ClanSupportConnection tie, bool hasEstimate)
        {
            _native = new EncyclopediaFactionVM(tie.Clan);
            var rows = BuildTooltip(tie, hasEstimate);
            _hint = new BasicTooltipViewModel(() => rows);
        }

        [DataSourceProperty] public BannerImageIdentifierVM ImageIdentifier => _native.ImageIdentifier;
        [DataSourceProperty] public string NameText => _native.NameText;
        [DataSourceProperty] public bool IsDestroyed => _native.IsDestroyed;
        [DataSourceMethod] public void ExecuteLink() => _native.ExecuteLink();
        [DataSourceMethod] public void ExecuteBeginHint() => _hint.ExecuteBeginHint();
        [DataSourceMethod] public void ExecuteEndHint() => _hint.ExecuteEndHint();

        internal static List<TooltipProperty> BuildTooltip(ClanSupportConnection tie, bool hasEstimate)
        {
            var clan = tie.Clan;
            var rows = new List<TooltipProperty>
            {
                new TooltipProperty(clan.Name.ToString(), string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.Title),
                new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator)
            };
            void Add(TextObject label, string value) => rows.Add(new TooltipProperty(label.ToString(), value, 0));
            Add(new TextObject("{=SrfYbg3x}Leader"), clan.Leader?.Name?.ToString() ?? "-");
            Add(new TextObject("{=tTLvo8sM}Clan Tier"), clan.Tier.ToString());
            Add(new TextObject("{=ODEnkg0o}Clan Strength"), RebellionPowerHelper.GetClanMilitaryStrength(clan).ToString("N0"));
            Add(new TextObject("{=BC_ClanSupport_Influence}Influence"), clan.Influence.ToString("N0"));
            Add(GameTexts.FindText("str_wealth"), CampaignUIHelper.GetClanWealthStatusText(clan));

            rows.Add(new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator));
            rows.Add(new TooltipProperty(string.Empty, new TextObject("{=BC_ClanSupport_Ties}Support ties").ToString(), 0));
            if (tie.ServiceShare.HasValue)
                Add(new TextObject("{=BC_ClanSupport_Vassal}Direct vassal"), new TextObject("{=BC_ClanSupport_Service}{SHARE}% service")
                    .SetTextVariable("SHARE", (tie.ServiceShare.Value * 100f).ToString("0.#")).ToString());
            if (tie.Friendship.HasValue)
                Add(new TextObject("{=BC_ClanSupport_Friendship}Close friends"), tie.Friendship.Value.ToString("+0"));
            if (tie.Spouses.First != null && tie.Spouses.Second != null)
                Add(new TextObject("{=BC_ClanSupport_Marriage}Marriage alliance"), new TextObject("{=BC_ClanSupport_Spouses}{FIRST} and {SECOND}")
                    .SetTextVariable("FIRST", tie.Spouses.First.Name).SetTextVariable("SECOND", tie.Spouses.Second.Name).ToString());
            if (tie.MarriageTruce.IsActive)
                Add(new TextObject("{=BC_ClanSupport_MarriageTruce}Marriage truce"), tie.MarriageTruce.IsLifetime
                    ? new TextObject("{=BC_ClanSupport_MarriageTruceLifetime}While the alliance lasts").ToString()
                    : new TextObject("{=BC_ClanSupport_MarriageTruceDays}{DAYS} days remaining")
                        .SetTextVariable("DAYS", tie.MarriageTruce.RemainingDays).ToString());
            if (tie.CloseKin)
                rows.Add(new TooltipProperty(string.Empty, new TextObject("{=BC_ClanSupport_CloseKin}Close kin").ToString(), 0));

            if (hasEstimate)
            {
                var support = tie.ExpectedSupport;
                Add(new TextObject("{=BC_ClanSupport_Expected}Expected support"), support == null ? "0"
                    : new TextObject("{=BC_UI_ExpectedSupportValue}+{POWER} / {TOTAL} ({CAP}% cap, {CHANCE}% chance)")
                        .SetTextVariable("POWER", support.CreditedPower.ToString("N0"))
                        .SetTextVariable("TOTAL", support.FullPower.ToString("N0"))
                        .SetTextVariable("CAP", (support.ContributionCap * 100f).ToString("0.#"))
                        .SetTextVariable("CHANCE", (support.JoinChance * 100f).ToString("0.#")).ToString());
                rows.Add(new TooltipProperty(string.Empty, new TextObject("{=BC_ClanSupport_EstimateHint}An estimate of support in a rebellion, not a pledge.").ToString(), 0));
            }
            return rows;
        }

        public override void OnFinalize()
        {
            _hint.OnFinalize();
            _native.OnFinalize();
            base.OnFinalize();
        }
    }
}
