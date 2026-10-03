$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw -LiteralPath (Join-Path $root $name) }
function Check($condition, $label) { if (!$condition) { throw $label }; "PASS: $label" }
$flow = Read 'Behaviors/CourtLiberationAgendas.cs'
$source = Read 'CourtLiberationObjectiveSource.cs'
$main = Read 'Behaviors/CourtAgendaBehavior.cs'
$client = Read 'Behaviors/ClientKingdomBehavior.cs'
$picker = Read 'Behaviors/CourtPlayerMotionPicker.cs'
Check ($source.Contains('owner.Faction != null') -and $source.Contains('owner.Sponsor != context.Realm.RulingClan')) 'Only the Crown can select preparations.'
Check ($source.Contains('BuildLibertyAssessmentWithBonus(realm, 0)') -and $source.Contains('Boosted?.CanAttemptLiberation') -and $source.Contains('WillDeclareThreshold')) 'Selection assesses baseline and boosted native eligibility without bypassing war enthusiasm.'
Check ($main.Contains('selector.Register(new CourtLiberationObjectiveSource(), "foreign_affairs")') -and $main.Contains('MaintainLiberationObjectives();')) 'Weighted selection and daily maintenance are connected.'
Check ($picker.Contains('CourtLiberationRules.Kind') -and $picker.Contains('agenda.Liberation = prepared.Liberation')) 'Player selection retains its saved plan.'
Check ($client.Contains('hypotheticalBonus ?? (CourtAgendaBehavior.Current?.LiberationDesireBonus') -and $client.Contains('CalculateClanLiberty(clan, suzerain, record, lawful, courtBonus)')) 'The overlay is shared by ordinary clan and realm liberty assessments.'
$getter = $flow.Substring($flow.IndexOf('internal float LiberationDesireBonus'), $flow.IndexOf('private void MaintainLiberationObjectives') - $flow.IndexOf('internal float LiberationDesireBonus'))
Check (!$getter.Contains('BuildLibertyAssessment') -and $getter.Contains('return CourtLiberationRules.DesireBonus')) 'Bonus lookup cannot recurse into assessment or stack multiple agendas.'
$hook = Read 'Patches/ClientKingdomDiplomacyPatches.cs'
Check ($hook.IndexOf('RecordCourtLiberationWar') -lt $hook.IndexOf('ConfirmLiberationWar')) 'Verified war receipt precedes removal of the original client record.'
Check (!$flow.Contains('DeclareWarAction') -and !$flow.Contains('MakeWarAction')) 'The motion never declares war on behalf of the player.'
Check ((Read 'BellumCivileSaveDefiner.cs').Contains('typeof(CourtLiberationRecord), 110') -and (Read 'CourtAgendaRecord.cs').Contains('[SaveableField(45)] public CourtLiberationRecord Liberation')) 'Saved plan is appended without renumbering older fields.'
[xml]$xml = Read 'ModuleData/Languages/EN/strings.xml'
$texts = $flow + $main + $picker + $client + (Read 'ConflictOutcomeText.cs') + (Read 'CourtAgendaPresentation.cs')
$ids = @([regex]::Matches($texts, '\{=(BC_(?:CourtLiberation|Liberation)[^}]+)\}') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
foreach ($id in $ids) { Check (@($xml.base.strings.string | Where-Object { $_.id -eq $id }).Count -eq 1) "Unique English localization: $id" }
