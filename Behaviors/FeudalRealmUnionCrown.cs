using System;

namespace BellumCivile.Behaviors
{
    public partial class FeudalTitleBehavior
    {
        internal void ApplyRealmUnionCrownInheritance(RealmUnionRecord journal)
        {
            EnsureCollectionsInitialized();
            // Pin both identities before rebuilding title indexes. The inherited shell
            // still exists during movement, but must not become the primary Crown.
            _currentRealmTitleByKingdomId[journal.Destination.StringId] = journal.PrimaryCrownId;
            _currentRealmTitleByKingdomId[journal.Source.StringId] = journal.InheritedCrownId;
            LegalizeTitleInheritance(journal.SurvivingHouse, GetTitle(journal.InheritedCrownId), "personal union inheritance");
            RefreshCachedTitleNames();
        }
    }
}
