using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public enum CourtTitleResponse { Selected, AwaitingPlayer, Deferred, Refused, Accepted, Delivered, Failed }
    public sealed class CourtTitleGrantRecord
    {
        [SaveableField(1)] public Kingdom Realm;
        [SaveableField(2)] public string TitleId;
        [SaveableField(3)] public Clan Grantor;
        [SaveableField(4)] public Hero Ruler;
        [SaveableField(5)] public Clan Recipient;
        [SaveableField(6)] public Hero Beneficiary;
        [SaveableField(7)] public string OldLegal;
        [SaveableField(8)] public string OldPractical;
        [SaveableField(9)] public bool Legal;
        [SaveableField(10)] public bool Practical;
        [SaveableField(11)] public double Deadline;
        [SaveableField(12)] public CourtTitleResponse Response;
        [SaveableField(13)] public float Acceptance;
        [SaveableField(14)] public bool PaymentAttempted;
        [SaveableField(15)] public bool Paid;
        [SaveableField(16)] public bool TransferAttempted;
        [SaveableField(17)] public bool TransferComplete;
        [SaveableField(18)] public bool RelationApplied;
        [SaveableField(19)] public bool MoodApplied;
        [SaveableField(20)] public bool Reported;
        [SaveableField(21)] public int Attempts;
        [SaveableField(22)] public double RetryDay;
        [SaveableField(23)] public int RelationGain;
        [SaveableField(24)] public FactionObject RewardFaction;
        [SaveableField(25)] public bool Refunded;
        [SaveableField(26)] public double RecoveryUntil;
        [SaveableField(27)] public float ApprovalGranted;
    }
    internal static class CourtTitleGrantRules
    {
        internal const string Grant = "court_bestow_title", Petition = "court_petition_title";
        internal static float Score(int rank, int claim, int relation, int generosity, int honor, bool favored, bool practical, bool full) =>
            Math.Max(0, Math.Min(100, 40 + claim + Math.Max(-15, Math.Min(15, relation * .15f))
                + generosity * 8 + honor * 5 + (favored ? 15 : 0) - 10 * (rank - 1) * (practical ? 1 : .5f) - (full ? 5 : 0)));
        internal static int Reward(int rank, bool full) => 15 + 5 * rank + (full ? 5 : 0);
        internal static bool Delivered(CourtTitleGrantRecord p, string legal, string practical) => p?.Recipient != null
            && legal == (p.Legal ? p.Recipient.StringId : p.OldLegal)
            && practical == (p.Practical ? p.Recipient.StringId : p.OldPractical);
        internal static bool Recoverable(CourtTitleGrantRecord p, string legal, string practical) =>
            (legal == p.OldLegal || p.Legal && legal == p.Recipient.StringId)
            && (practical == p.OldPractical || p.Practical && practical == p.Recipient.StringId);
    }
}
