using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapNotificationTypes;

namespace BellumCivile.UI
{
    public sealed class InternalPeaceCourierNotificationItemVM : MapNotificationItemBaseVM
    {
        public InternalPeaceCourierNotificationItemVM(InternalPeaceCourierNotice notice) : base(notice)
        {
            NotificationIdentifier = "ransom"; // Native peace-offer icon, without native tribute or vote callbacks.
            _onInspect = () =>
            {
                InternalPeaceSettlementBehavior.Current?.InspectCourier(notice);
                if (!notice.IsValid()) ExecuteRemove();
            };
        }

        public override void ManualRefreshRelevantStatus()
        {
            if (!Data.IsValid()) ExecuteRemove();
        }
    }
}
