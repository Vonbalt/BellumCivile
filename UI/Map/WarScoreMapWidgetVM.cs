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
    internal sealed class WarScoreMapWidgetVM : ViewModel
    {
        private readonly MBBindingList<WarScoreMapWidgetItemVM> _wars;
        private readonly MBBindingList<ClientStateMapWidgetItemVM> _clients;
        private int _lastRevision = int.MinValue;
        private string _lastRealmId = string.Empty;
        private bool _lastEnabled;
        private int _lastClientRevision = int.MinValue;
        private string _lastClientRealmId = string.Empty;
        private bool _lastClientEnabled;

        [DataSourceProperty]
        public MBBindingList<WarScoreMapWidgetItemVM> Wars => _wars;

        [DataSourceProperty]
        public MBBindingList<ClientStateMapWidgetItemVM> Clients => _clients;

        public WarScoreMapWidgetVM()
        {
            _wars = new MBBindingList<WarScoreMapWidgetItemVM>();
            _clients = new MBBindingList<ClientStateMapWidgetItemVM>();
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            RefreshWarsIfNeeded(true);
            RefreshClientsIfNeeded(true);
        }

        public override void OnFinalize()
        {
            CampaignEvents.TickEvent.ClearListeners(this);
            ClearWars();
            ClearClients();
            base.OnFinalize();
        }

        private void OnTick(float deltaTime)
        {
            RefreshWarsIfNeeded(false);
            RefreshClientsIfNeeded(false);
        }

        private void RefreshWarsIfNeeded(bool force)
        {
            bool enabled = BellumCivileOptions.EnableWarPeaceLogicRevamp
                && BellumCivileOptions.EnableWarScoreMapWidget;
            Kingdom playerRealm = Clan.PlayerClan?.MapFaction as Kingdom;
            WarScoreBehavior warScoreBehavior = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>();
            int revision = warScoreBehavior?.RuntimeRevision ?? int.MinValue;
            string realmId = playerRealm?.StringId ?? string.Empty;

            if (!force
                && enabled == _lastEnabled
                && revision == _lastRevision
                && realmId == _lastRealmId)
            {
                return;
            }

            _lastEnabled = enabled;
            _lastRevision = revision;
            bool realmChanged = realmId != _lastRealmId;
            _lastRealmId = realmId;

            if (!enabled || playerRealm == null || warScoreBehavior == null)
            {
                if (_wars.Count > 0)
                    ClearWars();
                return;
            }

            if (realmChanged)
                ClearWars();

            IReadOnlyList<WarScoreRecord> displayableWars = warScoreBehavior.GetDisplayableWars(playerRealm);
            HashSet<string> activeWarKeys = new HashSet<string>(displayableWars.Select(war => war.WarKey));
            for (int index = _wars.Count - 1; index >= 0; index--)
            {
                if (!activeWarKeys.Contains(_wars[index].WarKey))
                {
                    _wars[index].OnFinalize();
                    _wars.RemoveAt(index);
                }
            }

            foreach (WarScoreRecord war in displayableWars)
            {
                Kingdom opposingRealm = warScoreBehavior.GetOpposingKingdom(war, playerRealm);
                if (opposingRealm == null)
                    continue;

                float relativeScore = war.GetSelfRelativeScore(playerRealm.StringId);
                WarScoreMapWidgetItemVM existingItem = _wars.FirstOrDefault(item => item.WarKey == war.WarKey);
                if (existingItem != null)
                    existingItem.Update(war, playerRealm, opposingRealm, relativeScore);
                else
                    _wars.Add(new WarScoreMapWidgetItemVM(war, playerRealm, opposingRealm, relativeScore));
            }
        }

        private void ClearWars()
        {
            foreach (WarScoreMapWidgetItemVM item in _wars)
                item?.OnFinalize();

            _wars.Clear();
        }

        private void RefreshClientsIfNeeded(bool force)
        {
            Kingdom playerRealm = Clan.PlayerClan?.MapFaction as Kingdom;
            ClientKingdomBehavior clientBehavior = Campaign.Current?.GetCampaignBehavior<ClientKingdomBehavior>();
            bool enabled = BellumCivileOptions.EnableWarPeaceLogicRevamp
                && BellumCivileOptions.EnableClientStateMapWidget
                && playerRealm?.RulingClan == Clan.PlayerClan;
            int revision = clientBehavior?.RuntimeRevision ?? int.MinValue;
            string realmId = playerRealm?.StringId ?? string.Empty;

            if (!force
                && enabled == _lastClientEnabled
                && revision == _lastClientRevision
                && realmId == _lastClientRealmId)
            {
                return;
            }

            _lastClientEnabled = enabled;
            _lastClientRevision = revision;
            bool realmChanged = realmId != _lastClientRealmId;
            _lastClientRealmId = realmId;

            if (!enabled || playerRealm == null || clientBehavior == null)
            {
                if (_clients.Count > 0)
                    ClearClients();
                return;
            }

            if (realmChanged)
                ClearClients();

            IReadOnlyList<Kingdom> clientKingdoms = clientBehavior.GetClients(playerRealm);
            HashSet<string> activeClientIds = new HashSet<string>(clientKingdoms.Select(client => client.StringId));
            for (int index = _clients.Count - 1; index >= 0; index--)
            {
                if (!activeClientIds.Contains(_clients[index].ClientKingdomId))
                {
                    _clients[index].OnFinalize();
                    _clients.RemoveAt(index);
                }
            }

            foreach (Kingdom client in clientKingdoms)
            {
                ClientLibertyAssessment assessment = clientBehavior.BuildLibertyAssessment(client);
                if (assessment == null)
                    continue;

                ClientStateMapWidgetItemVM existingItem = _clients.FirstOrDefault(item => item.ClientKingdomId == client.StringId);
                if (existingItem != null)
                    existingItem.Update(assessment);
                else
                    _clients.Add(new ClientStateMapWidgetItemVM(assessment));
            }
        }

        private void ClearClients()
        {
            foreach (ClientStateMapWidgetItemVM item in _clients)
                item?.OnFinalize();

            _clients.Clear();
        }
    }

    internal sealed class WarScoreMapWidgetItemVM : ViewModel
    {
        private BannerImageIdentifierVM _opposingBanner;
        private string _scoreText;
        private bool _isWinning;
        private bool _isLosing;
        private bool _isEven;
        private int _roundedScore = int.MinValue;
        private WarScoreRecord _war;
        private Kingdom _viewingRealm;
        private Kingdom _opposingRealm;
        private readonly BasicTooltipViewModel _warScoreHint;

        public string WarKey { get; }

        [DataSourceProperty]
        public BannerImageIdentifierVM OpposingBanner
        {
            get => _opposingBanner;
            private set => SetField(ref _opposingBanner, value, nameof(OpposingBanner));
        }

        [DataSourceProperty]
        public string ScoreText
        {
            get => _scoreText;
            private set => SetField(ref _scoreText, value, nameof(ScoreText));
        }

        [DataSourceProperty]
        public bool IsWinning
        {
            get => _isWinning;
            private set => SetField(ref _isWinning, value, nameof(IsWinning));
        }

        [DataSourceProperty]
        public bool IsLosing
        {
            get => _isLosing;
            private set => SetField(ref _isLosing, value, nameof(IsLosing));
        }

        [DataSourceProperty]
        public bool IsEven
        {
            get => _isEven;
            private set => SetField(ref _isEven, value, nameof(IsEven));
        }

        public WarScoreMapWidgetItemVM(
            WarScoreRecord war,
            Kingdom viewingRealm,
            Kingdom opposingRealm,
            float selfRelativeScore)
        {
            WarKey = war?.WarKey ?? string.Empty;
            _warScoreHint = new BasicTooltipViewModel(BuildTooltipProperties);
            Update(war, viewingRealm, opposingRealm, selfRelativeScore);
        }

        public void ExecuteBeginWarScoreHint()
        {
            _warScoreHint?.ExecuteBeginHint();
        }

        public void ExecuteEndWarScoreHint()
        {
            _warScoreHint?.ExecuteEndHint();
        }

        public override void OnFinalize()
        {
            _warScoreHint?.ExecuteEndHint();
            base.OnFinalize();
        }

        public void Update(
            WarScoreRecord war,
            Kingdom viewingRealm,
            Kingdom opposingRealm,
            float selfRelativeScore)
        {
            _war = war;
            _viewingRealm = viewingRealm;

            if (_opposingRealm != opposingRealm)
            {
                _opposingRealm = opposingRealm;
                OpposingBanner = opposingRealm == null
                    ? null
                    : new BannerImageIdentifierVM(opposingRealm.Banner, true);
            }

            UpdateScore(selfRelativeScore);
        }

        private void UpdateScore(float selfRelativeScore)
        {
            int roundedScore = (int)Math.Round(selfRelativeScore, MidpointRounding.AwayFromZero);
            if (roundedScore == _roundedScore)
                return;

            _roundedScore = roundedScore;
            ScoreText = roundedScore.ToString("+0;-0;0");
            IsWinning = roundedScore > 0;
            IsLosing = roundedScore < 0;
            IsEven = roundedScore == 0;
        }

        private List<TooltipProperty> BuildTooltipProperties()
            => WarScoreTooltip.Build(_war, _viewingRealm, _opposingRealm);
    }

    internal static class WarScoreTooltip
    {
        internal static List<TooltipProperty> Build(WarScoreRecord war, Kingdom viewingRealm, Kingdom opposingRealm)
        {
            List<TooltipProperty> properties = new List<TooltipProperty>();
            if (war == null || viewingRealm == null || opposingRealm == null)
                return properties;

            properties.Add(new TooltipProperty(
                GetConflictTitle(war).ToString(),
                string.Empty,
                0,
                false,
                TooltipProperty.TooltipPropertyFlags.Title));

            properties.Add(new TooltipProperty(
                string.Empty,
                GetConflictStatus(war, viewingRealm, opposingRealm).ToString(),
                0,
                false,
                TooltipProperty.TooltipPropertyFlags.MultiLine));

            AddSeparator(properties);

            float direction = war.AttackerKingdomId == viewingRealm.StringId ? 1f : -1f;
            float score = war.GetSelfRelativeScore(viewingRealm.StringId);
            AddValueRow(properties, new TextObject("{=BC_MapWarScore_Total}War Score"), FormatScore(score));
            AddSeparator(properties);

            int componentCount = 0;
            componentCount += AddComponentRow(
                properties,
                new TextObject("{=BC_MapWarScore_TerritorialControl}Territorial control"),
                direction * war.OccupationScore,
                C.WarScoreForcePeaceThreshold);
            componentCount += AddComponentRow(
                properties,
                new TextObject("{=BC_MapWarScore_BattleAdvantage}Battle advantage"),
                direction * war.BattleScore,
                C.WarScoreBattleCap);
            componentCount += AddComponentRow(
                properties,
                new TextObject("{=BC_MapWarScore_RaidingAdvantage}Raiding advantage"),
                direction * war.RaidScore,
                C.WarScoreRaidCap);
            componentCount += AddComponentRow(
                properties,
                new TextObject("{=BC_MapWarScore_PrisonerLeverage}Prisoner leverage"),
                direction * war.PrisonerScore,
                C.WarScorePrisonerCap);
            componentCount += AddComponentRow(
                properties,
                new TextObject("{=BC_MapWarScore_CampaignMomentum}Campaign momentum"),
                direction * war.TickingScore,
                C.WarScoreTickingCap);
            componentCount += AddComponentRow(
                properties,
                new TextObject("{=BC_MapWarScore_LandlessPressure}Without a seat of power"),
                direction * war.LandlessPressureScore,
                C.WarScoreLandlessPressureCap);

            if (war.ConflictType == WarScoreConflictType.ClaimFeud)
            {
                componentCount += AddComponentRow(
                    properties,
                    new TextObject("{=BC_MapWarScore_ClaimObjective}Claim objective"),
                    direction * war.ObjectiveScore,
                    C.WarScoreFeudObjectiveCap);
            }

            if (componentCount == 0)
            {
                properties.Add(new TooltipProperty(
                    string.Empty,
                    new TextObject("{=BC_MapWarScore_NoAdvantage}No side currently holds a measurable advantage.").ToString(),
                    0,
                    false,
                    TooltipProperty.TooltipPropertyFlags.MultiLine));
            }

            float relativeComponentTotal = direction * (
                war.OccupationScore
                + war.BattleScore
                + war.RaidScore
                + war.PrisonerScore
                + war.TickingScore
                + war.LandlessPressureScore
                + war.ObjectiveScore);

            if (Math.Abs(direction * war.LandlessPressureScore) >= C.WarScoreLandlessPressureCap - 0.01f)
            {
                AddSeparator(properties);
                TextObject landlessOutcome = direction * war.LandlessPressureScore > 0f
                    ? new TextObject("{=BC_MapWarScore_LandlessVictory}The enemy's failure to secure a seat of power has forced its surrender.")
                    : new TextObject("{=BC_MapWarScore_LandlessDefeat}The realm's failure to secure a seat of power has forced its surrender.");
                properties.Add(new TooltipProperty(
                    string.Empty,
                    landlessOutcome.ToString(),
                    0,
                    false,
                    TooltipProperty.TooltipPropertyFlags.MultiLine));
            }
            else if (Math.Abs(score) >= C.WarScoreForcePeaceThreshold - 0.01f
                && Math.Abs(relativeComponentTotal) < C.WarScoreForcePeaceThreshold - 0.01f)
            {
                AddSeparator(properties);
                TextObject fullOccupation = score > 0f
                    ? new TextObject("{=BC_MapWarScore_FullOccupationVictory}Full occupation has forced total victory.")
                    : new TextObject("{=BC_MapWarScore_FullOccupationDefeat}Full occupation has forced total defeat.");
                properties.Add(new TooltipProperty(
                    string.Empty,
                    fullOccupation.ToString(),
                    0,
                    false,
                    TooltipProperty.TooltipPropertyFlags.MultiLine));
            }
            else if (Math.Abs(relativeComponentTotal) > C.WarScoreForcePeaceThreshold + 0.01f)
            {
                AddSeparator(properties);
                properties.Add(new TooltipProperty(
                    string.Empty,
                    new TextObject("{=BC_MapWarScore_CappedNotice}Overall War Score is capped at 100 in either direction.").ToString(),
                    0,
                    false,
                    TooltipProperty.TooltipPropertyFlags.MultiLine));
            }

            return properties;
        }

        private static TextObject GetConflictTitle(WarScoreRecord war)
        {
            switch (war?.ConflictType ?? WarScoreConflictType.ForeignWar)
            {
                case WarScoreConflictType.CivilWar:
                    return new TextObject("{=BC_MapWarScore_CivilWarTitle}Civil War");
                case WarScoreConflictType.ClaimFeud:
                    return new TextObject("{=BC_MapWarScore_ClaimFeudTitle}Claim Feud");
                default:
                    return new TextObject("{=BC_MapWarScore_ForeignWarTitle}Foreign War");
            }
        }

        private static TextObject GetConflictStatus(WarScoreRecord war, Kingdom viewingRealm, Kingdom opposingRealm)
        {
            TextObject realmName = ResolveRealmName(opposingRealm);
            TaleWorlds.CampaignSystem.Hero ruler = opposingRealm?.RulingClan?.Leader;
            if (ruler == null)
            {
                return new TextObject("{=BC_MapWarScore_AtWarWith}At war with {REALM_NAME}")
                    .SetTextVariable("REALM_NAME", realmName);
            }

            bool isAttacker = war.AttackerKingdomId == viewingRealm.StringId;
            TextObject status;
            switch (war.ConflictType)
            {
                case WarScoreConflictType.CivilWar:
                    status = isAttacker
                        ? new TextObject("{=BC_MapWarScore_RebellingAgainst}Rebelling against {RULER_NAME} of {REALM_NAME}")
                        : new TextObject("{=BC_MapWarScore_DefendingRealmAgainst}Defending the realm against {RULER_NAME} of {REALM_NAME}");
                    break;
                case WarScoreConflictType.ClaimFeud:
                    status = isAttacker
                        ? new TextObject("{=BC_MapWarScore_PressingClaimAgainst}Pressing the claim against {RULER_NAME} of {REALM_NAME}")
                        : new TextObject("{=BC_MapWarScore_DefendingClaimAgainst}Defending the disputed claim against {RULER_NAME} of {REALM_NAME}");
                    break;
                default:
                    status = isAttacker
                        ? new TextObject("{=BC_MapWarScore_Attacking}Attacking {RULER_NAME} of {REALM_NAME}")
                        : new TextObject("{=BC_MapWarScore_DefendingAgainst}Defending against {RULER_NAME} of {REALM_NAME}");
                    break;
            }

            status.SetTextVariable("RULER_NAME", ruler.Name);
            status.SetTextVariable("REALM_NAME", realmName);
            return status;
        }

        private static TextObject ResolveRealmName(Kingdom kingdom)
        {
            TextObject encyclopediaName = kingdom?.EncyclopediaTitle;
            return encyclopediaName != null && !string.IsNullOrWhiteSpace(encyclopediaName.ToString())
                ? encyclopediaName
                : kingdom?.Name ?? new TextObject("{=BC_MapWarScore_UnknownRealm}Unknown realm");
        }

        private static int AddComponentRow(
            List<TooltipProperty> properties,
            TextObject label,
            float value,
            float cap)
        {
            if (Math.Abs(value) < 0.05f)
                return 0;

            AddValueRow(properties, label, $"{FormatScore(value)} / {cap:0}");
            return 1;
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

        private static string FormatScore(float value)
        {
            string number = Math.Abs(value - (float)Math.Round(value)) < 0.01f
                ? ((int)Math.Round(value)).ToString()
                : value.ToString("0.#");
            return value > 0f ? "+" + number : number;
        }
    }
}
