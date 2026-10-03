using System.Reflection;
using Bannerlord.UIExtenderEx;

namespace BellumCivile.NavalDLCPatch
{
    public static class NavalDlcPatchBootstrap
    {
        private static bool _registered;
        private static UIExtender _uiExtender;

        public static bool Register(UIExtender uiExtender)
        {
            if (_registered || uiExtender == null)
                return false;

            uiExtender.Register(Assembly.GetExecutingAssembly());
            _registered = true;
            return true;
        }

        public static bool EnableStandalone()
        {
            if (_registered)
                return false;

            _uiExtender = UIExtender.Create("BellumCivile.NavalDLCPatch");
            _uiExtender.Register(Assembly.GetExecutingAssembly());
            _uiExtender.Enable();
            _registered = true;
            return true;
        }

        public static bool IsRegistered => _registered;
    }
}
