using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class TitleAcquiredMapNotification : InformationData
    {
        [SaveableField(10)] public string TitleId;
        [SaveableField(11)] public PersonalLordshipRecord Lordship;
        [SaveableField(12)] public Hero Recipient;
        [SaveableField(13)] public Clan ReceivingClan;
        [SaveableField(14)] public int Rights; // Bit 1: lawful title; bit 2: practical authority.
        [SaveableField(15)] public bool NativeCovered;

        public override TextObject TitleText => new TextObject("{=BC_TitleAcquired_Header}Title Acquired");
        public override string SoundEventPath => "event:/ui/notification/settlement_owner_change";

        public TitleAcquiredMapNotification(FeudalTitleRecord title, PersonalLordshipRecord lordship, Hero recipient, int rights)
            : base(new TextObject("{=BC_TitleAcquired_Body}New holder of the {TITLE} is {HOLDER}.")
                .SetTextVariable("TITLE", lordship != null ? LordshipBehavior.Name(lordship.Village)
                    : new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatTitleName(title, recipient.Clan)))
                .SetTextVariable("HOLDER", recipient.Name))
        {
            TitleId = title?.TitleId;
            Lordship = lordship;
            Recipient = recipient;
            ReceivingClan = recipient.Clan;
            Rights = rights;
        }

        public override bool IsValid()
        {
            if (Recipient == null || Recipient != Hero.MainHero || !Recipient.IsAlive || Recipient.IsDisabled
                || ReceivingClan == null || ReceivingClan.IsEliminated || Recipient.Clan != ReceivingClan) return false;
            if (Lordship != null)
            {
                var lordships = Campaign.Current == null ? LordshipBehavior.Instance : Campaign.Current.GetCampaignBehavior<LordshipBehavior>();
                return Lordship.Holder == Recipient && Lordship.IsValid
                    && lordships?.IsRegisteredGrant(Lordship) == true;
            }
            var titles = Campaign.Current == null ? FeudalTitleBehavior.Instance : Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            var title = titles?.GetTitle(TitleId);
            return ReceivingClan.Leader == Recipient && title?.IsActive == true
                && ((Rights & 1) != 0 && title.DeJureHolderClanId == ReceivingClan.StringId
                    || (Rights & 2) != 0 && title.DeFactoHolderClanId == ReceivingClan.StringId);
        }

        internal bool SameAcquisition(TitleAcquiredMapNotification other) => other != null && Recipient == other.Recipient
            && ReceivingClan == other.ReceivingClan && (Lordship != null ? Lordship == other.Lordship
                : other.Lordship == null && TitleId == other.TitleId);
    }
}
