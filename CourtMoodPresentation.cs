using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class CourtMoodPresentation
    {
        private static readonly Dictionary<string, (string Name, string Hint)> Entries =
            new Dictionary<string, (string Name, string Hint)>
            {
                ["BC_CourtSupportedPolicies"] = ("Laws in Our Favor", "The laws of the realm give weight to the causes this faction champions. Its members see their counsel reflected in the government of the realm."),
                ["BC_CourtOpposedPolicies"] = ("Laws Against Our Interests", "The realm is governed by measures this faction regards as harmful to its rights or ambitions. Each such law remains a source of grievance."),
                ["BC_CourtCentralization"] = ("The Crown's Encroachment", "As more authority passes into the ruler's hands, the houses of the court find less room for their own counsel and influence."),
                ["BC_CourtGloryWar"] = ("A Field for Glory", "War offers the realm's warriors a chance to win honor and distinguish their houses in service to the Crown."),
                ["BC_CourtGloryPeace"] = ("Idle Swords", "Long peace leaves those who seek renown in battle without a worthy campaign. Their impatience grows as opportunities for glory pass them by."),
                ["BC_CourtTributeReceived"] = ("Tribute to Our Might", "Foreign payments are taken as recognition of the realm's strength, lending pride to those who champion its martial standing."),
                ["BC_CourtTributePaid"] = ("The Price of Submission", "Wealth sent abroad is a bitter reminder that the realm has yielded to another power. Its warriors regard the burden as a stain upon their honor."),
                ["BC_CourtLibertyPeace"] = ("The Fruits of Peace", "Fields can be tended and markets supplied without the demands of a foreign war. The faction welcomes the respite granted to the realm's people."),
                ["BC_CourtLibertyWar"] = ("The Burden of War", "Prolonged campaigning draws labor and wealth away from the realm's communities. The faction grows weary of the sacrifices demanded of them."),
                ["BC_CourtTrade"] = ("Open Markets", "Agreements with foreign realms promise steadier commerce and wider opportunity. The faction welcomes ties that allow prosperity to travel beyond the border."),
                ["BC_CourtNoTrade"] = ("Mercantile Isolation", "Without foreign trade agreements, the faction sees opportunities for commerce neglected and urges the Crown to cultivate them."),
                ["BC_CourtProsperity"] = ("Flourishing Towns", "Prosperous towns with provision enough to sustain them give the faction reason to praise the realm's stewardship."),
                ["BC_CourtHunger"] = ("Empty Granaries", "Too many towns have exhausted their stores. The faction hears in their hardship a reproach to those entrusted with governing the realm."),
                ["BC_CourtAlliances"] = ("Friends Beyond Our Borders", "Alliances lend the realm standing among foreign courts and give its noble houses confidence that they need not face every danger alone."),
                ["BC_CourtNoAlliances"] = ("Alone Among Realms", "The absence of foreign allies leaves the nobility uneasy about the realm's standing and the dangers it may have to face unaided."),
                ["BC_CourtInternalPeace"] = ("Peace Among the Houses", "The great houses may dispute one another's claims, but their quarrels have not become open war. The faction values the order that preserves their estates."),
                ["BC_CourtInternalWar"] = ("The Realm Divided", "Noble houses have taken up arms within the realm. Civil strife imperils their estates and unsettles the order upon which their rights depend."),
                ["BC_CourtLawfulEstates"] = ("Rights of Possession Upheld", "Recognized owners hold the estates that are theirs by law. The nobility takes comfort in a settlement of property that respects its titles."),
                ["BC_CourtUnlawfulEstates"] = ("Estates Held Against Right", "Too many estates are held by those without the recognized right to possess them. The nobility sees force displacing the protection of lawful title."),
                ["BC_CourtConcentratedLand_Nobility"] = ("The Weight of Great Houses", "Much of the landed wealth rests with the strongest noble houses. The faction welcomes the standing this gives the realm's leading families."),
                ["BC_CourtConcentratedLand_Liberty"] = ("Estates in Too Few Hands", "A narrow circle of houses commands a disproportionate share of the land. The faction resents the influence concentrated in their hands."),
                ["BC_CourtBroadLand_Liberty"] = ("A Wider Share of the Land", "Land is spread more evenly among the realm's vassal houses. The faction welcomes a balance that leaves fewer families overshadowed by great estates."),
                ["BC_CourtBroadLand_Nobility"] = ("The Great Houses Diminished", "The spread of estates leaves the leading houses with less of the predominance this faction believes they should command."),
                ["BC_CourtOwnSeats"] = ("Our Voice in Council", "Members of the faction hold offices from which they can shape the realm's affairs. Their presence gives its interests a hearing close to the throne."),
                ["BC_CourtOtherSeats"] = ("Rival Voices in Council", "Other factions hold offices that bring their counsel before the ruler. Their influence leaves this faction wary of being set aside."),
                ["BC_CourtAdvisorRepresentation"] = ("Advocates at Court", "Advisors charged with representing the faction give its concerns a place in the deliberations of government."),
                ["BC_CourtCrownFavor"] = ("In the Crown's Favor", "For this court term, the ruler has chosen to favor the faction. Its members welcome the prospect of their concerns receiving a more sympathetic hearing."),
                ["BC_CourtCrownFavorsOthers"] = ("Others Enjoy the Crown's Favor", "The ruler has lent favor to another faction for this term. These houses resent seeing rival concerns placed before their own."),
                ["BC_Ag_AwardedFief"] = ("A House Rewarded", "One of the faction's houses has received land. Its fellows welcome the grant as an advancement of their standing within the realm."),
                ["BC_Ag_SnubFief"] = ("Passed Over for Land", "Land has been granted outside the faction, leaving its houses to feel that their own claims to reward have gone unanswered."),
                ["BC_Ag_WinElec"] = ("Our Candidate Crowned", "The candidate backed by the faction has secured the throne through election. Its houses welcome a result that vindicates their support."),
                ["BC_Ag_LostElec"] = ("Our Candidate Rejected", "The election has placed another candidate upon the throne. The faction resents the defeat of the choice it supported."),
                ["BC_Ag_FiefHumiliation_Nobility"] = ("An Estate Taken Away", "A member has been stripped of land. The noble houses regard the loss as an affront to the security of their own possessions."),
                ["BC_Ag_FiefHumiliation_Glory"] = ("Service Met with Dishonor", "A member has lost an estate. The faction sees in that dispossession a humiliation of one of its own houses."),
                ["BC_Ag_FiefHumiliation_Liberty"] = ("A House Dispossessed", "A member has been deprived of land. The faction fears the use of dispossession against those who fall out of favor."),
                ["BC_Ag_ExpulsionAttempt"] = ("One of Our Own Accused", "A member stands accused of treason. The faction closes ranks, troubled by the threat now hanging over one of its own houses."),
                ["BC_Ag_TribunalApproval"] = ("Judgment Welcomed", "The settlement imposed upon the defeated has been welcomed by the faction, bringing relief or satisfaction after the conflict."),
                ["BC_Ag_TribunalReprisal"] = ("Judgment Resented", "The treatment of its members among the defeated has embittered the faction. The tribunal's judgments have left grievances that peace alone cannot settle."),
                ["BC_Ag_MilVic"] = ("Honor Won in Battle", "A major victory has strengthened the pride of the realm's warriors. The faction welcomes proof that its faith in martial achievement is justified."),
                ["BC_Ag_MilDef"] = ("Honor Lost in Battle", "A major defeat has shaken the realm's warriors. The faction reproaches a government under which their arms have suffered such a reverse."),
                ["BC_Ag_RulerCaptured"] = ("The Crown in Captivity", "The ruler has fallen into enemy hands. The faction sees the capture as a humiliation of the realm and its military standing."),
                ["BC_Ag_NewHouse"] = ("A New House Raised", "A companion has been raised to lead a noble house. The faction welcomes the advancement of someone beyond the established ruling families."),
                ["BC_Ag_Raids"] = ("The Countryside Ravaged", "Raids have brought suffering to the realm's villages. The faction reproaches the failure to protect the people whose labor sustains it."),
                ["BC_Ag_NobleExecuted"] = ("Noble Blood Shed", "A noble has been put to death by royal sentence. The execution alarms the great houses, who see a threat to the security of their own rank."),
                ["BC_Ag_LordCaptured"] = ("A Noble in Enemy Hands", "A noble of the realm has been taken prisoner. The faction is troubled by the danger to one of the houses upon which its order rests."),
                ["BC_Ag_CouncilMemberAppointed"] = ("A Seat at the Ruler's Side", "A member has secured office on the {COUNCIL_NAME}. The faction welcomes the trust placed in its counsel and the influence that accompanies it."),
                ["BC_Ag_CouncilIncumbentRetained"] = ("Our Counsel Reaffirmed", "The realm has confirmed the faction's councilor in office, renewing the standing of its voice in government."),
                ["BC_Ag_CouncilCandidatePassedOver"] = ("Our Nominee Passed Over", "The faction's preferred candidate has failed to secure office on the {COUNCIL_NAME}. Its members resent seeing their counsel set aside."),
                ["BC_Ag_CouncilChoiceOverruled"] = ("The Court's Choice Overruled", "The ruler has rejected the preferred appointment. The faction takes the intervention as a rebuff to its influence at court."),
                ["BC_Ag_CouncilMemberDismissed"] = ("Cast Out of Council", "The ruler has dismissed a member from the {COUNCIL_NAME}, depriving the faction of a trusted voice near the throne."),
                ["BC_CourtAgendaSuccess"] = ("Our Motion Carried", "The faction has carried its proposed measure through the court. Its members welcome the result as recognition of their counsel."),
                ["BC_CourtAgendaFailure"] = ("Our Motion Rejected", "The court has rejected the faction's proposed measure. Its members resent the failure of an initiative they sought to advance."),
                ["BC_CourtObjectiveSuccess"] = ("A Promise Fulfilled", "The objective urged by the faction has been achieved. Its houses credit the fulfillment of their appeal."),
                ["BC_CourtObjectiveFailure"] = ("An Appeal Unanswered", "The faction's objective has ended without the result it sought. Its houses reproach the failure to fulfill their appeal."),
                ["BC_Ag_Conquest"] = ("Our Banners Advance", "A settlement has fallen to the realm's armies. The faction celebrates the advance as proof of its warriors' strength."),
                ["BC_Ag_LostSettlement"] = ("Our Hold Broken", "An enemy has seized a settlement from the realm. The faction regards the loss as a reverse that diminishes its martial standing."),
                ["BC_Ag_SettlementRebellion"] = ("A Town in Revolt", "A settlement has risen in rebellion. The faction sees the unrest as a rebuke to the government of the realm."),
                ["BC_Ag_Agitation"] = ("Whispers Against the Crown", "Bitter accusations against the Crown are circulating among the faction's houses, deepening their distrust of its rule."),
            };

        private static string Key(string id, FactionType type)
        {
            string variant = id + "_" + type;
            return Entries.ContainsKey(variant) ? variant : id;
        }

        internal static string Name(string id, FactionType type, string fallback)
        {
            string key = Key(id, type);
            return Entries.TryGetValue(key, out var entry)
                ? new TextObject("{=BC_MoodName_" + key + "}" + entry.Name).ToString() : fallback;
        }

        internal static string Hint(string id, FactionType type, Kingdom realm)
        {
            string key = Key(id, type);
            if (!Entries.TryGetValue(key, out var entry)) return "";
            var text = new TextObject("{=BC_MoodHint_" + key + "}" + entry.Hint);
            return CourtInstitutionDisplayHelper.ApplyPrivyCouncilName(text, realm).ToString();
        }
    }
}
