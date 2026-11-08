using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class LordshipPlayerActions
    {
        private static string T(string key, string text, Settlement village = null)
            => LordshipBehavior.EstateText(key, text, village, Clan.PlayerClan).ToString();

        internal static void ShowGrant(Settlement village, Action changed = null)
        {
            var behavior = LordshipBehavior.Instance;
            if (behavior == null) return;
            var choices = HouseholdMembership.Members(Clan.PlayerClan)
                .Where(h => h.IsAlive && !h.IsDisabled && !h.IsChild && h != Clan.PlayerClan.Leader && !h.IsNotable)
                .Concat(behavior.LandlessVassalRecipients(Clan.PlayerClan)).Distinct()
                .OrderBy(h => HouseholdMembership.IsFamily(h) ? 1 : 0).ThenByDescending(h => h.Age)
                .Select(hero => new InquiryElement(hero, hero.Clan == Clan.PlayerClan ? hero.Name.ToString()
                    : hero.Name + " (" + hero.Clan.Name + ")", null,
                    behavior.CanGrantFromPlayerHouse(village, hero, out var reason), reason?.ToString() ?? string.Empty)).ToList();
            var description = LordshipBehavior.EstateText("BC_Lordship_RecipientDescriptionExtended", "Choose a member of your household or the head of a landless vassal house to receive {LORDSHIP}. Its revenues will support the holder or their house for as long as the personal grant remains in force.", village);
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                T("BC_Lordship_GrantTitle", "Grant {LORDSHIP_TYPE}", village), description.ToString(), choices, true, 1, 1,
                T("BC_Hierarchy_ActionGrant", "Grant"), T("BC_UI_Cancel", "Cancel"), selected =>
                {
                    Hero hero = selected?.FirstOrDefault()?.Identifier as Hero;
                    if (hero == null) return;
                    if (!behavior.TryGrantFromPlayerHouse(village, hero, out var reason)) Failure(reason);
                    else changed?.Invoke();
                }, null), true);
        }

        internal static void ShowVillageChoice(Hero hero, Action changed = null)
        {
            var behavior = LordshipBehavior.Instance;
            if (behavior == null) return;
            var choices = behavior.Villages(Clan.PlayerClan).Where(v => behavior.CanGrant(Clan.PlayerClan, v, hero, out _))
                .Select(v => new InquiryElement(v, LordshipBehavior.Name(v).ToString(), null)).ToList();
            if (choices.Count == 0)
            {
                Failure(LordshipBehavior.Text("BC_Lordship_NoVacancies", "None of your villages are available for a grant."));
                return;
            }
            var description = LordshipBehavior.Text("BC_Lordship_ChooseVillage", "Choose a village grant for {HERO}. Its revenues will belong exclusively to the holder.").SetTextVariable("HERO", hero.Name);
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                T("BC_Lordship_GrantTitle", "Grant {LORDSHIP_TYPE}"), description.ToString(), choices, true, 1, 1,
                T("BC_Hierarchy_ActionGrant", "Grant"), T("BC_UI_Cancel", "Cancel"), selected =>
                {
                    var village = selected?.FirstOrDefault()?.Identifier as Settlement;
                    if (village == null) return;
                    if (!behavior.TryGrant(Clan.PlayerClan, village, hero, out var reason)) Failure(reason);
                    else changed?.Invoke();
                }, null), true);
        }

        internal static void ShowRevoke(Settlement village, Action changed = null)
        {
            var grant = LordshipBehavior.Instance?.GetGrant(village);
            if (grant == null) return;
            var body = LordshipBehavior.EstateText("BC_Lordship_RevokeConfirm", "Revoke {LORDSHIP} from {HERO}? Relations will decrease by 15. A retainer without another lordship or active indenture will also be relieved of party command or governorship when it is safe.", village)
                .SetTextVariable("HERO", grant.Holder.Name);
            InformationManager.ShowInquiry(new InquiryData(T("BC_Lordship_RevokeTitle", "Revoke {LORDSHIP_TYPE}", village), body.ToString(),
                true, true, T("BC_Hierarchy_ActionRevoke", "Revoke"), T("BC_UI_Cancel", "Cancel"), () =>
                {
                    if (LordshipBehavior.Instance?.TryRevoke(Clan.PlayerClan, village, out var reason) == true) changed?.Invoke();
                    else Failure(LordshipBehavior.Text("BC_Lordship_Changed", "This grant is no longer available."));
                }, null), true);
        }

        internal static void ShowMigration()
        {
            var behavior = LordshipBehavior.Instance;
            if (behavior == null) return;
            var heroes = HouseholdMembership.Members(Clan.PlayerClan).Where(h => h.IsAlive && HouseholdMembership.IsRetainer(h)
                && !behavior.HasServiceSupport(h) && (h.GovernorOf != null || h.PartyBelongedTo?.LeaderHero == h)).ToList();
            var choices = heroes.Select(h => new InquiryElement(h, h.Name.ToString(), null)).ToList();
            if (choices.Count == 0) return;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                T("BC_Lordship_MigrationTitle", "Retainers Await Endowment"),
                T("BC_Lordship_MigrationChoose", "Choose a retainer to receive a village endowment. The holder alone will receive its revenues."),
                choices, true, 1, 1, T("BC_Hierarchy_ActionGrant", "Grant"), T("BC_UI_Cancel", "Cancel"),
                selected => { if (selected?.FirstOrDefault()?.Identifier is Hero hero) ShowVillageChoice(hero); }, null), true);
        }

        private static void Failure(TextObject reason) => InformationManager.DisplayMessage(new InformationMessage(reason?.ToString() ?? string.Empty, Colors.Red));
    }
}
