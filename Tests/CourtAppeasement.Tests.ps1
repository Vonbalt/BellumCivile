$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$source = Read 'CourtAppeasementObjectiveSource.cs'
$execution = Read 'Behaviors/CourtAppeasementAgendas.cs'
$faction = Read 'FactionObject.cs'
$save = Read 'BellumCivileSaveDefiner.cs'
$record = Read 'CourtAppeasementRecord.cs'
Check ($source.Contains('owner.Faction != null || owner.Sponsor != context.Realm.RulingClan')) 'Only Crown sponsors can propose appeasement.'
Check ($source.Contains('target.Mood > -40')) 'Eligibility includes exactly -40.'
Check ($source.Contains('target.CrownAccommodation?.Active == true') -and $source.Contains('target.CrownAccommodation?.TermEnd.IsFuture == true')) 'Active accommodations and same-term repeat purchases are excluded.'
Check ($source.Contains('f.IsUltimatumPending || f.IsCivilWarActive() || f.HasTrackedRebelKingdom')) 'Issued coalitions cannot be bought off.'
Check ($source.Contains('agenda.HasScheduleSnapshot ? agenda.TermDays')) 'Effect duration uses the saved term, not later MCM changes.'
Check ($execution.Contains('Cost(MemberCount(plan.Target)) != plan.QuotedCost')) 'Changed membership price withdraws instead of repricing.'
Check ($execution.IndexOf('!plan.TryBegin()') -lt $execution.IndexOf('!TryPay(agenda, plan.QuotedCost)')) 'Attempt receipt is sealed before payment.'
Check ($execution.IndexOf('plan.Target.CrownAccommodation = plan;') -lt $execution.IndexOf('plan.Applied = true;')) 'Applied receipt is written only after the modifier is attached.'
Check (!$execution.Contains('FinishExecutive(') -and !$execution.Contains('ApplyMoodShock')) 'Appeasement grants no additional ordinary agenda mood reward.'
Check ($faction.Contains('CourtAppeasementRules.SetEffective(_mood, value, AccommodationBonus)')) 'All existing mood mutations use the effective-mood adapter.'
Check ((Read 'Behaviors/IdeologyBehavior.cs').Contains('faction.UnderlyingMood < baseThreshold')) 'Daily drift remains on underlying mood, not the temporary allowance.'
Check ((Read 'Behaviors/CourtAgendaBehavior.cs').Contains('CampaignEvents.RulingClanChanged.AddNonSerializedListener')) 'Ruler replacement permanently invalidates the old allowance.'
Check ($record.Contains('Until.IsFuture') -and $record.Contains('!Ended && SameRuler')) 'Expiry and ruler identity are checked at read time.'
Check ($save.Contains('AddClassDefinition(typeof(CourtAppeasementRecord), 92)')) 'Accommodation record is save-registered.'
$ids = @([regex]::Matches($save, 'Add(?:Class|Enum)Definition\(typeof\([^)]+\), (\d+)\)') | ForEach-Object { $_.Groups[1].Value })
Check (@($ids | Sort-Object -Unique).Count -eq $ids.Count) 'Class and enum save IDs remain globally unique.'
Check ($faction.Contains('[SaveableField(36)] internal CourtAppeasementRecord CrownAccommodation;')) 'Active record survives removal of its completed agenda.'
Check ((Read 'UI/FactionsWindowVM.cs').Contains('selectedBackendFaction.CrownAccommodation.Until.ToString()')) 'Mood tooltip names the temporary modifier and expiry.'
[xml]$xml = Read 'ModuleData/Languages/EN/strings.xml'
foreach ($file in @('Behaviors/CourtAppeasementAgendas.cs', 'Behaviors/CourtPlayerMotionPicker.cs', 'UI/FactionsWindowVM.cs')) {
    foreach ($match in [regex]::Matches((Read $file), '\{=(BC_Crown(?:Reconcile\w+|UnderlyingMood|Accommodation\w+))\}([^"\r\n]*)')) {
        $id = $match.Groups[1].Value
        $entry = @($xml.base.strings.string | Where-Object id -eq $id)
        Check ($entry.Count -eq 1 -and $entry[0].text -ceq $match.Groups[2].Value.Replace('\n', "`n")) "Localization matches: $id"
    }
}
'Source/XML contracts only; live payments, modifier serialization and rendered UI still require campaign testing.'
