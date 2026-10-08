using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class PrivyCouncilOfficeRecord
    {
        [SaveableField(1)] private string _recordId;
        [SaveableField(2)] private string _kingdomId;
        [SaveableField(3)] private PrivyCouncilOffice _office;
        [SaveableField(4)] private string _holderClanId;
        [SaveableField(5)] private float _controversy;
        [SaveableField(6)] private float _vacancyStartedDay;
        [SaveableField(7)] private float _appointedDay;
        [SaveableField(8)] private float _lastUpdatedDay;
        [SaveableField(9)] private bool _isInitialized;
        [SaveableField(10)] private bool _legacyControversyMigrated;
        [SaveableField(11)] private string _lastReason;
        [SaveableField(12)] private float _lastChange;
        [SaveableField(13)] private float _lastRealmMetric;
        [SaveableField(14)] private string _assignmentId;
        [SaveableField(15)] private float _lastAssignmentChangedDay;
        [SaveableField(16)] private float _nextAssignmentReviewDay;
        // Retained for saves made before captivity stopped vacating offices.
        [SaveableField(17)] private bool _captivityVacancy;

        public bool IsCaptivityVacancy => _captivityVacancy && string.IsNullOrEmpty(_holderClanId);

        public string RecordId => _recordId;
        public string KingdomId => _kingdomId;
        public PrivyCouncilOffice Office => _office;
        public string HolderClanId => _holderClanId;
        public float Controversy => _controversy;
        public float VacancyStartedDay => _vacancyStartedDay;
        public float AppointedDay => _appointedDay;
        public float LastUpdatedDay => _lastUpdatedDay;
        public bool IsInitialized => _isInitialized;
        public bool LegacyControversyMigrated => _legacyControversyMigrated;
        public string LastReason => _lastReason;
        public float LastChange => _lastChange;
        public float LastRealmMetric => _lastRealmMetric;
        public string AssignmentId => _assignmentId;
        public float LastAssignmentChangedDay => _lastAssignmentChangedDay;
        public float NextAssignmentReviewDay => _nextAssignmentReviewDay;

        public PrivyCouncilOfficeRecord(string kingdomId, PrivyCouncilOffice office, float currentDay)
        {
            _kingdomId = kingdomId ?? string.Empty;
            _office = office;
            _recordId = BuildRecordId(_kingdomId, office);
            _holderClanId = string.Empty;
            _controversy = 0f;
            _vacancyStartedDay = currentDay;
            _appointedDay = -1f;
            _lastUpdatedDay = currentDay;
            _isInitialized = false;
            _legacyControversyMigrated = false;
            _lastReason = string.Empty;
            _lastChange = 0f;
            _lastRealmMetric = -1f;
            _assignmentId = string.Empty;
            _lastAssignmentChangedDay = -1f;
            _nextAssignmentReviewDay = -1f;
        }

        public static string BuildRecordId(string kingdomId, PrivyCouncilOffice office)
        {
            return (kingdomId ?? string.Empty) + ":" + office;
        }

        public void Initialize(string holderClanId, float currentDay)
        {
            _holderClanId = holderClanId ?? string.Empty;
            _appointedDay = string.IsNullOrEmpty(_holderClanId) ? -1f : currentDay;
            _vacancyStartedDay = string.IsNullOrEmpty(_holderClanId) ? currentDay : -1f;
            _lastUpdatedDay = currentDay;
            _isInitialized = true;
        }

        public void Appoint(string holderClanId, float currentDay)
        {
            if (!string.IsNullOrEmpty(_holderClanId) && _holderClanId == holderClanId)
                return;

            _holderClanId = holderClanId ?? string.Empty;
            _captivityVacancy = false;
            _appointedDay = currentDay;
            _vacancyStartedDay = -1f;
            _lastAssignmentChangedDay = -1f;
            _nextAssignmentReviewDay = -1f;
            _lastUpdatedDay = currentDay;
            _isInitialized = true;
            _controversy = 0f;
            _lastReason = string.Empty;
            _lastChange = 0f;
        }

        public void Vacate(float currentDay)
        {
            if (string.IsNullOrEmpty(_holderClanId) && _vacancyStartedDay >= 0f)
                return;

            _holderClanId = string.Empty;
            _appointedDay = -1f;
            _vacancyStartedDay = currentDay;
            _lastUpdatedDay = currentDay;
            _isInitialized = true;
        }

        public void ChangeControversy(float amount, string reason, float currentDay)
        {
            float previous = _controversy;
            _controversy = Clamp(_controversy + amount, 0f, 100f);
            _lastChange = _controversy - previous;
            _lastReason = reason ?? string.Empty;
            _lastUpdatedDay = currentDay;
        }

        public void SetControversy(float value, string reason, float currentDay)
        {
            float previous = _controversy;
            _controversy = Clamp(value, 0f, 100f);
            _lastChange = _controversy - previous;
            _lastReason = reason ?? string.Empty;
            _lastUpdatedDay = currentDay;
        }

        public void MarkLegacyControversyMigrated()
        {
            _legacyControversyMigrated = true;
        }

        public void SetLastRealmMetric(float value)
        {
            _lastRealmMetric = value;
        }

        public void Touch(float currentDay)
        {
            _lastUpdatedDay = currentDay;
        }

        public void EnsureAssignment(string assignmentId, float lastChangedDay)
        {
            if (!string.IsNullOrEmpty(_assignmentId))
                return;

            _assignmentId = assignmentId ?? string.Empty;
            _lastAssignmentChangedDay = lastChangedDay;
        }

        public void RepairAssignment(string assignmentId, float lastChangedDay, float currentDay)
        {
            _assignmentId = assignmentId ?? string.Empty;
            _lastAssignmentChangedDay = lastChangedDay;
            _lastUpdatedDay = currentDay;
        }

        public void SetAssignment(string assignmentId, float currentDay)
        {
            _assignmentId = assignmentId ?? string.Empty;
            _lastAssignmentChangedDay = currentDay;
            _lastUpdatedDay = currentDay;
        }

        public void ScheduleAssignmentReview(float reviewDay)
        {
            _nextAssignmentReviewDay = reviewDay;
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            if (value < minimum) return minimum;
            if (value > maximum) return maximum;
            return value;
        }
    }
}
