using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal sealed class RebellionIntentComponent
    {
        public RebellionIntentComponent(TextObject label, float value)
        {
            Label = label;
            Value = value;
        }

        public TextObject Label { get; }
        public float Value { get; }
    }

    internal sealed class RebellionIntentAssessment
    {
        public RebellionIntentAssessment(bool isValid, List<RebellionIntentComponent> components, float total)
        {
            IsValid = isValid;
            Components = components ?? new List<RebellionIntentComponent>();
            Total = total;
        }

        public bool IsValid { get; }
        public IReadOnlyList<RebellionIntentComponent> Components { get; }
        public float Total { get; }
    }

    internal static class RebellionIntentCalculator
    {
        public static RebellionIntentAssessment Assess(Clan clan)
        {
            List<RebellionIntentComponent> components = new List<RebellionIntentComponent>();
            if (clan?.Kingdom?.RulingClan?.Leader == null || clan.Leader == null)
                return new RebellionIntentAssessment(false, components, 0f);

            float total = 0f;
            Hero liege = clan.Kingdom.RulingClan.Leader;

            ProxyWarBehavior proxyWar = Campaign.Current?.GetCampaignBehavior<ProxyWarBehavior>();
            if (proxyWar?.HasActiveAgitation(clan) == true)
                Add(components, ref total, new TextObject("{=BC_Score_Label_ForeignAgitation}Foreign Agitation"), 30f);

            int rulerRelation = clan.Leader.GetRelation(liege);
            Add(
                components,
                ref total,
                rulerRelation >= 0
                    ? new TextObject("{=BC_Score_Label_Regards}Regards")
                    : new TextObject("{=BC_Score_Label_Grievances}Grievances"),
                -rulerRelation,
                includeZero: true);

            if (clan.Culture != clan.Kingdom.Culture)
                Add(components, ref total, new TextObject("{=BC_Score_Label_CultureFriction}Cultural Friction"), 20f);

            float fiefDesirePressure = Math.Min(
                C.RebelliousFiefDesireIntentCap,
                ClanFiefDesireHelper.CalculateRebelliousFiefDesirePressure(clan));
            Add(components, ref total, new TextObject("{=BC_Score_Label_FiefDesire}Desire for Land"), fiefDesirePressure);

            Add(
                components,
                ref total,
                new TextObject("{=BC_Score_Label_HoardingFiefs}Ruler Hoarding Fiefs"),
                ClanFiefDesireHelper.CalculateRulerHoardingPressure(clan));

            int calculating = clan.Leader.GetTraitLevel(DefaultTraits.Calculating);
            if (calculating <= -2)
                Add(components, ref total, new TextObject("{=BC_Score_Label_Hotheaded}Hotheaded"), 20f);
            else if (calculating == -1)
                Add(components, ref total, new TextObject("{=BC_Score_Label_Impulsive}Impulsive"), 10f);
            else if (calculating == 1)
                Add(components, ref total, new TextObject("{=BC_Score_Label_Calculating}Calculating"), -10f);
            else if (calculating >= 2)
                Add(components, ref total, new TextObject("{=BC_Score_Label_Cerebral}Cerebral"), -20f);

            int honor = clan.Leader.GetTraitLevel(DefaultTraits.Honor);
            if (honor <= -2)
                Add(components, ref total, new TextObject("{=BC_Score_Label_Deceitful}Deceitful"), 20f);
            else if (honor == -1)
                Add(components, ref total, new TextObject("{=BC_Score_Label_Devious}Devious"), 10f);
            else if (honor == 1)
                Add(components, ref total, new TextObject("{=BC_Score_Label_Honest}Honest"), -10f);
            else if (honor >= 2)
                Add(components, ref total, new TextObject("{=BC_Score_Label_Honorable}Honorable"), -20f);

            int valor = clan.Leader.GetTraitLevel(DefaultTraits.Valor);
            if (valor <= -2)
                Add(components, ref total, new TextObject("{=BC_Score_Label_Craven}Craven"), -20f);
            else if (valor == -1)
                Add(components, ref total, new TextObject("{=BC_Score_Label_Cautious}Cautious"), -10f);
            else if (valor == 1)
                Add(components, ref total, new TextObject("{=BC_Score_Label_Brave}Brave"), 10f);
            else if (valor >= 2)
                Add(components, ref total, new TextObject("{=BC_Score_Label_Fearless}Fearless"), 20f);

            if (MarriageAllianceHelper.HasMarriageAlliance(clan, clan.Kingdom.RulingClan))
            {
                Add(
                    components,
                    ref total,
                    new TextObject("{=BC_Score_Label_Marriage}Marriage Alliance"),
                    -C.RebelliousMarriageAllianceIntentReduction);
            }

            DynasticClaimBehavior dynasticClaims = Campaign.Current?.GetCampaignBehavior<DynasticClaimBehavior>();
            if (dynasticClaims?.HasActiveCadetDynasticTieToRulingClan(clan, clan.Kingdom) == true)
                Add(components, ref total, new TextObject("{=BC_Score_Label_DynasticHead}Heir to the throne"), -C.CadetBranchDynasticHeadRebellionReduction);

            ClaimFeudBehavior claimFeuds = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            float claimFeudPenalty = claimFeuds?.GetRebelliousIntentPenalty(clan) ?? 0f;
            if (claimFeudPenalty > 0f)
            {
                string label = claimFeuds.GetRebelliousIntentPenaltyLabel(clan);
                Add(
                    components,
                    ref total,
                    string.IsNullOrWhiteSpace(label)
                        ? new TextObject("{=BC_Score_Label_ClaimFeudDistracted}Distracted by Feud")
                        : new TextObject(label),
                    -claimFeudPenalty);
            }

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject ideology = factionManager?.GetIdeologicalFaction(clan);
            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            float councilAmbition = 0f;
            float councilRepresentation = 0f;
            float councilDismissal = 0f;
            council?.GetRebelliousIntentComponents(
                clan,
                out councilAmbition,
                out councilRepresentation,
                out councilDismissal);
            Add(components, ref total, new TextObject("{=BC_Score_Label_WantsCouncilSeat}Desires a Council Seat"), councilAmbition);
            Add(components, ref total, new TextObject("{=BC_Score_Label_FactionCouncilRepresentation}Court Faction Represented"), councilRepresentation);
            Add(components, ref total, new TextObject("{=BC_Score_Label_DismissedFromCouncil}Dismissed from Council"), councilDismissal);

            ControversyBehavior controversy = Campaign.Current?.GetCampaignBehavior<ControversyBehavior>();
            Add(
                components,
                ref total,
                new TextObject("{=BC_Score_Label_RulerControversy}Ruler Controversy"),
                controversy?.GetTotalControversy(clan.Kingdom) ?? 0f);

            int realmSize = clan.Kingdom.Settlements.Count(settlement => settlement.IsTown || settlement.IsCastle);
            Add(components, ref total, new TextObject("{=BC_Score_Label_RealmSize}Realm Size"), Math.Min(50f, realmSize));

            return new RebellionIntentAssessment(true, components, total);
        }

        private static void Add(
            ICollection<RebellionIntentComponent> components,
            ref float total,
            TextObject label,
            float value,
            bool includeZero = false)
        {
            if (!includeZero && Math.Abs(value) < 0.01f)
                return;

            components.Add(new RebellionIntentComponent(label, value));
            total += value;
        }
    }
}
