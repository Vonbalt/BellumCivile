using System;

namespace BellumCivile
{
    // Relaxes only our model's temporary-state check during synchronous candidate discovery.
    internal sealed class MarriageProspectEvaluation : IDisposable
    {
        [ThreadStatic] private static int _depth;
        internal static bool Active => _depth > 0;
        internal MarriageProspectEvaluation() { _depth++; }
        public void Dispose() { _depth--; }
    }
}
