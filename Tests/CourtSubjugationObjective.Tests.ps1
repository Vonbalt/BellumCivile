$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($value, $message) { if (!$value) { throw $message }; "PASS: $message" }
$source = Read 'CourtSubjugationObjectiveSource.cs'
$rules = Read 'CourtSubjugationRules.cs'
$behavior = Read 'Behaviors/CourtSubjugationAgendas.cs'
$agenda = Read 'Behaviors/CourtAgendaBehavior.cs'
$picker = Read 'Behaviors/CourtPlayerMotionPicker.cs'
$draft = Read 'TreatyAiDraftService.cs'
$treaty = Read 'Behaviors/ForeignTreatyBehavior.cs'
$votes = Read 'Behaviors/WarPeaceRevampBehavior.cs'
$council = Read 'TreatyRatificationService.cs'
$client = Read 'Behaviors/ClientKingdomBehavior.cs'
Check ($source.Contains('FactionType.Glory') -and $source.Contains('Mood > -60') -and $source.Contains('MemberCount(owner.Faction) >= 2')) 'Glory selection preserves ordinary faction eligibility.'
Check ($source.Contains('!s.IsActiveWar && !s.IsLiberationTarget') -and $source.Contains('CourtPeaceObjectiveSource.EligibleWar')) 'Peaceful credible neighbors and tracked foreign enemies are separate target sources.'
Check ($rules.Contains('cost < 150') -and $rules.Contains('targetStrength < strength') -and $rules.Contains('fiefs > 0')) 'Discovery requires a landed, weaker and structurally affordable target.'
Check ((Read 'CourtPolicyObjectiveSource.cs').Contains('_clientageCandidates.TryGetValue')) 'Structural valuation is cached per selection pass.'
Check ($agenda.Contains('Register(new CourtSubjugationObjectiveSource(), "foreign_affairs")')) 'More foreign motion types do not add family lottery weight.'
Check (!$behavior.Contains('DeclareWarAction.Apply') -and !$behavior.Contains('MakePeaceAction.Apply') -and !$behavior.Contains('TryPay(')) 'Objective activation neither declares war, signs peace nor charges a filing fee.'
Check ($behavior.Contains('ReceivesPoliticalSupport(agenda, voter, agenda.Subjugation.Members)') -and $behavior.Contains('drafter.RulingClan == Clan.PlayerClan')) 'Shared member/Crown support preserves player control.'
Check ($behavior.Contains('CourtSubjugationObjectiveSource.ValidPair(realm, target)') -and $behavior.Contains('a.IsOngoingObjective && !a.ResultApplied')) 'Expired, completed or structurally invalid plans cannot provide assistance.'
Check ($behavior.Contains('CourtPeaceObjectiveSource.EligibleWar(realm, target, war)') -and $behavior.Contains('SubjugationPreference(agenda).Score(terms)')) 'Treaty utility matches the foreign conflict and actual intended term.'
Check ($behavior.Contains('WarPeaceRevampBehavior.CanSupportCourtCampaign(realm, target)')) 'War assistance preserves truce/front/native legality gates.'
$start = $votes.IndexOf('internal bool TryEvaluateWarSupport(')
$warVote = $votes.Substring($start, $votes.IndexOf('internal bool TryEvaluatePeaceSupport(') - $start)
Check ($warVote.IndexOf('SubjugationWarBonus') -lt $warVote.IndexOf('Math.Min(raw, -100f)')) 'Subjugation bonus cannot override the hard front veto.'
Check ($council.Contains('SubjugationTreatyBonus(kingdom, war, clan, proposal?.Terms)')) 'Council previews and final evaluations use live clientage assistance.'
Check ($client.IndexOf('OnCourtClientageEstablished') -gt $client.IndexOf('AlignClientDiplomacyOnEstablishment(client, suzerain)')) 'Success callback runs after actual establishment and diplomatic synchronization.'
Check ($behavior.Contains('client.StartedDay') -and $behavior.Contains('CourtSubjugationRules.Fulfilled')) 'Save recovery verifies the actual dated clientage record.'
$maintenance = $behavior.Substring($behavior.IndexOf('private void MaintainSubjugationObjectives()'))
Check ($maintenance.IndexOf('RecoverSubjugationResult(agenda)') -lt $maintenance.IndexOf('CourtSubjugationObjectiveSource.ValidPair')) 'Established clientage is recognized before client status invalidates eligibility.'
Check ($agenda.IndexOf('MaintainSubjugationObjectives();') -lt $agenda.IndexOf('OpenTerm(realm, manager, ideology);')) 'Old objectives settle before next-term selection.'
Check (!$behavior.Contains('MakePeace.Add') -and !$behavior.Contains('WarDeclared.Add')) 'Peace without clientage and enemy aggression do not prematurely settle the objective.'
Check ($behavior.Contains('TryClaimResult()') -and $behavior.Contains('agenda.ResultApplied = agenda.PaymentSettled = true')) 'Outcome receipts are sealed before mood changes.'
Check ($picker.Contains('agenda.Subjugation = prepared.Subjugation') -and $picker -match 'motion.Kind == CourtSubjugationRules.Kind[^\r\n]*\) && identity.HasTermSnapshot') 'Player confirmation retains the prepared plan and early session date.'
Check ((Read 'BellumCivileSaveDefiner.cs').Contains('AddClassDefinition(typeof(CourtSubjugationRecord), 95)') -and (Read 'CourtAgendaRecord.cs').Contains('[SaveableField(37)] public CourtSubjugationRecord Subjugation')) 'New save IDs append without reusing earlier fields.'
Check ($draft.Contains('BuildObjectiveDraft') -and $draft.Contains('score += preference.ConsiderationBonus') -and $draft.Contains('if (score >= 85f)')) 'Aligned demand consideration keeps the existing threshold.'
Check ($draft.Contains('client.WarScoreCost <= targetSpend') -and $draft.Contains('Math.Min(proposal.WarScoreBudget, desiredSpend)')) 'Objective alternative cannot invent budget or ignore spend limits.'
Check ($treaty.Contains('candidates.Add(objectiveDraft)') -and $treaty.Contains('TreatyAiDraftService.BuildDraft(')) 'Ordinary drafts remain available alongside the objective alternative.'
Check ($treaty.IndexOf('var courtPreference =') -lt $treaty.IndexOf('foreach (int spendTarget in spendTargets)')) 'Crown objective context is built once per negotiation pass.'
Check ($treaty.IndexOf('courtPreference?.Score(normalized)') -gt $treaty.IndexOf('proposal.ReplaceTerms(normalized)')) 'Package preference is evaluated against validated normalized terms.'
Check ($treaty.Contains('councilScore == bestScore && bestIsObjectiveAlternative && !candidate.IsObjectiveAlternative')) 'Ordinary drafts win exact score ties.'
Check ((Read 'CourtTreatyDraftPreference.cs').Contains('terms?.Any(Matches)')) 'Duplicate matching terms cannot multiply package preference.'
[xml]$xml = Read 'ModuleData/Languages/EN/strings.xml'
$texts = $behavior + $picker + (Read 'CourtAgendaPresentation.cs') + $council + (Read 'Patches/ForeignPolicyVoteAIPatch.cs')
$ids = [regex]::Matches($texts, '\{=(BC_[^}]*Clientage[^}]*)\}') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
foreach ($id in $ids) {
    $nodes = @($xml.SelectNodes("//string[@id='$id']"))
    Check ($nodes.Count -eq 1) "Unique clientage localization: $id"
    $fallback = [regex]::Match($texts, ('\{=' + [regex]::Escape($id) + '\}([^"\r\n]*)')).Groups[1].Value
    Check ($nodes[0].text -eq $fallback) "Matching clientage fallback: $id"
}
'Source/XML contracts passed. Native campaign, UI and save/load still require live tests.'
