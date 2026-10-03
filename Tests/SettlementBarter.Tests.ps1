$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Behaviors/FeudalTitleBehavior.cs') -Raw
$start = $source.IndexOf('        private static bool ShouldBarterTransferLegalTitle(')
$end = $source.IndexOf('        private bool ShouldLegalTitleTransferToHolder(', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Barter rule not found.' }
$rule = $source.Substring($start, $end - $start)
$testSource = @'
using System;
public class Clan { public string StringId; }
public static class SettlementBarterTests
{
    public static void Run()
    {
        Clan seller = new Clan { StringId = "seller" };
        Clan buyer = new Clan { StringId = "buyer" };
        Check(ShouldBarterTransferLegalTitle("seller", seller, buyer), "Seller's lawful ownership must transfer");
        Check(ShouldBarterTransferLegalTitle("buyer", seller, buyer), "Buyer's lawful ownership must remain");
        Check(!ShouldBarterTransferLegalTitle("third_party", seller, buyer), "Third-party ownership must remain");
        Check(!ShouldBarterTransferLegalTitle(null, seller, buyer), "Missing ownership must not be invented");
        Check(!ShouldBarterTransferLegalTitle("seller", null, buyer), "Unknown seller cannot convey ownership");
        Check(!ShouldBarterTransferLegalTitle("seller", seller, null), "Missing buyer accepted");
        Check(!ShouldBarterTransferLegalTitle("seller", seller, new Clan()), "Unidentified buyer accepted");
        Check(ShouldBarterTransferLegalTitle("buyer", buyer, seller), "Reverse trade must follow the same rule");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
'@
Add-Type -TypeDefinition ($testSource + $rule + '}')
[SettlementBarterTests]::Run()

# Check the event wiring as well as the executable ownership rule.
$handler = $source.Substring($source.IndexOf('        private void OnSettlementOwnerChanged('))
$handler = $handler.Substring(0, $handler.IndexOf('        private bool TryApplySubinfeudationOwnershipChange('))
if ($handler.IndexOf('string previousDeJure =') -gt $handler.IndexOf('FeudalTitleRecord title = EnsureBaronyTitle')) {
    throw 'Barter rights captured after synchronization.'
}
if ($handler -notmatch 'if \(!isBarter\)\s*\{\s*UpdateBaronyParentToCurrentKingdom\(title, newClan\);\s*if \(!wasAllocationCustody \|\| previousDeJure != previousDeFacto\)\s*RegisterDisplacedLegalOwnerClaimIfNeeded') {
    throw 'Barter must not create displaced-owner claims or move the legal parent.'
}
if ($handler -notmatch 'if \(isBarter && newClan != null && sellerClan != null && sellerClan != newClan\)\s*DeactivateClaimsForTitle\(sellerClan, title\);') {
    throw 'Seller claims must be relinquished only for the traded title.'
}
if ($handler -notmatch 'bool legalTransfer = isBarter\s*\? ShouldBarterTransferLegalTitle') {
    throw 'Barter falls through to generic claim-based legal transfer.'
}
Write-Output 'Settlement barter tests passed: eight ownership cases and event-wiring checks.'
