$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$behavior = Read 'Behaviors/ElectiveContestBehavior.cs'
$ballot = Read 'Behaviors/ElectiveSuccessionBehavior.cs'
$accession = Read 'Behaviors/CrownAccessionBehavior.cs'
$save = Read 'BellumCivileSaveDefiner.cs'
Check ($ballot.IndexOf('ElectiveContestBehavior.Instance?.Capture(accession, record)') -gt
    $ballot.IndexOf('if (!record.PlayerConfirmed && record.Votes.Any')) 'Contest snapshot follows final player ballot confirmation.'
Check ($behavior.Contains('!accession.ElectiveElection || accession.Emergency') -and
    $behavior.Contains('previous?.AccessionCompleted == true')) 'Only custom elections are captured; completed snapshots cannot be overwritten.'
Check ($accession.Contains('ElectiveContestBehavior.Instance?.CompleteAccession(record)') -and
    $behavior.Contains('!accession.Completed || !accession.TitleTransferred')) 'Actual completed crown transfer gates readiness.'
Check ($behavior.Contains('accession.Heir != record.ElectedWinner') -and $behavior.Contains('record.Closed = true')) 'Emergency or superseded accession closes its snapshot.'
Check ($behavior -notmatch 'DailyTick|TickEvent|ShowInquiry|DeclareWar|new FactionObject|MBRandom') 'Foundation cannot autonomously roll, prompt, or start a war.'
foreach ($type in @('ElectiveContestRecord', 'ElectiveContestCandidate', 'ElectiveContestVote', 'ElectiveContestPledge', 'ElectiveContestPreference')) {
    Check ($save.Contains("AddClassDefinition(typeof($type)") -and
        $save.Contains("ConstructContainerDefinition(typeof(List<$type>))")) "Save type and container registered: $type"
}
$ids = [regex]::Matches($save, 'Add(?:Class|Enum)Definition\(typeof\([^\r\n]+?\), (\d+)\)') | ForEach-Object { $_.Groups[1].Value }
Check (($ids | Select-Object -Unique).Count -eq $ids.Count) 'Global save class/enum identifiers remain unique.'
'Source contracts only. Full save/load and live multi-side disputes remain untested.'
$pledges = Read 'Behaviors/ElectiveContestPledges.cs'
$rules = Read 'ElectiveContestPledgeRules.cs'
$inquiries = Read 'Behaviors/ElectiveContestInquiries.cs'
$dispatch = Read 'Behaviors/ElectiveContestDispatch.cs'
Check ($dispatch.Contains('CalculateLiveClanPower(pledge.House)') -and $dispatch.Contains('record.PledgesResolved = false;')) 'Ultimata refresh forces and reallocate saved pledges without rerolling.'
Check ($dispatch.Contains('if (record.RulerRoll < 0)') -and $dispatch.Contains('ElectiveContestUltimatumRules.ChooseSurrender')) 'NPC ruler stores one roll and delegates the shared acceptance formula through pure rules.'
Check ((Read 'ElectiveContestUltimatumRules.cs').Contains('UltimatumAcceptanceRules.Calculate')) 'Elective surrender reuses ordinary ultimatum acceptance.'
Check ($dispatch.IndexOf('!WarPeaceRevampBehavior.IsRevampEnabled()') -lt $dispatch.IndexOf('record.WarDispatchStarted = true;')) 'Three-live-side dispatch requires war-score services before any world mutation.'
Check ($dispatch.Contains('BindSuccessionChallenge("elective_"') -and $dispatch.Contains('GetTrackedRebelKingdomIncludingEliminated()')) 'Single-side dispatch retains deterministic identity and recovers its tracked shell.'
Check ($dispatch.Contains('record.CrownTransferStarted = true;') -and $dispatch.Contains('crown.DeFactoHolderClanId') -and $dispatch.Contains('BeginSuccessorMandate')) 'Surrender journals Crown transfer, verifies title ownership and starts a full mandate.'
Check ($inquiries.Contains('record.UltimataPrepared) return false;')) 'Player cannot withdraw through an old final-report callback after ultimata freeze.'
Check ($inquiries.Contains('candidate.Decide(available, player,') -and $inquiries.Contains('Decision == ElectiveContestDecision.Pending')) 'Only pending candidates receive an initial willingness decision.'
Check ($inquiries.Contains('if (!ResolvePledges(record)) return false;') -and $inquiries.Contains('player.UltimatumConfirmed = true;')) 'Final player confirmation follows exclusive pledge resolution and saves its receipt.'
Check ($inquiries.Contains('player.Withdrawn = true;') -and $inquiries.Contains('record.PledgesResolved = false;')) 'Withdrawal reallocates saved fallback choices without rerolling.'
Check ($inquiries.Contains('record.ElectedHouse.Leader == record.ElectedWinner') -and $inquiries.Contains('p.House.Leader == p.Speaker')) 'Inquiry callbacks reject stale ruler and house leadership.'
Check ($inquiries.Contains('c.Candidate == record.ElectedWinner') -and $inquiries.Contains('!c.Withdrawn') -and $inquiries.Contains('ComparisonText(player.PledgedPower, opponent.PledgedPower)')) 'Final report compares each remaining opponent separately.'
[xml]$strings = Read 'ModuleData/Languages/EN/strings.xml'
foreach ($id in ([regex]::Matches($inquiries + $dispatch, '\{=(BC_Contest_[^}]+)\}') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)) {
    Check (@($strings.base.strings.string | Where-Object id -eq $id).Count -eq 1) "Unique localized contest message: $id"
}
Check ($pledges.Contains('if (record.PledgesCaptured) return true') -and $pledges.Contains('PlayerChoice = true') -and
    $pledges.Contains('pledge.AwaitingPlayer = true')) 'Saved capture cannot reroll and player contingencies require input.'
Check ($pledges.Contains('CivilWarSolidarityHelper.AssessSupport') -and $pledges.Contains('IsImmediateVassalOf') -and
    $pledges.Contains('c.Candidate.Candidate == endorsement')) 'Existing solidarity and direct-vassal probabilities are reused; endorsement orders preferences.'
Check ($rules.Contains('ElectiveContestUltimatumRules.Opposition') -and $rules.Contains('result.Active.Remove(hero)') -and
    $rules.Contains('p.HasStronghold')) 'Power gate includes discounted rival threat, permanent withdrawal and stronghold requirement.'
Check ($behavior.Contains('p.House.Leader != p.Speaker') -and $behavior.Contains('if (record.PledgesResolved) return true')) 'Commit rejects changed speakers and reuses completed pledge receipts.'
