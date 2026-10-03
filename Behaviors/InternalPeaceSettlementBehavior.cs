using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ScreenSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class InternalPeaceSettlementBehavior : CampaignBehaviorBase
    {
        private Dictionary<string, string> _first = new Dictionary<string, string>();
        private Dictionary<string, string> _second = new Dictionary<string, string>();
        private Dictionary<string, string> _winner = new Dictionary<string, string>();
        private Dictionary<string, string> _consentLeader = new Dictionary<string, string>();
        private Dictionary<string, string> _reservedScore = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _lastFailure = new Dictionary<string, string>();
        private bool _applying;
        private float _retrySeconds;
        internal static InternalPeaceSettlementBehavior Current => Campaign.Current?.GetCampaignBehavior<InternalPeaceSettlementBehavior>();

        public override void RegisterEvents()
        {
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
        }

        public override void SyncData(IDataStore store)
        {
            store.SyncData("BC_InternalPeaceFirst", ref _first);
            store.SyncData("BC_InternalPeaceSecond", ref _second);
            store.SyncData("BC_InternalPeaceWinner", ref _winner);
            store.SyncData("BC_InternalPeaceConsentLeader", ref _consentLeader);
            store.SyncData("BC_InternalPeaceReservedScore", ref _reservedScore);
            SyncOffers(store);
            _first = _first ?? new Dictionary<string, string>();
            _second = _second ?? new Dictionary<string, string>();
            _winner = _winner ?? new Dictionary<string, string>();
            _consentLeader = _consentLeader ?? new Dictionary<string, string>();
            _reservedScore = _reservedScore ?? new Dictionary<string, string>();
        }

        internal static bool TryIdentify(Kingdom first, Kingdom second, out string key, out bool rivalry)
        {
            key = null; rivalry = false;
            if (first == null || second == null || first == second) return false;
            var feud = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>()?.GetActiveWars().FirstOrDefault(w =>
                w.ClaimantKingdomId == first.StringId && w.HolderKingdomId == second.StringId
                || w.ClaimantKingdomId == second.StringId && w.HolderKingdomId == first.StringId);
            if (feud != null) { key = "feud:" + feud.WarId; return true; }
            var manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            var a = manager?.GetFactionByRebelKingdom(first);
            var b = manager?.GetFactionByRebelKingdom(second);
            if (a?.ParentKingdom == second) { key = "civil:" + first.StringId; return true; }
            if (b?.ParentKingdom == first) { key = "civil:" + second.StringId; return true; }
            rivalry = a != null && b != null && a.ParentKingdom != null && a.ParentKingdom == b.ParentKingdom;
            if (rivalry) key = string.CompareOrdinal(first.StringId, second.StringId) < 0
                ? "rival:" + first.StringId + ":" + second.StringId
                : "rival:" + second.StringId + ":" + first.StringId;
            return rivalry;
        }

        internal bool QueueWhitePeace(Kingdom first, Kingdom second, bool resolutionAlreadyBegun = false)
            => QueueSettlement(first, second, null, resolutionAlreadyBegun: resolutionAlreadyBegun);

        internal bool QueueSettlement(Kingdom first, Kingdom second, Kingdom winner, bool awaitingPlayer = false, bool resolutionAlreadyBegun = false)
        {
            if (!TryIdentify(first, second, out string key, out bool rivalry) || rivalry && winner == null
                || winner != null && winner != first && winner != second) return false;
            if (awaitingPlayer && (second.Leader == null || Clan.PlayerClan == null || second.RulingClan != Clan.PlayerClan)) return false;
            if (_first.ContainsKey(key)) return false;
            var score = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(first, second);
            if (score != null)
            {
                if (!resolutionAlreadyBegun && !score.BeginResolution()) return false;
                _reservedScore[key] = score.WarKey;
            }
            _first[key] = first.StringId;
            _second[key] = second.StringId;
            _winner[key] = winner?.StringId ?? "";
            if (awaitingPlayer && second.Leader != null) _consentLeader[key] = second.Leader.StringId;
            BellumCivileLogger.Log($"Internal settlement queued; conflict={key}; first={first.StringId}; second={second.StringId}; winner={winner?.StringId ?? "white_peace"}.");
            return true;
        }

        private void OnTick(float dt)
        {
            if (_applying || _first.Count == 0 && _offerDecisions.Count == 0 || InformationManager.IsAnyInquiryActive()
                || !(Game.Current?.GameStateManager?.ActiveState is MapState)
                || Hero.OneToOneConversationHero != null
                || UI.Parley.PeaceParleyInterface.Instance.IsShown) return;
            // Kingdom management can overlay MapState. Wait for the actual map screen.
            var screen = ScreenManager.TopScreen?.GetType();
            while (screen != null && screen.Name != "MapScreen") screen = screen.BaseType;
            if (screen == null) return;
            _retrySeconds += dt;
            if (_retrySeconds < 1f) return;
            _retrySeconds = 0f;
            PruneFinishedVotes();
            if (_first.Count == 0) return;
            _applying = true;
            try
            {
                foreach (string key in _first.Keys.ToList())
                {
                    if (InformationManager.IsAnyInquiryActive()) break;
                    try
                    {
                        var first = Kingdom.All.FirstOrDefault(k => k.StringId == _first[key]);
                        _second.TryGetValue(key, out string secondId);
                        var second = Kingdom.All.FirstOrDefault(k => k.StringId == secondId);
                        _winner.TryGetValue(key, out string winnerId);
                        if (!TryIdentify(first, second, out string current, out bool rival) || current != key || rival && string.IsNullOrEmpty(winnerId)
                            || !string.IsNullOrEmpty(winnerId) && winnerId != first.StringId && winnerId != second.StringId)
                        {
                            BellumCivileLogger.Log($"Internal settlement retired after conflict identity changed; conflict={key}.");
                            Remove(key); continue;
                        }
                        if (CivilWarConflictBehavior.IsRealmTransferPending(first) || CivilWarConflictBehavior.IsRealmTransferPending(second)) continue;
                        if (_consentLeader.TryGetValue(key, out string leader))
                        {
                            if (second.Leader?.StringId != leader || second.RulingClan != Clan.PlayerClan || !first.IsAtWarWith(second))
                            { Remove(key); continue; }
                            AskPlayerConsent(key, first, second, winnerId, leader);
                            break;
                        }
                        if (key.StartsWith("feud:", StringComparison.Ordinal))
                        {
                            var feuds = Campaign.Current.GetCampaignBehavior<ClaimFeudWarBehavior>();
                            var feud = feuds.GetActiveWars().First(w => "feud:" + w.WarId == key);
                            if (string.IsNullOrEmpty(winnerId)) feuds.TryResolveUnscriptedPeace(first, second);
                            else feuds.TryResolveWarScore(FindRealm(feud.ClaimantKingdomId), FindRealm(feud.HolderKingdomId),
                                winnerId == feud.ClaimantKingdomId, "negotiated internal settlement");
                        }
                        else if (rival)
                        {
                            var score = Campaign.Current.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(first, second);
                            if (score != null) Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>()?
                                .TryResolveRivalVictory(score, score.AttackerKingdomId == winnerId);
                        }
                        else
                        {
                            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
                            var faction = manager.GetFactionByRebelKingdom(first);
                            var rebel = first;
                            if (faction?.ParentKingdom != second) { faction = manager.GetFactionByRebelKingdom(second); rebel = second; }
                            var resolution = Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>();
                            if (string.IsNullOrEmpty(winnerId)) resolution?.ResolveUnscriptedPeace(faction, rebel);
                            else if (winnerId == rebel.StringId) resolution?.ResolveRebelVictory(faction, rebel);
                            else resolution?.TryResolveTerminalLiegeVictory(faction, rebel);
                        }
                        if (!TryIdentify(first, second, out _, out _))
                        {
                            Remove(key);
                            BellumCivileLogger.Log($"Internal settlement completed; conflict={key}; winner={winnerId}.");
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!_lastFailure.TryGetValue(key, out string previous) || previous != ex.ToString())
                            BellumCivileLogger.Log($"Internal settlement deferred after exception; conflict={key}; {ex}");
                        _lastFailure[key] = ex.ToString();
                    }
                }
            }
            finally { _applying = false; }
        }

        private static Kingdom FindRealm(string id) => Kingdom.All.FirstOrDefault(k => k.StringId == id);
        private void Remove(string key)
        {
            if (_reservedScore.TryGetValue(key, out string warKey))
                Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(warKey)?.CancelResolution();
            _reservedScore.Remove(key);
            _first.Remove(key); _second.Remove(key); _winner.Remove(key); _consentLeader.Remove(key); _lastFailure.Remove(key);
        }

        private void AskPlayerConsent(string key, Kingdom first, Kingdom second, string winner, string leader)
        {
            TextObject terms = string.IsNullOrEmpty(winner) ? Patches.InternalPeaceVoteText.Terms()
                : new TextObject("{=BC_InternalPeace_VictoryTerms}Recognize the cause of {REALM} and end this conflict in their favor. This is a concession of defeat, not a white peace; the existing laws of settlement and judgment will apply.")
                    .SetTextVariable("REALM", FindRealm(winner)?.Name ?? new TextObject(winner));
            InformationManager.ShowInquiry(new InquiryData(
                new TextObject("{=BC_InternalPeace_Title}Terms of Reconciliation").ToString(),
                new TextObject("{=BC_InternalPeace_Received}An envoy from {REALM} brings terms endorsed by their council. Will you accept this settlement?\n\n{TERMS}")
                    .SetTextVariable("REALM", first.Name).SetTextVariable("TERMS", terms).ToString(),
                true, true, new TextObject("{=BC_InternalPeace_Accept}Accept the Terms").ToString(),
                new TextObject("{=BC_InternalPeace_Refuse}Continue the Conflict").ToString(),
                () =>
                {
                    if (_first.ContainsKey(key) && second.Leader?.StringId == leader && second.RulingClan == Clan.PlayerClan
                        && first.IsAtWarWith(second) && TryIdentify(first, second, out string current, out _) && current == key)
                        _consentLeader.Remove(key);
                    else Remove(key);
                },
                () => { Remove(key); BellumCivileLogger.Log($"Internal settlement refused by player; conflict={key}."); }));
        }
    }
}
