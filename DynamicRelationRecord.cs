using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public class DynamicRelationRecord
    {
        [SaveableField(1)] private int _lastRecordedValue;
        [SaveableField(2)] private float _lastUpdateDay;

        public int LastRecordedValue
        {
            get => _lastRecordedValue;
            set => _lastRecordedValue = value;
        }

        public float LastUpdateDay
        {
            get => _lastUpdateDay;
            set => _lastUpdateDay = value;
        }

        public DynamicRelationRecord(int lastRecordedValue, float lastUpdateDay)
        {
            _lastRecordedValue = lastRecordedValue;
            _lastUpdateDay = lastUpdateDay;
        }
    }
}
