$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (Join-Path $root 'Behaviors/HouseholdRansomBehavior.cs') -Raw
$strings = [xml](Get-Content -LiteralPath (Join-Path $root 'ModuleData/Languages/EN/strings.xml') -Raw)
function Check($condition, $message) {
    if (!$condition) { throw $message }
    Write-Output "PASS: $message"
}

Check ($source -match '(?s)"bc_household_ransom_available"\s*,\s*"bc_household_ransom_response"\s*,\s*"bc_household_ransom_result"') 'The broker keeps the conversation open for the inquiry result.'
Check ($source -match '(?s)"bc_household_ransom_hostage_refusal"\s*,\s*"bc_household_ransom_result"\s*,\s*"bc_household_ransom_understood"') 'Protected hostages receive a broker dialogue response.'
Check ($source -match '(?s)"bc_household_ransom_understood"\s*,\s*"bc_household_ransom_understood"\s*,\s*"ransom_broker_pretalk"') 'The acknowledgement returns to the native broker branch.'
Check (([regex]::Matches($source, '"bc_household_ransom_understood"\s*,\s*"bc_household_ransom_understood"')).Count -eq 1) 'The refusal has one acknowledgement choice.'
Check ($source.Contains('_brokerRefusal = HostagePactText.BrokerRefusal(pact, prisoner)') -and
    !$source.Contains('InformationManager.ShowInquiry(')) 'The existing treaty-specific text is shown in dialogue, not a notification popup.'
Check ($source.Contains('_ => ResumeBrokerConversation()') -and $source.Contains('conversation.ContinueConversation()')) 'Both confirmation and cancellation resume the conversation.'
Check ($source -match '(?s)if \(quote.IsTreatyHostage\).*?return !TryOpenHouseholdRansomInquiry\(\)' -and
    $source -match '(?s)HostageCustodyGuard.IsProtected\(prisoner\).*?GiveGoldAction.ApplyBetweenCharacters') 'Stale hostage quotes are rebuilt and protected prisoners cannot be ransomed.'
foreach ($id in @('BC_HouseholdRansom_BrokerRefusal', 'BC_HouseholdRansom_Finished', 'BC_Hostage_BrokerUnderstood')) {
    Check ($strings.SelectNodes("//string[@id='$id']").Count -eq 1) "The $id dialogue text is localized."
}
Write-Output 'Source and localization checks only; the live conversation still needs an in-game test.'
