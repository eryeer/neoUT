using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Network.P2P.Payloads;
using Neo.Plugins;
using System;

namespace Neo.UnitTests.Plugins
{
    [TestClass]
    public class UT_Plugin
    {
        private static readonly object locker = new object();

        [TestMethod]
        public void TestCheckPolicy()
        {
            lock (locker)
            {
                Transaction tx = new Transaction
                {
                    Script = TestUtils.GetByteArray(32, 0x42),
                    Sender = UInt160.Zero,
                    SystemFee = 4200000000,
                    Attributes = new TransactionAttribute[0],
                    Witnesses = new[]
                    {
                    new Witness
                    {
                        InvocationScript = new byte[0],
                        VerificationScript = new byte[0]
                    }
                }
                };
                TestPolicyPlugin.GetPolicies().Clear();
                Plugin.CheckPolicy(tx).Should().BeTrue();
                var pp = new TestPolicyPlugin();
                Plugin.CheckPolicy(tx).Should().BeFalse();
            }
        }

        [TestMethod]
        public void TestGetConfigFile()
        {
            var pp = new TestPolicyPlugin();
            var file = pp.ConfigFile;
            file.EndsWith("config.json").Should().BeTrue();
        }

        [TestMethod]
        public void TestGetName()
        {
            var pp = new TestPolicyPlugin();
            pp.Name.Should().Be("TestPolicyPlugin");
        }

        [TestMethod]
        public void TestGetVersion()
        {
            var pp = new TestPolicyPlugin();
            pp.Version.ToString().Should().Be("1.0.0.0");
        }

        [TestMethod]
        public void TestLog()
        {
            var lp = new TestLogPlugin();
            lp.LogMessage("Hello");
            lp.Output.Should().Be("Plugin:TestLogPlugin_Info_Hello");
        }

        [TestMethod]
        public void TestSendMessage()
        {
            lock (locker)
            {
                Plugin.Plugins.Clear();
                Plugin.SendMessage("hey1").Should().BeFalse();

                var pp = new TestPolicyPlugin();
                Plugin.SendMessage("hey2").Should().BeFalse();

                var lp = new TestLogPlugin();
                Plugin.SendMessage("hey3").Should().BeTrue();
            }
        }

        [TestMethod]
        public void TestNotifyPluginsLoadedAfterSystemConstructed()
        {
            var pp = new TestPolicyPlugin();
            Action action = () => Plugin.NotifyPluginsLoadedAfterSystemConstructed();
            action.ShouldNotThrow();
        }

        [TestMethod]
        public void TestResumeNodeStartupAndSuspendNodeStartup()
        {
            var system = TestBlockchain.InitializeMockNeoSystem();
            TestPolicyPlugin.TestLoadPlugins(system);
            TestPolicyPlugin.TestSuspendNodeStartup();
            TestPolicyPlugin.TestSuspendNodeStartup();
            TestPolicyPlugin.TestResumeNodeStartup().Should().BeFalse();
            TestPolicyPlugin.TestResumeNodeStartup().Should().BeTrue();
        }

        [TestMethod]
        public void TestGetConfiguration()
        {
            var pp = new TestPolicyPlugin();
            pp.TestGetConfiguration().Key.Should().Be("PluginConfiguration");
        }
    }
}