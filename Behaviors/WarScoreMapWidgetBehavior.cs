using BellumCivile.UI.Map;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.Core;

namespace BellumCivile.Behaviors
{
    internal sealed class WarScoreMapWidgetBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.TickEvent.AddNonSerializedListener(this, TryAddMapView);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void TryAddMapView(float deltaTime)
        {
            if (!(Game.Current?.GameStateManager?.ActiveState is MapState) || MapScreen.Instance == null)
                return;

            MapScreen.Instance.AddMapView<GauntletWarScoreMapWidget>();
            CampaignEvents.TickEvent.ClearListeners(this);
        }
    }
}
