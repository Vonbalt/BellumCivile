$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw -LiteralPath (Join-Path $root $path) }
function Check($ok, $message) { if (!$ok) { throw $message }; "PASS: $message" }
$council = Read 'Behaviors/CourtCouncilAgendas.cs'
$source = Read 'CourtCouncilObjectiveSource.cs'
$hub = Read 'Behaviors/CourtAgendaBehavior.cs'
$priority = $council.Substring($council.IndexOf('private bool MaintainCaptivityVacancyPriority'))
Check ($priority.Contains('realm.RulingClan == Clan.PlayerClan')) 'Automatic crisis replacement excludes player rulers.'
Check (!$priority.Contains('source.FindCandidates')) 'Emergency selection cannot be filtered by the discretionary candidate-discovery gate.'
Check ($priority.Contains('NpcInfluenceExpenseKind.CrownEmergency')) 'Emergency selection uses emergency affordability.'
Check ($source.Contains('IsCaptivityAppointment(facts, owner, office)') -and $source.Contains('captivity_emergency; affordable=')) 'Filing revalidation recognizes eligible emergency nominees without an ordinary support forecast.'
Check ($council.Contains('IsCaptivityAppointment(agenda) ? NpcInfluenceExpenseKind.CrownEmergency')) 'Actual filing uses the same emergency spending category.'
Check ($hub.Contains('TrySpend(agenda.Sponsor, cost, expense, "court_agenda_filing")')) 'Payment honors the caller category; ordinary callers keep the default.'
Check ($council.Contains('!IsCaptivityAppointment(except) && a.IsUnopened')) 'Unfiled reservations cannot block an emergency, while filed appointments still do.'
Check ($priority.Contains('office) && CouncilTargetValid(reservation, out _)') -and $priority.Contains('urgent.Any(r => r.Office == office)')) 'A valid scheduled vacancy keeps its place instead of having its date reset for another vacancy.'
Check ($priority.Contains('CampaignTime.Now + CampaignTime.Days(1)') -and $priority.Contains('agenda.FreezeSchedule')) 'Emergency appointment keeps one preparation day and the configured deliberation window.'
foreach ($reason in @('succession_pending', 'council_proceeding_active', 'protected_crown_business', 'crown_proceeding_or_decree_pending', 'emergency_influence_unavailable', 'no_eligible_vacancy_candidate')) {
    Check ($priority.Contains($reason)) "Explicit wait reason: $reason"
}
Check ($council.Contains('previous != signature')) 'Repeated unchanged wait reasons do not flood logs.'
'Source-contract checks only; a live campaign is still needed to verify ballot and save/reload behavior.'
