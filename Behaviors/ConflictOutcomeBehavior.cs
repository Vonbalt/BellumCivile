using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed class ConflictOutcomeBehavior : CampaignBehaviorBase
    {
        public static ConflictOutcomeBehavior Current => Campaign.Current?.GetCampaignBehavior<ConflictOutcomeBehavior>();
        private List<ConflictOutcomeNotice> _notices = new List<ConflictOutcomeNotice>();
        private readonly Dictionary<string, ConflictOutcomeNotice> _byId = new Dictionary<string, ConflictOutcomeNotice>();
        private readonly Queue<ConflictOutcomeNotice> _pending = new Queue<ConflictOutcomeNotice>();
        private ConflictOutcomeNotice _active;
        internal bool HasPending => _active != null || _pending.Count > 0;

        public override void RegisterEvents() => CampaignEvents.TickEvent.AddNonSerializedListener(this, _ => Process());
        public override void SyncData(IDataStore store)
        {
            store.SyncData("BC_ConflictOutcomeNotices", ref _notices);
            _notices = _notices ?? new List<ConflictOutcomeNotice>();
            if (store.IsLoading) Rebuild();
        }
        private void Rebuild()
        {
            _active = null; _byId.Clear(); _pending.Clear();
            foreach (var notice in _notices)
            {
                _byId[notice.Id] = notice;
                if (notice.Ready && notice.PlayerInvolved && !notice.Acknowledged) _pending.Enqueue(notice);
            }
        }
        internal ConflictOutcomeNotice Begin(string id, Kingdom first, Kingdom second, Kingdom observerRealm = null)
        {
            if (_byId.TryGetValue(id, out var prior)) return prior;
            var playerRealm = Clan.PlayerClan?.Kingdom;
            var notice = new ConflictOutcomeNotice
            {
                Id = id,
                PlayerInvolved = playerRealm != null && (playerRealm == first || playerRealm == second || playerRealm == observerRealm),
                ChatAllowed = BellumCivileNotifications.ShouldShow(primaryKingdom: first, secondaryKingdom: second)
            };
            _notices.Add(notice); _byId[id] = notice;
            return notice;
        }
        internal ConflictOutcomeNotice BeginCivil(FactionObject faction, Kingdom rebel, string id = null, Kingdom third = null)
        {
            var notice = Begin(id ?? "civil:" + (rebel?.StringId ?? faction.GetTrackedRebelKingdomIncludingEliminated()?.StringId),
                faction.ParentKingdom, rebel, third);
            if (!notice.Ready && !notice.PlayerInvolved && Clan.PlayerClan != null)
                notice.PlayerInvolved = faction.Members.Contains(Clan.PlayerClan)
                    || Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionsInKingdom(faction.ParentKingdom)
                        .Any(f => !f.IsIdeology && f.HasTrackedRebelKingdom
                            && f.GetTrackedRebelKingdomIncludingEliminated() == Clan.PlayerClan.Kingdom) == true;
            Capture(notice, "LEADER", faction.Leader?.Leader?.Name ?? faction.Leader?.Name);
            Capture(notice, "RULER", faction.ParentKingdom?.Leader?.Name);
            Capture(notice, "REALM", faction.ParentKingdom?.Name);
            Capture(notice, "FACTION", faction.GetDisplayName());
            return notice;
        }
        internal static void Capture(ConflictOutcomeNotice notice, string key, TextObject value)
        {
            if (notice != null && !notice.Names.ContainsKey(key)) notice.Names[key] = value?.ToString() ?? "";
        }
        internal static string Render(ConflictOutcomeNotice notice, string template)
        {
            var text = new TextObject(template);
            foreach (var name in notice.Names) text.SetTextVariable(name.Key, name.Value);
            return text.ToString();
        }
        internal void Publish(ConflictOutcomeNotice notice, string kind, string addition = null)
        {
            if (notice == null || notice.Ready) return;
            var copy = ConflictOutcomeText.For(kind);
            notice.Title = Render(notice, copy[0]);
            notice.Chat = Render(notice, copy[1]);
            notice.Body = notice.Chat + "\n\n" + Render(notice, copy[2]);
            if (notice.Names.TryGetValue("EXHAUSTION", out var exhaustion) && (kind == "claimant" || kind == "loyalist" || kind == "abdication" || kind == "independence"))
                notice.Body += "\n\n" + exhaustion;
            if (!string.IsNullOrEmpty(addition)) notice.Body += "\n\n" + addition;
            if (notice.Names.TryGetValue("TRIBUNAL", out var tribunal)) notice.Body += "\n\n" + tribunal;
            notice.Ready = true;
            if (notice.PlayerInvolved) _pending.Enqueue(notice);
            else notice.Acknowledged = true;
            if (notice.ChatAllowed)
                InformationManager.DisplayMessage(new InformationMessage(notice.Chat, BellumNotificationColors.Warning));
        }
        internal static string ContinuingWars(Kingdom realm)
        {
            var rivals = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetFactionsInKingdom(realm)
                .Where(f => !f.IsIdeology && f.HasTrackedRebelKingdom && f.GetTrackedRebelKingdomIncludingEliminated()?.IsEliminated == false
                    && f.GetTrackedRebelKingdomIncludingEliminated().IsAtWarWith(realm))
                .Select(f => f.Leader?.Leader?.Name?.ToString()).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
            if (rivals == null || rivals.Count == 0) return null;
            return new TextObject("{=BC_Result_Rivals}{RIVALS} still stand against the Crown. Their wars continue.")
                .SetTextVariable("RIVALS", string.Join(", ", rivals)).ToString();
        }
        internal static string RemainingWars(Kingdom realm, IEnumerable<Kingdom> externalEnemies)
        {
            var parts = new List<string>();
            var rivals = ContinuingWars(realm);
            if (!string.IsNullOrEmpty(rivals)) parts.Add(rivals);
            var enemies = externalEnemies?.Where(k => k != null && !k.IsEliminated && realm.IsAtWarWith(k))
                .Select(k => k.Name.ToString()).Distinct().ToList();
            if (enemies?.Count > 0)
                parts.Add(new TextObject("{=BC_Result_ForeignWars}The war against {ENEMIES} continues.")
                    .SetTextVariable("ENEMIES", string.Join(", ", enemies)).ToString());
            return string.Join("\n\n", parts);
        }
        private void Process()
        {
            if (!HasPending || _active != null || InformationManager.IsAnyInquiryActive()
                || !(Game.Current?.GameStateManager?.ActiveState is MapState)
                || Hero.OneToOneConversationHero != null) return;
            while (_pending.Count > 0 && _pending.Peek().Acknowledged) _pending.Dequeue();
            if (_pending.Count == 0) return;
            var notice = _pending.Peek();
            _active = notice;
            try
            {
                InformationManager.ShowInquiry(new InquiryData(notice.Title, notice.Body, true, false,
                new TextObject("{=BC_Result_Continue}Continue").ToString(), "", () =>
                {
                    notice.Acknowledged = true;
                    if (_pending.Count > 0 && _pending.Peek() == notice) _pending.Dequeue();
                    _active = null;
                }, null), true);
            }
            catch
            {
                _active = null;
                throw;
            }
        }
    }
}
