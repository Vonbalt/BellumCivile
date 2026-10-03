$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$rules = Get-Content -Raw (Join-Path $root 'ElectiveAcceptance.cs')
$behavior = Get-Content -Raw (Join-Path $root 'Behaviors/ElectiveSuccessionBehavior.cs')
$vm = Get-Content -Raw (Join-Path $root 'UI/VanillaTabs/Kingdoms/Succession/ElectiveCandidateVM.cs')
$power = Get-Content -Raw (Join-Path $root 'RebellionPowerHelper.cs')
$xml = [xml](Get-Content -Raw (Join-Path $root 'GUI/Prefabs/KingdomManagement/Succession/BellumSuccessionPolicyPanel.xml'))
$strings = [xml](Get-Content -Raw (Join-Path $root 'ModuleData/Languages/EN/strings.xml'))
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
Check ($rules.Contains('Baseline = 100 - Math.Max(0, Math.Min(100, candidateSupport))') -and
    $rules.Contains('100 * record.Support(candidate) / totalWeight') -and $vm.Contains('score.Baseline')) 'Baseline uses candidate weighted support percentage in calculation and tooltip.'
Check ($vm.Contains('score.Honor') -and $vm.Contains('score.Mercy') -and
    $vm.Contains('if (score.PersonalityCap != 0)') -and !$vm.Contains('score.Personality);')) 'Honor and Mercy have separate rows with conditional combined-cap adjustment.'
Check ($rules.Contains('Leading ? 100') -and $rules.Contains('Hero winner = record.Winner;')) 'Leader acceptance follows the existing election winner, including tie-breaks.'
Check ($rules.Contains('CharacterRelationManager.GetHeroRelation(finalist, winner)')) 'Acceptance uses visible personal relation to projected winner.'
Check ($rules.Contains('GetRealmSovereignTitle(record.Realm)') -and $rules.Contains('FeudalClaimStrength.Strong') -and
    $rules.Contains('FeudalClaimStrength.Weak')) 'Only the strongest active claim to this sovereign title contributes.'
Check ($rules.Contains('ExpectedBackingClans(candidate, record.Votes)') -and
    $rules.Contains('expectedBackers, candidate.Clan, null, true, winner, expectedLoyalists')) 'Public endorsements seed expected power without recording armed pledges.'
Check ($rules.Contains('entry.Inputs.SequenceEqual(signature)') -and $rules.Contains('entry.Scores.TryGetValue') -and
    $behavior.Contains('Acceptance.Clear();')) 'Forecast cache guards inputs and is disposable on events and save/load.'
Check ($rules -notmatch 'MBRandom|DeclareWarAction|ChangeKingdomAction|ShowInquiry|SaveableField' -and
    $rules -notmatch 'RegisterEvents|DailyTickEvent') 'Forecast owns no scheduler, random roll, save state or conflict action.'
Check ($power.Contains('projectedSovereign == null ? CalculateClanPower(clan)') -and
    $power.Contains('committed.RemoveAll(clan => clan == rulingClan)')) 'Projected incumbent defeat does not remove the challenger; default power behavior is retained.'
Check ($power.Contains('projectedLoyalists?.Contains(clan) != true') -and
    $power.Contains('float effectiveChance = sponsorEntry.Value * assessment.JoinChance;')) 'Declared loyalist power cannot also support the challenger; extra hierarchy probabilities remain conditional.'
Check ($power.Contains('clan == Clan.PlayerClan && !declaredLoyalist') -and
    $rules.Contains('new HashSet<Clan>(votes.Where')) 'Player endorsements are counted; abstention remains uncommitted and duplicate houses are deduplicated.'
Check ($xml.SelectNodes('//*[@DataSource="{AcceptanceHint}"]').Count -eq 0 -and
    $xml.SelectNodes('//*[@Text="@AcceptanceText"]').Count -eq 1 -and
    $vm.Contains('new BasicTooltipViewModel(BuildCandidateTooltip)')) 'Acceptance shares the portrait tooltip; its summary row has no separate hover.'
Check ([regex]::Matches($vm, 'TooltipPropertyFlags.Title').Count -eq 1 -and
    $vm.Contains('BC_Election_SupportersSection') -and $vm.Contains('BC_Election_AcceptanceSection') -and
    !$vm.Contains('BC_Acceptance_Winner') -and !$vm.Contains('AcceptanceHint')) 'Unified tooltip has one candidate title, two sections and no redundant projected-winner row.'
Check (!$vm.Contains('BC_Acceptance_Forecast') -and $vm.Contains('Currently projected to win.')) 'Redundant forecast footer removed while leader explanation remains.'
Check ([regex]::Matches($vm, 'new TooltipProperty\(string.Empty, new TextObject\("\{=BC_Election_(Supporters|Acceptance)Section\}').Count -eq 2 -and
    !$vm.Contains('SectionColor')) 'Section headings occupy value-only rows, selecting native beige DescriptionTextBrush instead of the white definition brush.'
$ids = [regex]::Matches($vm, '\{=(BC_(?:Acceptance|Election)_[A-Za-z]+)\}') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
foreach ($id in $ids) { Check ($strings.SelectNodes("//string[@id='$id']").Count -eq 1) "Unique localized label: $id" }
'Source/XML contracts only; campaign forecasts, save/load and Gauntlet hover need in-game testing.'
