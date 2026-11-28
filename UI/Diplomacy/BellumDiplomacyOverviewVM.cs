using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using BellumCivile.UI.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.Diplomacy
{
    internal sealed class BellumDiplomacyOverviewVM : ViewModel
    {
        [DataSourceProperty] public MBBindingList<DiplomacyOverviewSectionVM> Sections { get; }
            = new MBBindingList<DiplomacyOverviewSectionVM>();

        internal void Refresh(Kingdom first, Kingdom second)
        {
            Clear();
            if (first == null || second == null) return;
            var wars = Add("{=BC_Overview_Wars}Wars");
            var allies = Add("{=BC_Overview_Alliances}Alliances");
            var pacts = Add("{=BC_Overview_Pacts}Non-Aggression Pacts");
            var defensive = !BellumCivileOptions.DefensivePactsActive
                || Campaign.Current?.GetCampaignBehavior<DefensivePactBehavior>() == null
                ? null : Add("{=BC_Overview_DefensivePacts}Defensive Pacts");
            var clients = Add("{=BC_Overview_Clients}Client States");
            Fill(first, wars.Left, allies.Left, pacts.Left, defensive?.Left, clients.Left);
            Fill(second, wars.Right, allies.Right, pacts.Right, defensive?.Right, clients.Right);
            foreach (var section in Sections) section.RefreshHeight();
        }

        private DiplomacyOverviewSectionVM Add(string title)
        {
            var section = new DiplomacyOverviewSectionVM(title);
            Sections.Add(section);
            return section;
        }

        private static void Fill(Kingdom realm, MBBindingList<DiplomacyOverviewRealmVM> wars,
            MBBindingList<DiplomacyOverviewRealmVM> allies, MBBindingList<DiplomacyOverviewRealmVM> pacts,
            MBBindingList<DiplomacyOverviewRealmVM> defensive, MBBindingList<DiplomacyOverviewRealmVM> clients)
        {
            foreach (var enemy in realm.FactionsAtWarWith.OfType<Kingdom>()
                .Where(k => k != realm && !k.IsEliminated).Distinct().OrderBy(k => k.Name.ToString()))
                wars.Add(new DiplomacyOverviewRealmVM(enemy, () => WarHint(realm, enemy)));

            var alliances = Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
            var clientBehavior = Campaign.Current?.GetCampaignBehavior<ClientKingdomBehavior>();
            if (alliances != null)
                foreach (var partner in Kingdom.All.Where(k => k != realm && !k.IsEliminated
                    && clientBehavior?.IsProtectedClientPair(realm, k) != true
                    && alliances.IsAllyWithKingdom(realm, k)).OrderBy(k => k.Name.ToString()))
                    allies.Add(new DiplomacyOverviewRealmVM(partner, () => AllianceHint(realm, partner)));

            var hostages = Campaign.Current?.GetCampaignBehavior<HostagePactBehavior>();
            if (hostages != null)
                foreach (var pact in hostages.GetActivePacts(realm).OrderBy(p => p.EndDay))
                {
                    var partner = pact.FirstRealm == realm ? pact.SecondRealm : pact.FirstRealm;
                    pacts.Add(new DiplomacyOverviewRealmVM(partner, () => PactHint(pact, realm)));
                }

            FillDefensivePacts(realm, defensive);

            if (clientBehavior == null) return;
            var suzerain = clientBehavior.GetSuzerain(realm);
            if (suzerain != null)
                clients.Add(new DiplomacyOverviewRealmVM(suzerain,
                    () => ClientLibertyTooltip.Build(clientBehavior.BuildLibertyAssessment(realm)),
                    new TextObject("{=BC_Overview_SubjectTo}Subject to {REALM}")
                        .SetTextVariable("REALM", suzerain.Name).ToString()));
            foreach (var client in clientBehavior.GetClients(realm).Where(k => !k.IsEliminated)
                .OrderBy(k => k.Name.ToString()))
                clients.Add(new DiplomacyOverviewRealmVM(client,
                    () => ClientLibertyTooltip.Build(clientBehavior.BuildLibertyAssessment(client))));
        }

        private static void FillDefensivePacts(Kingdom realm, MBBindingList<DiplomacyOverviewRealmVM> entries)
        {
            var owner = Campaign.Current?.GetCampaignBehavior<DefensivePactBehavior>();
            if (entries == null || owner == null || realm == null) return;
            var targets = new HashSet<string>(StringComparer.Ordinal);
            string membershipTarget = owner.GetMembershipTarget(realm.StringId);
            if (!string.IsNullOrWhiteSpace(membershipTarget)) targets.Add(membershipTarget);
            if (owner.GetMembersAgainst(realm.StringId).Count > 0) targets.Add(realm.StringId);
            if (targets.Count == 0) return;
            foreach (var target in Kingdom.All.Where(k => k != null && !k.IsEliminated && targets.Contains(k.StringId))
                .OrderBy(k => k.Name.ToString()))
            {
                var name = new TextObject("{=BC_Overview_DefensiveAgainst}Against {REALM}")
                    .SetTextVariable("REALM", target.Name).ToString();
                entries.Add(new DiplomacyOverviewRealmVM(target, () => DefensivePactHint(target), name));
            }
        }

        private static List<TooltipProperty> DefensivePactHint(Kingdom target)
        {
            var campaign = Campaign.Current;
            var membership = campaign?.GetCampaignBehavior<DefensivePactBehavior>()?.GetMembersAgainst(target.StringId);
            if (membership == null || membership.Count == 0 || target.IsEliminated)
                return BuildDefensivePactHint(target.Name, null, Array.Empty<string>());

            var memberIds = new HashSet<string>(membership
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.RealmId)).Select(m => m.RealmId), StringComparer.Ordinal);
            var members = Kingdom.All.Where(k => k != null && !k.IsEliminated && memberIds.Contains(k.StringId))
                .Select(k => k.Name.ToString()).OrderBy(name => name).ToArray();
            var threats = campaign.GetCampaignBehavior<DefensivePactThreatBehavior>();
            double? threat = members.Length > 0 && threats?.IsReady == true
                ? threats.GetThreat(target.StringId, CampaignTime.Now.ToDays) : (double?)null;
            return BuildDefensivePactHint(target.Name, threat, members);
        }

        internal static List<TooltipProperty> BuildDefensivePactHint(TextObject target, double? threat, IReadOnlyList<string> members)
        {
            var result = new List<TooltipProperty> { Title(new TextObject("{=BC_Overview_DefensiveAgainst}Against {REALM}")
                .SetTextVariable("REALM", target)), Separator() };
            if (members.Count == 0)
            {
                result.Add(Paragraph(new TextObject("{=BC_Overview_AgreementEnded}This agreement is no longer in force.")));
                return result;
            }
            if (threat.HasValue)
                result.Add(new TooltipProperty(new TextObject("{=BC_DefensivePact_TooltipCurrent}Current threat").ToString(),
                    DefensivePactThreatHint.FormatValue(threat.Value), 0));
            else result.Add(Paragraph(new TextObject("{=BC_Overview_DefensiveThreatUnavailable}Threat is currently unavailable.")));

            result.Add(Separator());
            result.Add(Title(new TextObject("{=BC_Overview_DefensiveRoster}Member Realms ({COUNT})")
                .SetTextVariable("COUNT", members.Count)));
            foreach (string member in members)
                result.Add(new TooltipProperty(string.Empty, member, 0, false, TooltipProperty.TooltipPropertyFlags.MultiLine));
            return result;
        }

        private static List<TooltipProperty> WarHint(Kingdom realm, Kingdom enemy)
        {
            var war = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.GetActiveWar(realm, enemy);
            if (WarPeaceRevampBehavior.IsRevampEnabled() && war != null)
                return WarScoreTooltip.Build(war, realm, enemy);
            return new List<TooltipProperty> {
                Title(enemy.Name),
                Paragraph(new TextObject("{=BC_Overview_NoScore}At war. No Bellum war-score breakdown is available for this conflict."))
            };
        }

        private static List<TooltipProperty> AllianceHint(Kingdom realm, Kingdom partner)
        {
            var result = new List<TooltipProperty> { Title(partner.Name) };
            var alliances = Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
            if (alliances?.IsAllyWithKingdom(realm, partner) == true)
                AddExpiry(result, alliances.GetAllianceEndDate(realm, partner).ToDays);
            else
                result.Add(Paragraph(new TextObject("{=BC_Overview_AgreementEnded}This agreement is no longer in force.")));
            return result;
        }

        internal static List<TooltipProperty> PactHint(HostagePactRecord pact, Kingdom realm)
        {
            var partner = pact.FirstRealm == realm ? pact.SecondRealm : pact.FirstRealm;
            var result = new List<TooltipProperty> { Title(partner.Name) };
            if (!HostagePactBehavior.IsCurrentAgreement(pact, realm, CampaignTime.Now.ToDays))
            {
                result.Add(Paragraph(new TextObject("{=BC_Overview_AgreementEnded}This agreement is no longer in force.")));
                return result;
            }
            AddExpiry(result, pact.EndDay);
            AddHostage(result, pact.FirstRealm, pact.FirstHostage);
            AddHostage(result, pact.SecondRealm, pact.SecondHostage);
            return result;
        }

        private static void AddHostage(List<TooltipProperty> result, Kingdom supplier, TreatyHostageRecord hostage)
        {
            TextObject text = hostage == null
                ? new TextObject("{=BC_Overview_NoHostage}{REALM} has pledged no hostage.")
                : new TextObject("{=BC_Overview_Hostage}{REALM} has pledged {HERO} of {HOUSE}, held by {CAPTOR} at {HOLDING}.")
                    .SetTextVariable("HERO", hostage.Hero.Name)
                    .SetTextVariable("HOUSE", hostage.SupplyingHouse.Name)
                    .SetTextVariable("CAPTOR", hostage.ReceivingHouse.Name)
                    .SetTextVariable("HOLDING", hostage.Holding.Name);
            result.Add(Paragraph(text.SetTextVariable("REALM", supplier.Name)));
        }

        private static void AddExpiry(List<TooltipProperty> result, double day)
        {
            if (double.IsNaN(day) || double.IsInfinity(day)) return;
            result.Add(Paragraph(new TextObject("{=BC_Overview_Expires}Expires on {DATE} ({DAYS} days remaining).")
                .SetTextVariable("DATE", CampaignTime.Days((float)day).ToString())
                .SetTextVariable("DAYS", Math.Max(0, (int)Math.Ceiling(day - CampaignTime.Now.ToDays)))));
        }

        private static TooltipProperty Title(TextObject text) => new TooltipProperty(text.ToString(), "", 0,
            false, TooltipProperty.TooltipPropertyFlags.Title);
        private static TooltipProperty Separator() => new TooltipProperty("", "", 0,
            false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator);
        private static TooltipProperty Paragraph(TextObject text) => new TooltipProperty("", text.ToString(), 0,
            false, TooltipProperty.TooltipPropertyFlags.MultiLine);

        private void Clear()
        {
            foreach (var section in Sections) section.OnFinalize();
            Sections.Clear();
        }

        public override void OnFinalize() { Clear(); base.OnFinalize(); }
    }

    internal sealed class DiplomacyOverviewSectionVM : ViewModel
    {
        internal const int Columns = 4;
        internal const int CellHeight = 132;
        [DataSourceProperty] public string Title { get; }
        [DataSourceProperty] public int BodyHeight => CalculateBodyHeight(Left.Count, Right.Count);
        [DataSourceProperty] public MBBindingList<DiplomacyOverviewRealmVM> Left { get; }
            = new MBBindingList<DiplomacyOverviewRealmVM>();
        [DataSourceProperty] public MBBindingList<DiplomacyOverviewRealmVM> Right { get; }
            = new MBBindingList<DiplomacyOverviewRealmVM>();
        public DiplomacyOverviewSectionVM(string title) { Title = new TextObject(title).ToString(); }
        internal static int CalculateBodyHeight(int leftCount, int rightCount)
            => Math.Max(32, ((Math.Max(leftCount, rightCount) + Columns - 1) / Columns) * CellHeight);
        internal void RefreshHeight() => OnPropertyChangedWithValue(BodyHeight, nameof(BodyHeight));
        public override void OnFinalize()
        {
            foreach (var entry in Left.Concat(Right)) entry.OnFinalize();
            Left.Clear(); Right.Clear();
            base.OnFinalize();
        }
    }

    internal sealed class DiplomacyOverviewRealmVM : ViewModel
    {
        private readonly Kingdom _realm;
        private readonly BasicTooltipViewModel _hint;
        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public BannerImageIdentifierVM Banner { get; }

        internal DiplomacyOverviewRealmVM(Kingdom realm, Func<List<TooltipProperty>> tooltip, string name = null)
        {
            _realm = realm;
            Name = name ?? realm.Name.ToString();
            Banner = new BannerImageIdentifierVM(realm.Banner, true);
            _hint = new BasicTooltipViewModel(tooltip);
        }
        public void ExecuteLink() => Campaign.Current?.EncyclopediaManager.GoToLink(_realm.EncyclopediaLink);
        public void ExecuteBeginHint() => _hint.ExecuteBeginHint();
        public void ExecuteEndHint() => _hint.ExecuteEndHint();
        public override void OnFinalize() { _hint.ExecuteEndHint(); base.OnFinalize(); }
    }
}
