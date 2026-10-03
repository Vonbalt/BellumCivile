using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public enum CourtDynasticResponse { AwaitingApproach, Deferred, Approaching, ReplyReady, OfferIssued, Declined, ForeignRefused }

    public sealed class CourtDynasticRecord
    {
        [SaveableField(1)] public Kingdom Target;
        [SaveableField(2)] public Clan OurHouse;
        [SaveableField(3)] public Clan TheirHouse;
        [SaveableField(4)] public Hero First;
        [SaveableField(5)] public Hero Second;
        [SaveableField(6)] public Clan Destination;
        [SaveableField(7)] public List<Clan> Members = new List<Clan>();
        [SaveableField(8)] public bool Activated;
        [SaveableField(9)] public double ActivatedDay;
        [SaveableField(10)] public CourtDynasticResponse Response;
        [SaveableField(11)] public double ReplyDay;
        [SaveableField(12)] public bool NpcAttempted;
        [SaveableField(13)] public double MarriageDay = -1;
        [SaveableField(14)] public double NextMarriageAttemptDay;
        [SaveableField(15)] public int MarriageTechnicalAttempts;
        [SaveableField(16)] public string MarriageOutcome;
        [SaveableField(17)] public string MarriageBlocker;
        [SaveableField(18)] public bool AllianceAttempted;
        [SaveableField(19)] public double NextAllianceAttemptDay;
        [SaveableField(20)] public int AllianceTechnicalAttempts;
        [SaveableField(21)] public string AllianceOutcome;
        [SaveableField(22)] public string AllianceBlocker;

        internal bool Matches(Hero first, Hero second) => first != null && second != null
            && (First == first && Second == second || First == second && Second == first);
        internal bool InTerm(double now, double deadline) => Activated && CourtAgendaRules.ObjectiveInWindow(now, ActivatedDay, deadline);
    }

    internal static class CourtDynasticRules
    {
        internal const string Kind = "court_royal_marriage";
        internal const float SelectionWeight = .25f;
        internal const float MarriageBonus = 15f;
        internal const float AllianceBonus = 15f;
        internal static bool MayConsider(CourtDynasticResponse response) => response != CourtDynasticResponse.Declined
            && response != CourtDynasticResponse.ForeignRefused && response != CourtDynasticResponse.OfferIssued;
        internal static float AcceptanceBonus(bool player, bool aligned, bool active) => !player && aligned && active ? MarriageBonus : 0;
        internal static float Support(float natural, bool eligible) => natural + (eligible ? AllianceBonus : 0);
    }
}
