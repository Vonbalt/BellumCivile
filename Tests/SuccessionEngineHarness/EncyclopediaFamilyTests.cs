using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

internal static class EncyclopediaFamilyTests
{
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static readonly Type PortraitBase = Type.GetType(
        "TaleWorlds.CampaignSystem.ViewModelCollection.HeroVM, TaleWorlds.CampaignSystem.ViewModelCollection", true);

    // Only native portrait construction is stubbed: rendering requires an engine process.
    private static bool Portrait(object __instance, Hero hero)
    {
        AccessTools.Field(PortraitBase, "<Hero>k__BackingField").SetValue(__instance, hero);
        AccessTools.Property(PortraitBase, "IsDead").SetValue(__instance, hero?.IsAlive == false);
        return false;
    }
    private static bool NoName() => false;
    private static bool Birthday(Hero __instance, ref CampaignTime __result)
    { __result = (CampaignTime)AccessTools.Field(typeof(Hero), "_birthDay").GetValue(__instance); return false; }

    internal static void Run(Action<bool, string> check, string game)
    {
        using var compatibility = new EncyclopediaFamilyCompatibilityTests();
        compatibility.Run(check, game);
        CheckLayout(check, game);
        var assembly = typeof(FeudalTitleRecord).Assembly;
        var type = assembly.GetType("BellumCivile.UI.EncyclopediaHeroFamilyVM", true);
        var vm = Activator.CreateInstance(type, true);
        var harmony = new Harmony("bellum.tests.encyclopedia_family");
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        object oldTicks = ticks.GetValue(null);
        var hidden = new HashSet<Hero>();
        Func<Hero, bool> visible = hero => !hidden.Contains(hero);
        var notifications = new HashSet<string>();
        ((ViewModel)vm).PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        object Value(object item, string name) => AccessTools.Property(item.GetType(), name).GetValue(item);
        object Get(string name) => Value(vm, name);
        object[] Entries(string name) => ((IEnumerable)Get(name)).Cast<object>().ToArray();
        Hero[] Heroes(string name) => Entries(name).Select(entry => (Hero)Value(entry, "Hero")).ToArray();
        string[] Roles(string name) => Entries(name).Select(entry => (string)Value(entry, "Role")).ToArray();
        var layoutType = assembly.GetType("BellumCivile.ViewModelMixin.EncyclopediaHeroFeudalTitlesPrefabExtension", true);
        var layout = (XmlDocument)AccessTools.Method(layoutType, "GetPrefabExtension").Invoke(Activator.CreateInstance(layoutType, true), null);
        int ActiveColumns(string name)
        {
            var group = layout.SelectSingleNode("//*[@Id='BellumFamily" + name + "']");
            bool Visible(XmlNode grid)
            {
                for (XmlNode node = grid; node != group.ParentNode; node = node.ParentNode)
                {
                    if (!(node is XmlElement element)) continue;
                    foreach (string attribute in new[] { "IsVisible", "IsHidden" })
                    {
                        string binding = element.GetAttribute(attribute);
                        if (binding.StartsWith("@") && (bool)Get(binding.Substring(1)) != (attribute == "IsVisible")) return false;
                    }
                }
                return true;
            }
            var grid = group.SelectNodes(".//NavigatableGridWidget").Cast<XmlElement>().Single(Visible);
            return int.Parse(grid.GetAttribute("ColumnCount"));
        }
        void Refresh(Hero hero, bool concealed = false, Func<Hero, bool> filter = null)
            => AccessTools.Method(type, "Refresh").Invoke(vm, new object[] { hero, concealed, filter ?? visible });
        Hero Person(string id, int born, bool female = false, bool dead = false)
        {
            var hero = Blank<Hero>(); hero.StringId = id;
            AccessTools.Field(typeof(Hero), "_children").SetValue(hero, new MBList<Hero>());
            AccessTools.Field(typeof(Hero), "_exSpouses").SetValue(hero, new MBList<Hero>());
            AccessTools.Field(typeof(Hero), "_birthDay").SetValue(hero, CampaignTime.Days(born));
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(hero, dead ? Hero.CharacterStates.Dead : Hero.CharacterStates.Active);
            AccessTools.PropertySetter(typeof(Hero), "IsFemale").Invoke(hero, new object[] { female });
            return hero;
        }
        try
        {
            ticks.SetValue(null, 24000L);
            harmony.Patch(AccessTools.Constructor(PortraitBase, new[] { typeof(Hero), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(EncyclopediaFamilyTests), nameof(Portrait)));
            harmony.Patch(AccessTools.Method(PortraitBase, "RefreshValues"),
                prefix: new HarmonyMethod(typeof(EncyclopediaFamilyTests), nameof(NoName)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), "BirthDay"),
                prefix: new HarmonyMethod(typeof(EncyclopediaFamilyTests), nameof(Birthday)));

            var hero = Person("viewed", 1000);
            Refresh(hero);
            check(!(bool)Get("IsVisible") && Heroes("Parents").SequenceEqual(new Hero[] { null, null })
                && Heroes("Grandparents").SequenceEqual(new Hero[] { null, null, null, null }),
                "An entirely unknown family is hidden while ancestry keeps its stable internal slot order");
            check(Entries("Parents").Concat(Entries("Grandparents")).All(e => !(bool)Value(e, "IsKnown") && (string)Value(e, "Role") == ""),
                "Unknown slots have neither a portrait nor a relationship label");
            check(!(bool)Get("HasHousehold") && !(bool)Get("HasDescendantsOrSiblings"),
                "Empty optional rows do not appear");
            check(!(bool)Get("HasParents") && !(bool)Get("HasGrandparents") && !(bool)Get("HasAncestors"),
                "All-unknown ancestry hides the headings and the entire ancestry row");
            check((int)Get("ParentsGap") == 0 && (int)Get("SpousesGap") == 0 && (int)Get("SiblingsGap") == 0,
                "Empty family rows do not reserve a gap");

            var father = Person("father", 500);
            var mother = Person("mother", 510, true);
            var paternalGrandfather = Person("paternal_grandfather", 100, dead: true);
            var maternalGrandmother = Person("maternal_grandmother", 120, true, true);
            hero.Father = father; hero.Mother = mother;
            Refresh(hero);
            check((bool)Get("HasParents") && !(bool)Get("HasGrandparents") && (bool)Get("HasAncestors"),
                "Known parents display independently of an empty grandparents section");
            check((int)Get("ParentsGap") == 0, "Parents alone have no trailing group gap");
            father.Father = paternalGrandfather; mother.Mother = maternalGrandmother;
            Refresh(hero);
            check(Heroes("Parents").SequenceEqual(new[] { father, mother }), "Parents occupy father and mother slots");
            check((int)Get("ParentsGap") == 16, "Parents and grandparents have a distinct inter-group gap");
            check(Heroes("Grandparents").SequenceEqual(new[] { paternalGrandfather, null, null, maternalGrandmother }),
                "Partially known grandparents retain their paternal and maternal positions");
            check(Roles("Grandparents")[0] == "Paternal grandfather" && Roles("Grandparents")[3] == "Maternal grandmother",
                "Grandparents have explicit localized labels rather than native unspecified-relative fallbacks");
            check((bool)Value(Entries("Grandparents")[0], "IsDead"), "Deceased ancestors retain native deceased portrait state");

            var young = Person("young", 3000, true);
            var deadOlder = Person("older_died_young", 2000, dead: true);
            var tiedB = Person("twin_b", 2500);
            var tiedA = Person("twin_a", 2500, true);
            young.Father = hero; tiedB.Father = hero; deadOlder.Father = hero; tiedA.Father = hero;
            hero.Children.Add(young); hero.Children.Add(null);
            Refresh(hero);
            check(Heroes("Children").SequenceEqual(new[] { deadOlder, tiedA, tiedB, young }),
                "Children use birth order, deduplicate records, and order equal birthdays deterministically");
            check((string)Get("ChildrenText") == "Children (4)" && Roles("Children").Last() == "Daughter",
                "Children heading counts visible portraits and retains relationship labels");
            var order = PatchProcessor.GetOriginalInstructions(AccessTools.Method(type, "OldestFirst"));
            check(!order.Any(i => i.Calls(AccessTools.PropertyGetter(typeof(Hero), "Age"))),
                "Birth-order sorting does not compare frozen ages at death");
            check((int)Get("ChildrenColumns") == 7 && (int)Get("ChildrenWidth") == 700 && ActiveColumns("Children") == 7,
                "Children use seven literal portrait columns when no spouse is present");
            check((int)Get("SpousesGap") == 0, "Children alone do not inherit an empty spouse-column gap");

            var currentSpouse = Person("current_spouse", 1100, true);
            var formerSpouse = Person("former_spouse", 1050, true, true);
            hero.Spouse = formerSpouse; hero.Spouse = null; hero.Spouse = currentSpouse;
            ((MBList<Hero>)AccessTools.Field(typeof(Hero), "_exSpouses").GetValue(hero)).Add(formerSpouse);
            Refresh(hero);
            check(Heroes("Spouses").SequenceEqual(new[] { currentSpouse, formerSpouse }),
                "Current spouse comes first, with former spouses preserved once");
            check(Roles("Spouses").SequenceEqual(new[] { "Wife", "Former wife" }),
                "Current and former wives are labeled without repeating the Spouse heading");
            check((int)Get("ChildrenColumns") == 6 && (int)Get("ChildrenWidth") == 600 && (bool)Get("HasHousehold")
                && ActiveColumns("Children") == 6,
                "Spouse column leaves six literal native-sized child portrait columns");
            check((int)Get("SpousesGap") == (int)Get("ParentsGap"),
                "Spouse and children use the same separation as parents and grandparents");

            var paternalHalf = Person("paternal_half", 800); paternalHalf.Father = father;
            var maternalHalf = Person("maternal_half", 900, true); maternalHalf.Mother = mother;
            var fullSibling = Person("full_sibling", 950); fullSibling.Father = father; fullSibling.Mother = mother;
            Refresh(hero);
            check(Heroes("Siblings").SequenceEqual(new[] { paternalHalf, maternalHalf, fullSibling }),
                "Siblings include both parental lines without duplicating full siblings or the viewed hero");
            check((string)Get("SiblingsText") == "Siblings (3)" && Roles("Siblings")[1] == "Sister",
                "Sibling count and labels reflect the combined family");
            Person("fourth_sibling", 1100).Father = father;
            Refresh(hero);
            check(Heroes("Siblings").Length == 4 && ActiveColumns("Siblings") == 7,
                "Four siblings without grandchildren remain on one seven-column row");
            check((int)Get("SiblingsGap") == 0, "Siblings alone keep the full row without a trailing gap");

            var grandson = Person("grandson", 4000); grandson.Father = young;
            var granddaughter = Person("granddaughter", 3500, true); granddaughter.Father = tiedA;
            tiedB.Children.Add(granddaughter); tiedB.Children.Add(null);
            Refresh(hero);
            check(Heroes("Grandchildren").SequenceEqual(new[] { granddaughter, grandson }),
                "Grandchildren are a separate oldest-first group with duplicates removed");
            check((string)Get("GrandchildrenText") == "Grandchildren (2)" && Roles("Grandchildren")[0] == "Granddaughter",
                "Grandchild heading and labels are independent of children");
            check((int)Get("SiblingsColumns") == 3 && (int)Get("GrandchildrenColumns") == 4
                && ActiveColumns("Siblings") == 3 && ActiveColumns("Grandchildren") == 4,
                "Siblings and grandchildren share seven native-sized slots without stretching portraits");
            check((int)Get("SiblingsGap") == (int)Get("ParentsGap") && (int)Get("SiblingsGap") == (int)Get("SpousesGap"),
                "Every paired family row uses the same group separation");

            for (int i = 0; i < 23; i++) Person("extra_child_" + i, 5000 + i).Father = hero;
            Refresh(hero);
            check(Heroes("Children").Length == 27 && (string)Get("ChildrenText") == "Children (27)",
                "Large families retain every visible child for wrapped portrait rows");
            string before = string.Join("|", Heroes("Children").Select(h => h.StringId));
            Refresh(hero);
            check(string.Join("|", Heroes("Children").Select(h => h.StringId)) == before,
                "Repeated refreshes neither duplicate relatives nor change their order");

            hidden.UnionWith(new[] { father, currentSpouse, formerSpouse, maternalHalf, young });
            Refresh(hero);
            check(Heroes("Parents")[0] == null && Heroes("Grandparents")[0] == null,
                "Hidden parent and its ancestry do not leak through the replacement layout");
            check(!(bool)Get("HasSpouses") && !Heroes("Children").Contains(young) && !Heroes("Siblings").Contains(maternalHalf)
                && !Heroes("Grandchildren").Contains(grandson), "Native visibility filtering is applied to every family group");
            check((string)Get("ChildrenText") == "Children (26)" && (int)Get("ChildrenColumns") == 7 && ActiveColumns("Children") == 7,
                "Hidden relatives do not inflate counts or reserve a spouse column");
            check((int)Get("SpousesGap") == 0, "Hiding the spouse group also removes its separation");
            hidden.Add(mother); Refresh(hero);
            check(!(bool)Get("HasAncestors") && (bool)Get("IsVisible") && (bool)Get("HasChildren"),
                "A family with visible descendants but unknown ancestry has no empty ancestry band");

            int traversals = 0;
            Refresh(hero, true, h => { traversals++; return true; });
            check(!(bool)Get("IsVisible") && traversals == 0
                && new[] { "Parents", "Grandparents", "Spouses", "Children", "Siblings", "Grandchildren" }.All(n => Entries(n).Length == 0),
                "Hidden viewed heroes clear every group without examining their relatives");
            Refresh(null);
            check(!(bool)Get("IsVisible"), "A missing viewed hero cannot leave stale family portraits");
            check(notifications.Contains("ChildrenText") && notifications.Contains("ChildrenColumns") && notifications.Contains("IsVisible")
                && notifications.Contains("ParentsGap") && notifications.Contains("SpousesGap") && notifications.Contains("SiblingsGap"),
                "Refresh notifies headings, layout dimensions, and visibility bindings");

            hidden.Clear();
            var lone = Person("lone", 0);
            var loneGrandchild = Person("lone_grandchild", 2000); var loneChild = Person("lone_child", 1000);
            loneChild.Father = lone; loneGrandchild.Father = loneChild;
            Refresh(lone);
            check((int)Get("GrandchildrenColumns") == 7 && (int)Get("GrandchildrenWidth") == 700 && ActiveColumns("Grandchildren") == 7,
                "Grandchildren use the full third row when there are no siblings");
            Refresh(fullSibling);
            check((int)Get("SiblingsColumns") == 7 && ActiveColumns("Siblings") == 7,
                "Siblings use the full third row when there are no grandchildren");
            var marriedWoman = Person("married_woman", 100, true);
            var husband = Person("husband", 100);
            var formerHusband = Person("former_husband", 50, dead: true);
            marriedWoman.Spouse = formerHusband; marriedWoman.Spouse = null; marriedWoman.Spouse = husband;
            Refresh(marriedWoman);
            check(Roles("Spouses").SequenceEqual(new[] { "Husband", "Former husband" }),
                "Current and former husbands use their correct relationship labels");
            check(!(bool)Get("HasAncestors") && (bool)Get("HasHousehold") && (bool)Get("IsVisible"),
                "Spouse-only families display without a blank ancestor row");
            check((int)Get("SpousesGap") == 0, "Spouses alone have no trailing group separation");

            ((ViewModel)vm).OnFinalize();
            check(Entries("Parents").Length == 0 && Entries("Children").Length == 0,
                "Finalization releases portrait lists");

            var mixinType = assembly.GetType("BellumCivile.ViewModelMixin.EncyclopediaHeroFamilyMixin", true);
            var attribute = mixinType.GetCustomAttributes(false).Single(a => a.GetType().Name == "ViewModelMixinAttribute");
            check((string)Value(attribute, "RefreshMethodName") == "RefreshValues",
                "The family mixin uses the startup-safe RefreshValues hook");
            var pageType = Type.GetType("TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages.EncyclopediaHeroPageVM, TaleWorlds.CampaignSystem.ViewModelCollection", true);
            var page = (ViewModel)FormatterServices.GetUninitializedObject(pageType);
            var mixin = Activator.CreateInstance(mixinType, new object[] { page });
            var policy = assembly.GetType("BellumCivile.UI.EncyclopediaFamilyLayoutCompatibility", true);
            var changed = AccessTools.Field(policy, "Changed");
            check(((Delegate)changed.GetValue(null)).GetInvocationList().Any(d => ReferenceEquals(d.Target, mixin)),
                "Open hero pages subscribe to family layout ownership changes");
            var layoutNotifications = new HashSet<string>();
            page.PropertyChanged += (_, args) => layoutNotifications.Add(args.PropertyName);
            compatibility.SetSharedLayout(true);
            check(!(bool)Value(mixin, "BellumUseFamilyLayout") && layoutNotifications.Contains("BellumUseFamilyLayout")
                && !(bool)Value(Value(mixin, "BellumFamily"), "IsVisible"),
                "Falling back notifies an already constructed page and hides its grouped family section");
            compatibility.SetSharedLayout(false);
            var refreshes = new List<object>();
            harmony.Patch(AccessTools.Method(mixinType, "OnRefresh"), prefix: new HarmonyMethod(
                typeof(EncyclopediaFamilyTests), nameof(RecordRefresh)));
            _refreshes = refreshes;
            try
            {
                AccessTools.Property(pageType, "IsLoadingOver").SetValue(page, false);
                AccessTools.Property(pageType, "NameText").SetValue(page, "Unrelated change");
                check(refreshes.Count == 0, "Loading and unrelated page changes do not rebuild family portraits");
                AccessTools.Property(pageType, "IsLoadingOver").SetValue(page, true);
                check(refreshes.Count == 1 && refreshes[0] == mixin,
                    "Native Refresh completion updates the family without patching the unsafe method");
                AccessTools.Property(pageType, "IsLoadingOver").SetValue(page, false);
                AccessTools.Property(pageType, "IsLoadingOver").SetValue(page, true);
                check(refreshes.Count == 2, "Repeated native page refreshes keep updating the family");
                AccessTools.Method(mixinType, "OnFinalize").Invoke(mixin, null);
                check(changed.GetValue(null) == null || !((Delegate)changed.GetValue(null)).GetInvocationList().Any(d => ReferenceEquals(d.Target, mixin)),
                    "Closing the page releases its family layout ownership subscription");
                AccessTools.Property(pageType, "IsLoadingOver").SetValue(page, false);
                AccessTools.Property(pageType, "IsLoadingOver").SetValue(page, true);
                check(refreshes.Count == 2, "Closing the page unsubscribes its family completion handler");
            }
            finally { _refreshes = null; }
            var portraitType = assembly.GetType("BellumCivile.UI.EncyclopediaFamilyPortraitVM", true);
            foreach (string nativeMethod in new[] { "ExecuteLink", "ExecuteBeginHint", "ExecuteEndHint" })
                check(AccessTools.Method(portraitType, nativeMethod).DeclaringType == PortraitBase,
                    "Family portraits retain native " + nativeMethod);
        }
        finally { harmony.UnpatchAll(harmony.Id); ticks.SetValue(null, oldTicks); }
    }

    private static List<object> _refreshes;
    private static bool RecordRefresh(object __instance) { _refreshes?.Add(__instance); return false; }

    private static void CheckLayout(Action<bool, string> check, string game)
    {
        var assembly = typeof(FeudalTitleRecord).Assembly;
        var titlesType = assembly.GetType("BellumCivile.ViewModelMixin.EncyclopediaHeroFeudalTitlesPrefabExtension", true);
        var patchType = assembly.GetType("BellumCivile.ViewModelMixin.EncyclopediaHeroFamilyPrefabPatch", true);
        var patch = Activator.CreateInstance(patchType, true);
        var injected = (XmlDocument)AccessTools.Method(titlesType, "GetPrefabExtension")
            .Invoke(Activator.CreateInstance(titlesType, true), null);
        void Apply(XmlDocument document) => AccessTools.Method(patchType, "Apply").Invoke(patch, new object[] { document });
        foreach (bool familyFirst in new[] { false, true })
        {
            var document = new XmlDocument();
            document.Load(Path.Combine(game, "Modules/SandBox/GUI/Prefabs/Encyclopedia/EncyclopediaSubPages/EncyclopediaHeroPage.xml"));
            if (familyFirst) Apply(document);
            var allies = document.SelectSingleNode("//*[@Id='AlliesDivider']");
            allies.ParentNode.ReplaceChild(document.ImportNode(injected.DocumentElement, true), allies);
            if (!familyFirst) Apply(document);
            var dividers = document.SelectNodes("//EncyclopediaDivider").Cast<XmlElement>().Select(n => n.GetAttribute("Id")).ToList();
            check(dividers.IndexOf("BellumInheritanceDivider") < dividers.IndexOf("BellumFamilyDivider")
                && dividers.IndexOf("BellumFamilyDivider") < dividers.IndexOf("AlliesDivider")
                && dividers.IndexOf("AlliesDivider") < dividers.IndexOf("EnemiesDivider"),
                "Family follows Bellum titles and precedes Friends/Enemies regardless of patch order: " + familyFirst);
            check(document.SelectNodes("//*[@Id='FamilyGrid' or @Id='FamilyDivider']").Count == 2
                && document.SelectNodes("//NavigationScopeTargeter[@ScopeParent='..\\FamilyGrid']").Count == 1
                && document.SelectNodes("//*[@IsHidden='true' and (@Id='FamilyGrid' or @Id='FamilyDivider' or @ScopeParent='..\\FamilyGrid')]").Count == 3,
                "Native family anchors remain in place and hide only while Bellum owns the layout: " + familyFirst);
            check(document.SelectNodes("//*[@Id='AlliesGridParent' or @Id='EnemiesGridParent']").Count == 2,
                "Friends and Enemies lists remain untouched: " + familyFirst);
            var newAllies = (XmlElement)document.SelectSingleNode("//*[@Id='AlliesDivider']");
            check(newAllies.GetAttribute("Parameter.ItemList") == "..\\..\\AlliesGridParent"
                && newAllies.ParentNode.ParentNode.ParentNode.SelectSingleNode("Widget[@Id='AlliesGridParent']") != null,
                "Friends collapse still reaches its original list after the insertion");
            string once = document.OuterXml; Apply(document);
            check(document.OuterXml == once, "Family visibility patch is idempotent: " + familyFirst);
        }

        var section = (XmlElement)injected.SelectSingleNode("//*[@Id='BellumFamilySection']");
        check(section.GetAttribute("DataSource") == "{BellumFamily}" && section.GetAttribute("IsVisible") == "@IsVisible",
            "Family section uses its own visibility-guarded data source");
        check(section.SelectNodes("Children/ListPanel/Children/ListPanel").Count == 3,
            "Family has exactly three logical two-column rows");
        check(section.SelectNodes(".//NavigatableGridWidget").Count == 9,
            "Six family groups use literal-count grids with shared and full-width variants where needed");
        foreach (string group in new[] { "Parents", "Spouses", "Siblings" })
            check(section.SelectSingleNode(".//*[@Id='BellumFamily" + group + "' and @MarginRight='@" + group + "Gap']") != null,
                "Group spacing binds on the family view model, not on a portrait list: " + group);
        foreach (string group in new[] { "Grandparents", "Children", "Grandchildren" })
            check(section.SelectSingleNode(".//*[@Id='BellumFamily" + group + "' and @MarginRight='0']") != null,
                "Right-hand family group has no extra trailing margin: " + group);
        check(section.SelectSingleNode(".//*[@Id='BellumFamilyAncestors' and @IsVisible='@HasAncestors']") != null
            && section.SelectSingleNode(".//*[@Id='BellumFamilyParents' and @IsVisible='@HasParents']") != null
            && section.SelectSingleNode(".//*[@Id='BellumFamilyGrandparents' and @IsVisible='@HasGrandparents']") != null,
            "Empty ancestry groups and their entire row are hidden, not just their text");
        var divider = (XmlElement)section.SelectSingleNode(".//*[@Id='BellumFamilyDivider']");
        check(divider.GetAttribute("Parameter.ItemList") == "..\\BellumFamilyBody",
            "One Family divider collapses all three rows");
        foreach (XmlElement grid in section.SelectNodes(".//NavigatableGridWidget"))
        {
            check(grid.GetAttribute("DefaultCellWidth") == "100" && grid.GetAttribute("DefaultCellHeight") == "140"
                && grid.GetAttribute("HeightSizePolicy") == "CoverChildren" && grid.GetAttribute("WidthSizePolicy") == "CoverChildren",
                "Native-width portrait cells never spread out to fill unused space: " + grid.GetAttribute("Id"));
            var group = (XmlElement)grid.SelectSingleNode("ancestor::ListPanel[@SuggestedWidth][1]");
            check(group.GetAttribute("WidthSizePolicy") == "Fixed" && group.GetAttribute("HeightSizePolicy") == "CoverChildren",
                "Each group has a bounded measurement width: " + grid.GetAttribute("Id"));
            check(int.TryParse(grid.GetAttribute("ColumnCount"), out int columns) && columns >= 1 && columns <= 7,
                "Column count is literal, not an unresolved property on the relatives list: " + grid.GetAttribute("Id"));
            check(grid.ParentNode.SelectSingleNode("NavigationScopeTargeter").Attributes["AlternateMovementStepSize"].Value == grid.GetAttribute("ColumnCount"),
                "Controller navigation follows the actual column count: " + grid.GetAttribute("Id"));
            check(grid.SelectSingleNode(".//EncyclopediaSubPageElement[@IsVisible='@IsKnown' and @VerticalAlignment='Top']") != null,
                "Known relatives reuse native portrait sizing and unknown slots cannot receive portrait input: " + grid.GetAttribute("Id"));
        }
        var nativePortrait = new XmlDocument(); nativePortrait.Load(Path.Combine(game,
            "Modules/SandBox/GUI/Prefabs/Encyclopedia/EncyclopediaSubPages/EncyclopediaSubPageElement.xml"));
        check(nativePortrait.SelectSingleNode("//ButtonWidget[@Command.Click='ExecuteLink']") != null
            && nativePortrait.SelectSingleNode("//HintWidget[@Command.HoverBegin='ExecuteBeginHint']") != null,
            "Reused native portrait supports encyclopedia links and hero tooltips on this game version");
        var absent = new XmlDocument(); absent.LoadXml("<Prefab><Window><Widget /></Window></Prefab>");
        string original = absent.OuterXml; Apply(absent);
        check(absent.OuterXml == original, "Family patch leaves an unfamiliar replacement page alone");
        absent.LoadXml("<Prefab><Window><Children><EncyclopediaDivider Id='FamilyDivider' /><NavigatableGridWidget Id='FamilyGrid' DataSource='{Family}' /></Children></Window></Prefab>");
        original = absent.OuterXml; Apply(absent);
        check(absent.OuterXml == original, "Original family remains when another layout has removed the replacement insertion point");
        var restored = new XmlDocument();
        restored.Load(Path.Combine(game, "Modules/SandBox/GUI/Prefabs/Encyclopedia/EncyclopediaSubPages/EncyclopediaHeroPage.xml"));
        Apply(restored);
        AccessTools.Method(patchType, "InsertSection").Invoke(null, new object[] { injected });
        check(injected.SelectNodes("//*[@Id='BellumFamilySection']").Count == 1, "Family insertion is idempotent");
        var nativePage = new XmlDocument();
        nativePage.Load(Path.Combine(game, "Modules/SandBox/GUI/Prefabs/Encyclopedia/EncyclopediaSubPages/EncyclopediaHeroPage.xml"));
        var nativeContent = (XmlElement)nativePage.SelectSingleNode("//*[@Id='FamilyGrid']/ancestor::Widget[@SuggestedWidth][1]");
        var body = (XmlElement)section.SelectSingleNode(".//*[@Id='BellumFamilyBody']");
        float availableWidth = float.Parse(nativeContent.GetAttribute("SuggestedWidth")) - float.Parse(body.GetAttribute("MarginLeft"));
        CheckNativeMeasurement(section, availableWidth, check);
    }

    private static void CheckNativeMeasurement(XmlElement section, float availableWidth, Action<bool, string> check)
    {
        var assembly = Assembly.Load("TaleWorlds.GauntletUI");
        var gridType = Assembly.Load("TaleWorlds.MountAndBlade.GauntletUI.Widgets")
            .GetType("TaleWorlds.MountAndBlade.GauntletUI.Widgets.NavigatableGridWidget", true);
        AccessTools.Method(assembly.GetType("TaleWorlds.GauntletUI.WidgetInfo", true), "Refresh").Invoke(null, null);
        var contextType = assembly.GetType("TaleWorlds.GauntletUI.UIContext", true);
        var context = FormatterServices.GetUninitializedObject(contextType);
        var events = FormatterServices.GetUninitializedObject(assembly.GetType("TaleWorlds.GauntletUI.EventManager", true));
        AccessTools.Property(contextType, "EventManager").SetValue(context, events);
        var cellType = assembly.GetType("TaleWorlds.GauntletUI.BaseTypes.ListPanel", true);
        void Set(object target, string name, object value)
        {
            var property = AccessTools.Property(target.GetType(), name);
            property.SetValue(target, property.PropertyType.IsEnum ? Enum.Parse(property.PropertyType, (string)value) : value);
        }
        foreach (float scale in new[] { 0.75f, 1f, 1.5f })
        {
            Set(context, "CustomScale", scale); Set(context, "Scale", scale);
            foreach (XmlElement template in section.SelectNodes(".//NavigatableGridWidget"))
            {
                int columns = int.Parse(template.GetAttribute("ColumnCount"));
                var group = Activator.CreateInstance(cellType, context);
                Set(group, "WidthSizePolicy", "Fixed"); Set(group, "SuggestedWidth", 700f);
                Set(group, "HeightSizePolicy", "CoverChildren");
                var grid = Activator.CreateInstance(gridType, context);
                Set(grid, "WidthSizePolicy", template.GetAttribute("WidthSizePolicy"));
                Set(grid, "HeightSizePolicy", "CoverChildren"); Set(grid, "ColumnCount", columns);
                Set(grid, "DefaultCellWidth", float.Parse(template.GetAttribute("DefaultCellWidth")));
                Set(grid, "DefaultCellHeight", float.Parse(template.GetAttribute("DefaultCellHeight")));
                Set(grid, "ParentWidget", group);
                for (int i = 0; i < 27; i++)
                {
                    var cell = Activator.CreateInstance(cellType, context);
                    Set(cell, "WidthSizePolicy", "Fixed"); Set(cell, "SuggestedWidth", 100f);
                    Set(cell, "HeightSizePolicy", "Fixed"); Set(cell, "SuggestedHeight", 140f);
                    Set(cell, "ParentWidget", grid);
                }
                var measure = AccessTools.Method(cellType, "Measure");
                var vectorType = measure.GetParameters()[0].ParameterType;
                measure.Invoke(group, new[] { Activator.CreateInstance(vectorType) });
                var size = AccessTools.Property(cellType, "MeasuredSize").GetValue(group);
                float height = (float)vectorType.GetField("Y").GetValue(size);
                var gridSize = AccessTools.Property(gridType, "MeasuredSize").GetValue(grid);
                float width = (float)vectorType.GetField("X").GetValue(gridSize);
                object gridLayout = AccessTools.Property(gridType, "GridLayout").GetValue(grid);
                var widths = ((IEnumerable)AccessTools.Property(gridLayout.GetType(), "ColumnWidths").GetValue(gridLayout)).Cast<float>().ToArray();
                check(Math.Abs(height - Math.Ceiling(27d / columns) * 140 * scale) < 0.01
                    && Math.Abs(width - columns * 100 * scale) < 0.01
                    && widths.All(w => Math.Abs(w - 100 * scale) < 0.01),
                    "Actual prefab grid keeps 100-unit portrait spacing and wraps at its literal count: " + template.GetAttribute("Id") + ", scale=" + scale);
            }
            foreach (var pair in new[] { ("Parents", "Grandparents"), ("Spouses", "Children"), ("Siblings", "Grandchildren") })
            {
                var leftTemplate = (XmlElement)section.SelectSingleNode(".//*[@Id='BellumFamily" + pair.Item1 + "']");
                var rightTemplate = (XmlElement)section.SelectSingleNode(".//*[@Id='BellumFamily" + pair.Item2 + "']");
                int Columns(XmlElement template) => int.Parse(((XmlElement)template.SelectSingleNode(".//NavigatableGridWidget")).GetAttribute("ColumnCount"));
                foreach (int visible in new[] { 1, 2, 3 })
                {
                    bool hasLeft = (visible & 1) != 0, hasRight = (visible & 2) != 0;
                    var row = Activator.CreateInstance(cellType, context);
                    Set(row, "WidthSizePolicy", "Fixed"); Set(row, "SuggestedWidth", availableWidth);
                    Set(row, "HeightSizePolicy", "CoverChildren");
                    Set(AccessTools.Property(cellType, "StackLayout").GetValue(row), "LayoutMethod", "HorizontalLeftToRight");
                    var left = Activator.CreateInstance(cellType, context);
                    var right = Activator.CreateInstance(cellType, context);
                    int leftColumns = !hasRight && pair.Item1 == "Siblings" ? 7 : Columns(leftTemplate);
                    int rightColumns = !hasLeft && pair.Item2 != "Grandparents" ? 7 : Columns(rightTemplate);
                    foreach (object group in new[] { left, right })
                    {
                        Set(group, "WidthSizePolicy", "Fixed"); Set(group, "HeightSizePolicy", "Fixed");
                        Set(group, "SuggestedHeight", 140f); Set(group, "ParentWidget", row);
                    }
                    Set(left, "SuggestedWidth", leftColumns * 100f); Set(right, "SuggestedWidth", rightColumns * 100f);
                    Set(left, "IsVisible", hasLeft); Set(right, "IsVisible", hasRight);
                    Set(left, "MarginRight", hasLeft && hasRight ? 16f : 0f);
                    var measure = AccessTools.Method(cellType, "Measure");
                    var vectorType = measure.GetParameters()[0].ParameterType;
                    measure.Invoke(row, new[] { Activator.CreateInstance(vectorType) });
                    AccessTools.Method(cellType, "Layout").Invoke(row, new object[] { 0f, 140 * scale, availableWidth * scale, 0f });
                    float leftStart = (float)AccessTools.Property(cellType, "Left").GetValue(left);
                    float leftEnd = (float)AccessTools.Property(cellType, "Right").GetValue(left);
                    float rightStart = (float)AccessTools.Property(cellType, "Left").GetValue(right);
                    float rightEnd = (float)AccessTools.Property(cellType, "Right").GetValue(right);
                    bool spacing = hasLeft && hasRight ? Math.Abs(rightStart - leftEnd - 16 * scale) < 0.01
                        : Math.Abs(hasLeft ? leftStart : rightStart) < 0.01;
                    check(spacing && (!hasLeft || leftEnd <= availableWidth * scale + 0.01)
                        && (!hasRight || rightEnd <= availableWidth * scale + 0.01),
                        "Native paired row has a gap only between visible groups and fits the page: " + pair.Item1 + "/" + pair.Item2
                        + ", visible=" + visible + ", scale=" + scale);
                }
            }
        }
    }
}
