using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI
{
    public class ClaimFeudItemVM : ViewModel
    {
        private readonly ClaimFeudRecord _record;
        private readonly Action<ClaimFeudItemVM> _onSelection;
        private readonly Action _onRefreshRequested;

        private string _name;
        private string _foundingDate;
        private string _demands;
        private string _claimantSideText;
        private string _holderSideText;
        private string _claimantLabel;
        private string _holderLabel;
        private string _feudIntensityText;
        private string _balanceOfPowerText;
        private Color _balanceOfPowerColor;
        private bool _isSelected;
        private bool _canPetition;
        private HintViewModel _petitionHint;
        private FactionMemberVM _claimantLeader;
        private FactionMemberVM _holderLeader;
        private MBBindingList<FactionMemberVM> _claimantMembers;
        private MBBindingList<FactionMemberVM> _holderMembers;
        private int _claimantStrength;
        private int _holderStrength;
        private int _totalStrength;

        public ClaimFeudRecord Record => _record;

        public ClaimFeudItemVM(ClaimFeudRecord record, Action<ClaimFeudItemVM> onSelection, Action onRefreshRequested = null)
        {
            _record = record;
            _onSelection = onSelection;
            _onRefreshRequested = onRefreshRequested;
            ClaimantMembers = new MBBindingList<FactionMemberVM>();
            HolderMembers = new MBBindingList<FactionMemberVM>();
            PetitionHint = new HintViewModel(new TextObject(""));
            Refresh();
        }

        public void Refresh()
        {
            Clan claimant = ResolveClan(_record?.ClaimantClanId);
            Clan holder = ResolveClan(_record?.HolderClanId);
            FeudalTitleRecord title = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>()?.GetTitle(_record?.TargetTitleId);

            TextObject nameText = new TextObject("{=BC_ClaimFeud_ItemName}{CLAIMANT_CLAN} feud with {HOLDER_CLAN}");
            nameText.SetTextVariable("CLAIMANT_CLAN", claimant?.Name ?? new TextObject("?"));
            nameText.SetTextVariable("HOLDER_CLAN", holder?.Name ?? new TextObject("?"));
            Name = nameText.ToString();

            float startedDay = Math.Max(0f, _record?.StartedDay ?? 0f);
            TextObject dateText = new TextObject("{=BC_ClaimFeud_Founded}Founded: {DATE}");
            dateText.SetTextVariable("DATE", CampaignTime.Days(startedDay).ToString());
            FoundingDate = dateText.ToString();

            TextObject demandsText = new TextObject("{=BC_ClaimFeud_Demands}Demands: {CLAIMANT_CLAN} claim to {TITLE_NAME}");
            demandsText.SetTextVariable("CLAIMANT_CLAN", claimant?.Name ?? new TextObject("?"));
            demandsText.SetTextVariable("TITLE_NAME", FormatTitle(title, claimant));
            Demands = demandsText.ToString();

            ClaimantSideText = claimant?.Name?.ToString() ?? "?";
            HolderSideText = holder?.Name?.ToString() ?? "?";

            ClaimantLeader = claimant != null ? new FactionMemberVM(claimant, false) : null;
            HolderLeader = holder != null ? new FactionMemberVM(holder, false) : null;

            ClaimantMembers.Clear();
            HolderMembers.Clear();
            foreach (Clan clan in ResolveSupporters(_record?.ClaimantSupporterIds, claimant))
                ClaimantMembers.Add(new FactionMemberVM(clan, false));
            foreach (Clan clan in ResolveSupporters(_record?.HolderSupporterIds, holder))
                HolderMembers.Add(new FactionMemberVM(clan, false));

            List<Clan> claimantSide = BuildSide(_record?.ClaimantSupporterIds, claimant);
            List<Clan> holderSide = BuildSide(_record?.HolderSupporterIds, holder);
            ClaimantStrength = Math.Max(0, (int)RebellionPowerHelper.CalculateFactionPower(claimantSide));
            HolderStrength = Math.Max(0, (int)RebellionPowerHelper.CalculateFactionPower(holderSide));
            TotalStrength = Math.Max(1, ClaimantStrength + HolderStrength);
            BalanceOfPowerText = HolderStrength > 0 ? $"{(int)((ClaimantStrength / (float)HolderStrength) * 100f)}%" : "999%";
            BalanceOfPowerColor = ClaimantStrength >= HolderStrength
                ? Color.ConvertStringToColor("#82E06AFF")
                : ClaimantStrength >= HolderStrength * 0.8f
                    ? Color.ConvertStringToColor("#FF8C00FF")
                    : Colors.Red;

            FeudIntensityText = new TextObject("{=BC_ClaimFeud_Intensity}Feud Intensity: {INTENSITY}%")
                .SetTextVariable("INTENSITY", Math.Max(0, Math.Min(100, (int)Math.Round(_record?.Pressure ?? 0f))))
                .ToString();

            RefreshPetitionState(claimant);
        }

        public void ExecuteSelect()
        {
            _onSelection?.Invoke(this);
        }

        public void ExecutePetition()
        {
            if (!CanPetition)
                return;

            ClaimFeudBehavior behavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            string report = null;
            if (behavior == null || !behavior.TryPetitionFeud(_record.RecordId, Clan.PlayerClan, out report))
            {
                TextObject failure = new TextObject("{=BC_ClaimFeud_PetitionFailed}The petition could not be sent: {REASON}");
                failure.SetTextVariable("REASON", report ?? "?");
                BellumCivileNotifications.ShowPersonal(failure, BellumNotificationColors.Danger);
                Refresh();
                return;
            }

            BellumCivileNotifications.ShowPersonal(new TextObject("{=BC_ClaimFeud_PetitionSent}Your petition has been placed before the ruler."), BellumNotificationColors.InheritanceWarning);
            _onRefreshRequested?.Invoke();
        }


        private void RefreshPetitionState(Clan claimant)
        {
            bool isPetitionableState = _record.State == ClaimFeudState.PetitionReady
                || (_record.State == ClaimFeudState.Agitating
                    && _record.Pressure >= BellumCivileOptions.DiscontentTrigger * 0.8f);
            CanPetition = claimant == Clan.PlayerClan
                && isPetitionableState;

            TextObject hint;
            if (claimant != Clan.PlayerClan)
                hint = new TextObject("{=BC_ClaimFeud_PetitionHint_NotClaimant}Only the claimant side can petition the ruler in this feud.");
            else if (_record.State == ClaimFeudState.Paused)
            {
                hint = new TextObject("{=BC_ClaimFeud_PetitionHint_Paused}This dispute is paused while {REASON}.");
                hint.SetTextVariable("REASON", string.IsNullOrWhiteSpace(_record.PauseReason)
                    ? new TextObject("{=BC_ClaimFeud_PauseReason_Generic}other conflicts require the parties' attention")
                    : new TextObject("{=!}" + _record.PauseReason));
            }
            else if (_record.State == ClaimFeudState.WarActive || _record.State == ClaimFeudState.DefiedPendingWar)
                hint = new TextObject("{=BC_ClaimFeud_PetitionHint_OpenWar}Once banners have been raised, the dispute must be settled by arms, surrender, or an imposed peace.");
            else if (_record.State != ClaimFeudState.PetitionReady && _record.Pressure < BellumCivileOptions.DiscontentTrigger * 0.8f)
                hint = new TextObject("{=BC_ClaimFeud_PetitionHint_LowIntensity}Feud intensity must reach 80% before you can petition the ruler.");
            else if (!isPetitionableState)
                hint = new TextObject("{=BC_ClaimFeud_PetitionHint_Pending}The dispute is already awaiting judgment or a response from one of the parties.");
            else
                hint = new TextObject("{=BC_ClaimFeud_PetitionHint_Ready}Bring this dispute before the ruler for legal judgment.");

            PetitionHint = new HintViewModel(hint);
        }


        private static IEnumerable<Clan> ResolveSupporters(string supporterIds, Clan leader)
        {
            foreach (Clan clan in BuildSide(supporterIds, leader))
            {
                if (clan != leader)
                    yield return clan;
            }
        }

        private static List<Clan> BuildSide(string supporterIds, Clan leader)
        {
            List<Clan> clans = new List<Clan>();
            if (leader != null)
                clans.Add(leader);

            foreach (string clanId in SplitIds(supporterIds))
            {
                Clan clan = ResolveClan(clanId);
                if (clan != null && !clans.Contains(clan))
                    clans.Add(clan);
            }

            return clans;
        }

        private static IEnumerable<string> SplitIds(string ids)
        {
            return string.IsNullOrWhiteSpace(ids)
                ? Enumerable.Empty<string>()
                : ids.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim());
        }

        private static Clan ResolveClan(string clanId)
        {
            return string.IsNullOrWhiteSpace(clanId)
                ? null
                : Clan.All?.FirstOrDefault(clan => clan != null && clan.StringId == clanId);
        }

        private static TextObject FormatTitle(FeudalTitleRecord title, Clan styleClan)
        {
            string text = styleClan == null
                ? FeudalTitleDisplayHelper.FormatTitleName(title)
                : FeudalTitleDisplayHelper.FormatTitleName(title, styleClan);
            return new TextObject("{=!}" + (string.IsNullOrWhiteSpace(text) ? title?.TitleId ?? "?" : text));
        }

        [DataSourceProperty] public string Name { get => _name; set { if (value != _name) { _name = value; OnPropertyChangedWithValue(value, nameof(Name)); } } }
        [DataSourceProperty] public string FoundingDate { get => _foundingDate; set { if (value != _foundingDate) { _foundingDate = value; OnPropertyChangedWithValue(value, nameof(FoundingDate)); } } }
        [DataSourceProperty] public string Demands { get => _demands; set { if (value != _demands) { _demands = value; OnPropertyChangedWithValue(value, nameof(Demands)); } } }
        [DataSourceProperty] public string ClaimantSideText { get => _claimantSideText; set { if (value != _claimantSideText) { _claimantSideText = value; OnPropertyChangedWithValue(value, nameof(ClaimantSideText)); } } }
        [DataSourceProperty] public string HolderSideText { get => _holderSideText; set { if (value != _holderSideText) { _holderSideText = value; OnPropertyChangedWithValue(value, nameof(HolderSideText)); } } }
        [DataSourceProperty] public string ClaimantLabel { get => _claimantLabel; set { if (value != _claimantLabel) { _claimantLabel = value; OnPropertyChangedWithValue(value, nameof(ClaimantLabel)); } } }
        [DataSourceProperty] public string HolderLabel { get => _holderLabel; set { if (value != _holderLabel) { _holderLabel = value; OnPropertyChangedWithValue(value, nameof(HolderLabel)); } } }
        [DataSourceProperty] public string FeudIntensityText { get => _feudIntensityText; set { if (value != _feudIntensityText) { _feudIntensityText = value; OnPropertyChangedWithValue(value, nameof(FeudIntensityText)); } } }
        [DataSourceProperty] public string BalanceOfPowerText { get => _balanceOfPowerText; set { if (value != _balanceOfPowerText) { _balanceOfPowerText = value; OnPropertyChangedWithValue(value, nameof(BalanceOfPowerText)); } } }
        [DataSourceProperty] public Color BalanceOfPowerColor { get => _balanceOfPowerColor; set { if (value != _balanceOfPowerColor) { _balanceOfPowerColor = value; OnPropertyChangedWithValue(value, nameof(BalanceOfPowerColor)); } } }
        [DataSourceProperty] public bool IsSelected { get => _isSelected; set { if (value != _isSelected) { _isSelected = value; OnPropertyChangedWithValue(value, nameof(IsSelected)); } } }
        [DataSourceProperty] public bool CanPetition { get => _canPetition; set { if (value != _canPetition) { _canPetition = value; OnPropertyChangedWithValue(value, nameof(CanPetition)); } } }
        [DataSourceProperty] public HintViewModel PetitionHint { get => _petitionHint; set { if (value != _petitionHint) { _petitionHint = value; OnPropertyChangedWithValue(value, nameof(PetitionHint)); } } }
        [DataSourceProperty] public FactionMemberVM ClaimantLeader { get => _claimantLeader; set { if (value != _claimantLeader) { _claimantLeader = value; OnPropertyChangedWithValue(value, nameof(ClaimantLeader)); } } }
        [DataSourceProperty] public FactionMemberVM HolderLeader { get => _holderLeader; set { if (value != _holderLeader) { _holderLeader = value; OnPropertyChangedWithValue(value, nameof(HolderLeader)); } } }
        [DataSourceProperty] public MBBindingList<FactionMemberVM> ClaimantMembers { get => _claimantMembers; set { if (value != _claimantMembers) { _claimantMembers = value; OnPropertyChangedWithValue(value, nameof(ClaimantMembers)); } } }
        [DataSourceProperty] public MBBindingList<FactionMemberVM> HolderMembers { get => _holderMembers; set { if (value != _holderMembers) { _holderMembers = value; OnPropertyChangedWithValue(value, nameof(HolderMembers)); } } }
        [DataSourceProperty] public int ClaimantStrength { get => _claimantStrength; set { if (value != _claimantStrength) { _claimantStrength = value; OnPropertyChangedWithValue(value, nameof(ClaimantStrength)); } } }
        [DataSourceProperty] public int HolderStrength { get => _holderStrength; set { if (value != _holderStrength) { _holderStrength = value; OnPropertyChangedWithValue(value, nameof(HolderStrength)); } } }
        [DataSourceProperty] public int TotalStrength { get => _totalStrength; set { if (value != _totalStrength) { _totalStrength = value; OnPropertyChangedWithValue(value, nameof(TotalStrength)); } } }
        [DataSourceProperty] public string LeaderText => new TextObject("{=BC_UI_Leader}Leader").ToString();
        [DataSourceProperty] public string SupportersText => new TextObject("{=BC_ClaimFeud_Supporters}Supporters").ToString();
        [DataSourceProperty] public string BalanceOfPowerLabel => new TextObject("{=BC_UI_BalanceOfPower}Balance of Power: ").ToString();
        [DataSourceProperty] public string PetitionText => new TextObject("{=BC_ClaimFeud_Petition}Petition").ToString();
    }
}
