using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.UI.Map
{
    internal sealed class ClientStateMapWidgetItemVM : ViewModel
    {
        private BannerImageIdentifierVM _clientBanner;
        private string _libertyDesireText;
        private bool _isContent;
        private bool _isRestless;
        private bool _isDefiant;
        private int _roundedLibertyDesire = int.MinValue;
        private ClientLibertyAssessment _assessment;
        private readonly BasicTooltipViewModel _libertyHint;

        public string ClientKingdomId { get; }

        [DataSourceProperty]
        public BannerImageIdentifierVM ClientBanner
        {
            get => _clientBanner;
            private set => SetField(ref _clientBanner, value, nameof(ClientBanner));
        }

        [DataSourceProperty]
        public string LibertyDesireText
        {
            get => _libertyDesireText;
            private set => SetField(ref _libertyDesireText, value, nameof(LibertyDesireText));
        }

        [DataSourceProperty]
        public bool IsContent
        {
            get => _isContent;
            private set => SetField(ref _isContent, value, nameof(IsContent));
        }

        [DataSourceProperty]
        public bool IsRestless
        {
            get => _isRestless;
            private set => SetField(ref _isRestless, value, nameof(IsRestless));
        }

        [DataSourceProperty]
        public bool IsDefiant
        {
            get => _isDefiant;
            private set => SetField(ref _isDefiant, value, nameof(IsDefiant));
        }

        public ClientStateMapWidgetItemVM(ClientLibertyAssessment assessment)
        {
            ClientKingdomId = assessment?.ClientKingdom?.StringId ?? string.Empty;
            _libertyHint = new BasicTooltipViewModel(BuildTooltipProperties);
            Update(assessment);
        }

        public void ExecuteBeginLibertyHint()
        {
            _libertyHint?.ExecuteBeginHint();
        }

        public void ExecuteEndLibertyHint()
        {
            _libertyHint?.ExecuteEndHint();
        }

        public override void OnFinalize()
        {
            _libertyHint?.ExecuteEndHint();
            base.OnFinalize();
        }

        public void Update(ClientLibertyAssessment assessment)
        {
            _assessment = assessment;
            if (assessment?.ClientKingdom == null)
                return;

            if (ClientBanner == null)
                ClientBanner = new BannerImageIdentifierVM(assessment.ClientKingdom.Banner, true);

            int roundedDesire = (int)Math.Round(assessment.RealmLibertyDesire, MidpointRounding.AwayFromZero);
            if (roundedDesire == _roundedLibertyDesire)
                return;

            _roundedLibertyDesire = roundedDesire;
            LibertyDesireText = roundedDesire.ToString();
            IsContent = roundedDesire < 40;
            IsRestless = roundedDesire >= 40 && roundedDesire < C.ClientRealmLiberationDesireThreshold;
            IsDefiant = roundedDesire >= C.ClientRealmLiberationDesireThreshold;
        }

        private List<TooltipProperty> BuildTooltipProperties()
        {
            var clients = Campaign.Current?.GetCampaignBehavior<ClientKingdomBehavior>();
            return ClientLibertyTooltip.Build(clients != null
                ? clients.BuildLibertyAssessment(_assessment?.ClientKingdom) : _assessment);
        }
    }

    internal static class ClientLibertyTooltip
    {
        internal static List<TooltipProperty> Build(ClientLibertyAssessment assessment)
        {
            List<TooltipProperty> properties = new List<TooltipProperty>();
            if (assessment?.ClientKingdom == null)
                return properties;

            TextObject title = new TextObject("{=BC_MapClientState_Title}Client {KINGDOM_NAME}");
            title.SetTextVariable("KINGDOM_NAME", assessment.ClientKingdom.Name);
            properties.Add(new TooltipProperty(
                title.ToString(),
                string.Empty,
                0,
                false,
                TooltipProperty.TooltipPropertyFlags.Title));

            if (assessment.SuzerainKingdom != null)
                AddValueRow(properties, new TextObject("{=BC_ClientTooltip_Suzerain}Suzerain"), assessment.SuzerainKingdom.Name.ToString());
            ClientLiberationProposalAssessment proposer = ClientLiberationProposalAssessment.FindBest(assessment);
            AddParagraph(properties, GetStatus(assessment, proposer));

            AddSeparator(properties);
            AddValueRow(properties, new TextObject("{=BC_MapClientState_LibertyDesire}Liberty Desire"),
                Required(assessment.RealmLibertyDesire, C.ClientRealmLiberationDesireThreshold));

            foreach (KeyValuePair<string, float> reason in BuildRealmReasons(assessment))
                AddValueRow(properties, ResolveReasonLabel(reason.Key), FormatSigned(reason.Value));

            AddSeparator(properties);
            AddValueRow(properties, new TextObject("{=BC_ClientTooltip_PowerReadiness}Power readiness"),
                new TextObject("{=BC_ClientTooltip_ReadinessValue}{CURRENT}% / 100% required")
                    .SetTextVariable("CURRENT", assessment.LiberationReadiness.ToString("0.#")).ToString());
            AddValueRow(properties, new TextObject("{=BC_ClientTooltip_ClientPower}Effective client power"), assessment.EffectiveClientPower.ToString("0.#"));
            AddValueRow(properties, new TextObject("{=BC_ClientTooltip_BlocPower}Opposing bloc power"), assessment.SuzerainBlocPower.ToString("0.#"));
            AddValueRow(properties, new TextObject("{=BC_ClientTooltip_SuzerainPower}Suzerain's contribution"), assessment.SuzerainPower.ToString("0.#"));
            if (assessment.OtherClientsPower > 0f)
                AddValueRow(properties, new TextObject("{=BC_ClientTooltip_ClientsPower}Other clients' contribution"), assessment.OtherClientsPower.ToString("0.#"));
            if (assessment.AlliesPower > 0f)
                AddValueRow(properties, new TextObject("{=BC_ClientTooltip_AlliesPower}Allies' contribution"), assessment.AlliesPower.ToString("0.#"));
            AddValueRow(properties, new TextObject("{=BC_ClientTooltip_RequiredRatio}Required power ratio"), (assessment.RequiredPowerRatio * 100f).ToString("0.#") + "%");
            AddValueRow(properties, new TextObject("{=BC_ClientTooltip_PowerNeeded}Power needed"), (assessment.SuzerainBlocPower * assessment.RequiredPowerRatio).ToString("0.#"));
            AddParagraph(properties, new TextObject("{=BC_ClientTooltip_PowerBasis}Power combines military strength and influence; client support depends on Liberty Desire."));

            if (proposer != null)
            {
                AddSeparator(properties);
                AddValueRow(properties, new TextObject("{=BC_ClientTooltip_Proposer}Leading prospective proposer"), proposer.Candidate.Clan.Name.ToString());
                AddValueRow(properties, new TextObject("{=BC_ClientTooltip_PersonalDesire}Personal liberty desire"),
                    Required(proposer.Candidate.LibertyDesire, C.ClientClanLiberationDesireThreshold));
                AddValueRow(properties, new TextObject("{=BC_ClientTooltip_WarWill}War Will"), proposer.WarWill.ToString("0.#"));
                AddValueRow(properties, new TextObject("{=BC_ClientTooltip_Resolve}Liberation resolve"), FormatSigned(proposer.Resolve));
                AddValueRow(properties, new TextObject("{=BC_ClientTooltip_Willingness}Willingness to propose"),
                    Required(proposer.Willingness, BellumCivileOptions.WarWillDeclareThreshold));
                AddValueRow(properties, new TextObject("{=BC_ClientTooltip_Influence}Influence"),
                    Required(proposer.Budget.CurrentInfluence, proposer.Budget.RequiredInfluence));
                AddValueRow(properties, new TextObject("{=BC_ClientTooltip_CostReserve}Proposal cost / reserve"),
                    proposer.Budget.RequestedCost.ToString("0.#") + " / " + proposer.Budget.ProtectedReserve.ToString("0.#"));
            }
            AddParagraph(properties, new TextObject("{=BC_ClientTooltip_Council}A declaration still requires a war council decision."));

            return properties;
        }

        private static TextObject GetStatus(ClientLibertyAssessment assessment, ClientLiberationProposalAssessment proposer)
        {
            if (assessment.SuzerainKingdom != null && assessment.ClientKingdom.IsAtWarWith(assessment.SuzerainKingdom))
                return new TextObject("{=BC_ClientTooltip_AtWar}The liberation war is already underway.");
            if (!WarPeaceRevampBehavior.IsRevampEnabled())
                return new TextObject("{=BC_ClientTooltip_Disabled}Liberation proposals are inactive while the War and Peace Revamp is disabled.");
            if (assessment.CooldownRemainingDays > 0f)
            {
                TextObject status = new TextObject("{=BC_MapClientState_StatusBound}The settlement remains binding for {DAYS} more days.");
                status.SetTextVariable("DAYS", (int)Math.Ceiling(assessment.CooldownRemainingDays));
                return status;
            }

            if (assessment.RealmLibertyDesire < C.ClientRealmLiberationDesireThreshold)
                return new TextObject("{=BC_ClientTooltip_LowDesire}Liberation blocked: the realm's desire for independence is too low.");

            if (assessment.LiberationReadiness < 100f)
                return new TextObject("{=BC_ClientTooltip_LowPower}Liberation blocked: insufficient power.");
            if (!assessment.CanAttemptLiberation)
                return new TextObject("{=BC_ClientTooltip_Unavailable}Liberation is not currently available.");
            if (assessment.ClientKingdom.UnresolvedDecisions.Any(decision => decision is DeclareWarDecision))
                return new TextObject("{=BC_ClientTooltip_Pending}Liberation blocked: a war decision is already pending.");
            if (proposer == null)
                return new TextObject("{=BC_ClientTooltip_NoProposer}No eligible clan is available to propose liberation.");

            switch (proposer.Blocker)
            {
                case ClientLiberationProposalBlocker.PersonalDesire:
                    return new TextObject("{=BC_ClientTooltip_NoPersonalDesire}Liberation blocked: no eligible clan has sufficient personal desire for independence.");
                case ClientLiberationProposalBlocker.Willingness:
                    return new TextObject("{=BC_ClientTooltip_NoWillingness}Liberation blocked: no eligible clan has sufficient willingness to propose it.");
                case ClientLiberationProposalBlocker.Influence:
                    return new TextObject("{=BC_ClientTooltip_NoInfluence}Liberation blocked: willing clans lack the influence needed to propose it while keeping their reserve.");
                case ClientLiberationProposalBlocker.StrategicTarget:
                    return new TextObject("{=BC_ClientTooltip_NoTarget}Liberation blocked: otherwise eligible clans are still deterred from targeting the suzerain.");
                case ClientLiberationProposalBlocker.Permission:
                    return proposer.PermissionReason != null && !proposer.PermissionReason.IsEmpty()
                        ? new TextObject("{=BC_ClientTooltip_Permission}Liberation blocked: {REASON}").SetTextVariable("REASON", proposer.PermissionReason)
                        : new TextObject("{=BC_ClientTooltip_Unavailable}Liberation is not currently available.");
                default:
                    return new TextObject("{=BC_ClientTooltip_Ready}Ready to propose liberation.");
            }
        }

        private static IEnumerable<KeyValuePair<string, float>> BuildRealmReasons(ClientLibertyAssessment assessment)
        {
            Dictionary<string, float> weightedReasons = new Dictionary<string, float>();
            float totalWeight = assessment.Clans?
                .Where(entry => entry?.Clan != null)
                .Sum(entry => Math.Max(1f, entry.Power)) ?? 0f;
            if (totalWeight <= 0f)
                return Enumerable.Empty<KeyValuePair<string, float>>();

            foreach (ClientClanLibertyAssessment clanAssessment in assessment.Clans.Where(entry => entry?.Clan != null))
            {
                float weight = Math.Max(1f, clanAssessment.Power) / totalWeight;
                foreach (ClientLibertyReason reason in clanAssessment.Reasons)
                {
                    if (!weightedReasons.ContainsKey(reason.Label))
                        weightedReasons[reason.Label] = 0f;
                    weightedReasons[reason.Label] += reason.Amount * weight;
                }
                float clamp = clanAssessment.LibertyDesire - clanAssessment.Reasons.Sum(reason => reason.Amount);
                if (!weightedReasons.ContainsKey("desire limits")) weightedReasons["desire limits"] = 0f;
                weightedReasons["desire limits"] += clamp * weight;
            }

            return weightedReasons
                .Where(reason => Math.Abs(reason.Value) >= 0.05f)
                .OrderByDescending(reason => Math.Abs(reason.Value))
                .ThenBy(reason => reason.Key);
        }

        private static TextObject ResolveReasonLabel(string label)
        {
            switch (label)
            {
                case "client condition": return new TextObject("{=BC_MapClientState_ReasonClientCondition}Client condition");
                case "forced submission": return new TextObject("{=BC_MapClientState_ReasonForcedSubmission}Forced submission");
                case "rightful suzerainty": return new TextObject("{=BC_MapClientState_ReasonRightfulSuzerainty}Rightful suzerainty");
                case "unlawful suzerainty": return new TextObject("{=BC_MapClientState_ReasonUnlawfulSuzerainty}Unlawful suzerainty");
                case "shared culture": return new TextObject("{=BC_MapClientState_ReasonSharedCulture}Shared culture");
                case "foreign culture": return new TextObject("{=BC_MapClientState_ReasonForeignCulture}Foreign culture");
                case "relations with suzerain": return new TextObject("{=BC_MapClientState_ReasonRelations}Relations with the suzerain");
                case "marriage tie to suzerain": return new TextObject("{=BC_MapClientState_ReasonMarriage}Marriage ties");
                case "militarist independence": return new TextObject("{=BC_MapClientState_ReasonMilitarists}Glory: independence ambitions");
                case "populist self-rule": return new TextObject("{=BC_MapClientState_ReasonPopulists}Liberty: opposition to foreign rule");
                case "aristocratic legality": return new TextObject("{=BC_MapClientState_ReasonAristocrats}Nobility: unlawful suzerainty");
                case "valor": return new TextObject("{=BC_MapClientState_ReasonValor}Valor");
                case "mercy": return new TextObject("{=BC_MapClientState_ReasonMercy}Mercy");
                case "honor": return new TextObject("{=BC_MapClientState_ReasonHonor}Honor");
                case "desire limits": return new TextObject("{=BC_ClientTooltip_DesireLimits}Individual desire limits (0-100)");
                default: return new TextObject(label ?? string.Empty);
            }
        }

        private static void AddValueRow(List<TooltipProperty> properties, TextObject label, string value)
        {
            properties.Add(new TooltipProperty(label.ToString(), value, 0));
        }

        private static string Required(float current, float required)
            => new TextObject("{=BC_ClientTooltip_Required}{CURRENT} / {REQUIRED} required")
                .SetTextVariable("CURRENT", current.ToString("0.#"))
                .SetTextVariable("REQUIRED", required.ToString("0.#")).ToString();

        private static void AddParagraph(List<TooltipProperty> properties, TextObject text)
            => properties.Add(new TooltipProperty(string.Empty, text.ToString(), 0, false,
                TooltipProperty.TooltipPropertyFlags.MultiLine));

        private static void AddSeparator(List<TooltipProperty> properties)
        {
            properties.Add(new TooltipProperty(
                string.Empty,
                string.Empty,
                0,
                false,
                TooltipProperty.TooltipPropertyFlags.DefaultSeperator));
        }

        private static string FormatSigned(float value)
        {
            return value.ToString("+0.#;-0.#;0");
        }
    }
}
