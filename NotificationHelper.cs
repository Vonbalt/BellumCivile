﻿﻿﻿using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    /// <summary>
    /// Why did I do this file?
    /// To centralize and standardize all UI text outputs, information messages, and dialogue popups, specifically ensuring that foreign events are blocked from spamming the player's event log.
    /// </summary>
    public static class NotificationHelper
    {
        private static readonly Color FormedColor = BellumNotificationColors.FactionActivity;
        private static readonly Color LeftColor = BellumNotificationColors.FactionDeparture;
        private static readonly Color DisbandedColor = BellumNotificationColors.FactionDisbanded;

        public static void ShowClanJoinedFaction(Clan clan, FactionObject faction)
        {
            if (faction == null) return;

            TextObject text = new TextObject("{=BC_Notification_Joined}The {CLAN_NAME} have pledged their support to the {FACTION_NAME} of {KINGDOM_NAME}.");
            text.SetTextVariable("CLAN_NAME", clan.Name);
            text.SetTextVariable("FACTION_NAME", GetFactionDisplayName(faction));
            text.SetTextVariable("KINGDOM_NAME", faction.ParentKingdom?.Name ?? new TextObject(""));

            BellumCivileNotifications.Show(text, FormedColor, primaryKingdom: faction.ParentKingdom, primaryClan: clan);
        }

        public static void ShowFactionFormed(FactionObject faction)
        {
            if (faction.Leader == null) return;

            TextObject text = new TextObject("{=BC_Notification_Formed}{CLAN_NAME} has rallied supporters to form the {FACTION_NAME} of {KINGDOM_NAME}.");
            text.SetTextVariable("CLAN_NAME", faction.Leader.Name);
            text.SetTextVariable("FACTION_NAME", GetFactionDisplayName(faction));
            text.SetTextVariable("KINGDOM_NAME", faction.ParentKingdom?.Name ?? new TextObject(""));

            BellumCivileNotifications.Show(text, FormedColor, primaryKingdom: faction.ParentKingdom, primaryClan: faction.Leader);
        }

        public static void ShowClanLeftFaction(Clan clan, FactionObject faction, string reason)
        {
            if (faction == null) return;

            TextObject text = new TextObject("{=BC_Notification_Abandoned}The {CLAN_NAME} clan has abandoned the {FACTION_NAME} of {KINGDOM_NAME} ({REASON}).");
            text.SetTextVariable("CLAN_NAME", clan.Name);
            text.SetTextVariable("FACTION_NAME", GetFactionDisplayName(faction));
            text.SetTextVariable("KINGDOM_NAME", faction.ParentKingdom?.Name ?? new TextObject(""));
            text.SetTextVariable("REASON", reason);

            BellumCivileNotifications.Show(text, LeftColor, primaryKingdom: faction.ParentKingdom, primaryClan: clan);
        }

        public static void ShowFactionDisbanded(FactionObject faction, string reason)
        {
            if (faction == null) return;

            TextObject text = new TextObject("{=BC_Notification_Dissolved}The {FACTION_NAME} of {KINGDOM_NAME} has formally dissolved since {REASON}.");
            text.SetTextVariable("FACTION_NAME", GetFactionDisplayName(faction));
            text.SetTextVariable("KINGDOM_NAME", faction.ParentKingdom?.Name ?? new TextObject(""));
            text.SetTextVariable("REASON", reason);

            BellumCivileNotifications.Show(text, DisbandedColor, primaryKingdom: faction.ParentKingdom, primaryClan: faction.Leader);
        }

        public static TextObject GetFactionDisplayName(FactionObject faction)
        {
            return faction?.GetDisplayName() ?? new TextObject("");
        }

        public static void ShowClanDefectedToRebellion(Clan clan, Kingdom rebelKingdom, Kingdom parentKingdom)
        {
            if (clan.Leader == null) return;

            TextObject text = new TextObject("{=BC_Notification_Defected}With the ongoing momentum of {REBEL_KINGDOM}, {LEADER_NAME} of clan {CLAN_NAME} has switched sides and declared support for the rebellion!");
            text.SetTextVariable("REBEL_KINGDOM", rebelKingdom.Name);
            text.SetTextVariable("LEADER_NAME", clan.Leader.Name);
            text.SetTextVariable("CLAN_NAME", clan.Name);

            BellumCivileNotifications.Show(text, BellumNotificationColors.Rebellion, primaryKingdom: parentKingdom, secondaryKingdom: rebelKingdom, primaryClan: clan);
        }

        public static void ShowUltimatumToPlayer(FactionObject faction, System.Action onAccept, System.Action onRefuse)
        {
            TextObject demand;

            switch (faction.Type)
            {
                case FactionType.Independence: demand = new TextObject("{=BC_Demand_Independence}grant them independence from the realm"); break;
                case FactionType.Abdication: demand = new TextObject("{=BC_Demand_Abdication}step down from the throne"); break;
                case FactionType.InstallRuler:
                    demand = new TextObject("{=BC_Demand_InstallRuler}surrender the crown to {LEADER_NAME}");
                    demand.SetTextVariable("LEADER_NAME", faction.Leader?.Leader?.Name ?? new TextObject("Unknown"));
                    break;
                case FactionType.Royalists: demand = new TextObject("{=BC_Demand_Royalist}centralize power to the crown"); break;
                case FactionType.Glory: demand = new TextObject("{=BC_Demand_Militarist}prioritize military campaigns and conquest"); break;
                case FactionType.Nobility: demand = new TextObject("{=BC_Demand_Aristocrat}protect the rights of the nobility"); break;
                case FactionType.Liberty: demand = new TextObject("{=BC_Demand_Populist}empower the common folk"); break;
                default: demand = new TextObject(faction.Type.ToString()); break;
            }

            TextObject balanceReport = ConflictBalanceReportHelper.Build(
                ConflictBalanceReportHelper.BuildFactionSideName(faction),
                faction?.CalculateFactionPower() ?? 0f,
                ConflictBalanceReportHelper.CrownLoyalistsSideName,
                faction?.CalculateLoyalistPower() ?? 0f,
                playerUncommitted: false);

            TextObject title = new TextObject("{=BC_Notification_UltimatumTitle}Faction Ultimatum!");
            TextObject desc = new TextObject("{=BC_Notification_UltimatumDesc}The leader of the {FACTION_NAME}, {LEADER_NAME}, demands that you {DEMAND}. Refuse, and they will declare war!\n\n{BALANCE_REPORT}");
            desc.SetTextVariable("FACTION_NAME", GetFactionDisplayName(faction));
            desc.SetTextVariable("LEADER_NAME", faction.Leader?.Name ?? new TextObject("Unknown"));
            desc.SetTextVariable("DEMAND", demand);
            desc.SetTextVariable("BALANCE_REPORT", balanceReport);

            bool resolved = false;
            System.Action acceptOnce = () =>
            {
                if (resolved) return;
                resolved = true;
                onAccept?.Invoke();
            };
            System.Action refuseOnce = () =>
            {
                if (resolved) return;
                resolved = true;
                onRefuse?.Invoke();
            };

            InquiryData inquiry = new InquiryData(title.ToString(), desc.ToString(),
                true, true, new TextObject("{=BC_Notification_AcceptDemands}Accept Demands").ToString(), new TextObject("{=BC_Notification_RefuseFight}Refuse & Fight").ToString(), acceptOnce, refuseOnce);
            InformationManager.ShowInquiry(inquiry, true);
        }

        public static void ShowLoyalistRealmCollapsed(FactionObject faction, Kingdom parentKingdom, Kingdom rebelKingdom)
        {
            if (faction == null || parentKingdom == null || rebelKingdom == null)
                return;

            TextObject description = new TextObject("{=BC_CivilWar_LoyalistCollapse_Desc}With no towns or castles remaining under loyalist control, the government of {KINGDOM_NAME} has collapsed. The {FACTION_NAME} stands victorious, and its demands will now be enforced.");
            description.SetTextVariable("KINGDOM_NAME", parentKingdom.Name);
            description.SetTextVariable("FACTION_NAME", GetFactionDisplayName(faction));

            bool playerInvolved = Clan.PlayerClan?.Kingdom == parentKingdom
                || Clan.PlayerClan?.Kingdom == rebelKingdom;
            if (playerInvolved)
            {
                InformationManager.ShowInquiry(
                    new InquiryData(
                        new TextObject("{=BC_CivilWar_LoyalistCollapse_Title}The Loyalist Realm Collapses").ToString(),
                        description.ToString(),
                        true,
                        false,
                        GameTexts.FindText("str_done").ToString(),
                        string.Empty,
                        null,
                        null),
                    true);
                return;
            }

            BellumCivileNotifications.Show(
                description,
                BellumNotificationColors.Rebellion,
                primaryKingdom: parentKingdom,
                secondaryKingdom: rebelKingdom,
                primaryClan: faction.Leader,
                isMajorEvent: true);
        }

        public static void ShowPlayerSuccessionOpportunity(System.Action onUltimatum)
        {
            TextObject title = new TextObject("{=BC_Notification_SuccessionTitle}Succession Crisis!");
            TextObject desc = new TextObject("{=BC_Notification_SuccessionDesc}The old ruler is dead. Now is your chance to strike and seize the crown that rightfully belongs to you!");

            InquiryData inquiry = new InquiryData(title.ToString(), desc.ToString(),
                true, true, new TextObject("{=BC_Notification_IssueUltimatum}Issue Ultimatum").ToString(), new TextObject("{=BC_Notification_NotYet}Not yet").ToString(), onUltimatum, null);
            InformationManager.ShowInquiry(inquiry, true);
        }

        public static void ShowFeudalTitleUsurped(Clan usurperClan, Clan oldHolderClan, FeudalTitleRecord title)
        {
            if (usurperClan == null || title == null)
                return;

            TextObject text = oldHolderClan == usurperClan
                ? new TextObject("{=BC_Notification_TitleAssumed}After consolidating control of its lands, {USURPER_NAME} of the {USURPER_CLAN} has assumed the vacant {TITLE_NAME}, exercising the house's lawful rights.")
                : oldHolderClan != null
                ? new TextObject("{=BC_Notification_TitleUsurped}After consolidating control of its lands, {USURPER_NAME} of the {USURPER_CLAN} has usurped the {TITLE_NAME}, displacing the claim of the {OLD_CLAN}.")
                : new TextObject("{=BC_Notification_TitleUsurped_NoOldHolder}After consolidating control of its lands, {USURPER_NAME} of the {USURPER_CLAN} has asserted legal control over the {TITLE_NAME}.");

            text.SetTextVariable("USURPER_NAME", usurperClan.Leader?.Name ?? usurperClan.Name ?? new TextObject("?"));
            text.SetTextVariable("USURPER_CLAN", usurperClan.Name ?? new TextObject("?"));
            text.SetTextVariable("TITLE_NAME", FormatFeudalTitleText(title));
            if (oldHolderClan != null)
                text.SetTextVariable("OLD_CLAN", oldHolderClan.Name ?? new TextObject("?"));

            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.InheritanceWarning,
                primaryKingdom: usurperClan.Kingdom,
                secondaryKingdom: oldHolderClan?.Kingdom,
                primaryClan: usurperClan,
                secondaryClan: oldHolderClan);
        }

        public static void ShowFeudalTitleFormed(Clan founderClan, FeudalTitleRecord title, int childTitleCount)
        {
            if (founderClan == null || title == null)
                return;

            TextObject text = new TextObject("{=BC_Notification_TitleFormed}After consolidating their lands, {FOUNDER_NAME} of the {CLAN_NAME} has founded the {TITLE_NAME}, binding {CHILD_COUNT} lesser titles under their authority.");
            text.SetTextVariable("FOUNDER_NAME", founderClan.Leader?.Name ?? founderClan.Name ?? new TextObject("?"));
            text.SetTextVariable("CLAN_NAME", founderClan.Name ?? new TextObject("?"));
            text.SetTextVariable("TITLE_NAME", FormatFeudalTitleText(title));
            text.SetTextVariable("CHILD_COUNT", childTitleCount);

            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.InheritanceWarning,
                primaryKingdom: founderClan.Kingdom,
                primaryClan: founderClan);
        }

        public static void ShowFeudalTitleGranted(Clan grantorClan, Clan recipientClan, FeudalTitleRecord title, bool createsIndependentRealm, int relationGain)
        {
            if (grantorClan == null || recipientClan == null || title == null)
                return;

            TextObject text = createsIndependentRealm
                ? new TextObject("{=BC_Notification_TitleGranted_Independent}By sovereign grant, {GRANTOR_NAME} of the {GRANTOR_CLAN} has bestowed the {TITLE_NAME} upon {RECIPIENT_NAME} of the {RECIPIENT_CLAN}, raising them as an independent peer.")
                : new TextObject("{=BC_Notification_TitleGranted}By sovereign grant, {GRANTOR_NAME} of the {GRANTOR_CLAN} has bestowed the {TITLE_NAME} upon {RECIPIENT_NAME} of the {RECIPIENT_CLAN}.");

            text.SetTextVariable("GRANTOR_NAME", grantorClan.Leader?.Name ?? grantorClan.Name ?? new TextObject("?"));
            text.SetTextVariable("GRANTOR_CLAN", grantorClan.Name ?? new TextObject("?"));
            text.SetTextVariable("RECIPIENT_NAME", recipientClan.Leader?.Name ?? recipientClan.Name ?? new TextObject("?"));
            text.SetTextVariable("RECIPIENT_CLAN", recipientClan.Name ?? new TextObject("?"));
            text.SetTextVariable("TITLE_NAME", FormatFeudalTitleText(title));
            text.SetTextVariable("RELATION_GAIN", relationGain);

            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.InheritanceWarning,
                primaryKingdom: grantorClan.Kingdom,
                secondaryKingdom: recipientClan.Kingdom,
                primaryClan: grantorClan,
                secondaryClan: recipientClan);
        }

        public static void ShowSovereignTitleElevated(
            Clan sovereignClan,
            Kingdom formerKingdom,
            Kingdom independentKingdom,
            FeudalTitleRecord title,
            bool retainedHoldings)
        {
            if (sovereignClan == null || formerKingdom == null || independentKingdom == null || title == null)
                return;

            TextObject text = retainedHoldings
                ? new TextObject("{=BC_Notification_SovereignElevationDefiant}Having secured the {TITLE_NAME}, {RULER_NAME} has proclaimed {NEW_REALM} independent of {FORMER_REALM}. Refusing to relinquish their former holdings, the new sovereign now faces their old liege in war.")
                : new TextObject("{=BC_Notification_SovereignElevationPeaceful}Having secured the {TITLE_NAME}, {RULER_NAME} has peacefully separated {NEW_REALM} from {FORMER_REALM}. Lands outside the new title's legal hierarchy have been returned to the former realm.");
            text.SetTextVariable("TITLE_NAME", FormatFeudalTitleText(title));
            text.SetTextVariable("RULER_NAME", sovereignClan.Leader?.Name ?? sovereignClan.Name ?? new TextObject("?"));
            text.SetTextVariable("NEW_REALM", independentKingdom.Name ?? new TextObject("?"));
            text.SetTextVariable("FORMER_REALM", formerKingdom.Name ?? new TextObject("?"));

            BellumCivileNotifications.Show(
                text,
                retainedHoldings ? BellumNotificationColors.Rebellion : BellumNotificationColors.InheritanceWarning,
                primaryKingdom: independentKingdom,
                secondaryKingdom: formerKingdom,
                primaryClan: sovereignClan,
                secondaryClan: formerKingdom.RulingClan);
        }

        public static void ShowFeudalTitleRevoked(Clan revokerClan, Clan holderClan, FeudalTitleRecord title)
        {
            if (revokerClan == null || holderClan == null || title == null)
                return;

            TextObject text = new TextObject("{=BC_Notification_TitleRevoked}By right of claim and liege authority, {REVOKER_NAME} of the {REVOKER_CLAN} has revoked the {TITLE_NAME} from {HOLDER_NAME} of the {HOLDER_CLAN}. The dispossessed house accepts the judgment, though resentment lingers.");
            text.SetTextVariable("REVOKER_NAME", revokerClan.Leader?.Name ?? revokerClan.Name ?? new TextObject("?"));
            text.SetTextVariable("REVOKER_CLAN", revokerClan.Name ?? new TextObject("?"));
            text.SetTextVariable("HOLDER_NAME", holderClan.Leader?.Name ?? holderClan.Name ?? new TextObject("?"));
            text.SetTextVariable("HOLDER_CLAN", holderClan.Name ?? new TextObject("?"));
            text.SetTextVariable("TITLE_NAME", FormatFeudalTitleText(title));

            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.InheritanceWarning,
                primaryKingdom: revokerClan.Kingdom ?? holderClan.Kingdom,
                primaryClan: revokerClan,
                secondaryClan: holderClan);
        }

        public static void ShowFeudalTitleRevocationDefied(Clan revokerClan, Clan holderClan, FeudalTitleRecord title)
        {
            if (revokerClan == null || holderClan == null || title == null)
                return;

            TextObject text = new TextObject("{=BC_Notification_TitleRevocationDefied}{REVOKER_NAME} of the {REVOKER_CLAN} has demanded the {TITLE_NAME} by right of claim, but {HOLDER_NAME} of the {HOLDER_CLAN} has refused the writ. The dispute now threatens to erupt into private war.");
            text.SetTextVariable("REVOKER_NAME", revokerClan.Leader?.Name ?? revokerClan.Name ?? new TextObject("?"));
            text.SetTextVariable("REVOKER_CLAN", revokerClan.Name ?? new TextObject("?"));
            text.SetTextVariable("HOLDER_NAME", holderClan.Leader?.Name ?? holderClan.Name ?? new TextObject("?"));
            text.SetTextVariable("HOLDER_CLAN", holderClan.Name ?? new TextObject("?"));
            text.SetTextVariable("TITLE_NAME", FormatFeudalTitleText(title));

            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.Rebellion,
                primaryKingdom: revokerClan.Kingdom ?? holderClan.Kingdom,
                primaryClan: revokerClan,
                secondaryClan: holderClan);
        }

        public static void ShowFeudalDeJureDriftCompleted(
            FeudalTitleRecord title,
            FeudalTitleRecord oldParent,
            FeudalTitleRecord newParent,
            Kingdom formerKingdom,
            Kingdom receivingKingdom)
        {
            if (title == null || oldParent == null || newParent == null)
                return;

            TextObject text = new TextObject("{=BC_Notification_DeJureDriftCompleted}After generations under {TARGET_KINGDOM}, the {TITLE_NAME} is now recognized as a de jure part of the {NEW_PARENT_TITLE}, severing its former legal allegiance to the {OLD_PARENT_TITLE}.");
            text.SetTextVariable("TARGET_KINGDOM", receivingKingdom?.Name ?? new TextObject("{=BC_Hierarchy_Independent}Independent"));
            text.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatTitleName(title)));
            text.SetTextVariable("NEW_PARENT_TITLE", new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatTitleName(newParent)));
            text.SetTextVariable("OLD_PARENT_TITLE", new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatTitleName(oldParent)));

            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.InheritanceWarning,
                primaryKingdom: receivingKingdom,
                secondaryKingdom: formerKingdom);
        }

        public static void ShowFeudalClaimFabricationDiscovered(int tier, Clan claimantClan, Clan legalHolderClan, Hero fabricator, FeudalTitleRecord title)
        {
            if (claimantClan == null || fabricator == null || title == null)
                return;

            TextObject text;
            switch (tier)
            {
                case 1:
                    text = new TextObject("{=BC_Notification_ClaimFabricationDiscovered_1}Travelers speak of unusual activity in the region, as {HERO_NAME} of the {CLAN_NAME} seems to be seeking legal precedents regarding the historical lineage of the {TITLE_NAME}.");
                    break;
                case 2:
                    text = new TextObject("{=BC_Notification_ClaimFabricationDiscovered_2}A quiet unease settles over the {TITLE_NAME} as rumors circulate that {HERO_NAME} of the {CLAN_NAME} is actively digging up ancient, disputed charters to challenge the current ownership of those lands.");
                    break;
                default:
                    text = new TextObject("{=BC_Notification_ClaimFabricationDiscovered_3}Whispers out of court suggest that {HERO_NAME} of the {CLAN_NAME} has secretly commissioned scribes to forge false deeds illegally altering the boundaries of the {TITLE_NAME}.");
                    break;
            }

            SetFeudalClaimFabricationVariables(text, claimantClan, fabricator, title);
            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.InheritanceWarning,
                primaryKingdom: claimantClan.Kingdom,
                secondaryKingdom: legalHolderClan?.Kingdom,
                primaryClan: claimantClan,
                secondaryClan: legalHolderClan);
        }

        public static void ShowPlayerFeudalClaimFabricationStarted(Clan claimantClan, Hero fabricator, FeudalTitleRecord title)
        {
            if (claimantClan == null || fabricator == null || title == null)
                return;

            TextObject text = new TextObject("{=BC_Notification_PlayerClaimFabricationStarted}Your agents have begun quietly gathering precedents and disputed charters concerning the {TITLE_NAME}.");
            text.SetTextVariable("TITLE_NAME", FormatFeudalTitleText(title));
            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.InheritanceWarning,
                primaryKingdom: claimantClan.Kingdom,
                primaryClan: claimantClan,
                isPersonal: true);
        }

        public static void ShowFeudalClaimFabricationCompleted(Clan claimantClan, Clan legalHolderClan, Hero fabricator, FeudalTitleRecord title)
        {
            if (claimantClan == null || fabricator == null || title == null)
                return;

            TextObject text;
            switch (MBRandom.RandomInt(3))
            {
                case 0:
                    text = new TextObject("{=BC_Notification_ClaimFabricationCompleted_1}Citing forgotten grants from generations past, {HERO_NAME} of the {CLAN_NAME} has formally proclaimed {POSSESSIVE} disputed but legally binding claim to the {TITLE_NAME}.");
                    break;
                case 1:
                    text = new TextObject("{=BC_Notification_ClaimFabricationCompleted_2}The courts have gone quiet as {HERO_NAME} of the {CLAN_NAME} has presented ancient letters patent, boldly announcing {POSSESSIVE} rightful claim to the {TITLE_NAME}.");
                    break;
                default:
                    text = new TextObject("{=BC_Notification_ClaimFabricationCompleted_3}With a collection of documents allegedly citing one of {POSSESSIVE} ancestors, {HERO_NAME} of the {CLAN_NAME} has laid down formal claim to the {TITLE_NAME}.");
                    break;
            }

            SetFeudalClaimFabricationVariables(text, claimantClan, fabricator, title);
            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.InheritanceWarning,
                primaryKingdom: claimantClan.Kingdom,
                secondaryKingdom: legalHolderClan?.Kingdom,
                primaryClan: claimantClan,
                secondaryClan: legalHolderClan);
        }

        public static void ShowClaimFeudStarted(Clan claimantClan, Clan holderClan, FeudalTitleRecord title)
        {
            if (claimantClan == null || holderClan == null || title == null)
                return;

            TextObject text = new TextObject("{=BC_Notification_ClaimFeudStarted}{CLAIMANT_NAME} of the {CLAIMANT_CLAN} has begun to press {HOLDER_NAME} of the {HOLDER_CLAN} over {POSSESSIVE} claim to the {TITLE_NAME}, starting a bitter feud between their houses.");
            SetClaimFeudBasicVariables(text, claimantClan, holderClan, title);
            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.InheritanceWarning,
                primaryKingdom: claimantClan.Kingdom ?? holderClan.Kingdom,
                primaryClan: claimantClan,
                secondaryClan: holderClan);
        }

        public static void ShowClaimFeudTensionsMount(Clan claimantClan, Clan holderClan, FeudalTitleRecord title)
        {
            if (claimantClan == null || holderClan == null || title == null)
                return;

            TextObject text = new TextObject("{=BC_Notification_ClaimFeudTensions}Tensions mount over the {TITLE_NAME} as retainers of the {CLAIMANT_CLAN} have been found harassing the vassals of the {HOLDER_CLAN}.");
            SetClaimFeudBasicVariables(text, claimantClan, holderClan, title);
            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.InheritanceWarning,
                primaryKingdom: claimantClan.Kingdom ?? holderClan.Kingdom,
                primaryClan: claimantClan,
                secondaryClan: holderClan);
        }

        public static void ShowClaimFeudCallToArms(Clan claimantClan, Clan holderClan, FeudalTitleRecord title)
        {
            if (claimantClan == null || holderClan == null || title == null)
                return;

            TextObject text = new TextObject("{=BC_Notification_ClaimFeudCallToArms}The feud over the {TITLE_NAME} threatens to escalate into violence as friends and allies start to declare their support for either the {CLAIMANT_CLAN} or the {HOLDER_CLAN}.");
            SetClaimFeudBasicVariables(text, claimantClan, holderClan, title);
            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.InheritanceWarning,
                primaryKingdom: claimantClan.Kingdom ?? holderClan.Kingdom,
                primaryClan: claimantClan,
                secondaryClan: holderClan);
        }

        public static void ShowClaimFeudDropped(Clan claimantClan, Clan holderClan, FeudalTitleRecord title)
        {
            if (claimantClan == null || holderClan == null || title == null)
                return;

            TextObject text = new TextObject("{=BC_Notification_ClaimFeudDropped}The feud over the {TITLE_NAME} seems to have finally ceased as {CLAIMANT_NAME} of the {CLAIMANT_CLAN} decided to drop {POSSESSIVE} case, fearing to be outmatched by {HOLDER_NAME} of the {HOLDER_CLAN}.");
            SetClaimFeudBasicVariables(text, claimantClan, holderClan, title);
            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.InheritanceWarning,
                primaryKingdom: claimantClan.Kingdom ?? holderClan.Kingdom,
                primaryClan: claimantClan,
                secondaryClan: holderClan);
        }

        public static void ShowClaimFeudRuling(
            Kingdom kingdom,
            Hero ruler,
            Clan claimantClan,
            Clan holderClan,
            FeudalTitleRecord title,
            ClaimFeudJudgment judgment,
            ClaimFeudResponse claimantResponse,
            ClaimFeudResponse holderResponse)
        {
            if (kingdom == null || ruler == null || claimantClan == null || holderClan == null || title == null)
                return;

            TextObject text = new TextObject("{=BC_Notification_ClaimFeudRuling}{CLAIMANT_NAME} of the {CLAIMANT_CLAN} has petitioned before {RULER_NAME} to pass judgment on {POSSESSIVE} claim over the {TITLE_NAME}, held {LEGAL_STATUS} by {HOLDER_NAME} of the {HOLDER_CLAN}. After a long session at court, the {RULER_TITLE} has {RULING_TEXT}. {RESPONSE_TEXT}");
            text.SetTextVariable("CLAIMANT_TITLE", GetHeroCourtTitle(claimantClan.Leader));
            text.SetTextVariable("CLAIMANT_NAME", claimantClan.Leader?.Name ?? claimantClan.Name ?? new TextObject("?"));
            text.SetTextVariable("CLAIMANT_CLAN", claimantClan.Name ?? new TextObject("?"));
            text.SetTextVariable("HOLDER_TITLE", GetHeroCourtTitle(holderClan.Leader));
            text.SetTextVariable("HOLDER_NAME", holderClan.Leader?.Name ?? holderClan.Name ?? new TextObject("?"));
            text.SetTextVariable("HOLDER_CLAN", holderClan.Name ?? new TextObject("?"));
            text.SetTextVariable("RULER_TITLE", GetHeroCourtTitle(ruler));
            text.SetTextVariable("RULER_NAME", ruler.Name ?? new TextObject("?"));
            text.SetTextVariable("POSSESSIVE", claimantClan.Leader?.IsFemale == true ? new TextObject("{=BC_Pronoun_Her}her") : new TextObject("{=BC_Pronoun_His}his"));
            text.SetTextVariable("TITLE_NAME", FormatFeudalTitleText(title));
            text.SetTextVariable("LEGAL_STATUS", GetClaimFeudLegalStatus(title, holderClan));
            text.SetTextVariable("RULING_TEXT", GetClaimFeudRulingText(judgment, claimantClan, holderClan));
            text.SetTextVariable("RESPONSE_TEXT", GetClaimFeudResponseText(judgment, claimantClan, holderClan, claimantResponse, holderResponse));

            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.InheritanceWarning,
                primaryKingdom: kingdom,
                primaryClan: claimantClan,
                secondaryClan: holderClan);
        }

        public static void ShowClaimFeudWarStarted(Kingdom kingdom, Clan claimantClan, Clan holderClan, FeudalTitleRecord title)
        {
            if (claimantClan == null || holderClan == null || title == null)
                return;

            TextObject text = new TextObject("{=BC_Notification_ClaimFeudWarStarted}The feud over the {TITLE_NAME} has broken into open violence. The {CLAIMANT_CLAN} and the {HOLDER_CLAN} have raised their banners against one another, and the realm watches uneasily as blood answers law.");
            SetClaimFeudBasicVariables(text, claimantClan, holderClan, title);
            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.Rebellion,
                primaryKingdom: kingdom ?? claimantClan.Kingdom ?? holderClan.Kingdom,
                primaryClan: claimantClan,
                secondaryClan: holderClan,
                isMajorEvent: true);
        }

        public static void ShowClaimFeudWarResolved(Kingdom kingdom, Clan claimantClan, Clan holderClan, FeudalTitleRecord title, ClaimFeudWarOutcome outcome, bool claimantDefaulted = false)
        {
            if (claimantClan == null || holderClan == null || title == null)
                return;

            TextObject text;
            switch (outcome)
            {
                case ClaimFeudWarOutcome.ClaimantVictory:
                    text = new TextObject("{=BC_Notification_ClaimFeudWarClaimantVictory}The {CLAIMANT_CLAN} has prevailed in the feud over the {TITLE_NAME}. By feat of arms, {CLAIMANT_NAME}'s claim is now recognized, though the defeated {HOLDER_CLAN} will not soon forget the loss.");
                    break;
                case ClaimFeudWarOutcome.HolderVictory:
                    text = claimantDefaulted
                        ? new TextObject("{=BC_Notification_ClaimFeudWarClaimantFailure}With {CLAIMANT_NAME} of the {CLAIMANT_CLAN} unable to assert {POSSESSIVE} claim to the {TITLE_NAME}, retainers and supporters begin deserting {POSSESSIVE} cause. The feud collapses into a humiliating defeat, and the {HOLDER_CLAN} stands vindicated.")
                        : new TextObject("{=BC_Notification_ClaimFeudWarHolderVictory}The {HOLDER_CLAN} has broken the challenge of the {CLAIMANT_CLAN} over the {TITLE_NAME}. {HOLDER_NAME}'s right stands, and the claimant house retreats in bitterness.");
                    break;
                case ClaimFeudWarOutcome.WhitePeace:
                default:
                    text = new TextObject("{=BC_Notification_ClaimFeudWarWhitePeace}The feud between the {CLAIMANT_CLAN} and the {HOLDER_CLAN} over the {TITLE_NAME} has been forced into an uneasy peace. No right has been settled, and both houses withdraw under watchful eyes.");
                    break;
            }

            SetClaimFeudBasicVariables(text, claimantClan, holderClan, title);
            BellumCivileNotifications.Show(
                text,
                outcome == ClaimFeudWarOutcome.WhitePeace ? BellumNotificationColors.Success : BellumNotificationColors.InheritanceWarning,
                primaryKingdom: kingdom ?? claimantClan.Kingdom ?? holderClan.Kingdom,
                primaryClan: claimantClan,
                secondaryClan: holderClan,
                isMajorEvent: true);
        }

        private static void SetClaimFeudBasicVariables(TextObject text, Clan claimantClan, Clan holderClan, FeudalTitleRecord title)
        {
            text.SetTextVariable("CLAIMANT_TITLE", GetHeroCourtTitle(claimantClan?.Leader));
            text.SetTextVariable("CLAIMANT_NAME", claimantClan?.Leader?.Name ?? claimantClan?.Name ?? new TextObject("?"));
            text.SetTextVariable("CLAIMANT_CLAN", claimantClan?.Name ?? new TextObject("?"));
            text.SetTextVariable("HOLDER_TITLE", GetHeroCourtTitle(holderClan?.Leader));
            text.SetTextVariable("HOLDER_NAME", holderClan?.Leader?.Name ?? holderClan?.Name ?? new TextObject("?"));
            text.SetTextVariable("HOLDER_CLAN", holderClan?.Name ?? new TextObject("?"));
            text.SetTextVariable("POSSESSIVE", claimantClan?.Leader?.IsFemale == true ? new TextObject("{=BC_Pronoun_Her}her") : new TextObject("{=BC_Pronoun_His}his"));
            text.SetTextVariable("TITLE_NAME", FormatFeudalTitleText(title));
        }

        private static void SetFeudalClaimFabricationVariables(TextObject text, Clan claimantClan, Hero fabricator, FeudalTitleRecord title)
        {
            text.SetTextVariable("HERO_NAME", fabricator.Name ?? new TextObject("?"));
            text.SetTextVariable("POSSESSIVE", fabricator.IsFemale ? new TextObject("{=BC_Pronoun_Her}her") : new TextObject("{=BC_Pronoun_His}his"));
            text.SetTextVariable("CLAN_NAME", claimantClan.Name ?? new TextObject("?"));
            text.SetTextVariable("TITLE_NAME", FormatFeudalTitleText(title));
        }

        private static TextObject FormatFeudalTitleText(FeudalTitleRecord title)
        {
            string displayName = FeudalTitleDisplayHelper.FormatTitleName(title);
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = title?.TitleId ?? "?";

            return new TextObject("{=!}" + displayName);
        }

        private static TextObject GetHeroCourtTitle(Hero hero)
        {
            if (hero != null && FeudalTitleDisplayHelper.TryGetHighestDisplayTitle(hero, out string displayTitle) && !string.IsNullOrWhiteSpace(displayTitle))
                return new TextObject("{=!}" + displayTitle);

            return new TextObject("{=BC_TitleDisplay_Lord}Lord");
        }

        private static TextObject GetClaimFeudLegalStatus(FeudalTitleRecord title, Clan holderClan)
        {
            if (title == null || holderClan == null)
                return new TextObject("{=BC_ClaimFeud_Status_DeFacto}de facto");

            bool deJure = title.DeJureHolderClanId == holderClan.StringId;
            bool deFacto = title.DeFactoHolderClanId == holderClan.StringId;
            if (deJure && deFacto)
                return new TextObject("{=BC_ClaimFeud_Status_Lawfully}lawfully");
            if (deJure)
                return new TextObject("{=BC_ClaimFeud_Status_DeJure}de jure");
            return new TextObject("{=BC_ClaimFeud_Status_DeFacto}de facto");
        }

        private static TextObject GetClaimFeudRulingText(ClaimFeudJudgment judgment, Clan claimantClan, Clan holderClan)
        {
            TextObject text;
            switch (judgment)
            {
                case ClaimFeudJudgment.UpholdClaimant:
                    text = new TextObject("{=BC_ClaimFeud_Ruling_Claimant}ruled in favor of {PARTY}'s claim");
                    text.SetTextVariable("PARTY", claimantClan?.Leader?.Name ?? claimantClan?.Name ?? new TextObject("?"));
                    return text;
                case ClaimFeudJudgment.UpholdHolder:
                    text = new TextObject("{=BC_ClaimFeud_Ruling_Holder}ruled in favor of {PARTY}'s claim");
                    text.SetTextVariable("PARTY", holderClan?.Leader?.Name ?? holderClan?.Name ?? new TextObject("?"));
                    return text;
                case ClaimFeudJudgment.Suppress:
                    return new TextObject("{=BC_ClaimFeud_Ruling_Suppress}decreed that both parties cease their feud");
                case ClaimFeudJudgment.Abstain:
                    return new TextObject("{=BC_ClaimFeud_Ruling_Abstain}refused to intervene in the matter");
                default:
                    return new TextObject("{=BC_ClaimFeud_Ruling_Abstain}refused to intervene in the matter");
            }
        }

        private static TextObject GetClaimFeudResponseText(
            ClaimFeudJudgment judgment,
            Clan claimantClan,
            Clan holderClan,
            ClaimFeudResponse claimantResponse,
            ClaimFeudResponse holderResponse)
        {
            bool claimantDefied = claimantResponse == ClaimFeudResponse.Defy;
            bool holderDefied = holderResponse == ClaimFeudResponse.Defy;
            if (!claimantDefied && !holderDefied)
            {
                return judgment == ClaimFeudJudgment.Suppress
                    ? new TextObject("{=BC_ClaimFeud_Response_Suppressed}Reports say both parties accepted the decree begrudgingly, and the matter is quiet for now.")
                    : new TextObject("{=BC_ClaimFeud_Response_Accepted}Reports say both parties accepted the ruling begrudgingly, and the matter is settled.");
            }

            if (claimantDefied && holderDefied)
                return new TextObject("{=BC_ClaimFeud_Response_BothDefy}Reports say both parties stormed out of court after delivering a formal Letter of Deffiance, declaring private war to solve their dispute.");

            Clan defiantClan = claimantDefied ? claimantClan : holderClan;
            TextObject text = new TextObject("{=BC_ClaimFeud_Response_Defied}Reports say {PARTY} stormed out of court and delivered a formal Letter of Defiance, declaring private war to solve the dispute.");
            text.SetTextVariable("PARTY", defiantClan?.Leader?.Name ?? defiantClan?.Name ?? new TextObject("?"));
            return text;
        }

        public static void ShowFiefRevocationProposed(FactionObject faction, Settlement settlement, Kingdom kingdom, Clan claimant)
        {
            TextObject themeLine;
            switch (faction.Type)
            {
                case FactionType.Liberty:
                    themeLine = new TextObject("{=BC_Revoke_Populist}arguing that unlawful hoarding must give way to a recognized legal claim");
                    break;
                case FactionType.Nobility:
                    themeLine = new TextObject("{=BC_Revoke_Aristocrat}arguing that noble law demands the title be restored to its rightful claimant");
                    break;
                case FactionType.Glory:
                    themeLine = new TextObject("{=BC_Revoke_Militarist}arguing that disputed strongholds should not remain in uncertain hands");
                    break;
                case FactionType.Royalists:
                    themeLine = new TextObject("{=BC_Revoke_Royalist}arguing that order beneath the crown requires clear and lawful tenure");
                    break;
                default:
                    themeLine = new TextObject("{=BC_Revoke_Default}calling for the title dispute to be judged by the council");
                    break;
            }
            themeLine.SetTextVariable("SETTLEMENT_NAME", settlement.Name);

            TextObject msg = new TextObject("{=BC_Notification_FiefRevocation}The {FACTION_NAME} of {KINGDOM_NAME} have moved to press {CLAIMANT_CLAN}'s legal claim to {SETTLEMENT_NAME}, {THEME_LINE}.");
            msg.SetTextVariable("FACTION_NAME", faction.GetDisplayName());
            msg.SetTextVariable("KINGDOM_NAME", kingdom?.Name ?? new TextObject(""));
            msg.SetTextVariable("CLAIMANT_CLAN", claimant?.Name ?? faction?.Leader?.Name ?? new TextObject("?"));
            msg.SetTextVariable("SETTLEMENT_NAME", settlement.Name);
            msg.SetTextVariable("THEME_LINE", themeLine);

            BellumCivileNotifications.Show(msg, BellumNotificationColors.Land, primaryKingdom: kingdom, primaryClan: claimant ?? faction?.Leader, secondaryClan: settlement?.OwnerClan);
        }

        public static void ShowProxyWarDiscovered(Kingdom actingKingdom, Kingdom targetKingdom, Hero actingRuler, Hero targetRuler, int actionType)
        {
            TextObject plotDescription;

            switch (actionType)
            {
                case 0: plotDescription = new TextObject("{=BC_Proxy_Desc_0}fund highwaymen targeting royal domains"); break;
                case 1: plotDescription = new TextObject("{=BC_Proxy_Desc_1}smuggle weapons and incite a peasant revolt"); break;
                case 2: plotDescription = new TextObject("{=BC_Proxy_Desc_2}spread vicious rumors damaging royal influence"); break;
                case 3: plotDescription = new TextObject("{=BC_Proxy_Desc_3}run a shadow campaign and radicalize lords in court"); break;
                case 4: plotDescription = new TextObject("{=BC_Proxy_Desc_4}bribe lords of the realm into committing treason"); break;
                case 5: plotDescription = new TextObject("{=BC_Proxy_Desc_5}send covert military advisors in aid of the rebels"); break;
                case 6: plotDescription = new TextObject("{=BC_Proxy_Desc_6}smuggle chests of silver to fund the rebellion"); break;
                case 7: plotDescription = new TextObject("{=BC_Proxy_Desc_7}bribe a mercenary into breaking their contract"); break;
                case 8: plotDescription = new TextObject("{=BC_Proxy_Desc_8}smuggle foreign mercenaries into the rebel's ranks"); break;
                case 9: plotDescription = new TextObject("{=BC_Proxy_Desc_9}bribe lords into defecting to the rebels"); break;
                default: plotDescription = new TextObject("{=BC_Proxy_Desc_Default}conspire to destabilize the realm"); break;
            }

            TextObject text = new TextObject("{=BC_Notification_ProxyDiscovered}{TARGET_RULER} has unearthed a conspiracy within {TARGET_KINGDOM}. Under interrogation, the captured agent confessed to acting on {ACTING_RULER}'s orders to {PLOT_DESCRIPTION}.");
            text.SetTextVariable("ACTING_RULER", actingRuler.Name);
            text.SetTextVariable("TARGET_KINGDOM", targetKingdom.Name);
            text.SetTextVariable("TARGET_RULER", targetRuler.Name);
            text.SetTextVariable("PLOT_DESCRIPTION", plotDescription);

            BellumCivileNotifications.Show(text, BellumNotificationColors.CovertAction, primaryKingdom: actingKingdom, secondaryKingdom: targetKingdom, primaryClan: actingRuler?.Clan, secondaryClan: targetRuler?.Clan);
        }

        public static void ShowProxyWarUndetectedImpact(
            int actionType,
            Kingdom targetKingdom,
            Kingdom victimKingdom,
            Settlement affectedSettlement,
            FactionObject affectedFaction,
            Clan affectedClan)
        {
            TextObject text;

            switch (actionType)
            {
                case 0:
                    text = new TextObject("{=BC_Notification_ProxyUndetected_0}Unusually well-organized bands of highwaymen have appeared along the roads of {REALM_NAME}. Their equipment suggests outside support, but no patron has been identified.");
                    text.SetTextVariable("REALM_NAME", targetKingdom?.Name ?? new TextObject("?"));
                    break;
                case 1:
                    text = new TextObject("{=BC_Notification_ProxyUndetected_1}Unrest has erupted in {SETTLEMENT_NAME}. Paid agitators vanished into the crowds before they could be questioned, leaving no proof of who sent them.");
                    text.SetTextVariable("SETTLEMENT_NAME", affectedSettlement?.Name ?? targetKingdom?.Name ?? new TextObject("?"));
                    break;
                case 2:
                    text = new TextObject("{=BC_Notification_ProxyUndetected_2}A damaging scandal has seized {RULER_NAME}'s court. Letters and witnesses appeared with suspicious precision, yet their source remains unknown.");
                    text.SetTextVariable("RULER_NAME", victimKingdom?.RulingClan?.Leader?.Name ?? affectedClan?.Leader?.Name ?? new TextObject("?"));
                    break;
                case 3:
                    text = new TextObject("{=BC_Notification_ProxyUndetected_3}Vicious accusations have inflamed the {FACTION_NAME}. The rumors appear carefully coordinated, but investigators cannot trace their origin.");
                    text.SetTextVariable("FACTION_NAME", GetFactionDisplayName(affectedFaction));
                    break;
                case 4:
                    text = new TextObject("{=BC_Notification_ProxyUndetected_4}Seditious coin and correspondence are circulating among the lords of {REALM_NAME}. No sponsor has been uncovered.");
                    text.SetTextVariable("REALM_NAME", targetKingdom?.Name ?? new TextObject("?"));
                    break;
                case 5:
                    text = new TextObject("{=BC_Notification_ProxyUndetected_5}The rebel armies now maneuver with unfamiliar discipline. Foreign advisers are rumored to be among them, but no patron can be identified.");
                    break;
                case 6:
                    text = new TextObject("{=BC_Notification_ProxyUndetected_6}Unmarked chests of silver have reached the rebel treasury. Their origin remains unknown.");
                    break;
                case 7:
                    text = new TextObject("{=BC_Notification_ProxyUndetected_7}Mercenary captains have abruptly abandoned the Crown for the rebels. Secret payments are suspected, though no patron has been named.");
                    break;
                case 8:
                    text = new TextObject("{=BC_Notification_ProxyUndetected_8}Seasoned foreign soldiers have appeared in the rebel ranks, bearing no colors that reveal who sent them.");
                    break;
                case 9:
                    text = new TextObject("{=BC_Notification_ProxyUndetected_9}The {CLAN_NAME} defected to the rebellion after secret negotiations. Any outside hand behind the bargain remains hidden.");
                    text.SetTextVariable("CLAN_NAME", affectedClan?.Name ?? new TextObject("?"));
                    break;
                default:
                    return;
            }

            BellumCivileNotifications.ShowPersonal(text, BellumNotificationColors.Warning);
        }

        public static void ShowCoalitionChoiceToPlayer(
            FactionObject triggeringFaction,
            FactionObject grandCoalition,
            FactionObject playerIdFaction,
            Kingdom kingdom)
        {
            FactionObject displayFaction = playerIdFaction ?? triggeringFaction;
            string displayFactionName = GetFactionDisplayName(displayFaction).ToString();
            string triggeringFactionName = GetFactionDisplayName(triggeringFaction).ToString();
            TextObject rulerName = kingdom.RulingClan?.Leader?.Name ?? new TextObject("?");

            string titleStr, descStr, joinStr, loyalStr;

            if (playerIdFaction != null && playerIdFaction != triggeringFaction)
            {
                TextObject t = new TextObject("{=BC_Coalition_SweptIn_Title}The {TRIGGERING_FACTION} Calls for War!");
                t.SetTextVariable("TRIGGERING_FACTION", triggeringFactionName);
                titleStr = t.ToString();

                TextObject d = new TextObject("{=BC_Coalition_SweptIn_Desc}The {TRIGGERING_FACTION} of {KINGDOM_NAME} have declared open rebellion against {RULER_NAME}, rallying all discontented lords to their cause. Your faction, the {PLAYER_FACTION}, has answered that call, your lords are already marching.\n\nWill you ride with them, or stand with the crown?");
                d.SetTextVariable("TRIGGERING_FACTION", triggeringFactionName);
                d.SetTextVariable("KINGDOM_NAME", kingdom.Name);
                d.SetTextVariable("RULER_NAME", rulerName);
                d.SetTextVariable("PLAYER_FACTION", displayFactionName);
                descStr = d.ToString();

                joinStr  = new TextObject("{=BC_Coalition_Join}March to War").ToString();
                loyalStr = new TextObject("{=BC_Coalition_Loyal}Stand with the Crown").ToString();
            }
            else
            {
                TextObject t = new TextObject("{=BC_Coalition_Title}The {FACTION_NAME} Has Risen!");
                t.SetTextVariable("FACTION_NAME", displayFactionName);
                titleStr = t.ToString();

                TextObject d = new TextObject("{=BC_Coalition_Desc}Your political faction, the {FACTION_NAME} of {KINGDOM_NAME}, has declared open rebellion against {RULER_NAME}. Your fellow lords are already marching to war.\n\nWill you march with them, or stand with the crown?");
                d.SetTextVariable("FACTION_NAME", displayFactionName);
                d.SetTextVariable("KINGDOM_NAME", kingdom.Name);
                d.SetTextVariable("RULER_NAME", rulerName);
                descStr = d.ToString();

                joinStr  = new TextObject("{=BC_Coalition_Join}March to War").ToString();
                loyalStr = new TextObject("{=BC_Coalition_Loyal}Stand with the Crown").ToString();
            }

            InquiryData inquiry = new InquiryData(
                titleStr, descStr,
                true, true,
                joinStr, loyalStr,
                () =>
                {
                    var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
                    if (factionManager == null) return;

                    FactionObject existing = factionManager.GetRebelFaction(Clan.PlayerClan);
                    if (existing != null && existing != grandCoalition)
                    {
                        if (existing.Leader == Clan.PlayerClan) factionManager.RemoveFaction(existing);
                        else existing.RemoveMember(Clan.PlayerClan);
                    }
                    grandCoalition.AddMember(Clan.PlayerClan);

                    Kingdom rebelKingdom = grandCoalition.Leader?.Kingdom;
                    if (rebelKingdom != null && rebelKingdom != kingdom)
                        ChangeKingdomAction.ApplyByJoinToKingdom(Clan.PlayerClan, rebelKingdom, showNotification: false);

                    if (Clan.PlayerClan.Leader != null)
                    {
                        Hero rebelLeader = grandCoalition.Leader?.Leader;
                        if (rebelLeader != null && rebelLeader.Clan != Clan.PlayerClan)
                            RelationMemoryService.ApplyChange(
                                Clan.PlayerClan.Leader, rebelLeader, C.CoalitionRebelLeaderBonus, true,
                                RelationMemorySources.HonoredCallToArms, 10f, RelationMemoryScope.House, displayFactionName);

                        foreach (Clan rebel in grandCoalition.Members.ToList())
                        {
                            if (rebel != Clan.PlayerClan && rebel.Leader != null)
                                RelationMemoryService.ApplyChange(
                                    Clan.PlayerClan.Leader, rebel.Leader, C.CoalitionRebelMemberBonus, false,
                                    RelationMemorySources.HonoredCallToArms, 10f, RelationMemoryScope.House, displayFactionName);
                        }
                    }

                    TextObject msg = BuildJoinedMsg(displayFactionName, rulerName.ToString());
                    BellumCivileNotifications.ShowPersonal(msg, BellumNotificationColors.Danger);
                },
                () =>
                {
                    playerIdFaction?.RemoveMember(Clan.PlayerClan);

                    if (Clan.PlayerClan.Leader != null)
                    {
                        foreach (Clan rebel in grandCoalition.Members.ToList())
                        {
                            if (rebel != Clan.PlayerClan && rebel.Leader != null)
                                RelationMemoryService.ApplyChange(
                                    Clan.PlayerClan.Leader, rebel.Leader, -C.CoalitionLoyalistRelPenalty, false,
                                    RelationMemorySources.RefusedCallToArms, 10f, RelationMemoryScope.House, displayFactionName);
                        }

                        if (kingdom.RulingClan?.Leader != null)
                            RelationMemoryService.ApplyChange(
                                Clan.PlayerClan.Leader, kingdom.RulingClan.Leader, C.CoalitionLoyalistKingBonus, true,
                                RelationMemorySources.HonoredCallToArms, 10f, RelationMemoryScope.House, displayFactionName);
                    }

                    TextObject msg = BuildLoyalMsg(displayFactionName);
                    BellumCivileNotifications.ShowPersonal(msg, BellumNotificationColors.Success);
                });

            InformationManager.ShowInquiry(inquiry, true);
        }

        private static TextObject BuildJoinedMsg(string factionName, string rulerName)
        {
            TextObject msg = new TextObject("{=BC_Coalition_JoinedMsg}You ride with the rebellion. Your banners join the {FACTION_NAME} as they march against {RULER_NAME}!");
            msg.SetTextVariable("FACTION_NAME", new TextObject(factionName));
            msg.SetTextVariable("RULER_NAME", new TextObject(rulerName));
            return msg;
        }

        private static TextObject BuildLoyalMsg(string factionName)
        {
            TextObject msg = new TextObject("{=BC_Coalition_LoyalMsg}You stand with the crown. The {FACTION_NAME} will not forget your betrayal, but the king owes you a debt.");
            msg.SetTextVariable("FACTION_NAME", new TextObject(factionName));
            return msg;
        }

        internal static void ShowCivilWarSolidarityChoiceToPlayer(FactionObject rebelFaction, CivilWarPlayerCallContext context)
        {
            Clan playerClan = Clan.PlayerClan;
            Kingdom parentKingdom = rebelFaction?.ParentKingdom;
            Kingdom rebelKingdom = rebelFaction?.GetRebelKingdom();
            Clan sponsorClan = context?.SponsorClan ?? rebelFaction?.Leader;
            Hero sponsor = sponsorClan?.Leader;
            Hero ruler = parentKingdom?.RulingClan?.Leader;
            if (playerClan?.Leader == null || parentKingdom == null || rebelKingdom == null
                || sponsor == null || playerClan.Kingdom != parentKingdom)
            {
                return;
            }

            TextObject balanceReport = ConflictBalanceReportHelper.Build(
                ConflictBalanceReportHelper.BuildFactionSideName(rebelFaction),
                rebelFaction.CalculateFactionPower(),
                ConflictBalanceReportHelper.CrownLoyalistsSideName,
                rebelFaction.CalculateLoyalistPower(),
                playerUncommitted: true);

            TextObject title = new TextObject("{=BC_CivilWar_PlayerCall_Title}{LIEGE_NAME} Calls You to War");
            title.SetTextVariable("LIEGE_NAME", sponsor.Name);

            TextObject description;
            if (context.Reason == CivilWarPlayerCallReason.DirectVassal)
            {
                TextObject liegeNoun = sponsor.IsFemale
                    ? new TextObject("{=BC_CivilWar_LiegeLady}liege lady")
                    : new TextObject("{=BC_CivilWar_LiegeLord}liege lord");
                description = new TextObject("{=BC_CivilWar_PlayerVassalCall_Desc}{LIEGE_NAME} of the {LIEGE_CLAN}, your {LIEGE_NOUN}, has raised their banners against {RULER_NAME}. Bound by fealty, you are summoned to follow them into open rebellion.\n\n{BALANCE_REPORT}\n\nWill you honor your oath to your liege, or stand with the crown?");
                description.SetTextVariable("LIEGE_NOUN", liegeNoun);
            }
            else if (context.Reason == CivilWarPlayerCallReason.CourtFactionMember)
            {
                description = new TextObject("{=BC_CivilWar_PlayerCourtCall_Desc}{LIEGE_NAME} of the {LIEGE_CLAN} has rallied the discontented court factions and raised their banners against {RULER_NAME}. Your political allies are already marching.\n\n{BALANCE_REPORT}\n\nWill you ride with them, or stand with the crown?");
            }
            else
            {
                description = new TextObject("{=BC_CivilWar_PlayerFactionCall_Desc}{LIEGE_NAME} of the {LIEGE_CLAN} has delivered the faction's ultimatum and raised their banners against {RULER_NAME}. Your fellow conspirators are already marching.\n\n{BALANCE_REPORT}\n\nWill you join the rebellion, or stand with the crown?");
            }

            description.SetTextVariable("LIEGE_NAME", sponsor.Name);
            description.SetTextVariable("LIEGE_CLAN", sponsorClan.Name);
            description.SetTextVariable("RULER_NAME", ruler?.Name ?? parentKingdom.Name);
            description.SetTextVariable("BALANCE_REPORT", balanceReport);

            TextObject joinText = context.Reason == CivilWarPlayerCallReason.DirectVassal
                ? new TextObject("{=BC_CivilWar_PlayerCall_HonorOath}Honor Your Oath")
                : new TextObject("{=BC_CivilWar_PlayerCall_Join}Join the Rebellion");

            InformationManager.ShowInquiry(
                new InquiryData(
                    title.ToString(),
                    description.ToString(),
                    true,
                    true,
                    joinText.ToString(),
                    new TextObject("{=BC_CivilWar_PlayerCall_Crown}Stand with the Crown").ToString(),
                    () =>
                    {
                        CivilWarSolidarityHelper.JoinPlayerRebellion(rebelFaction, context);
                        TextObject msg = new TextObject("{=BC_CivilWar_PlayerCall_JoinedMsg}You answer {LIEGE_NAME}'s summons. Your banners now fly with the rebellion against the crown.");
                        msg.SetTextVariable("LIEGE_NAME", sponsor.Name);
                        BellumCivileNotifications.ShowPersonal(msg, BellumNotificationColors.Rebellion);
                    },
                    () =>
                    {
                        CivilWarSolidarityHelper.KeepPlayerLoyal(rebelFaction, context);
                        TextObject msg = new TextObject("{=BC_CivilWar_PlayerCall_LoyalMsg}You refuse {LIEGE_NAME}'s summons and stand with the crown. Your choice will not soon be forgotten.");
                        msg.SetTextVariable("LIEGE_NAME", sponsor.Name);
                        BellumCivileNotifications.ShowPersonal(msg, BellumNotificationColors.Success);
                    }),
                true);
        }

        public static void ShowTreasonSolidarityChoiceToPlayer(FactionObject rebelFaction, Kingdom parentKingdom, Clan accusedClan)
        {
            if (rebelFaction == null || parentKingdom == null || accusedClan?.Leader == null)
                return;

            Clan playerClan = Clan.PlayerClan;
            if (playerClan?.Leader == null || playerClan.Kingdom != parentKingdom || playerClan == parentKingdom.RulingClan)
                return;

            if (playerClan.IsUnderMercenaryService)
                return;

            int relationWithAccused = playerClan.Leader.GetRelation(accusedClan.Leader);
            bool marriageAlliance = MarriageAllianceHelper.HasMarriageAlliance(playerClan, accusedClan);
            bool directVassal = IsDirectTitleVassalOf(Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>(), playerClan, accusedClan);
            if (!marriageAlliance && !directVassal && relationWithAccused < C.TreasonSolidarityFriendRelationThreshold)
                return;

            Kingdom rebelKingdom = rebelFaction.GetRebelKingdom();
            if (rebelKingdom == null || rebelKingdom == parentKingdom || rebelKingdom.IsEliminated)
                return;

            Hero ruler = parentKingdom.RulingClan?.Leader;
            TextObject balanceReport = ConflictBalanceReportHelper.Build(
                ConflictBalanceReportHelper.BuildLeaderSupportersSideName(accusedClan.Leader.Name),
                rebelFaction.CalculateFactionPower(),
                ConflictBalanceReportHelper.CrownLoyalistsSideName,
                rebelFaction.CalculateLoyalistPower(),
                playerUncommitted: true);
            TextObject title = new TextObject("{=BC_Treason_PlayerChoice_Title}{LEADER_NAME} Appeals to You");
            title.SetTextVariable("LEADER_NAME", accusedClan.Leader.Name);

            TextObject desc = new TextObject("{=BC_Treason_PlayerChoice_Desc}{LEADER_NAME} of the {CLAN_NAME} has refused the crown's indictment and raised their banners against {RULER_NAME}. Bound by personal ties, they ask you to stand with them against what they call an unjust accusation.\n\n{BALANCE_REPORT}\n\nWill you join their rebellion, or stand with the crown?");
            desc.SetTextVariable("LEADER_NAME", accusedClan.Leader.Name);
            desc.SetTextVariable("CLAN_NAME", accusedClan.Name);
            desc.SetTextVariable("RULER_NAME", ruler?.Name ?? parentKingdom.Name);
            desc.SetTextVariable("BALANCE_REPORT", balanceReport);

            TextObject joinText = new TextObject("{=BC_Treason_PlayerChoice_Join}Stand with {LEADER_NAME}");
            joinText.SetTextVariable("LEADER_NAME", accusedClan.Leader.Name);

            InquiryData inquiry = new InquiryData(
                title.ToString(),
                desc.ToString(),
                true,
                true,
                joinText.ToString(),
                new TextObject("{=BC_Treason_PlayerChoice_Loyal}Stand with the Crown").ToString(),
                () =>
                {
                    FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
                    if (factionManager == null)
                        return;

                    FactionObject existing = factionManager.GetRebelFaction(playerClan);
                    if (existing != null && existing != rebelFaction)
                    {
                        if (existing.Leader == playerClan)
                            factionManager.RemoveFaction(existing);
                        else
                            existing.RemoveMember(playerClan);
                    }

                    rebelFaction.AddMember(playerClan);
                    ChangeKingdomAction.ApplyByJoinToKingdom(playerClan, rebelKingdom, showNotification: false);
                    RelationMemoryService.ApplyChange(playerClan.Leader, accusedClan.Leader, C.CoalitionRebelLeaderBonus, true,
                        RelationMemorySources.HonoredCallToArms, 10f, RelationMemoryScope.House, rebelFaction.Name?.ToString());

                    TextObject msg = new TextObject("{=BC_Treason_PlayerChoice_JoinedMsg}You stand with {LEADER_NAME}. Your banners now fly with the rebels against the crown.");
                    msg.SetTextVariable("LEADER_NAME", accusedClan.Leader.Name);
                    BellumCivileNotifications.ShowPersonal(msg, BellumNotificationColors.Danger);
                },
                () =>
                {
                    if (ruler != null)
                        RelationMemoryService.ApplyChange(playerClan.Leader, ruler, C.CoalitionLoyalistKingBonus, true,
                            RelationMemorySources.HonoredCallToArms, 10f, RelationMemoryScope.House, rebelFaction.Name?.ToString());

                    RelationMemoryService.ApplyChange(playerClan.Leader, accusedClan.Leader, -C.CoalitionLoyalistRelPenalty, true,
                        RelationMemorySources.RefusedCallToArms, 10f, RelationMemoryScope.House, rebelFaction.Name?.ToString());

                    TextObject msg = new TextObject("{=BC_Treason_PlayerChoice_LoyalMsg}You stand with the crown. {LEADER_NAME} will remember that you refused their appeal.");
                    rebelFaction.RecordLoyaltyDeclaration(playerClan);
                    msg.SetTextVariable("LEADER_NAME", accusedClan.Leader.Name);
                    BellumCivileNotifications.ShowPersonal(msg, BellumNotificationColors.Success);
                });

            InformationManager.ShowInquiry(inquiry, true);
        }

        private static bool IsDirectTitleVassalOf(FeudalTitleBehavior titleBehavior, Clan possibleVassal, Clan possibleLiege)
        {
            return CivilWarSolidarityHelper.IsImmediateVassalOf(titleBehavior, possibleVassal, possibleLiege);
        }
    }
}
