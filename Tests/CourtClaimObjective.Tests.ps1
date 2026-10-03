$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($value, $message) { if (!$value) { throw $message }; "PASS: $message" }
$source = Read 'CourtClaimObjectiveSource.cs'
$behavior = Read 'Behaviors/CourtClaimAgendas.cs'
$agenda = Read 'Behaviors/CourtAgendaBehavior.cs'
$picker = Read 'Behaviors/CourtPlayerMotionPicker.cs'
$draft = Read 'TreatyAiDraftService.cs'
$score = Read 'FiefNominationHelper.cs'
Check ($source.Contains('FactionType.Nobility') -and $source.Contains('Mood > -60') -and $source.Contains('MemberCount(owner.Faction) >= 2')) 'Claim motion requires ordinary Nobility agenda eligibility.'
Check ($source.Contains('title.ParentTitleId') -and $source.Contains('HashSet<string>') -and $source.Contains('claimed.DeJureHolderClanId')) 'Direct and ancestor title claims retain identity and cycle protection.'
Check ($source.Contains('IsWithinOrdinaryOwnershipCeiling') -and $source.Contains('IsDirectDeJureHolder')) 'Discovery preserves ordinary ownership eligibility and existing de jure exception.'
Check ((Read 'CourtPolicyObjectiveSource.cs').Contains('_claimCandidates.TryGetValue')) 'Discovery is cached per selection pass, not rerun for each score.'
Check ($agenda.Contains('Register(new CourtClaimObjectiveSource(), "foreign_affairs")')) 'Claim targets do not multiply foreign-family lottery weight.'
Check ($agenda.Contains('SyncData("BC_CourtClaimGrace", ref _claimGrace)')) 'Pending grace saves separately from next-term agendas.'
Check ($behavior.Contains('_agendas.Remove(agenda)') -and $behavior.Contains('_claimGrace.Add(agenda)')) 'Grace cannot occupy the next normal term.'
Check ($behavior.Contains('CourtClaimRules.GraceEnd(deadline, plan.GraceDays)') -and !$behavior.Contains('PoliticalDeliberationDays')) 'Runtime grace uses frozen duration rather than current MCM or retry date.'
Check ($behavior.Contains('OnCourtClaimAllocationResolved') -and $behavior.Contains('allocation_awarded_to_another_house')) 'Verified award to another house seals grace failure.'
Check ($behavior.Contains('if (plan.GraceActive) FinishClaimObjective')) 'Loss during grace seals failure before recapture.'
Check ($behavior.Contains('title.DeFactoHolderClanId == plan.Beneficiary.StringId') -and $behavior.Contains('!FiefDeliberationBehavior.IsAwaitingAllocation(plan.Fief)')) 'Final ownership requires both native and title ownership with no pending allocation.'
$maintenance = $behavior.Substring($behavior.IndexOf('private void MaintainClaimObjectives()'))
Check ($maintenance.IndexOf('SettledClaimOwnership(agenda)') -lt $maintenance.IndexOf('CourtClaimObjectiveSource.ValidClaim(plan)')) 'Satisfied claim is recognized before title restoration invalidates it.'
Check ([regex]::Matches($score, 'ClaimAllocationBonus\(').Count -eq 1 -and !(Read 'Patches/FiefVoteAIPatch.cs').Contains('ClaimAllocationBonus')) 'Allocation bonus is applied once in the shared scorer, never over bribery commitments.'
Check ($behavior.Contains('ReceivesPoliticalSupport(a, voter, a.Claim.Members)')) 'Allocation reuses current member and aligned NPC Crown checks.'
Check ((Read 'CourtTreatyDraftPreference.cs').Contains('term.SettlementId == SettlementId')) 'Treaty matching includes the exact named holding.'
Check ($draft.Contains('BuildTerritorialCandidates(war, winner, loser, posture, false, core, optional)') -and $draft.Contains('preference.Matches(c.Term) && c.Cost <= spend')) 'Named treaty alternative reuses legal territorial candidates and real budget.'
Check ($draft.IndexOf('AddCandidates(new[] { priorityClaim }') -lt $draft.IndexOf('AddCandidates(coreTerritory')) 'Alternative seeds named claim before competing ordinary demands.'
Check ($draft.Contains('coreTerritory.RemoveAll') -and $draft.Contains('optionalTerritory.RemoveAll')) 'Named demand cannot be added twice.'
Check ($picker.Contains('agenda.Claim = prepared.Claim') -and $picker -match 'motion.Kind == CourtClaimRules.Kind[^\r\n]*\) && identity.HasTermSnapshot') 'Manual selection retains prepared claimant and early session.'
Check ((Read 'BellumCivileSaveDefiner.cs').Contains('AddClassDefinition(typeof(CourtClaimRecord), 96)') -and (Read 'CourtAgendaRecord.cs').Contains('[SaveableField(38)] public CourtClaimRecord Claim')) 'Claim save IDs are appended.'
Check ($behavior.Contains('TryClaimResult()') -and $behavior.Contains('actual = agenda.Faction.Mood - before')) 'Outcome reports use one-shot receipts and actual capped mood.'
[xml]$xml = Read 'ModuleData/Languages/EN/strings.xml'
$texts = $behavior + $picker + $score + (Read 'CourtAgendaPresentation.cs') + (Read 'Patches/ForeignPolicyVoteAIPatch.cs')
$ids = [regex]::Matches($texts, '\{=(BC_[^}]*Claim[^}]*)\}') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
foreach ($id in $ids | Where-Object { $_ -like 'BC_Court*' -or $_ -in @('BC_AgendaShortClaim', 'BC_Fief_Delib_ReasonCourtClaim', 'BC_WarClaimReason') }) {
    $nodes = @($xml.SelectNodes("//string[@id='$id']"))
    Check ($nodes.Count -eq 1) "Unique claim localization: $id"
    $fallback = [regex]::Match($texts, ('\{=' + [regex]::Escape($id) + '\}([^"\r\n]*)')).Groups[1].Value.Replace('\n', "`n")
    Check ($nodes[0].text -ceq $fallback) "Matching claim fallback: $id"
}
'Claim source/XML contracts passed; live campaign and save/load remain separate tests.'
