using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public class FeudalClaimFabricationRecord
    {
        [SaveableField(1)] private string _fabricationId;
        [SaveableField(2)] private string _fabricatorHeroId;
        [SaveableField(3)] private string _fabricatorClanId;
        [SaveableField(4)] private string _targetTitleId;
        [SaveableField(5)] private FeudalClaimFabricationTrack _track;
        [SaveableField(6)] private float _progress;
        [SaveableField(7)] private float _dailyProgressDelta;
        [SaveableField(8)] private float _startedDay;
        [SaveableField(9)] private int _paidGoldCost;
        [SaveableField(10)] private float _paidInfluenceCost;
        [SaveableField(11)] private bool _isActive;
        [SaveableField(12)] private bool _hasPassed33;
        [SaveableField(13)] private bool _hasPassed66;
        [SaveableField(14)] private bool _hasPassed100;
        // Retained only so saves written by the abandoned cover-up prototype remain readable.
        [SaveableField(15)] private bool _legacyIsPaused;
        [SaveableField(16)] private int _failedStage;
        [SaveableField(17)] private int _pendingSetbackStage;
        [SaveableField(18)] private int _setbackGoldCost;
        [SaveableField(19)] private int _reworkStage;
        [SaveableField(20)] private int _resolvedSetbackMask;

        public int PendingSetbackStage => _pendingSetbackStage;
        public int SetbackGoldCost => _setbackGoldCost;
        public int ReworkStage => _reworkStage;
        public bool AwaitingSetback => _pendingSetbackStage != 0;

        public void BeginSetback(int stage)
        {
            if (!IsActive || AwaitingSetback || stage < 1 || stage > 2) return;
            SetProgress(stage == 1 ? 0.33f : 0.66f);
            _pendingSetbackStage = stage;
            _setbackGoldCost = _paidGoldCost / 4;
        }

        public bool ResolveSetback(bool paid)
        {
            if (!IsActive || !AwaitingSetback) return false;
            int stage = _pendingSetbackStage;
            _resolvedSetbackMask |= 1 << (stage - 1);
            _pendingSetbackStage = 0;
            if (!paid)
            {
                SetProgress(stage == 1 ? 0f : 0.33f);
                _reworkStage = stage;
            }
            return true;
        }

        public bool DidStageFail(int stage) => _failedStage == stage;
        public void MarkStageFailed(int stage) { _failedStage = stage; }

        public string FabricationId => _fabricationId;
        public string FabricatorHeroId => _fabricatorHeroId;
        public string FabricatorClanId => _fabricatorClanId;
        public string TargetTitleId => _targetTitleId;
        public FeudalClaimFabricationTrack Track => _track;
        public float Progress => _progress;
        public float DailyProgressDelta => _dailyProgressDelta;
        public float StartedDay => _startedDay;
        public int PaidGoldCost => _paidGoldCost;
        public float PaidInfluenceCost => _paidInfluenceCost;
        public bool IsActive => _isActive;
        public bool HasPassed33 => _hasPassed33 || (_resolvedSetbackMask & 1) != 0;
        public bool HasPassed66 => _hasPassed66 || (_resolvedSetbackMask & 2) != 0;
        public bool HasPassed100 => _hasPassed100;

        public FeudalClaimFabricationRecord(
            string fabricationId,
            string fabricatorHeroId,
            string fabricatorClanId,
            string targetTitleId,
            FeudalClaimFabricationTrack track,
            float progress,
            float dailyProgressDelta,
            float startedDay,
            int paidGoldCost,
            float paidInfluenceCost,
            bool isActive = true)
        {
            _fabricationId = fabricationId ?? string.Empty;
            _fabricatorHeroId = fabricatorHeroId ?? string.Empty;
            _fabricatorClanId = fabricatorClanId ?? string.Empty;
            _targetTitleId = targetTitleId ?? string.Empty;
            _track = track;
            _progress = progress;
            _dailyProgressDelta = dailyProgressDelta;
            _startedDay = startedDay;
            _paidGoldCost = paidGoldCost;
            _paidInfluenceCost = paidInfluenceCost;
            _isActive = isActive;
        }

        public void AddProgress(float delta)
        {
            if (AwaitingSetback) return;
            SetProgress(_progress + delta);
        }

        public void SetProgress(float progress)
        {
            _progress = progress;
            if (_progress > 1f)
                _progress = 1f;
            if (_progress < 0f)
                _progress = 0f;

            ResetMilestonesBelowProgress();
            if (_reworkStage != 0 && _progress >= (_reworkStage == 1 ? 0.33f : 0.66f))
                _reworkStage = 0;
        }

        public void SetActive(bool active)
        {
            _isActive = active;
        }

        public void ClearLegacyPauseState()
        {
            if (_legacyIsPaused)
                _legacyIsPaused = false;
        }

        public void SetDailyProgressDelta(float dailyProgressDelta)
        {
            _dailyProgressDelta = dailyProgressDelta < 0f ? 0f : dailyProgressDelta;
        }

        public void MarkMilestonePassed(float threshold)
        {
            if (threshold <= 0.33f)
                _hasPassed33 = true;
            else if (threshold <= 0.66f)
                _hasPassed66 = true;
            else if (threshold <= 1f)
                _hasPassed100 = true;
        }

        private void ResetMilestonesBelowProgress()
        {
            if (_progress < 0.33f)
                _hasPassed33 = false;
            if (_progress < 0.66f)
                _hasPassed66 = false;
            if (_progress < 1f)
                _hasPassed100 = false;
        }
    }
}
