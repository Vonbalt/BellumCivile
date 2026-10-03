using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile.Patches
{
    [HarmonyPatch(typeof(Hero), nameof(Hero.SetHeroEncyclopediaTextAndLinks))]
    internal static class HeroEncyclopediaDescriptionPatch
    {
        internal enum Allegiance { None, Realm, Loyalist, Rebellion, Feud, Temporary }

        public static void Postfix(Hero o, ref TextObject __result)
        {
            // Authored biographies and origin descriptions are not statements of current allegiance.
            if (o == null || !TextObject.IsNullOrEmpty(o.EncyclopediaText)
                || o.Clan == null || !o.IsAlive || o.IsDisabled || !o.IsLord
                || o.Clan.IsMinorFaction || o.Clan.IsRebelClan) return;
            try
            {
                Clan clan = o.Clan;
                Kingdom kingdom = clan.Kingdom;
                var manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
                var rebellion = manager?.GetFactionByRebelKingdom(kingdom);
                var feuds = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>();
                Kingdom feudParent = kingdom == null ? null : feuds?.GetParentKingdomForTemporaryRealm(kingdom);
                bool rebel = rebellion?.IsCivilWarActive() == true;
                bool temporary = kingdom != null && BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom);
                var allegiance = Classify(kingdom != null, rebel, feudParent != null, temporary,
                    kingdom != null && manager?.GetFactionsInKingdom(kingdom).Any(f => f.IsCivilWarActive()) == true);
                bool ruler = kingdom?.Leader == o;
                TextObject rank = HeroHelper.GetTitleInIndefiniteCase(o);
                if (FeudalTitleHeroNamePatch.ShouldApplyTitlePrefix(o)
                    && FeudalTitleDisplayHelper.TryGetDisplayStyle(o, out string style)
                    && !string.IsNullOrWhiteSpace(style)) rank = new TextObject("{=!}" + style);
                else if (temporary && ruler)
                    rank = new TextObject("{=BC_HeroBio_Noble}a noble");

                TextObject text = new TextObject("");
                o.SetPropertiesToTextObject(text, "LORD");
                text.SetTextVariable("TITLE", rank);
                text.SetTextVariable("CLAN", clan.Name);
                Kingdom realm = rebel ? rebellion.ParentKingdom : feudParent ?? kingdom;
                text.SetTextVariable("REALM", realm?.Name ?? new TextObject("{=BC_HeroBio_RealmFallback}the realm"));
                text.SetTextVariable("SIDE", rebel ? rebellion.GetDisplayName() : kingdom?.Name ?? clan.Name);
                text.SetTextVariable("REPUTATION", CharacterHelper.GetReputationDescription(o.CharacterObject));
                __result = BuildText(clan.Leader == o, ruler, allegiance, text.Attributes);
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Hero description styling failed for {o.StringId}: {ex.Message}");
            }
        }

        internal static Allegiance Classify(bool hasRealm, bool rebel, bool feud, bool temporary, bool loyalist)
        {
            if (!hasRealm) return Allegiance.None;
            if (rebel) return Allegiance.Rebellion;
            if (feud) return Allegiance.Feud;
            if (temporary) return Allegiance.Temporary;
            return loyalist ? Allegiance.Loyalist : Allegiance.Realm;
        }

        internal static TextObject BuildText(bool head, bool ruler, Allegiance allegiance, Dictionary<string, object> variables)
        {
            bool sovereign = ruler && (allegiance == Allegiance.Realm || allegiance == Allegiance.Loyalist);
            TextObject introduction = sovereign
                ? new TextObject("{=BC_HeroBio_Ruler}{LORD.NAME} is {TITLE} of {REALM} and head of the {CLAN}, a noble family.")
                : head ? new TextObject("{=BC_HeroBio_Head}{LORD.NAME} is {TITLE} and head of the {CLAN}, a noble family.")
                : new TextObject("{=BC_HeroBio_Member}{LORD.NAME} is a member of the {CLAN}, a noble family.");
            TextObject affiliation = TextObject.GetEmpty();
            switch (allegiance)
            {
                case Allegiance.Realm:
                    if (!sovereign) affiliation = new TextObject("{=BC_HeroBio_Allegiance}The house belongs to {REALM}.");
                    break;
                case Allegiance.Loyalist:
                    affiliation = new TextObject("{=BC_HeroBio_Loyalist}The house stands with the Crown of {REALM} in the ongoing civil war.");
                    break;
                case Allegiance.Rebellion:
                    affiliation = ruler
                        ? new TextObject("{=BC_HeroBio_RebelLeader}{?LORD.GENDER}She{?}He{\\?} leads {SIDE} in the civil war within {REALM}.")
                        : new TextObject("{=BC_HeroBio_RebelMember}The house has joined {SIDE} in the civil war within {REALM}.");
                    break;
                case Allegiance.Feud:
                    affiliation = ruler
                        ? new TextObject("{=BC_HeroBio_FeudLeader}{?LORD.GENDER}She{?}He{\\?} leads one side of a private feud within {REALM}.")
                        : new TextObject("{=BC_HeroBio_FeudMember}The house fights alongside {SIDE} in a private feud within {REALM}.");
                    break;
                case Allegiance.Temporary:
                    affiliation = new TextObject("{=BC_HeroBio_Temporary}The house currently marches under the banners of {SIDE}.");
                    break;
            }
            // Each nested fragment needs its own local context; parent variables are not inherited.
            return new TextObject("{=BC_HeroBio_Description}{INTRODUCTION} {AFFILIATION} {?LORD.GENDER}She{?}He{\\?} has the reputation of being {REPUTATION}.", new Dictionary<string, object>(variables))
                .SetTextVariable("INTRODUCTION", new TextObject(introduction.Value, new Dictionary<string, object>(variables)))
                .SetTextVariable("AFFILIATION", new TextObject(affiliation.Value, new Dictionary<string, object>(variables)));
        }
    }
}
