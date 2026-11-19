using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class InternalPeaceCourierNotice : InformationData
    {
        [SaveableField(10)] internal string ConflictId;
        [SaveableField(11)] internal float ExpiresDay;
        [SaveableField(12)] internal bool Delivered;
        [SaveableField(13)] internal string SenderLeaderId;

        public override TextObject TitleText => new TextObject("{=BC_InternalPeace_Title}Terms of Reconciliation");
        public override string SoundEventPath => "event:/ui/notification/peace_offer";

        internal InternalPeaceCourierNotice(string id, Kingdom sender, float expiresDay)
            : base(new TextObject("{=BC_InternalPeace_Courier}A courier from {REALM} brings terms of reconciliation.")
                .SetTextVariable("REALM", sender.Name))
        {
            ConflictId = id;
            ExpiresDay = expiresDay;
            SenderLeaderId = sender.Leader?.StringId;
        }

        public override bool IsValid() => InternalPeaceSettlementBehavior.Current?.IsCurrentCourier(this) == true;
    }
}
