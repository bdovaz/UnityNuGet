using System;

namespace UnityNuGet
{
    public class RegistryProgressEventArgs : EventArgs
    {
        public int Current { get; }

        public int Total { get; }

        public RegistryProgressEventArgs(int current, int total)
        {
            Current = current;
            Total = total;
        }
    }
}
