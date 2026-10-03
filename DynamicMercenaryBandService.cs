using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal static class DynamicMercenaryBandService
    {
        public const string ClanIdPrefix = "bc_dynamic_merc_";

        private static readonly BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly PropertyInfo IsMinorFactionProperty = typeof(Clan).GetProperty("IsMinorFaction", InstanceFlags);
        private static readonly PropertyInfo IsMercenaryProperty = typeof(Clan).GetProperty("IsClanTypeMercenary", InstanceFlags);
        private static readonly FieldInfo IsMinorFactionField = typeof(Clan).GetField("<IsMinorFaction>k__BackingField", InstanceFlags);
        private static readonly FieldInfo IsMercenaryField = typeof(Clan).GetField("<IsClanTypeMercenary>k__BackingField", InstanceFlags);
        private static readonly PropertyInfo ClanTierProperty = typeof(Clan).GetProperty("Tier", InstanceFlags);
        private static readonly FieldInfo ClanTierField = typeof(Clan).GetField("_tier", InstanceFlags);
        private static readonly FieldInfo DefaultPartyTemplateField = typeof(Clan).GetField("_defaultPartyTemplate", InstanceFlags);

        public static bool IsDynamicBand(Clan clan)
        {
            return clan != null
                && !string.IsNullOrWhiteSpace(clan.StringId)
                && clan.StringId.StartsWith(ClanIdPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static int GetAdultOfficerCount(Clan clan)
        {
            return clan?.AliveLords?.Count(hero => hero != null
                && hero.IsAlive
                && !hero.IsChild
                && hero.Age >= SuccessionLawHelper.GetAgeOfMajority()) ?? 0;
        }

        public static bool CanTransferHeroNow(Hero hero, bool allowGovernorDeparture = false)
        {
            if (hero == null
                || !hero.IsAlive
                || !hero.IsActive
                || hero.IsChild
                || hero.Age < SuccessionLawHelper.GetAgeOfMajority()
                || hero.IsDisabled
                || hero.IsPrisoner
                || hero.PartyBelongedToAsPrisoner != null
                || hero.IsTraveling
                || (!allowGovernorDeparture && hero.GovernorOf != null))
            {
                return false;
            }

            MobileParty party = hero.PartyBelongedTo;
            if (party != null)
            {
                if (party.MapEvent != null || party.SiegeEvent != null || party.Army != null)
                    return false;
                if (party.LeaderHero == hero && !party.IsLordParty)
                    return false;
            }

            return hero.CurrentSettlement == null || !hero.CurrentSettlement.IsUnderSiege;
        }

        public static bool TryCreateBand(
            Hero founder,
            Settlement home,
            int startingTier,
            out Clan clan,
            out DynamicMercenaryBandRecord record,
            out string failure,
            bool allowGovernorDeparture = false)
        {
            clan = null;
            record = null;
            failure = string.Empty;

            Clan sourceClan = founder?.Clan;
            CultureObject culture = founder?.Culture ?? sourceClan?.Culture ?? home?.Culture;
            if (founder == null || sourceClan == null || culture == null || home == null || !home.IsTown)
            {
                failure = "missing founder, source clan, culture, or city";
                return false;
            }
            PartyTemplateObject partyTemplate = culture.DefaultPartyTemplate ?? sourceClan.DefaultPartyTemplate;
            if (partyTemplate == null || culture.BasicTroop == null)
            {
                failure = "culture has no usable lord-party template or basic troop";
                return false;
            }
            if (!CanTransferHeroNow(founder, allowGovernorDeparture))
            {
                failure = "founder cannot safely change clans now";
                return false;
            }
            if (!CanConfigureMercenaryFlags())
            {
                failure = "minor-faction flags are unavailable in this game version";
                return false;
            }

            string clanId = BuildClanId(culture, home, founder);
            TextObject clanName = DynamicMercenaryNameConfig.Instance.BuildName(
                culture.StringId,
                home,
                founder,
                clanId,
                IsClanNameInUse);
            Banner banner = KingdomVisualHelper.CreateRandomClanBannerWithoutStrokes(clanId);

            try
            {
                clan = Clan.CreateClan(clanId);
                clan.ChangeClanName(clanName, clanName);
                clan.Culture = culture;
                clan.Banner = banner;
                clan.Color = banner?.GetPrimaryColor() ?? sourceClan.Color;
                clan.Color2 = banner?.GetFirstIconColor() ?? sourceClan.Color2;
                clan.UpdateBannerColor(clan.Color, clan.Color2);
                clan.BasicTroop = culture.BasicTroop;
                if (culture.DefaultPartyTemplate == null)
                    DefaultPartyTemplateField?.SetValue(clan, partyTemplate);
                clan.IsNoble = false;
                SetClanTier(clan, startingTier);
                clan.SetInitialHomeSettlement(home);

                if (!TryConfigureMercenaryFlags(clan))
                {
                    failure = "failed to apply minor-faction flags";
                    DestroyClanAction.Apply(clan);
                    clan = null;
                    return false;
                }

                if (!TryTransferHero(founder, clan, home, allowGovernorDeparture, out failure))
                {
                    DestroyClanAction.Apply(clan);
                    clan = null;
                    return false;
                }

                clan.SetLeader(founder);
                clan.ConsiderAndUpdateHomeSettlement();
                record = new DynamicMercenaryBandRecord(
                    clan.StringId,
                    founder.StringId,
                    sourceClan.StringId,
                    culture.StringId,
                    home.StringId,
                    CampaignTime.Now);
                Clan createdClan = clan;
                DynamicMercenaryDescriptionHelper.EnsureDescription(clan, record, founder, home);

                RunPostCreationStep(
                    "clan-created event",
                    createdClan,
                    founder,
                    () => CampaignEventDispatcher.Instance.OnClanCreated(createdClan, isCompanion: false));
                RunPostCreationStep(
                    "starting treasury",
                    createdClan,
                    founder,
                    () =>
                    {
                        if (founder.Gold < C.DynamicMercenaryStartingGold)
                            GiveGoldAction.ApplyBetweenCharacters(null, founder, C.DynamicMercenaryStartingGold - founder.Gold, false);
                    });
                RunPostCreationStep(
                    "founder party",
                    createdClan,
                    founder,
                    () => EnsureHeroPartyOrHome(founder, createdClan, home));
                return true;
            }
            catch (Exception ex)
            {
                failure = ex.GetType().Name + ":" + ex.Message;
                BellumCivileLogger.Log(
                    $"Dynamic mercenary company creation failed; founder={founder.StringId}; source={sourceClan.StringId}; home={home.StringId}; error={ex}.");
                if (clan != null && founder.Clan == clan)
                {
                    if (clan.Leader == null)
                        clan.SetLeader(founder);
                    record = new DynamicMercenaryBandRecord(
                        clan.StringId,
                        founder.StringId,
                        sourceClan.StringId,
                        culture.StringId,
                        home.StringId,
                        CampaignTime.Now);
                    failure = string.Empty;
                    DynamicMercenaryDescriptionHelper.EnsureDescription(clan, record, founder, home);
                    return true;
                }

                if (clan != null && !clan.IsEliminated && !clan.Heroes.Any(hero => hero != null && hero.IsAlive))
                    DestroyClanAction.Apply(clan);
                return false;
            }
        }

        public static bool TryJoinBand(
            Hero hero,
            Clan targetClan,
            Settlement fallbackHome,
            out string failure,
            bool allowGovernorDeparture = false)
        {
            failure = string.Empty;
            if (hero?.Clan == null || targetClan == null || targetClan.IsEliminated || !IsDynamicBand(targetClan))
            {
                failure = "invalid hero or target company";
                return false;
            }
            if (!CanTransferHeroNow(hero, allowGovernorDeparture))
            {
                failure = "hero cannot safely change clans now";
                return false;
            }
            if (!TryConfigureMercenaryFlags(targetClan))
            {
                failure = "target company flags could not be repaired";
                return false;
            }

            Clan sourceClan = hero.Clan;
            Settlement home = targetClan.InitialHomeSettlement ?? targetClan.HomeSettlement ?? fallbackHome;
            try
            {
                if (!TryTransferHero(hero, targetClan, home, allowGovernorDeparture, out failure))
                    return false;

                try
                {
                    EnsureHeroPartyOrHome(hero, targetClan, home);
                }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log(
                        $"Dynamic mercenary join party setup failed; hero={hero.StringId}; company={targetClan.StringId}; error={ex.GetType().Name}:{ex.Message}.");
                }

                BellumCivileLogger.Log(
                    $"Noble joined dynamic mercenary company; hero={hero.StringId}; source={sourceClan.StringId}; company={targetClan.StringId}; culture={hero.Culture?.StringId ?? "none"}.");
                return true;
            }
            catch (Exception ex)
            {
                if (hero.Clan == targetClan)
                {
                    BellumCivileLogger.Log(
                        $"Dynamic mercenary join completed with a recoverable post-transfer error; hero={hero.StringId}; company={targetClan.StringId}; error={ex.GetType().Name}:{ex.Message}.");
                    return true;
                }

                failure = ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        public static bool RepairBand(Clan clan, Settlement recordedHome)
        {
            if (!IsDynamicBand(clan) || clan.IsEliminated)
                return false;

            bool flagsValid = TryConfigureMercenaryFlags(clan);
            if (clan.Culture == null && recordedHome?.Culture != null)
                clan.Culture = recordedHome.Culture;
            if (clan.BasicTroop == null && clan.Culture?.BasicTroop != null)
                clan.BasicTroop = clan.Culture.BasicTroop;
            if (clan.InitialHomeSettlement == null && recordedHome != null)
                clan.SetInitialHomeSettlement(recordedHome);

            foreach (Hero hero in clan.Heroes.Where(member => member != null && member.IsAlive))
                hero.IsMinorFactionHero = true;

            return flagsValid;
        }

        private static bool TryTransferHero(
            Hero hero,
            Clan targetClan,
            Settlement home,
            bool allowGovernorDeparture,
            out string failure)
        {
            failure = string.Empty;
            if (hero == null || targetClan == null || hero.Clan == targetClan)
                return hero?.Clan == targetClan;

            Clan oldClan = hero.Clan;
            if (hero.GovernorOf != null)
            {
                if (!allowGovernorDeparture)
                {
                    failure = "hero is governing a settlement";
                    return false;
                }

                ChangeGovernorAction.RemoveGovernorOf(hero);
            }

            MobileParty party = hero.PartyBelongedTo;
            if (party != null)
            {
                bool wasPartyLeader = party.LeaderHero == hero;
                party.MemberRoster.RemoveTroop(hero.CharacterObject);
                MakeHeroFugitiveAction.Apply(hero);
                if (wasPartyLeader && party.IsActive && party.IsLordParty)
                    DisbandPartyAction.StartDisband(party);
            }
            else if (hero.CurrentSettlement != null)
            {
                MakeHeroFugitiveAction.Apply(hero);
            }

            hero.Clan = targetClan;
            hero.SetNewOccupation(Occupation.Lord);
            hero.IsMinorFactionHero = true;
            hero.ChangeState(Hero.CharacterStates.Active);

            foreach (Hero member in oldClan?.Heroes ?? Enumerable.Empty<Hero>())
                member.UpdateHomeSettlement();
            foreach (Hero member in targetClan.Heroes)
                member.UpdateHomeSettlement();

            if (home != null)
                hero.UpdateHomeSettlement();
            return true;
        }

        private static void RunPostCreationStep(string step, Clan clan, Hero founder, Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log(
                    $"Dynamic mercenary company optional {step} failed; company={clan?.StringId ?? "none"}; founder={founder?.StringId ?? "none"}; error={ex.GetType().Name}:{ex.Message}.");
            }
        }

        private static void EnsureHeroPartyOrHome(Hero hero, Clan clan, Settlement home)
        {
            if (hero == null || clan == null || hero.PartyBelongedTo != null)
                return;

            int partyLimit = Campaign.Current.Models.ClanTierModel.GetPartyLimitForTier(clan, clan.Tier);
            if (home != null && clan.WarPartyComponents.Count < partyLimit)
            {
                string partyId = SanitizeId($"{clan.StringId}_{hero.StringId}_{(int)CampaignTime.Now.ToDays}");
                LordPartyComponent.CreateLordParty(
                    partyId,
                    hero,
                    home.GatePosition,
                    3f,
                    home,
                    hero);
                return;
            }

            if (home != null)
                EnterSettlementAction.ApplyForCharacterOnly(hero, home);
        }

        private static bool CanConfigureMercenaryFlags()
        {
            return CanSetBoolean(IsMinorFactionProperty, IsMinorFactionField)
                && CanSetBoolean(IsMercenaryProperty, IsMercenaryField);
        }

        private static bool TryConfigureMercenaryFlags(Clan clan)
        {
            if (clan == null)
                return false;

            try
            {
                SetBoolean(clan, IsMinorFactionProperty, IsMinorFactionField, true);
                SetBoolean(clan, IsMercenaryProperty, IsMercenaryField, true);
                return clan.IsMinorFaction && clan.IsClanTypeMercenary;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log(
                    $"Failed to configure dynamic mercenary flags; clan={clan.StringId}; error={ex.GetType().Name}:{ex.Message}.");
                return false;
            }
        }

        private static bool CanSetBoolean(PropertyInfo property, FieldInfo field)
        {
            return property?.GetSetMethod(true) != null || field != null;
        }

        private static void SetBoolean(Clan clan, PropertyInfo property, FieldInfo field, bool value)
        {
            MethodInfo setter = property?.GetSetMethod(true);
            if (setter != null)
                setter.Invoke(clan, new object[] { value });
            else if (field != null)
                field.SetValue(clan, value);
            else
                throw new MissingMemberException(typeof(Clan).FullName, property?.Name ?? "mercenary flag");
        }

        private static void SetClanTier(Clan clan, int targetTier)
        {
            int minTier = Campaign.Current.Models.ClanTierModel.MinClanTier;
            int maxTier = Campaign.Current.Models.ClanTierModel.MaxClanTier;
            int tier = Math.Max(minTier, Math.Min(maxTier, targetTier));
            clan.Renown = Campaign.Current.Models.ClanTierModel.GetRequiredRenownForTier(tier);

            try
            {
                ClanTierProperty?.SetValue(clan, tier);
            }
            catch
            {
                ClanTierField?.SetValue(clan, tier);
            }
        }

        private static bool IsClanNameInUse(string name)
        {
            return Clan.All.Any(candidate => candidate != null
                && !candidate.IsEliminated
                && string.Equals(candidate.Name?.ToString(), name, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildClanId(CultureObject culture, Settlement home, Hero founder)
        {
            return SanitizeId($"{ClanIdPrefix}{culture?.StringId ?? "culture"}_{home?.StringId ?? "home"}_{founder?.StringId ?? "founder"}");
        }

        private static string SanitizeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return ClanIdPrefix + "company";

            return new string(value.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray());
        }
    }
}
