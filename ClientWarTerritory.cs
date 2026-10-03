using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BellumCivile
{
    // A treaty's two principals pay the costs; participating clients remain distinct owners.
    internal sealed class ClientWarTerritory
    {
        private readonly Dictionary<string, int> _sides = new Dictionary<string, int>();
        internal readonly WarScoreRecord War;
        internal ClientWarTerritory(WarScoreRecord war)
        {
            War = war;
            if (war == null) return;
            _sides[war.AttackerKingdomId] = 1;
            _sides[war.DefenderKingdomId] = -1;
            if (war.ConflictType != WarScoreConflictType.ForeignWar || Campaign.Current == null) return;
            var clients = ClientKingdomBehavior.Instance;
            var attacker = Realm(war.AttackerKingdomId); var defender = Realm(war.DefenderKingdomId);
            if (clients == null || attacker == null || defender == null || clients.IsAuxiliaryClientWar(attacker, defender)) return;
            AddClients(clients, attacker, defender, 1);
            AddClients(clients, defender, attacker, -1);
            // Older saves contain the client estates only in auxiliary war snapshots. Import
            // their earliest recorded ownership once; never infer pre-war ownership from today's map.
            var wars = Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWars();
            if (wars == null) return;
            ImportSnapshots(wars);
        }
        internal void ImportSnapshots(IEnumerable<WarScoreRecord> wars)
        {
            var war = War;
            if (war?.ConflictType != WarScoreConflictType.ForeignWar || wars == null) return;
            foreach (var other in wars.Where(w => w != null && w != war && w.ConflictType == WarScoreConflictType.ForeignWar
                && Side(w.AttackerKingdomId) * Side(w.DefenderKingdomId) == -1).OrderBy(w => w.StartedDay))
                war.AddMissingFiefSnapshots((other.FiefSnapshots ?? new WarScoreFiefSnapshotRecord[0]).Where(s => s != null && Side(s.OwnerKingdomId) != 0));
        }
        private void AddClients(ClientKingdomBehavior clients, Kingdom principal, Kingdom enemy, int side)
        {
            foreach (var client in clients.GetClients(principal))
                if (!client.IsEliminated && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(client)
                    && client.IsAtWarWith(enemy) && !client.IsAtWarWith(principal) && !_sides.ContainsKey(client.StringId))
                    _sides.Add(client.StringId, side);
        }
        internal int Side(string realmId) => realmId != null && _sides.TryGetValue(realmId, out int side) ? side : 0;
        internal bool OnSide(Kingdom realm, Kingdom principal) => realm != null && principal != null
            && Side(principal.StringId) != 0 && Side(realm.StringId) == Side(principal.StringId);
        internal bool Opposing(string original, string current) => Side(original) * Side(current) == -1;
        internal static Kingdom Realm(string id) => Kingdom.All.FirstOrDefault(k => k.StringId == id);
        internal static bool IsTerritorial(TreatyTermType type) => type == TreatyTermType.TransferFief || type == TreatyTermType.RecognizeClientOccupation;
        internal static TextObject RecognitionText(Settlement fief, Kingdom client) => new TextObject(
            "{=BC_Treaty_ClientOccupation}Recognize {CLIENT}'s possession of {FIEF}")
            .SetTextVariable("CLIENT", client?.Name ?? TextObject.GetEmpty()).SetTextVariable("FIEF", fief?.Name ?? TextObject.GetEmpty());

        internal static IReadOnlyList<ClientOccupationCandidate> Candidates(WarScoreRecord war, Kingdom from, Kingdom to)
        {
            var result = new List<ClientOccupationCandidate>();
            if (war?.ConflictType != WarScoreConflictType.ForeignWar || from == null || to == null || !war.IsActive) return result;
            var context = new ClientWarTerritory(war);
            if (context.Side(from.StringId) * context.Side(to.StringId) != -1) return result;
            foreach (var snapshot in war.FiefSnapshots)
            {
                if (snapshot == null || context.Side(snapshot.OwnerKingdomId) != context.Side(from.StringId)) continue;
                var fief = Settlement.All.FirstOrDefault(s => s.StringId == snapshot.SettlementId);
                var owner = fief?.OwnerClan; var client = owner?.Kingdom;
                if (fief?.Town == null || owner == null || owner.IsEliminated || client == to || !context.OnSide(client, to)
                    || ClientKingdomBehavior.Instance?.GetSuzerain(client) != to) continue;
                result.Add(new ClientOccupationCandidate(fief, client, owner,
                    TreatyTermCostModel.GetFiefTransferCost(fief, client, true)));
            }
            return result;
        }
    }
    internal sealed class ClientOccupationCandidate
    {
        internal Settlement Fief { get; }
        internal Kingdom Client { get; }
        internal Clan Owner { get; }
        internal int Cost { get; }
        internal ClientOccupationCandidate(Settlement fief, Kingdom client, Clan owner, int cost)
        { Fief = fief; Client = client; Owner = owner; Cost = cost; }
        internal TreatyTermRecord Term(Kingdom from, Kingdom to, bool offering = false) => new TreatyTermRecord(
            TreatyTermType.RecognizeClientOccupation, Cost, Fief.StringId, fromKingdomId: from.StringId,
            toKingdomId: to.StringId, wasOccupiedAtDrafting: true, clanId: Owner.StringId,
            thirdKingdomId: Client.StringId, wasVoluntaryOffering: offering);
        internal bool Matches(TreatyTermRecord term) => term.SettlementId == Fief.StringId
            && term.ThirdKingdomId == Client.StringId && term.ClanId == Owner.StringId;
    }
}
