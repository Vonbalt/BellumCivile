using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Why did I do this file?
    /// To track the dynastic heir (best succession candidate) of every kingdom's ruling clan and
    /// whether the current ruler is a civil war usurper. This data drives marriage dowry premiums
    /// and supplies the protected lawful candidate in king selection elections.
    /// </summary>
    public class DynasticHeirBehavior : CampaignBehaviorBase
    {
        // These caches hold at most one entry per kingdom; the ceiling only guards malformed save data.
        private const int MaxDynasticCacheEnumeration = 4096;

        public static DynasticHeirBehavior Instance { get; private set; }

        private Dictionary<string, string> _dynasticHeirs = new Dictionary<string, string>();

        private Dictionary<string, DynasticSuccessionStateRecord> _dynasticStates = new Dictionary<string, DynasticSuccessionStateRecord>();

        private Dictionary<string, string> _cadetBranchOrigins = new Dictionary<string, string>();

        private List<PendingCadetMarriageRecord> _pendingCadetMarriages = new List<PendingCadetMarriageRecord>();

        private List<string> _usurperKingdoms = new List<string>();


        private readonly HashSet<string> _pendingUsurperKingdoms = new HashSet<string>();
        private readonly Dictionary<string, Hero> _heroesById = new Dictionary<string, Hero>(StringComparer.Ordinal);
        // Political succession may preserve a displaced claimant; title styling needs the reigning house's heir.
        private readonly Dictionary<string, Hero> _reigningDynasticHeirsByKingdom = new Dictionary<string, Hero>(StringComparer.Ordinal);
        private readonly Dictionary<Hero, List<Kingdom>> _displayRealmsByHeir = new Dictionary<Hero, List<Kingdom>>();
        private bool _displayHeirIndexDirty = true;
        private readonly HashSet<Hero> _changedHouseholdHeroes = new HashSet<Hero>();
        private readonly HashSet<Clan> _changedHouseholdClans = new HashSet<Clan>();
        private long _displayHeirIndexBuilds;
        private bool _canResolveSavedObjects;
        private bool _dynasticCacheEnumerationWarningLogged;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.RulingClanChanged.AddNonSerializedListener(this, OnRulingClanChanged);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.BeforeHeroesMarried.AddNonSerializedListener(this, OnBeforeHeroesMarried);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, RefreshChangedHouseholds);
            CampaignEvents.OnGivenBirthEvent.AddNonSerializedListener(this, (mother, children, count) =>
            {
                QueueHouseholdRefresh(mother);
                if (children != null) foreach (Hero child in children) QueueHouseholdRefresh(child);
            });
            CampaignEvents.HeroComesOfAgeEvent.AddNonSerializedListener(this, hero => QueueHouseholdRefresh(hero));
            CampaignEvents.OnHeroChangedClanEvent.AddNonSerializedListener(this, (hero, oldClan) => QueueHouseholdRefresh(hero, oldClan));
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, (clan, oldRealm, newRealm, detail, show) =>
            {
                if (clan != null) foreach (Hero hero in clan.Heroes) QueueHouseholdRefresh(hero);
            });
            CampaignEvents.OnClanLeaderChangedEvent.AddNonSerializedListener(this, (oldLeader, newLeader) =>
            {
                QueueHouseholdRefresh(oldLeader);
                QueueHouseholdRefresh(newLeader);
            });
        }

        public override void SyncData(IDataStore dataStore)
        {
            Instance = this;
            dataStore.SyncData("BellumCivile_DynasticHeirs", ref _dynasticHeirs);
            dataStore.SyncData("BellumCivile_DynasticSuccessionStates", ref _dynasticStates);
            dataStore.SyncData("BellumCivile_CadetBranchOrigins", ref _cadetBranchOrigins);
            dataStore.SyncData("BellumCivile_PendingCadetMarriages", ref _pendingCadetMarriages);
            dataStore.SyncData("BellumCivile_UsurperKingdoms", ref _usurperKingdoms);
            EnsureCollectionsInitialized();
            PruneMalformedDynasticStates();
            PruneMalformedCadetBranchOrigins();
            // Retire queued marriage endowments without altering any established clan.
            _pendingCadetMarriages.Clear();
        }

        public void RefreshSuccessionAfterLawChange(Kingdom kingdom)
        {
            if (kingdom != null)
                RecalculateHeirForKingdom(kingdom);
        }

        internal void RefreshLegitimacy()
        {
            foreach (var realm in Kingdom.All.Where(k => !k.IsEliminated))
                if (CrownAccessionBehavior.Instance?.IsPending(realm) != true)
                    RecalculateHeirForKingdom(realm);
            _displayHeirIndexDirty = true;
        }

        // ------------------------------------------------------------
        // EVENT HANDLERS
        // -------------------- EVENT HANDLERS --------------------

        private void OnRulingClanChanged(Kingdom kingdom, Clan newRulingClan)
        {
            if (kingdom == null) return;
            if (_pendingUsurperKingdoms.Contains(kingdom.StringId))
            {

                _pendingUsurperKingdoms.Remove(kingdom.StringId);
            }
            else
            {

                _usurperKingdoms.Remove(kingdom.StringId);
            }

            if (!TryGetTrackedDynasticHeir(kingdom, out Hero trackedHeir))
            {
                RecalculateHeirForKingdom(kingdom);
                return;
            }

            Hero installedDynasticCandidate = ResolveDynasticSuccessionCandidate(kingdom, trackedHeir);
            if (installedDynasticCandidate?.Clan == newRulingClan)
            {
                RecalculateHeirForKingdom(kingdom);
                return;
            }

            // A different house taking the throne does not make it the rightful dynasty. Keep the
            // displaced line according to the lawful succession and title records.
            TraceDynasticSuccession(
                $"preserved displaced dynastic line for {kingdom.StringId}: " +
                $"rightful_heir={trackedHeir.StringId}; reigning_clan={newRulingClan?.StringId ?? "none"}.");
            RecalculateHeirForKingdom(kingdom);
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            QueueHouseholdRefresh(victim);
            if (victim != null && !string.IsNullOrWhiteSpace(victim.StringId))
                _heroesById.Remove(victim.StringId);



            string affectedKingdomId = null;
            try
            {
                int inspected = 0;
                foreach (KeyValuePair<string, string> kvp in _dynasticHeirs)
                {
                    if (++inspected > MaxDynasticCacheEnumeration)
                    {
                        LogDynasticCacheEnumerationFailure("legacy heir cache", "hero-death lookup", null);
                        return;
                    }

                    if (kvp.Value == victim.StringId)
                    {
                        affectedKingdomId = kvp.Key;
                        break;
                    }
                }
            }
            catch (Exception ex) when (IsDynasticCacheEnumerationException(ex))
            {
                LogDynasticCacheEnumerationFailure("legacy heir cache", "hero-death lookup", ex);
                return;
            }

            if (string.IsNullOrWhiteSpace(affectedKingdomId))
                return;

            Kingdom affectedKingdom = Kingdom.All.FirstOrDefault(k => k.StringId == affectedKingdomId);
            if (affectedKingdom != null)
                RecalculateHeirForKingdom(affectedKingdom);
        }

        private void OnBeforeHeroesMarried(Hero firstHero, Hero secondHero, bool showNotification)
        {
            QueueHouseholdRefresh(firstHero);
            QueueHouseholdRefresh(secondHero);
            if (firstHero?.Clan == null || secondHero?.Clan == null
                || Campaign.Current?.Models?.MarriageModel == null) return;
            EnsureCollectionsInitialized();
            Clan destination = Campaign.Current.Models.MarriageModel.GetClanAfterMarriage(firstHero, secondHero);
            if (destination == null) return;
            TrackMarriageRights(firstHero, secondHero, destination);
            TrackMarriageRights(secondHero, firstHero, destination);
        }

        private void TrackMarriageRights(Hero dynasticHeir, Hero spouse, Clan clanAfterMarriage)
        {
            Kingdom kingdom = dynasticHeir?.Clan?.Kingdom;
            Clan brideClan = dynasticHeir?.Clan;
            if (kingdom == null || spouse == null
                || !IsCurrentDynasticHeirForMarriage(kingdom, dynasticHeir, "marriage rights")) return;

            Clan dynastyClan = ResolveDynasticOriginClan(kingdom, brideClan);
            if (clanAfterMarriage == Clan.PlayerClan && IsDirectPlayerMarriageClaimSpouse(spouse, dynasticHeir))
            {
                bool hereditary = CrownAccessionBehavior.IsHereditaryRealm(kingdom);
                if (!hereditary && IsPlayerClanAttachedToForeignKingdom(kingdom))
                {
                    ShowDynasticRightsRenouncedMessage(dynasticHeir, spouse, brideClan, Clan.PlayerClan, Clan.PlayerClan.Kingdom, kingdom);
                    return;
                }

                Hero carrier = hereditary ? dynasticHeir : Clan.PlayerClan.Leader ?? spouse;
                if (!hereditary)
                    Campaign.Current.GetCampaignBehavior<DynasticClaimBehavior>()
                        ?.RegisterClaim(Clan.PlayerClan, carrier, kingdom, dynastyClan, "royal_heiress_player_marriage");
                // Hereditary marriage birthrights are registered once by the shared title hook.
                if (!hereditary)
                    Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>()
                        ?.RegisterDynasticCadetTitleClaims(dynastyClan, Clan.PlayerClan, carrier, "royal_heiress_player_marriage");
                SetDynasticState(kingdom, dynasticHeir, dynastyClan, Clan.PlayerClan, carrier, "royal_heiress_player_marriage");
                return;
            }

            // Marriage changes a household, not the blood heir or their personal Crown right.
            if (CrownAccessionBehavior.IsHereditaryRealm(kingdom))
                SetDynasticState(kingdom, dynasticHeir, dynastyClan, clanAfterMarriage, dynasticHeir, "hereditary_marriage");
            else
                TryShowDynasticRightsRenunciation(dynasticHeir, spouse, clanAfterMarriage);
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            _reigningDynasticHeirsByKingdom.Clear();
            _displayRealmsByHeir.Clear();
            _displayHeirIndexDirty = true;
            Patches.FeudalTitleHeroNamePatch.InvalidateCache();
            _canResolveSavedObjects = true;
            EnsureCollectionsInitialized();
            RebuildHeroResolutionCache();
            PruneDynasticStates();
            PruneCadetBranchOrigins();
            RefreshDynasticHeirCacheForAllKingdoms();
        }

        private void OnDailyTick()
        {
            _canResolveSavedObjects = true;
            EnsureCollectionsInitialized();
            PruneDynasticStates();
            PruneCadetBranchOrigins();
            RefreshDynasticHeirCacheForAllKingdoms();
        }

        private void QueueHouseholdRefresh(Hero hero, Clan previousClan = null)
        {
            if (hero != null)
            {
                _changedHouseholdHeroes.Add(hero);
                if (hero.Clan != null) _changedHouseholdClans.Add(hero.Clan);
                Patches.FeudalTitleHeroNamePatch.InvalidateHero(hero);
            }
            if (previousClan != null) _changedHouseholdClans.Add(previousClan);
        }

        private void RefreshChangedHouseholds()
        {
            if (!_canResolveSavedObjects || _changedHouseholdHeroes.Count == 0) return;
            Hero[] changedHeroes = _changedHouseholdHeroes.ToArray();
            var changedClans = new HashSet<Clan>(_changedHouseholdClans);
            _changedHouseholdHeroes.Clear();
            _changedHouseholdClans.Clear();
            foreach (Kingdom realm in Kingdom.All)
            {
                if (realm == null || realm.IsEliminated || realm.RulingClan?.Leader == null) continue;
                Hero sovereign = RegencyBehavior.Instance?.GetLegalClanHead(realm.RulingClan) ?? realm.RulingClan.Leader;
                _reigningDynasticHeirsByKingdom.TryGetValue(realm.StringId, out Hero heir);
                if (changedClans.Contains(realm.RulingClan) || changedHeroes.Any(hero => hero == sovereign
                    || hero == heir || HereditaryRealmSuccession.IsBloodRelative(hero, sovereign)))
                    RecalculateHeirForKingdom(realm);
            }
        }

        // ------------------------------------------------------------
        // HEIR CALCULATION
        // -------------------- HEIR CALCULATION --------------------

        // What does this method do?
        // Runs DynamicSuccessionModel over every adult member of the ruling clan (excluding the
        // current leader) and stores the highest-scoring candidate as the kingdom's dynastic heir.
        // A fresh DynamicSuccessionModel instance is used; the model is stateless, so this avoids
        // a timing dependency on the model being registered at the right moment.
        private void RecalculateHeirForKingdom(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated || kingdom.RulingClan?.Leader == null)
            {
                if (kingdom != null)
                {
                    ClearDynasticState(kingdom);
                    SetReigningDynasticHeir(kingdom, null);
                }
                return;
            }

            Hero reigningHeir = CalculateBestDynasticHeirForKingdom(kingdom);
            SetReigningDynasticHeir(kingdom, reigningHeir);

            if (CrownAccessionBehavior.IsHereditaryRealm(kingdom))
            {
                // Do not freeze an old nominee ahead of the new sovereign's family.
                if (reigningHeir != null)
                    SetDynasticState(kingdom, reigningHeir, kingdom.RulingClan, reigningHeir.Clan,
                        reigningHeir, "hereditary_realm_line");
                else ClearDynasticState(kingdom);
                return;
            }

            if (TryResolvePendingKingSelectionHeir(kingdom, out Hero pendingSuccessionHeir))
            {
                SetDynasticState(
                    kingdom,
                    pendingSuccessionHeir,
                    ResolveDynasticOriginClan(kingdom, pendingSuccessionHeir.Clan),
                    ResolveDynasticSuccessionCandidate(kingdom, pendingSuccessionHeir)?.Clan ?? pendingSuccessionHeir.Clan,
                    ResolveDynasticSuccessionCandidate(kingdom, pendingSuccessionHeir) ?? pendingSuccessionHeir,
                    "pending_king_selection",
                    CampaignTime.Now + CampaignTime.Days(7f));
                TraceDynasticSuccession(
                    $"preserved pending succession heir for {kingdom.StringId}: heir={pendingSuccessionHeir.StringId}; clan={pendingSuccessionHeir.Clan?.StringId ?? "none"}.");
                return;
            }

            if (TryGetTrackedDynasticHeir(kingdom, out Hero currentTrackedHeir)
                && currentTrackedHeir.Clan != kingdom.RulingClan
                && TryResolveValidDynasticSuccessionCandidate(kingdom, currentTrackedHeir, out Hero resolvedTrackedCandidate))
            {
                SetDynasticState(
                    kingdom,
                    currentTrackedHeir,
                    ResolveTrackedRightfulDynastyClan(kingdom, currentTrackedHeir),
                    resolvedTrackedCandidate.Clan,
                    resolvedTrackedCandidate,
                    "preserved_transferred_heir");
                TraceDynasticSuccession(
                    $"preserved transferred dynastic heir for {kingdom.StringId}: heir={currentTrackedHeir.StringId}; carrier={resolvedTrackedCandidate.StringId}; carrier_clan={resolvedTrackedCandidate.Clan?.StringId ?? "none"}.");
                return;
            }

            if (currentTrackedHeir != null && currentTrackedHeir.Clan != kingdom.RulingClan)
            {
                TraceDynasticSuccession(
                    $"released transferred dynastic heir for {kingdom.StringId}: heir={currentTrackedHeir.StringId}; " +
                    $"carrier_clan={currentTrackedHeir.Clan?.StringId ?? "none"}; " +
                    $"carrier_kingdom={currentTrackedHeir.Clan?.Kingdom?.StringId ?? "none"}; reason=carrier no longer belongs to the succession realm.");
            }

            if (TryGetTrackedDynasticHeir(kingdom, out Hero trackedHeir)
                && SuccessionLawHelper.IsEligibleUnderSuccessionLaws(
                    trackedHeir,
                    ResolveLawfulSuccessionRoot(kingdom),
                    SuccessionLawHelper.GetLawsForKingdom(kingdom))
                && IsPlayerMarriageClaimCarrier(kingdom, trackedHeir))
            {
                SetDynasticState(kingdom, trackedHeir, ResolveTrackedRightfulDynastyClan(kingdom, trackedHeir), Clan.PlayerClan, Clan.PlayerClan?.Leader ?? trackedHeir, "player_marriage_claim");
                return;
            }

            if (reigningHeir != null)
                SetDynasticState(kingdom, reigningHeir, ResolveDynasticOriginClan(kingdom, reigningHeir.Clan), reigningHeir.Clan, reigningHeir, "calculated");
            else
                ClearDynasticState(kingdom);
        }

        private void RefreshDynasticHeirCacheForAllKingdoms()
        {
            _changedHouseholdHeroes.Clear();
            _changedHouseholdClans.Clear();
            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom == null || kingdom.IsEliminated || kingdom.RulingClan?.Leader == null)
                    continue;

                RecalculateHeirForKingdom(kingdom);
            }
        }

        // ------------------------------------------------------------
        // PUBLIC API
        // -------------------- PUBLIC API --------------------

        // What does this method do?
        // Returns the stored dynastic heir for the kingdom's ruling clan. If no entry exists
        // (new game, first access) the heir is calculated now and cached. If the stored hero is
        // no longer valid (dead, disabled, or moved kingdoms) the entry is purged and null returned.
        public Hero GetDynasticHeir(Kingdom kingdom)
        {
            EnsureCollectionsInitialized();
            if (kingdom == null) return null;
            if (CrownAccessionBehavior.IsHereditaryRealm(kingdom))
                return HereditaryRealmSuccession.GetLine(kingdom).FirstOrDefault();

            Clan rightfulDynasty = GetRightfulDynastyClan(kingdom) ?? kingdom.RulingClan;
            Hero regencyWard = RegencyBehavior.Instance?.GetWard(rightfulDynasty);
            if (regencyWard != null
                && regencyWard.IsAlive
                && !regencyWard.IsDisabled
                && regencyWard.Clan == rightfulDynasty)
            {
                return regencyWard;
            }

            Hero successionRoot = ResolveLawfulSuccessionRoot(kingdom);
            if (!TryGetTrackedDynasticHeir(kingdom, out Hero hero)
                || !SuccessionLawHelper.IsEligibleUnderSuccessionLaws(
                    hero,
                    successionRoot,
                    SuccessionLawHelper.GetLawsForKingdom(kingdom)))
            {
                RecalculateHeirForKingdom(kingdom);
                if (!TryGetTrackedDynasticHeir(kingdom, out hero))
                    return null;
            }

            return hero;
        }

        public Hero GetDynasticSuccessionCandidate(Kingdom kingdom)
        {
            if (CrownAccessionBehavior.IsHereditaryRealm(kingdom))
                return HereditaryRealmSuccession.GetLine(kingdom).FirstOrDefault();
            Hero dynasticHeir = GetDynasticHeir(kingdom);
            return ResolveDynasticSuccessionCandidate(kingdom, dynasticHeir);
        }

        public Clan GetRightfulDynastyClan(Kingdom kingdom)
        {
            if (kingdom == null)
                return null;
            if (CrownAccessionBehavior.IsHereditaryRealm(kingdom)) return kingdom.RulingClan;

            if (TryGetDynasticState(kingdom, out DynasticSuccessionStateRecord state)
                && state != null
                && !string.IsNullOrWhiteSpace(state.RightfulDynastyClanId))
            {
                Clan rightfulDynasty = ResolveClanById(state.RightfulDynastyClanId);
                if (rightfulDynasty != null && !rightfulDynasty.IsEliminated)
                    return rightfulDynasty;
            }

            return kingdom.RulingClan;
        }

        public bool TryGetCachedDynasticHeir(Kingdom kingdom, out Hero heir)
        {
            EnsureCollectionsInitialized();
            if (CrownAccessionBehavior.IsHereditaryRealm(kingdom))
            {
                return TryGetCachedReigningDynasticHeir(kingdom, out heir);
            }
            return TryGetTrackedDynasticHeir(kingdom, out heir);
        }

        public Hero GetReigningHouseSuccessionCandidate(Kingdom kingdom)
        {
            if (CrownAccessionBehavior.IsHereditaryRealm(kingdom))
                return HereditaryRealmSuccession.GetLine(kingdom).FirstOrDefault();
            Hero heir = CalculateBestDynasticHeirForKingdom(kingdom);
            // Keep the existing player-marriage Crown carrier without replacing the
            // controlled player-clan head. Displaced dynasties are not consulted here.
            return heir?.Clan == Clan.PlayerClan && kingdom.RulingClan != Clan.PlayerClan
                ? Clan.PlayerClan.Leader : heir;
        }

        public void RecognizeLawfulCrownAccession(Kingdom kingdom)
        {
            ClearDynasticState(kingdom);
            _usurperKingdoms.Remove(kingdom.StringId);
            RecalculateHeirForKingdom(kingdom);
        }

        public bool TryGetCachedReigningDynasticHeir(Kingdom kingdom, out Hero heir)
        {
            heir = null;
            if (kingdom == null || kingdom.IsEliminated || kingdom.RulingClan?.Leader == null)
                return false;
            SuccessionLawSet laws = SuccessionLawHelper.GetLawsForKingdom(kingdom);
            if (SuccessionRealmRules.Classify(laws.SuccessionLaw) == RealmSuccessionSystem.Hereditary)
            {
                Hero sovereign = RegencyBehavior.Instance?.GetLegalClanHead(kingdom.RulingClan)
                    ?? kingdom.RulingClan?.Leader;
                if (_reigningDynasticHeirsByKingdom.TryGetValue(kingdom.StringId, out Hero cached))
                {
                    if (cached == null) return false;
                    if (cached.IsAlive && !cached.IsDisabled
                        && !BellumIntegrationBehavior.IsBarred(cached)
                        && RegencyBehavior.Instance?.IsGeneratedRegent(cached) != true
                        && HereditaryRealmSuccession.IsBloodRelative(cached, sovereign)
                        && HereditaryRealmSuccession.CanConsiderClan(cached.Clan, kingdom)
                        && SuccessionLawHelper.IsEligibleUnderGenderLaw(cached, laws)
                        && CrownAccessionBehavior.Instance?.GetAbdicatedMonarchs(kingdom).Contains(cached) != true)
                    {
                        heir = cached;
                        return true;
                    }
                }
                heir = HereditaryRealmSuccession.GetLine(kingdom, sovereign, laws).FirstOrDefault();
                SetReigningDynasticHeir(kingdom, heir);
                return heir != null;
            }
            Clan rulingClan = kingdom.RulingClan;
            Hero successionRoot = RegencyBehavior.Instance?.GetLegalClanHead(rulingClan)
                ?? rulingClan.Leader;
            if (!_reigningDynasticHeirsByKingdom.TryGetValue(kingdom.StringId, out Hero cachedHeir)
                || cachedHeir == null
                || !cachedHeir.IsAlive
                || cachedHeir.IsDisabled
                || BellumIntegrationBehavior.IsBarred(cachedHeir)
                || cachedHeir == successionRoot
                || !SuccessionLawHelper.IsEligibleUnderSuccessionLaws(
                    cachedHeir,
                    successionRoot,
                    laws)
                || !IsClanAttachedToSuccessionRealm(cachedHeir.Clan, kingdom)
                || (cachedHeir.Clan != rulingClan
                    && !IsRecognizedCadetBranch(cachedHeir.Clan, rulingClan)
                    && !IsPlayerMarriageClaimCarrier(kingdom, cachedHeir)))
            {
                SetReigningDynasticHeir(kingdom, null);
                return false;
            }

            heir = cachedHeir;
            return true;
        }

        private void SetReigningDynasticHeir(Kingdom kingdom, Hero heir)
        {
            if (kingdom == null || string.IsNullOrWhiteSpace(kingdom.StringId))
                return;

            _reigningDynasticHeirsByKingdom.TryGetValue(kingdom.StringId, out Hero previous);
            _reigningDynasticHeirsByKingdom[kingdom.StringId] = heir;
            if (previous == heir)
                return;
            _displayHeirIndexDirty = true;
            Patches.FeudalTitleHeroNamePatch.InvalidateHero(previous);
            Patches.FeudalTitleHeroNamePatch.InvalidateHero(heir);
        }

        internal IEnumerable<Kingdom> GetReigningHeirRealmsForDisplay(Hero hero)
        {
            if (hero == null) return Array.Empty<Kingdom>();
            if (_displayHeirIndexDirty)
            {
                _displayRealmsByHeir.Clear();
                // Retain Kingdom.All order for heroes who inherit more than one Crown.
                foreach (Kingdom realm in Kingdom.All)
                {
                    if (realm == null || realm.IsEliminated || realm.RulingClan == null
                        || !_reigningDynasticHeirsByKingdom.TryGetValue(realm.StringId, out Hero heir) || heir == null)
                        continue;
                    if (!_displayRealmsByHeir.TryGetValue(heir, out List<Kingdom> realms))
                        _displayRealmsByHeir[heir] = realms = new List<Kingdom>();
                    realms.Add(realm);
                }
                _displayHeirIndexDirty = false;
                _displayHeirIndexBuilds++;
            }
            return _displayRealmsByHeir.TryGetValue(hero, out List<Kingdom> result)
                ? (IEnumerable<Kingdom>)result : Array.Empty<Kingdom>();
        }

        internal string BuildDisplayCacheDiagnostics(bool reset)
        {
            string result = $"heir_index_builds={_displayHeirIndexBuilds}; indexed_heirs={_displayRealmsByHeir.Count}";
            if (reset) _displayHeirIndexBuilds = 0;
            return result;
        }

        private Hero ResolveDynasticSuccessionCandidate(Kingdom kingdom, Hero dynasticHeir)
        {
            if (dynasticHeir?.Clan == null || BellumIntegrationBehavior.IsBarred(dynasticHeir))
                return null;
            if (!SuccessionLawHelper.IsEligibleUnderSuccessionLaws(
                    dynasticHeir,
                    ResolveLawfulSuccessionRoot(kingdom),
                    SuccessionLawHelper.GetLawsForKingdom(kingdom)))
                return null;

            if (!IsClanAttachedToSuccessionRealm(dynasticHeir.Clan, kingdom))
                return null;

            if (dynasticHeir.Clan == kingdom.RulingClan)
                return dynasticHeir;

            if (dynasticHeir.Clan == Clan.PlayerClan)
                return dynasticHeir.Clan.Leader ?? dynasticHeir;

            if (IsRecognizedCadetBranch(dynasticHeir.Clan, kingdom.RulingClan))
                return dynasticHeir;

            if (IsSameHero(dynasticHeir.Clan.Leader, dynasticHeir))
                return dynasticHeir;

            Hero inheritedClanCandidate = GetAcceptedMarriageTransferCandidate(dynasticHeir);
            if (IsClanAttachedToSuccessionRealm(inheritedClanCandidate?.Clan, kingdom))
                return inheritedClanCandidate;

            return IsClanAttachedToSuccessionRealm(dynasticHeir.Clan, kingdom) ? dynasticHeir : null;
        }

        internal static bool IsClanAttachedToSuccessionRealm(Clan clan, Kingdom kingdom)
        {
            if (clan == null || kingdom == null)
                return false;

            if (clan.Kingdom == kingdom)
                return true;

            Kingdom temporaryKingdom = clan.Kingdom;
            if (temporaryKingdom == null)
                return false;

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject civilWarFaction = factionManager?.GetFactionByRebelKingdom(temporaryKingdom)
                ?? factionManager?.GetFactionByTrackedRebelKingdomId(temporaryKingdom.StringId);
            if (civilWarFaction?.ParentKingdom == kingdom)
                return true;

            ClaimFeudWarBehavior feudWars = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>();
            return feudWars?.IsTemporaryFeudKingdomForParent(temporaryKingdom, kingdom) == true;
        }

        private Hero ResolveLawfulSuccessionRoot(Kingdom kingdom)
        {
            Clan dynastyClan = GetRightfulDynastyClan(kingdom) ?? kingdom?.RulingClan;
            return RegencyBehavior.Instance?.GetLegalClanHead(dynastyClan)
                ?? dynastyClan?.Leader;
        }


        private bool TryGetTrackedDynasticHeir(Kingdom kingdom, out Hero hero)
        {
            hero = null;
            if (kingdom == null) return false;

            if (!TryGetDynasticState(kingdom, out DynasticSuccessionStateRecord state))
                return false;

            hero = ResolveHeroById(state.HeirHeroId);
            if (BellumIntegrationBehavior.IsBarred(hero)) { hero = null; return false; }
            if (hero == null || !hero.IsAlive || hero.IsDisabled
                || RegencyBehavior.Instance?.IsGeneratedRegent(hero) == true)
            {
                ClearDynasticState(kingdom);
                hero = null;
                return false;
            }

            return true;
        }

        private Hero CalculateBestDynasticHeirForKingdom(Kingdom kingdom)
        {
            if (CrownAccessionBehavior.IsHereditaryRealm(kingdom))
                return HereditaryRealmSuccession.GetLine(kingdom).FirstOrDefault();
            Clan dynastyClan = kingdom?.RulingClan;
            Hero ruler = RegencyBehavior.Instance?.GetLegalClanHead(dynastyClan)
                ?? dynastyClan?.Leader;
            if (dynastyClan == null || ruler == null)
                return null;

            SuccessionLawSet laws = SuccessionLawHelper.GetLawsForKingdom(kingdom);
            List<Hero> candidates = new List<Hero>();

            foreach (Clan candidateClan in GetDynasticCandidateClans(kingdom))
            {
                foreach (Hero member in candidateClan.Heroes)
                {
                    if (member == null || member == ruler || !member.IsAlive || member.IsDisabled)
                        continue;
                    if (member.DeathMark != KillCharacterAction.KillCharacterActionDetail.None
                        || RegencyBehavior.Instance?.IsGeneratedRegent(member) == true)
                        continue;
                    if (member.Clan == null || NobleClanEligibilityHelper.IsNonPlayerMinorClan(member.Clan) || member.Clan.IsUnderMercenaryService)
                        continue;
                    if (!SuccessionLawHelper.IsEligibleUnderSuccessionLaws(member, ruler, laws))
                        continue;
                    if (member.Clan == Clan.PlayerClan && member.Clan != dynastyClan && !IsPlayerMarriageClaimCarrier(kingdom, member))
                        continue;

                    candidates.Add(member);
                }
            }

            return SuccessionLawHelper.OrderSuccessionCandidates(
                candidates,
                ruler,
                laws,
                includeUnderage: true).FirstOrDefault();
        }

        private bool TryResolvePendingKingSelectionHeir(Kingdom kingdom, out Hero heir)
        {
            heir = null;
            if (kingdom?.UnresolvedDecisions == null || !kingdom.UnresolvedDecisions.OfType<KingSelectionKingdomDecision>().Any())
                return false;

            if (TryGetTrackedDynasticHeir(kingdom, out Hero trackedHeir)
                && TryResolveValidDynasticSuccessionCandidate(kingdom, trackedHeir, out _))
            {
                heir = trackedHeir;
                return true;
            }

            Hero rulingClanLeader = kingdom.RulingClan?.Leader;
            if (IsValidPendingSuccessionClaimant(kingdom, rulingClanLeader))
            {
                heir = rulingClanLeader;
                return true;
            }

            return false;
        }

        private bool TryResolveValidDynasticSuccessionCandidate(Kingdom kingdom, Hero dynasticHeir, out Hero successionCandidate)
        {
            successionCandidate = ResolveDynasticSuccessionCandidate(kingdom, dynasticHeir);
            return kingdom != null
                && successionCandidate != null
                && successionCandidate.IsAlive
                && !successionCandidate.IsDisabled
                && successionCandidate.Age >= SuccessionLawHelper.GetAgeOfMajority()
                && successionCandidate.Clan != null
                && IsClanAttachedToSuccessionRealm(successionCandidate.Clan, kingdom)
                && !successionCandidate.Clan.IsEliminated
                && !successionCandidate.Clan.IsUnderMercenaryService
                && !successionCandidate.Clan.IsBanditFaction;
        }

        private static bool IsValidPendingSuccessionClaimant(Kingdom kingdom, Hero claimant)
        {
            return kingdom != null
                && claimant != null
                && claimant.IsAlive
                && !claimant.IsDisabled
                && claimant.Age >= SuccessionLawHelper.GetAgeOfMajority()
                && claimant.Clan != null
                && claimant.Clan == kingdom.RulingClan
                && claimant.Clan.Kingdom == kingdom;
        }

        private IEnumerable<Clan> GetDynasticCandidateClans(Kingdom kingdom)
        {
            Clan dynastyClan = kingdom?.RulingClan;
            if (dynastyClan == null)
                yield break;

            yield return dynastyClan;

            foreach (Clan clan in kingdom.Clans)
            {
                if (clan == null || clan == dynastyClan || clan.IsEliminated)
                    continue;

                if (IsRecognizedCadetBranch(clan, dynastyClan))
                    yield return clan;
            }

            if (Clan.PlayerClan != null && Clan.PlayerClan != dynastyClan)
                yield return Clan.PlayerClan;
        }

        private bool IsPlayerMarriageClaimCarrier(Kingdom kingdom, Hero candidate)
        {
            Clan dynastyClan = kingdom?.RulingClan;
            Hero ruler = dynastyClan?.Leader;
            if (candidate == null || dynastyClan == null || ruler == null)
                return false;

            if (IsPlayerClanAttachedToForeignKingdom(kingdom))
                return false;

            if (candidate.Clan != Clan.PlayerClan || candidate.Spouse == null || candidate.Spouse.Clan != Clan.PlayerClan)
                return false;

            if (!IsDirectPlayerMarriageClaimSpouse(candidate.Spouse, candidate))
                return false;

            if (!candidate.IsAlive || candidate.IsDisabled
                || candidate.Age < SuccessionLawHelper.GetAgeOfMajority())
                return false;

            if (candidate.Father == ruler || candidate.Mother == ruler || AreSiblings(candidate, ruler))
                return true;

            if (candidate.Father?.Clan == dynastyClan || candidate.Mother?.Clan == dynastyClan)
                return true;

            return dynastyClan.Heroes.Any(h => h != null
                                            && h != candidate
                                            && (candidate.Father == h || candidate.Mother == h));
        }

        private static bool IsDirectPlayerMarriageClaimSpouse(Hero spouse, Hero dynasticHeir)
        {
            if (spouse == null || dynasticHeir == null || spouse.Clan != Clan.PlayerClan)
                return false;

            if (spouse == Clan.PlayerClan?.Leader || spouse == Hero.MainHero)
                return true;

            return IsPlayerImmediateHeir(spouse, dynasticHeir);
        }

        private static bool IsPlayerImmediateHeir(Hero candidate, Hero excludedDynasticHeir = null)
        {
            if (candidate == null || Clan.PlayerClan == null)
                return false;

            IEnumerable<Hero> candidates = Clan.PlayerClan.GetHeirApparents()?.Keys
                .Where(h => h != null && h != excludedDynasticHeir)
                .ToList();
            if (candidates == null)
                return false;

            Hero deadHero = Clan.PlayerClan.Leader ?? Hero.MainHero;
            if (deadHero == null)
                return false;

            return SuccessionLawHelper.TryResolveLegalPlayerHeir(
                    deadHero,
                    candidates,
                    out Hero legalHeir,
                    out _,
                    out _)
                && legalHeir == candidate;
        }

        private static bool AreSiblings(Hero a, Hero b)
        {
            if (a == null || b == null)
                return false;

            return (a.Father != null && a.Father == b.Father)
                || (a.Mother != null && a.Mother == b.Mother);
        }

        private static bool IsSameHero(Hero first, Hero second)
        {
            if (ReferenceEquals(first, second))
                return first != null;

            return first != null
                && second != null
                && !string.IsNullOrWhiteSpace(first.StringId)
                && first.StringId == second.StringId;
        }

        private Hero GetAcceptedMarriageTransferCandidate(Hero dynasticHeir)
        {
            Clan marriedClan = dynasticHeir?.Clan;
            Hero spouse = dynasticHeir?.Spouse;
            if (marriedClan == null || spouse == null || spouse.Clan != marriedClan)
                return null;

            if (marriedClan == Clan.PlayerClan)
                return IsDirectPlayerMarriageClaimSpouse(spouse, dynasticHeir)
                    ? marriedClan.Leader ?? spouse
                    : null;

            if (IsSameHero(spouse, marriedClan.Leader))
                return spouse;

            return IsDirectHeirExcludingDynasticHeir(marriedClan, spouse, dynasticHeir) ? spouse : null;
        }

        private bool IsDirectHeirExcludingDynasticHeir(Clan clan, Hero candidate, Hero excludedHero)
        {
            if (clan?.Leader == null
                || candidate == null
                || candidate.Clan?.StringId != clan.StringId
                || IsSameHero(candidate, clan.Leader))
                return false;

            // Use the same ordered succession line exposed by Bellum elsewhere. The old local
            // score reconstruction ignored configured succession laws and could classify a
            // secondary son as the direct heir, incorrectly suppressing a cadet marriage.
            Hero directHeir = SuccessionLawHelper.GetLegalSuccessionLine(clan)
                .FirstOrDefault(member => !IsSameHero(member, excludedHero));
            return IsSameHero(directHeir, candidate);
        }

        private bool IsRecognizedCadetBranch(Clan clan, Clan dynastyClan)
        {
            return clan != null
                && dynastyClan != null
                && _cadetBranchOrigins.TryGetValue(clan.StringId, out string originClanId)
                && originClanId == dynastyClan.StringId;
        }

        internal static TextObject BuildRoyalHeiressCadetName(Clan dynastyClan, Clan marriedClan)
        {
            string dynastyName = CleanClanName(dynastyClan?.Name?.ToString());
            string branchName = StripCadetPrefixWhenSecondName(CleanClanName(marriedClan?.Name?.ToString()), dynastyClan, marriedClan);

            if (string.IsNullOrWhiteSpace(dynastyName))
                return string.IsNullOrWhiteSpace(branchName)
                    ? new TextObject("{=BC_CadetName_Generic}Cadet Branch")
                    : new TextObject("{=!}" + branchName);

            if (string.IsNullOrWhiteSpace(branchName))
                return new TextObject("{=!}" + dynastyName);

            TextObject text = new TextObject("{=BC_DynasticHeiress_CadetName}{DYNASTY_NAME}-{BRANCH_NAME}");
            text.SetTextVariable("DYNASTY_NAME", new TextObject("{=!}" + dynastyName));
            text.SetTextVariable("BRANCH_NAME", new TextObject("{=!}" + branchName));
            return text;
        }

        private static string StripCadetPrefixWhenSecondName(string name, Clan dynastyClan, Clan marriedClan)
        {
            if (string.IsNullOrWhiteSpace(name))
                return name;

            string cultureId = marriedClan?.Culture?.StringId?.ToLowerInvariant()
                ?? dynastyClan?.Culture?.StringId?.ToLowerInvariant()
                ?? string.Empty;

            switch (cultureId)
            {
                case "vlandia":
                    return StripLeadingPrefix(name, "dey");
                case "battania":
                    return StripLeadingPrefix(name, "fen");
                case "aserai":
                    return StripLeadingPrefix(name, "Banu");
                default:
                    return name.Trim();
            }
        }

        private static string StripLeadingPrefix(string name, string prefix)
        {
            string trimmed = name?.Trim() ?? string.Empty;
            if (trimmed.Length <= prefix.Length)
                return trimmed;

            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return trimmed;

            if (!char.IsWhiteSpace(trimmed[prefix.Length]))
                return trimmed;

            string stripped = trimmed.Substring(prefix.Length).Trim();
            return string.IsNullOrWhiteSpace(stripped) ? trimmed : stripped;
        }

        private static string CleanClanName(string name)
        {
            return string.IsNullOrWhiteSpace(name)
                ? string.Empty
                : string.Join(" ", name.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private void PruneCadetBranchOrigins()
        {
            if (!_canResolveSavedObjects)
            {
                PruneMalformedCadetBranchOrigins();
                return;
            }

            foreach (string cadetClanId in _cadetBranchOrigins.Keys.ToList())
            {
                Clan cadetClan = ResolveClanById(cadetClanId);
                if (cadetClan == null || cadetClan.IsEliminated)
                    _cadetBranchOrigins.Remove(cadetClanId);
            }
        }

        private void PruneMalformedCadetBranchOrigins()
        {
            if (_cadetBranchOrigins == null)
                _cadetBranchOrigins = new Dictionary<string, string>();

            foreach (string cadetClanId in _cadetBranchOrigins.Keys.ToList())
            {
                if (string.IsNullOrWhiteSpace(cadetClanId) || string.IsNullOrWhiteSpace(_cadetBranchOrigins[cadetClanId]))
                    _cadetBranchOrigins.Remove(cadetClanId);
            }
        }

        private void EnsureCollectionsInitialized()
        {
            if (_dynasticHeirs == null) _dynasticHeirs = new Dictionary<string, string>();
            if (_dynasticStates == null) _dynasticStates = new Dictionary<string, DynasticSuccessionStateRecord>();
            if (_cadetBranchOrigins == null) _cadetBranchOrigins = new Dictionary<string, string>();
            if (_pendingCadetMarriages == null) _pendingCadetMarriages = new List<PendingCadetMarriageRecord>();
            if (_usurperKingdoms == null) _usurperKingdoms = new List<string>();
        }

        private void PruneDynasticStates()
        {
            if (_dynasticStates == null)
            {
                _dynasticStates = new Dictionary<string, DynasticSuccessionStateRecord>();
                return;
            }

            if (!_canResolveSavedObjects)
            {
                PruneMalformedDynasticStates();
                return;
            }

            var stateKeysToRemove = new List<string>();
            var mirroredHeirKeysToRemove = new List<string>();
            try
            {
                int inspected = 0;
                foreach (KeyValuePair<string, DynasticSuccessionStateRecord> entry in _dynasticStates)
                {
                    if (++inspected > MaxDynasticCacheEnumeration)
                    {
                        LogDynasticCacheEnumerationFailure("succession state cache", "resolved pruning", null);
                        return;
                    }

                    string kingdomId = entry.Key;
                    DynasticSuccessionStateRecord state = entry.Value;
                    Kingdom kingdom = ResolveKingdomById(state?.KingdomId ?? kingdomId);
                    Hero heir = ResolveHeroById(state?.HeirHeroId);
                    Clan carrierClan = ResolveClanById(state?.ClaimCarrierClanId);

                    if (kingdom == null || kingdom.IsEliminated || heir == null || !heir.IsAlive || heir.IsDisabled)
                    {
                        stateKeysToRemove.Add(kingdomId);
                        mirroredHeirKeysToRemove.Add(kingdomId);
                        continue;
                    }

                    if (carrierClan != null && (carrierClan.IsEliminated || carrierClan.IsBanditFaction))
                        stateKeysToRemove.Add(kingdomId);
                }
            }
            catch (Exception ex) when (IsDynasticCacheEnumerationException(ex))
            {
                LogDynasticCacheEnumerationFailure("succession state cache", "resolved pruning", ex);
                return;
            }

            foreach (string kingdomId in stateKeysToRemove)
                _dynasticStates.Remove(kingdomId);

            foreach (string kingdomId in mirroredHeirKeysToRemove)
                _dynasticHeirs.Remove(kingdomId);
        }

        private void PruneMalformedDynasticStates()
        {
            if (_dynasticStates == null)
                _dynasticStates = new Dictionary<string, DynasticSuccessionStateRecord>();

            var malformedStateKeys = new List<string>();
            try
            {
                int inspected = 0;
                foreach (KeyValuePair<string, DynasticSuccessionStateRecord> entry in _dynasticStates)
                {
                    if (++inspected > MaxDynasticCacheEnumeration)
                    {
                        LogDynasticCacheEnumerationFailure("succession state cache", "malformed-state pruning", null);
                        return;
                    }

                    string kingdomId = entry.Key;
                    DynasticSuccessionStateRecord state = entry.Value;
                    if (string.IsNullOrWhiteSpace(kingdomId)
                        || state == null
                        || string.IsNullOrWhiteSpace(state.KingdomId)
                        || string.IsNullOrWhiteSpace(state.HeirHeroId))
                    {
                        malformedStateKeys.Add(kingdomId);
                    }
                }
            }
            catch (Exception ex) when (IsDynasticCacheEnumerationException(ex))
            {
                LogDynasticCacheEnumerationFailure("succession state cache", "malformed-state pruning", ex);
                return;
            }

            foreach (string kingdomId in malformedStateKeys)
            {
                if (kingdomId == null)
                    continue;

                _dynasticStates.Remove(kingdomId);
                _dynasticHeirs.Remove(kingdomId);
            }

            var malformedHeirKeys = new List<string>();
            try
            {
                int inspected = 0;
                foreach (KeyValuePair<string, string> entry in _dynasticHeirs)
                {
                    if (++inspected > MaxDynasticCacheEnumeration)
                    {
                        LogDynasticCacheEnumerationFailure("legacy heir cache", "malformed-heir pruning", null);
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value))
                        malformedHeirKeys.Add(entry.Key);
                }
            }
            catch (Exception ex) when (IsDynasticCacheEnumerationException(ex))
            {
                LogDynasticCacheEnumerationFailure("legacy heir cache", "malformed-heir pruning", ex);
                return;
            }

            foreach (string kingdomId in malformedHeirKeys)
            {
                if (kingdomId != null)
                    _dynasticHeirs.Remove(kingdomId);
            }
        }

        private static bool IsDynasticCacheEnumerationException(Exception ex)
        {
            return ex is OutOfMemoryException
                || ex is InvalidOperationException
                || ex is IndexOutOfRangeException
                || ex is ArgumentException;
        }

        private void LogDynasticCacheEnumerationFailure(string cacheName, string operation, Exception ex)
        {
            if (_dynasticCacheEnumerationWarningLogged)
                return;

            _dynasticCacheEnumerationWarningLogged = true;
            string detail = ex == null
                ? $"enumeration exceeded the safety limit of {MaxDynasticCacheEnumeration} records"
                : $"{ex.GetType().Name}: {ex.Message}";
            BellumCivileLogger.Log(
                $"Dynastic cache hardening skipped {operation} for the {cacheName} to preserve saved succession data; {detail}.");
        }

        private void SetDynasticState(Kingdom kingdom, Hero heir, Clan rightfulDynastyClan, Clan claimCarrierClan, Hero claimCarrierHero, string source, CampaignTime? lockedUntil = null)
        {
            EnsureCollectionsInitialized();

            if (kingdom == null || string.IsNullOrEmpty(kingdom.StringId))
                return;

            if (heir == null || !heir.IsAlive || heir.IsDisabled)
            {
                ClearDynasticState(kingdom);
                return;
            }

            Clan dynastyClan = rightfulDynastyClan ?? ResolveDynasticOriginClan(kingdom, heir.Clan) ?? kingdom.RulingClan ?? heir.Clan;
            Clan carrierClan = claimCarrierClan ?? ResolveDynasticSuccessionCandidate(kingdom, heir)?.Clan ?? heir.Clan;
            Hero carrierHero = claimCarrierHero ?? ResolveDynasticSuccessionCandidate(kingdom, heir) ?? heir;

            _dynasticStates[kingdom.StringId] = new DynasticSuccessionStateRecord(
                kingdom.StringId,
                dynastyClan?.StringId ?? string.Empty,
                heir.StringId ?? string.Empty,
                carrierClan?.StringId ?? string.Empty,
                carrierHero?.StringId ?? string.Empty,
                source,
                lockedUntil ?? CampaignTime.Zero,
                (float)CampaignTime.Now.ToDays);

            _dynasticHeirs[kingdom.StringId] = heir.StringId;
            if (!string.IsNullOrWhiteSpace(heir.StringId))
                _heroesById[heir.StringId] = heir;
        }

        private void ClearDynasticState(Kingdom kingdom)
        {
            if (kingdom == null)
                return;

            _dynasticStates.Remove(kingdom.StringId);
            _dynasticHeirs.Remove(kingdom.StringId);
        }

        private bool TryGetDynasticState(Kingdom kingdom, out DynasticSuccessionStateRecord state)
        {
            state = null;
            EnsureCollectionsInitialized();
            if (kingdom == null)
                return false;

            if (_dynasticStates.TryGetValue(kingdom.StringId, out state) && state != null)
                return true;

            if (_dynasticHeirs.TryGetValue(kingdom.StringId, out string legacyHeirId))
            {
                Hero legacyHeir = ResolveHeroById(legacyHeirId);
                if (legacyHeir != null && legacyHeir.IsAlive && !legacyHeir.IsDisabled)
                {
                    SetDynasticState(kingdom, legacyHeir, ResolveDynasticOriginClan(kingdom, legacyHeir.Clan), legacyHeir.Clan, legacyHeir, "legacy_migration");
                    return _dynasticStates.TryGetValue(kingdom.StringId, out state) && state != null;
                }
            }

            return false;
        }

        private static bool IsPlayerClanAttachedToForeignKingdom(Kingdom originKingdom)
        {
            return Clan.PlayerClan?.Kingdom != null
                && originKingdom != null
                && Clan.PlayerClan.Kingdom != originKingdom;
        }

        private bool TryShowDynasticRightsRenunciation(Hero dynasticHeir, Hero spouse, Clan clanAfterMarriage)
        {
            Kingdom kingdom = dynasticHeir?.Clan?.Kingdom;
            Clan brideClan = dynasticHeir?.Clan;
            Clan spouseClan = spouse?.Clan;
            Kingdom spouseKingdom = spouseClan?.Kingdom;

            if (dynasticHeir == null
                || spouse == null
                || brideClan == null
                || kingdom == null
                || spouseClan == null
                || spouseKingdom == null
                || spouseKingdom == kingdom
                || clanAfterMarriage != spouseClan)
            {
                return false;
            }

            if (!dynasticHeir.IsFemale || spouseClan == Clan.PlayerClan)
                return false;

            if (spouseClan.IsEliminated
                || spouseClan.IsMinorFaction
                || spouseClan.IsClanTypeMercenary
                || spouseClan.IsUnderMercenaryService
                || spouseClan.IsBanditFaction)
            {
                return false;
            }

            if (!IsCurrentDynasticHeirForMarriage(kingdom, dynasticHeir, "rights renunciation"))
                return false;

            if (!IsSameHero(spouse, spouseClan.Leader)
                && !IsDirectHeirExcludingDynasticHeir(spouseClan, spouse, dynasticHeir))
                return false;

            ShowDynasticRightsRenouncedMessage(dynasticHeir, spouse, brideClan, spouseClan, spouseKingdom, kingdom);
            TraceDynasticMarriage(
                $"dynastic heiress rights renounced: heiress={dynasticHeir.StringId}; kingdom={kingdom.StringId}; spouse={spouse.StringId}; spouse_clan={spouseClan.StringId}; spouse_kingdom={spouseKingdom.StringId}.");
            return true;
        }

        private bool IsCurrentDynasticHeirForMarriage(Kingdom kingdom, Hero possibleDynasticHeir, string context)
        {
            if (kingdom == null || possibleDynasticHeir == null)
                return false;

            Hero trackedHeir = GetDynasticHeir(kingdom);
            if (IsSameHero(trackedHeir, possibleDynasticHeir))
                return true;

            // Marriage can happen between daily refreshes, and 1.4.5 appears to be less forgiving
            // around the moment vanilla evaluates clan transfer. Recalculate once before rejecting
            // a dynastic heiress so stale heir cache data cannot make her lose the claim.
            RecalculateHeirForKingdom(kingdom);
            trackedHeir = GetDynasticHeir(kingdom);
            bool isHeir = IsSameHero(trackedHeir, possibleDynasticHeir);

            TraceDynasticMarriage(
                $"dynastic heiress check ({context}): candidate={possibleDynasticHeir.StringId}; kingdom={kingdom.StringId}; tracked={(trackedHeir != null ? trackedHeir.StringId : "none")}; accepted={isHeir}.");

            return isHeir;
        }

        private static void ShowDynasticRightsRenouncedMessage(Hero bride, Hero groom, Clan brideParentClan, Clan groomParentClan, Kingdom groomKingdom, Kingdom originKingdom)
        {
            if (bride == null || groom == null || brideParentClan == null || groomParentClan == null || groomKingdom == null)
                return;

            TextObject text = new TextObject(CrownAccessionBehavior.IsHereditaryRealm(originKingdom)
                ? "{=BC_DynasticHeiress_RightsRetained}Following the recent marriage between {BRIDE_NAME} of the {BRIDE_PARENT_CLAN} and {GROOM_NAME} of the {GROOM_PARENT_CLAN}, the princess has joined her husband's household in {GROOM_KINGDOM}, retaining her hereditary rights to her homeland's Crown."
                : "{=BC_DynasticHeiress_RightsRenounced}Following the recent marriage between {BRIDE_NAME} of the {BRIDE_PARENT_CLAN} and {GROOM_NAME} of the {GROOM_PARENT_CLAN}, according to the realm's inheritance laws the princess has renounced her dynastic rights and moved to join her husband's household in {GROOM_KINGDOM}.");
            text.SetTextVariable("BRIDE_NAME", bride.Name ?? new TextObject("?"));
            text.SetTextVariable("BRIDE_PARENT_CLAN", brideParentClan.Name ?? new TextObject("?"));
            text.SetTextVariable("GROOM_NAME", groom.Name ?? new TextObject("?"));
            text.SetTextVariable("GROOM_PARENT_CLAN", groomParentClan.Name ?? new TextObject("?"));
            text.SetTextVariable("GROOM_KINGDOM", groomKingdom.Name ?? new TextObject("?"));

            BellumCivileNotifications.Show(text, BellumNotificationColors.InheritanceWarning, primaryKingdom: originKingdom, primaryClan: brideParentClan, secondaryClan: groomParentClan);
        }

        private Clan ResolveDynasticOriginClan(Kingdom kingdom, Clan currentClan)
        {
            if (currentClan != null
                && _cadetBranchOrigins.TryGetValue(currentClan.StringId, out string originClanId))
            {
                Clan originClan = ResolveClanById(originClanId);
                if (originClan != null)
                    return originClan;
            }

            return kingdom?.RulingClan ?? currentClan;
        }

        private Clan ResolveTrackedRightfulDynastyClan(Kingdom kingdom, Hero trackedHeir)
        {
            if (kingdom != null
                && trackedHeir != null
                && TryGetDynasticState(kingdom, out DynasticSuccessionStateRecord state)
                && state != null
                && state.HeirHeroId == trackedHeir.StringId
                && !string.IsNullOrWhiteSpace(state.RightfulDynastyClanId))
            {
                Clan rightfulDynasty = ResolveClanById(state.RightfulDynastyClanId);
                if (rightfulDynasty != null && !rightfulDynasty.IsEliminated)
                    return rightfulDynasty;
            }

            return ResolveDynasticOriginClan(kingdom, trackedHeir?.Clan);
        }

        private Hero ResolveHeroById(string heroId)
        {
            if (string.IsNullOrEmpty(heroId))
                return null;

            if (_heroesById.TryGetValue(heroId, out Hero cachedHero))
            {
                if (cachedHero != null && cachedHero.IsAlive && !cachedHero.IsDisabled
                    && string.Equals(cachedHero.StringId, heroId, StringComparison.Ordinal))
                {
                    return cachedHero;
                }

                _heroesById.Remove(heroId);
            }

            try
            {
                // Campaign heroes are not guaranteed to remain addressable through
                // MBObjectManager on every game version or after save reconstruction.
                Hero hero = MBObjectManager.Instance?.GetObject<Hero>(heroId);
                if (hero != null && hero.IsAlive && !hero.IsDisabled)
                {
                    _heroesById[heroId] = hero;
                    return hero;
                }

                hero = Hero.AllAliveHeroes?
                    .FirstOrDefault(candidate => candidate != null && candidate.StringId == heroId);
                if (hero != null && hero.IsAlive && !hero.IsDisabled)
                {
                    _heroesById[heroId] = hero;
                    return hero;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private void RebuildHeroResolutionCache()
        {
            _heroesById.Clear();
            try
            {
                foreach (Hero hero in Hero.AllAliveHeroes)
                {
                    if (hero != null && hero.IsAlive && !hero.IsDisabled && !string.IsNullOrWhiteSpace(hero.StringId))
                        _heroesById[hero.StringId] = hero;
                }
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Dynastic heir hero cache initialization was incomplete: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static Clan ResolveClanById(string clanId)
        {
            if (string.IsNullOrEmpty(clanId)) return null;
            try
            {
                return Clan.All?.FirstOrDefault(clan => clan != null && clan.StringId == clanId);
            }
            catch
            {
                return null;
            }
        }

        private static Kingdom ResolveKingdomById(string kingdomId)
        {
            if (string.IsNullOrEmpty(kingdomId)) return null;
            try
            {
                return Kingdom.All?.FirstOrDefault(kingdom => kingdom != null && kingdom.StringId == kingdomId);
            }
            catch
            {
                return null;
            }
        }

        private static void TraceDynasticMarriage(string message)
        {
            BellumCivileDebug.TraceIfEnabled("marriage", message, requestInGameDisplay: true);
        }

        private static void TraceDynasticSuccession(string message)
        {
            BellumCivileDebug.TraceIfEnabled("succession", message, requestInGameDisplay: true);
        }

        public bool IsUsurper(Kingdom kingdom) =>
            kingdom != null && _usurperKingdoms.Contains(kingdom.StringId);


        public void MarkAsUsurper(Kingdom kingdom)
        {
            if (kingdom == null) return;
            if (!_usurperKingdoms.Contains(kingdom.StringId))
                _usurperKingdoms.Add(kingdom.StringId);
            _pendingUsurperKingdoms.Add(kingdom.StringId);
        }

        public void ClearUsurper(Kingdom kingdom)
        {
            if (kingdom == null) return;
            _usurperKingdoms.Remove(kingdom.StringId);
        }
    }
}
