$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($value, $message) { if (!$value) { throw $message }; "PASS: $message" }
$source = Read 'CourtPeaceObjectiveSource.cs'
$behavior = Read 'Behaviors/CourtPeaceAgendas.cs'
$agenda = Read 'Behaviors/CourtAgendaBehavior.cs'
$picker = Read 'Behaviors/CourtPlayerMotionPicker.cs'
$treaty = Read 'TreatyRatificationService.cs'
$votes = Read 'Behaviors/WarPeaceRevampBehavior.cs'
Check ($source.Contains('FactionType.Liberty') -and $source.Contains('WarScoreConflictType.ForeignWar')) 'Peace objectives belong to Liberty and tracked foreign wars.'
Check ($source.Contains('IsTemporaryBellumKingdom') -and $source.Contains('IsClientKingdom')) 'Shell realms and independent client diplomacy are excluded.'
Check ($agenda.Contains('Register(new CourtPeaceObjectiveSource(), "foreign_affairs")')) 'Peace shares the bounded foreign-affairs selection family.'
Check (!$source.Contains('TryCreatePeaceDecision') -and !$behavior.Contains('TryQueue') -and !$behavior.Contains('MakePeaceAction.Apply')) 'Discovery and activation never open a parley or force peace.'
Check ($behavior.Contains('voter == Clan.PlayerClan') -and $behavior.Contains('ReceivesPoliticalSupport(agenda, voter, plan.Members)')) 'Player choice is preserved; aligned Crown uses shared political eligibility.'
Check ($behavior.Contains('plan.Members = agenda.Faction.Members') -and $behavior.Contains('plan.Members?.RemoveAll')) 'Membership is snapshotted at activation and departures are pruned.'
Check ((Read 'Behaviors/CourtPoliticalSupport.cs').Contains('agenda.Faction.Members.Contains(voter)') -and $behavior.Contains('!Eligible(voter, realm)')) 'Live eligibility prevents stale membership bonuses.'
Check ($treaty.Contains('PeaceInitiativeBonus(kingdom, war, clan)') -and $treaty.Contains('"Court peace initiative"')) 'Treaty ratification and previews include a separately explained bonus.'
Check ($votes.Contains('PeaceInitiativeBonus(source, war, voter)') -and $votes.Contains('support = supportPeaceOutcome ? raw : -raw')) 'Native peace support keeps symmetric outcome scoring with the bonus.'
Check ($agenda.IndexOf('MaintainPeaceObjectives();') -lt $agenda.IndexOf('OpenTerm(realm, manager, ideology);')) 'Expired objectives settle before new-term cleanup and selection.'
Check ($agenda.Contains('!a.IsUnopened && !a.IsOngoingObjective') -and (Read 'Behaviors/CourtExecutiveAgendas.cs').Contains('!a.IsUnopened && !a.IsOngoingObjective')) 'Ongoing objectives survive term and closed-vote cleanup.'
Check ($agenda.Contains('CampaignEvents.MakePeace.AddNonSerializedListener(this, OnCourtPeace)') -and $behavior.Contains('latest != plan.War')) 'Actual peace callback is matched to the saved war instance.'
Check ($behavior.Contains('CourtAgendaSuccessShock') -and $behavior.Contains('CourtAgendaFailureShock') -and $behavior.Contains('TryClaimResult()')) 'Existing outcome mood weights use a one-shot result receipt.'
Check ($behavior.Contains('CourtObjectiveCredit.FulfilledElsewhere')) 'External fulfillment keeps distinct result credit.'
Check ((Read 'UI/FactionItemVM.cs').Contains('PeaceHint(faction.ParentKingdom, faction)')) 'The existing conditional agenda hover displays peace initiative details.'
Check ($picker.Contains('agenda.Peace = prepared.Peace') -and $picker.Contains('agenda.SessionDate = prepared.SessionDate')) 'Confirmed player substitution saves the selected conflict and early date.'
Check ((Read 'BellumCivileSaveDefiner.cs').Contains('AddClassDefinition(typeof(CourtPeaceRecord), 93)')) 'Peace plan has a registered save class.'
[xml]$xml = Read 'ModuleData/Languages/EN/strings.xml'
$texts = $behavior + $picker + $treaty + (Read 'CourtAgendaPresentation.cs')
foreach ($id in @('BC_CourtSeekPeace','BC_CourtPeaceHint','BC_CourtPeaceBegun','BC_CourtPeaceSucceeded','BC_CourtPeaceFailed','BC_CourtPeaceCancelled',
    'BC_CourtPeaceStatus','BC_CourtPeaceScheduled','BC_CourtPeaceExpired','BC_CourtPeaceComplete','BC_CourtPeaceClosed',
    'BC_CourtObjectiveDeadline','BC_AgendaShortPursuing','BC_CourtPickerForeignAffairs','BC_CourtPeaceDetail','BC_Parley_Reason_CourtPeaceInitiative')) {
    $entries = @($xml.base.strings.string | Where-Object id -eq $id)
    $fallback = [regex]::Match($texts, '\{=' + $id + '\}([^"\r\n]*)').Groups[1].Value
    Check ($entries.Count -eq 1 -and $entries[0].text -ceq $fallback) "Unique matching localization: $id"
}
'Source/XML contracts only; native event ordering, UI and save/load still require campaign testing.'
