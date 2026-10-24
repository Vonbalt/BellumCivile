using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    internal static class RealmLeadershipDiagnostics
    {
        private const string Tag = "[BellumCivile realm leadership diagnostic]";
        private static readonly ConditionalWeakTable<Campaign, Session> Sessions = new ConditionalWeakTable<Campaign, Session>();

        private sealed class Session
        {
            internal readonly Dictionary<Kingdom, Snapshot> Last = new Dictionary<Kingdom, Snapshot>();
            internal readonly Dictionary<Kingdom, double> Open = new Dictionary<Kingdom, double>();
            internal readonly HashSet<Kingdom> DeferredDestruction = new HashSet<Kingdom>();
            internal bool FailureLogged;
        }

        internal sealed class Snapshot
        {
            internal readonly Kingdom Realm;
            internal readonly string HouseId, LeaderId, Reason;
            internal readonly bool Eliminated;

            internal Snapshot(Kingdom realm)
            {
                Realm = realm;
                var house = realm.RulingClan;
                var leader = realm.Leader;
                HouseId = house?.StringId ?? "null";
                LeaderId = leader?.StringId ?? "null";
                Eliminated = realm.IsEliminated;
                Reason = house == null ? "no_ruling_clan" : leader == null ? "ruling_house_has_no_leader"
                    : leader.IsDead ? "ruler_dead" : null;
            }
        }

        internal static Snapshot[] Capture(object instance)
        {
            try
            {
                if (Campaign.Current == null) return Array.Empty<Snapshot>();
                if (instance is Kingdom realm) return new[] { new Snapshot(realm) };
                var clan = instance as Clan;
                if (instance is Hero hero)
                {
                    clan = hero.Clan;
                    if (clan?.Leader != hero) return Array.Empty<Snapshot>();
                }
                if (clan == null) return Array.Empty<Snapshot>();
                // A former realm may still reference this house after it has moved elsewhere.
                return Kingdom.All.Where(k => k != null && k.RulingClan == clan).Select(k => new Snapshot(k)).ToArray();
            }
            catch (Exception ex) { Failure(ex); return Array.Empty<Snapshot>(); }
        }

        internal static void Complete(Snapshot[] before, MethodBase origin, Exception exception)
        {
            try
            {
                foreach (var state in before ?? Array.Empty<Snapshot>())
                    Observe(state.Realm, origin?.DeclaringType?.FullName + "." + origin?.Name, state, origin, exception);
            }
            catch (Exception ex) { Failure(ex); }
        }

        internal static void OnGameLoaded(CampaignGameStarter starter) => Scan("save_loaded");
        internal static void OnSessionLaunched(CampaignGameStarter starter) => Scan("session_launched");
        internal static void OnHourlyTick() => Scan("hourly_audit");

        private static void Scan(string source)
        {
            try
            {
                if (Campaign.Current == null) return;
                foreach (var realm in Kingdom.All) Observe(realm, source);
            }
            catch (Exception ex) { Failure(ex); }
        }

        private static void Observe(Kingdom realm, string source, Snapshot before = null, MethodBase origin = null, Exception exception = null)
        {
            try
            {
                var campaign = Campaign.Current;
                if (realm == null || campaign == null) return;
                var session = Sessions.GetValue(campaign, _ => new Session());
                var now = new Snapshot(realm);
                if (before == null) session.Last.TryGetValue(realm, out before);
                session.Last[realm] = now;
                if (now.Eliminated || now.Reason == null)
                {
                    if (!session.Open.TryGetValue(realm, out double started)) return;
                    session.Open.Remove(realm);
                    session.DeferredDestruction.Remove(realm);
                    BellumCivileLogger.Log($"{Tag} stage={(now.Eliminated ? "retired" : "recovered")}; realm={realm.StringId}; "
                        + $"source={source}; day={CampaignTime.Now.ToDays:F3}; elapsed_days={Math.Max(0, CampaignTime.Now.ToDays - started):F3}; "
                        + $"ruling_clan={now.HouseId}; leader={now.LeaderId}; native_execution_unchanged=true.");
                    BellumCivileLogger.Flush();
                    return;
                }
                if (session.Open.ContainsKey(realm))
                {
                    if (origin?.DeclaringType == typeof(TaleWorlds.CampaignSystem.Actions.DestroyKingdomAction)
                        && session.DeferredDestruction.Add(realm))
                    {
                        BellumCivileLogger.Log($"{Tag} stage=destruction_not_completed; realm={realm.StringId}; "
                            + $"day={CampaignTime.Now.ToDays:F3}; reason={now.Reason}; exception={exception?.GetType().FullName ?? "none"}; "
                            + $"patch_owners={string.Join(",", Harmony.GetPatchInfo(origin)?.Owners ?? Enumerable.Empty<string>())}; "
                            + "native_execution_unchanged=true.");
                        BellumCivileLogger.Flush();
                    }
                    return;
                }
                session.Open.Add(realm, CampaignTime.Now.ToDays);
                try
                {
                    BellumCivileLogger.Log($"{Tag} stage={(before != null && before.Reason == null ? "became_leaderless" : "first_observed_leaderless")}; "
                        + $"realm={realm.StringId}; source={source}; day={CampaignTime.Now.ToDays:F3}; reason={now.Reason}; "
                        + $"previous_ruling_clan={before?.HouseId ?? "unknown"}; previous_leader={before?.LeaderId ?? "unknown"}; "
                        + $"ruling_clan={now.HouseId}; leader={now.LeaderId}; eliminated={now.Eliminated}; "
                        + $"causal_stack_available={origin != null && before != null && before.Reason == null}; "
                        + $"exception={exception?.GetType().FullName ?? "none"}; native_execution_unchanged=true.");
                    Detail("realm", () => $"culture={realm.Culture?.StringId}; home={realm.InitialHomeSettlement?.StringId}; "
                        + $"ruling_house_realm={realm.RulingClan?.Kingdom?.StringId}; ruling_house_eliminated={realm.RulingClan?.IsEliminated}; "
                        + $"bellum_id_pattern={BellumKingdomVisibilityHelper.IsBellumGeneratedKingdom(realm)}; "
                        + $"clans={realm.Clans.Count}; fiefs={realm.Fiefs.Count}; decisions={realm.UnresolvedDecisions.Count}");
                    Detail("houses", () => Join(realm.Clans.Select(c => c == null ? "null" :
                        $"{c.StringId}:leader={c.Leader?.StringId},leader_dead={c.Leader?.IsDead},realm={c.Kingdom?.StringId},"
                        + $"eliminated={c.IsEliminated},mercenary={c.IsUnderMercenaryService}")));
                    Detail("holdings", () => Join(realm.Fiefs.Select(f => f?.Settlement?.StringId)));
                    Detail("recovery", () => DescribeRecovery(realm));
                    Detail("accessions", () => Join(Records<CrownAccessionRecord>(campaign.GetCampaignBehavior<CrownAccessionBehavior>(), "_accessions")
                        .Where(a => a != null && (a.Realm == realm || a.Union?.Source == realm || a.Union?.Destination == realm))
                        .Select(a => $"realm={a.Realm?.StringId},heir={a.Heir?.StringId},completed={a.Completed},"
                            + $"union_source={a.Union?.Source?.StringId},union_target={a.Union?.Destination?.StringId},union_completed={a.Union?.Completed}")));
                    Detail("callers", () => string.Join(" > ", (new StackTrace(2, false).GetFrames() ?? Array.Empty<StackFrame>())
                        .Select(f => f.GetMethod()).Where(m => m != null && m.DeclaringType != typeof(RealmLeadershipDiagnostics))
                        .Take(12).Select(m => $"{m.DeclaringType?.FullName}.{m.Name}[{m.Module.Assembly.GetName().Name}]")));
                    Detail("patch_owners", () => string.Join(" | ", Patches.RealmLeadershipDiagnosticPatch.TargetMethods()
                        .Where(m => m != null).Select(m => m.DeclaringType?.Name + "." + m.Name + ":"
                            + string.Join(",", Harmony.GetPatchInfo(m)?.Owners ?? Enumerable.Empty<string>()))));
                }
                finally { BellumCivileLogger.Flush(); }
            }
            catch (Exception ex) { Failure(ex); }
        }

        private static string DescribeRecovery(Kingdom realm)
        {
            var campaign = Campaign.Current;
            var resolution = campaign.GetCampaignBehavior<CivilWarResolutionBehavior>();
            var manager = campaign.GetCampaignBehavior<FactionManagerBehavior>();
            bool cleanup = Read<List<string>>(resolution, "_pendingRebelCleanupOrder")?.Contains(realm.StringId) == true;
            var pending = Read<Dictionary<string, string>>(resolution, "_pendingSuccessionCandidates");
            string candidate = null;
            pending?.TryGetValue(realm.StringId, out candidate);
            return $"cleanup_queued={cleanup}; succession_candidate={candidate ?? "none"}; factions=["
                + Join(Records<FactionObject>(manager, "_activeFactions")
                    .Where(f => f != null && !f.IsIdeology && (f.ParentKingdom == realm || f.IsTrackedRebelKingdom(realm)))
                    .Select(f => $"{f.Type}:parent={f.ParentKingdom?.StringId},house={f.Leader?.StringId}"))
                + "]; collapses=[" + Join(Records<CivilWarCollapseRecord>(resolution, "_collapses")
                    .Where(c => c != null && (c.Parent == realm || c.WinnerRealm == realm || c.Successor == realm || c.SuccessorId == realm.StringId))
                    .Select(c => $"parent={c.Parent?.StringId},successor={c.SuccessorId},stage={c.Stage},completed={c.Completed},failure={c.Failure}"))
                + "]; restorations=[" + Join(Records<RestoredRealmCreationRecord>(resolution, "_restoredCreations")
                    .Where(r => r != null && (r.Parent == realm || r.Source == realm || r.Successor == realm || r.SuccessorId == realm.StringId))
                    .Select(r => $"parent={r.Parent?.StringId},source={r.Source?.StringId},successor={r.SuccessorId},"
                        + $"moves_complete={r.MovesComplete},ready={r.Ready},failure={r.Failure}")) + "]";
        }

        // Read journals directly; diagnostic queries must never queue repairs or destroy a realm.
        private static T Read<T>(object owner, string field) where T : class => owner == null ? null
            : (T)AccessTools.Field(owner.GetType(), field).GetValue(owner);
        private static IEnumerable<T> Records<T>(object owner, string field) => Read<IEnumerable<T>>(owner, field) ?? Enumerable.Empty<T>();
        private static string Join(IEnumerable<string> rows)
        {
            var list = rows.Take(21).ToList();
            return list.Count == 0 ? "none" : string.Join(" | ", list.Take(20)) + (list.Count > 20 ? " | truncated_after_20" : "");
        }
        private static void Detail(string section, Func<string> describe)
        {
            try { BellumCivileLogger.Log($"{Tag} {section}: {describe()}"); }
            catch (Exception ex) { BellumCivileLogger.Log($"{Tag} {section}: unavailable={ex.GetType().Name}: {ex.Message}"); }
        }
        private static void Failure(Exception ex)
        {
            try
            {
                var campaign = Campaign.Current;
                if (campaign == null) return;
                var session = Sessions.GetValue(campaign, _ => new Session());
                if (session.FailureLogged) return;
                session.FailureLogged = true;
                BellumCivileLogger.Log($"{Tag} diagnostic_failed={ex.GetType().Name}: {ex.Message}; native_execution_unchanged=true");
                BellumCivileLogger.Flush();
            }
            catch { }
        }
    }
}
