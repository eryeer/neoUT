using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Network.P2P.Payloads;
using Neo.Plugins;

namespace Neo.UnitTests.Plugins
{
    class PolicyPlugin : IPolicyPlugin
    {
        public IEnumerable<Transaction> FilterForBlock(IEnumerable<Transaction> transactions)
        {
            return transactions;
        }

        public bool FilterForMemoryPool(Transaction tx)
        {
            return true;
        }
    }

    [TestClass]
    public class UT_Plugin
    {
        [TestMethod]
        public void TestCheckPolicy()
        {

        }
    }
}
