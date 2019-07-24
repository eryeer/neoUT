using Microsoft.Extensions.Configuration;
using Neo.Network.P2P.Payloads;
using Neo.Plugins;
using System.Collections.Generic;

namespace Neo.UnitTests.Plugins
{
    class TestPolicyPlugin : Plugin, IPolicyPlugin
    {
        public TestPolicyPlugin() : base() { }

        public override void Configure() { }

        public IEnumerable<Transaction> FilterForBlock(IEnumerable<Transaction> transactions)
        {
            return transactions;
        }

        public bool FilterForMemoryPool(Transaction tx)
        {
            return false;
        }

        public bool TestOnMessage(object message)
        {
            return OnMessage(message);
        }

        public IConfigurationSection TestGetConfiguration()
        {
            return GetConfiguration();
        }

        public static bool TestResumeNodeStartup()
        {
            return ResumeNodeStartup();
        }

        public static void TestSuspendNodeStartup()
        {
            SuspendNodeStartup();
        }

        public static void TestLoadPlugins(NeoSystem system)
        {
            LoadPlugins(system);
        }

        public static List<IPolicyPlugin> GetPolicies()
        {
            return Policies;
        }
    }
}