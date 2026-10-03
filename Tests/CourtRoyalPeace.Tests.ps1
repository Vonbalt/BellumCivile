$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw -LiteralPath (Join-Path $root $name) }
function Check($condition, $label) { if (!$condition) { throw $label }; "PASS: $label" }
$flow = Read 'Behaviors/CourtRoyalPeaceAgendas.cs'
$main = Read 'Behaviors/CourtAgendaBehavior.cs'
Check ($main.Contains('OnRoyalPeaceInvasion') -and $main.Contains('OnRoyalPeaceEnded') -and $main.Contains('MaintainRoyalPeaceCases();')) 'Declaration, peace and daily maintenance are wired.'
Check ($flow.Contains('defender.RulingClan == Clan.PlayerClan') -and $flow.Contains('w.ParentKingdomId == defender.StringId')) 'Only an attacked NPC Crown with active private wars creates cases.'
Check ($flow.Contains('c.WarStarted == warStarted') -and $flow.Contains('WarStartDate.ToDays == entry.WarStarted')) 'Exact foreign-war instance guards duplicate events and redeclarations.'
Check ($flow.Contains('entry.Assessed = true;') -and $flow.Contains('if (entry.Assessed)') -and $flow.Contains('"ruler_declined"')) 'The assessment is saved and is not rolled daily.'
Check ($flow.Contains('OrderByDescending(x => x.Power)') -and $flow.Contains('GetClaimFeudPreview')) 'Selection prioritizes recoverable strength among executable, affordable feuds.'
Check ($flow.Contains('agenda.IsFiled || agenda.IsOngoingObjective') -and $flow.Contains('agenda.PaidInfluence > 0') -and $flow.Contains('PendingDeposition') -and $flow.Contains('IsPending(entry.Realm)')) 'Paid business, ongoing objectives and succession are protected.'
Check ((Read 'Behaviors/CourtExecutiveAgendas.cs').Contains('!IsRoyalPeace(agenda)') -and (Read 'Behaviors/CourtCouncilAgendas.cs').Contains('IsRoyalPeace(agenda)')) 'Other priority agendas cannot overwrite announced royal peace.'
Check ($flow.Contains('CourtRoyalPeaceRules.ExecutionDay(now, BellumCivileOptions.PoliticalDeliberationDays)')) 'Notice uses the configured deliberation window.'
Check ($flow.Contains('if (!entry.TryBegin()) return;') -and $flow.Contains('if (entry.Started)') -and $flow.Contains('will not replay')) 'Saved execution receipts prevent retrying interrupted settlements.'
Check ($flow.Contains('RoyalPeaceNotice(entry, "royal_peace_warning"') -and $flow.Contains('"royal_peace_cancelled"') -and $flow.Contains('notice.PlayerInvolved = true')) 'Warnings and cancellations reach players inside feud shells.'
Check ((Read 'CourtAgendaPresentation.cs').Contains('BC_RoyalPeaceDate') -and $main.Contains('BC_RoyalPeaceStatus')) 'Agenda date and tooltip describe a decree, not a ballot.'
Check ($main.Contains('BC_CourtRoyalPeaceCases') -and (Read 'BellumCivileSaveDefiner.cs').Contains('typeof(CourtRoyalPeaceCase), 109')) 'Case collection and type are registered for persistence.'
[xml]$xml = Read 'ModuleData/Languages/EN/strings.xml'
$texts = $flow + $main + (Read 'ConflictOutcomeText.cs') + (Read 'CourtAgendaPresentation.cs')
$ids = @([regex]::Matches($texts, '\{=(BC_RoyalPeace[^}]+)\}') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
foreach ($id in $ids) { Check (@($xml.base.strings.string | Where-Object { $_.id -eq $id }).Count -eq 1) "Unique English localization: $id" }
