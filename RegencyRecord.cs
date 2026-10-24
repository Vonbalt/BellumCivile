using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class RegencyRecord
    {
        [SaveableField(1)] private string _clanId;
        [SaveableField(2)] private string _wardHeroId;
        [SaveableField(3)] private string _regentHeroId;
        [SaveableField(4)] private string _predecessorHeroId;
        [SaveableField(5)] private CampaignTime _startedAt;
        [SaveableField(6)] private bool _regentWasGenerated;
        [SaveableField(7)] private string _adulthoodPreparedWardId;
        [SaveableField(8)] private string _adulthoodFailure;

        public string ClanId => _clanId ?? string.Empty;
        public string WardHeroId => _wardHeroId ?? string.Empty;
        public string RegentHeroId => _regentHeroId ?? string.Empty;
        public string PredecessorHeroId => _predecessorHeroId ?? string.Empty;
        public CampaignTime StartedAt => _startedAt;
        public bool RegentWasGenerated => _regentWasGenerated;
        internal string AdulthoodFailure => _adulthoodFailure;
        internal bool IsAdulthoodPrepared(Hero ward) => ward != null && _adulthoodPreparedWardId == ward.StringId;
        internal void BeginAdulthood() => _adulthoodFailure = "Native adulthood event did not complete";
        internal void CompleteAdulthood(Hero ward)
        {
            _adulthoodPreparedWardId = ward.StringId;
            _adulthoodFailure = null;
        }
        internal void FailAdulthood(string failure) => _adulthoodFailure = failure;

        public RegencyRecord(
            string clanId,
            string wardHeroId,
            string regentHeroId,
            string predecessorHeroId,
            CampaignTime startedAt,
            bool regentWasGenerated)
        {
            _clanId = clanId ?? string.Empty;
            _wardHeroId = wardHeroId ?? string.Empty;
            _regentHeroId = regentHeroId ?? string.Empty;
            _predecessorHeroId = predecessorHeroId ?? string.Empty;
            _startedAt = startedAt;
            _regentWasGenerated = regentWasGenerated;
        }

        public void ReplaceWard(Hero ward)
        {
            _wardHeroId = ward?.StringId ?? string.Empty;
            _adulthoodPreparedWardId = null;
            _adulthoodFailure = null;
        }

        public void ReplaceRegent(Hero regent, bool wasGenerated)
        {
            _regentHeroId = regent?.StringId ?? string.Empty;
            _regentWasGenerated = wasGenerated;
        }
    }
}
