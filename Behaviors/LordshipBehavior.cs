using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class LordshipBehavior : CampaignBehaviorBase
    {
        private List<PersonalLordshipRecord> _grants = new List<PersonalLordshipRecord>();
        private List<LordshipAppointmentRecord> _appointments = new List<LordshipAppointmentRecord>();
        private int _schema;
        private bool _warned;
        private bool _ready;
        private bool _retirementQueued;
        private bool _realmCheckPending;
        private Campaign _campaign;
        private readonly Dictionary<Settlement, PersonalLordshipRecord> _byVillage = new Dictionary<Settlement, PersonalLordshipRecord>();
        private readonly Dictionary<Hero, PersonalLordshipRecord> _byHero = new Dictionary<Hero, PersonalLordshipRecord>();
        private readonly Dictionary<Clan, List<Settlement>> _villages = new Dictionary<Clan, List<Settlement>>();
        private readonly Dictionary<Clan, int> _fiefCounts = new Dictionary<Clan, int>();
        public static LordshipBehavior Instance { get; private set; }
        public int Revision { get; private set; }
        internal bool Ready => _ready && _campaign == Campaign.Current;

        public override void RegisterEvents()
        {
            Instance = this;
            _campaign = Campaign.Current;
            HouseholdMembership.Invalidate();
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, _ => _schema = 1);
            CampaignEvents.OnAfterSessionLaunchedEvent.AddNonSerializedListener(this, OnLoaded);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, AddDialogues);
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, OnClanDay);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, Maintain);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, (s, o, n, p, c, d) =>
            {
                if (n?.Clan != null) _villages.Remove(n.Clan);
                if (p?.Clan != null) _villages.Remove(p.Clan);
                Validate();
            });
            CampaignEvents.OnHeroChangedClanEvent.AddNonSerializedListener(this, (hero, old) => { HouseholdMembership.Invalidate(); Validate(); });
            CampaignEvents.OnClanLeaderChangedEvent.AddNonSerializedListener(this, (old, current) => { HouseholdMembership.Invalidate(); Validate(); });
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, (h, k, d, show) => { HouseholdMembership.Invalidate(); Validate(); });
            CampaignEvents.HeroComesOfAgeEvent.AddNonSerializedListener(this, h => HouseholdMembership.Invalidate(h?.Clan));
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, (c, o, n, d, show) => _realmCheckPending = true);
        }

        public override void SyncData(IDataStore store)
        {
            store.SyncData("BellumCivile_PersonalLordships", ref _grants);
            store.SyncData("BellumCivile_LordshipAppointments", ref _appointments);
            store.SyncData("BellumCivile_LordshipSchema", ref _schema);
            store.SyncData("BellumCivile_LordshipWarning", ref _warned);
            store.SyncData("BellumCivile_LordshipIncomeCredits", ref _incomeCredits);
            _grants = _grants ?? new List<PersonalLordshipRecord>();
            _appointments = _appointments ?? new List<LordshipAppointmentRecord>();
            _incomeCredits = _incomeCredits ?? new List<LordshipIncomeCredit>();
        }

        private void OnLoaded(CampaignGameStarter starter)
        {
            Patches.LordshipAppointmentPatches.Initialize();
            Patches.VassalLordshipPatches.Initialize();
            try { Patches.HouseholdOccupationDisplayPatch.Initialize(); }
            catch (Exception ex) { BellumCivileLogger.Log("Retainer occupation display is unavailable: " + ex); }
            Patches.LordshipMapBannerPatch.Initialize();
            Reindex();
            Validate();
            if (_schema == 0)
            {
                foreach (Clan clan in Clan.All.Where(HouseholdMembership.Supports))
                    foreach (Hero hero in HouseholdMembership.Members(clan).Where(h => h.IsAlive && HouseholdMembership.IsRetainer(h)))
                    {
                        if (GetGrant(hero) != null) continue;
                        MobileParty party = hero.PartyBelongedTo;
                        if (party?.LeaderHero == hero && party.IsLordParty)
                            _appointments.Add(new LordshipAppointmentRecord { Hero = hero, Party = party, OwningClan = clan, PlayerTransition = clan == Clan.PlayerClan });
                        if (hero.GovernorOf != null)
                            _appointments.Add(new LordshipAppointmentRecord { Hero = hero, Town = hero.GovernorOf, OwningClan = clan, PlayerTransition = clan == Clan.PlayerClan });
                    }
                _schema = 1;
            }
            _ready = true;
            _realmCheckPending = true;
            Patches.FeudalTitleHeroNamePatch.InvalidateCache();
            foreach (Clan clan in Clan.All.Where(c => c != Clan.PlayerClan && HouseholdMembership.Supports(c)))
                foreach (Hero hero in HouseholdMembership.Members(clan).Where(h => h.IsAlive
                    && (h.GovernorOf != null || h.PartyBelongedTo?.LeaderHero == h)))
                    EnsureAppointmentGrant(hero);
            foreach (var record in _appointments.Where(a => a?.IsCurrent == true && !a.PlayerTransition && !a.Retirement).ToList())
                EnsureAppointmentGrant(record.Hero);
            Maintain();
        }

        private void Reindex()
        {
            _byVillage.Clear();
            _byHero.Clear();
            foreach (var grant in _grants.Where(IsRetainedGrant))
            {
                if (_byVillage.ContainsKey(grant.Village) || _byHero.ContainsKey(grant.Holder)) continue;
                _byVillage.Add(grant.Village, grant);
                _byHero.Add(grant.Holder, grant);
            }
        }

        private static bool IsRetainedGrant(PersonalLordshipRecord grant) => grant?.HasValidEstate == true;
        public PersonalLordshipRecord GetGrant(Hero hero) => hero != null && _byHero.TryGetValue(hero, out var grant) && IsRetainedGrant(grant) ? grant : null;
        public PersonalLordshipRecord GetGrant(Settlement village) => village != null && _byVillage.TryGetValue(village, out var grant) && IsRetainedGrant(grant) ? grant : null;
        // Saved map notices can be validated before the session rebuilds the runtime indexes.
        internal bool IsRegisteredGrant(PersonalLordshipRecord grant) => grant != null
            && (Ready ? GetGrant(grant.Holder) == grant : _grants.Contains(grant));

        public IEnumerable<Settlement> Villages(Clan clan)
        {
            if (clan == null) return Enumerable.Empty<Settlement>();
            if (!_villages.TryGetValue(clan, out var villages) || !_fiefCounts.TryGetValue(clan, out int count) || count != clan.Fiefs.Count)
            {
                villages = clan.Fiefs.SelectMany(t => t.Settlement.BoundVillages).Select(v => v.Settlement).Distinct().ToList();
                _villages[clan] = villages;
                _fiefCounts[clan] = clan.Fiefs.Count;
            }
            return villages.Where(s => s?.Village?.Bound?.OwnerClan == clan);
        }

        public Settlement FindVacancy(Clan clan) => Villages(clan).FirstOrDefault(v => GetGrant(v) == null
            && FeudalTitleBehavior.Instance?.IsHeldForAllocation(v.Village.Bound, clan) != true);

        internal static TextObject Text(string key, string value) => new TextObject("{=" + key + "}" + value);
        public static TextObject Name(Settlement village) => FeudalTitleDisplayHelper.FormatLordshipName(village);

        internal static TextObject EstateText(string key, string value, Settlement village = null, Clan clan = null)
        {
            var noun = village != null ? FeudalTitleDisplayHelper.GetLordshipNoun(village)
                : FeudalTitleDisplayHelper.GetLordshipNounForClan(clan);
            return Text(key, value).SetTextVariable("LORDSHIP", village != null ? Name(village) : noun)
                .SetTextVariable("LORDSHIP_TYPE", noun).SetTextVariable("VILLAGE", village?.Name ?? TextObject.GetEmpty());
        }

        public bool CanGrant(Clan clan, Settlement village, Hero hero, out TextObject reason)
        {
            reason = null;
            if (!HouseholdMembership.Supports(clan) || village?.Village?.Bound?.OwnerClan != clan)
                reason = Text("BC_Lordship_NotOwner", "Only the holder of the parent barony may make this grant.");
            else if (FeudalTitleBehavior.Instance?.IsHeldForAllocation(village.Village.Bound, clan) == true)
                reason = Text("BC_Lordship_AwaitingAllocation", "This barony is awaiting allocation. Its temporary custodian cannot grant its village revenues.");
            else if (GetGrant(village) != null)
                reason = EstateText("BC_Lordship_Occupied", "{LORDSHIP} already has a holder.", village);
            else if (hero?.Clan != clan || !hero.IsAlive || hero.IsDisabled || hero.IsChild || hero.IsNotable || hero.IsSpecial || hero.IsTemplate
                || hero == clan.Leader || hero == RegencyBehavior.Instance?.GetWard(clan))
                reason = Text("BC_Lordship_InvalidMember", "Choose an eligible adult member of your household.");
            else if (GetGrant(hero) is PersonalLordshipRecord existing)
                reason = EstateText("BC_Lordship_AlreadyHolds", "This character already holds {LORDSHIP}.", existing.Village);
            return reason == null;
        }

        public bool TryGrant(Clan clan, Settlement village, Hero hero, out TextObject reason)
        {
            if (!CanGrant(clan, village, hero, out reason)) return false;
            var grant = new PersonalLordshipRecord { Village = village, Parent = village.Village.Bound, GrantingClan = clan, Holder = hero, GrantedDay = Now, RewardApplied = true };
            _grants.Add(grant);
            _byVillage[village] = grant;
            _byHero[hero] = grant;
            Changed();
            RelationMemoryService.ApplyChange(clan.Leader, hero, 10, clan == Clan.PlayerClan,
                "lordship_granted", 5f, RelationMemoryScope.Personal, village.Name.ToString());
            return true;
        }

        public bool TryRevoke(Clan clan, Settlement village, out TextObject reason)
        {
            var grant = GetGrant(village);
            if (grant == null || grant.GrantingClan != clan || village.Village.Bound.OwnerClan != clan)
            {
                reason = Text("BC_Lordship_NotOwner", "Only the holder of the parent barony may make this grant.");
                return false;
            }
            reason = null;
            Remove(grant);
            RelationMemoryService.ApplyChange(clan.Leader, grant.Holder, -15, clan == Clan.PlayerClan,
                "lordship_revoked", 5f, RelationMemoryScope.Personal, village.Name.ToString());
            return true;
        }

        private void Remove(PersonalLordshipRecord grant)
        {
            _grants.Remove(grant);
            if (grant.Village != null && _byVillage.TryGetValue(grant.Village, out var currentVillage) && currentVillage == grant) _byVillage.Remove(grant.Village);
            if (grant.Holder != null && _byHero.TryGetValue(grant.Holder, out var currentHero) && currentHero == grant) _byHero.Remove(grant.Holder);
            if (!grant.IsVassalGrant && HouseholdMembership.IsRetainer(grant.Holder) && GetGrant(grant.Holder) == null)
            {
                Hero hero = grant.Holder;
                if (hero.GovernorOf != null) QueueRetirement(hero, null, hero.GovernorOf);
                if (hero.PartyBelongedTo?.LeaderHero == hero && hero.PartyBelongedTo.IsLordParty)
                    QueueRetirement(hero, hero.PartyBelongedTo, null);
            }
            Changed();
        }

        private void QueueRetirement(Hero hero, MobileParty party, Town town)
        {
            _appointments.RemoveAll(a => a.Hero == hero && a.Party == party && a.Town == town);
            _appointments.Add(new LordshipAppointmentRecord { Hero = hero, OwningClan = hero.Clan, Party = party, Town = town, Retirement = true, Deadline = Now });
            _retirementQueued = true;
        }

        internal void CheckAppointment(Hero member)
        {
            if (!_ready || !HouseholdMembership.IsRetainer(member) || GetGrant(member) != null) return;
            if (member.Clan != Clan.PlayerClan && (member.GovernorOf != null || member.PartyBelongedTo?.LeaderHero == member)
                && EnsureAppointmentGrant(member)) return;
            if (member.GovernorOf != null && !HasLegacyAppointment(member, town: member.GovernorOf))
                QueueRetirement(member, null, member.GovernorOf);
            if (member.PartyBelongedTo?.LeaderHero == member && member.PartyBelongedTo.IsLordParty
                && !HasLegacyAppointment(member, party: member.PartyBelongedTo))
                QueueRetirement(member, member.PartyBelongedTo, null);
        }

        internal void PruneAppointments() => _appointments.RemoveAll(a => a?.IsCurrent != true);

        private void Validate()
        {
            var villages = new HashSet<Settlement>();
            var heroes = new HashSet<Hero>();
            foreach (var grant in _grants.ToList())
                if (grant == null) _grants.Remove(grant);
                else if (!IsRetainedGrant(grant) || !villages.Add(grant.Village) || !heroes.Add(grant.Holder)) Remove(grant);
        }

        private void Changed()
        {
            Revision++;
            Patches.FeudalTitleHeroNamePatch.InvalidateCache();
        }

        private void OnClanDay(Clan clan)
        {
            if (!_ready || !HouseholdMembership.Supports(clan)) return;
            _villages.Remove(clan);
            HouseholdMembership.Invalidate(clan);
            foreach (Hero member in HouseholdMembership.Members(clan).Where(h => h.IsAlive && HouseholdMembership.IsRetainer(h) && GetGrant(h) == null))
                CheckAppointment(member);
            if (clan == Clan.PlayerClan) return;
            foreach (var record in _appointments.Where(a => a.Hero?.Clan == clan && a.IsCurrent && !a.Retirement).ToList())
                EnsureAppointmentGrant(record.Hero);
            if (clan.Gold <= 0 || FindVacancy(clan) == null) return;
            var line = SuccessionLawHelper.GetVisibleSuccessionLine(clan).ToList();
            foreach (Hero hero in HouseholdMembership.Members(clan)
                .Where(h => h.IsAlive && !h.IsDisabled && !h.IsChild && h != clan.Leader && !h.IsNotable && GetGrant(h) == null)
                .OrderBy(h => HouseholdMembership.IsFamily(h) ? 1 : 0)
                .ThenBy(h => line.IndexOf(h) < 0 ? int.MaxValue : line.IndexOf(h)).ThenByDescending(h => h.Age).ToList())
            {
                Settlement village = FindVacancy(clan);
                if (village == null) break;
                TryGrant(clan, village, hero, out _);
            }
        }

        internal bool EnsureAppointmentGrant(Hero hero)
        {
            if (!HouseholdMembership.IsRetainer(hero) || GetGrant(hero) != null) return true;
            Settlement village = FindVacancy(hero.Clan);
            return village != null && TryGrant(hero.Clan, village, hero, out _);
        }

        internal bool HasLegacyAppointment(Hero hero, MobileParty party = null, Town town = null) => _appointments.Any(a =>
            a != null && (party != null || town != null) && a.Hero == hero && a.IsCurrent && !a.Retirement && (!a.PlayerTransition || a.Deadline < 0 || Now < a.Deadline)
            && (party != null ? a.Party == party : town != null ? a.Town == town : true));

        private void Maintain()
        {
            if (!_ready) return;
            _realmCheckPending = true;
            Validate();
            foreach (var record in _appointments.ToList())
            {
                if (record?.IsCurrent != true || !HouseholdMembership.IsRetainer(record.Hero) || GetGrant(record.Hero) != null)
                { _appointments.Remove(record); continue; }
                if (!record.Retirement && (!record.PlayerTransition || record.Deadline < 0 || Now < record.Deadline)) continue;
                MobileParty party = record.Party;
                if (party != null)
                {
                    if (party.MapEvent != null || party.SiegeEvent != null || party.IsCurrentlyAtSea || party.Army != null) continue;
                    DisbandPartyAction.StartDisband(party);
                }
                else if (record.Town != null && !record.Town.Settlement.IsUnderSiege)
                    ChangeGovernorAction.RemoveGovernorOf(record.Hero);
                else continue;
                _appointments.Remove(record);
            }
        }

        private void OnTick(float dt)
        {
            if (Ready && _realmCheckPending)
            {
                _realmCheckPending = false;
                ValidateVassalRealms();
            }
            if (_retirementQueued)
            {
                _retirementQueued = false;
                Maintain();
            }
            if (!_ready || _warned || InformationManager.IsAnyInquiryActive() || Hero.MainHero == null
                || PlayerEncounter.Current != null || Campaign.Current.CurrentMenuContext != null
                || Campaign.Current.ConversationManager?.IsConversationInProgress == true) return;
            var pending = _appointments.Where(a => a.PlayerTransition && a.IsCurrent
                && HouseholdMembership.IsRetainer(a.Hero) && GetGrant(a.Hero) == null).ToList();
            if (pending.Count == 0) { _warned = true; return; }
            _warned = true;
            foreach (var record in pending) record.Deadline = Now + 5f;
            var body = Text("BC_Lordship_MigrationBody", "The following retainers command parties or govern settlements in your name without village endowments:\n{RETAINERS}\n\nGrant them land within five days if they are to remain in these posts. Those who remain unendowed will be relieved of their duties.")
                .SetTextVariable("RETAINERS", string.Join("\n", pending.Select(a => a.Hero.Name + " - " + (a.Town?.Name?.ToString() ?? a.Party.Name.ToString()))));
            InformationManager.ShowInquiry(new InquiryData(Text("BC_Lordship_MigrationTitle", "Retainers Await Endowment").ToString(),
                body.ToString(), true, true, Text("BC_Lordship_GrantPlural", "Endow Retainers").ToString(),
                Text("BC_Lordship_Attend", "I Will Attend to It").ToString(), () => LordshipPlayerActions.ShowMigration(), null,
                isAffirmativeOptionEnabled: () => (Villages(Clan.PlayerClan).Any(v => pending.Any(a => CanGrant(Clan.PlayerClan, v, a.Hero, out _))),
                    Text("BC_Lordship_NoVacancies", "None of your villages are available for a grant.").ToString())), true);
        }

        private static float Now => (float)CampaignTime.Now.ToDays;
    }
}
