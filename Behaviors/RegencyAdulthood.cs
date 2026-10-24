using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Behaviors
{
    public sealed partial class RegencyBehavior
    {
        private static readonly AccessTools.FieldRef<AgingCampaignBehavior, Dictionary<Hero, int>> NativeYoungHeroes =
            AccessTools.FieldRefAccess<AgingCampaignBehavior, Dictionary<Hero, int>>("_heroesYoungerThanHeroComesOfAge");
        private readonly HashSet<Hero> _adultWardHandovers = new HashSet<Hero>();
        private readonly HashSet<Hero> _adulthoodEventsInProgress = new HashSet<Hero>();

        internal RegencyRecord BeginWardAdulthoodEvent(Hero ward)
        {
            if (ward?.Clan == null || !TryGetRegency(ward.Clan, out var record)
                || record.WardHeroId != ward.StringId || !_adulthoodEventsInProgress.Add(ward)) return null;
            record.BeginAdulthood();
            return record;
        }

        internal void FinishWardAdulthoodEvent(Hero ward, RegencyRecord record, Exception failure)
        {
            _adulthoodEventsInProgress.Remove(ward);
            if (record.WardHeroId != ward.StringId) return;
            if (failure == null) record.CompleteAdulthood(ward);
            else
            {
                record.FailAdulthood(failure.Message);
                BellumCivileLogger.Log($"Regency adulthood initialization interrupted; ward={ward.StringId}; error={failure}");
            }
        }

        private bool EnsureWardAdulthood(Clan clan, RegencyRecord record, Hero ward)
        {
            if (ward == null || !ward.IsAlive || ward.Clan != clan || record?.WardHeroId != ward.StringId
                || ward.Age < SuccessionLawHelper.GetAgeOfMajority() || _adulthoodEventsInProgress.Contains(ward)) return false;
            if (!record.IsAdulthoodPrepared(ward))
            {
                // A saved failure may represent partially initialized skills. Do not
                // replay the full native initializer and reroll a developed hero.
                if (!string.IsNullOrEmpty(record.AdulthoodFailure)) return false;
                var aging = Campaign.Current?.GetCampaignBehavior<AgingCampaignBehavior>();
                if (aging == null) return false;
                var pending = NativeYoungHeroes(aging);
                if (pending == null) return false;
                if (pending.ContainsKey(ward))
                {
                    BeginWardAdulthoodEvent(ward);
                    // Mirror native consumption before dispatch. Other birthday
                    // listeners still run once, including education and custody guards.
                    pending.Remove(ward);
                    Exception failure = null;
                    try { CampaignEventDispatcher.Instance.OnHeroComesOfAge(ward); }
                    catch (Exception ex) { failure = ex; }
                    finally { FinishWardAdulthoodEvent(ward, record, failure); }
                    if (failure != null) return false;
                }
                else record.CompleteAdulthood(ward);
            }
            return ward.IsAlive && ward.Clan == clan && record.WardHeroId == ward.StringId
                && RegencyAdulthoodEquipment.TryPrepareWard(ward);
        }

        private void ProcessAdultWardHandovers()
        {
            if (_adultWardHandovers.Count == 0) return;
            foreach (var ward in _adultWardHandovers.ToList())
            {
                _adultWardHandovers.Remove(ward);
                if (ward?.Clan == null || ward.Clan == Clan.PlayerClan
                    || !TryGetRegency(ward.Clan, out var record) || record.WardHeroId != ward.StringId) continue;
                EndRegency(ward.Clan, record, ward, showNotification: true);
            }
        }

        private static void RecoverLegacyWardEquipment()
        {
            foreach (var hero in Hero.AllAliveHeroes.ToList())
                RegencyAdulthoodEquipment.TryRecoverLegacy(hero);
        }
    }
}
