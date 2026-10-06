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

            IsContent = assessment.RealmLibertyDesire < 40f;
            IsRestless = assessment.RealmLibertyDesire >= 40f && assessment.RealmLibertyDesire < C.ClientRealmLiberationDesireThreshold;
            IsDefiant = assessment.RealmLibertyDesire >= C.ClientRealmLiberationDesireThreshold;

            int roundedDesire = (int)Math.Round(assessment.RealmLibertyDesire, MidpointRounding.AwayFromZero);
            if (roundedDesire == _roundedLibertyDesire)
                return;

            _roundedLibertyDesire = roundedDesire;
            LibertyDesireText = roundedDesire.ToString();
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

            AddValueRow(properties, new TextObject("{=BC_MapClientState_LibertyDesire}Liberty Desire"),
                new TextObject("{=BC_ClientTooltip_DesireValue}{CURRENT} ({STATE})")
                    .SetTextVariable("CURRENT", assessment.RealmLibertyDesire.ToString("0.#"))
                    .SetTextVariable("STATE", GetDesireState(assessment.RealmLibertyDesire)).ToString());
            AddValueRow(properties, new TextObject("{=BC_MapClientState_LiberationReadiness}Liberation Readiness"),
                new TextObject("{=BC_ClientTooltip_ReadinessValue}{CURRENT}% / 100%")
                    .SetTextVariable("CURRENT", assessment.LiberationReadiness.ToString("0.#")).ToString());

            ClientLiberationProposalAssessment proposer = ClientLiberationProposalAssessment.FindBest(assessment);
            AddParagraph(properties, GetStatus(assessment, proposer));
            if (assessment.CooldownRemainingDays > 0f && WarPeaceRevampBehavior.IsRevampEnabled()
                && (assessment.SuzerainKingdom == null || !assessment.ClientKingdom.IsAtWarWith(assessment.SuzerainKingdom)))
            {
                AddParagraph(properties, new TextObject("{=BC_MapClientState_StatusBound}The settlement remains binding for {DAYS} more days.")
                    .SetTextVariable("DAYS", (int)Math.Ceiling(assessment.CooldownRemainingDays)));
            }

            AddSeparator(properties);
            AddValueRow(properties, new TextObject("{=BC_MapClientState_PrincipalCauses}Principal causes"), string.Empty);
            foreach (KeyValuePair<string, float> reason in BuildRealmReasons(assessment))
                AddValueRow(properties, ResolveReasonLabel(reason.Key), FormatSigned(reason.Value));

            var mostDefiant = assessment.Clans?.Where(entry => entry?.Clan != null)
                .OrderByDescending(entry => entry.LibertyDesire)
                .ThenBy(entry => entry.Clan.StringId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (mostDefiant != null)
            {
                AddSeparator(properties);
                AddValueRow(properties, new TextObject("{=BC_MapClientState_MostDefiantClan}Most defiant clan"),
                    new TextObject("{=BC_ClientTooltip_ClanDesire}{CLAN_NAME} ({DESIRE})")
                        .SetTextVariable("CLAN_NAME", mostDefiant.Clan.Name)
                        .SetTextVariable("DESIRE", mostDefiant.LibertyDesire.ToString("0.#")).ToString());
            }

            return properties;
        }

        private static TextObject GetDesireState(float desire)
        {
            if (desire < 40f)
                return new TextObject("{=BC_ClientTooltip_Content}Content");
            if (desire < C.ClientRealmLiberationDesireThreshold)
                return new TextObject("{=BC_ClientTooltip_Restless}Restless");
            return new TextObject("{=BC_ClientTooltip_Defiant}Defiant, {THRESHOLD}+")
                .SetTextVariable("THRESHOLD", C.ClientRealmLiberationDesireThreshold.ToString("0.#"));
        }

        private static TextObject GetStatus(ClientLibertyAssessment assessment, ClientLiberationProposalAssessment proposer)
        {
            if (assessment.SuzerainKingdom != null && assessment.ClientKingdom.IsAtWarWith(assessment.SuzerainKingdom))
                return new TextObject("{=BC_ClientTooltip_AtWar}The liberation war is already underway.");
            if (!WarPeaceRevampBehavior.IsRevampEnabled())
                return new TextObject("{=BC_ClientTooltip_Disabled}Liberation proposals are inactive while the War and Peace Revamp is disabled.");
            if (assessment.RealmLibertyDesire < 40f)
                return new TextObject("{=BC_MapClientState_StatusContent}The client realm remains broadly content with its present settlement.");
            if (assessment.RealmLibertyDesire < C.ClientRealmLiberationDesireThreshold)
                return new TextObject("{=BC_ClientTooltip_LowDesire}Discontent is growing, but the realm is not yet prepared to seek independence.");
            if (assessment.LiberationReadiness < 100f)
                return new TextObject("{=BC_ClientTooltip_LowPower}The realm seeks independence, but lacks the power to challenge its suzerain.");
            if (assessment.CooldownRemainingDays > 0f)
                return new TextObject("{=BC_ClientTooltip_BoundPower}The realm seeks independence and has the power to challenge its suzerain.");
            if (!assessment.CanAttemptLiberation)
                return new TextObject("{=BC_ClientTooltip_Unavailable}Liberation is not currently available.");
            if (assessment.ClientKingdom.UnresolvedDecisions.Any(decision => decision is DeclareWarDecision))
                return new TextObject("{=BC_ClientTooltip_Pending}The realm awaits the outcome of a pending war decision.");
            if (proposer == null)
                return new TextObject("{=BC_ClientTooltip_NoProposer}No eligible clan is available to propose liberation.");

            switch (proposer.Blocker)
            {
                case ClientLiberationProposalBlocker.PersonalDesire:
                    return new TextObject("{=BC_ClientTooltip_NoPersonalDesire}No leading clan is determined enough to propose independence.");
                case ClientLiberationProposalBlocker.Willingness:
                    return new TextObject("{=BC_ClientTooltip_NoWillingness}The realm has sufficient power, but its leading clans remain reluctant to act.");
                case ClientLiberationProposalBlocker.Influence:
                    return new TextObject("{=BC_ClientTooltip_NoInfluence}Willing clans lack the influence they can spare to propose liberation.");
                case ClientLiberationProposalBlocker.StrategicTarget:
                    return new TextObject("{=BC_ClientTooltip_NoTarget}The realm has sufficient power, but its leading clans still judge war with the suzerain unwise.");
                case ClientLiberationProposalBlocker.Permission:
                    return proposer.PermissionReason != null && !proposer.PermissionReason.IsEmpty()
                        ? new TextObject("{=BC_ClientTooltip_Permission}Liberation blocked: {REASON}").SetTextVariable("REASON", proposer.PermissionReason)
                        : new TextObject("{=BC_ClientTooltip_Unavailable}Liberation is not currently available.");
                default:
                    return new TextObject("{=BC_ClientTooltip_Ready}Support and resources are sufficient to propose liberation. A declaration requires council approval.");
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
