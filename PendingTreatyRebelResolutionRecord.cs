using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class PendingTreatyRebelResolutionRecord
    {
        [SaveableField(1)] private string _proposalId;
        [SaveableField(2)] private string _rebelKingdomId;
        [SaveableField(3)] private string _parentKingdomId;
        [SaveableField(4)] private string _beneficiaryKingdomId;

        public string ProposalId => _proposalId;
        public string RebelKingdomId => _rebelKingdomId;
        public string ParentKingdomId => _parentKingdomId;
        public string BeneficiaryKingdomId => _beneficiaryKingdomId;

        public PendingTreatyRebelResolutionRecord(
            string proposalId,
            string rebelKingdomId,
            string parentKingdomId,
            string beneficiaryKingdomId)
        {
            _proposalId = proposalId ?? string.Empty;
            _rebelKingdomId = rebelKingdomId ?? string.Empty;
            _parentKingdomId = parentKingdomId ?? string.Empty;
            _beneficiaryKingdomId = beneficiaryKingdomId ?? string.Empty;
        }
    }
}
