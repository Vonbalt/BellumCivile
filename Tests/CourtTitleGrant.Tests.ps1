$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
function Read($p) { Get-Content -Raw -LiteralPath (Join-Path $root $p) }
function Check($v,$message) { if (!$v) {throw $message}; "PASS: $message" }
$behavior=Read 'Behaviors/CourtTitleGrantAgendas.cs'
$source=Read 'CourtTitleGrantObjectiveSource.cs'
$flow=Read 'Behaviors/CourtTitleGrantPlayerFlow.cs'
$picker=Read 'Behaviors/CourtPlayerMotionPicker.cs'
$backend=Read 'Behaviors/FeudalTitleBehavior.cs'
$agenda=Read 'Behaviors/CourtAgendaBehavior.cs'
$patch=Read 'Patches/CourtTitleGrantPatches.cs'
Check ($source.Contains('title.TitleType >= sovereign.TitleType') -and $source.Contains('title.TitleType <= FeudalTitleType.Barony')) 'Title grants cover higher ranks strictly below the sovereign.'
Check ($source.Contains('HasActiveClaim(recipient, title)') -and $source.Contains('GetChildTitles(title, FeudalHierarchyMode.DeFacto)') -and $source.Contains('GetChildTitles(title, FeudalHierarchyMode.DeJure)')) 'Claim and immediate subordinate title required in either hierarchy.'
Check ($source.Contains('c.TitleGrantCandidates') -and $source.Contains('c.ManualSelection || score >= 60')) 'Per-pass title discovery and player bypass of NPC willingness retained.'
Check ($source.Contains('FactionType.Nobility') -and $source.Contains('Mood > -60') -and $source.Contains('MemberCount(o.Faction) >= 2')) 'Nobility petitions use normal faction eligibility.'
Check ($agenda.Contains('new CourtTitleGrantObjectiveSource(CourtTitleGrantRules.Grant), "higher_titles"') -and $agenda.Contains('new CourtTitleGrantObjectiveSource(CourtTitleGrantRules.Petition), "higher_titles"')) 'Both sources share a weighted family.'
Check (!$behavior.Contains('TryExecuteGrant(') -and !$behavior.Contains('ApplyGrantMoodFallout(')) 'Court transaction cannot duplicate ordinary grant costs or jealousy.'
Check ($behavior.Contains('p.RelationApplied = true') -and $behavior.Contains('p.MoodApplied = true') -and $behavior.Contains('p.ApprovalGranted = p.RewardFaction.Mood - before')) 'Sealed rewards retain actual capped approval.'
Check ($behavior.Contains('RelationMemorySources.CourtTitleGrant, 10') -and $behavior.Contains('RelationMemoryScope.Personal')) 'Court grant uses a named personal memory and standard duration machinery.'
Check ($behavior.Contains('p.Attempts >= 3') -and $behavior.Contains('p.RecoveryUntil = now + 1') -and $behavior.Contains('p.RetryDay = now + .25')) 'Interrupted delivery is bounded to three attempts within one day.'
Check ($behavior.Contains('!p.PaymentAttempted && !_agendas.Any') -and $behavior.Contains('p.Refunded = true')) 'Paid recovery survives agenda cleanup and refund is sealed.'
Check ($backend.Contains('CompleteCourtTitleGrant(CourtTitleGrantRecord plan') -and $backend.Contains('CourtTitleGrantRules.Recoverable')) 'Lower-level repair checks exact original or delivered rights.'
Check ($patch.Contains('if (__result)') -and $patch.Contains('ObserveHierarchyTitleGrant')) 'Ordinary successful grants fulfill matching motions without changing ordinary rewards.'
Check ($flow.Contains('new InquiryElement("grant"') -and $flow.Contains('new InquiryElement("refuse"') -and $flow.Contains('new InquiryElement("defer"')) 'Petition offers grant, refuse and defer.'
Check ($flow.Contains('!PlayerTitlePetition(a)') -and $flow.Contains('!GrantUnchanged(p)') -and $flow.Contains('selected.Count != 1')) 'Player callback revalidates authority, rights and selection.'
Check ($picker.Contains('agenda.TitleGrant = prepared.TitleGrant') -and $picker.Contains('CourtTitleGrantRules.Petition) && identity.HasTermSnapshot')) 'Substitution copies the title plan and early term schedule.'
Check ($agenda.Contains('BC_CourtTitleDeliveries') -and (Read 'BellumCivileSaveDefiner.cs').Contains('AddClassDefinition(typeof(CourtTitleGrantRecord), 102)')) 'Delivery journal is saved under appended identifiers.'
[xml]$ui=Read 'GUI/Prefabs/KingdomManagement/Factions/BellumFactionsPanel.xml'
Check ($null -ne $ui.SelectSingleNode('//*[@Command.Click="ExecuteReviewTitlePetition"]')) 'Faction panel exposes deferred petition review.'
[xml]$xml=Read 'ModuleData/Languages/EN/strings.xml'
$texts=$behavior+$flow+$picker+(Read 'CourtAgendaPresentation.cs')+(Read 'UI/FactionItemVM.cs')+(Read 'RelationMemoryService.cs')
$ids=[regex]::Matches($texts,'\{=(BC_TitleGrant[^}]+)\}') | ForEach-Object {$_.Groups[1].Value} | Sort-Object -Unique
foreach ($id in $ids) {
    $nodes=@($xml.SelectNodes("//string[@id='$id']"))
    Check ($nodes.Count -eq 1) "Unique title localization: $id"
    $fallback=[regex]::Match($texts,('\{='+[regex]::Escape($id)+'\}([^"\r\n]*)')).Groups[1].Value.Replace('\n',"`n")
    Check ($nodes[0].text -ceq $fallback) "Matching title fallback: $id"
}
'Title source/XML contracts passed; live campaign, UI and native save/load remain to test.'
