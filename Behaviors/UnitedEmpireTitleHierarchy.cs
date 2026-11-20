using System;
using System.Collections.Generic;
using System.Linq;

namespace BellumCivile.Behaviors
{
    internal sealed class UnitedEmpireTitleHierarchy
    {
        internal const string KingdomId = "calradian_empire";
        internal const string CrownTitleId = "bc_title_kingdom_" + KingdomId;
        private static readonly HashSet<string> FormerKingdoms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "empire", "empire_s", "empire_w"
        };

        internal IReadOnlyList<FeudalTitleConfig.ConfiguredTitle> Titles { get; }
        internal IReadOnlyList<string> CrownChildren { get; }

        private UnitedEmpireTitleHierarchy(IReadOnlyList<FeudalTitleConfig.ConfiguredTitle> titles, IReadOnlyList<string> crownChildren)
        {
            Titles = titles;
            CrownChildren = crownChildren;
        }

        internal static UnitedEmpireTitleHierarchy From(IReadOnlyList<FeudalTitleConfig.ConfiguredTitle> source,
            Func<string, string> resolveKingdomId)
        {
            bool IsFormerKingdom(string reference) => !string.IsNullOrWhiteSpace(reference)
                && (FormerKingdoms.Contains(reference) || FormerKingdoms.Contains(resolveKingdomId(reference)));

            var foldedRefs = new HashSet<string>(FormerKingdoms, StringComparer.OrdinalIgnoreCase)
            {
                "empire_calradia", "bc_title_kingdom_empire", "bc_title_kingdom_empire_n",
                "bc_title_kingdom_empire_s", "bc_title_kingdom_empire_w", KingdomId, CrownTitleId
            };
            var folded = source.Where(title => title != null && (foldedRefs.Contains(title.Id)
                || (title.Type == FeudalTitleType.Kingdom && IsFormerKingdom(title.KingdomRef)))).ToList();
            foldedRefs.UnionWith(folded.Select(title => title.Id));

            // Move child declarations from removed crowns onto the generated crown without creating another title record.
            var crownChildren = folded.SelectMany(title => title.ChildRefs)
                .Where(child => !foldedRefs.Contains(child))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var inheritedChildRefs = new HashSet<string>(crownChildren, StringComparer.OrdinalIgnoreCase);
            var titles = new List<FeudalTitleConfig.ConfiguredTitle>(source.Count - folded.Count);
            foreach (FeudalTitleConfig.ConfiguredTitle title in source)
            {
                if (title == null || foldedRefs.Contains(title.Id))
                    continue;

                bool newParent = foldedRefs.Contains(title.ParentRef);
                bool newKingdom = IsFormerKingdom(title.KingdomRef) || newParent
                    || inheritedChildRefs.Contains(title.Id) || inheritedChildRefs.Contains(title.SettlementRef);
                List<string> children = title.ChildRefs.Any(foldedRefs.Contains)
                    ? title.ChildRefs.Where(child => !foldedRefs.Contains(child)).ToList()
                    : title.ChildRefs;
                titles.Add(newKingdom || children != title.ChildRefs
                    ? title.WithHierarchyReferences(newKingdom ? KingdomId : title.KingdomRef,
                        newParent ? CrownTitleId : title.ParentRef, children)
                    : title);
            }

            return new UnitedEmpireTitleHierarchy(titles, crownChildren);
        }
    }
}
