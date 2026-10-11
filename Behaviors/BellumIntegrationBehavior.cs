using System;
using System.Threading;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class BellumIntegrationBehavior : CampaignBehaviorBase
    {
        internal static BellumIntegrationBehavior Current => Campaign.Current?.CampaignBehaviorManager?.GetBehavior<BellumIntegrationBehavior>();
        private int _threadId = Thread.CurrentThread.ManagedThreadId;
        internal bool IsCampaignThread => Thread.CurrentThread.ManagedThreadId == _threadId;

        internal static void RequireCampaignThread()
        {
            if (Current?.IsCampaignThread != true)
                throw new InvalidOperationException("Bellum integration requires an active campaign and its campaign thread.");
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this,
                _ => _threadId = Thread.CurrentThread.ManagedThreadId);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, DispatchPartitions);
        }
        public override void SyncData(IDataStore store) { SyncLegitimacy(store); SyncPartitions(store); }
    }
}
