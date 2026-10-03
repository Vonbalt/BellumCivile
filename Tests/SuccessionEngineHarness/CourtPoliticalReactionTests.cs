using System;
using System.Runtime.Serialization;
using BellumCivile.Patches;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;

internal static class CourtPoliticalReactionTests
{
    internal static void Run(Action<bool, string> check)
    {
        var decision = (SettlementClaimantDecision)FormatterServices.GetUninitializedObject(typeof(SettlementClaimantDecision));
        var other = (Settlement)FormatterServices.GetUninitializedObject(typeof(Settlement));
        var owns = AccessTools.Method(typeof(FiefVoteResolutionPatch), "OwnsAward");
        bool Owns(Settlement settlement) => (bool)owns.Invoke(null, new object[] { settlement });
        try
        {
            check(!Owns(null), "No ballot award scope before entry");
            FiefVoteResolutionPatch.Prefix(decision);
            check(Owns(null), "Ballot owns its settlement callbacks during native execution");
            check(!Owns(other), "Ballot does not suppress another settlement's callbacks");
            FiefVoteResolutionPatch.Prefix(decision);
            var error = new InvalidOperationException("fixture");
            check(ReferenceEquals(error, FiefVoteResolutionPatch.Finalizer(decision, error)), "Finalizer preserves native exception");
            check(Owns(null), "Nested finalizer preserves outer award scope");
            check(FiefVoteResolutionPatch.Finalizer(decision, null) == null && !Owns(null), "Finalizer releases last scope");
            FiefVoteResolutionPatch.Finalizer(decision, null);
            check(!Owns(null), "Repeated finalizer leaves no leaked scope");
            FiefVoteResolutionPatch.Postfix(null, null);
            FiefVoteResolutionPatch.Postfix(decision, null);
            check(true, "Invalid ballot aftermath is harmless");
        }
        finally { FiefVoteResolutionPatch.Finalizer(decision, null); FiefVoteResolutionPatch.Finalizer(decision, null); }
    }
}
