using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI
{
    internal sealed class EncyclopediaHeroFamilyVM : ViewModel
    {
        private const int GroupGap = 16;

        [DataSourceProperty] public MBBindingList<EncyclopediaFamilyPortraitVM> Parents { get; } = new MBBindingList<EncyclopediaFamilyPortraitVM>();
        [DataSourceProperty] public MBBindingList<EncyclopediaFamilyPortraitVM> Grandparents { get; } = new MBBindingList<EncyclopediaFamilyPortraitVM>();
        [DataSourceProperty] public MBBindingList<EncyclopediaFamilyPortraitVM> Spouses { get; } = new MBBindingList<EncyclopediaFamilyPortraitVM>();
        [DataSourceProperty] public MBBindingList<EncyclopediaFamilyPortraitVM> Children { get; } = new MBBindingList<EncyclopediaFamilyPortraitVM>();
        [DataSourceProperty] public MBBindingList<EncyclopediaFamilyPortraitVM> Siblings { get; } = new MBBindingList<EncyclopediaFamilyPortraitVM>();
        [DataSourceProperty] public MBBindingList<EncyclopediaFamilyPortraitVM> Grandchildren { get; } = new MBBindingList<EncyclopediaFamilyPortraitVM>();

        [DataSourceProperty] public bool IsVisible { get; private set; }
        [DataSourceProperty] public bool HasParents => Parents.Any(p => p.IsKnown);
        [DataSourceProperty] public bool HasGrandparents => Grandparents.Any(p => p.IsKnown);
        [DataSourceProperty] public bool HasAncestors => HasParents || HasGrandparents;
        [DataSourceProperty] public bool HasSpouses => Spouses.Count > 0;
        [DataSourceProperty] public bool HasChildren => Children.Count > 0;
        [DataSourceProperty] public bool HasSiblings => Siblings.Count > 0;
        [DataSourceProperty] public bool HasGrandchildren => Grandchildren.Count > 0;
        [DataSourceProperty] public bool HasHousehold => HasSpouses || HasChildren;
        [DataSourceProperty] public bool HasDescendantsOrSiblings => HasSiblings || HasGrandchildren;
        [DataSourceProperty] public int ParentsGap => HasParents && HasGrandparents ? GroupGap : 0;
        [DataSourceProperty] public int SpousesGap => HasSpouses && HasChildren ? GroupGap : 0;
        [DataSourceProperty] public int SiblingsGap => HasSiblings && HasGrandchildren ? GroupGap : 0;
        [DataSourceProperty] public int ChildrenColumns => HasSpouses ? 6 : 7;
        [DataSourceProperty] public int SiblingsColumns => HasGrandchildren ? 3 : 7;
        [DataSourceProperty] public int GrandchildrenColumns => HasSiblings ? 4 : 7;
        [DataSourceProperty] public int ChildrenWidth => ChildrenColumns * 100;
        [DataSourceProperty] public int SiblingsWidth => SiblingsColumns * 100;
        [DataSourceProperty] public int GrandchildrenWidth => GrandchildrenColumns * 100;
        [DataSourceProperty] public string Header => new TextObject("{=BC_HeroFamily_Header}Family").ToString();
        [DataSourceProperty] public string ParentsText => new TextObject("{=BC_HeroFamily_Parents}Parents").ToString();
        [DataSourceProperty] public string GrandparentsText => new TextObject("{=BC_HeroFamily_Grandparents}Grandparents").ToString();
        [DataSourceProperty] public string SpousesText => new TextObject("{=BC_HeroFamily_Spouse}Spouse").ToString();
        [DataSourceProperty] public string ChildrenText => CountText("{=BC_HeroFamily_Children}Children ({COUNT})", Children.Count);
        [DataSourceProperty] public string SiblingsText => CountText("{=BC_HeroFamily_Siblings}Siblings ({COUNT})", Siblings.Count);
        [DataSourceProperty] public string GrandchildrenText => CountText("{=BC_HeroFamily_Grandchildren}Grandchildren ({COUNT})", Grandchildren.Count);

        internal void Refresh(Hero hero, bool informationHidden, Func<Hero, bool> canDisplay)
        {
            Clear();
            IsVisible = hero != null && !informationHidden && canDisplay != null;
            if (IsVisible)
            {
                bool Known(Hero relative) => relative != null && relative != hero && canDisplay(relative);
                Hero father = Known(hero.Father) ? hero.Father : null;
                Hero mother = Known(hero.Mother) ? hero.Mother : null;
                Add(Parents, father, "{=BC_HeroFamily_Father}Father");
                Add(Parents, mother, "{=BC_HeroFamily_Mother}Mother");
                Add(Grandparents, Known(father?.Father) ? father.Father : null, "{=BC_HeroFamily_PaternalGrandfather}Paternal grandfather");
                Add(Grandparents, Known(father?.Mother) ? father.Mother : null, "{=BC_HeroFamily_PaternalGrandmother}Paternal grandmother");
                Add(Grandparents, Known(mother?.Father) ? mother.Father : null, "{=BC_HeroFamily_MaternalGrandfather}Maternal grandfather");
                Add(Grandparents, Known(mother?.Mother) ? mother.Mother : null, "{=BC_HeroFamily_MaternalGrandmother}Maternal grandmother");

                if (Known(hero.Spouse)) Add(Spouses, hero.Spouse,
                    hero.Spouse.IsFemale ? "{=BC_HeroFamily_Wife}Wife" : "{=BC_HeroFamily_Husband}Husband");
                foreach (Hero former in hero.ExSpouses.Where(Known).Distinct().Where(h => h != hero.Spouse))
                    Add(Spouses, former, former.IsFemale ? "{=BC_HeroFamily_FormerWife}Former wife" : "{=BC_HeroFamily_FormerHusband}Former husband");

                foreach (Hero child in OldestFirst(hero.Children.Where(Known)))
                    Add(Children, child, child.IsFemale ? "{=BC_HeroFamily_Daughter}Daughter" : "{=BC_HeroFamily_Son}Son");

                // Native Siblings omits the mother's other children when a father is known.
                IEnumerable<Hero> siblings = (hero.Father?.Children.AsEnumerable() ?? Enumerable.Empty<Hero>())
                    .Concat(hero.Mother?.Children.AsEnumerable() ?? Enumerable.Empty<Hero>());
                foreach (Hero sibling in OldestFirst(siblings.Where(Known)))
                    Add(Siblings, sibling, sibling.IsFemale ? "{=BC_HeroFamily_Sister}Sister" : "{=BC_HeroFamily_Brother}Brother");

                var children = new HashSet<Hero>(hero.Children.Where(h => h != null && h != hero));
                foreach (Hero grandchild in OldestFirst(children.Where(Known).SelectMany(h => h.Children)
                    .Where(h => Known(h) && !children.Contains(h))))
                    Add(Grandchildren, grandchild, grandchild.IsFemale ? "{=BC_HeroFamily_Granddaughter}Granddaughter" : "{=BC_HeroFamily_Grandson}Grandson");
            }

            IsVisible = IsVisible && (HasAncestors || HasHousehold || HasDescendantsOrSiblings);
            foreach (string property in new[] { nameof(IsVisible), nameof(HasParents), nameof(HasGrandparents), nameof(HasAncestors), nameof(HasSpouses), nameof(HasChildren), nameof(HasSiblings),
                nameof(HasGrandchildren), nameof(HasHousehold), nameof(HasDescendantsOrSiblings), nameof(ParentsGap), nameof(SpousesGap),
                nameof(SiblingsGap), nameof(ChildrenColumns),
                nameof(SiblingsColumns), nameof(GrandchildrenColumns), nameof(ChildrenWidth), nameof(SiblingsWidth), nameof(GrandchildrenWidth) })
                OnPropertyChanged(property);
            RefreshValues();
        }

        private static IEnumerable<Hero> OldestFirst(IEnumerable<Hero> heroes)
            => heroes.Distinct().OrderBy(h => h.BirthDay.ToDays).ThenBy(h => h.StringId, StringComparer.Ordinal);

        private static string CountText(string template, int count)
        {
            var text = new TextObject(template);
            text.SetTextVariable("COUNT", count);
            return text.ToString();
        }

        private static void Add(MBBindingList<EncyclopediaFamilyPortraitVM> list, Hero hero, string role)
            => list.Add(new EncyclopediaFamilyPortraitVM(hero, new TextObject(role)));

        private IEnumerable<MBBindingList<EncyclopediaFamilyPortraitVM>> Lists()
        {
            yield return Parents;
            yield return Grandparents;
            yield return Spouses;
            yield return Children;
            yield return Siblings;
            yield return Grandchildren;
        }

        private void Clear()
        {
            foreach (var list in Lists())
            {
                foreach (var member in list) member.OnFinalize();
                list.Clear();
            }
        }

        public override void RefreshValues()
        {
            base.RefreshValues();
            foreach (string property in new[] { nameof(Header), nameof(ParentsText), nameof(GrandparentsText), nameof(SpousesText),
                nameof(ChildrenText), nameof(SiblingsText), nameof(GrandchildrenText) })
                OnPropertyChanged(property);
            foreach (var list in Lists())
                foreach (var member in list) member.RefreshValues();
        }

        public override void OnFinalize()
        {
            Clear();
            base.OnFinalize();
        }
    }

    internal sealed class EncyclopediaFamilyPortraitVM : HeroVM
    {
        private readonly TextObject _role;
        [DataSourceProperty] public bool IsKnown => Hero != null;
        [DataSourceProperty] public string Role => IsKnown ? _role?.ToString() ?? string.Empty : string.Empty;

        internal EncyclopediaFamilyPortraitVM(Hero hero, TextObject role) : base(hero) { _role = role; }

        public override void RefreshValues()
        {
            base.RefreshValues();
            OnPropertyChanged(nameof(Role));
        }
    }
}
