using System;
using System.Collections.Generic;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile
{
    internal static class DefensivePactCampaignTreatyFacts
    {
        internal static DefensivePactTreatySnapshot Capture(WarScoreRecord war, TreatyProposalRecord proposal,
            Kingdom winner, Kingdom loser)
        {
            if (war == null || proposal == null || winner == null || loser == null || winner == loser
                || war.ConflictType != WarScoreConflictType.ForeignWar
                || proposal.WinnerKingdomId != winner.StringId || proposal.LoserKingdomId != loser.StringId
                || !((war.AttackerKingdomId == winner.StringId && war.DefenderKingdomId == loser.StringId)
                    || (war.AttackerKingdomId == loser.StringId && war.DefenderKingdomId == winner.StringId))) return null;
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            var sides = CaptureSides(winner, loser, ClientKingdomBehavior.Instance);
            var legalByRealm = new Dictionary<string, DefensivePactLegalBasisSnapshot>(StringComparer.Ordinal);
            bool? SameSide(string first, string second)
            {
                return first != null && second != null && sides.TryGetValue(first, out int a) && sides.TryGetValue(second, out int b)
                    ? a == b : (bool?)null;
            }
            DefensivePactLegalBasis Basis(TreatyTermRecord term)
            {
                Kingdom realm = term.ToKingdomId == winner.StringId ? winner
                    : term.ToKingdomId == loser.StringId ? loser : null;
                if (realm == null || titles == null || string.IsNullOrWhiteSpace(term.SettlementId))
                    return DefensivePactLegalBasis.Unknown;
                if (!legalByRealm.TryGetValue(realm.StringId, out var legal))
                    legalByRealm[realm.StringId] = legal = new DefensivePactLegalBasisSnapshot(titles, realm);
                return legal.For(Settlement.Find(term.SettlementId));
            }
            return DefensivePactTreatySnapshot.Capture(proposal, war.FiefSnapshots, SameSide, Basis);
        }

        internal static IReadOnlyDictionary<string, int> CaptureSides(Kingdom first, Kingdom second,
            ClientKingdomBehavior clients)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            if (first == null || second == null || first == second || string.IsNullOrWhiteSpace(first.StringId)
                || string.IsNullOrWhiteSpace(second.StringId) || first.StringId == second.StringId) return result;
            result[first.StringId] = 1;
            result[second.StringId] = -1;
            if (clients == null || clients.IsAuxiliaryClientWar(first, second)) return result;
            AddClients(first, second, 1);
            AddClients(second, first, -1);
            return result;

            void AddClients(Kingdom principal, Kingdom enemy, int side)
            {
                foreach (var client in clients.GetClients(principal))
                    if (client != null && !client.IsEliminated && !string.IsNullOrWhiteSpace(client.StringId)
                        && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(client)
                        && client.IsAtWarWith(enemy) && !client.IsAtWarWith(principal) && !result.ContainsKey(client.StringId))
                        result.Add(client.StringId, side);
            }
        }
    }
}
