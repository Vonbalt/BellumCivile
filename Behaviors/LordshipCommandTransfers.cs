using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BellumCivile.Behaviors
{
    public sealed partial class LordshipBehavior
    {
        private List<LordshipCommandTransferRecord> _commandTransfers = new List<LordshipCommandTransferRecord>();
        private bool _replacementInquiryOpen;
        private bool _replacementPromptPending;
        [ThreadStatic] private static MobileParty _retirementDisbandParty;

        internal bool BlocksNativeRetirementReplacement(MobileParty party) => Ready && party != null
            && (_retirementDisbandParty == party || _commandTransfers.Any(t => t?.Party == party));

        private static void DisbandRetiredParty(MobileParty party)
        {
            var previous = _retirementDisbandParty;
            _retirementDisbandParty = party;
            try { DisbandPartyAction.StartDisband(party); }
            finally { _retirementDisbandParty = previous; }
        }

        private bool CanReplaceCommander(Hero hero, MobileParty party, bool arriving = false)
        {
            if (hero == null || party?.ActualClan == null || hero.Clan != party.ActualClan || hero == Hero.MainHero || hero == party.LeaderHero
                || !hero.IsAlive || (!hero.IsActive && !(arriving && hero.IsTraveling))
                || hero.IsChild || hero.IsWounded || hero.IsNoncombatant || hero.IsNotable || hero.IsSpecial || hero.IsTemplate
                || hero.DeathMark != KillCharacterAction.KillCharacterActionDetail.None || hero.IsPrisoner
                || hero.PartyBelongedToAsPrisoner != null || hero.GovernorOf != null
                || hero.CurrentSettlement?.IsUnderSiege == true || hero.CurrentSettlement?.IsUnderRaid == true
                || !hero.CanLeadParty() || !hero.CanBeGovernorOrHavePartyRole()
                || (!HouseholdMembership.IsFamily(hero) && !HasServiceSupport(hero))
                || Campaign.Current.GetCampaignBehavior<HostagePactBehavior>()?.IsReserved(hero) == true
                || Campaign.Current.GetCampaignBehavior<ExiledClanRecoveryBehavior>()?.IsAwaitingAsylum(hero) == true)
                return false;
            var source = hero.PartyBelongedTo;
            if (source != null && (source.LeaderHero == hero || source.MapEvent != null || source.SiegeEvent != null
                || source.Army != null || source.IsCurrentlyAtSea
                || source != party && (party.ActualClan != Clan.PlayerClan || source != MobileParty.MainParty))) return false;
            if (arriving) return true;
            if (_commandTransfers.Any(t => t?.Replacement == hero)) return false;
            return party.ActualClan != Clan.PlayerClan
                || FactionHelper.IsMainClanMemberAvailableForPartyLeaderChange(hero, true, party, out _);
        }

        private IEnumerable<Hero> ReplacementCandidates(MobileParty party)
        {
            if (party?.ActualClan == null || party.MemberRoster.TotalManCount <= (party.LeaderHero == null ? 0 : 1))
                return Enumerable.Empty<Hero>();
            return HouseholdMembership.Members(party.ActualClan).Where(h => CanReplaceCommander(h, party))
                .OrderByDescending(h => h.PartyBelongedTo == party)
                .ThenByDescending(h => 3 * h.GetSkillValue(DefaultSkills.Tactics) + 2 * h.GetSkillValue(DefaultSkills.Leadership)
                    + h.GetSkillValue(DefaultSkills.Scouting) + h.GetSkillValue(DefaultSkills.Steward));
        }

        private bool BeginCommandTransfer(Hero formerLeader, MobileParty party, Hero replacement,
            LordshipCommandTransferRecord transfer = null)
        {
            if (!CanRetireParty(party) || !CanReplaceCommander(replacement, party)
                || party.LeaderHero != null && party.LeaderHero != formerLeader) return false;
            if (transfer == null)
            {
                transfer = new LordshipCommandTransferRecord { Party = party, Clan = party.ActualClan, FormerLeader = formerLeader };
                _commandTransfers.Add(transfer);
            }
            if (!ReleaseRetiredCommander(formerLeader, party))
            {
                if (party.LeaderHero != null || !party.IsActive) _commandTransfers.Remove(transfer);
                return false;
            }
            transfer.Replacement = replacement;
            if (party.IsDisbanding || Campaign.Current.GetCampaignBehavior<IDisbandPartyCampaignBehavior>()?.IsPartyWaitingForDisband(party) == true)
                DisbandPartyAction.CancelDisband(party);
            if (transfer.Clan == Clan.PlayerClan)
            {
                int needed = Campaign.Current.Models.ClanFinanceModel.PartyGoldLowerThreshold - replacement.Gold;
                if (needed > 0) GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, replacement, needed);
            }
            if (replacement.PartyBelongedTo == party)
                TeleportHeroAction.ApplyImmediateTeleportToPartyAsPartyLeader(replacement, party);
            else TeleportHeroAction.ApplyDelayedTeleportToPartyAsPartyLeader(replacement, party);
            BellumCivileLogger.Log($"Replacing retired lordship commander; party={party.StringId}; former={formerLeader?.StringId}; replacement={replacement.StringId}; clan={transfer.Clan.StringId}.");
            return true;
        }

        private static bool IsReplacementTravelling(LordshipCommandTransferRecord transfer)
        {
            var travel = Campaign.Current.GetCampaignBehavior<ITeleportationCampaignBehavior>();
            return transfer.Replacement != null && travel != null
                && travel.GetTargetOfTeleportingHero(transfer.Replacement, out _, out bool leader, out var target)
                && leader && target == transfer.Party;
        }

        private static bool ValidTransferParty(LordshipCommandTransferRecord transfer) => transfer?.Party?.IsActive == true
            && transfer.Party.IsLordParty && !transfer.Party.IsMainParty && transfer.Clan != null && !transfer.Clan.IsEliminated
            && transfer.Party.ActualClan == transfer.Clan;

        private void MaintainCommandTransfers()
        {
            foreach (var transfer in _commandTransfers.ToList())
            {
                if (transfer == null) { _commandTransfers.Remove(transfer); continue; }
                // Native travel owns cancellation. Keep our record until arrival so changed allegiance cannot bypass its guard.
                if (IsReplacementTravelling(transfer)) continue;
                if (!ValidTransferParty(transfer) || transfer.Party.LeaderHero != null)
                { _commandTransfers.Remove(transfer); continue; }
                var travel = Campaign.Current.GetCampaignBehavior<ITeleportationCampaignBehavior>();
                if (travel != null && HouseholdMembership.Members(transfer.Clan).Any(h => h.IsTraveling
                    && travel.GetTargetOfTeleportingHero(h, out _, out bool leader, out var target) && leader && target == transfer.Party))
                { _commandTransfers.Remove(transfer); continue; }
                transfer.Replacement = null;
                if (!CanRetireParty(transfer.Party)) continue;
                var replacement = ReplacementCandidates(transfer.Party).FirstOrDefault();
                if (replacement != null)
                {
                    if (transfer.Clan != Clan.PlayerClan)
                        BeginCommandTransfer(transfer.FormerLeader, transfer.Party, replacement, transfer);
                    else _replacementPromptPending = true;
                    continue;
                }
                DisbandRetiredParty(transfer.Party);
                _commandTransfers.Remove(transfer);
            }
        }

        internal bool BeforeReplacementArrival(Hero hero, MobileParty party)
        {
            var transfer = _commandTransfers.FirstOrDefault(t => t?.Party == party && t.Replacement == hero);
            if (transfer == null) return true;
            if (ValidTransferParty(transfer) && CanRetireParty(party) && party.LeaderHero == null
                && CanReplaceCommander(hero, party, arriving: true)) return true;
            if (hero.IsTraveling && hero.DeathMark == KillCharacterAction.KillCharacterActionDetail.None)
                MakeHeroFugitiveAction.Apply(hero);
            transfer.Replacement = null;
            _retirementQueued = true;
            BellumCivileLogger.Log($"Canceled ineligible lordship command replacement; party={party?.StringId}; hero={hero.StringId}.");
            return false;
        }

        private bool ShowCommandReplacementInquiry()
        {
            if (_replacementInquiryOpen) return true;
            if (!_replacementPromptPending) return false;
            var appointment = _appointments.FirstOrDefault(a => a?.IsCurrent == true && a.Party != null
                && a.OwningClan == Clan.PlayerClan && HouseholdMembership.IsRetainer(a.Hero) && !HasServiceSupport(a.Hero)
                && (a.Retirement || a.PlayerTransition && a.Deadline >= 0 && Now >= a.Deadline)
                && CanRetireParty(a.Party) && ReplacementCandidates(a.Party).Any());
            var transfer = appointment == null ? _commandTransfers.FirstOrDefault(t => ValidTransferParty(t)
                && t.Clan == Clan.PlayerClan && t.Replacement == null && t.Party.LeaderHero == null
                && CanRetireParty(t.Party) && ReplacementCandidates(t.Party).Any()) : null;
            var party = appointment?.Party ?? transfer?.Party;
            if (party == null) { _replacementPromptPending = false; return false; }
            var former = appointment?.Hero ?? transfer.FormerLeader;
            int threshold = Campaign.Current.Models.ClanFinanceModel.PartyGoldLowerThreshold;
            var choices = ReplacementCandidates(party).Select(h => new InquiryElement(h, h.Name.ToString(), null, true,
                Text("BC_Lordship_ReplacementCost", "Assigning this commander costs {COST} denars.")
                    .SetTextVariable("COST", Math.Max(0, threshold - h.Gold)).ToString())).ToList();
            _replacementInquiryOpen = true;
            void Decide(Hero replacement)
            {
                _replacementInquiryOpen = false;
                bool current = appointment != null ? _appointments.Contains(appointment) && appointment.IsCurrent
                    && HouseholdMembership.IsRetainer(former) && !HasServiceSupport(former)
                    : _commandTransfers.Contains(transfer) && ValidTransferParty(transfer)
                        && transfer.Replacement == null && transfer.Party.LeaderHero == null;
                if (!Ready || !current || !CanRetireParty(party)) { _retirementQueued = true; return; }
                if (replacement != null)
                {
                    if (BeginCommandTransfer(former, party, replacement, transfer) && appointment != null) _appointments.Remove(appointment);
                }
                else if (ReleaseRetiredCommander(former, party))
                {
                    DisbandRetiredParty(party);
                    if (appointment != null) _appointments.Remove(appointment);
                    if (transfer != null) _commandTransfers.Remove(transfer);
                }
            }
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                Text("BC_Lordship_ReplacementTitle", "Appoint a New Commander").ToString(),
                Text("BC_Lordship_ReplacementBody", "{HERO} must relinquish command of {PARTY}. Choose a new commander to keep the party in the field, or order it disbanded.")
                    .SetTextVariable("HERO", former.Name).SetTextVariable("PARTY", party.Name).ToString(), choices, true, 1, 1,
                Text("BC_Lordship_ReplacementAppoint", "Appoint").ToString(), Text("BC_Lordship_ReplacementDisband", "Disband Party").ToString(),
                selected =>
                {
                    if (selected?.FirstOrDefault()?.Identifier is Hero replacement) Decide(replacement);
                    else { _replacementInquiryOpen = false; _retirementQueued = true; }
                }, _ => Decide(null)), true);
            return true;
        }
    }
}
