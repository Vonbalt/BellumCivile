using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    public sealed partial class WarScoreBehavior
    {
        internal void RefreshHostageCustodyScore() => ReconcileReversibleScores();
        private void OnScorePrisonerReleased(Hero prisoner, PartyBase party, IFaction capturerFaction, EndCaptivityDetail detail, bool showNotification)
            => RemoveCustodyScore(prisoner);

        private void RemoveCustodyScore(Hero prisoner)
        {
            if (prisoner == null || !WarPeaceRevampBehavior.IsRevampEnabled()) return;
            foreach (var war in GetActiveWars())
            {
                float previous = war.Score, previousCustody = war.PrisonerScore;
                war.RemovePrisoner(prisoner.StringId, C.WarScorePrisonerCap);
                if (Math.Abs(previousCustody - war.PrisonerScore) < .0001f) continue;
                AddEvent(war, WarScoreEventType.PrisonerCustodyChanged, war.Score - previous, null, null,
                    heroId: prisoner.StringId, debugText: "prisoner released or died");
                QueueTerminalWarScoreResolution(war, "prisoner custody changed");
            }
        }

        private void ReconcileReversibleScores()
        {
            if (!WarPeaceRevampBehavior.IsRevampEnabled()) return;
            // One prisoner scan per reconciliation, not one full hero scan per war.
            var prisoners = Hero.AllAliveHeroes.Where(h => h.IsPrisoner && !HostageCustodyGuard.IsProtected(h))
                .Select(h => new { Hero = h, Captor = RetainedTreatyPrisonerBehavior.GetCaptorKingdom(h), Victim = h.Clan?.Kingdom })
                .Where(p => p.Captor != null && p.Victim != null && p.Captor != p.Victim).ToList();
            foreach (var war in GetActiveWars())
            {
                if (CivilWarConflictBehavior.IsScoreTransferPending(war)) continue;
                var current = new Dictionary<string, float>();
                foreach (var p in prisoners)
                {
                    bool capturedByAttacker = p.Captor.StringId == war.AttackerKingdomId && p.Victim.StringId == war.DefenderKingdomId;
                    bool capturedByDefender = p.Captor.StringId == war.DefenderKingdomId && p.Victim.StringId == war.AttackerKingdomId;
                    if (!capturedByAttacker && !capturedByDefender) continue;
                    float value = IsWarLeader(p.Victim, p.Hero) ? C.WarScoreRulerCaptured
                        : IsPrimaryHeirOfWarLeader(p.Victim, p.Hero) ? C.WarScoreHeirCaptured : C.WarScoreNobleCaptured;
                    current[p.Hero.StringId] = capturedByAttacker ? value : -value;
                }
                float previous = war.Score, previousCustody = war.PrisonerScore;
                war.ReconcilePrisoners(current, C.WarScorePrisonerCap);
                if (Math.Abs(previousCustody - war.PrisonerScore) < .0001f) continue;
                AddEvent(war, WarScoreEventType.PrisonerCustodyChanged, war.Score - previous, null, null,
                    debugText: "current opposing custody reconciled");
                QueueTerminalWarScoreResolution(war, "prisoner custody reconciled");
            }
            Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>()?.ReconcileObjectiveControlScores();
        }
    }
}
