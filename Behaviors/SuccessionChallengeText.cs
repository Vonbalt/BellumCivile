using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class SuccessionChallengeBehavior
    {
        internal static string DemandDescription(SuccessionChallengeDemand demand, bool grant, bool ruler)
        {
            if (demand == SuccessionChallengeDemand.Crown)
                return ruler
                    ? "{=BC_Challenge_RulerCrown}{HEIR} has openly laid claim to your crown. Emboldened by the houses gathered in support, they demand that you yield the throne of {REALM}, or face their banners in rebellion."
                    : "{=BC_Challenge_CallCrown}Discord has broken out within the ruling family of {REALM}. {HEIR} has openly challenged {RULER} for the crown and is calling upon the realm's houses to help seize the throne.";
            if (grant)
                return ruler
                    ? "{=BC_Challenge_RulerGrant}{HEIR} refuses to remain without lands of their own. With other houses now taking up the cause, they demand lands from the royal domains to establish their own household, threatening to claim your crown by force if turned away."
                    : "{=BC_Challenge_CallGrant}Discord has broken out within the ruling family of {REALM}. With no landed share of the inheritance awaiting them, {HEIR} demands that {RULER} grant lands from the royal domains, threatening to press a claim to the crown if refused.";
            return ruler
                ? "{=BC_Challenge_RulerInheritance}{HEIR} will no longer wait to command lands of their own. With other houses now taking up the cause, they demand lands to establish their own household while you still reign, threatening to claim your crown by force if refused."
                : "{=BC_Challenge_CallInheritance}Discord has broken out within the ruling family of {REALM}. {HEIR} demands that {RULER} grant lands to establish an independent household, threatening to press a claim to the crown if refused.";
        }

        private static TextObject DemandMessage(SuccessionChallengeRecord record, bool ruler)
        {
            var demand = new TextObject(DemandDescription(record.Demand, record.FallbackGrant, ruler));
            SetSubjects(demand, record);
            var text = new TextObject(ruler
                ? "{=BC_Challenge_Ultimatum}{DEMAND}\n\nAn answer is expected. Will you give way, or stand against this defiance?"
                : "{=BC_Challenge_Call}{DEMAND}\n\nAn envoy asks your house to stand with {HEIR} should this quarrel come to war. Will you pledge your support, or remain loyal to {RULER}?");
            text.SetTextVariable("DEMAND", demand);
            SetSubjects(text, record);
            return text;
        }

        internal static string PowerDescription(double backing, double loyalists)
        {
            if (!SuccessionChallengeRules.Finite(backing) || !SuccessionChallengeRules.Finite(loyalists)
                || backing < 0 || loyalists < 0 || backing + loyalists == 0)
                return "{=BC_Challenge_PowerUncertain}The strength of the opposing sides is not yet clear.";
            if (backing > 0 && backing >= loyalists * 2)
                return "{=BC_Challenge_PowerDominant}{HEIR}'s supporters appear to hold an overwhelming advantage over the houses loyal to {RULER}.";
            if (backing > loyalists * 1.1)
                return "{=BC_Challenge_PowerStronger}{HEIR}'s supporters appear stronger than the houses loyal to {RULER}.";
            if (backing >= loyalists * 0.9)
                return "{=BC_Challenge_PowerEven}{HEIR}'s supporters and the houses loyal to {RULER} appear closely matched.";
            if (backing >= loyalists * 0.5)
                return "{=BC_Challenge_PowerWeaker}The houses loyal to {RULER} appear stronger than {HEIR}'s supporters.";
            return "{=BC_Challenge_PowerOutmatched}{HEIR}'s supporters appear greatly outmatched by the houses loyal to {RULER}.";
        }

        private static string PowerSummary(SuccessionChallengeRecord record)
        {
            var balance = new TextObject(PowerDescription(record.BackingPower, record.LoyalistPower));
            SetSubjects(balance, record);
            return new TextObject("{=BC_Challenge_Power}{BALANCE}").SetTextVariable("BALANCE", balance).ToString();
        }
    }
}
