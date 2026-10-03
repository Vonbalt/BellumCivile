using TaleWorlds.Library;

namespace BellumCivile.UI.VanillaTabs.Kingdoms.Hierarchy
{
    public sealed class HierarchyTierColumnVM : ViewModel
    {
        public HierarchyTierColumnVM(string name)
        {
            Name = name;
            Titles = new MBBindingList<HierarchyTitleNodeVM>();
        }

        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public MBBindingList<HierarchyTitleNodeVM> Titles { get; }
    }
}
