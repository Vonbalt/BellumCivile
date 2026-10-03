using System.Collections.Generic;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.HeirSelectionPopup;

namespace BellumCivile.ViewModelMixin
{
    internal static class HeirSelectionPopupLegalHeirMixinRegistry
    {
        private static readonly Dictionary<HeirSelectionPopupVM, HeirSelectionPopupLegalHeirMixin> Mixins =
            new Dictionary<HeirSelectionPopupVM, HeirSelectionPopupLegalHeirMixin>();

        public static void Register(HeirSelectionPopupVM vm, HeirSelectionPopupLegalHeirMixin mixin)
        {
            if (vm == null || mixin == null)
                return;

            Mixins[vm] = mixin;
        }

        public static void Refresh(HeirSelectionPopupVM vm)
        {
            if (vm != null && Mixins.TryGetValue(vm, out HeirSelectionPopupLegalHeirMixin mixin))
                mixin.RefreshLegalHeirState();
        }

        public static void Unregister(HeirSelectionPopupVM vm)
        {
            if (vm != null)
                Mixins.Remove(vm);
        }
    }
}
