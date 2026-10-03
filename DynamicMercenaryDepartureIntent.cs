using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    internal enum DynamicMercenaryDepartureResolution
    {
        Undecided = 0,
        Supported = 1,
        Blessed = 2,
        ForbiddenStay = 3,
        ForbiddenLeave = 4,
        AskedStay = 5,
        AskedLeave = 6
    }

    public sealed class DynamicMercenaryDepartureIntent
    {
        [SaveableField(1)] private string _heroId;
        [SaveableField(2)] private CampaignTime _queuedAt;
        [SaveableField(3)] private float _obedienceRoll;
        [SaveableField(4)] private int _resolution;

        public string HeroId => _heroId ?? string.Empty;
        public CampaignTime QueuedAt => _queuedAt;
        public float ObedienceRoll => _obedienceRoll;
        internal DynamicMercenaryDepartureResolution Resolution =>
            (DynamicMercenaryDepartureResolution)_resolution;

        public DynamicMercenaryDepartureIntent()
        {
        }

        public DynamicMercenaryDepartureIntent(string heroId, CampaignTime queuedAt, float obedienceRoll)
        {
            _heroId = heroId ?? string.Empty;
            _queuedAt = queuedAt;
            _obedienceRoll = obedienceRoll;
            _resolution = (int)DynamicMercenaryDepartureResolution.Undecided;
        }

        internal void SetResolution(DynamicMercenaryDepartureResolution resolution)
        {
            _resolution = (int)resolution;
        }
    }
}
