using System;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    // Only new offers receive a record. Older pending offers retain their native terms.
    public sealed class PlayerMarriageAgreement
    {
        [SaveableField(1)] public Hero Player;
        [SaveableField(2)] public Hero Other;
        [SaveableField(3)] public Clan PlayerHouse;
        [SaveableField(4)] public Clan OtherHouse;
        [SaveableField(5)] public Clan Destination;
        [SaveableField(6)] public bool Negotiated;
        [SaveableField(7)] public bool Accepted;
        [SaveableField(8)] public int Dowry;
        [SaveableField(9)] public bool StandingRequired;
        [SaveableField(10)] public bool DowrySettled;
        // Only the active in-person barter can authorize this exception.
        internal bool RetainerElevationNegotiated { get; set; }

        internal bool Matches(Hero a, Hero b) => Player == a && Other == b || Player == b && Other == a;
        internal Hero Departing => Destination == PlayerHouse ? Other : Player;
        internal Clan SendingHouse => Destination == PlayerHouse ? OtherHouse : PlayerHouse;
        internal bool Matrilineal => (Player.IsFemale ? PlayerHouse : OtherHouse) == Destination;
        internal static bool CanElevateRetainer(Hero noble, Hero retainer) =>
            BellumCivileOptions.EnableBellumStrategicMarriageLogic && noble?.Clan != null && noble.Clan == Clan.PlayerClan
            && retainer?.Clan != null && retainer.Clan != noble.Clan && HouseholdMembership.IsFamily(noble)
            && MarriageParticipantPolicy.IsParticipant(retainer) && HouseholdMembership.IsRetainer(retainer);

        internal bool AllowsRetainerElevation(Hero first, Hero second) => RetainerElevationNegotiated && Negotiated
            && Matches(first, second) && Destination == PlayerHouse && Player.Clan == PlayerHouse && Other.Clan == OtherHouse
            && CanElevateRetainer(Player, Other);
        internal static PlayerMarriageAgreement Create(Hero player, Hero other, Clan destination, bool negotiated)
            => new PlayerMarriageAgreement { Player = player, Other = other, PlayerHouse = player.Clan,
                OtherHouse = other.Clan, Destination = destination, Negotiated = negotiated, StandingRequired = true };

        internal static bool CanChoose(Hero player, Hero other, Clan destination, bool negotiated,
            MarriageHouseholdPolicy policy, out TextObject reason, bool preserveStanding = false)
        {
            reason = null;
            if (player?.Clan == null || other?.Clan == null || player.Clan != Clan.PlayerClan
                || other.Clan == player.Clan || destination == null || destination.IsEliminated
                || player.Clan.IsEliminated || other.Clan.IsEliminated
                || destination != player.Clan && destination != other.Clan
                || player.IsFemale == other.IsFemale || !player.IsAlive || !other.IsAlive
                || player.Spouse != null || other.Spouse != null)
                reason = new TextObject("{=BC_Marriage_AgreementChanged}This marriage can no longer proceed on the agreed terms.");
            else if (!preserveStanding && !MarriageParticipantPolicy.SameMarriageStanding(player, other))
                reason = new TextObject("{=BC_Marriage_Standing}Members of noble families marry within their rank, and retainers marry other retainers.");
            else if (CrownAccessionBehavior.Instance?.IsHouseholdReservedForMarriage(player) == true
                || CrownAccessionBehavior.Instance?.IsHouseholdReservedForMarriage(other) == true)
                reason = new TextObject("{=BC_Marriage_AccessionPending}A pending succession must be settled before this marriage can proceed.");
            else if (player.Clan.IsAtWarWith(other.Clan))
                reason = new TextObject("{=BC_Marriage_AtWar}Our houses cannot conclude this marriage while at war.");
            else
            {
                Hero leaving = destination == player.Clan ? other : player;
                if (leaving == Hero.MainHero || leaving == leaving.Clan.Leader
                    || leaving == MarriageHouseholdPolicy.LegalHead(leaving.Clan))
                    reason = new TextObject("{=BC_Marriage_HeadMustStay}The head of a house cannot leave it through marriage.");
                // Direct negotiations price household loss; automatic offers must preserve continuity.
                else if (leaving == other && !negotiated && policy.IsLastContinuation(other))
                    reason = new TextObject("{=BC_Marriage_LastContinuation}This house cannot give up its last viable continuation of the family line.");
                else if (leaving == other && !negotiated && policy.MustRemain(other))
                    reason = new TextObject("{=BC_Marriage_HeirMustStay}This offer requires the heir to remain in their own house.");
            }
            return reason == null;
        }

        internal bool Validate(out TextObject reason)
        {
            if (Player == null || Other == null || PlayerHouse == null || OtherHouse == null
                || Player.Clan != PlayerHouse || Other.Clan != OtherHouse)
            { reason = new TextObject("{=BC_Marriage_HouseChanged}One of the betrothed has changed houses. A new marriage agreement is required."); return false; }
            return CanChoose(Player, Other, Destination, Negotiated, new MarriageHouseholdPolicy(), out reason,
                Accepted && !StandingRequired || AllowsRetainerElevation(Player, Other));
        }

        internal TextObject HouseholdText() => new TextObject("{=BC_Marriage_HouseholdTerms}{HERO} will leave {SOURCE} and join {HOUSE}.")
            .SetTextVariable("HERO", Departing.Name).SetTextVariable("SOURCE", Destination == PlayerHouse ? OtherHouse.Name : PlayerHouse.Name)
            .SetTextVariable("HOUSE", Destination.Name);
        internal TextObject FormText() => new TextObject(Matrilineal
            ? "{=BC_Marriage_Matrilineal}Matrilineal marriage" : "{=BC_Marriage_Patrilineal}Patrilineal marriage");
        internal TextObject ChildrenText() => new TextObject("{=BC_Marriage_FutureChildren}Future children will belong to {HOUSE}.")
            .SetTextVariable("HOUSE", Destination.Name);
        internal TextObject CrownWarning() => new TextObject("{=BC_Marriage_CrownRights}Crown inheritance rights remain. The heir may later leave to establish a ruling branch of the family.");
    }

    // Ignore only the agreed couple's own reservation during its wedding checks.
    internal sealed class PlayerMarriageValidationScope : IDisposable
    {
        [ThreadStatic] internal static PlayerMarriageAgreement Current;
        private readonly PlayerMarriageAgreement _previous;
        internal PlayerMarriageValidationScope(PlayerMarriageAgreement agreement)
        { _previous = Current; Current = agreement; }
        public void Dispose() { Current = _previous; }
    }
}
