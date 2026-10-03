using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal enum CourtActivityEffect { Towns, Castles, Holdings, Villages, Peers, Notables, Armies, Cavalry, Veterans, Influence, Gold }

    internal sealed class CourtActivityDefinition
    {
        internal readonly string Id, Label;
        internal readonly FactionType Faction;
        internal readonly bool Positive;
        internal readonly CourtActivityEffect Effect;
        internal readonly int Amount, Security, Loyalty, Renown;
        internal CourtActivityDefinition(string id, FactionType faction, bool positive, string label,
            CourtActivityEffect effect, int amount = 0, int security = 0, int loyalty = 0, int renown = 0)
        { Id = id; Faction = faction; Positive = positive; Label = label; Effect = effect; Amount = amount; Security = security; Loyalty = loyalty; Renown = renown; }
        internal TextObject AgendaText => new TextObject("{=" + Id + "_Agenda}" + Label);
    }

    internal static class CourtActivityCatalog
    {
        internal static readonly IReadOnlyList<CourtActivityDefinition> All = new[]
        {
            new CourtActivityDefinition("BC_CourtCavalry", FactionType.Nobility, true, "Muster noble cavalry", CourtActivityEffect.Cavalry, C.AriEventCavalryGiftTroops),
            new CourtActivityDefinition("BC_CourtEstateInvestment", FactionType.Nobility, true, "Invest in local estates", CourtActivityEffect.Towns, C.AriEventMonopolyProsperityBonus),
            new CourtActivityDefinition("BC_CourtStonemasons", FactionType.Nobility, true, "Repair local castles", CourtActivityEffect.Castles, C.AriEventStonemasonProsperityBonus, C.AriEventStonemasonSecurityBonus),
            new CourtActivityDefinition("BC_CourtBanquet", FactionType.Nobility, true, "Host a noble banquet", CourtActivityEffect.Peers, C.AriEventBanquetRelationBonus),
            new CourtActivityDefinition("BC_CourtEndorsement", FactionType.Nobility, true, "Proclaim support for the Crown", CourtActivityEffect.Influence, C.RoyEventFealtyInfluence),
            new CourtActivityDefinition("BC_CourtExtraordinaryAid", FactionType.Nobility, true, "Raise extraordinary aid", CourtActivityEffect.Gold, C.RoyEventTributeGold),
            new CourtActivityDefinition("BC_CourtCensure", FactionType.Nobility, false, "Censure the Crown", CourtActivityEffect.Influence, -C.RoyEventMandateInfluencePenalty),
            new CourtActivityDefinition("BC_CourtInvestmentWithdrawn", FactionType.Nobility, false, "Withdraw estate investment", CourtActivityEffect.Towns, -C.AriEventHoardProsperityPenalty),
            new CourtActivityDefinition("BC_CourtLaborWithheld", FactionType.Nobility, false, "Withhold castle labor", CourtActivityEffect.Castles, -C.AriEventLaborProsperityPenalty, -C.AriEventLaborSecurityPenalty),
            new CourtActivityDefinition("BC_CourtObstruction", FactionType.Nobility, false, "Obstruct local administration", CourtActivityEffect.Holdings, -C.RoyEventGridlockProsperityPenalty, -C.RoyEventGridlockSecurityPenalty),
            new CourtActivityDefinition("BC_CourtSnub", FactionType.Nobility, false, "Organize a noble snub", CourtActivityEffect.Peers, -C.AriEventSnubRelationPenalty),
            new CourtActivityDefinition("BC_CourtArmyInspiration", FactionType.Glory, true, "Rally the faction's hosts", CourtActivityEffect.Armies, C.MilEventArmyCohesionBonus),
            new CourtActivityDefinition("BC_CourtVeterans", FactionType.Glory, true, "Muster veteran volunteers", CourtActivityEffect.Veterans, C.MilEventVeteranGiftTroops),
            new CourtActivityDefinition("BC_CourtMartialParade", FactionType.Glory, true, "Prepare a martial parade", CourtActivityEffect.Influence, C.MilEventParadeInfluence, renown: C.MilEventParadeRenown),
            new CourtActivityDefinition("BC_CourtGarrisonSupport", FactionType.Glory, true, "Support local garrisons", CourtActivityEffect.Castles, security: C.MilEventReinforceSecurity, loyalty: C.MilEventReinforceLoyalty),
            new CourtActivityDefinition("BC_CourtMartialGames", FactionType.Glory, true, "Hold martial games", CourtActivityEffect.Peers, C.MilEventBraveLordRelationBonus),
            new CourtActivityDefinition("BC_CourtArmyProtest", FactionType.Glory, false, "Organize an army protest", CourtActivityEffect.Armies, -C.MilEventArmyCohesionPenalty),
            new CourtActivityDefinition("BC_CourtNeglectedWatches", FactionType.Glory, false, "Withdraw support for local watches", CourtActivityEffect.Holdings, security: -C.MilEventDesertionSecurityPenalty, loyalty: -C.MilEventDesertionLoyaltyPenalty),
            new CourtActivityDefinition("BC_CourtMartialMockery", FactionType.Glory, false, "Challenge the ruler's martial reputation", CourtActivityEffect.Influence, -C.MilEventMockeryInfluencePenalty),
            new CourtActivityDefinition("BC_CourtAgriculturalAid", FactionType.Liberty, true, "Aid local villages", CourtActivityEffect.Villages, C.PopEventSubsidiesHearthBonus),
            new CourtActivityDefinition("BC_CourtCommunityPatronage", FactionType.Liberty, true, "Extend community patronage", CourtActivityEffect.Notables, C.PopEventFoodRelationBonus),
            new CourtActivityDefinition("BC_CourtFestival", FactionType.Liberty, true, "Prepare town festivities", CourtActivityEffect.Towns, security: C.PopEventFestivalSecurityBonus, loyalty: C.PopEventFestivalLoyaltyBonus),
            new CourtActivityDefinition("BC_CourtDebtRelief", FactionType.Liberty, true, "Arrange debt relief", CourtActivityEffect.Holdings, C.PopEventDebtProsperityBonus),
            new CourtActivityDefinition("BC_CourtTownWatches", FactionType.Liberty, true, "Organize local watches", CourtActivityEffect.Holdings, security: C.PopEventWatchesSecurityBonus, loyalty: C.PopEventWatchesLoyaltyBonus),
            new CourtActivityDefinition("BC_CourtRuralNoncooperation", FactionType.Liberty, false, "Encourage rural noncooperation", CourtActivityEffect.Villages, -C.PopEventSabotageHearthPenalty),
            new CourtActivityDefinition("BC_CourtNotableAgitation", FactionType.Liberty, false, "Rally discontented notables", CourtActivityEffect.Notables, -C.PopEventSlanderRelationPenalty),
            new CourtActivityDefinition("BC_CourtStrikes", FactionType.Liberty, false, "Organize work stoppages", CourtActivityEffect.Holdings, -C.PopEventStrikesProsperityPenalty, loyalty: -C.PopEventStrikesLoyaltyPenalty),
            new CourtActivityDefinition("BC_CourtRiots", FactionType.Liberty, false, "Agitate against town authorities", CourtActivityEffect.Towns, security: -C.PopEventRiotsSecurityPenalty, loyalty: -C.PopEventRiotsLoyaltyPenalty),
            new CourtActivityDefinition("BC_CourtLevyEvasion", FactionType.Liberty, false, "Encourage levy evasion", CourtActivityEffect.Holdings, security: -C.PopEventEvasionSecurityPenalty, loyalty: -C.PopEventEvasionLoyaltyPenalty),
            new CourtActivityDefinition("BC_CourtTaxBoycott", FactionType.Liberty, false, "Organize a tax boycott", CourtActivityEffect.Gold, -C.RoyEventBoycottGoldPenalty)
        };
        internal static CourtActivityDefinition Find(string id) => All.FirstOrDefault(d => d.Id == id);
    }
}
