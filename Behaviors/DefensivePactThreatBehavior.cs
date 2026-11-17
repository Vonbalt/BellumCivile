using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public sealed class DefensivePactThreatBehavior : CampaignBehaviorBase
    {
        private List<DefensivePactThreatLedger> _records = new List<DefensivePactThreatLedger>();
        private List<string> _treatyReceipts = new List<string>();
        private List<DefensivePactTreatyJournal> _treatyJournals = new List<DefensivePactTreatyJournal>();
        private Dictionary<string, DefensivePactThreatLedger> _byRealm = new Dictionary<string, DefensivePactThreatLedger>(StringComparer.Ordinal);
        private HashSet<string> _receivedTreaties = new HashSet<string>(StringComparer.Ordinal);
        private Dictionary<string, DefensivePactTreatyJournal> _journalsByProposal = new Dictionary<string, DefensivePactTreatyJournal>(StringComparer.Ordinal);
        private double _baseWeeklyDecay = 1d;
        private bool _ratesInitialized;
        internal bool IsReady => _ratesInitialized;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ => InitializeCampaignRates());
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
        }

        private void InitializeCampaignRates()
        {
            if (!BellumCivileOptions.DefensivePactsActive) { _ratesInitialized = false; return; }
            if (!RecoverAppliedTreaties()) return;
            _ratesInitialized = true;
            RefreshCampaignRateSettings(BellumCivileOptions.DefensivePactWeeklyThreatDecay, true);
        }

        internal void RefreshFeatureSettings()
        {
            if (!BellumCivileOptions.DefensivePactsActive) { _ratesInitialized = false; return; }
            if (!_ratesInitialized) InitializeCampaignRates();
            else RefreshCampaignRateSettings(BellumCivileOptions.DefensivePactWeeklyThreatDecay);
        }

        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner,
            Hero capturer, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (!_ratesInitialized || settlement == null || (!settlement.IsTown && !settlement.IsCastle)) return;
            RefreshCampaignRealmRates(oldOwner?.Clan?.Kingdom, settlement.OwnerClan?.Kingdom);
        }

        private void OnClanChangedKingdom(Clan clan, Kingdom oldRealm, Kingdom newRealm,
            ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
        {
            if (!_ratesInitialized || clan == null) return;
            RefreshCampaignRealmRates(oldRealm, newRealm);
        }

        private void OnWarDeclared(IFaction attackerFaction, IFaction defenderFaction, DeclareWarAction.DeclareWarDetail detail)
        {
            if (!_ratesInitialized || !BellumCivileOptions.DefensivePactsActive
                || !(attackerFaction is Kingdom attacker) || !(defenderFaction is Kingdom defender)
                || attacker == defender || attacker.IsEliminated || defender.IsEliminated
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(attacker)
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(defender)
                || !DefensivePactBehavior.IsInitiatingAttack(detail, false,
                    ClientKingdomBehavior.Instance?.IsSynchronizingDiplomacy == true, attacker.IsAtWarWith(defender))) return;
            try
            {
                var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
                var justification = DefensivePactWarJustification.Capture(attacker, defender, titles);
                double warStart = attacker.GetStanceWith(defender).WarStartDate.ToDays;
                RecordUnjustifiedWar(attacker, defender, CampaignTime.Now.ToDays, warStart, justification);
            }
            catch (Exception ex) { BellumCivileLogger.Log("Defensive pact war threat assessment failed: " + ex); }
        }

        internal bool RecordUnjustifiedWar(Kingdom attacker, Kingdom defender, double day, double warStart,
            DefensivePactLegalBasis justification)
        {
            if (!_ratesInitialized || !BellumCivileOptions.DefensivePactsActive || attacker == null || defender == null || attacker == defender
                || string.IsNullOrWhiteSpace(attacker.StringId) || string.IsNullOrWhiteSpace(defender.StringId)
                || justification != DefensivePactLegalBasis.NotEstablished || !DefensivePactThreatRules.Finite(day)
                || !DefensivePactThreatRules.Finite(warStart) || warStart < 0 || day < warStart) return false;
            string key = "war:" + attacker.StringId.Length + ":" + attacker.StringId
                + defender.StringId.Length + ":" + defender.StringId + ":" + warStart.ToString("R", CultureInfo.InvariantCulture);
            return ApplyThreatEventOnce(key, attacker, day, 10d);
        }

        internal bool RecordPactBreach(HostagePactRecord pact, Kingdom aggressor)
        {
            if (!_ratesInitialized || !BellumCivileOptions.DefensivePactsActive || pact == null || aggressor == null || aggressor.IsEliminated
                || string.IsNullOrWhiteSpace(pact.Id) || pact.VoluntaryAggressor != aggressor
                || pact.Phase != HostagePactPhase.Resolving || (pact.EndReason != HostagePactEndReason.War
                    && (pact.EndReason != HostagePactEndReason.Betrayal
                        || (pact.FirstHostage?.ExecutionSucceeded != true && pact.SecondHostage?.ExecutionSucceeded != true)))) return false;
            try { return ApplyThreatEventOnce("pact-breach:" + pact.Id, aggressor, CampaignTime.Now.ToDays, 25d); }
            catch (Exception ex) { BellumCivileLogger.Log("Defensive pact breach threat recording failed: " + ex); return false; }
        }

        internal bool RecordExposedSubterfuge(Kingdom actor, Kingdom target, string missionId)
        {
            if (!_ratesInitialized || !BellumCivileOptions.DefensivePactsActive || actor == null || target == null || actor == target
                || actor.IsEliminated || target.IsEliminated || string.IsNullOrWhiteSpace(missionId)
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(actor)
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(target)) return false;
            try { return ApplyThreatEventOnce("subterfuge:" + missionId, actor, CampaignTime.Now.ToDays, 2.5d); }
            catch (Exception ex) { BellumCivileLogger.Log("Defensive pact subterfuge threat recording failed: " + ex); return false; }
        }

        private bool ApplyThreatEventOnce(string key, Kingdom realm, double day, double delta)
        {
            if (realm == null || string.IsNullOrWhiteSpace(realm.StringId) || string.IsNullOrWhiteSpace(key)) return false;
            return ApplyTreatyOnce("event:" + key, realm.StringId, day, CampaignWeeklyRate(realm, _baseWeeklyDecay), delta);
        }

        internal void RefreshCouncilRate(Kingdom realm)
        {
            if (!_ratesInitialized || string.IsNullOrWhiteSpace(realm?.StringId) || !_byRealm.ContainsKey(realm.StringId)) return;
            RefreshCampaignRealmRates(realm, null);
        }

        internal bool HasLiveThreat(Kingdom realm)
        {
            return _ratesInitialized && !string.IsNullOrWhiteSpace(realm?.StringId)
                && _byRealm.TryGetValue(realm.StringId, out var record) && record.Peek(CampaignTime.Now.ToDays) > 0;
        }

        private void RefreshCampaignRealmRates(Kingdom first, Kingdom second)
        {
            if (_byRealm.Count == 0) return;
            var realms = new Dictionary<string, Kingdom>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(first?.StringId)) realms[first.StringId] = first;
            if (!string.IsNullOrWhiteSpace(second?.StringId)) realms[second.StringId] = second;
            if (realms.Count == 0) return;
            try { RefreshRates(realms.Keys, CampaignTime.Now.ToDays, id => CampaignWeeklyRate(realms[id], _baseWeeklyDecay)); }
            catch (Exception ex) { BellumCivileLogger.Log("Defensive pact realm decay refresh failed: " + ex); }
        }

        internal void RefreshCampaignRateSettings(double baseWeeklyDecay, bool force = false)
        {
            if (!_ratesInitialized || !DefensivePactThreatRules.Finite(baseWeeklyDecay) || baseWeeklyDecay < 0
                || (!force && _baseWeeklyDecay == baseWeeklyDecay)) return;
            try
            {
                if (_byRealm.Count > 0)
                {
                    var realms = Kingdom.All.Where(k => k != null && !string.IsNullOrWhiteSpace(k.StringId)
                            && _byRealm.ContainsKey(k.StringId))
                        .GroupBy(k => k.StringId).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                    RefreshAllRates(CampaignTime.Now.ToDays, id => realms.TryGetValue(id, out var realm)
                        ? CampaignWeeklyRate(realm, baseWeeklyDecay) : _byRealm[id].WeeklyDecay);
                }
                _baseWeeklyDecay = baseWeeklyDecay;
            }
            catch (Exception ex) { BellumCivileLogger.Log("Defensive pact settings decay refresh failed: " + ex); }
        }

        private static double CampaignWeeklyRate(Kingdom realm, double baseWeeklyDecay)
        {
            var council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            bool active = council?.IsAssignmentActive(realm, PrivyCouncilOffice.Chancellor,
                DefensivePactThreatRules.ChancellorAssignmentId) == true;
            double competence = active ? council.GetEffectiveCoreOfficeCompetence(realm, PrivyCouncilOffice.Chancellor) : 0d;
            return DefensivePactThreatRules.WeeklyDecay(baseWeeklyDecay, realm?.Fiefs?.Count ?? 0,
                DefensivePactThreatRules.ChancellorEffect(active, competence));
        }

        private bool RecoverAppliedTreaties()
        {
            if (_treatyJournals == null || _treatyJournals.Count == 0) return true;
            try
            {
                var treaties = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>();
                if (treaties == null) return false;
                var proposals = treaties.GetProposals();
                ReconcileAppliedTreaties(proposals);
                foreach (var proposal in proposals.Where(p => p?.State == TreatyProposalState.Applied))
                {
                    var journal = FindTreaty(proposal.ProposalId);
                    if (journal != null && !journal.IsSealed) return false;
                }
                return ProcessSealedTreaties();
            }
            catch (Exception ex) { BellumCivileLogger.Log("Defensive pact treaty journal recovery failed: " + ex); return false; }
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_DefensivePactThreat", ref _records);
            dataStore.SyncData("BellumCivile_DefensivePactThreatTreatyReceipts", ref _treatyReceipts);
            dataStore.SyncData("BellumCivile_DefensivePactTreatyJournals", ref _treatyJournals);
            if (!dataStore.IsLoading) return;
            if (_records == null) _records = new List<DefensivePactThreatLedger>();
            if (_treatyReceipts == null) _treatyReceipts = new List<string>();
            _receivedTreaties = new HashSet<string>(_treatyReceipts, StringComparer.Ordinal);
            if (_treatyJournals == null) _treatyJournals = new List<DefensivePactTreatyJournal>();
            _journalsByProposal = new Dictionary<string, DefensivePactTreatyJournal>(StringComparer.Ordinal);
            foreach (var journal in _treatyJournals)
                if (journal != null && !string.IsNullOrWhiteSpace(journal.ProposalId) && !_journalsByProposal.ContainsKey(journal.ProposalId))
                    _journalsByProposal.Add(journal.ProposalId, journal);
            _byRealm = new Dictionary<string, DefensivePactThreatLedger>(StringComparer.Ordinal);
            foreach (var record in _records)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.RealmId) || _byRealm.ContainsKey(record.RealmId))
                    continue;
                _byRealm.Add(record.RealmId, record);
            }
        }

        internal double GetThreat(string realmId, double now)
        {
            if (string.IsNullOrWhiteSpace(realmId)) throw new ArgumentException(nameof(realmId));
            if (!DefensivePactThreatRules.Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            return _byRealm.TryGetValue(realmId, out var record) ? record.Peek(now) : 0d;
        }

        internal bool SetThreatForTesting(Kingdom realm, double value, double now)
        {
            if (!_ratesInitialized || !BellumCivileOptions.DefensivePactsActive || realm == null || realm.IsEliminated || string.IsNullOrWhiteSpace(realm.StringId)
                || !DefensivePactThreatRules.Finite(value) || value < 0d || value > 100d
                || !DefensivePactThreatRules.Finite(now) || now < 0d) return false;
            double current = GetThreat(realm.StringId, now);
            AdjustThreat(realm.StringId, now, CampaignWeeklyRate(realm, _baseWeeklyDecay), value - current);
            return true;
        }

        internal void AdjustThreat(string realmId, double now, double weeklyDecay, double delta)
        {
            if (string.IsNullOrWhiteSpace(realmId)) throw new ArgumentException(nameof(realmId));
            if (!DefensivePactThreatRules.Finite(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
            if (!_byRealm.TryGetValue(realmId, out var record))
            {
                record = new DefensivePactThreatLedger(realmId, 0d, now, weeklyDecay);
                record.Adjust(now, delta);
                if (record.Value <= 0) return;
                _records.Add(record);
                _byRealm.Add(realmId, record);
                return;
            }
            if (record.WeeklyDecay != weeklyDecay) record.ChangeRate(now, weeklyDecay);
            record.Adjust(now, delta);
        }

        internal void ChangeRate(string realmId, double now, double weeklyDecay)
        {
            if (string.IsNullOrWhiteSpace(realmId)) throw new ArgumentException(nameof(realmId));
            if (_byRealm.TryGetValue(realmId, out var record)) record.ChangeRate(now, weeklyDecay);
        }

        // The rate provider is never called for an untracked or fully decayed realm.
        internal int RefreshRates(IEnumerable<string> realmIds, double now, Func<string, double> rateForRealm)
        {
            if (realmIds == null) throw new ArgumentNullException(nameof(realmIds));
            if (rateForRealm == null) throw new ArgumentNullException(nameof(rateForRealm));
            if (!DefensivePactThreatRules.Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var updates = new List<(DefensivePactThreatLedger Record, double Rate)>();
            foreach (var id in realmIds)
            {
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id) || !_byRealm.TryGetValue(id, out var record)
                    || record.Peek(now) <= 0) continue;
                double rate = rateForRealm(id);
                DefensivePactThreatRules.ValueAt(0d, now, rate, now);
                if (record.WeeklyDecay != rate) updates.Add((record, rate));
            }
            foreach (var update in updates) update.Record.ChangeRate(now, update.Rate);
            return updates.Count;
        }

        internal int RefreshAllRates(double now, Func<string, double> rateForRealm)
            => RefreshRates(_byRealm.Keys, now, rateForRealm);

        internal bool ApplyTreatyOnce(string proposalId, string realmId, double now, double weeklyDecay, double delta)
        {
            if (string.IsNullOrWhiteSpace(proposalId)) throw new ArgumentException(nameof(proposalId));
            if (string.IsNullOrWhiteSpace(realmId)) throw new ArgumentException(nameof(realmId));
            if (!DefensivePactThreatRules.Finite(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
            DefensivePactThreatRules.ValueAt(0d, now, weeklyDecay, now);
            string receipt = TreatyReceiptKey(proposalId, realmId);
            if (_receivedTreaties.Contains(receipt)) return false;
            AdjustThreat(realmId, now, weeklyDecay, delta);
            _treatyReceipts.Add(receipt);
            _receivedTreaties.Add(receipt);
            return true;
        }

        private static string TreatyReceiptKey(string proposalId, string realmId) => proposalId.Length + ":" + proposalId + realmId;

        internal bool CaptureTreatyRates(DefensivePactTreatyJournal journal, Kingdom winner, Kingdom loser)
        {
            return _ratesInitialized && journal != null && winner != null && loser != null
                && journal.CaptureRates(CampaignWeeklyRate(winner, _baseWeeklyDecay), CampaignWeeklyRate(loser, _baseWeeklyDecay));
        }

        internal void SuspendRateUpdates() => _ratesInitialized = false;

        internal DefensivePactTreatyJournal PrepareTreaty(TreatyProposalRecord proposal, DefensivePactTreatySnapshot snapshot,
            double settlementDay)
        {
            if (!BellumCivileOptions.DefensivePactsActive || proposal == null || string.IsNullOrWhiteSpace(proposal.ProposalId)) return null;
            if (_journalsByProposal.TryGetValue(proposal.ProposalId, out var existing)) return existing;
            if (proposal.State != TreatyProposalState.ParleyPending || snapshot == null || !snapshot.Matches(proposal)) return null;
            var journal = snapshot.CreateJournal(proposal.ProposalId, settlementDay);
            if (!journal.CaptureGainMultiplier(BellumCivileOptions.TreatyThreatGainPercent / 100d)) return null;
            _treatyJournals.Add(journal);
            _journalsByProposal.Add(proposal.ProposalId, journal);
            return journal;
        }

        internal DefensivePactTreatyJournal FindTreaty(string proposalId)
        {
            return !string.IsNullOrWhiteSpace(proposalId) && _journalsByProposal.TryGetValue(proposalId, out var journal)
                ? journal : null;
        }

        internal int ReconcileAppliedTreaties(IEnumerable<TreatyProposalRecord> proposals)
        {
            if (proposals == null || _treatyJournals == null || _treatyJournals.Count == 0) return 0;
            int sealedCount = 0;
            foreach (var proposal in proposals)
            {
                if (proposal?.State != TreatyProposalState.Applied) continue;
                var journal = FindTreaty(proposal.ProposalId);
                if (journal != null && journal.Seal(proposal)) sealedCount++;
            }
            return sealedCount;
        }

        // Callers must settle pending journals before advancing other threat events past their settlement day.
        internal bool ApplySealedTreaty(DefensivePactTreatyJournal journal)
        {
            if (!BellumCivileOptions.DefensivePactsActive || journal == null || !journal.IsSealed || FindTreaty(journal.ProposalId) != journal) return false;
            var projection = journal.EvaluateDelivered();
            if (projection == null || !projection.IsReliable) return false;
            double settlementDay = journal.SettlementDay;
            var pending = new List<(string Realm, double Rate, double Delta)>();
            foreach (var entry in projection.Impacts.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(entry.Key)) return false;
                if (_receivedTreaties.Contains(TreatyReceiptKey(journal.ProposalId, entry.Key))) continue;
                if (!journal.TryGetRate(entry.Key, out double rate)) return false;
                double delta = entry.Value.NetChange;
                if (!DefensivePactThreatRules.Finite(delta)) return false;
                try
                {
                    DefensivePactThreatRules.ValueAt(0d, settlementDay, rate, settlementDay);
                    GetThreat(entry.Key, settlementDay);
                }
                catch (ArgumentOutOfRangeException) { return false; }
                pending.Add((entry.Key, rate, delta));
            }
            foreach (var entry in pending)
                ApplyTreatyOnce(journal.ProposalId, entry.Realm, settlementDay, entry.Rate, entry.Delta);
            return true;
        }

        internal bool ProcessSealedTreaties()
        {
            foreach (var journal in _treatyJournals.Where(j => j != null && j.IsSealed).OrderBy(j => j.SettlementDay).ToList())
                if (!CompleteSealedTreaty(journal)) return false;
            return true;
        }

        internal bool CompleteSealedTreaty(DefensivePactTreatyJournal journal)
        {
            if (!BellumCivileOptions.DefensivePactsActive) return false;
            if (!ApplySealedTreaty(journal)) return false;
            _journalsByProposal.Remove(journal.ProposalId);
            _treatyJournals.Remove(journal);
            return true;
        }
    }
}
