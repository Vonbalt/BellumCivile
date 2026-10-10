using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    public sealed partial class ClientKingdomBehavior : CampaignBehaviorBase
    {
        private List<ClientKingdomRecord> _clients = new List<ClientKingdomRecord>();
        private int _diplomaticSyncDepth;
        private int _runtimeRevision;

        public static ClientKingdomBehavior Instance { get; private set; }
        public bool IsSynchronizingDiplomacy => _diplomaticSyncDepth > 0;
        public int RuntimeRevision => _runtimeRevision;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnAfterSessionLaunchedEvent.AddNonSerializedListener(this, OnAfterSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.KingdomDestroyedEvent.AddNonSerializedListener(this, OnKingdomDestroyed);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnMakePeace);
        }

        public override void SyncData(IDataStore dataStore)
        {
            Instance = this;
            dataStore.SyncData("BellumCivile_ClientKingdoms", ref _clients);
            dataStore.SyncData("BellumCivile_StartingClientagePending", ref _startingClientagePending);
            dataStore.SyncData("BellumCivile_PendingStartingClientages", ref _pendingStartingClientages);
            EnsureCollectionsInitialized();
        }

        public IReadOnlyList<ClientKingdomRecord> GetClientRecords()
        {
            EnsureCollectionsInitialized();
            return _clients.ToList();
        }

        public ClientKingdomRecord GetClientRecord(Kingdom client)
        {
            if (client == null)
                return null;
            EnsureCollectionsInitialized();
            return _clients.FirstOrDefault(record => record?.ClientKingdomId == client.StringId);
        }

        public Kingdom GetSuzerain(Kingdom client)
        {
            ClientKingdomRecord record = GetClientRecord(client);
            return ResolveKingdom(record?.SuzerainKingdomId);
        }

        public IReadOnlyList<Kingdom> GetClients(Kingdom suzerain)
        {
            if (suzerain == null)
                return new List<Kingdom>();
            EnsureCollectionsInitialized();
            return _clients
                .Where(record => record?.SuzerainKingdomId == suzerain.StringId)
                .Select(record => ResolveKingdom(record.ClientKingdomId))
                .Where(IsValidPermanentRealm)
                .ToList();
        }

        public bool IsClientKingdom(Kingdom kingdom) => GetClientRecord(kingdom) != null;

        public bool IsClientOf(Kingdom client, Kingdom suzerain)
        {
            return client != null && suzerain != null && GetClientRecord(client)?.SuzerainKingdomId == suzerain.StringId;
        }

        public bool IsProtectedClientPair(Kingdom first, Kingdom second)
        {
            return IsClientOf(first, second) || IsClientOf(second, first);
        }

        public bool IsAuxiliaryClientWar(Kingdom first, Kingdom second)
        {
            if (first == null || second == null)
                return false;

            Kingdom firstSuzerain = GetSuzerain(first);
            if (firstSuzerain != null && firstSuzerain.IsAtWarWith(second))
                return true;

            Kingdom secondSuzerain = GetSuzerain(second);
            return secondSuzerain != null && secondSuzerain.IsAtWarWith(first);
        }

        public bool TryEstablishClientKingdom(
            Kingdom client,
            Kingdom suzerain,
            bool voluntary,
            out string report)
        {
            if (!CanEstablishClientKingdom(client, suzerain, out report))
                return false;

            float today = CurrentDay;
            _clients.Add(new ClientKingdomRecord(
                client.StringId,
                suzerain.StringId,
                today,
                voluntary,
                today + CampaignTime.DaysInYear));
            AdvanceRuntimeRevision();

            RunDiplomaticSync(() =>
            {
                EnsureProtectedAgreements(client, suzerain);
                EndThirdPartyAgreements(client, suzerain);
                AlignClientDiplomacyOnEstablishment(client, suzerain);
            });
            ModIntegrationHelper.TryExpireDiplomacyNonAggressionPacts(client, suzerain);

            BellumCivileLogger.Log(
                $"Client kingdom established; client={client.StringId}; suzerain={suzerain.StringId}; voluntary={voluntary}; liberation_cooldown_until={today + CampaignTime.DaysInYear:0.0}.");
            ShowClientageNotification(client, suzerain, voluntary);
            try { CourtAgendaBehavior.Current?.OnCourtClientageEstablished(client, suzerain); }
            catch (Exception ex) { BellumCivileLogger.Log($"Court clientage receipt deferred to maintenance: {ex}"); }
            report = voluntary
                ? $"{client.Name} voluntarily entered the clientage of {suzerain.Name}"
                : $"{client.Name} was compelled to become a client of {suzerain.Name}";
            return true;
        }

        public bool TryInheritClientage(Kingdom sourceClient, Kingdom successorClient, out string report)
        {
            report = "clientage could not be inherited";
            ClientKingdomRecord sourceRecord = GetClientRecord(sourceClient);
            Kingdom suzerain = ResolveKingdom(sourceRecord?.SuzerainKingdomId);
            if (sourceRecord == null || suzerain == null)
            {
                report = "the source realm has no valid client obligation";
                return false;
            }

            if (!CanEstablishClientKingdom(successorClient, suzerain, out report))
                return false;

            _clients.Add(new ClientKingdomRecord(
                successorClient.StringId,
                suzerain.StringId,
                sourceRecord.StartedDay,
                sourceRecord.WasVoluntary,
                sourceRecord.LiberationCooldownUntilDay));
            AdvanceRuntimeRevision();

            RunDiplomaticSync(() =>
            {
                EnsureProtectedAgreements(successorClient, suzerain);
                EndThirdPartyAgreements(successorClient, suzerain);
                AlignClientDiplomacyOnEstablishment(successorClient, suzerain);
            });
            ModIntegrationHelper.TryExpireDiplomacyNonAggressionPacts(successorClient, suzerain);

            BellumCivileLogger.Log(
                $"Clientage inherited by successor realm; source={sourceClient.StringId}; successor={successorClient.StringId}; suzerain={suzerain.StringId}; voluntary={sourceRecord.WasVoluntary}; liberation_cooldown_until={sourceRecord.LiberationCooldownUntilDay:0.0}.");
            report = $"{successorClient.Name} inherited the client obligations of {sourceClient.Name}";
            return true;
        }

        // Registry-only absorption adapter. The union coordinator must transfer the
        // existing protected agreements separately, not establish clientage again.
        internal bool TryInheritRealmUnionClients(Kingdom source, Kingdom destination,
            Action beforeWrite, Action returned, out string reason)
        {
            reason = "client inheritance requires native transfer receipts";
            if (beforeWrite == null || returned == null) return false;
            if (!TryPrepareRealmUnionClients(source, destination, out var replacement, out reason)) return false;
            beforeWrite();
            _clients = replacement;
            AdvanceRuntimeRevision();
            returned();
            return true;
        }

        internal bool CanInheritRealmUnionClients(Kingdom source, Kingdom destination, out string reason) =>
            TryPrepareRealmUnionClients(source, destination, out _, out reason);

        private bool TryPrepareRealmUnionClients(Kingdom source, Kingdom destination,
            out List<ClientKingdomRecord> replacement, out string reason)
        {
            replacement = null;
            reason = "client inheritance requires distinct permanent independent realms";
            if (!IsValidPermanentRealm(source) || !IsValidPermanentRealm(destination) || source == destination
                || source.IsAtWarWith(destination)) return false;
            return RealmUnionClientAdapter.TryPrepare(_clients, source.StringId, destination.StringId,
                id => {
                    Kingdom client = ResolveKingdom(id);
                    return !IsValidPermanentRealm(client) || client.IsAtWarWith(source) || client.IsAtWarWith(destination)
                        || Kingdom.All.Any(other => other != null && !other.IsEliminated && other != client
                            && other != source && other != destination
                            && client.IsAtWarWith(other) != destination.IsAtWarWith(other));
                }, out replacement, out reason);
        }

        internal void CompleteProtectionClientage(CourtProtectionRecord offer)
        {
            Kingdom client = offer.Client, suzerain = offer.Protector;
            if (!offer.EstablishmentAttempted || !client.IsAtWarWith(offer.Threat) || !suzerain.IsAtWarWith(offer.Threat))
                throw new InvalidOperationException("Protection requires an active shared threat war.");
            var existing = GetClientRecord(client);
            if (existing != null && existing.SuzerainKingdomId != suzerain.StringId)
                throw new InvalidOperationException("The applicant already belongs to another protector.");
            if (existing == null)
            {
                if (!CanEstablishClientKingdom(client, suzerain, out string reason)) throw new InvalidOperationException(reason);
                float today = CurrentDay;
                _clients.Add(new ClientKingdomRecord(client.StringId, suzerain.StringId, today, true, today + CampaignTime.DaysInYear));
                AdvanceRuntimeRevision();
            }
            // War-first execution preserves the named war. Retrying never recreates the client record.
            RunDiplomaticSync(() =>
            {
                EnsureProtectedAgreements(client, suzerain);
                EndThirdPartyAgreements(client, suzerain);
                AlignClientDiplomacyOnEstablishment(client, suzerain);
            });
            ModIntegrationHelper.TryExpireDiplomacyNonAggressionPacts(client, suzerain);
        }

        internal bool ProtectionAlignmentComplete(CourtProtectionRecord offer)
        {
            Kingdom client = offer.Client, suzerain = offer.Protector;
            if (!IsClientOf(client, suzerain) || client.IsAtWarWith(suzerain)
                || !client.IsAtWarWith(offer.Threat) || !suzerain.IsAtWarWith(offer.Threat)) return false;
            var alliances = Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
            var trade = Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
            if (alliances?.IsAllyWithKingdom(client, suzerain) != true || trade?.HasTradeAgreement(client, suzerain, out _) != true) return false;
            foreach (var other in Kingdom.All.Where(k => IsValidPermanentRealm(k) && k != client && k != suzerain))
            {
                if (client.IsAtWarWith(other) != suzerain.IsAtWarWith(other) || alliances.IsAllyWithKingdom(client, other)
                    || trade.HasTradeAgreement(client, other, out _)) return false;
                if (!ModIntegrationHelper.TryReadDiplomacyNonAggressionPact(client, other, out bool pact) || pact) return false;
            }
            return true;
        }

        private static void ShowClientageNotification(Kingdom client, Kingdom suzerain, bool voluntary)
        {
            if (client == null || suzerain == null)
                return;

            TextObject message = voluntary
                ? new TextObject("{=BC_Treaty_ClientKingdomVoluntary}{CLIENT_REALM} has agreed to submit as a client state under the overlordship of the {SUZERAIN_REALM}.")
                : new TextObject("{=BC_Treaty_ClientKingdomForced}{CLIENT_REALM} has been forced into clientage by the {SUZERAIN_REALM}. Incapable of refusing after a disastrous war, {CLIENT_RULER} swallowed the humiliation and accepted.");
            message.SetTextVariable("CLIENT_REALM", client.Name);
            message.SetTextVariable("SUZERAIN_REALM", suzerain.Name);
            message.SetTextVariable("CLIENT_RULER", client.RulingClan?.Leader?.Name ?? client.Name);
            BellumCivileNotifications.Show(message, BellumNotificationColors.Warning,
                primaryKingdom: suzerain, secondaryKingdom: client, isMajorEvent: true);
        }

        public bool CanEstablishClientKingdom(Kingdom client, Kingdom suzerain, out string report)
        {
            report = "client status could not be established";
            EnsureCollectionsInitialized();
            if (!IsValidPermanentRealm(client) || !IsValidPermanentRealm(suzerain) || client == suzerain)
            {
                report = "both sides must be distinct permanent realms";
                return false;
            }
            if (IsClientKingdom(client))
            {
                report = "the prospective client already owes allegiance to another realm";
                return false;
            }
            if (IsClientKingdom(suzerain))
            {
                report = "a client kingdom cannot itself become a suzerain";
                return false;
            }
            if (GetClients(client).Count > 0)
            {
                report = "a realm with clients cannot enter another realm's clientage";
                return false;
            }

            report = "client status is available";
            return true;
        }

        public bool IsLawfulClientage(Kingdom client, Kingdom suzerain)
        {
            return IsLawfulSuzerainty(client, suzerain);
        }

        public bool EndClientStatus(Kingdom client, string reason)
        {
            ClientKingdomRecord record = GetClientRecord(client);
            Kingdom suzerain = ResolveKingdom(record?.SuzerainKingdomId);
            if (record == null)
                return false;

            _clients.Remove(record);
            AdvanceRuntimeRevision();
            EndProtectedAgreements(client, suzerain, reason);

            BellumCivileLogger.Log($"Client kingdom status ended; client={record.ClientKingdomId}; suzerain={record.SuzerainKingdomId}; reason={reason ?? "unknown"}.");
            return true;
        }

        public bool CanStartAlliance(Kingdom first, Kingdom second)
        {
            if (IsSynchronizingDiplomacy)
                return true;
            if (first == null || second == null)
                return true;
            if (!IsClientKingdom(first) && !IsClientKingdom(second))
                return true;
            return IsProtectedClientPair(first, second);
        }

        public bool CanEndAlliance(Kingdom first, Kingdom second)
        {
            return CanEndProtectedAgreement(first, second, "alliance");
        }

        public bool CanMakeTradeAgreement(Kingdom first, Kingdom second)
        {
            return CanStartAlliance(first, second);
        }

        public bool CanEndTradeAgreement(Kingdom first, Kingdom second)
        {
            return CanEndProtectedAgreement(first, second, "trade agreement");
        }

        private bool CanEndProtectedAgreement(Kingdom first, Kingdom second, string agreementType)
        {
            if (IsSynchronizingDiplomacy || !IsProtectedClientPair(first, second))
                return true;
            if (IsValidPermanentRealm(first) && IsValidPermanentRealm(second))
                return false;

            BellumCivileLogger.Log(
                $"Allowed protected client {agreementType} cleanup for an invalid realm; " +
                $"first={DescribeAgreementRealm(first)}; second={DescribeAgreementRealm(second)}.");
            return true;
        }

        public bool CanDeclareWar(Kingdom initiator, Kingdom target, out string reason)
        {
            reason = string.Empty;
            if (IsSynchronizingDiplomacy || initiator == null || target == null)
                return true;

            if (IsProtectedClientPair(initiator, target))
            {
                if (IsClientOf(initiator, target))
                {
                    ClientLibertyAssessment assessment = BuildLibertyAssessment(initiator);
                    if (assessment?.CanAttemptLiberation == true)
                    {
                        return true;
                    }

                    reason = assessment?.BlockReason ?? "the client realm is not ready to fight for liberation";
                    return false;
                }

                reason = "a suzerain cannot declare war on its own client kingdom";
                return false;
            }

            if (IsClientKingdom(initiator))
            {
                reason = "a client kingdom cannot conduct an independent war";
                return false;
            }

            return true;
        }

        internal bool ConfirmLiberationWar(Kingdom client, Kingdom suzerain)
        {
            // Eligibility is read-only. Only an actual hostile stance releases clientage.
            if (IsSynchronizingDiplomacy || client == null || suzerain == null
                || !IsClientOf(client, suzerain) || !client.IsAtWarWith(suzerain)) return false;
            var record = GetClientRecord(client);
            // Keep the record available for reconciliation if agreement cleanup throws.
            EndProtectedAgreements(client, suzerain, "liberation war confirmed");
            if (!_clients.Remove(record)) return false;
            AdvanceRuntimeRevision();
            BellumCivileLogger.Log($"Client liberation committed after verified hostility; client={client.StringId}; suzerain={suzerain.StringId}.");
            return true;
        }

        public bool CanMakePeace(Kingdom first, Kingdom second, out string reason)
        {
            reason = string.Empty;
            if (IsSynchronizingDiplomacy || first == null || second == null)
                return true;

            Kingdom firstSuzerain = GetSuzerain(first);
            if (firstSuzerain != null && firstSuzerain.IsAtWarWith(second))
            {
                reason = "only the suzerain may conclude peace for its client kingdom";
                return false;
            }

            Kingdom secondSuzerain = GetSuzerain(second);
            if (secondSuzerain != null && secondSuzerain.IsAtWarWith(first))
            {
                reason = "only the suzerain may conclude peace for its client kingdom";
                return false;
            }

            return true;
        }

        public bool TryCanProposeLiberation(Clan proposer, out Kingdom suzerain, out string report)
        {
            suzerain = GetSuzerain(proposer?.Kingdom);
            report = "the clan does not belong to a client kingdom";
            if (proposer?.Kingdom == null || suzerain == null)
                return false;

            ClientLibertyAssessment assessment = BuildLibertyAssessment(proposer.Kingdom);
            ClientClanLibertyAssessment clanAssessment = assessment?.Clans.FirstOrDefault(entry => entry.Clan == proposer);
            if (assessment == null)
                return false;
            if (!assessment.CanAttemptLiberation)
            {
                report = assessment.BlockReason;
                return false;
            }
            if ((clanAssessment?.LibertyDesire ?? 0f) < C.ClientClanLiberationDesireThreshold)
            {
                report = $"personal liberty desire is too low ({clanAssessment?.LibertyDesire ?? 0f:0.0})";
                return false;
            }

            float warWill = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>()?.GetWarWill(proposer) ?? 0f;
            float desire = clanAssessment.LibertyDesire;
            float resolve = ClientLiberationRules.ResolveBonus(desire);
            float willingness = ClientLiberationRules.EffectiveWarWill(warWill, desire);
            if (willingness < BellumCivileOptions.WarWillDeclareThreshold)
            {
                report = new TextObject("{=BC_ClientLiberation_WillingnessLow}Insufficient willingness for liberation ({CURRENT}/{REQUIRED}; War Will {WILL}, resolve +{RESOLVE}).")
                    .SetTextVariable("CURRENT", willingness.ToString("0.#"))
                    .SetTextVariable("REQUIRED", BellumCivileOptions.WarWillDeclareThreshold.ToString("0.#"))
                    .SetTextVariable("WILL", warWill.ToString("0.#"))
                    .SetTextVariable("RESOLVE", resolve.ToString("0.#"))
                    .ToString();
                return false;
            }

            report = $"liberty desire={assessment.RealmLibertyDesire:0.0}; readiness={assessment.LiberationReadiness:0.0}%; war will={warWill:0.0}; resolve={resolve:0.0}; willingness={willingness:0.0}";
            return true;
        }

        internal float GetLiberationWarWill(Clan clan, Kingdom target, float warWill)
        {
            ClientKingdomRecord record = GetClientRecord(clan?.Kingdom);
            if (target == null || record == null || record.SuzerainKingdomId != target.StringId
                || !IsEligiblePoliticalClan(clan))
                return warWill;

            // Council votes need this clan's desire, without recalculating the whole opposing bloc.
            float courtBonus = CourtAgendaBehavior.Current?.LiberationDesireBonus(clan.Kingdom, record) ?? 0f;
            ClientClanLibertyAssessment assessment = CalculateClanLiberty(clan, target, record,
                IsLawfulSuzerainty(clan.Kingdom, target), courtBonus);
            return ClientLiberationRules.EffectiveWarWill(warWill, assessment.LibertyDesire);
        }

        public ClientLibertyAssessment BuildLibertyAssessment(Kingdom client)
            => BuildLibertyAssessmentCore(client, null);

        internal ClientLibertyAssessment BuildLibertyAssessmentWithBonus(Kingdom client, float bonus)
            => BuildLibertyAssessmentCore(client, bonus);

        private ClientLibertyAssessment BuildLibertyAssessmentCore(Kingdom client, float? hypotheticalBonus)
        {
            ClientKingdomRecord record = GetClientRecord(client);
            Kingdom suzerain = ResolveKingdom(record?.SuzerainKingdomId);
            if (record == null || !IsValidPermanentRealm(client) || !IsValidPermanentRealm(suzerain))
                return null;

            bool lawful = IsLawfulSuzerainty(client, suzerain);
            float courtBonus = hypotheticalBonus ?? (CourtAgendaBehavior.Current?.LiberationDesireBonus(client, record) ?? 0f);
            List<ClientClanLibertyAssessment> clans = client.Clans
                .Where(IsEligiblePoliticalClan)
                .Select(clan =>
                {
                    var clanAssessment = CalculateClanLiberty(clan, suzerain, record, lawful, courtBonus);
                    clanAssessment.Power = RebellionPowerHelper.CalculateClanPower(clan);
                    return clanAssessment;
                })
                .ToList();

            float totalWeight = clans.Sum(entry => Math.Max(1f, entry.Power));
            float realmDesire = totalWeight > 0f
                ? clans.Sum(entry => entry.LibertyDesire * Math.Max(1f, entry.Power)) / totalWeight
                : 0f;

            float effectiveClientPower = clans.Sum(entry =>
            {
                float multiplier = ClientLiberationRules.EffectivePowerMultiplier(entry.LibertyDesire);
                return entry.Power * multiplier;
            });

            float suzerainPower = RebellionPowerHelper.CalculateFactionPower(suzerain.Clans.Where(IsEligiblePoliticalClan));
            float otherClientsPower = 0f;
            float alliesPower = 0f;
            IAllianceCampaignBehavior alliances = Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
            var otherClients = new HashSet<Kingdom>(GetClients(suzerain).Where(kingdom => kingdom != client));
            foreach (Kingdom supporter in Kingdom.All.Where(kingdom => IsValidPermanentRealm(kingdom)
                && kingdom != client
                && kingdom != suzerain).Distinct())
            {
                bool isClient = otherClients.Contains(supporter);
                bool isAlly = alliances?.IsAllyWithKingdom(suzerain, supporter) == true;
                if (!isClient && !isAlly) continue;
                float contribution = ClientLiberationRules.BlocContribution(
                    RebellionPowerHelper.CalculateFactionPower(supporter.Clans.Where(IsEligiblePoliticalClan)),
                    isAlly, isClient, C.ClientSuzerainAllyPowerContribution, C.ClientOtherClientPowerContribution);
                if (isClient) otherClientsPower += contribution;
                else alliesPower += contribution;
            }

            float suzerainBlocPower = suzerainPower + otherClientsPower + alliesPower;
            float requiredRatio = RebellionPowerHelper.CalculateRebellionPowerThreshold(client.RulingClan?.Leader);
            float readiness = ClientLiberationRules.Readiness(effectiveClientPower, suzerainBlocPower, requiredRatio);
            float cooldown = Math.Max(0f, record.LiberationCooldownUntilDay - CurrentDay);

            ClientLibertyAssessment assessment = new ClientLibertyAssessment
            {
                ClientKingdom = client,
                SuzerainKingdom = suzerain,
                IsLawfulSuzerainty = lawful,
                WasVoluntary = record.WasVoluntary,
                RealmLibertyDesire = realmDesire,
                EffectiveClientPower = effectiveClientPower,
                SuzerainBlocPower = suzerainBlocPower,
                SuzerainPower = suzerainPower,
                OtherClientsPower = otherClientsPower,
                AlliesPower = alliesPower,
                RequiredPowerRatio = requiredRatio,
                LiberationReadiness = readiness,
                CooldownRemainingDays = cooldown,
                Clans = clans
            };

            if (client.IsAtWarWith(suzerain))
                assessment.BlockReason = "the liberation war is already active";
            else if (cooldown > 0f)
                assessment.BlockReason = $"the settlement remains binding for {cooldown:0} more days";
            else if (realmDesire < C.ClientRealmLiberationDesireThreshold)
                assessment.BlockReason = $"realm liberty desire is too low ({realmDesire:0.0})";
            else if (readiness < 100f)
                assessment.BlockReason = $"liberation readiness is too low ({readiness:0.0}%)";
            else
                assessment.CanAttemptLiberation = true;

            return assessment;
        }

        public string BuildDebugReport(Kingdom client)
        {
            ClientLibertyAssessment assessment = BuildLibertyAssessment(client);
            if (assessment == null)
                return client == null ? "Client kingdom not found." : $"{client.Name} is not a client kingdom.";

            List<string> lines = new List<string>
            {
                $"Client kingdom: {assessment.ClientKingdom.Name} -> {assessment.SuzerainKingdom.Name}",
                $"voluntary={assessment.WasVoluntary}; lawful={assessment.IsLawfulSuzerainty}; cooldown_days={assessment.CooldownRemainingDays:0.0}",
                $"realm_liberty_desire={assessment.RealmLibertyDesire:0.0}; effective_client_power={assessment.EffectiveClientPower:0}; suzerain_bloc_power={assessment.SuzerainBlocPower:0}; required_ratio={assessment.RequiredPowerRatio:0.00}; readiness={assessment.LiberationReadiness:0.0}%",
                $"can_attempt={assessment.CanAttemptLiberation}; blocker={assessment.BlockReason ?? "none"}"
            };
            foreach (ClientClanLibertyAssessment clan in assessment.Clans.OrderByDescending(entry => entry.LibertyDesire))
            {
                float warWill = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>()?.GetWarWill(clan.Clan) ?? 0f;
                string reasons = string.Join(", ", clan.Reasons.Where(reason => Math.Abs(reason.Amount) > 0.01f)
                    .Select(reason => $"{reason.Label} {reason.Amount:+0;-0;0}"));
                lines.Add($"- {clan.Clan.Name}: desire={clan.LibertyDesire:0.0}; war_will={warWill:0.0}; "
                    + $"resolve={ClientLiberationRules.ResolveBonus(clan.LibertyDesire):0.0}; "
                    + $"willingness={ClientLiberationRules.EffectiveWarWill(warWill, clan.LibertyDesire):0.0}; {reasons}");
            }
            return string.Join(Environment.NewLine, lines);
        }

        internal void RunDiplomaticSync(Action action)
        {
            if (action == null)
                return;
            _diplomaticSyncDepth++;
            try
            {
                action();
            }
            finally
            {
                _diplomaticSyncDepth--;
            }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            if (_startingClientagePending) return;
            ReconcileClientKingdoms();
            ReconcileDiplomacyNonAggressionPacts();
        }

        private void OnDailyTick()
        {
            ApplyStartingClientages();
            if (_startingClientagePending) return;
            ReconcileClientKingdoms();
            AdvanceRuntimeRevision();
        }

        private void OnKingdomDestroyed(Kingdom destroyedKingdom)
        {
            if (destroyedKingdom == null)
                return;

            EnsureCollectionsInitialized();
            foreach (ClientKingdomRecord record in _clients
                .Where(entry => entry != null
                    && (entry.ClientKingdomId == destroyedKingdom.StringId
                        || entry.SuzerainKingdomId == destroyedKingdom.StringId))
                .ToList())
            {
                Kingdom client = ResolveKingdom(record.ClientKingdomId);
                Kingdom suzerain = ResolveKingdom(record.SuzerainKingdomId);
                _clients.Remove(record);
                AdvanceRuntimeRevision();
                EndProtectedAgreements(client, suzerain, "clientage participant kingdom destroyed");

                BellumCivileLogger.Log(
                    $"Removed client kingdom record during kingdom destruction; destroyed={destroyedKingdom.StringId}; " +
                    $"client={record.ClientKingdomId}; suzerain={record.SuzerainKingdomId}.");

                if (record.SuzerainKingdomId == destroyedKingdom.StringId && IsValidPermanentRealm(client))
                    ShowIndependenceAfterSuzerainCollapse(client, destroyedKingdom, destroyedKingdom.Name);
            }
        }

        private void OnWarDeclared(IFaction firstFaction, IFaction secondFaction, DeclareWarAction.DeclareWarDetail detail)
        {
            Kingdom first = firstFaction as Kingdom;
            Kingdom second = secondFaction as Kingdom;
            if (first == null || second == null || IsSynchronizingDiplomacy || !first.IsAtWarWith(second))
                return;

            // Fallback for declarations dispatched by another integration. Resolve the pair
            // before bloc propagation, otherwise a client can call its own suzerain against itself.
            try { CourtAgendaBehavior.Current?.RecordCourtLiberationWar(first, second); }
            catch (Exception ex) { BellumCivileLogger.Log("Court liberation event receipt failed: " + ex); }
            ConfirmLiberationWar(first, second);
            ConfirmLiberationWar(second, first);

            RunDiplomaticSync(() =>
            {
                Kingdom firstSuzerain = GetSuzerain(first);
                Kingdom secondSuzerain = GetSuzerain(second);

                if (firstSuzerain != null && !firstSuzerain.IsAtWarWith(second))
                    DeclareWarAction.ApplyByDefault(firstSuzerain, second);
                if (secondSuzerain != null && !secondSuzerain.IsAtWarWith(first))
                    DeclareWarAction.ApplyByDefault(first, secondSuzerain);

                Kingdom firstBlocLeader = firstSuzerain ?? first;
                Kingdom secondBlocLeader = secondSuzerain ?? second;
                CallClientsToWar(firstBlocLeader, secondBlocLeader);
                CallClientsToWar(secondBlocLeader, firstBlocLeader);
            });
        }

        private void OnMakePeace(IFaction firstFaction, IFaction secondFaction, MakePeaceAction.MakePeaceDetail detail)
        {
            Kingdom first = firstFaction as Kingdom;
            Kingdom second = secondFaction as Kingdom;
            if (first == null || second == null || IsSynchronizingDiplomacy)
                return;

            RunDiplomaticSync(() =>
            {
                var firstSide = GetClients(first).Concat(new[] { first }).Distinct().ToList();
                var secondSide = GetClients(second).Concat(new[] { second }).Distinct().ToList();
                foreach (var ally in firstSide)
                    foreach (var enemy in secondSide)
                        if (ally != enemy && ally.IsAtWarWith(enemy)) MakePeaceAction.Apply(ally, enemy);
            });
        }

        private void ReconcileClientKingdoms()
        {
            EnsureCollectionsInitialized();
            foreach (ClientKingdomRecord record in _clients.ToList())
            {
                if (CrownAccessionBehavior.Instance?.IsRealmUnionClientProtected(record.ClientKingdomId) == true) continue;
                Kingdom client = ResolveKingdom(record.ClientKingdomId);
                Kingdom suzerain = ResolveKingdom(record.SuzerainKingdomId);
                if (!IsValidPermanentRealm(client) || client == suzerain)
                {
                    _clients.Remove(record);
                    AdvanceRuntimeRevision();
                    EndProtectedAgreements(client, suzerain, "invalid client kingdom reconciliation");
                    BellumCivileLogger.Log(
                        $"Removed invalid client kingdom record; client={record.ClientKingdomId}; " +
                        $"suzerain={record.SuzerainKingdomId}; client_state={DescribeAgreementRealm(client)}; " +
                        $"suzerain_state={DescribeAgreementRealm(suzerain)}.");
                    continue;
                }
                if (!IsValidPermanentRealm(suzerain) || IsClientKingdom(suzerain))
                {
                    TextObject formerSuzerainName = suzerain?.Name
                        ?? new TextObject("{=BC_ClientKingdom_FormerOverlord}its former overlord");
                    if (EndClientStatus(client, "suzerain lost political sovereignty"))
                        ShowIndependenceAfterSuzerainCollapse(client, suzerain, formerSuzerainName);
                    continue;
                }
                if (client.IsAtWarWith(suzerain))
                {
                    ConfirmLiberationWar(client, suzerain);
                    continue;
                }

                RunDiplomaticSync(() =>
                {
                    EnsureProtectedAgreements(client, suzerain);
                    EndThirdPartyAgreements(client, suzerain);
                    foreach (Kingdom other in Kingdom.All.Where(kingdom => IsValidPermanentRealm(kingdom)
                        && kingdom != client
                        && kingdom != suzerain))
                    {
                        if (suzerain.IsAtWarWith(other) && !client.IsAtWarWith(other))
                            DeclareWarAction.ApplyByDefault(client, other);
                        else if (client.IsAtWarWith(other) && !suzerain.IsAtWarWith(other))
                            DeclareWarAction.ApplyByDefault(suzerain, other);
                    }
                });
            }
        }

        private void EndProtectedAgreements(Kingdom client, Kingdom suzerain, string reason)
        {
            if (client == null || suzerain == null || client == suzerain)
                return;

            bool endedAlliance = false;
            bool endedTradeAgreement = false;
            RunDiplomaticSync(() =>
            {
                IAllianceCampaignBehavior alliances = Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
                if (alliances?.IsAllyWithKingdom(client, suzerain) == true)
                {
                    alliances.EndAlliance(client, suzerain);
                    endedAlliance = true;
                }

                ITradeAgreementsCampaignBehavior trade = Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
                if (trade?.HasTradeAgreement(client, suzerain, out _) == true)
                {
                    trade.EndTradeAgreement(client, suzerain);
                    endedTradeAgreement = true;
                }
            });

            if (endedAlliance || endedTradeAgreement)
            {
                BellumCivileLogger.Log(
                    $"Cleaned client protected agreements; client={client.StringId}; suzerain={suzerain.StringId}; " +
                    $"alliance={endedAlliance}; trade={endedTradeAgreement}; reason={reason ?? "unknown"}.");
            }
        }

        private static string DescribeAgreementRealm(Kingdom kingdom)
        {
            if (kingdom == null)
                return "null";

            return $"{kingdom.StringId}(eliminated={kingdom.IsEliminated}," +
                $"ruler={kingdom.RulingClan?.StringId ?? "null"}," +
                $"temporary={BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom)})";
        }

        private static void ShowIndependenceAfterSuzerainCollapse(
            Kingdom client,
            Kingdom formerSuzerain,
            TextObject formerSuzerainName)
        {
            if (client == null)
                return;

            TextObject message = new TextObject("{=BC_ClientKingdom_SuzerainCollapsed}With the recent collapse of {SUZERAIN_REALM}, {CLIENT_REALM} has regained its political independence.");
            message.SetTextVariable("SUZERAIN_REALM", formerSuzerainName);
            message.SetTextVariable("CLIENT_REALM", client.Name);
            BellumCivileNotifications.Show(
                message,
                BellumNotificationColors.Success,
                primaryKingdom: client,
                secondaryKingdom: formerSuzerain,
                isMajorEvent: true);
        }

        private void EnsureProtectedAgreements(Kingdom client, Kingdom suzerain)
        {
            IAllianceCampaignBehavior alliances = Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
            if (alliances?.IsAllyWithKingdom(client, suzerain) != true)
                alliances?.StartAlliance(suzerain, client);

            ITradeAgreementsCampaignBehavior trade = Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
            if (trade?.HasTradeAgreement(client, suzerain, out _) != true)
            {
                // MakeTradeAgreement expects a duration and adds it to CampaignTime.Now.
                // CampaignTime.Never is long.MaxValue, so using it here overflows the
                // resulting end time and makes the agreement expire immediately.
                trade?.MakeTradeAgreement(
                    suzerain,
                    client,
                    CampaignTime.Years(C.ClientProtectedTradeAgreementDurationYears));

                if (trade?.HasTradeAgreement(client, suzerain, out _) != true)
                {
                    BellumCivileLogger.Log(
                        $"Failed to establish protected client trade agreement; client={client?.StringId}; suzerain={suzerain?.StringId}.");
                }
            }
        }

        private void EndThirdPartyAgreements(Kingdom client, Kingdom suzerain)
        {
            IAllianceCampaignBehavior alliances = Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
            ITradeAgreementsCampaignBehavior trade = Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
            foreach (Kingdom other in Kingdom.All.Where(kingdom => IsValidPermanentRealm(kingdom)
                && kingdom != client
                && kingdom != suzerain))
            {
                if (alliances?.IsAllyWithKingdom(client, other) == true)
                    alliances.EndAlliance(client, other);
                if (trade?.HasTradeAgreement(client, other, out _) == true)
                    trade.EndTradeAgreement(client, other);
            }
        }

        private void ReconcileDiplomacyNonAggressionPacts()
        {
            if (!ModIntegrationHelper.IsDiplomacyLoaded)
                return;

            foreach (ClientKingdomRecord record in GetClientRecords())
            {
                Kingdom client = ResolveKingdom(record?.ClientKingdomId);
                Kingdom suzerain = ResolveKingdom(record?.SuzerainKingdomId);
                if (client != null && suzerain != null)
                    ModIntegrationHelper.TryExpireDiplomacyNonAggressionPacts(client, suzerain);
            }
        }

        private void AlignClientDiplomacyOnEstablishment(Kingdom client, Kingdom suzerain)
        {
            foreach (Kingdom other in Kingdom.All.Where(kingdom => IsValidPermanentRealm(kingdom)
                && kingdom != client
                && kingdom != suzerain))
            {
                if (suzerain.IsAtWarWith(other) && !client.IsAtWarWith(other))
                    DeclareWarAction.ApplyByDefault(client, other);
                else if (!suzerain.IsAtWarWith(other) && client.IsAtWarWith(other))
                    MakePeaceAction.Apply(client, other);
            }
        }

        private void CallClientsToWar(Kingdom suzerain, Kingdom target)
        {
            if (suzerain == null || target == null || suzerain == target)
                return;
            foreach (Kingdom client in GetClients(suzerain))
            {
                if (client != target && !client.IsAtWarWith(target))
                    DeclareWarAction.ApplyByDefault(client, target);
            }
        }

        private ClientClanLibertyAssessment CalculateClanLiberty(
            Clan clan,
            Kingdom suzerain,
            ClientKingdomRecord record,
            bool lawful, float courtBonus)
        {
            List<ClientLibertyReason> reasons = new List<ClientLibertyReason>();
            float desire = 0f;
            AddReason(reasons, ref desire, C.ClientLibertyBase, "client condition");
            AddReason(reasons, ref desire, record.WasVoluntary ? 0f : C.ClientForcedSubmissionLiberty, "forced submission");
            AddReason(reasons, ref desire, lawful ? C.ClientLawfulSuzeraintyLiberty : C.ClientUnlawfulSuzeraintyLiberty,
                lawful ? "rightful suzerainty" : "unlawful suzerainty");

            bool sameCulture = clan.Culture != null && clan.Culture == suzerain.Culture;
            AddReason(reasons, ref desire, sameCulture ? C.ClientSameCultureLiberty : C.ClientDifferentCultureLiberty,
                sameCulture ? "shared culture" : "foreign culture");

            Hero suzerainRuler = suzerain.RulingClan?.Leader;
            int relation = clan.Leader?.GetRelation(suzerainRuler) ?? 0;
            float relationPressure = Math.Max(-C.ClientSuzerainRelationLibertyCap,
                Math.Min(C.ClientSuzerainRelationLibertyCap, -relation * C.ClientSuzerainRelationLibertyScale));
            AddReason(reasons, ref desire, relationPressure, "relations with suzerain");

            if (MarriageAllianceHelper.HasMarriageAlliance(clan, suzerain.RulingClan))
                AddReason(reasons, ref desire, C.ClientSuzerainMarriageLiberty, "marriage tie to suzerain");

            FactionObject ideology = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetIdeologicalFaction(clan);
            if (ideology != null)
            {
                switch (ideology.Type)
                {
                    case FactionType.Glory:
                        AddReason(reasons, ref desire, C.ClientMilitaristLiberty, "militarist independence");
                        break;
                    case FactionType.Liberty:
                        if (!sameCulture)
                            AddReason(reasons, ref desire, C.ClientPopulistForeignLiberty, "populist self-rule");
                        break;
                    case FactionType.Nobility:
                        if (!lawful)
                            AddReason(reasons, ref desire, C.ClientAristocratUnlawfulLiberty, "aristocratic legality");
                        break;
                }
            }

            Hero leader = clan.Leader;
            if (leader != null)
            {
                AddReason(reasons, ref desire, leader.GetTraitLevel(DefaultTraits.Valor) * C.ClientValorLibertyPerLevel, "valor");
                AddReason(reasons, ref desire, leader.GetTraitLevel(DefaultTraits.Mercy) * C.ClientMercyLibertyPerLevel, "mercy");
                int honor = leader.GetTraitLevel(DefaultTraits.Honor);
                if (honor != 0)
                    AddReason(reasons, ref desire, honor * (lawful || record.WasVoluntary
                        ? C.ClientHonorLawfulLibertyPerLevel
                        : C.ClientHonorUnlawfulLibertyPerLevel), "honor");
            }

            AddReason(reasons, ref desire, courtBonus, new TextObject("{=BC_CourtLiberationDesire}Crown's liberation preparations").ToString());
            return new ClientClanLibertyAssessment(clan, Math.Max(0f, Math.Min(100f, desire)), reasons);
        }

        private bool IsLawfulSuzerainty(Kingdom client, Kingdom suzerain)
        {
            FeudalTitleBehavior titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalTitleRecord clientTitle = titles?.GetKingdomPoliticalTitle(client);
            FeudalTitleRecord suzerainTitle = titles?.GetKingdomPoliticalTitle(suzerain);
            if (clientTitle == null || suzerainTitle == null)
                return false;

            HashSet<string> visited = new HashSet<string>();
            FeudalTitleRecord current = clientTitle;
            while (current != null && visited.Add(current.TitleId))
            {
                if (current.TitleId == suzerainTitle.TitleId)
                    return true;
                current = string.IsNullOrWhiteSpace(current.ParentTitleId) ? null : titles.GetTitle(current.ParentTitleId);
            }
            return false;
        }

        private static void AddReason(List<ClientLibertyReason> reasons, ref float total, float amount, string label)
        {
            if (Math.Abs(amount) < 0.01f)
                return;
            total += amount;
            reasons.Add(new ClientLibertyReason(label, amount));
        }

        private static bool IsEligiblePoliticalClan(Clan clan)
        {
            return clan != null
                && !clan.IsEliminated
                && clan.Leader != null
                && !clan.IsUnderMercenaryService;
        }

        private static bool IsValidPermanentRealm(Kingdom kingdom)
        {
            return kingdom != null
                && !kingdom.IsEliminated
                && kingdom.RulingClan?.Leader != null
                && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(kingdom);
        }

        private static Kingdom ResolveKingdom(string kingdomId)
        {
            return string.IsNullOrWhiteSpace(kingdomId)
                ? null
                : Kingdom.All.FirstOrDefault(kingdom => kingdom?.StringId == kingdomId);
        }

        private void EnsureCollectionsInitialized()
        {
            if (_clients == null)
                _clients = new List<ClientKingdomRecord>();
            if (_clients.RemoveAll(record => record == null || string.IsNullOrWhiteSpace(record.ClientKingdomId)) > 0)
                AdvanceRuntimeRevision();
        }

        private void AdvanceRuntimeRevision()
        {
            unchecked
            {
                _runtimeRevision++;
            }
        }

        private static float CurrentDay => (float)CampaignTime.Now.ToDays;
    }
}
