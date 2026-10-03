using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public class FeudalServiceRecord
    {
        [SaveableField(1)] private string _recordId;
        [SaveableField(2)] private string _childTitleId;
        [SaveableField(3)] private string _parentTitleId;
        [SaveableField(4)] private FeudalServiceLevel _level;
        [SaveableField(5)] private float _lastChangedDay;
        [SaveableField(6)] private string _changedByClanId;
        [SaveableField(7)] private string _reason;

        public string RecordId => _recordId;
        public string ChildTitleId => _childTitleId;
        public string ParentTitleId => _parentTitleId;
        public FeudalServiceLevel Level => _level;
        public float LastChangedDay => _lastChangedDay;
        public string ChangedByClanId => _changedByClanId;
        public string Reason => _reason;

        public FeudalServiceRecord(
            string recordId,
            string childTitleId,
            string parentTitleId,
            FeudalServiceLevel level,
            float lastChangedDay,
            string changedByClanId,
            string reason)
        {
            _recordId = recordId ?? string.Empty;
            _childTitleId = childTitleId ?? string.Empty;
            _parentTitleId = parentTitleId ?? string.Empty;
            _level = level;
            _lastChangedDay = lastChangedDay;
            _changedByClanId = changedByClanId ?? string.Empty;
            _reason = reason ?? string.Empty;
        }

        public void Update(FeudalServiceLevel level, float day, string changedByClanId, string reason)
        {
            _level = level;
            _lastChangedDay = day;
            _changedByClanId = changedByClanId ?? string.Empty;
            _reason = reason ?? string.Empty;
        }
    }
}
