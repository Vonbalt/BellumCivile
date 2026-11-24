using BellumCivile.Behaviors;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class BetrothalOfferNotice : InformationData
    {
        [SaveableField(10)] internal BetrothalRecord Pledge;

        internal BetrothalOfferNotice(BetrothalRecord pledge) : base(OfferTitle(pledge)) => Pledge = pledge;

        internal static TextObject OfferTitle(BetrothalRecord pledge) => new TextObject("{=BC_Betrothal_OfferHeader}Betrothal offer from {CLAN}")
            .SetTextVariable("CLAN", pledge?.OtherPlayerHouse?.Name ?? new TextObject(string.Empty));

        public override TextObject TitleText => OfferTitle(Pledge);
        public override string SoundEventPath => "event:/ui/notification/marriage";
        public override bool IsValid() => BetrothalBehavior.Instance?.IsIncomingOfferCurrent(Pledge) == true;
    }
}
