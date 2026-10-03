using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace BellumCivile.Behaviors
{
    // Creation-only receipts, not a population-maintenance scheduler. Death or departure
    // never grants the branch another founding member.
    public sealed class CadetHouseholdBehavior : CampaignBehaviorBase
    {
        private Dictionary<string, Hero> _members = new Dictionary<string, Hero>();
        private Dictionary<string, bool> _initialized = new Dictionary<string, bool>();

        public override void RegisterEvents() { }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_CadetFoundingMembers", ref _members);
            dataStore.SyncData("BellumCivile_CadetFoundingMembersInitialized", ref _initialized);
            if (_members == null) _members = new Dictionary<string, Hero>();
            if (_initialized == null) _initialized = new Dictionary<string, bool>();
        }

        internal void EnsureFoundingMembers(Clan clan)
        {
            if (clan == null || clan.IsEliminated || clan == Clan.PlayerClan) return;
            try
            {
                var templates = clan.Culture?.LordTemplates;
                Settlement home = clan.HomeSettlement ?? clan.InitialHomeSettlement
                    ?? Settlement.All.FirstOrDefault(s => (s.IsTown || s.IsCastle) && s.Culture == clan.Culture)
                    ?? Settlement.All.FirstOrDefault(s => s.IsTown || s.IsCastle);
                if (templates == null || templates.Count == 0 || home == null)
                {
                    BellumCivileLogger.Log($"Cadet founding members unavailable; clan={clan.StringId}; missing lord templates or home.");
                    return;
                }

                EnsureSlots(clan.StringId, _members, _initialized,
                    slot => HeroCreator.CreateSpecialHero(templates[MBRandom.RandomInt(templates.Count)],
                        home, clan, null,
                        MBRandom.RandomInt(Campaign.Current.Models.AgeModel.HeroComesOfAge, 50)),
                    (member, slot) =>
                    {
                        if (member != null && member.IsAlive && member.Clan == clan)
                        {
                            CompanionSubinfeudationService.InitializeNewHouseMemberSkills(member, slot == 0);
                            member.ChangeState(Hero.CharacterStates.Active);
                            if (member.CurrentSettlement == null && member.PartyBelongedTo == null)
                                EnterSettlementAction.ApplyForCharacterOnly(member, home);
                        }
                        BellumCivileLogger.Log($"Cadet founding member recorded; clan={clan.StringId}; slot={slot}; hero={member?.StringId ?? "none"}.");
                    });
            }
            catch (Exception ex)
            {
                // Optional household expansion must not abort the crown/estate transfer.
                BellumCivileLogger.Log($"Cadet founding-member generation interrupted; clan={clan.StringId}; {ex}");
            }
        }

        internal static void EnsureSlots(string clanId, Dictionary<string, Hero> members,
            Dictionary<string, bool> initialized, Func<int, Hero> create, Action<Hero, int> initialize)
        {
            for (int slot = 0; slot < 2; slot++)
            {
                string key = clanId + ":" + slot;
                if (initialized.ContainsKey(key)) continue;
                if (!members.TryGetValue(key, out Hero member))
                {
                    member = create(slot);
                    if (member == null) throw new InvalidOperationException("Cadet member creation returned no hero.");
                    // Persist identity before activation/settlement callbacks can fail.
                    members.Add(key, member);
                }
                initialize(member, slot);
                initialized[key] = true;
            }
        }
    }
}
