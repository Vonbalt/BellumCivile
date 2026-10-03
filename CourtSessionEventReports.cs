using System;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class CourtSessionEventReports
    {
        internal static TextObject Narrative(string id)
        {
            string text;
            switch (id)
            {
                case "BC_CourtCavalry": text = "The lords of the {FACTION} faction in {REALM} have sent noble cavalry to join {RULER}'s retinue, placing their household riders at the Crown's service."; break;
                case "BC_CourtEstateInvestment": text = "The lords of the {FACTION} faction in {REALM} have invested in the markets and workshops of {TARGETS}, putting their wealth behind local trade."; break;
                case "BC_CourtStonemasons": text = "The lords of the {FACTION} faction in {REALM} have supplied skilled masons and materials to {TARGETS}, repairing fortifications and tending to their estates."; break;
                case "BC_CourtBanquet": text = "A banquet hosted by the {FACTION} faction in {REALM} has brought {TARGETS} closer to {RULER}, renewing ties between the noble houses and the Crown."; break;
                case "BC_CourtEndorsement": text = "The lords of the {FACTION} faction in {REALM} have publicly affirmed their support for {RULER}, lending the weight of their houses to the Crown's authority."; break;
                case "BC_CourtExtraordinaryAid": text = "The lords of the {FACTION} faction in {REALM} have granted extraordinary aid to {RULER}, opening their coffers in support of the Crown."; break;
                case "BC_CourtCensure": text = "The lords of the {FACTION} faction in {REALM} have formally censured {RULER}, calling the Crown's judgment into question before the court."; break;
                case "BC_CourtInvestmentWithdrawn": text = "The lords of the {FACTION} faction in {REALM} have withdrawn their backing from the markets of {TARGETS}. Coin lies idle in private coffers while local trade suffers."; break;
                case "BC_CourtLaborWithheld": text = "The lords of the {FACTION} faction in {REALM} have withheld workers and materials from {TARGETS}, leaving repairs unfinished and their fortifications neglected."; break;
                case "BC_CourtObstruction": text = "Officials backed by the {FACTION} faction in {REALM} have obstructed administration in {TARGETS}. Petitions go unanswered and the work of government languishes."; break;
                case "BC_CourtSnub": text = "The {FACTION} faction in {REALM} has encouraged a pointed snub of the Crown by {TARGETS}, deepening the rift with {RULER}."; break;
                case "BC_CourtArmyInspiration": text = "The martial fervor of the {FACTION} faction in {REALM} has stirred the hosts commanded by {TARGETS}, renewing their resolve to serve under the Crown's banners."; break;
                case "BC_CourtArmyProtest": text = "Discontent within the {FACTION} faction in {REALM} has unsettled the hosts commanded by {TARGETS}. Their soldiers march with growing reluctance as discipline falters."; break;
                case "BC_CourtVeterans": text = "At the urging of the {FACTION} faction in {REALM}, seasoned soldiers have joined {RULER}'s retinue, offering their swords in the Crown's service."; break;
                case "BC_CourtMartialParade": text = "The lords of the {FACTION} faction in {REALM} have staged a grand martial parade in honor of {RULER}, proclaiming the Crown's strength before the assembled crowds."; break;
                case "BC_CourtGarrisonSupport": text = "The lords of the {FACTION} faction in {REALM} have rallied support for the garrisons of {TARGETS}, encouraging their defenders to keep faith with the Crown."; break;
                case "BC_CourtMartialGames": text = "Martial games sponsored by the {FACTION} faction in {REALM} have drawn {TARGETS} into fellowship with {RULER}, strengthening bonds through contests of skill and courage."; break;
                case "BC_CourtNeglectedWatches": text = "Disaffected retainers of the {FACTION} faction in {REALM} have neglected their watches in {TARGETS}, leaving the inhabitants less secure and less trusting of the Crown."; break;
                case "BC_CourtMartialMockery": text = "The lords of the {FACTION} faction in {REALM} have openly mocked {RULER}'s prowess as a commander, diminishing the Crown's standing at court."; break;
                case "BC_CourtAgriculturalAid": text = "The lords of the {FACTION} faction in {REALM} have supplied aid to the farming households of {TARGETS}, helping them tend their fields and sustain their villages."; break;
                case "BC_CourtRuralNoncooperation": text = "Encouraged by the {FACTION} faction in {REALM}, rural households in {TARGETS} have withheld their cooperation with the Crown. The resulting disruption has taken its toll on village life."; break;
                case "BC_CourtCommunityPatronage": text = "The {FACTION} faction in {REALM} has extended patronage to the communities represented by {TARGETS}, winning goodwill for {RULER} among their local notables."; break;
                case "BC_CourtNotableAgitation": text = "The {FACTION} faction in {REALM} has stirred grievances among {TARGETS}, turning these local notables against {RULER}."; break;
                case "BC_CourtFestival": text = "The lords of the {FACTION} faction in {REALM} have sponsored festivities in {TARGETS}, offering hospitality to the commons in a gesture of goodwill and concord."; break;
                case "BC_CourtDebtRelief": text = "Backed by the {FACTION} faction in {REALM}, debts have been forgiven in {TARGETS}, easing hardship and giving local trade room to recover."; break;
                case "BC_CourtTownWatches": text = "At the urging of the {FACTION} faction in {REALM}, inhabitants of {TARGETS} have rallied to their local watches, taking a greater hand in keeping the peace."; break;
                case "BC_CourtStrikes": text = "Encouraged by the {FACTION} faction in {REALM}, workers in {TARGETS} have laid down their tools. Livelihoods suffer as resentment toward the Crown spreads."; break;
                case "BC_CourtRiots": text = "Agitation encouraged by the {FACTION} faction in {REALM} has spilled into the streets of {TARGETS}. Rioters defy the authorities and disturb the peace."; break;
                case "BC_CourtLevyEvasion": text = "The {FACTION} faction in {REALM} has encouraged the inhabitants of {TARGETS} to evade the levy, weakening local order and defying the Crown's authority."; break;
                case "BC_CourtTaxBoycott": text = "Encouraged by the {FACTION} faction in {REALM}, taxpayers have withheld their dues in defiance of the Crown, depriving {RULER} of revenue."; break;
                default: throw new ArgumentOutOfRangeException(nameof(id), id, "Court event has no narrative report.");
            }
            return new TextObject("{=" + id + "_Report}" + text);
        }

        internal static string Change(float before, float after, TextObject unit)
        {
            float delta = after - before;
            if (Math.Abs(delta) < 0.005f) delta = 0;
            return delta.ToString("+0.##;-0.##;0") + " " + unit;
        }

        internal static string ForTarget(object target, string changes) =>
            new TextObject("{=BC_CourtEvent_TargetEffects}{TARGET}: {CHANGES}")
                .SetTextVariable("TARGET", target?.ToString() ?? "")
                .SetTextVariable("CHANGES", changes).ToString();
    }
}
