using BellumCivile.Behaviors;
using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;
using O = BellumCivile.BellumCivileOptions;

namespace BellumCivile
{
    internal enum ArmySummonsRefusalKind
    {
        None,
        NotLegalVassal,
        SouredRelations,
        RebelliousFaction
    }

    /// <summary>
    /// Keeps formal army command rules consistent between the army interface,
    /// AI army creation, and the vanilla face-to-face recruitment dialogue.
    /// </summary>
    internal static class ArmySummonsEligibilityHelper
    {
        internal static bool ShouldRefuseCallToArms(
            Clan callerClan,
            Clan calledClan,
            out ArmySummonsRefusalKind refusalKind,
            out TextObject reason,
            FactionManagerBehavior factionManager = null)
        {
            refusalKind = ArmySummonsRefusalKind.None;
            reason = null;

            if (callerClan == null || calledClan == null || callerClan == calledClan)
                return false;

            if (callerClan.Kingdom == null || callerClan.Kingdom != calledClan.Kingdom)
                return false;

            if (IsTemporaryWarCommandScope(callerClan, calledClan, factionManager))
                return false;

            bool callerHasRoyalCommand = HasRoyalArmyCommand(callerClan);
            if (O.EnableFeudalArmySummons && !callerHasRoyalCommand)
            {
                FeudalTitleBehavior titleBehavior = FeudalTitleBehavior.Instance
                    ?? Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
                if (titleBehavior != null && !titleBehavior.IsClanWithinDeFactoAuthority(calledClan, callerClan))
                {
                    refusalKind = ArmySummonsRefusalKind.NotLegalVassal;
                    reason = new TextObject("{=BC_Army_NotLegalVassal}You can only legally summon your vassals to arms.");
                    return true;
                }
            }

            // Hired minor clans answer to the realm rather than its court factions.
            // The feudal check above still prevents an ordinary peer from commanding them.
            if (calledClan.IsMinorFaction || calledClan.IsUnderMercenaryService)
                return false;

            if (O.EnableRebelliousArmyRefusal
                && callerClan.Leader != null
                && calledClan.Leader != null
                && calledClan.Leader.GetRelation(callerClan.Leader) <= O.ArmyRefusalMoodThreshold)
            {
                refusalKind = ArmySummonsRefusalKind.SouredRelations;
                reason = new TextObject("{=BC_Army_RefuseSouredRelations}Your vassal refuses any call to arms over your soured relations.");
                return true;
            }

            if (!callerHasRoyalCommand || !O.EnableRebelliousArmyRefusal)
                return false;

            factionManager = factionManager
                ?? Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject calledIdeology = factionManager?.GetIdeologicalFaction(calledClan);
            if (calledIdeology == null || calledIdeology.Mood > O.ArmyRefusalMoodThreshold)
                return false;

            refusalKind = ArmySummonsRefusalKind.RebelliousFaction;
            reason = new TextObject("{=BC_Army_RefuseRebelliousFaction}The {FACTION_NAME} are openly rebellious and refuse all calls to arms.");
            reason.SetTextVariable("FACTION_NAME", calledIdeology.GetDisplayName());
            return true;
        }

        internal static bool HasRoyalArmyCommand(Clan clan)
        {
            Kingdom kingdom = clan?.Kingdom;
            if (kingdom == null)
                return false;

            if (clan == kingdom.RulingClan)
                return true;

            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            return council?.GetOfficeHolder(kingdom, PrivyCouncilOffice.Marshal) == clan
                && PrivyCouncilBehavior.CanPerformCouncilDuties(clan);
        }

        internal static bool CanAnswerAsPersonalFriend(
            Hero caller,
            Hero called,
            ArmySummonsRefusalKind refusalKind)
        {
            return O.EnableFeudalArmySummons
                && refusalKind == ArmySummonsRefusalKind.NotLegalVassal
                && caller != null
                && called != null
                && called.GetRelation(caller) >= O.ArmyPersonalFriendRelationThreshold;
        }

        internal static bool ShouldRefuseArmyMenuCall(
            Hero caller,
            Hero called,
            out TextObject reason,
            FactionManagerBehavior factionManager = null)
        {
            bool refused = ShouldRefuseCallToArms(
                caller?.Clan,
                called?.Clan,
                out ArmySummonsRefusalKind refusalKind,
                out reason,
                factionManager);
            if (!refused)
                return false;

            if (caller == Hero.MainHero
                && CanAnswerAsPersonalFriend(caller, called, refusalKind))
            {
                reason = null;
                return false;
            }

            return true;
        }

        internal static bool IsTemporaryWarCommandScope(
            Clan callerClan,
            Clan calledClan,
            FactionManagerBehavior factionManager = null)
        {
            if (callerClan == null
                || calledClan == null
                || callerClan.Kingdom == null
                || callerClan.Kingdom != calledClan.Kingdom)
            {
                return false;
            }

            Kingdom kingdom = callerClan.Kingdom;
            if (kingdom.IsEliminated)
                return false;

            factionManager = factionManager
                ?? Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject rebelKingdomFaction = factionManager?.GetFactionByRebelKingdom(kingdom);
            if (rebelKingdomFaction != null && rebelKingdomFaction.IsCivilWarActive())
                return true;

            if (string.IsNullOrWhiteSpace(kingdom.StringId)
                || !kingdom.StringId.StartsWith("bc_feud_", StringComparison.Ordinal))
            {
                return false;
            }

            return Kingdom.All.Any(otherKingdom =>
                otherKingdom != null
                && otherKingdom != kingdom
                && kingdom.IsAtWarWith(otherKingdom));
        }
    }
}
