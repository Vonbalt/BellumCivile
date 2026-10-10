using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BellumCivile.Behaviors
{
    public sealed partial class ClientKingdomBehavior
    {
        private bool _startingClientagePending;
        private List<ClientKingdomRecord> _pendingStartingClientages;
        internal bool IsApplyingStartingClientage { get; private set; }

        private void OnNewGameCreated(CampaignGameStarter starter)
        {
            // Loading an old save never arms this path, even if it has no client records.
            _startingClientagePending = true;
            _pendingStartingClientages = null;
        }

        private void OnAfterSessionLaunched(CampaignGameStarter starter)
        {
            ApplyStartingClientages();
        }

        private bool PrepareStartingClientages()
        {
            var declarations = FeudalTitleConfig.Instance.StartingClientages;
            var records = new List<ClientKingdomRecord>();
            float today = CurrentDay;
            foreach (var entry in declarations)
            {
                var client = ResolveKingdom(entry.ClientId);
                var suzerain = ResolveKingdom(entry.SuzerainId);
                if (!IsValidStartingRealm(client) || !IsValidStartingRealm(suzerain))
                {
                    BellumCivileLogger.Log($"Starting clientage configuration rejected: '{entry.ClientId}' -> '{entry.SuzerainId}' in {entry.Source} requires exact IDs of permanent realms with living ruling clans and leaders.");
                    return false;
                }
                records.Add(new ClientKingdomRecord(entry.ClientId, entry.SuzerainId, today,
                    entry.Voluntary, today + entry.LiberationCooldownDays));
            }

            if (!ValidateStartingClientageGraph(_clients.Concat(records), out string error))
            {
                BellumCivileLogger.Log("Starting clientage configuration rejected: " + error + ". Sources: "
                    + string.Join(", ", declarations.Select(entry => entry.Source).Distinct()));
                return false;
            }

            // Persist the resolved declarations, not a request to reread possibly edited XML on reload.
            _pendingStartingClientages = records;
            return true;
        }

        private static bool IsValidStartingRealm(Kingdom realm)
        {
            return IsValidPermanentRealm(realm) && !realm.RulingClan.IsEliminated
                && realm.RulingClan.Leader.IsAlive && realm.RulingClan.Kingdom == realm;
        }

        private static bool ValidateStartingClientageGraph(IEnumerable<ClientKingdomRecord> records, out string error)
        {
            var byClient = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.ClientKingdomId)
                    || string.IsNullOrWhiteSpace(record.SuzerainKingdomId))
                {
                    error = "missing client or suzerain ID";
                    return false;
                }
                if (record.ClientKingdomId == record.SuzerainKingdomId
                    || byClient.ContainsKey(record.ClientKingdomId))
                {
                    error = $"self-clientage or conflicting clientages for '{record.ClientKingdomId}'";
                    return false;
                }
                byClient.Add(record.ClientKingdomId, record.SuzerainKingdomId);
            }
            foreach (var entry in byClient)
            {
                if (byClient.ContainsKey(entry.Value))
                {
                    error = $"nested or cyclic clientage: '{entry.Key}' -> '{entry.Value}'; a client cannot also be a suzerain";
                    return false;
                }
            }
            error = null;
            return true;
        }

        private void ApplyStartingClientages()
        {
            if (!_startingClientagePending || IsApplyingStartingClientage) return;
            EnsureCollectionsInitialized();
            try
            {
                if (_pendingStartingClientages == null && !PrepareStartingClientages())
                {
                    _startingClientagePending = false;
                    return;
                }
                if (_pendingStartingClientages.Count == 0)
                {
                    _startingClientagePending = false;
                    return;
                }

                var combined = _clients.Concat(_pendingStartingClientages).ToList();
                if (!ValidateStartingClientageGraph(combined, out string error)
                    || combined.Any(record => !IsValidStartingRealm(ResolveKingdom(record.ClientKingdomId))
                        || !IsValidStartingRealm(ResolveKingdom(record.SuzerainKingdomId))))
                {
                    // Another initialization or gameplay action took ownership of these realms.
                    BellumCivileLogger.Log("Starting clientage cancelled before publication: " + (error ?? "a participant is no longer valid"));
                    _pendingStartingClientages.Clear();
                    _startingClientagePending = false;
                    return;
                }

                var realms = Kingdom.All.Where(IsValidPermanentRealm).ToList();
                var parents = combined.ToDictionary(record => ResolveKingdom(record.ClientKingdomId),
                    record => ResolveKingdom(record.SuzerainKingdomId));
                // On recovery, use current suzerain diplomacy, not an obsolete saved war list.
                var wars = new List<Tuple<Kingdom, Kingdom, bool>>();
                for (int i = 0; i < realms.Count; i++)
                {
                    for (int j = i + 1; j < realms.Count; j++)
                    {
                        Kingdom first = realms[i], second = realms[j];
                        bool firstClient = parents.TryGetValue(first, out Kingdom firstRoot);
                        bool secondClient = parents.TryGetValue(second, out Kingdom secondRoot);
                        if (!firstClient && !secondClient) continue;
                        firstRoot = firstRoot ?? first;
                        secondRoot = secondRoot ?? second;
                        wars.Add(Tuple.Create(first, second, firstRoot != secondRoot && firstRoot.IsAtWarWith(secondRoot)));
                    }
                }

                IsApplyingStartingClientage = true;
                RunDiplomaticSync(() =>
                {
                    foreach (var pair in parents)
                    {
                        EndThirdPartyAgreements(pair.Key, pair.Value);
                        ModIntegrationHelper.TryExpireDiplomacyNonAggressionPacts(pair.Key, pair.Value);
                    }

                    // Snapshot the complete bloc matrix before changing any stance. Client-only
                    // wars never become their suzerain's wars, even when both sides have clients.
                    foreach (var war in wars.Where(war => !war.Item3))
                        if (war.Item1.IsAtWarWith(war.Item2)) MakePeaceAction.Apply(war.Item1, war.Item2);
                    foreach (var war in wars.Where(war => war.Item3))
                        if (!war.Item1.IsAtWarWith(war.Item2)) DeclareWarAction.ApplyByDefault(war.Item1, war.Item2);
                    if (wars.Any(war => war.Item1.IsAtWarWith(war.Item2) != war.Item3))
                        throw new InvalidOperationException("A starting clientage war/peace action was blocked; client records have not been published.");
                    if (!ValidateStartingClientageGraph(_clients.Concat(_pendingStartingClientages), out string changedGraph)
                        || combined.Any(record => !IsValidStartingRealm(ResolveKingdom(record.ClientKingdomId))
                            || !IsValidStartingRealm(ResolveKingdom(record.SuzerainKingdomId))))
                        throw new InvalidOperationException("Starting clientage participants changed during diplomacy alignment: " + changedGraph);

                    var established = _pendingStartingClientages;
                    _clients.AddRange(established);
                    _pendingStartingClientages = new List<ClientKingdomRecord>();
                    _startingClientagePending = false;
                    AdvanceRuntimeRevision();
                    foreach (var record in established)
                        BellumCivileLogger.Log($"Starting clientage initialized; client={record.ClientKingdomId}; suzerain={record.SuzerainKingdomId}; voluntary={record.WasVoluntary}; liberation_cooldown_until={record.LiberationCooldownUntilDay:0.0}.");
                    BellumCivileLogger.Log($"Initialized {established.Count} configured starting clientages; no submission rewards or court agenda credit applied.");

                    // Clear the pending plan before calling external agreement code. If it fails,
                    // normal maintenance repairs agreements without ever recreating a liberated client.
                    foreach (var pair in parents)
                    {
                        EnsureProtectedAgreements(pair.Key, pair.Value);
                        ModIntegrationHelper.TryExpireDiplomacyNonAggressionPacts(pair.Key, pair.Value);
                    }
                });
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"Starting clientage initialization interrupted; pending={_startingClientagePending}; maintenance will retry: {ex}");
            }
            finally
            {
                IsApplyingStartingClientage = false;
            }
        }
    }
}
