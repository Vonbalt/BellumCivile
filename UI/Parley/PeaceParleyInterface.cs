using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.ScreenSystem;
using TaleWorlds.TwoDimension;

namespace BellumCivile.UI.Parley
{
    /// <summary>
    /// A campaign-screen overlay rather than a separate screen. This keeps a parley available from both
    /// the diplomacy panel and map notifications without disrupting the player's current campaign context.
    /// </summary>
    public sealed class PeaceParleyInterface
    {
        private GauntletLayer _layer;
        private GauntletMovieIdentifier _movie;
        private ScreenBase _screen;
        private PeaceParleyVM _viewModel;
        private SpriteCategory _barterCategory;
        private SpriteCategory _kingdomCategory;
        private Action _onClosed;
        private CampaignTimeControlMode? _previousTimeControlMode;

        public static PeaceParleyInterface Instance { get; } = new PeaceParleyInterface();

        public bool IsShown => _layer != null;

        public bool Show(TreatyProposalRecord proposal, Action onClosed = null)
        {
            if (proposal == null || _layer != null || ScreenManager.TopScreen == null)
                return false;

            _screen = ScreenManager.TopScreen;
            _onClosed = onClosed;
            _viewModel = new PeaceParleyVM(proposal, Close);
            _layer = new GauntletLayer("BellumPeaceParleyLayer", 240);
            _layer.InputRestrictions.SetInputRestrictions();
            _layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericCampaignPanelsGameKeyCategory"));
            _layer.IsFocusLayer = true;
            ScreenManager.TrySetFocus(_layer);
            _screen.AddLayer(_layer);
            _barterCategory = UIResourceManager.LoadSpriteCategory("ui_barter");
            _kingdomCategory = UIResourceManager.LoadSpriteCategory("ui_kingdom");
            _movie = _layer.LoadMovie("BellumPeaceParley", _viewModel);
            PauseCampaignTime();
            SoundEvent.PlaySound2D("event:/ui/reign/vote");
            return true;
        }

        private void Close()
        {
            if (_layer == null)
                return;

            if (_movie != null)
                _layer.ReleaseMovie(_movie);

            // Sprite categories are global resources shared with the screen below this overlay.
            // Unloading ui_kingdom here strips the still-open Kingdom/Diplomacy movie until it is rebuilt.
            _screen?.RemoveLayer(_layer);

            Action onClosed = _onClosed;
            RestoreCampaignTime();

            _movie = null;
            _barterCategory = null;
            _kingdomCategory = null;
            _layer = null;
            _viewModel = null;
            _screen = null;
            _onClosed = null;

            SoundEvent.PlaySound2D("event:/ui/reign/decision");
            try
            {
                onClosed?.Invoke();
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Peace parley close callback failed: {ex}");
            }
        }

        private void PauseCampaignTime()
        {
            Campaign campaign = Campaign.Current;
            if (campaign == null)
                return;

            _previousTimeControlMode = campaign.TimeControlMode;
            campaign.SetTimeSpeed(0);
        }

        private void RestoreCampaignTime()
        {
            Campaign campaign = Campaign.Current;
            if (campaign != null && _previousTimeControlMode.HasValue)
                campaign.TimeControlMode = _previousTimeControlMode.Value;
            _previousTimeControlMode = null;
        }
    }
}
