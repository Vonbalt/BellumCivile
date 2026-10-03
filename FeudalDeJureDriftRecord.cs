using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public class FeudalDeJureDriftRecord
    {
        [SaveableField(1)] private string _titleId;
        [SaveableField(2)] private string _originalParentTitleId;
        [SaveableField(3)] private string _targetParentTitleId;
        [SaveableField(4)] private string _targetKingdomId;
        [SaveableField(5)] private float _progress;
        [SaveableField(6)] private float _startedDay;
        [SaveableField(7)] private float _lastEvaluatedDay;
        [SaveableField(8)] private FeudalDeJureDriftState _state;
        [SaveableField(9)] private bool _isActive;

        public string TitleId => _titleId;
        public string OriginalParentTitleId => _originalParentTitleId;
        public string TargetParentTitleId => _targetParentTitleId;
        public string TargetKingdomId => _targetKingdomId;
        public float Progress => _progress;
        public float StartedDay => _startedDay;
        public float LastEvaluatedDay => _lastEvaluatedDay;
        public FeudalDeJureDriftState State => _state;
        public bool IsActive => _isActive;

        public FeudalDeJureDriftRecord(
            string titleId,
            string originalParentTitleId,
            string targetParentTitleId,
            string targetKingdomId,
            float progress,
            float startedDay,
            float lastEvaluatedDay,
            FeudalDeJureDriftState state = FeudalDeJureDriftState.Advancing,
            bool isActive = true)
        {
            _titleId = titleId ?? string.Empty;
            _originalParentTitleId = originalParentTitleId ?? string.Empty;
            _targetParentTitleId = targetParentTitleId ?? string.Empty;
            _targetKingdomId = targetKingdomId ?? string.Empty;
            SetProgress(progress);
            _startedDay = startedDay;
            _lastEvaluatedDay = lastEvaluatedDay;
            _state = state;
            _isActive = isActive;
        }

        public void SetProgress(float progress)
        {
            _progress = progress < 0f ? 0f : progress > 1f ? 1f : progress;
        }

        public void AddProgress(float delta)
        {
            SetProgress(_progress + delta);
        }

        public void MarkEvaluated(float day)
        {
            _lastEvaluatedDay = day;
        }

        public void UpdateParents(string originalParentTitleId, string targetParentTitleId)
        {
            _originalParentTitleId = originalParentTitleId ?? string.Empty;
            _targetParentTitleId = targetParentTitleId ?? string.Empty;
        }

        public void SetState(FeudalDeJureDriftState state)
        {
            _state = state;
        }

        public void Retarget(string originalParentTitleId, string targetParentTitleId, string targetKingdomId, float day)
        {
            _originalParentTitleId = originalParentTitleId ?? string.Empty;
            _targetParentTitleId = targetParentTitleId ?? string.Empty;
            _targetKingdomId = targetKingdomId ?? string.Empty;
            _progress = 0f;
            _startedDay = day;
            _lastEvaluatedDay = day;
            _state = FeudalDeJureDriftState.Advancing;
            _isActive = true;
        }

        public void SetActive(bool active)
        {
            _isActive = active;
        }
    }
}
