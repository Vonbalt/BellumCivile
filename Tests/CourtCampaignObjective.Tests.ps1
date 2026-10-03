$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($value, $message) { if (!$value) { throw $message }; "PASS: $message" }
$source = Read 'CourtCampaignObjectiveSource.cs'
$behavior = Read 'Behaviors/CourtCampaignAgendas.cs'
$agenda = Read 'Behaviors/CourtAgendaBehavior.cs'
$picker = Read 'Behaviors/CourtPlayerMotionPicker.cs'
$votes = Read 'Behaviors/WarPeaceRevampBehavior.cs'
$context = Read 'CourtPolicyObjectiveSource.cs'
$voteStart = $votes.IndexOf('internal bool TryEvaluateWarSupport(')
$warVote = $votes.Substring($voteStart, $votes.IndexOf('internal bool TryEvaluatePeaceSupport(') - $voteStart)
$proposalStart = $votes.IndexOf('internal bool TryCreateWarDecision(')
$proposal = $votes.Substring($proposalStart, $votes.IndexOf('internal bool TryCreatePeaceDecision(') - $proposalStart)
Check ($source.Contains('FactionType.Glory') -and $source.Contains('!s.IsActiveWar && !s.IsLiberationTarget')) 'Only Glory can choose credible pre-war campaign targets; liberation is separate.'
Check ($source.Contains('ValidRealm(target)') -and $source.Contains('IsClientKingdom(realm)')) 'Campaigns exclude temporary targets and independent client diplomacy.'
Check ($context.Contains('_campaignTargets.TryGetValue') -and $context.Contains('_campaignPermissions.TryGetValue')) 'Term discovery caches rankings per house and legality per target.'
Check ($source.Contains('Distinct().SelectMany(c => context.CampaignTargets(c))')) 'Faction discovery considers member-house interests, not just the leader.'
Check ($agenda.Contains('Register(new CourtCampaignObjectiveSource(), "foreign_affairs")')) 'Campaign uses the bounded foreign-affairs family.'
Check (!$source.Contains('TryCreateWarDecision') -and !$behavior.Contains('DeclareWarAction.Apply') -and !$behavior.Contains('TryPay(')) 'Selection and activation neither declare war nor charge a filing payment.'
Check (!$proposal.Contains('CampaignInitiativeBonus')) 'Ordinary NPC proposal thresholds and target preferences remain unchanged.'
Check ($votes.Contains('load.CivilWarCount > 0') -and $votes.Contains('load.ForeignWarCount >= 2') -and $votes.Contains('IsWarDecisionAllowedBetweenKingdoms(realm, target')) 'Campaign legality uses existing internal-war, front and permission checks.'
Check ($behavior.Contains('voter == Clan.PlayerClan') -and $behavior.Contains('ReceivesPoliticalSupport(agenda, voter, plan.Members)')) 'Player choice is preserved; Crown and membership eligibility share the political support rule.'
Check ($behavior.Contains('plan.Members = agenda.Faction.Members') -and $behavior.Contains('plan.Members?.RemoveAll')) 'Session snapshots participation and prunes departing houses.'
Check ($warVote.Contains('CampaignInitiativeBonus(source, target, voter)') -and $warVote.IndexOf('CampaignInitiativeBonus') -lt $warVote.IndexOf('Math.Min(raw, -100f)')) 'Shared war utility includes bonus before hard front veto.'
Check ($warVote.Contains('support = supportWarOutcome ? raw : -raw')) 'Supporting and opposing outcomes stay symmetric.'
Check ($behavior.Contains('WarPeaceRevampBehavior.CanSupportCourtCampaign(realm, target)')) 'Live restrictions can suspend campaign assistance.'
Check ($agenda.IndexOf('MaintainCampaignObjectives();') -lt $agenda.IndexOf('OpenTerm(realm, manager, ideology);')) 'Campaign expiry is processed before new-term cleanup.'
Check ($behavior.Contains('detail == DeclareWarAction.DeclareWarDetail.CausedByKingdomDecision') -and $behavior.Contains('bool initiated = agenda.Realm == attacker && target == defender')) 'Only an authorized offensive receives success credit.'
Check ($behavior.Contains('bool attacked = agenda.Realm == defender && target == attacker') -and $behavior.Contains('target_attacked_first')) 'Enemy-first entry has a distinct neutral cancellation report.'
Check ($behavior.Contains('TryClaimResult()') -and $behavior.Contains('CourtAgendaSuccessShock') -and $behavior.Contains('CourtAgendaFailureShock')) 'Outcome uses one-shot receipts and existing mood weights.'
Check ($picker.Contains('agenda.Campaign = prepared.Campaign') -and $picker.Contains('CourtAgendaRules.EarlyObjectiveSession')) 'Player selection saves the campaign and previews its early date.'
Check ((Read 'UI/FactionItemVM.cs').Contains('CampaignHint(faction.ParentKingdom, faction)')) 'Campaign explanation uses the conditional agenda hover.'
Check ((Read 'BellumCivileSaveDefiner.cs').Contains('AddClassDefinition(typeof(CourtCampaignRecord), 94)')) 'Campaign save class is registered.'
[xml]$xml = Read 'ModuleData/Languages/EN/strings.xml'
$texts = $behavior + $picker + (Read 'CourtAgendaPresentation.cs') + (Read 'Patches/ForeignPolicyVoteAIPatch.cs')
foreach ($id in @('BC_CourtSupportCampaign','BC_CourtCampaignBegun','BC_CourtCampaignSucceeded','BC_CourtCampaignFailed',
    'BC_CourtCampaignAttacked','BC_CourtCampaignCancelled','BC_CourtCampaignHint','BC_CourtCampaignStatus','BC_CourtCampaignScheduled',
    'BC_CourtCampaignExpired','BC_CourtCampaignComplete','BC_CourtCampaignClosed','BC_AgendaShortCampaign','BC_CourtCampaignDetail','BC_WarCampaignReason')) {
    $entries = @($xml.base.strings.string | Where-Object id -eq $id)
    $fallback = [regex]::Match($texts, '\{=' + $id + '\}([^"\r\n]*)').Groups[1].Value
    Check ($entries.Count -eq 1 -and $entries[0].text -ceq $fallback) "Unique matching localization: $id"
}
'Source/XML contracts only; campaign events, UI and native save/load still require live tests.'
