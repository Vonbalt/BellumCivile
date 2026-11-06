using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement.Categories;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.Core.ViewModelCollection.Information;

namespace BellumCivile.Patches
{
    // Installed after campaign texts exist: patching clan UI methods at module startup can initialize native UI helpers too soon.
    internal static class LordshipAppointmentPatches
    {
        private static bool _installed;
        internal static void Initialize()
        {
            if (_installed) return;
            var harmony = new Harmony("com.bellumcivile.lordship.appointments");
            void Patch(Type type, string name, string prefix = null, string postfix = null, string transpiler = null, string finalizer = null)
                => harmony.Patch(AccessTools.Method(type, name),
                    prefix == null ? null : new HarmonyMethod(typeof(LordshipAppointmentPatches), prefix),
                    postfix == null ? null : new HarmonyMethod(typeof(LordshipAppointmentPatches), postfix),
                    transpiler == null ? null : new HarmonyMethod(typeof(LordshipAppointmentPatches), transpiler),
                    finalizer == null ? null : new HarmonyMethod(typeof(LordshipAppointmentPatches), finalizer));
            try
            {
                Patch(typeof(Hero), nameof(Hero.CanLeadParty), postfix: nameof(CanLead));
                Patch(typeof(DefaultArmyManagementCalculationModel), "CanLordCreateArmy", nameof(EnterCommandCheck), finalizer: nameof(ExitCommandCheck));
                Patch(typeof(HeroSpawnCampaignBehavior), "GetBestAvailableCommander", nameof(EnterSelection), transpiler: nameof(CommanderCode), finalizer: nameof(ExitSelection));
                Patch(typeof(HeroSpawnCampaignBehavior), "SpawnLordParty", nameof(BeforeSpawn));
                Patch(typeof(ClanVariablesCampaignBehavior), "UpdateGovernorsOfClan", nameof(EnterSelection), transpiler: nameof(GovernorCode), finalizer: nameof(ExitSelection));
                Patch(typeof(ChangeGovernorAction), "Apply", nameof(BeforeGovernor), nameof(AppointmentsChanged));
                Patch(typeof(ChangeGovernorAction), "RemoveGovernorOf", postfix: nameof(AppointmentsChanged));
                Patch(typeof(MobileParty), "ChangePartyLeader", postfix: nameof(PartyLeaderChanged));
                Patch(typeof(MobileParty), "RemovePartyLeader", postfix: nameof(AppointmentsChanged));
                Patch(typeof(MarriageAction), "Apply", postfix: nameof(FamilyChanged));
                Patch(typeof(TooltipRefresherCollection), "RefreshHeroTooltip", postfix: nameof(HeroTooltip));
                var parties = AccessTools.TypeByName("TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement.Categories.ClanPartiesVM");
                var fiefs = AccessTools.TypeByName("TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement.Categories.ClanFiefsVM");
                Patch(parties, "GetNewPartyLeaderCandidates", postfix: nameof(PartyCandidates));
                Patch(parties, "GetChangeLeaderCandidates", postfix: nameof(ChangeLeaderCandidates));
                Patch(parties, "GetCanCreateNewParty", nameof(EnterCommandCheck), nameof(CreatePartyReason), finalizer: nameof(ExitCommandCheck));
                Patch(fiefs, "GetGovernorCandidates", postfix: nameof(GovernorCandidates));
                Patch(parties, "CreateNewClanParty", nameof(PlayerCommand));
                Patch(parties, "OnPartyLeaderChanged", nameof(PlayerLeaderChange));
                _installed = true;
            }
            catch
            {
                harmony.UnpatchAll(harmony.Id);
                throw;
            }
        }

        private static void EnterSelection(Clan clan, out Clan __state)
        { __state = LordshipEligibility.SelectingClan; LordshipEligibility.SelectingClan = clan; }

        private static Exception ExitSelection(Exception __exception, Clan __state)
        { LordshipEligibility.SelectingClan = __state; return __exception; }

        private static void CanLead(Hero __instance, ref bool __result)
        {
            if (__result && !LordshipEligibility.ListingCandidates
                && (LordshipEligibility.SelectingClan != null || LordshipEligibility.CheckingClanCommand)
                && !LordshipEligibility.Permitted(__instance)) __result = false;
        }

        private static void EnterCommandCheck(out bool __state)
        { __state = LordshipEligibility.CheckingClanCommand; LordshipEligibility.CheckingClanCommand = true; }

        private static Exception ExitCommandCheck(Exception __exception, bool __state)
        { LordshipEligibility.CheckingClanCommand = __state; return __exception; }

        private static void CreatePartyReason(bool __result, ref TextObject disabledReason)
        {
            if (__result || LordshipBehavior.Instance?.Ready != true || disabledReason?.GetID() != "6OZycZbc") return;
            disabledReason = LordshipBehavior.Text("BC_Lordship_NoPartyLeader",
                "Your clan has no available family members or retainers holding a lordship grant to lead a new party.");
        }

        private static void FamilyChanged()
        {
            HouseholdMembership.Invalidate();
            FeudalTitleHeroNamePatch.InvalidateCache();
        }

        private static void AppointmentsChanged()
        {
            if (LordshipBehavior.Instance?.Ready == true) LordshipBehavior.Instance.PruneAppointments();
        }

        private static void PartyLeaderChanged(Hero __0)
        {
            AppointmentsChanged();
            if (__0 != null) LordshipBehavior.Instance?.CheckAppointment(__0);
        }

        private static bool ActiveCommander(Hero hero) => hero.IsActive && LordshipEligibility.Permitted(hero);

        private static bool FallbackCommander(Hero hero) => ActiveCommander(hero)
            && (LordshipBehavior.Instance?.Ready != true || hero.CanLeadParty());

        private static MBReadOnlyList<Hero> Candidates(Clan clan) => LordshipBehavior.Instance?.Ready != true ? clan.Heroes
            : new MBReadOnlyList<Hero>(HouseholdMembership.Members(clan));
        private static MBReadOnlyList<Hero> AliveCandidates(Clan clan) => LordshipBehavior.Instance?.Ready != true ? clan.AliveLords
            : new MBReadOnlyList<Hero>(HouseholdMembership.Members(clan).Where(h => h.IsAlive && (h.IsLord || HouseholdMembership.IsRetainer(h))));

        private static Occupation CommanderOccupation(CharacterObject character) => LordshipBehavior.Instance?.Ready == true && character.Occupation == Occupation.Wanderer
            && character.HeroObject != null && HouseholdMembership.IsRetainer(character.HeroObject) ? Occupation.Lord : character.Occupation;

        private static IEnumerable<CodeInstruction> CommanderCode(IEnumerable<CodeInstruction> instructions)
        {
            int active = 0, pool = 0, occupation = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(AccessTools.PropertyGetter(typeof(Hero), "IsActive")))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(LordshipAppointmentPatches), active++ == 0 ? nameof(ActiveCommander) : nameof(FallbackCommander));
                }
                else if (instruction.Calls(AccessTools.PropertyGetter(typeof(Clan), "Heroes")))
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(LordshipAppointmentPatches), nameof(Candidates)); pool++; }
                else if (instruction.Calls(AccessTools.PropertyGetter(typeof(CharacterObject), "Occupation")))
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(LordshipAppointmentPatches), nameof(CommanderOccupation)); occupation++; }
                yield return instruction;
            }
            if (active != 2 || pool != 2 || occupation != 2) throw new InvalidOperationException("Lordship commander selection contract changed.");
        }

        private static IEnumerable<CodeInstruction> GovernorCode(IEnumerable<CodeInstruction> instructions)
        {
            int pool = 0, remove = 0, apply = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(AccessTools.PropertyGetter(typeof(Clan), "AliveLords")))
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(LordshipAppointmentPatches), nameof(AliveCandidates)); pool++; }
                else if (instruction.Calls(AccessTools.Method(typeof(ChangeGovernorAction), "RemoveGovernorOf")))
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(LordshipAppointmentPatches), nameof(DeferGovernorRemoval)); remove++; }
                else if (instruction.Calls(AccessTools.Method(typeof(ChangeGovernorAction), "Apply")))
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(LordshipAppointmentPatches), nameof(ApplyGovernor)); apply++; }
                yield return instruction;
            }
            if (pool != 1 || remove != 1 || apply != 1) throw new InvalidOperationException("Lordship governor selection contract changed.");
        }

        private static void DeferGovernorRemoval(Hero hero) { }

        private static void ApplyGovernor(Town town, Hero hero)
        {
            var registry = LordshipBehavior.Instance;
            if (LordshipEligibility.Required(hero) && registry.GetGrant(hero) == null
                && !registry.HasLegacyAppointment(hero, town: town) && !registry.EnsureAppointmentGrant(hero)) return;
            if (hero.GovernorOf != null) ChangeGovernorAction.RemoveGovernorOf(hero);
            ChangeGovernorAction.Apply(town, hero);
        }

        private static bool BeforeSpawn(Hero hero, ref MobileParty __result)
        {
            var registry = LordshipBehavior.Instance;
            if (!LordshipEligibility.Required(hero) || registry.GetGrant(hero) != null
                || (hero.Clan != Clan.PlayerClan && registry.EnsureAppointmentGrant(hero))) return true;
            __result = null;
            return false;
        }

        private static bool BeforeGovernor(Town __0, Hero __1)
        {
            var registry = LordshipBehavior.Instance;
            if (__1 == null || !LordshipEligibility.Required(__1) || registry.GetGrant(__1) != null
                || registry.HasLegacyAppointment(__1, town: __0)) return true;
            if (__1.Clan != Clan.PlayerClan) return registry.EnsureAppointmentGrant(__1);
            InformationManager.DisplayMessage(new InformationMessage(LordshipEligibility.Reason(true).ToString(), Colors.Red));
            return false;
        }

        private static bool PlayerCommand(Hero __0)
        {
            if (__0 == null || !LordshipEligibility.Required(__0) || LordshipBehavior.Instance.GetGrant(__0) != null) return true;
            InformationManager.DisplayMessage(new InformationMessage(LordshipEligibility.Reason(false).ToString(), Colors.Red));
            return false;
        }

        private static bool PlayerLeaderChange(ClanPartiesVM __instance, Hero __0)
            => __instance.CurrentSelectedParty?.Party?.MobileParty?.IsLordParty != true || PlayerCommand(__0);

        private static void HeroTooltip(PropertyBasedTooltipVM __0, object[] __1)
        {
            if (__1?.FirstOrDefault() is Hero hero && LordshipEligibility.Required(hero)
                && !CampaignUIHelper.IsHeroInformationHidden(hero, out _))
            {
                var village = LordshipBehavior.Instance.GetGrant(hero)?.Village;
                __0.AddProperty((village != null ? FeudalTitleDisplayHelper.GetLordshipNoun(village)
                    : FeudalTitleDisplayHelper.GetLordshipNounForClan(hero.Clan)).ToString(),
                    village?.Name?.ToString() ?? LordshipBehavior.Text("BC_Hierarchy_None", "None").ToString());
            }
        }

        private static void PartyCandidates(ref IEnumerable<ClanCardSelectionItemInfo> __result) => __result = Filter(__result, false);
        private static void ChangeLeaderCandidates(ClanPartiesVM __instance, ref IEnumerable<ClanCardSelectionItemInfo> __result)
        {
            if (__instance.CurrentSelectedParty?.Party?.MobileParty?.IsLordParty == true) __result = Filter(__result, false);
        }
        private static void GovernorCandidates(ref IEnumerable<ClanCardSelectionItemInfo> __result) => __result = Filter(__result, true);

        private static IEnumerable<ClanCardSelectionItemInfo> Filter(IEnumerable<ClanCardSelectionItemInfo> source, bool governor)
        {
            using (var enumerator = source.GetEnumerator())
                while (true)
                {
                    ClanCardSelectionItemInfo item;
                    bool old = LordshipEligibility.ListingCandidates;
                    try
                    {
                        LordshipEligibility.ListingCandidates = true;
                        if (!enumerator.MoveNext()) break;
                        item = enumerator.Current;
                    }
                    finally { LordshipEligibility.ListingCandidates = old; }
                    if (!item.IsDisabled && item.Identifier is Hero hero && LordshipEligibility.Required(hero)
                        && LordshipBehavior.Instance.GetGrant(hero) == null)
                        item = new ClanCardSelectionItemInfo(item.Identifier, item.Title, item.Image, item.SpriteType,
                            item.SpriteName, item.SpriteLabel, item.Properties, true, LordshipEligibility.Reason(governor), item.ActionResult);
                    yield return item;
                }
        }
    }
}
