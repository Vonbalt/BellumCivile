using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using JetBrains.Annotations;
using System.Collections.Generic;

namespace BellumCivile.ViewModelMixin
{
    [PrefabExtension("HeirSelectionPopup", "descendant::ButtonWidget[@Id='HeirSelectionOkButton']")]
    [UsedImplicitly]
    internal sealed class HeirSelectionConfirmButtonLegalHeirStatePatch : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => new List<Attribute>
        {
            new Attribute("IsEnabled", "@BellumCanConfirmLegalHeir")
        };
    }
}
