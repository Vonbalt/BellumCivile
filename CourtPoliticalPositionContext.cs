using System;
using System.Collections.Generic;
using System.Diagnostics;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile
{
    // Short-lived snapshot shared by a single assignment or membership-review batch.
    internal sealed class CourtPoliticalPositionContext : IDisposable
    {
        private sealed class House
        {
            internal Clan Clan;
            internal float Strength;
            internal readonly HashSet<string> Subordinates = new HashSet<string>();
            internal readonly HashSet<string> AuthorityBaronies = new HashSet<string>();
        }

        private readonly Kingdom _realm;
        private readonly FeudalTitleBehavior _titles;
        private readonly CourtPoliticalPositionBehavior _history;
        private readonly Dictionary<string, House> _houses = new Dictionary<string, House>();
        private readonly Dictionary<string, HashSet<string>> _legalFootprints = new Dictionary<string, HashSet<string>>();
        private readonly Dictionary<Kingdom, Kingdom> _permanentRealms = new Dictionary<Kingdom, Kingdom>();
        private readonly HashSet<string> _officeHolders = new HashSet<string>();
        private readonly bool _councilKnown;
        private readonly bool _affiliationOnly;
        private readonly float _meanStrength;
        private double _milliseconds;
        private int _neighborReads;
        private bool _disposed;
        private bool _frontierUnavailable;
        private CourtSocialTieContext _socialTies;

        internal CourtSocialTieScore GetSocialScore(Clan clan, FactionObject faction)
        {
            long started = Stopwatch.GetTimestamp();
            _socialTies = _socialTies ?? new CourtSocialTieContext(_realm, _titles);
            int previousReads = _socialTies.RelationReads;
            CourtSocialTieScore score = _socialTies.GetScore(clan, faction);
            _history?.RecordSocialEvaluation(score, ElapsedMilliseconds(started), _socialTies.RelationReads - previousReads);
            return score;
        }

        internal CourtPoliticalPositionContext(Kingdom realm, bool affiliationOnly = false)
        {
            long started = Stopwatch.GetTimestamp();
            _realm = realm;
            _titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            _history = Campaign.Current?.GetCampaignBehavior<CourtPoliticalPositionBehavior>();
            _affiliationOnly = affiliationOnly;
            if (affiliationOnly) return;
            foreach (Clan clan in realm.Clans)
            {
                if (!CourtPoliticalPositionBehavior.IsNoble(clan) || clan.Leader == null || clan.Leader.IsDead)
                    continue;
                float strength = RebellionPowerHelper.GetClanMilitaryStrength(clan);
                if (float.IsNaN(strength) || float.IsInfinity(strength) || strength < 0f) strength = 0f;
                _houses[clan.StringId] = new House { Clan = clan, Strength = strength };
                _meanStrength += strength;
            }
            _meanStrength /= Math.Max(1, _houses.Count);

            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            _councilKnown = council != null && council.TryGetExistingOfficeHolders(realm, _officeHolders);
            if (_titles != null)
            {
                foreach (House house in _houses.Values)
                {
                    foreach (FeudalTitleRecord title in _titles.GetTitlesHeldByClan(house.Clan, deJure: false))
                        CreditAuthority(house, title, null);
                    foreach (Town fief in house.Clan.Fiefs)
                        if (_titles.TryGetBarony(fief.Settlement, out FeudalTitleRecord barony))
                            CreditAuthority(house, barony, barony.TitleId);
                }
            }
            _milliseconds = ElapsedMilliseconds(started);
        }

        private void CreditAuthority(House owner, FeudalTitleRecord title, string baronyId)
        {
            if (baronyId != null) owner.AuthorityBaronies.Add(baronyId);
            var visited = new HashSet<string>();
            FeudalTitleRecord current = title;
            while (current != null && current.IsActive && visited.Count < 32 && visited.Add(current.TitleId))
            {
                if (!string.IsNullOrEmpty(current.DeFactoHolderClanId))
                {
                    if (!_houses.TryGetValue(current.DeFactoHolderClanId, out House liege))
                        break;
                    if (liege != owner) liege.Subordinates.Add(owner.Clan.StringId);
                    if (baronyId != null) liege.AuthorityBaronies.Add(baronyId);
                }
                current = _titles.GetParentTitle(current, FeudalHierarchyMode.DeFacto);
            }
        }

        private HashSet<string> GetLegalFootprint(FeudalTitleRecord title, HashSet<string> visiting)
        {
            if (_legalFootprints.TryGetValue(title.TitleId, out HashSet<string> cached))
                return cached;
            var footprint = new HashSet<string>();
            if (!title.IsActive || visiting.Count >= 32 || !visiting.Add(title.TitleId))
                return footprint;
            if (title.TitleType == FeudalTitleType.Barony)
                footprint.Add(title.TitleId);
            else
                foreach (FeudalTitleRecord child in _titles.GetChildTitles(title, FeudalHierarchyMode.DeJure))
                    footprint.UnionWith(GetLegalFootprint(child, visiting));
            visiting.Remove(title.TitleId);
            _legalFootprints[title.TitleId] = footprint;
            return footprint;
        }

        internal CourtPoliticalPositionScore GetScore(Clan clan)
        {
            if (clan == null || !_houses.TryGetValue(clan.StringId, out House house))
                return default;
            long started = Stopwatch.GetTimestamp();
            int lawful = 0, frontier = 0, missingLegal = 0;
            var legalBaronies = new HashSet<string>();
            if (_titles != null)
            {
                foreach (FeudalTitleRecord title in _titles.GetTitlesHeldByClan(clan, deJure: true))
                    legalBaronies.UnionWith(GetLegalFootprint(title, new HashSet<string>()));
                foreach (string id in legalBaronies)
                    if (!house.AuthorityBaronies.Contains(id)) missingLegal++;
            }
            foreach (Town fief in clan.Fiefs)
            {
                if (_titles != null && _titles.TryGetBarony(fief.Settlement, out FeudalTitleRecord barony)
                    && barony.DeJureHolderClanId == clan.StringId)
                    lawful++;
                if (IsFrontier(fief)) frontier++;
            }
            var score = CourtPoliticalPositionScore.Calculate(clan.Fiefs.Count, lawful,
                _history?.GetStandingYears(clan, _realm) ?? 0f, house.Subordinates.Count,
                legalBaronies.Count, missingLegal, house.Strength, _meanStrength,
                frontier, clan.Tier, clan == _realm.RulingClan, _councilKnown, _officeHolders.Contains(clan.StringId));
            _milliseconds += ElapsedMilliseconds(started);
            _history?.RecordScore(score);
            return score;
        }

        private bool IsFrontier(Town fief)
        {
            if (_frontierUnavailable) return false;
            var model = Campaign.Current?.Models?.MapDistanceModel;
            if (model == null) return false;
            try
            {
                _neighborReads++;
                // Vanilla and NavalDLC expose cached land neighbors; never calculate paths here.
                var neighbors = model.GetNeighborsOfFortification(fief, MobileParty.NavigationType.Default);
                if (neighbors == null) return false;
                foreach (Settlement neighbor in neighbors)
                {
                    Kingdom kingdom = neighbor?.OwnerClan?.Kingdom;
                    if (kingdom == null) continue;
                    if (!_permanentRealms.TryGetValue(kingdom, out Kingdom permanent))
                    {
                        permanent = CourtPoliticalPositionBehavior.ResolveRealm(kingdom);
                        _permanentRealms[kingdom] = permanent;
                    }
                    if (permanent != null && !permanent.IsEliminated && permanent != _realm)
                        return true;
                }
            }
            catch (Exception exception)
            {
                _frontierUnavailable = true;
                _history?.ReportNeighborFailure(exception);
            }
            return false;
        }

        private static double ElapsedMilliseconds(long started) =>
            (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _history?.RecordBatch(_milliseconds, _neighborReads, _councilKnown, _affiliationOnly);
        }
    }
}
