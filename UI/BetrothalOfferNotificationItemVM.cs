using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapNotificationTypes;

namespace BellumCivile.UI
{
    public sealed class BetrothalOfferNotificationItemVM : MapNotificationItemBaseVM
    {
        public BetrothalOfferNotificationItemVM(BetrothalOfferNotice notice) : base(notice)
        {
            NotificationIdentifier = "marriage";
            _onInspect = () =>
            {
                BetrothalBehavior.Instance?.InspectIncomingOffer(notice.Pledge);
                if (!notice.IsValid()) ExecuteRemove();
            };
        }

        public override void ManualRefreshRelevantStatus()
        {
            if (!Data.IsValid()) ExecuteRemove();
        }
    }
}
