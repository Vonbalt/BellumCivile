using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class HostagePactText
    {
        internal static HostagePactRecord Find(Hero hero)
            => Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>()?.GetProtectedPact(hero);

        internal static TreatyHostageRecord Hostage(HostagePactRecord pact, Hero hero)
            => pact == null || hero == null ? null : pact.FirstHostage?.Hero == hero ? pact.FirstHostage
                : pact.SecondHostage?.Hero == hero ? pact.SecondHostage : null;

        internal static TextObject Status(HostagePactRecord pact, Hero hero)
        {
            var text = new TextObject(pact.Phase == HostagePactPhase.Active
                ? "{=BC_Hostage_Status}{HERO} is held at {HOLDING} as a hostage to secure peace with {REALM}. The pledge lasts until {DATE}; an ordinary ransom cannot secure their release."
                : "{=BC_Hostage_StatusPending}{HERO} remains in treaty custody at {HOLDING}. The pledge of peace with {REALM} has ended, and their fate awaits settlement.");
            Fill(text, pact, hero);
            text.SetTextVariable("DATE", CampaignTime.Days((float)pact.EndDay).ToString());
            return text;
        }

        internal static TextObject BrokerRefusal(HostagePactRecord pact, Hero hero)
        {
            bool signed = SignedBy(pact, hero, Hero.MainHero);
            TextObject text = BrokerWords(signed, pact.Phase == HostagePactPhase.Active);
            Fill(text, pact, hero);
            return text;
        }

        internal static bool SignedBy(HostagePactRecord pact, Hero hostage, Hero speaker)
            => pact != null && hostage != null && speaker != null
                && (pact.FirstHostage?.Hero == hostage ? pact.FirstSignatory == speaker
                    : pact.SecondHostage?.Hero == hostage && pact.SecondSignatory == speaker);

        internal static TextObject BrokerWords(bool signedByPlayer, bool active)
            => new TextObject(!active
                ? "{=BC_Hostage_BrokerPending}Last I heard, {HERO} was still held under the pledge of peace with {REALM}. Though that pledge has ended, their fate has not yet been settled. I cannot buy their freedom while the rulers deliberate."
                : signedByPlayer
                    ? "{=BC_Hostage_BrokerSigned}Last I heard, {HERO} was being held as a hostage to secure peace with {REALM}, under the treaty you agreed. A purse of silver cannot undo a sovereign's pledge. This is a matter for your diplomats, not a simple broker like myself."
                    : "{=BC_Hostage_BrokerOther}Last I heard, {HERO} was being held as a hostage to secure peace with {REALM}, under a treaty between the two realms. A purse of silver cannot undo a sovereign's pledge. This is a matter for their rulers, not a simple broker like myself.");

        internal static void Fill(TextObject text, HostagePactRecord pact, Hero hero)
        {
            var record = Hostage(pact, hero);
            var other = pact.FirstHostage?.Hero == hero ? pact.SecondRealm : pact.FirstRealm;
            text.SetTextVariable("HERO", hero?.Name ?? TextObject.GetEmpty());
            text.SetTextVariable("REALM", other?.Name ?? TextObject.GetEmpty());
            text.SetTextVariable("HOLDING", record?.Holding?.Name ?? TextObject.GetEmpty());
        }
    }
}
