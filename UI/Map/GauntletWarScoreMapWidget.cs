using SandBox.View.Map;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;
using TaleWorlds.ScreenSystem;

namespace BellumCivile.UI.Map
{
    [ViewCreatorModule]
    public sealed class GauntletWarScoreMapWidget : MapView
    {
        private WarScoreMapWidgetVM _dataSource;
        private GauntletLayer _layer;
        private GauntletMovieIdentifier _movie;

        protected override void CreateLayout()
        {
            base.CreateLayout();
            _dataSource = new WarScoreMapWidgetVM();
            _layer = new GauntletLayer("BellumWarScoreMapWidgetLayer", 101);
            _movie = _layer.LoadMovie("BellumWarScoreMapWidget", _dataSource);
            Layer = _layer;
            Layer.InputRestrictions.SetInputRestrictions(false, InputUsageMask.MouseButtons);
            MapScreen.AddLayer(Layer);
        }

        protected override void OnFinalize()
        {
            if (_movie != null && _layer != null)
                _layer.ReleaseMovie(_movie);

            _dataSource?.OnFinalize();
            _movie = null;
            _dataSource = null;
            _layer = null;
            base.OnFinalize();
        }

        protected override void OnMapConversationStart()
        {
            base.OnMapConversationStart();
            if (_layer != null)
                ScreenManager.SetSuspendLayer(_layer, true);
        }

        protected override void OnMapConversationOver()
        {
            base.OnMapConversationOver();
            if (_layer != null)
                ScreenManager.SetSuspendLayer(_layer, false);
        }
    }
}
