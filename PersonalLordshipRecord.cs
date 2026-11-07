using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.SaveSystem;

namespace BellumCivile
{
    public sealed class PersonalLordshipRecord
    {
        [SaveableField(1)] public Settlement Village;
        [SaveableField(2)] public Clan GrantingClan;
        [SaveableField(3)] public Hero Holder;
        [SaveableField(4)] public float GrantedDay;
        [SaveableField(5)] public bool RewardApplied;
        [SaveableField(6)] public Settlement Parent;
        [SaveableField(7)] public bool IsVassalGrant;
        [SaveableField(8)] public Clan BeneficiaryClan;
        [SaveableField(9)] public Hero Grantor;
        [SaveableField(10)] public List<Hero> WelcomedNotables = new List<Hero>();

        public bool HasValidEstate => Village?.Village?.Bound == Parent && Parent?.OwnerClan == GrantingClan
            && GrantingClan != null && !GrantingClan.IsEliminated
            && Holder?.IsAlive == true && !Holder.IsDisabled
            && (IsVassalGrant ? BeneficiaryClan != null && !BeneficiaryClan.IsEliminated && Holder.Clan == BeneficiaryClan
                : Holder.Clan == GrantingClan && Holder != GrantingClan.Leader);

        public bool IsValid => HasValidEstate && (!IsVassalGrant
            || Behaviors.LordshipBehavior.ShareLordshipRealm(GrantingClan, BeneficiaryClan));
    }

    public sealed class LordshipIncomeCredit
    {
        [SaveableField(1)] public Clan Recipient;
        [SaveableField(2)] public Clan Source;
        [SaveableField(3)] public Settlement Village;
        [SaveableField(4)] public int Amount;
    }

    public sealed class LordshipAppointmentRecord
    {
        [SaveableField(1)] public Hero Hero;
        [SaveableField(2)] public MobileParty Party;
        [SaveableField(3)] public Town Town;
        [SaveableField(4)] public bool PlayerTransition;
        [SaveableField(5)] public float Deadline = -1f;
        [SaveableField(6)] public bool Retirement;
        [SaveableField(7)] public Clan OwningClan;

        public bool IsCurrent => Hero?.IsAlive == true && OwningClan != null && Hero.Clan == OwningClan && (Party != null
            ? Party.IsLordParty && Party.LeaderHero == Hero && Hero.PartyBelongedTo == Party && Party.ActualClan == OwningClan
            : Town != null && Town.Governor == Hero && Town.OwnerClan == OwningClan);
    }

    public sealed class LordshipCommandTransferRecord
    {
        [SaveableField(1)] public MobileParty Party;
        [SaveableField(2)] public Clan Clan;
        [SaveableField(3)] public Hero FormerLeader;
        [SaveableField(4)] public Hero Replacement;
    }
}
