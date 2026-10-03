using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    internal static partial class BellumMarriageStrategyHelper
    {
        private sealed partial class EvaluationContext
        {
            private readonly Dictionary<Clan, float> _clanStrengths = new Dictionary<Clan, float>();
            private readonly Dictionary<Kingdom, float> _realmStrengths = new Dictionary<Kingdom, float>();
            private readonly Dictionary<Kingdom, RealmPolitics> _realmPolitics = new Dictionary<Kingdom, RealmPolitics>();

            private sealed class RealmPolitics
            {
                public float Strength, StrongestEnemy;
                public readonly HashSet<Kingdom> Enemies = new HashSet<Kingdom>();
            }

            private float ClanStrength(Clan clan)
            {
                if (!_clanStrengths.TryGetValue(clan, out float value))
                    _clanStrengths[clan] = value = Campaign.Current.Models.DiplomacyModel.GetClanStrength(clan);
                return value;
            }

            private float RealmStrength(Kingdom realm)
            {
                if (!_realmStrengths.TryGetValue(realm, out float value))
                    _realmStrengths[realm] = value = realm.CurrentTotalStrength;
                return value;
            }

            private RealmPolitics RealmProfile(Kingdom realm)
            {
                if (_realmPolitics.TryGetValue(realm, out var profile)) return profile;
                profile = new RealmPolitics { Strength = RealmStrength(realm) };
                foreach (var faction in realm.FactionsAtWarWith)
                {
                    var enemy = faction as Kingdom;
                    if (enemy == null || enemy.IsEliminated || enemy == realm
                        || MarriagePoliticalRealm(enemy.RulingClan) != enemy) continue;
                    profile.Enemies.Add(enemy);
                    profile.StrongestEnemy = System.Math.Max(profile.StrongestEnemy, RealmStrength(enemy));
                }
                _realmPolitics[realm] = profile;
                return profile;
            }

            private float CalculatePoliticalValue(Clan house, Clan other)
            {
                var realm = Realms[house];
                var target = Realms[other];
                if (realm == null || target == null || house == other || house.IsAtWarWith(other)
                    || MarriageAllianceHelper.HasMarriageAlliance(house, other)) return 0;
                if (realm == target)
                {
                    // Rulers retain pacification incentives; ordinary houses value useful supporters.
                    if (realm.RulingClan == house)
                        return CalculateRoyalPoliticalValue(house, other, realm, Factions, new List<string>());
                    return MarriagePoliticalRules.DomesticPartner(ClanStrength(house), ClanStrength(other),
                        other.Influence, realm.RulingClan == other);
                }
                if (realm.IsAtWarWith(target)) return 0;
                if (realm.RulingClan != house || target.RulingClan != other)
                    return MarriagePoliticalRules.ForeignHousePartner(ClanStrength(house), ClanStrength(other),
                        other.Influence, target.RulingClan == other);
                var own = RealmProfile(realm);
                var partner = RealmProfile(target);
                bool allied = AreKingdomsAllied(realm, target);
                int maximum = Campaign.Current.Models.AllianceModel.MaxNumberOfAlliances;
                bool room = realm.AlliedKingdoms.Count < maximum && target.AlliedKingdoms.Count < maximum;
                float relation = house.Leader != null && other.Leader != null
                    ? house.Leader.GetRelation(other.Leader) : 0;
                return MarriagePoliticalRules.ForeignPartner(own.Strength, partner.Strength, own.StrongestEnemy,
                    own.Enemies.Overlaps(partner.Enemies), allied, room, realm.AlliedKingdoms.Count == 0,
                    house.Culture == other.Culture, relation);
            }
        }
    }
}
