using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapNotificationTypes;

namespace BellumCivile.UI
{
    public sealed class TitleAcquiredNotificationItemVM : MapNotificationItemBaseVM
    {
        public TitleAcquiredNotificationItemVM(TitleAcquiredMapNotification data) : base(data)
        {
            NotificationIdentifier = "settlementownerchanged";
            _onInspect = () =>
            {
                if (data.IsValid())
                {
                    if (data.Lordship != null) FastMoveCameraToPosition?.Invoke(data.Lordship.Village.Position);
                    else Campaign.Current?.EncyclopediaManager?.GoToLink(data.Recipient.EncyclopediaLink);
                }
                ExecuteRemove();
            };
        }

        public override void ManualRefreshRelevantStatus()
        {
            if (!Data.IsValid()) ExecuteRemove();
        }
    }
}
