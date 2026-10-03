$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
function Read($p) { Get-Content -Raw -LiteralPath (Join-Path $root $p) }
function Check($v,$message) { if (!$v) {throw $message}; "PASS: $message" }
$behavior=Read 'Behaviors/CourtTradeAgendas.cs'
$source=Read 'CourtTradeObjectiveSource.cs'
$patch=Read 'Patches/CourtTradePatches.cs'
$picker=Read 'Behaviors/CourtPlayerMotionPicker.cs'
$agenda=Read 'Behaviors/CourtAgendaBehavior.cs'
$pursuit=Read 'Behaviors/CourtTradeProposals.cs'
Check ($source.Contains('FactionType.Liberty') -and $source.Contains('Mood > -60') -and $source.Contains('MemberCount(o.Faction) >= 2')) 'Trade selection respects Liberty eligibility.'
Check ($source.Contains('Legal(context.Realm, target, true)') -and $source.Contains('model.CanMakeTradeAgreement(target, realm, false')) 'Trade selection checks foreign support and both directions of legality.'
Check ($agenda.Contains('selector.Register(new CourtTradeObjectiveSource(), "foreign_affairs")')) 'Trade shares the existing foreign category.'
Check ($patch.Contains('GetRandomTradeAgreementDecision') -and $patch.Contains('Consider.Invoke') -and $patch.Contains('NpcInfluenceBudgetService.CanAfford')) 'Trade preference preserves native opportunity, forecast and influence budget.'
Check (!$patch.Contains('MakeTradeAgreement(') -and !$behavior.Contains('MakeTradeAgreement(') -and !$pursuit.Contains('.MakeTradeAgreement(')) 'Objective cannot create free agreements.'
Check ($pursuit.Contains('ConsiderCourtTrade.Invoke') -and $pursuit.Contains('NpcInfluenceBudgetService.TrySpend') -and $pursuit.Contains('ProposalAttempted') -and $pursuit.Contains('decision.TriggerTime.ToDays > a.ObjectiveData.DeadlineDay')) 'Agenda-driven proposal retains native willingness, payment, one-vote receipt and deadline.'
Check ($source.Contains('c.ManualSelection || ProspectiveSponsor') -and $source.Contains('TradeRepeatWeight')) 'NPC selection checks funded willingness and repetition while manual choices retain agency.'
Check ($behavior.Contains('ReceivesPoliticalSupport(a, voter, a.Trade.Members)') -and $behavior.Contains('voter == Clan.PlayerClan')) 'Bonus preserves common membership and player agency rules.'
Check ($agenda.Contains('OnTradeAgreementSignedEvent') -and $behavior.Contains('!TradeExists(a)')) 'Signing receipt verifies actual agreement.'
Check ($behavior.Contains('saved_signature_recovered') -and $behavior.Contains('active_trade_observed_in_term')) 'Recovery distinguishes saved timing from late observation.'
Check ($behavior.Contains('TryClaimResult()') -and $behavior.Contains('actual = a.Faction.Mood - before')) 'Approval is one-shot and capped.'
Check ($behavior.Contains('CausedByKingdomDecision') -and $behavior.Contains('CausedByPlayerHostility')) 'Offensive failure requires deliberate war provenance.'
Check ($picker.Contains('agenda.Trade = prepared.Trade') -and $picker -match 'CourtTradeRules.Kind[^\r\n]*identity.HasTermSnapshot') 'Player substitution preserves target and early session.'
Check ((Read 'BellumCivileSaveDefiner.cs').Contains('AddClassDefinition(typeof(CourtTradeRecord), 101)') -and (Read 'CourtAgendaRecord.cs').Contains('[SaveableField(41)] public CourtTradeRecord Trade')) 'Trade save IDs append without replacement.'
[xml]$xml=Read 'ModuleData/Languages/EN/strings.xml'
$texts=$behavior+$patch+$picker+(Read 'CourtAgendaPresentation.cs')
$ids=[regex]::Matches($texts,'\{=(BC_CourtTrade[^}]+)\}') | ForEach-Object {$_.Groups[1].Value} | Sort-Object -Unique
foreach ($id in $ids) {
    $nodes=@($xml.SelectNodes("//string[@id='$id']"))
    Check ($nodes.Count -eq 1) "Unique trade localization: $id"
    $fallback=[regex]::Match($texts,('\{='+[regex]::Escape($id)+'\}([^"\r\n]*)')).Groups[1].Value.Replace('\n',"`n")
    Check ($nodes[0].text -ceq $fallback) "Matching trade fallback: $id"
}
'Trade source/XML contracts passed; native campaign, UI and save/load remain to test.'
