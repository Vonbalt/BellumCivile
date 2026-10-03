using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public class FeudalUsurpationCandidateRecord
    {
        [SaveableField(1)] private string _titleId;
        [SaveableField(2)] private string _claimantClanId;

        public string TitleId => _titleId;
        public string ClaimantClanId => _claimantClanId;

        public FeudalUsurpationCandidateRecord(string titleId, string claimantClanId)
        {
            _titleId = titleId ?? string.Empty;
            _claimantClanId = claimantClanId ?? string.Empty;
        }
    }
}
