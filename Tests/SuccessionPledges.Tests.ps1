$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$appeal = Read 'Behaviors/SuccessionChallengePledges.cs'
$owner = Read 'Behaviors/SuccessionChallengeBehavior.cs'
$solidarity = Read 'CivilWarSolidarityHelper.cs'
$tribunal = Read 'Behaviors/CivilWarResolutionBehavior.cs'
$strings = [xml](Read 'ModuleData/Languages/EN/strings.xml')
Check ($appeal.Contains('if (record.PledgesCaptured) return true') -and $appeal.Contains('record.PledgesCaptured = true')) 'Pledge probabilities and rolls are captured once per appeal.'
Check ($appeal.Contains('CivilWarSolidarityHelper.AssessSupport(house, sponsor,') -and $appeal.Contains('IsImmediateVassalOf')) 'Direct-vassal pledges retain shared solidarity obligations.'
Check ($appeal.Contains('HereditaryAllegiance.Chance(intent,') -and
    $appeal.Contains('CharacterRelationManager.GetHeroRelation(house.Leader, record.Challenger)') -and
    $appeal.Contains('CharacterRelationManager.GetHeroRelation(house.Leader, record.Sovereign)') -and
    $appeal -notmatch 'MeetsOutsiderIntentRequirement|CanAnswerOutsiderCall|personalCall') 'Every eligible house assesses both royals personally without the old outsider gate.'
Check ($appeal.Contains('HasRoyalMarriage(house, record.Challenger), HasRoyalMarriage(house, record.Sovereign)') -and
    $appeal.Contains('h.Spouse?.IsAlive == true') -and !$appeal.Contains('HasMarriageAlliance')) 'Marriage comparison uses living personal family ties, not blanket shared-clan alliances.'
Check ([regex]::Matches($appeal, 'MBRandom.RandomFloat').Count -eq 1 -and
    $appeal.Contains('manager.IsClanPacified(house)') -and $appeal.Contains('IsCivilWarActive() == true')) 'One saved roll and existing pacification/active-war exclusions remain intact.'
Check ($appeal.Contains('RebellionPowerHelper.CalculateClanPower(house)') -and
    $appeal.Contains('pledge.Choice = SuccessionPledgeChoice.Loyal') -and !$appeal.Contains('EstimatedStrength')) 'Royal household stays loyal; no fictional transferred army is added to the dependent heir.'
Check ($appeal.Contains('InformationManager.IsAnyInquiryActive()') -and $appeal.Contains('SuccessionPledgeChoice.AwaitingPlayer') -and
    $appeal.Contains('PledgeParticipantsUnchanged')) 'Player prompt is exclusive, saved and revalidated before pledge resolution.'
Check ($appeal.Contains('p.Clan.Leader == p.Speaker') -and $appeal.Contains('current.Count == record.Pledges.Count')) 'Changed leaders and realm membership invalidate an unsealed appeal.'
Check ($appeal -notmatch 'DeclareWarAction|CreateKingdom\(|RallyAiSupporters\(|TryBegin\(') 'Pledge subpass neither starts new appeals nor re-recruits factions nor dispatches unfinished wars.'
Check ([regex]::Matches($tribunal, 'RoyalHeirTribunalRules.ExecutionChance\(').Count -eq 2 -and
    $tribunal.Contains('HereditaryRealmSuccession.OrderLine(new[] { defeated }, victor')) 'Immediate and deferred AI tribunals recognize lawful blood heirs even in a rebel shell.'
$eligibility = $tribunal.Substring($tribunal.IndexOf('private static bool IsDefeatedRoyalHeir'),
    $tribunal.IndexOf('private void ApplyTribunalVerdict') - $tribunal.IndexOf('private static bool IsDefeatedRoyalHeir'))
Check ($eligibility.Contains('realm.RulingClan?.Leader != victor') -and $eligibility.Contains('IsHereditaryRealm') -and
    !$eligibility.Contains('ExileCause.Treason') -and !$eligibility.Contains('ExileCause.Loyalist')) 'Leniency applies only to defending hereditary rulers judging defeated rebels, not general treason or victorious usurpers.'
$start = $tribunal.IndexOf('private void ApplyTribunalVerdict')
$verdict = $tribunal.Substring($start, $tribunal.IndexOf('private ', $start + 1) - $start)
Check ($verdict.Contains('case TribunalVerdict.Execute:') -and $verdict.Contains('case TribunalVerdict.Punish:') -and
    $verdict.Contains('case TribunalVerdict.Pardon:') -and !$verdict.Contains('RoyalHeirTribunalRules')) 'Player tribunal retains all three verdicts without an AI override.'
$wording = Read 'Behaviors/SuccessionChallengeText.cs'
Check ($owner.Contains('record.FallbackGrant = estate.EndowmentFiefs.Count == 0') -and
    $appeal.Contains('DemandMessage(choice, false)') -and (Read 'Behaviors/SuccessionChallengeTesting.cs').Contains('DemandMessage(record, true)')) 'Demand classification exists before the pledge popup and both audiences share the demand selector.'
Check ($wording.Contains('PowerDescription(record.BackingPower, record.LoyalistPower)') -and
    $wording -notmatch 'RequiredRatio|ToString\("0\.#"\)|\{REBEL\}|\{LOYAL\}|\{RATIO\}') 'Narrative power summary does not expose raw backing or acceptance thresholds.'
$ids = [regex]::Matches(($appeal + $wording), '\{=(BC_Challenge_[A-Za-z]+)\}') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
foreach ($id in $ids) { Check ($strings.SelectNodes("//string[@id='$id']").Count -eq 1) "Localized challenge label: $id" }
'Source contracts only; live inquiries, save/load and tribunals require campaign verification.'
$pledgeUi = Get-Content -Raw (Join-Path (Split-Path $PSScriptRoot -Parent) 'Behaviors/SuccessionChallengePledges.cs')
if (!$pledgeUi.Contains('r.ReportPending && r.Realm?.RulingClan == Clan.PlayerClan') -or
    !$pledgeUi.Contains('pending.ReportPending = false;') -or
    $pledgeUi.IndexOf('pending.ReportPending = false;') -gt $pledgeUi.IndexOf('var report = _records.FirstOrDefault')) {
    throw 'Ruler backing reports must be drained before choosing an inquiry, including saved reports.'
}
if (!$pledgeUi.Contains('r.ReportPending && r.Realm == Clan.PlayerClan?.Kingdom') -or !$pledgeUi.Contains('ShowRulerResponse();')) {
    throw 'Vassal reports and the actual ruler ultimatum must remain available.'
}
'PASS: Rulers skip backing reports while vassal reports and ultimatum routing remain intact.'
