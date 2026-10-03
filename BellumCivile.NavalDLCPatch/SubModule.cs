using Bannerlord.UIExtenderEx;
using TaleWorlds.MountAndBlade;

namespace BellumCivile.NavalDLCPatch
{
    public sealed class SubModule : MBSubModuleBase
    {
        private UIExtender _uiExtender;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            _uiExtender = UIExtender.Create("BellumCivile.NavalDLCPatch");
            NavalDlcPatchBootstrap.Register(_uiExtender);
            _uiExtender.Enable();
        }
    }
}
