using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
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
            => ClientLibertyTooltip.Build(_assessment);
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

            AddValueRow(properties, new TextObject("{=BC_MapClientState_LibertyDesire}Liberty Desire"), assessment.RealmLibertyDesire.ToString("0"));
            AddValueRow(properties, new TextObject("{=BC_MapClientState_LiberationReadiness}Liberation Readiness"), assessment.LiberationReadiness.ToString("0") + "%");

            properties.Add(new TooltipProperty(
                string.Empty,
                GetStatus(assessment).ToString(),
                0,
                false,
                TooltipProperty.TooltipPropertyFlags.MultiLine));

            AddSeparator(properties);
            properties.Add(new TooltipProperty(
                new TextObject("{=BC_MapClientState_PrincipalCauses}Principal causes").ToString(),
                string.Empty,
                0));

            foreach (KeyValuePair<string, float> reason in BuildRealmReasons(assessment))
                AddValueRow(properties, ResolveReasonLabel(reason.Key), FormatSigned(reason.Value));

            ClientClanLibertyAssessment mostDefiant = assessment.Clans?
                .Where(entry => entry?.Clan != null)
                .OrderByDescending(entry => entry.LibertyDesire)
                .ThenByDescending(entry => RebellionPowerHelper.CalculateClanPower(entry.Clan))
                .FirstOrDefault();
            if (mostDefiant != null)
            {
                AddSeparator(properties);
                string value = $"{mostDefiant.Clan.Name} ({mostDefiant.LibertyDesire:0})";
                AddValueRow(properties, new TextObject("{=BC_MapClientState_MostDefiantClan}Most defiant clan"), value);
            }

            return properties;
        }

        private static TextObject GetStatus(ClientLibertyAssessment assessment)
        {
            if (assessment.CooldownRemainingDays > 0f)
            {
                TextObject status = new TextObject("{=BC_MapClientState_StatusBound}The settlement remains binding for {DAYS} more days.");
                status.SetTextVariable("DAYS", (int)Math.Ceiling(assessment.CooldownRemainingDays));
                return status;
            }

            if (assessment.RealmLibertyDesire < C.ClientRealmLiberationDesireThreshold)
                return new TextObject("{=BC_MapClientState_StatusContent}The client realm remains broadly content with its present settlement.");

            if (assessment.LiberationReadiness < 100f)
                return new TextObject("{=BC_MapClientState_StatusWeak}The client realm desires independence but presently lacks the strength to revolt.");

            WarPeaceRevampBehavior warWill = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            bool hasReadyAgitator = assessment.Clans != null && assessment.Clans.Any(entry =>
                entry?.Clan != null
                && entry.LibertyDesire >= C.ClientClanLiberationDesireThreshold
                && (warWill?.GetWarWill(entry.Clan) ?? 0f) >= BellumCivileOptions.WarWillDeclareThreshold);

            return hasReadyAgitator
                ? new TextObject("{=BC_MapClientState_StatusReady}The client realm is ready to seek liberation.")
                : new TextObject("{=BC_MapClientState_StatusNoAgitator}The client realm is defiant, but no leading clan currently has the will to begin a liberation war.");
        }

        private static IEnumerable<KeyValuePair<string, float>> BuildRealmReasons(ClientLibertyAssessment assessment)
        {
            Dictionary<string, float> weightedReasons = new Dictionary<string, float>();
            float totalWeight = assessment.Clans?
                .Where(entry => entry?.Clan != null)
                .Sum(entry => Math.Max(1f, RebellionPowerHelper.CalculateClanPower(entry.Clan))) ?? 0f;
            if (totalWeight <= 0f)
                return Enumerable.Empty<KeyValuePair<string, float>>();

            foreach (ClientClanLibertyAssessment clanAssessment in assessment.Clans.Where(entry => entry?.Clan != null))
            {
                float weight = Math.Max(1f, RebellionPowerHelper.CalculateClanPower(clanAssessment.Clan)) / totalWeight;
                foreach (ClientLibertyReason reason in clanAssessment.Reasons.Where(reason => Math.Abs(reason.Amount) >= 0.01f))
                {
                    if (!weightedReasons.ContainsKey(reason.Label))
                        weightedReasons[reason.Label] = 0f;
                    weightedReasons[reason.Label] += reason.Amount * weight;
                }
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
                case "militarist independence": return new TextObject("{=BC_MapClientState_ReasonMilitarists}Militarist independence");
                case "populist self-rule": return new TextObject("{=BC_MapClientState_ReasonPopulists}Populist self-rule");
                case "aristocratic legality": return new TextObject("{=BC_MapClientState_ReasonAristocrats}Aristocratic legality");
                case "royalist loyalty": return new TextObject("{=BC_MapClientState_ReasonRoyalists}Traditionalist respect for authority");
                case "valor": return new TextObject("{=BC_MapClientState_ReasonValor}Valor");
                case "mercy": return new TextObject("{=BC_MapClientState_ReasonMercy}Mercy");
                case "honor": return new TextObject("{=BC_MapClientState_ReasonHonor}Honor");
                default: return new TextObject(label ?? string.Empty);
            }
        }

        private static void AddValueRow(List<TooltipProperty> properties, TextObject label, string value)
        {
            properties.Add(new TooltipProperty(label.ToString(), value, 0));
        }

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
