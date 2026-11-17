using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.WarPeace;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed class DefensivePactBehavior : CampaignBehaviorBase
    {
        private List<DefensivePactRecord> _pacts = new List<DefensivePactRecord>();
        private List<DefensivePactJoinDecision> _joinVotes = new List<DefensivePactJoinDecision>();
        private Dictionary<string, DefensivePactRecord> _activeByTarget = new Dictionary<string, DefensivePactRecord>(StringComparer.Ordinal);
        private Dictionary<string, DefensivePactRecord> _activeByMember = new Dictionary<string, DefensivePactRecord>(StringComparer.Ordinal);
        private Dictionary<string, double> _cooldowns = new Dictionary<string, double>(StringComparer.Ordinal);
        private bool _processingCalls;
        private bool _hasPendingCalls;
        private bool _hasReadyNotice;
        private bool _noticeOpen;
        private int _voteCursor;
        private const int MembershipChecksPerDay = 8;
        private readonly Queue<string> _membershipChecks = new Queue<string>();
        private readonly HashSet<string> _membershipQueued = new HashSet<string>(StringComparer.Ordinal);
        private const int FrontierChecksPerDay = 8;
        private const int FrontierDiscoveryIntervalDays = 30;
        private DefensivePactFrontierCache _frontiers;
        private Dictionary<string, Kingdom> _frontierRealms = new Dictionary<string, Kingdom>(StringComparer.Ordinal);
        private int _frontierNextDiscoveryDay;
        private int _frontierLastDay = -1;
        private bool _frontierDiscoveryDue = true;

        public override void RegisterEvents()
        {
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, ProcessPendingCalls);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, PruneCompletedPacts);
            CampaignEvents.KingdomDestroyedEvent.AddNonSerializedListener(this, OnKingdomDestroyed);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, ProcessPlayerNotice);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, MaintainFrontierCache);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, MaintainMemberships);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnFrontierOwnerChanged);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnFrontierClanChanged);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ => RecoverJoinVotes());
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, RecoverJoinVotes);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_DefensivePacts", ref _pacts);
            dataStore.SyncData("BellumCivile_DefensivePactJoinVotes", ref _joinVotes);
            if (_joinVotes == null) _joinVotes = new List<DefensivePactJoinDecision>();
            if (dataStore.IsLoading) RebuildIndexes();
        }

        private void RebuildIndexes()
        {
            if (_pacts == null) _pacts = new List<DefensivePactRecord>();
            _activeByTarget = new Dictionary<string, DefensivePactRecord>(StringComparer.Ordinal);
            _activeByMember = new Dictionary<string, DefensivePactRecord>(StringComparer.Ordinal);
            _cooldowns = new Dictionary<string, double>(StringComparer.Ordinal);
            _hasPendingCalls = false;
            _hasReadyNotice = false;
            _frontiers = null;
            _frontierRealms.Clear();
            _frontierDiscoveryDue = true;
            _frontierLastDay = -1;
            _membershipChecks.Clear();
            _membershipQueued.Clear();
            foreach (var pact in _pacts)
            {
                if (pact == null || string.IsNullOrWhiteSpace(pact.TargetId)) continue;
                if (pact.Activated)
                {
                    if (pact.IsNoticeReady) _hasReadyNotice = true;
                    if (!_cooldowns.TryGetValue(pact.TargetId, out double until) || pact.CooldownUntil > until)
                        _cooldowns[pact.TargetId] = pact.CooldownUntil;
                    if (!pact.AllCallsCompleted) _hasPendingCalls = true;
                    continue;
                }
                if (_activeByTarget.ContainsKey(pact.TargetId)) continue;
                _activeByTarget.Add(pact.TargetId, pact);
                foreach (var member in pact.Members)
                    if (member != null && !string.IsNullOrWhiteSpace(member.RealmId) && !_activeByMember.ContainsKey(member.RealmId))
                    {
                        _activeByMember.Add(member.RealmId, pact);
                        QueueMembershipCheck(member.RealmId);
                    }
            }
        }

        // Diplomatic and geographic facts must be freshly assessed by the caller before this operation.
        internal bool TryJoin(string memberId, string targetId, double now, double threat, int joiningThreshold,
            int fiefs, double strength, double targetStrength, int rulerRelation, bool atPeace,
            bool alliedToTarget, bool pactWithTarget, bool clientOfTarget)
        {
            if (!BellumCivileOptions.DefensivePactsActive || string.IsNullOrWhiteSpace(memberId) || string.IsNullOrWhiteSpace(targetId) || memberId == targetId
                || !DefensivePactThreatRules.Finite(now) || now < 0) return false;
            bool alreadyMember = _activeByMember.ContainsKey(memberId);
            bool cooldown = _cooldowns.TryGetValue(targetId, out double until) && now < until;
            if (!DefensivePactEligibilityRules.CanJoin(threat, joiningThreshold, fiefs, strength, targetStrength,
                rulerRelation, atPeace, alreadyMember, alliedToTarget, pactWithTarget, clientOfTarget, cooldown)) return false;
            bool created = !_activeByTarget.TryGetValue(targetId, out var pact);
            if (created) pact = new DefensivePactRecord(targetId);
            if (!pact.TryJoin(memberId, joiningThreshold)) return false;
            if (created) { _pacts.Add(pact); _activeByTarget.Add(targetId, pact); }
            _activeByMember.Add(memberId, pact);
            QueueMembershipCheck(memberId);
            return true;
        }

        private void QueueMembershipCheck(string memberId)
        {
            if (!string.IsNullOrWhiteSpace(memberId) && _membershipQueued.Add(memberId))
                _membershipChecks.Enqueue(memberId);
        }

        private void MaintainMemberships()
        {
            if (!BellumCivileOptions.DefensivePactsActive || Campaign.Current == null || _membershipChecks.Count == 0) return;
            var threats = Campaign.Current.GetCampaignBehavior<DefensivePactThreatBehavior>();
            var hostages = Campaign.Current.GetCampaignBehavior<HostagePactBehavior>();
            var clients = ClientKingdomBehavior.Instance;
            double now = CampaignTime.Now.ToDays;
            if (threats?.IsReady != true || hostages == null || clients == null
                || !DefensivePactThreatRules.Finite(now) || now < 0) return;
            int count = Math.Min(MembershipChecksPerDay, _membershipChecks.Count);
            for (int i = 0; i < count; i++)
            {
                string id = _membershipChecks.Dequeue();
                _membershipQueued.Remove(id);
                if (!_activeByMember.TryGetValue(id, out var pact)) continue;
                try
                {
                    CheckMembership(id, pact, threats, hostages, clients, now);
                }
                catch (Exception ex) { BellumCivileLogger.Log($"Defensive pact membership check deferred; member={id}; error={ex}"); }
                finally { if (_activeByMember.ContainsKey(id)) QueueMembershipCheck(id); }
            }
        }

        private void CheckMembership(string id, DefensivePactRecord pact, DefensivePactThreatBehavior threats,
            HostagePactBehavior hostages, ClientKingdomBehavior clients, double now, bool removeExistingWar = true)
        {
            if (!_frontierRealms.TryGetValue(id, out var member)
                || !_frontierRealms.TryGetValue(pact.TargetId, out var target)) return;
            if (member.IsEliminated) { RemoveRealm(id); return; }
            if (target.IsEliminated) { RemoveRealm(pact.TargetId); return; }
            if (removeExistingWar && member.IsAtWarWith(target)) { TryLeave(id); return; }
            var entry = pact.Members.FirstOrDefault(m => m?.RealmId == id);
            if (ShouldLeave(entry, member, target, threats, hostages, clients, now) == true) TryLeave(id);
        }

        private static bool? ShouldLeave(DefensivePactMemberRecord entry, Kingdom member, Kingdom target,
            DefensivePactThreatBehavior threats, HostagePactBehavior hostages, ClientKingdomBehavior clients, double now)
        {
            var memberLeader = member.RulingClan?.Leader;
            var targetLeader = target.RulingClan?.Leader;
            if (entry == null || memberLeader?.IsAlive != true || targetLeader?.IsAlive != true
                || CivilWarConflictBehavior.IsRealmTransferPending(member)
                || CivilWarConflictBehavior.IsRealmTransferPending(target)) return null;
            bool playerRealm = Campaign.Current != null && Clan.PlayerClan?.Kingdom == member
                && Clan.PlayerClan.IsUnderMercenaryService == false;
            return DefensivePactEligibilityRules.MustLeaveWithoutVote(threats.GetThreat(target.StringId, now), entry.JoiningThreshold,
                memberLeader.GetRelation(targetLeader), member.IsAllyWith(target), hostages.HasPactBetween(member, target),
                clients.IsClientOf(member, target), playerRealm);
        }

        private void RecheckBeforeAttack(DefensivePactRecord pact, string attackedMemberId, double now)
        {
            var threats = Campaign.Current?.GetCampaignBehavior<DefensivePactThreatBehavior>();
            var hostages = Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>();
            var clients = ClientKingdomBehavior.Instance;
            if (pact == null || threats?.IsReady != true || hostages == null || clients == null
                || !DefensivePactThreatRules.Finite(now) || now < 0) return;
            foreach (var entry in pact.Members.ToList())
            {
                string id = entry?.RealmId;
                if (id == null || !_activeByMember.TryGetValue(id, out var current) || current != pact) continue;
                try { CheckMembership(id, pact, threats, hostages, clients, now, id != attackedMemberId); }
                catch (Exception ex) { BellumCivileLogger.Log($"Defensive pact pre-attack check deferred; member={id}; error={ex}"); }
            }
        }

        internal bool TryLeave(string memberId)
        {
            if (string.IsNullOrWhiteSpace(memberId) || !_activeByMember.TryGetValue(memberId, out var pact)
                || !pact.TryLeave(memberId)) return false;
            _activeByMember.Remove(memberId);
            if (pact.Members.Count == 0) { _activeByTarget.Remove(pact.TargetId); _pacts.Remove(pact); }
            return true;
        }

        internal bool HasMembership(string realmId) => !string.IsNullOrWhiteSpace(realmId) && _activeByMember.ContainsKey(realmId);

        internal string GetMembershipTarget(string realmId) => !string.IsNullOrWhiteSpace(realmId)
            && _activeByMember.TryGetValue(realmId, out var pact) ? pact.TargetId : null;

        internal bool TryGetDepartureContext(Kingdom member, Kingdom target, out double threat, out int threshold)
        {
            threat = 0; threshold = 0;
            if (member == null || target == null || !IsMemberAgainst(member.StringId, target.StringId)
                || !_activeByMember.TryGetValue(member.StringId, out var pact)) return false;
            var entry = pact.Members.FirstOrDefault(item => item?.RealmId == member.StringId);
            var threats = Campaign.Current?.GetCampaignBehavior<DefensivePactThreatBehavior>();
            if (entry == null || threats?.IsReady != true) return false;
            double now = CampaignTime.Now.ToDays;
            if (!DefensivePactThreatRules.Finite(now) || now < 0) return false;
            threshold = entry.JoiningThreshold;
            threat = threats.GetThreat(target.StringId, now);
            return true;
        }

        internal bool IsMemberAgainst(string realmId, string targetId) => !string.IsNullOrWhiteSpace(realmId)
            && !string.IsNullOrWhiteSpace(targetId) && _activeByMember.TryGetValue(realmId, out var pact)
            && pact.TargetId == targetId;

        internal bool WasActivatedAt(string targetId, string attackedMemberId, double day)
            => _pacts.Any(p => p?.Activated == true && p.TargetId == targetId
                && p.AttackedMemberId == attackedMemberId && p.ActivatedDay == day);

        internal IReadOnlyList<DefensivePactMemberRecord> GetMembersAgainst(string targetId)
            => !string.IsNullOrWhiteSpace(targetId) && _activeByTarget.TryGetValue(targetId, out var pact)
                ? pact.Members : Array.Empty<DefensivePactMemberRecord>();

        internal float AdditionalDefensiveStrength(Kingdom attacker, Kingdom defender)
        {
            if (!BellumCivileOptions.DefensivePactsActive || Campaign.Current == null) return 0f;
            return EstimateAdditionalDefensiveStrength(attacker, defender,
                Campaign.Current.GetCampaignBehavior<DefensivePactThreatBehavior>(),
                Campaign.Current.GetCampaignBehavior<HostagePactBehavior>(), ClientKingdomBehavior.Instance, CampaignTime.Now.ToDays);
        }

        internal float EstimateAdditionalDefensiveStrength(Kingdom attacker, Kingdom defender,
            DefensivePactThreatBehavior threats, HostagePactBehavior hostages, ClientKingdomBehavior clients, double now)
        {
            if (!BellumCivileOptions.DefensivePactsActive || attacker == null || defender == null || attacker == defender
                || attacker.IsEliminated || defender.IsEliminated || attacker.IsAtWarWith(defender)
                || !IsMemberAgainst(defender.StringId, attacker.StringId)
                || !_activeByTarget.TryGetValue(attacker.StringId, out var pact)) return 0f;
            if (threats?.IsReady != true || hostages == null || clients == null
                || !DefensivePactThreatRules.Finite(now) || now < 0) return 0f;
            try
            {
                var defendingEntry = pact.Members.FirstOrDefault(m => m?.RealmId == defender.StringId);
                if (ShouldLeave(defendingEntry, defender, attacker, threats, hostages, clients, now) != false) return 0f;
                double total = 0;
                foreach (var entry in pact.Members)
                {
                    if (entry == null || entry.RealmId == defender.StringId
                        || !_frontierRealms.TryGetValue(entry.RealmId, out var member)
                        || !IsPermanentRealm(member) || attacker.IsAtWarWith(member)
                        || ShouldLeave(entry, member, attacker, threats, hostages, clients, now) != false
                        || !CanDefensiveMemberDeclare(member, attacker, hostages, clients)) continue;
                    float strength = member.CurrentTotalStrength;
                    if (!float.IsNaN(strength) && !float.IsInfinity(strength) && strength > 0) total += strength;
                }
                return (float)Math.Min(float.MaxValue, total);
            }
            catch (Exception ex) { BellumCivileLogger.Log("Defensive pact strength assessment deferred: " + ex); return 0f; }
        }

        private static bool CanDefensiveMemberDeclare(Kingdom member, Kingdom attacker,
            HostagePactBehavior hostages, ClientKingdomBehavior clients)
            => member != null && attacker != null && !member.IsAllyWith(attacker)
                && clients?.IsClientOf(member, attacker) != true && clients?.IsClientOf(attacker, member) != true
                && hostages?.GetActivePacts(member).Any(p => p.FirstRealm == attacker || p.SecondRealm == attacker) != true;

        internal bool IsCooldownActive(string targetId, double now) => !string.IsNullOrWhiteSpace(targetId)
            && DefensivePactThreatRules.Finite(now) && now >= 0
            && _cooldowns.TryGetValue(targetId, out double until) && now < until;

        internal DefensivePactCampaignAssessment Assess(Kingdom member, Kingdom target)
        {
            if (!BellumCivileOptions.DefensivePactsActive || Campaign.Current == null || member == null || target == null
                || !TryGetFrontierDistance(member.StringId, target.StringId, out int distance)) return null;
            try
            {
                return DefensivePactCampaignAssessment.Capture(member, target, distance, CampaignTime.Now.ToDays, this,
                    Campaign.Current.GetCampaignBehavior<DefensivePactThreatBehavior>(),
                    Campaign.Current.GetCampaignBehavior<HostagePactBehavior>(), ClientKingdomBehavior.Instance);
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Defensive pact assessment deferred; member={member.StringId}; target={target.StringId}; error={ex}");
                return null;
            }
        }

        // Reassess after a vote: relations, war, geography and competing memberships may have changed.
        internal bool TryJoinAfterVote(Kingdom member, Kingdom target)
        {
            var assessment = Assess(member, target);
            return assessment?.IsEligible == true && TryJoin(member.StringId, target.StringId, CampaignTime.Now.ToDays,
                assessment.Threat, assessment.JoiningThreshold, assessment.Fiefs, assessment.MemberStrength,
                assessment.TargetStrength, assessment.RulerRelation, assessment.AtPeace, assessment.Allied,
                assessment.HasPledge, assessment.IsTargetClient);
        }

        private bool HasPendingJoinVote(Kingdom member) => _joinVotes.Any(v => v != null && v.Member == member
            && (!v.Resolved || member.UnresolvedDecisions?.Contains(v) == true));

        internal DefensivePactJoinDecision GetPendingVote(Kingdom member, Kingdom target)
            => member?.UnresolvedDecisions?.OfType<DefensivePactJoinDecision>()
                .FirstOrDefault(vote => vote.Member == member && vote.Target == target && !vote.Resolved && !vote.JoinStarted
                    && vote.IsAllowed());

        internal bool CanProposeJoin(Clan proposer, Kingdom target, out TextObject reason)
        {
            reason = null;
            Kingdom member = proposer?.Kingdom;
            if (!BellumCivileOptions.DefensivePactsActive || member == null || target == null || member == target
                || proposer.IsEliminated || proposer.IsUnderMercenaryService || target.IsEliminated)
                reason = new TextObject("{=BC_DefensivePact_ProposeUnavailable}A defensive pact cannot be proposed under the current circumstances.");
            else if (HasMembership(member.StringId))
                reason = new TextObject("{=BC_DefensivePact_AlreadyMember}Your realm already belongs to a defensive pact.");
            else if (HasPendingJoinVote(member))
                reason = new TextObject("{=BC_DefensivePact_VotePending}Your realm is already considering a defensive pact.");
            else if (float.IsNaN(proposer.Influence) || float.IsInfinity(proposer.Influence)
                || proposer.Influence < DefensivePactVoteRules.InfluenceCost)
                reason = new TextObject("{=BC_DefensivePact_InfluenceBlocked}Your clan needs 100 influence to propose a defensive pact.");
            else if (IsCooldownActive(target.StringId, CampaignTime.Now.ToDays))
                reason = new TextObject("{=BC_DefensivePact_CooldownBlocked}A defensive pact against this realm cannot yet be formed again.");
            else
            {
                var assessment = Assess(member, target);
                if (assessment == null)
                    reason = new TextObject("{=BC_DefensivePact_AssessmentPending}The realms' current circumstances and frontiers are still being assessed.");
                else if (!assessment.AtPeace)
                    reason = new TextObject("{=BC_DefensivePact_PeaceRequired}Your realm must be at peace to join a defensive pact.");
                else if (assessment.Allied || assessment.HasPledge || assessment.IsTargetClient)
                    reason = new TextObject("{=BC_DefensivePact_ProtectedTie}Your realm has a protected agreement or client relationship with this realm.");
                else if (assessment.RulerRelation >= 0)
                    reason = new TextObject("{=BC_DefensivePact_RelationRequired}The rulers must have negative relations.");
                else if (assessment.Fiefs >= 25)
                    reason = new TextObject("{=BC_DefensivePact_SizeBlocked}Your realm must have fewer than 25 towns and castles.");
                else if (assessment.TargetStrength <= 0 || assessment.MemberStrength <= 0
                    || assessment.MemberStrength >= assessment.TargetStrength * .8d)
                    reason = new TextObject("{=BC_DefensivePact_StrengthBlocked}Your realm must have less than 80% of the target's strength.");
                else if (assessment.Threat < assessment.JoiningThreshold)
                    reason = new TextObject("{=BC_DefensivePact_ThreatBlocked}The target's threat is {THREAT}; it must reach {THRESHOLD} for a realm at this distance.")
                        .SetTextVariable("THREAT", assessment.Threat.ToString("0.##"))
                        .SetTextVariable("THRESHOLD", assessment.JoiningThreshold);
                else if (!assessment.IsEligible)
                    reason = new TextObject("{=BC_DefensivePact_ProposeUnavailable}A defensive pact cannot be proposed under the current circumstances.");
            }
            return reason == null;
        }

        internal bool TryFileJoinVote(Clan proposer, Kingdom target, Action<KingdomDecision> showDecision = null)
        {
            Kingdom member = proposer?.Kingdom;
            if (member == null || target == null || proposer.IsEliminated || proposer.IsUnderMercenaryService
                || float.IsNaN(proposer.Influence) || float.IsInfinity(proposer.Influence)
                || proposer.Influence < DefensivePactVoteRules.InfluenceCost
                || HasPendingJoinVote(member)) return false;
            var assessment = Assess(member, target);
            if (assessment?.IsEligible != true) return false;
            return FileJoinOrDepartureVote(proposer, target, assessment.Threat, assessment.JoiningThreshold, false, showDecision);
        }

        internal bool CanProposeDeparture(Clan proposer, Kingdom target, out TextObject reason)
        {
            reason = null;
            var member = proposer?.Kingdom;
            if (!BellumCivileOptions.DefensivePactsActive || member == null || target == null || member == target
                || proposer.IsEliminated || proposer.IsUnderMercenaryService || member.IsEliminated || target.IsEliminated
                || !IsMemberAgainst(member.StringId, target.StringId))
                reason = new TextObject("{=BC_DefensivePact_LeaveUnavailable}Your realm cannot propose leaving this defensive pact now.");
            else if (HasPendingJoinVote(member))
                reason = new TextObject("{=BC_DefensivePact_VotePending}Your realm is already considering a defensive pact.");
            else if (float.IsNaN(proposer.Influence) || float.IsInfinity(proposer.Influence)
                || proposer.Influence < DefensivePactVoteRules.InfluenceCost)
                reason = new TextObject("{=BC_DefensivePact_LeaveInfluenceBlocked}Your clan needs 100 influence to propose leaving the defensive pact.");
            else if (!TryGetDepartureContext(member, target, out _, out _))
                reason = new TextObject("{=BC_DefensivePact_AssessmentPending}The realms' current circumstances and frontiers are still being assessed.");
            return reason == null;
        }

        internal bool TryFileDepartureVote(Clan proposer, Kingdom target, Action<KingdomDecision> showDecision = null)
        {
            if (!CanProposeDeparture(proposer, target, out _) || !TryGetDepartureContext(proposer.Kingdom, target, out double threat, out int threshold))
                return false;
            return FileJoinOrDepartureVote(proposer, target, threat, threshold, true, showDecision);
        }

        private bool FileJoinOrDepartureVote(Clan proposer, Kingdom target, double threat, int threshold,
            bool departure, Action<KingdomDecision> showDecision)
        {
            Kingdom member = proposer?.Kingdom;
            if (member == null || target == null || HasPendingJoinVote(member)) return false;
            var decision = new DefensivePactJoinDecision(proposer, target, threat, threshold, departure)
            { FilingStarted = true, InfluenceBefore = proposer.Influence };
            _joinVotes.Add(decision);
            try { IdeologyBehavior.AddDecisionAsModAction(member, decision, false); }
            catch (Exception ex) { BellumCivileLogger.Log("Defensive pact vote filing interrupted: " + ex); }
            if (decision.Resolved || decision.JoinStarted || IsNativeVotePending(decision))
            {
                decision.Filed = true;
                if (showDecision != null && IsNativeVotePending(decision))
                    try { showDecision(decision); }
                    catch (Exception ex) { BellumCivileLogger.Log("Defensive pact vote display deferred: " + ex); }
                return true;
            }
            decision.Resolved = true;
            RefundUnfiledVote(decision);
            return false;
        }

        private static bool IsNativeVotePending(DefensivePactJoinDecision decision)
            => decision?.Member?.UnresolvedDecisions?.Contains(decision) == true;

        private static void RefundUnfiledVote(DefensivePactJoinDecision decision)
        {
            var clan = decision?.ProposerClan;
            if (clan == null || decision.Filed || decision.RefundStarted || !decision.FilingStarted
                || float.IsNaN(decision.InfluenceBefore) || float.IsInfinity(decision.InfluenceBefore)
                || float.IsNaN(clan.Influence) || float.IsInfinity(clan.Influence)
                || Math.Abs(clan.Influence - (decision.InfluenceBefore - DefensivePactVoteRules.InfluenceCost)) > .001f) return;
            decision.RefundStarted = true;
            try { ChangeClanInfluenceAction.Apply(clan, DefensivePactVoteRules.InfluenceCost); }
            catch (Exception ex) { BellumCivileLogger.Log("Defensive pact vote refund interrupted: " + ex); }
        }

        private void RecoverJoinVotes()
        {
            if (_joinVotes == null || _joinVotes.Count == 0) return;
            int count = Math.Min(4, _joinVotes.Count);
            for (int i = 0; i < count; i++)
            {
                if (_voteCursor >= _joinVotes.Count) _voteCursor = 0;
                var vote = _joinVotes[_voteCursor++];
                if (vote == null) continue;
                try
                {
                    if (vote.JoinStarted && !vote.Resolved) vote.RecoverJoin(this);
                    bool pending = IsNativeVotePending(vote);
                    if (vote.FilingStarted && !vote.Filed)
                    {
                        if (pending || vote.JoinStarted || vote.Resolved) vote.Filed = true;
                        else { vote.Resolved = true; RefundUnfiledVote(vote); }
                    }
                    else if (vote.Filed && !pending && !vote.JoinStarted && !vote.Resolved)
                        vote.Resolved = true;
                }
                catch (Exception ex) { BellumCivileLogger.Log("Defensive pact vote recovery deferred: " + ex); }
            }
            _joinVotes.RemoveAll(v => v == null || v.Resolved && !IsNativeVotePending(v));
        }

        internal bool Activate(string targetId, string attackedMemberId, double now)
        {
            if (!BellumCivileOptions.DefensivePactsActive || string.IsNullOrWhiteSpace(targetId) || !_activeByTarget.TryGetValue(targetId, out var pact)
                || !pact.TryActivate(attackedMemberId, now)) return false;
            _activeByTarget.Remove(targetId);
            foreach (var member in pact.Members)
                if (member != null && _activeByMember.TryGetValue(member.RealmId, out var current) && current == pact)
                    _activeByMember.Remove(member.RealmId);
            if (!_cooldowns.TryGetValue(targetId, out double until) || pact.CooldownUntil > until)
                _cooldowns[targetId] = pact.CooldownUntil;
            if (!pact.AllCallsCompleted) _hasPendingCalls = true;
            return true;
        }

        private void OnWarDeclared(IFaction attacker, IFaction defender, DeclareWarAction.DeclareWarDetail detail)
        {
            if (!BellumCivileOptions.DefensivePactsActive || !(attacker is Kingdom target) || !(defender is Kingdom member) || target == member
                || !IsInitiatingAttack(detail, _processingCalls,
                    ClientKingdomBehavior.Instance?.IsSynchronizingDiplomacy == true, target.IsAtWarWith(member))) return;
            _activeByTarget.TryGetValue(target.StringId, out var pact);
            double now = CampaignTime.Now.ToDays;
            RecheckBeforeAttack(pact, member.StringId, now);
            if (Activate(target.StringId, member.StringId, now))
            {
                var playerRealm = Campaign.Current != null ? Clan.PlayerClan?.Kingdom : null;
                if (playerRealm != null && pact.QueueNotice(playerRealm.StringId) && pact.IsNoticeReady) _hasReadyNotice = true;
                BellumCivileLogger.Log($"Defensive pact activated; target={target.StringId}; attacked={member.StringId}.");
            }
        }

        internal static bool IsInitiatingAttack(DeclareWarAction.DeclareWarDetail detail, bool processingCalls,
            bool clientSync, bool atWar) => !processingCalls && !clientSync && atWar
                && HostagePactBehavior.IsVoluntaryDeclaration(detail);

        private void OnKingdomDestroyed(Kingdom kingdom)
        {
            RemoveRealm(kingdom?.StringId);
            _frontiers = null;
            _frontierRealms.Clear();
            _frontierDiscoveryDue = true;
        }

        private void OnFrontierOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner,
            Hero capturer, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (settlement == null || (!settlement.IsTown && !settlement.IsCastle)) return;
            _frontiers?.Invalidate(oldOwner?.Clan?.Kingdom?.StringId);
            _frontiers?.Invalidate(settlement.OwnerClan?.Kingdom?.StringId);
        }

        private void OnFrontierClanChanged(Clan clan, Kingdom oldRealm, Kingdom newRealm,
            ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
        {
            if (clan?.Fiefs == null || clan.Fiefs.Count == 0) return;
            _frontiers?.Invalidate(oldRealm?.StringId);
            _frontiers?.Invalidate(newRealm?.StringId);
        }

        internal bool TryGetFrontierDistance(string firstId, string secondId, out int distance)
        {
            distance = 0;
            if (_frontiers == null) return false;
            if (_frontiers.TryGetDistance(firstId, secondId, out distance)) return true;
            _frontiers.Prioritize(firstId, secondId);
            return false;
        }

        private void MaintainFrontierCache()
        {
            if (!BellumCivileOptions.DefensivePactsActive || Campaign.Current == null) return;
            double day = CampaignTime.Now.ToDays;
            if (!DefensivePactThreatRules.Finite(day) || day < 0 || day >= int.MaxValue - FrontierDiscoveryIntervalDays) return;
            int today = (int)Math.Floor(day);
            if (_frontierLastDay == today) return;
            _frontierLastDay = today;
            try
            {
                if (_frontierDiscoveryDue || _frontiers == null || today >= _frontierNextDiscoveryDay)
                {
                    var realms = Kingdom.All.Where(IsPermanentRealm).Where(k => !string.IsNullOrWhiteSpace(k.StringId))
                        .GroupBy(k => k.StringId).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                    if (_frontiers == null || !_frontierRealms.Keys.OrderBy(id => id, StringComparer.Ordinal)
                        .SequenceEqual(realms.Keys.OrderBy(id => id, StringComparer.Ordinal)))
                        _frontiers = new DefensivePactFrontierCache(realms.Keys);
                    _frontierRealms = realms;
                    _frontierNextDiscoveryDay = today + FrontierDiscoveryIntervalDays;
                    _frontierDiscoveryDue = false;
                }
                if (_frontiers.PendingCount > 0)
                {
                    var scoring = new WarTargetScoringService();
                    _frontiers.Step(FrontierChecksPerDay, (first, second) =>
                        scoring.ProtectionReach(_frontierRealms[first], _frontierRealms[second]) != CourtProtectionReach.None);
                }
            }
            catch (Exception ex) { BellumCivileLogger.Log("Defensive pact frontier update deferred: " + ex); }
        }

        internal void RemoveRealm(string realmId)
        {
            if (string.IsNullOrWhiteSpace(realmId)) return;
            if (_activeByTarget.TryGetValue(realmId, out var pact))
            {
                foreach (var member in pact.Members)
                    if (member != null && _activeByMember.TryGetValue(member.RealmId, out var current) && current == pact)
                        _activeByMember.Remove(member.RealmId);
                _activeByTarget.Remove(realmId);
                _pacts.Remove(pact);
            }
            TryLeave(realmId);
        }

        private void ProcessPendingCalls()
        {
            if (!BellumCivileOptions.DefensivePactsActive || !_hasPendingCalls || _processingCalls) return;
            _processingCalls = true;
            try
            {
                var pending = _pacts.Where(p => p?.Activated == true && !p.AllCallsCompleted).ToList();
                var needed = new HashSet<string>(pending.Select(p => p.TargetId), StringComparer.Ordinal);
                foreach (var pact in pending)
                {
                    if (!string.IsNullOrWhiteSpace(pact.AttackedMemberId)) needed.Add(pact.AttackedMemberId);
                    foreach (var call in pact.Calls.Where(c => c != null && !c.Completed))
                        if (!string.IsNullOrWhiteSpace(call.RealmId)) needed.Add(call.RealmId);
                }
                var realms = Kingdom.All.Where(k => k != null && !string.IsNullOrWhiteSpace(k.StringId) && needed.Contains(k.StringId))
                    .GroupBy(k => k.StringId).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                Kingdom Realm(string id) => id != null && realms.TryGetValue(id, out var realm) ? realm : null;
                bool Available(string target, string member) => IsPermanentRealm(Realm(target)) && IsPermanentRealm(Realm(member));
                bool AtWar(string target, string member) => Realm(member)?.IsAtWarWith(Realm(target)) == true;
                bool CanDeclare(string target, string member)
                {
                    Kingdom aggressor = Realm(target), defender = Realm(member);
                    var hostages = Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>();
                    return CanDefensiveMemberDeclare(defender, aggressor, hostages, ClientKingdomBehavior.Instance);
                }
                void Declare(string target, string member) => DeclareWarAction.ApplyByDefault(Realm(member), Realm(target));
                foreach (var pact in pending)
                {
                    bool StillTriggered(string target, string member) => IsPermanentRealm(Realm(pact.AttackedMemberId))
                        && AtWar(target, pact.AttackedMemberId) && CanDeclare(target, member);
                    foreach (var call in pact.Calls.Where(c => c != null && !c.Completed))
                        DefensivePactCallExecution.Step(pact, call, Available, AtWar, StillTriggered, Declare);
                }
                _hasPendingCalls = pending.Any(p => !p.AllCallsCompleted);
                _hasReadyNotice = _pacts.Any(p => p?.IsNoticeReady == true);
            }
            catch (Exception ex) { BellumCivileLogger.Log("Defensive pact call queue interrupted: " + ex); }
            finally { _processingCalls = false; }
        }

        private static bool IsPermanentRealm(Kingdom realm) => realm != null && !realm.IsEliminated
            && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm);

        private void PruneCompletedPacts()
        {
            if (_cooldowns.Count == 0) return;
            double now = CampaignTime.Now.ToDays;
            _pacts.RemoveAll(p => p?.Activated == true && p.AllCallsCompleted && !p.HasPendingNotice && now >= p.CooldownUntil);
            foreach (string id in _cooldowns.Where(pair => now >= pair.Value
                    && !_pacts.Any(p => p?.TargetId == pair.Key && p.HasPendingNotice)).Select(pair => pair.Key).ToList())
                _cooldowns.Remove(id);
        }

        private void ProcessPlayerNotice(float dt)
        {
            if (!BellumCivileOptions.DefensivePactsActive || !_hasReadyNotice || _noticeOpen || InformationManager.IsAnyInquiryActive()
                || !(Game.Current?.GameStateManager?.ActiveState is MapState map) || map.AtMenu
                || map.MapConversationActive || map.NextIncident != null || PlayerEncounter.Current != null
                || Campaign.Current?.CurrentMenuContext != null) return;
            var pact = _pacts.FirstOrDefault(p => p?.IsNoticeReady == true);
            if (pact == null) { _hasReadyNotice = false; return; }
            var playerRealm = Clan.PlayerClan?.Kingdom;
            if (playerRealm == null || playerRealm.StringId != pact.NoticeRealmId)
            {
                pact.AcknowledgeNotice();
                _hasReadyNotice = _pacts.Any(p => p?.IsNoticeReady == true);
                return;
            }
            var target = Kingdom.All.FirstOrDefault(k => k?.StringId == pact.TargetId);
            var attacked = Kingdom.All.FirstOrDefault(k => k?.StringId == pact.AttackedMemberId);
            bool isVictim = pact.NoticeRealmId == pact.AttackedMemberId;
            var call = pact.Calls.FirstOrDefault(c => c?.RealmId == pact.NoticeRealmId);
            bool warConfirmed = isVictim || call?.Outcome == DefensivePactCallOutcome.WarConfirmed;
            bool stillAtWar = target != null && playerRealm.IsAtWarWith(target);
            var text = BuildNotice(target?.Name, attacked?.Name, isVictim, warConfirmed, stillAtWar);
            _noticeOpen = true;
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    new TextObject("{=BC_DefensivePact_CallTitle}A Call to Arms").ToString(), text.ToString(), true, false,
                    new TextObject("{=BC_DefensivePact_CallConfirm}I understand.").ToString(), "", () =>
                    {
                        pact.AcknowledgeNotice();
                        _noticeOpen = false;
                        _hasReadyNotice = _pacts.Any(p => p?.IsNoticeReady == true);
                    }, null), true);
            }
            catch (Exception ex)
            {
                _noticeOpen = false;
                BellumCivileLogger.Log("Defensive pact notice could not be shown: " + ex);
            }
        }

        internal static TextObject BuildNotice(TextObject target, TextObject attacked, bool isVictim, bool warConfirmed, bool stillAtWar)
        {
            string template = isVictim
                ? "{=BC_DefensivePact_CallAttacked}A messenger brings word that {TARGET} has attacked our realm. By the defensive pact, the other signatory realms have been called to arms. Each will fight its own war against the aggressor."
                : !warConfirmed
                    ? "{=BC_DefensivePact_CallFailed}A messenger brings word that {TARGET} has attacked {ATTACKED}. Our realm was called upon under the defensive pact, but could not enter the war under the present circumstances."
                    : stillAtWar
                        ? "{=BC_DefensivePact_CallJoined}A messenger brings word that {TARGET} has attacked {ATTACKED}. By the defensive pact, our realm has taken up arms against the aggressor. Each signatory fights its own war."
                        : "{=BC_DefensivePact_CallEnded}A messenger brings word that {TARGET} attacked {ATTACKED}. Our realm answered the defensive pact's call to arms, though our separate war has since ended.";
            return new TextObject(template)
                .SetTextVariable("TARGET", target ?? new TextObject("{=BC_DefensivePact_UnknownTarget}the aggressor"))
                .SetTextVariable("ATTACKED", attacked ?? new TextObject("{=BC_DefensivePact_UnknownMember}a fellow signatory"));
        }
    }
}
