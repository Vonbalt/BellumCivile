using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Conversation.Persuasion;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    public sealed partial class ElectionLobbyingBehavior : CampaignBehaviorBase
    {
        public static ElectionLobbyingBehavior Current => Campaign.Current?.GetCampaignBehavior<ElectionLobbyingBehavior>();
        private Dictionary<string, CampaignTime> _attempts = new Dictionary<string, CampaignTime>();
        private readonly Dictionary<int, PersuasionOptionArgs> _options = new Dictionary<int, PersuasionOptionArgs>();
        private Hero _speaker, _candidate;
        private Kingdom _realm;
        private CampaignTime _until;
        private ElectiveSuccessionRecord _ballot;
        private int _mandateNumber;
        private List<Hero> _candidates = new List<Hero>();
        private int _page, _used;
        private bool _persuading;
        private ElectionPersuasionAttempt _attempt;
        internal bool BribeCommitted;
        private ElectiveSuccessionBehavior Elections => ElectiveSuccessionBehavior.Instance;
        private ElectiveCommitment Vote => Elections?.Get(_realm)?.Votes.FirstOrDefault(v => v.Speaker == _speaker);
        private int Relation => Hero.MainHero?.GetRelation(_speaker) ?? 0;
        private bool SelfVoting => _candidate != _speaker && Vote?.Supported == _speaker;
        private double Gap => Vote?.Preferences.Any(p => p.Candidate == _candidate) == true
            ? ElectionLobbyingRules.Resistance(Vote.Preferences.Max(p => p.Score),
                Vote.Preferences.First(p => p.Candidate == _candidate).Score, SelfVoting) : 100;
        public override void RegisterEvents() => CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, AddDialogues);
        public override void SyncData(IDataStore store)
        {
            store.SyncData("BC_ElectionLobbyingAttempts", ref _attempts);
            _attempts = _attempts ?? new Dictionary<string, CampaignTime>();
        }
        private bool Entry() => Hero.OneToOneConversationHero?.Clan != null
            && Hero.OneToOneConversationHero.Clan != Clan.PlayerClan && !Clan.PlayerClan.IsUnderMercenaryService
            && Hero.OneToOneConversationHero.Clan.Kingdom == Clan.PlayerClan.Kingdom
            && ElectiveSuccessionBehavior.UsesElection(Clan.PlayerClan.Kingdom);
        private void Begin()
        {
            _speaker = Hero.OneToOneConversationHero; _realm = Clan.PlayerClan.Kingdom;
            EndPersuasion(false);
            _candidate = null; _page = 0; _options.Clear(); BribeCommitted = false;
            Elections.Maintain(_realm);
            _ballot = Elections.Get(_realm);
            _mandateNumber = _ballot?.MandateNumber ?? -1;
            _until = CourtAgendaBehavior.Current?.NextEvaluation(_realm) ?? CampaignTime.Now;
            _candidates = Vote?.Preferences.Select(p => p.Candidate).Where(h => h?.IsAlive == true)
                .OrderByDescending(h => h == Hero.MainHero).ThenBy(h => h.Name.ToString()).ToList() ?? new List<Hero>();
            foreach (var key in _attempts.Where(p => p.Value.ToDays <= CampaignTime.Now.ToDays).Select(p => p.Key).ToList()) _attempts.Remove(key);
        }
        private bool NotLeader()
        {
            MBTextManager.SetTextVariable("EL_HEAD", ElectiveSuccessionBehavior.LegalHead(_speaker?.Clan)?.Name ?? new TextObject("{=BC_EL_FamilyHead}the head of our family"));
            return _speaker != ElectiveSuccessionBehavior.LegalHead(_speaker?.Clan);
        }
        private bool Ready() => _speaker == Hero.OneToOneConversationHero && _realm == Clan.PlayerClan?.Kingdom
            && !Clan.PlayerClan.IsUnderMercenaryService && _speaker?.IsAlive == true && !_speaker.IsPrisoner
            && _until.ToDays > CampaignTime.Now.ToDays
            && Elections?.CanCommitPromise(_realm, _speaker, _candidate, out var until) == true && until == _until
            && _ballot != null && Elections.Get(_realm) == _ballot && _ballot.MandateNumber == _mandateNumber;
        private bool Negotiable(out TextObject hint)
        {
            hint = new TextObject("{=BC_EL_Unavailable}This vote is already pledged, the ballot is closed, or the circumstances have changed.");
            if (!Ready()) return false;
            hint = new TextObject("{=BC_EL_Expiry}Commitment lasts until {DATE}, unless the election concludes or either participant becomes ineligible.");
            hint.SetTextVariable("DATE", _until.ToString());
            return true;
        }
        private bool Persuadable(out TextObject hint)
        {
            if (!Negotiable(out hint)) return false;
            return CanBeginAttempt(_realm, _speaker, _candidate, out hint);
        }
        internal static double BribeOpenness(Hero speaker, double gap) => ElectionLobbyingRules.Openness(
            Hero.MainHero.GetRelation(speaker), gap, speaker.GetTraitLevel(DefaultTraits.Honor), speaker.GetTraitLevel(DefaultTraits.Mercy),
            speaker.GetTraitLevel(DefaultTraits.Generosity), speaker.GetTraitLevel(DefaultTraits.Calculating));
        private bool Bribable(out TextObject hint)
        {
            if (!Negotiable(out hint)) return false;
            if (BribeOpenness(_speaker, Gap) < C.FiefBribeOpennessThreshold)
            { hint = new TextObject("{=BC_EL_BribeRefusal}They will not bargain over this choice of ruler."); return false; }
            return true;
        }
        private void StartPersuasion()
        {
            if (!Persuadable(out _)) return;
            _used = 0; _options.Clear(); _persuading = true;
            ConversationManager.StartPersuasion(C.FiefPersuasionGoal, C.FiefPersuasionSuccessValue, 0,
                C.FiefPersuasionCriticalSuccessValue, C.FiefPersuasionCriticalFailValue, 0, PersuasionDifficulty.Medium);
        }
        private void EndPersuasion(bool success)
        {
            CompleteAttempt(_attempt, success && Ready());
            _attempt = null;
            if (_persuading) ConversationManager.EndPersuasion();
            _persuading = false; _options.Clear();
        }
        private void UseArgument(int argument)
        {
            if (_attempt == null && !TryBeginAttempt(_realm, _speaker, _candidate, out _attempt, out _))
            { EndPersuasion(false); return; }
            _used++;
            Option(argument).BlockTheOption(true);
        }
        private bool ArgumentAllowed(int argument, out TextObject hint)
        {
            if (!Negotiable(out hint) || !_persuading) return false;
            if (argument == 5 && Relation < 60)
            { hint = new TextObject("{=BC_EL_PersonalTrustGate}Personal trust requires at least 60 relations."); return false; }
            if (_used >= 3 || Option(argument).IsBlocked)
            { hint = new TextObject("{=BC_EL_ArgumentSpent}You have already made this appeal, or used your three arguments."); return false; }
            return true;
        }
        private bool Failed() => _persuading && (_used >= 3 || ConversationManager.GetPersuasionIsFailure()
            || ConversationManager.GetPersuasionChosenOptions()?.LastOrDefault()?.Item2 == PersuasionOptionResult.CriticalFailure);
        private PersuasionOptionArgs Option(int argument)
        {
            if (_options.TryGetValue(argument, out var option)) return option;
            var traits = new[] { DefaultTraits.Honor, DefaultTraits.Mercy, DefaultTraits.Calculating,
                DefaultTraits.Valor, DefaultTraits.Generosity, DefaultTraits.Honor };
            var trait = traits[argument];
            int fit = _speaker.GetTraitLevel(trait);
            if (argument == 1) fit = Math.Max(fit, _speaker.GetTraitLevel(DefaultTraits.Generosity));
            if (argument == 3) fit = Math.Max(fit, _speaker.GetTraitLevel(DefaultTraits.Calculating));
            if (argument == 4) fit = Math.Max(-fit, _speaker.GetTraitLevel(DefaultTraits.Calculating));
            var faction = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.GetIdeologicalFaction(_speaker.Clan);
            bool aligned = argument == 1 && faction?.Type == FactionType.Liberty
                || argument == 2 && faction?.Type == FactionType.Nobility || argument == 3 && faction?.Type == FactionType.Glory;
            if (aligned) fit = Math.Max(fit, 1);
            if (argument == 0 && _candidate.GetTraitLevel(DefaultTraits.Honor) < 0
                || argument == 1 && _candidate.GetTraitLevel(DefaultTraits.Mercy) < 0
                || argument == 4 && _candidate.GetTraitLevel(DefaultTraits.Generosity) < 0) fit = -1;
            var strengths = new[] { PersuasionArgumentStrength.ExtremelyEasy, PersuasionArgumentStrength.VeryEasy,
                PersuasionArgumentStrength.Easy, PersuasionArgumentStrength.Normal, PersuasionArgumentStrength.Hard,
                PersuasionArgumentStrength.VeryHard, PersuasionArgumentStrength.ExtremelyHard };
            int difficulty = ElectionLobbyingRules.Difficulty(Gap, Relation, fit, SelfVoting, argument == 5);
            option = new PersuasionOptionArgs(DefaultSkills.Charm, trait, argument == 4 ? TraitEffect.Negative : TraitEffect.Positive,
                strengths[difficulty + 3], false, ArgumentText(argument), new Tuple<TraitObject, int>[0], false, false, false);
            _options[argument] = option;
            return option;
        }
        private void LaunchBarter()
        {
            BribeCommitted = false;
            if (!Bribable(out _)) return;
            var record = Elections.Get(_realm);
            double share = record.TotalWeight > 0 ? 100 * Vote.Weight / record.TotalWeight : 0;
            int price = BellumCivileOptions.ApplyBribeCostMultiplier(ElectionLobbyingRules.Price(share, Gap, Relation,
                _speaker.GetTraitLevel(DefaultTraits.Honor), _speaker.GetTraitLevel(DefaultTraits.Generosity)));
            var item = new ElectionVoteBribeBarterable(_realm, _speaker, _candidate, Hero.MainHero, _until, price, Gap);
            BarterManager.Instance.StartBarterOffer(Hero.MainHero, _speaker, PartyBase.MainParty, _speaker.PartyBelongedTo?.Party,
                null, (barter, data, context) => true, 0, false, new Barterable[] { item });
        }
        private bool Slot(int slot)
        {
            int index = _page * 4 + slot;
            if (index >= _candidates.Count) return false;
            MBTextManager.SetTextVariable("EL_NAME" + slot, _candidates[index] == Hero.MainHero
                ? new TextObject("{=BC_Fief_Delib_PushPlayer}I would like to nominate myself for such an honor.")
                : _candidates[index].Name);
            return true;
        }
        private void Select(int slot)
        {
            _candidate = _candidates[_page * 4 + slot]; _options.Clear();
            MBTextManager.SetTextVariable("EL_CANDIDATE", _candidate.Name);
        }
        private bool Stance()
        {
            var vote = Vote;
            TextObject text;
            if (vote == null) text = new TextObject("{=BC_EL_NoVote}My house has no voice in this election.");
            else if (vote.Supported == _speaker) text = new TextObject("{=BC_EL_SelfVote}I mean to put my own house forward. I believe I can lead this realm.");
            else if (vote.Source != "natural" && vote.Until.ToDays > CampaignTime.Now.ToDays)
                text = new TextObject("{=BC_EL_Pledged}I have given my word to support {NAME}, until the court next weighs these matters.");
            else if (vote.Supported != null) text = new TextObject("{=BC_EL_Leaning}At present, I favor {NAME}. That is where my house's voice will be heard.");
            else text = new TextObject("{=BC_EL_Undecided}I have not yet decided whose cause deserves my house's voice.");
            var namedCandidate = vote != null && vote.Source != "natural" ? vote.Nominee : vote?.Supported;
            text.SetTextVariable("NAME", namedCandidate?.Name ?? new TextObject("{=BC_EL_NoCandidate}no candidate"));
            MBTextManager.SetTextVariable("EL_STANCE", text);
            return true;
        }
        private bool Explain()
        {
            var vote = Vote;
            var preference = vote?.Preferences.FirstOrDefault(p => p.Candidate == vote.Supported);
            string text = vote?.Source != "natural" && vote?.Until.ToDays > CampaignTime.Now.ToDays
                ? "{=BC_EL_WhyPromise}I have made an agreement. For the time being, I intend to honor it."
                : vote?.Supported == _speaker ? "{=BC_EL_WhySelf}I know my house's strength, and I trust my own judgment more than another's."
                : preference == null ? "{=BC_EL_WhyUndecided}None has yet persuaded me that their cause outweighs the others."
                : _speaker.GetRelation(preference.Candidate) >= 30 ? "{=BC_EL_WhyFriend}I know their character and trust them to govern well."
                : preference.Law >= preference.Politics ? "{=BC_EL_WhyLaw}Their standing under our laws weighs heavily with me. A crown should not rest on favors alone."
                : "{=BC_EL_WhyPolitics}Their standing among the houses and the interests of my own family make them the sounder choice.";
            MBTextManager.SetTextVariable("EL_REASON", new TextObject(text)); return true;
        }
    }
}
