using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.Core.ViewModelCollection.Generic;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.ViewModelMixin
{
    [ViewModelMixin("RefreshValues", true)]
    internal sealed class EncyclopediaHeroFeudalTitlesMixin : BaseViewModelMixin<EncyclopediaHeroPageVM>
    {
        private readonly Hero _hero;
        private string _bellumTitlesText;
        private string _bellumClaimsText;
        private string _bellumInheritanceText;
        private bool _hasBellumTitles;
        private bool _hasBellumClaims;
        private bool _hasBellumInheritance;
        private bool _hasBellumTitleOrClaimSection;

        public EncyclopediaHeroFeudalTitlesMixin(EncyclopediaHeroPageVM vm) : base(vm)
        {
            _hero = vm?.Obj as Hero;
            BellumTitles = new MBBindingList<StringPairItemVM>();
            BellumClaims = new MBBindingList<StringPairItemVM>();
            BellumInheritance = new MBBindingList<StringPairItemVM>();
            _bellumTitlesText = new TextObject("{=BC_HeroTitles_Header}Titles").ToString();
            _bellumClaimsText = new TextObject("{=BC_HeroClaims_Header}Claims").ToString();
            _bellumInheritanceText = new TextObject("{=BC_EstatePreview_Header}Inheritance").ToString();
        }

        [DataSourceProperty]
        public string BellumTitlesText
        {
            get => _bellumTitlesText;
            set
            {
                if (value != _bellumTitlesText)
                {
                    _bellumTitlesText = value;
                    ViewModel.OnPropertyChangedWithValue(value, "BellumTitlesText");
                }
            }
        }

        [DataSourceProperty]
        public string BellumClaimsText
        {
            get => _bellumClaimsText;
            set
            {
                if (value != _bellumClaimsText)
                {
                    _bellumClaimsText = value;
                    ViewModel.OnPropertyChangedWithValue(value, "BellumClaimsText");
                }
            }
        }

        [DataSourceProperty] public MBBindingList<StringPairItemVM> BellumTitles { get; set; }
        [DataSourceProperty] public MBBindingList<StringPairItemVM> BellumClaims { get; set; }
        [DataSourceProperty] public MBBindingList<StringPairItemVM> BellumInheritance { get; set; }

        [DataSourceProperty]
        public string BellumInheritanceText
        {
            get => _bellumInheritanceText;
            set
            {
                if (value != _bellumInheritanceText)
                {
                    _bellumInheritanceText = value;
                    ViewModel.OnPropertyChangedWithValue(value, "BellumInheritanceText");
                }
            }
        }

        [DataSourceProperty]
        public bool HasBellumInheritance
        {
            get => _hasBellumInheritance;
            set
            {
                if (value != _hasBellumInheritance)
                {
                    _hasBellumInheritance = value;
                    ViewModel.OnPropertyChangedWithValue(value, "HasBellumInheritance");
                }
            }
        }

        [DataSourceProperty]
        public bool HasBellumTitles
        {
            get => _hasBellumTitles;
            set
            {
                if (value != _hasBellumTitles)
                {
                    _hasBellumTitles = value;
                    ViewModel.OnPropertyChangedWithValue(value, "HasBellumTitles");
                }
            }
        }

        [DataSourceProperty]
        public bool HasBellumClaims
        {
            get => _hasBellumClaims;
            set
            {
                if (value != _hasBellumClaims)
                {
                    _hasBellumClaims = value;
                    ViewModel.OnPropertyChangedWithValue(value, "HasBellumClaims");
                }
            }
        }

        [DataSourceProperty]
        public bool HasBellumTitleOrClaimSection
        {
            get => _hasBellumTitleOrClaimSection;
            set
            {
                if (value != _hasBellumTitleOrClaimSection)
                {
                    _hasBellumTitleOrClaimSection = value;
                    ViewModel.OnPropertyChangedWithValue(value, "HasBellumTitleOrClaimSection");
                }
            }
        }

        public override void OnRefresh()
        {
            BellumTitlesText = new TextObject("{=BC_HeroTitles_Header}Titles").ToString();
            BellumClaimsText = new TextObject("{=BC_HeroClaims_Header}Claims").ToString();
            BellumInheritanceText = new TextObject("{=BC_EstatePreview_Header}Inheritance").ToString();
            BellumTitles.Clear();
            BellumClaims.Clear();
            BellumInheritance.Clear();
            HasBellumTitles = false;
            HasBellumClaims = false;
            HasBellumTitleOrClaimSection = false;

            foreach (StringPairItemVM entry in EstateInheritanceDisplay.BuildHeroEntries(_hero))
                BellumInheritance.Add(entry);
            HasBellumInheritance = BellumInheritance.Count > 0;
            HasBellumTitleOrClaimSection = HasBellumInheritance;

            var lordship = LordshipBehavior.Instance?.GetGrant(_hero);
            if (lordship != null)
            {
                BellumTitles.Add(new StringPairItemVM(LordshipBehavior.Name(lordship.Village).ToString(), string.Empty, null));
                HasBellumTitles = true;
                HasBellumTitleOrClaimSection = true;
            }

            if (_hero?.Clan == null || _hero.Clan.Leader != _hero)
                return;

            foreach (FeudalTitleDisplayHelper.HeldTitleDisplayEntry entry in FeudalTitleDisplayHelper.GetHeldTitleEntries(_hero.Clan))
            {
                string value = string.IsNullOrWhiteSpace(entry.Status) ? string.Empty : $"({entry.Status})";
                BellumTitles.Add(new StringPairItemVM(entry.Name, value, null));
            }

            AddCouncilOffices();

            foreach (FeudalTitleDisplayHelper.ClaimDisplayEntry entry in FeudalTitleDisplayHelper.GetClaimEntries(_hero.Clan))
                BellumClaims.Add(new StringPairItemVM(entry.Strength, entry.TitleName, null));

            HasBellumTitles = BellumTitles.Count > 0;
            HasBellumClaims = BellumClaims.Count > 0;
            HasBellumTitleOrClaimSection = HasBellumTitles || HasBellumClaims || HasBellumInheritance;
        }

        private void AddCouncilOffices()
        {
            Clan clan = _hero?.Clan;
            Kingdom kingdom = clan?.Kingdom;
            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            if (kingdom == null || council == null)
                return;

            foreach (PrivyCouncilOfficeRecord record in council.GetOfficeRecords(kingdom))
            {
                if (record == null
                    || !council.IsOfficeUnlocked(kingdom, record.Office)
                    || council.GetOfficeHolder(kingdom, record.Office) != clan)
                {
                    continue;
                }

                TextObject officeTitle = new TextObject("{=BC_HeroTitles_CouncilOffice}{OFFICE} of {KINGDOM_NAME}");
                officeTitle.SetTextVariable("OFFICE", PrivyCouncilBehavior.GetLocalizedOfficeName(record.Office, kingdom));
                FeudalTitleRecord sovereignTitle = FeudalTitleBehavior.Instance?
                    .GetRealmSovereignTitle(kingdom, FeudalHierarchyMode.DeFacto);
                string conciseKingdomName = sovereignTitle != null && sovereignTitle.IsActive
                    ? FeudalTitleDisplayHelper.CleanTerritorialRoot(new TextObject(sovereignTitle.Name).ToString())
                    : null;
                if (string.IsNullOrWhiteSpace(conciseKingdomName))
                    conciseKingdomName = DynamicKingdomTitleNameHelper.GetNativeName(kingdom);

                officeTitle.SetTextVariable(
                    "KINGDOM_NAME",
                    new TextObject("{=!}" + (string.IsNullOrWhiteSpace(conciseKingdomName)
                        ? kingdom.StringId ?? string.Empty
                        : conciseKingdomName)));
                BellumTitles.Add(new StringPairItemVM(officeTitle.ToString(), string.Empty, null));
            }
        }
    }
}
