$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -Raw (Join-Path $root 'CouncilAppointmentNominationHelper.cs')
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$ranking = $source.Substring($source.IndexOf('internal static List<CouncilAppointmentNominationRanking> BuildRanking('))
$ranking = $ranking.Substring(0, $ranking.IndexOf('internal static TextObject BuildReasonText('))
Check ([regex]::Matches($ranking, 'GetAppointmentCandidatesForVote\(').Count -eq 1) 'Ranking fetches the eligible pool once.'
Check ($ranking.IndexOf('new CandidatePool(') -lt $ranking.IndexOf('foreach (Clan voter')) 'Pool creation is outside the voter loop.'
Check ($ranking.Contains('ScoreCandidate(voter, committed, kingdom, office, council, candidates)')) 'Committed nominations use the same eligibility snapshot.'
Check ($ranking.Contains('ChooseNominee(voter, kingdom, office, council, candidates)')) 'Uncommitted nominations reuse the ranking snapshot.'
Check ($source.Contains('foreach (Clan candidate in candidates.Ordered)')) 'Candidate scoring retains the original candidate order.'
Check ($source.Contains('!candidates.Eligible.Contains(candidate)')) 'Snapshot membership still rejects ineligible candidates.'
Check ($source.Contains('float score = council.CalculateAppointmentSupport(kingdom, voter, candidate, office);')) 'Nomination support delegates to the existing council formula.'
Check (!$source.Contains('static CandidatePool')) 'No static candidate pool survives between ranking passes.'
'8 source contracts passed; these do not substitute for campaign nomination-equivalence or performance tests.'
