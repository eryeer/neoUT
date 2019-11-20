using System;

namespace Neo.SmartContract
{
    internal class InteropDescriptor1
    {
        public int Method { get; }
        public Func<String, bool> Handler { get; }

        public InteropDescriptor1(int method, Func<String, bool> handler)
        {
            this.Method = method;
            this.Handler = handler;
        }
    }
}
