using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using BellumCivile.WarPeace;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;

namespace BellumCivile
{
    internal sealed class CourtProtectionAssessment
    {
        internal Kingdom Protector;
        internal string BlockReason;
        internal CourtProtectionScore Score;
        internal double AlliedStrength, EnemyStrength, OtherEnemyStrength, ProtectorStrength, MotionWeight;
        internal List<Kingdom> OtherEnemies = new List<Kingdom>();
        internal int FiefValue;
        internal bool Eligible => BlockReason == null && Score != null;
    }

    // One disposable inspection/selection pass; no cross-day or cross-campaign caches.
    internal sealed class CourtProtectionAssessmentService
    {
        private readonly WarTargetScoringService _geography = new WarTargetScoringService();
        private readonly Dictionary<Tuple<Kingdom, Kingdom>, CourtProtectionReach> _reach = new Dictionary<Tuple<Kingdom, Kingdom>, CourtProtectionReach>();
        private CourtProtectionReach Reach(Kingdom a, Kingdom b)
        {
            var key = Tuple.Create(a, b);
            if (!_reach.TryGetValue(key, out var result)) _reach[key] = result = _geography.ProtectionReach(a, b);
            return result;
        }
        private static bool Permanent(Kingdom realm) => CourtAgendaBehavior.ValidRealm(realm)
            && !ModIntegrationHelper.IsDiplomacyRebelKingdom(realm);
        private static HashSet<Kingdom> Bloc(Kingdom realm)
        {
            var clients = ClientKingdomBehavior.Instance;
            var leader = clients?.GetSuzerain(realm) ?? realm;
            var result = new HashSet<Kingdom> { leader };
            if (clients != null) result.UnionWith(clients.GetClients(leader));
            return result;
        }
        private static double Strength(IEnumerable<Kingdom> realms) => realms.Distinct().Sum(k => (double)k.CurrentTotalStrength);
        private static bool InternalConflict(Kingdom realm)
        {
            var wars = Campaign.Current.GetCampaignBehavior<WarScoreBehavior>();
            return WarPeaceRevampBehavior.HasInternalConflict(realm) || realm.FactionsAtWarWith.OfType<Kingdom>().Any(enemy => !Permanent(enemy)
                || (wars?.GetActiveWar(realm, enemy) is WarScoreRecord war && war.ConflictType != WarScoreConflictType.ForeignWar));
        }

        internal CourtProtectionAssessment Assess(Kingdom client, Kingdom threat, Kingdom protector)
        {
            var result = new CourtProtectionAssessment { Protector = protector };
            CourtProtectionAssessment Block(string why) { result.BlockReason = why; return result; }
            try
            {
                if (!Permanent(client) || !Permanent(threat) || !Permanent(protector)
                    || client == threat || client == protector || protector == threat) return Block("Three distinct permanent, ruled realms are required.");
                var clients = ClientKingdomBehavior.Instance;
                if (clients == null) return Block("Client kingdom behavior is unavailable.");
                if (!clients.CanEstablishClientKingdom(client, protector, out string reason)) return Block(reason);
                if (!client.IsAtWarWith(threat)) return Block("The named threat is not at war with the applicant.");
                if (client.IsAtWarWith(protector)) return Block("Applicant and protector are at war.");
                if (!TreatyDraftService.TryGetClientKingdomCandidate(protector, client, out var territory, out reason)) return Block(reason);
                result.FiefValue = territory.WarScoreCost;
                if (!CourtProtectionRules.Desperate(client.CurrentTotalStrength, threat.CurrentTotalStrength, territory.FiefCount, territory.WarScoreCost))
                    return Block("Requires an enemy at least twice as strong and a landed realm valued below 150.");
                var friendly = Bloc(protector);
                var hostile = Bloc(threat);
                if (friendly.Overlaps(hostile) || hostile.Contains(client)) return Block("The proposed sides have conflicting client obligations.");
                if (friendly.Any(k => k.IsAtWarWith(client)) || friendly.Any(k => friendly.Any(k.IsAtWarWith))
                    || hostile.Any(k => hostile.Any(k.IsAtWarWith))) return Block("A client bloc is at war with itself or the applicant.");
                if (friendly.Concat(hostile).Append(client).Any(InternalConflict)) return Block("A participating realm has an internal conflict or temporary-realm war.");
                result.ProtectorStrength = Strength(friendly);
                if (protector.CurrentTotalStrength <= client.CurrentTotalStrength) return Block("The prospective protector must be stronger than the applicant.");
                if (Reach(client, protector) == CourtProtectionReach.None) return Block("No nearby land or supported maritime connection to the applicant.");
                var otherEnemies = new HashSet<Kingdom>(friendly.SelectMany(k => k.FactionsAtWarWith.OfType<Kingdom>()).Where(Permanent).SelectMany(Bloc));
                otherEnemies.ExceptWith(hostile);
                otherEnemies.ExceptWith(friendly);
                otherEnemies.Remove(client);
                result.OtherEnemyStrength = Strength(otherEnemies);
                result.OtherEnemies = otherEnemies.OrderBy(k => k.StringId, StringComparer.Ordinal).ToList();
                friendly.Add(client);
                result.AlliedStrength = Strength(friendly);
                result.EnemyStrength = Strength(hostile);
                if (!CourtProtectionRules.Finite(result.AlliedStrength) || !CourtProtectionRules.Finite(result.EnemyStrength)
                    || !CourtProtectionRules.Finite(result.ProtectorStrength) || result.EnemyStrength <= 0 || result.ProtectorStrength <= 0)
                    return Block("Military strength is unavailable or invalid.");
                foreach (var ally in friendly)
                    foreach (var enemy in hostile)
                        if (!CanEnterWar(ally, enemy, out reason)) return Block(reason);
                // Alignment would join remaining protector wars too, and end client-only wars.
                foreach (var enemy in otherEnemies)
                    if (!CanEnterWar(client, enemy, out reason)) return Block(reason);
                var permission = Campaign.Current.Models.KingdomDecisionPermissionModel;
                foreach (var enemy in client.FactionsAtWarWith.OfType<Kingdom>().Where(k => !hostile.Contains(k) && !otherEnemies.Contains(k)))
                    if (!permission.IsPeaceDecisionAllowedBetweenKingdoms(client, enemy, out _))
                        return Block("An unrelated applicant war cannot lawfully be ended during alignment.");

                double ratio = result.AlliedStrength / result.EnemyStrength;
                Hero ruler = protector.RulingClan.Leader;
                double personality = CourtProtectionRules.ProtectorPersonality(ruler.GetTraitLevel(DefaultTraits.Valor),
                    ruler.GetTraitLevel(DefaultTraits.Mercy), ruler.GetTraitLevel(DefaultTraits.Honor), ruler.GetTraitLevel(DefaultTraits.Calculating), ratio);
                result.Score = CourtProtectionRules.Score(ratio, result.OtherEnemyStrength / result.ProtectorStrength,
                    CourtProtectionRules.StrategicValue(Reach(client, protector), territory.WarScoreCost,
                        Reach(protector, threat) != CourtProtectionReach.None), ruler.GetRelation(client.RulingClan.Leader), personality, protector.IsAtWarWith(threat));
                if (result.Score == null) return Block("Invalid score inputs.");
                var war = Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(client, threat);
                double ownScore = war == null ? 0 : war.Score * (war.AttackerKingdomId == client.StringId ? 1 : -1);
                var crown = client.RulingClan.Leader;
                double desperationRatio = client.CurrentTotalStrength > 0 ? (double)threat.CurrentTotalStrength / client.CurrentTotalStrength : 5;
                result.MotionWeight = CourtProtectionRules.MotionWeight(desperationRatio, ownScore,
                    Campaign.Current.GetCampaignBehavior<WarPeaceRevampBehavior>()?.PeekWarWill(client.RulingClan) ?? BellumCivileOptions.WarWillInitial,
                    crown.GetTraitLevel(DefaultTraits.Calculating), crown.GetTraitLevel(DefaultTraits.Valor));
                return result;
            }
            catch (Exception ex) { return Block("Assessment unavailable: " + ex.GetType().Name + ": " + ex.Message); }
        }

        internal static bool CanAlignClient(Kingdom client, Kingdom protector)
        {
            if (InternalConflict(client) || InternalConflict(protector)) return false;
            foreach (var other in Kingdom.All.Where(k => Permanent(k) && k != client && k != protector))
            {
                if (protector.IsAtWarWith(other) && !CanEnterWar(client, other, out _)) return false;
                if (!protector.IsAtWarWith(other) && client.IsAtWarWith(other)
                    && !Campaign.Current.Models.KingdomDecisionPermissionModel.IsPeaceDecisionAllowedBetweenKingdoms(client, other, out _)) return false;
            }
            return true;
        }

        private static bool CanEnterWar(Kingdom first, Kingdom second, out string reason)
        {
            reason = null;
            if (first.IsAtWarWith(second)) return true;
            if (first == second || first.AlliedKingdoms.Contains(second)) { reason = "Intervention conflicts with an alliance."; return false; }
            if (!ModIntegrationHelper.TryReadDiplomacyNonAggressionPact(first, second, out bool pact) || pact)
            { reason = "A non-aggression pact blocks intervention, or its compatibility query is unavailable."; return false; }
            if (!ModIntegrationHelper.TryReadDiplomacyWarCooldown(first, second, out bool cooldown) || cooldown)
            { reason = "Diplomacy's war cooldown blocks intervention, or its compatibility query is unavailable."; return false; }
            if (BellumCivileOptions.MinimumPeaceBeforeRenewedWarDays > 0 && first.GetStanceWith(second).PeaceDeclarationDate.ElapsedDaysUntilNow
                <= BellumCivileOptions.MinimumPeaceBeforeRenewedWarDays)
            { reason = "Intervention would violate the configured peace interval."; return false; }
            if (!Campaign.Current.Models.KingdomDecisionPermissionModel.IsWarDecisionAllowedBetweenKingdoms(first, second, out _))
            { reason = "The active kingdom decision model forbids intervention."; return false; }
            return true;
        }

        internal static string DebugReport(Kingdom client, Kingdom threat)
        {
            var service = new CourtProtectionAssessmentService();
            var rows = Kingdom.All.Where(k => k != client && k != threat && Permanent(k)).Select(k => service.Assess(client, threat, k))
                .OrderByDescending(r => r.Eligible).ThenByDescending(r => r.Score?.Total ?? double.NegativeInfinity)
                .ThenBy(r => r.Protector.StringId, StringComparer.Ordinal).ToList();
            var lines = new List<string> { $"Protection assessment: {client.Name} against {threat.Name}. READ ONLY; this command sends no offers.",
                "NPC threshold 60; utilities are not percentages. Player recipients decide freely. Ranking: eligibility, score, stable realm ID." };
            foreach (var r in rows)
            {
                if (!r.Eligible) { lines.Add($"{r.Protector.Name} [{r.Protector.StringId}]: blocked - {r.BlockReason}"); continue; }
                var s = r.Score;
                lines.Add(FormattableString.Invariant($"{r.Protector.Name} [{r.Protector.StringId}]: {s.Total:0.00}, NPC {(s.WouldAccept ? "accept" : "decline")}; base=50 military={s.Military:+0.00;-0.00;0} other_wars={s.OtherWars:+0.00;-0.00;0} value={s.StrategicValue:0.00} relations={s.Relations:+0.00;-0.00;0} personality={s.Personality:+0.00;-0.00;0} already_fighting={s.ExistingWar:0}; forces={r.AlliedStrength:0}/{r.EnemyStrength:0}, other_enemies={r.OtherEnemyStrength:0}; fief_value={r.FiefValue}; Crown_weight={r.MotionWeight:0.000}."));
            }
            lines.Add("Forecast only: no clientage, war, offer, cost, agenda receipt or vote was created. Final execution must revalidate and check mod-specific restrictions.");
            return string.Join("\n", lines);
        }
    }
}
