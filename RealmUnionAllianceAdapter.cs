using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile
{
    internal static class RealmUnionAllianceAdapter
    {
        private static readonly Type Alliance = typeof(AllianceCampaignBehavior).GetNestedType("Alliance", BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo First = Alliance == null ? null : AccessTools.Field(Alliance, "Kingdom1");
        private static readonly FieldInfo Second = Alliance == null ? null : AccessTools.Field(Alliance, "Kingdom2");
        private static readonly FieldInfo End = Alliance == null ? null : AccessTools.Field(Alliance, "EndTime");
        private static readonly ConstructorInfo Constructor = Alliance == null ? null : AccessTools.Constructor(Alliance,
            new[] { typeof(Kingdom), typeof(Kingdom), typeof(CampaignTime) });

        internal static bool CanTransferCalls(IAllianceCampaignBehavior behavior, Kingdom source, Kingdom destination, out string reason)
        {
            reason = "unsupported alliance call-to-war storage";
            if (behavior?.GetType() != typeof(AllianceCampaignBehavior) || source == null || destination == null || source == destination) return false;
            var field = AccessTools.Field(typeof(AllianceCampaignBehavior), "_callToWarAgreements");
            var type = typeof(AllianceCampaignBehavior).GetNestedType("CallToWarAgreement", BindingFlags.Public | BindingFlags.NonPublic);
            if (type == null || field?.FieldType != typeof(List<>).MakeGenericType(type)
                || !(field.GetValue(behavior) is IList calls)) return false;
            var roles = new[] { AccessTools.Field(type, "CallingKingdom"), AccessTools.Field(type, "CalledKingdom"),
                AccessTools.Field(type, "KingdomToCallToWarAgainst") };
            foreach (var role in roles) if (role?.FieldType != typeof(Kingdom)) return false;
            foreach (object call in calls)
                foreach (var role in roles)
                {
                    var realm = (Kingdom)role.GetValue(call);
                    if (realm == source || realm == destination)
                    { reason = "call-to-war commitment needs separate absorption handling"; return false; }
                }
            reason = null;
            return true;
        }

        internal static bool TryPrepare(IList original, Kingdom source, Kingdom destination, Func<Kingdom, bool> conflicts,
            out IList replacement, out HashSet<Kingdom> affected, out string reason)
        {
            replacement = null;
            affected = null;
            reason = "unsupported alliance storage or incomplete inheritance input";
            if (Alliance == null || First?.FieldType != typeof(Kingdom) || Second?.FieldType != typeof(Kingdom)
                || End?.FieldType != typeof(CampaignTime) || Constructor == null || original == null
                || original.GetType() != typeof(List<>).MakeGenericType(Alliance)
                || source == null || destination == null || source == destination || conflicts == null) return false;
            var result = (IList)Activator.CreateInstance(original.GetType());
            var partners = new HashSet<Kingdom>();
            var touched = new HashSet<Kingdom> { source, destination };
            foreach (object entry in original) result.Add(entry);
            foreach (object entry in original)
            {
                var first = (Kingdom)First.GetValue(entry);
                var second = (Kingdom)Second.GetValue(entry);
                if (first != source && second != source) continue;
                Kingdom partner = first == source ? second : first;
                if (partner == null || partner == source || partner == destination || !partners.Add(partner) || conflicts(partner))
                { reason = "alliance partner is duplicated, internal to the union or incompatible"; return false; }
                var expiry = (CampaignTime)End.GetValue(entry);
                int matches = 0;
                foreach (object existing in original)
                    if (Pair(existing, destination, partner))
                    {
                        if (++matches > 1) { reason = "destination has duplicate alliances"; return false; }
                        var retained = (CampaignTime)End.GetValue(existing);
                        if (retained > expiry) expiry = retained;
                    }
                for (int i = result.Count - 1; i >= 0; i--)
                    if (Pair(result[i], source, partner) || Pair(result[i], destination, partner)) result.RemoveAt(i);
                result.Add(Constructor.Invoke(new object[] { destination, partner, expiry }));
                touched.Add(partner);
            }
            replacement = result;
            affected = touched;
            reason = null;
            return true;
        }

        private static bool Pair(object entry, Kingdom first, Kingdom second) =>
            (Kingdom)First.GetValue(entry) == first && (Kingdom)Second.GetValue(entry) == second
            || (Kingdom)First.GetValue(entry) == second && (Kingdom)Second.GetValue(entry) == first;

        // Starting a fresh alliance would clear tribute and dispatch new alliance/call-to-war
        // events. Replace the native records and refresh caches without renegotiating the pact.
        internal static bool CanApply(IAllianceCampaignBehavior behavior, Kingdom source, Kingdom destination,
            Func<Kingdom, bool> conflicts, out string reason)
        {
            if (!CanTransferCalls(behavior, source, destination, out reason)) return false;
            var field = AccessTools.Field(typeof(AllianceCampaignBehavior), "_alliances");
            if (Alliance == null || field?.FieldType != typeof(List<>).MakeGenericType(Alliance)
                || AccessTools.Method(typeof(Kingdom), "UpdateAlliedKingdoms", Type.EmptyTypes) == null
                || !(field.GetValue(behavior) is IList original))
            { reason = "unsupported native alliance storage or cache refresh"; return false; }
            return TryPrepare(original, source, destination, conflicts, out _, out _, out reason);
        }

        internal static bool TryApply(IAllianceCampaignBehavior behavior, Kingdom source, Kingdom destination,
            Func<Kingdom, bool> conflicts, Action beforeWrite, Action returned, out string reason)
        {
            reason = "alliance inheritance callbacks are unavailable";
            if (beforeWrite == null || returned == null) return false;
            if (!CanTransferCalls(behavior, source, destination, out reason)) return false;
            var field = AccessTools.Field(typeof(AllianceCampaignBehavior), "_alliances");
            var refresh = AccessTools.Method(typeof(Kingdom), "UpdateAlliedKingdoms", Type.EmptyTypes);
            if (field?.FieldType != (Alliance == null ? null : typeof(List<>).MakeGenericType(Alliance)) || refresh == null
                || !(field.GetValue(behavior) is IList original))
            { reason = "unsupported native alliance storage or cache refresh"; return false; }
            if (!TryPrepare(original, source, destination, conflicts, out var replacement, out var affected, out reason)) return false;
            beforeWrite();
            field.SetValue(behavior, replacement);
            foreach (var realm in affected) refresh.Invoke(realm, null);
            returned();
            return true;
        }
    }
}
