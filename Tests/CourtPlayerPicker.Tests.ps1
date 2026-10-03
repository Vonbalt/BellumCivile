$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$picker = Read 'Behaviors/CourtPlayerMotionPicker.cs'
$flow = Read 'Behaviors/CourtAgendaPlayerFlow.cs'
$context = Read 'CourtPolicyObjectiveSource.cs'
$council = Read 'CourtCouncilObjectiveSource.cs'
$agenda = Read 'CourtAgendaRecord.cs'
Check ($context.Contains('if (ManualSelection) return true;')) 'Manual activities bypass admission without changing NPC admission.'
Check ($picker.Contains('!evaluation.Eligible || kind == CourtActivityRules.Kind && !evaluation.Viable')) 'Manual choices use eligibility and valid activity targets, not NPC weights.'
Check (!$picker.Contains('MBRandom')) 'Browsing and filtering do not reroll candidate lotteries.'
Check ($council.Contains('HasCouncilReservation(context.Realm, context.PlayerAgenda)')) 'Substitution ignores only its own council reservation.'
Check ($council.Contains('foreach (var candidate in facts.Candidates(record.Office).Where(c => c != incumbent))')) 'Manual council list includes all eligible nominees, not only the favored shortlist.'
Check ($context.Contains('owner.Sponsor == Clan.PlayerClan && !context.ManualSelection')) 'Player Crown policies are available manually, not automatically selected.'
Check ($flow.Contains('if (choice == 2) { ShowTermMotionPicker(agenda, crisis); return; }')) 'Substitute opens the shared picker before any spending.'
Check ($picker.Contains('agenda.Faction == null ? 0 : BlockCost(agenda, crisis)')) 'Crown selection is free; crisis replacement uses suppression instead of two fees.'
Check ($picker.IndexOf('current == null || current.FilingCost != motion.FilingCost') -lt $picker.IndexOf('NpcInfluenceBudgetService.TrySpend')) 'Confirmation revalidates candidate and price before spending.'
Check ($picker.IndexOf('agenda.PlayerSelectionConfirmed = true;', $picker.IndexOf('private void ShowPlayerMotionConfirmation')) -lt $picker.IndexOf('NpcInfluenceBudgetService.TrySpend')) 'Confirmation seals before influence callbacks.'
Check ($agenda.Contains('[SaveableField(32)] public bool PlayerSelectionConfirmed;') -and $agenda.Contains('[SaveableField(33)] public int SubstitutionInfluencePaid;')) 'Selection and fee receipts persist with the agenda.'
Check ($picker.Contains('agenda.State = motion.Kind == DebateKind ? CourtAgendaState.Crisis : CourtAgendaState.Announced')) 'Tyranny debate enters the existing scheduled crisis lifecycle.'
Check ((Read 'Behaviors/IdeologyBehavior.cs').Contains('faction.Mood > -60 || CourtAgendaBehavior.MemberCount(faction) < 2')) 'Manual debate includes exactly -60 and requires at least two houses.'
Check ($picker.Contains('SessionDate = agenda.SessionDate') -and $picker.Contains('agenda.SessionDate = prepared.SessionDate') -and $picker -match 'motion.Kind == CourtPeaceRules.Kind[^\r\n]*motion.Kind == CourtClaimRules.Kind[^\r\n]*\) && identity.HasTermSnapshot') 'Picker preserves ordinary dates and previews early foreign-objective sessions.'
Check ($picker.Contains('motions.Count > 12')) 'Long lists use categories.'
Check ($flow.Contains('if (agenda.Faction == null)') -and $flow.Contains('choice == 1 ? CourtAgendaState.Blocked')) 'Crown none closes ordinary business without a faction blocking charge.'
foreach ($name in @('Behaviors/CourtCouncilAgendas.cs', 'Behaviors/CourtExecutiveAgendas.cs')) {
    Check ((Read $name).Contains('agenda.PlayerSelectionConfirmed || agenda.IsFiled')) "Alternate nomination respects confirmed selection: $name"
}
[xml]$xml = Read 'ModuleData/Languages/EN/strings.xml'
foreach ($match in [regex]::Matches($picker, '\{=(BC_(?:CrownTermInvitation|CrownChooseBusiness|CrownNoInitiative|CrownBusinessPrompt|CourtPicker\w+|CourtTermProposal|CourtDeclineCost|CourtChoiceCost|CourtNoBusiness|CourtDebateDescription))\}([^"\r\n]*)')) {
    $id = $match.Groups[1].Value
    $fallback = $match.Groups[2].Value.Replace('\n', "`n")
    $entry = @($xml.base.strings.string | Where-Object id -eq $id)
    Check ($entry.Count -eq 1 -and $entry[0].text -ceq $fallback) "Unique picker localization matches fallback: $id"
}
'Source/localization contracts only; live inquiry clicks, payments and save/load remain campaign tests.'
