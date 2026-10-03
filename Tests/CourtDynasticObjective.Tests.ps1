$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -LiteralPath (Join-Path $root $path) -Raw }
function Check($value, $message) { if (!$value) { throw $message }; "PASS: $message" }
$behavior = Read 'Behaviors/CourtDynasticAgendas.cs'
$flow = Read 'Behaviors/CourtDynasticPlayerFlow.cs'
$source = Read 'CourtDynasticObjectiveSource.cs'
$selection = Read 'Behaviors/CourtDynasticMarriageSelection.cs'
$strategy = Read 'Behaviors/StrategicMarriageBehavior.cs'
$picker = Read 'Behaviors/CourtPlayerMotionPicker.cs'
$patches = Read 'Patches/CourtDynasticPatches.cs'
Check ($source.Contains('FactionType.Nobility') -and $source.Contains('Mood > -60') -and $source.Contains('MemberCount(o.Faction) >= 2')) 'Royal marriage respects ordinary Nobility agenda eligibility.'
Check ($source.Contains('d.IsAllowed() && d.CanMakeDecision(out _)') -and $source.Contains('AlliedKingdoms.Contains')) 'Discovery keeps native alliance viability and excludes existing alliances.'
Check ($source.Contains('HasCourtMarriageOpportunity') -and $strategy.Contains('CampaignTime.Now.ToDays + 1 <= deadline')) 'Court marriage opportunity depends on remaining time, not a spent annual random roll.'
Check ($strategy.IndexOf('PursueDynasticMarriage(clan, this)') -lt $strategy.IndexOf('if (!ShouldEvaluateToday(clan))')) 'Court marriage pursuit runs before the ordinary annual eligibility and chance gates.'
Check ($strategy.IndexOf('_lastHouseEvaluationYear[clan.StringId] = year') -ge 0 -or $strategy.Contains('_lastHouseEvaluationYear[clan.StringId] = CurrentDay / GetCampaignDaysInYear();')) 'Marriage opportunity receipt persists before the chance roll.'
Check ($selection.Contains('hero != hero.Clan.Leader') -and $selection.Contains('IsBloodRelative') -and $selection.Contains('GetPairRejectionReason')) 'Royal relatives retain ordinary legal and household marriage gates.'
Check ($selection.Contains('context, false') -and $selection.Contains('match.CandidateAcceptance >= BellumCivileConstants.MarriageStrategyMinimumScore')) 'Discovery forecasts aligned domestic consideration without bribing foreign acceptance.'
Check ($behavior.Contains('CourtAgendaState.Completed') -and $behavior.Contains('ReceivesPoliticalSupport(a, voter, a.Dynastic.Members)')) 'Completed weddings retain shared political support until the original term ends.'
Check ($behavior.Contains('GetFavoredBloc(a.Realm) == FactionType.Nobility') -and $behavior.Contains('member.Clan != Clan.PlayerClan') -and $selection.Contains('forecastBonus - existingBonus')) 'Only an aligned NPC Crown gets the marriage bonus, and discovery never doubles it.'
Check ($behavior.Contains('TryClaimResult()') -and $behavior.Contains('actual = a.Faction.Mood - before')) 'Outcome reports use one-shot receipts and actual capped mood.'
Check ($flow.Contains('CourtDynasticResponse.Deferred') -and $flow.Contains('RequestDynasticReview') -and $flow.Contains('_agendas.Contains(a)')) 'Deferral can resume and stale removed agendas cannot act.'
Check ($behavior.IndexOf('if (!BellumMarriageStrategyHelper.CourtMarriageParticipant(p.First)') -lt $behavior.IndexOf('p.Response = CourtDynasticResponse.ForeignRefused')) 'Temporary unavailability defers the foreign reply instead of fabricating refusal.'
Check ($strategy.Contains('OfferedPlayer.GetValue(offers) == hero') -and $strategy.Contains('offers.IsHeroEngaged(hero)') -and $strategy.Contains('LastNativeOffer')) 'Custom approaches respect active native offers, engagement and cooldown.'
Check ($patches.Contains('nameof(MarriageAction.Apply)') -and !$patches.Contains('OnBeforeHeroesMarried')) 'Success is certified after the actual marriage action.'
Check ($picker.Contains('agenda.Dynastic = prepared.Dynastic') -and $picker -match 'motion.Kind == CourtDynasticRules.Kind[^\r\n]*\) && identity.HasTermSnapshot') 'Substitution preserves exact couple and early session.'
Check ((Read 'BellumCivileSaveDefiner.cs').Contains('AddClassDefinition(typeof(CourtDynasticRecord), 97)') -and (Read 'CourtAgendaRecord.cs').Contains('[SaveableField(39)] public CourtDynasticRecord Dynastic')) 'Royal marriage save IDs append to existing fields.'
[xml]$prefab = Read 'GUI/Prefabs/KingdomManagement/Factions/BellumFactionsPanel.xml'
$buttons = @($prefab.SelectNodes('//*[@Command.Click="ExecuteReviewMarriage"]'))
Check ($buttons.Count -eq 1 -and $buttons[0].SelectSingleNode('ancestor::*[@DataSource="{SelectedFaction}"]') -ne $null) 'Resume control belongs to the faction panel, not the Crown datasource.'
[xml]$xml = Read 'ModuleData/Languages/EN/strings.xml'
$texts = $behavior + $flow + $picker + $patches + (Read 'CourtAgendaPresentation.cs') + (Read 'UI/FactionItemVM.cs')
$ids = [regex]::Matches($texts, '\{=(BC_[^}]*Dynastic[^}]*)\}') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
foreach ($id in $ids) {
    $nodes = @($xml.SelectNodes("//string[@id='$id']"))
    Check ($nodes.Count -eq 1) "Unique royal marriage localization: $id"
    $fallback = [regex]::Match($texts, ('\{=' + [regex]::Escape($id) + '\}([^"\r\n]*)')).Groups[1].Value.Replace('\n', "`n")
    Check ($nodes[0].text -ceq $fallback) "Matching royal marriage fallback: $id"
}
'Royal marriage source/XML contracts passed; native UI and save/load require live testing.'
