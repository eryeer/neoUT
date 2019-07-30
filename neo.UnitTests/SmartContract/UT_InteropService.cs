using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.Cryptography.ECC;
using Neo.Network.P2P.Payloads;
using Neo.Network.P2P;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.VM;
using Neo.VM.Types;
using Neo.Wallets;
using System;
using System.Text;
using Neo.Cryptography;
using System.Linq;
using Neo.Ledger;

namespace Neo.UnitTests.SmartContract
{
    [TestClass]
    public class UT_InteropService
    {
        [TestInitialize]
        public void TestSetup()
        {
            TestBlockchain.InitializeMockNeoSystem();
        }

        [TestMethod]
        public void Runtime_GetNotifications_Test()
        {
            UInt160 scriptHash2;
            var snapshot = TestBlockchain.GetStore().GetSnapshot();

            using (var script = new ScriptBuilder())
            {
                // Drop arguments

                script.Emit(VM.OpCode.TOALTSTACK);
                script.Emit(VM.OpCode.DROP);
                script.Emit(VM.OpCode.FROMALTSTACK);

                // Notify method

                script.EmitSysCall(InteropService.System_Runtime_Notify);

                // Add return

                script.EmitPush(true);

                // Mock contract

                scriptHash2 = script.ToArray().ToScriptHash();

                snapshot.Contracts.Delete(scriptHash2);
                snapshot.Contracts.Add(scriptHash2, new Neo.Ledger.ContractState()
                {
                    Script = script.ToArray(),
                    Manifest = ContractManifest.CreateDefault(scriptHash2),
                });
            }

            // Wrong length

            using (var engine = new ApplicationEngine(TriggerType.Application, null, snapshot, 0, true))
            using (var script = new ScriptBuilder())
            {
                // Retrive

                script.EmitPush(1);
                script.EmitSysCall(InteropService.System_Runtime_GetNotifications);

                // Execute

                engine.LoadScript(script.ToArray());

                Assert.AreEqual(VMState.FAULT, engine.Execute());
            }

            // All test

            using (var engine = new ApplicationEngine(TriggerType.Application, null, snapshot, 0, true))
            using (var script = new ScriptBuilder())
            {
                // Notification 1 -> 13

                script.EmitPush(13);
                script.EmitSysCall(InteropService.System_Runtime_Notify);

                // Call script

                script.EmitAppCall(scriptHash2, "test");

                // Drop return

                script.Emit(OpCode.DROP);

                // Receive all notifications

                script.EmitPush(UInt160.Zero.ToArray());
                script.EmitSysCall(InteropService.System_Runtime_GetNotifications);

                // Execute

                engine.LoadScript(script.ToArray());
                var currentScriptHash = engine.EntryScriptHash;

                Assert.AreEqual(VMState.HALT, engine.Execute());
                Assert.AreEqual(1, engine.ResultStack.Count);
                Assert.AreEqual(2, engine.Notifications.Count);

                Assert.IsInstanceOfType(engine.ResultStack.Peek(), typeof(VM.Types.Array));

                var array = (VM.Types.Array)engine.ResultStack.Pop();

                // Check syscall result

                AssertNotification(array[1], scriptHash2, "test");
                AssertNotification(array[0], currentScriptHash, 13);

                // Check notifications

                Assert.AreEqual(scriptHash2, engine.Notifications[1].ScriptHash);
                Assert.AreEqual("test", engine.Notifications[1].State.GetString());

                Assert.AreEqual(currentScriptHash, engine.Notifications[0].ScriptHash);
                Assert.AreEqual(13, engine.Notifications[0].State.GetBigInteger());
            }

            // Script notifications

            using (var engine = new ApplicationEngine(TriggerType.Application, null, snapshot, 0, true))
            using (var script = new ScriptBuilder())
            {
                // Notification 1 -> 13

                script.EmitPush(13);
                script.EmitSysCall(InteropService.System_Runtime_Notify);

                // Call script

                script.EmitAppCall(scriptHash2, "test");

                // Drop return

                script.Emit(OpCode.DROP);

                // Receive all notifications

                script.EmitPush(scriptHash2.ToArray());
                script.EmitSysCall(InteropService.System_Runtime_GetNotifications);

                // Execute

                engine.LoadScript(script.ToArray());
                var currentScriptHash = engine.EntryScriptHash;

                Assert.AreEqual(VMState.HALT, engine.Execute());
                Assert.AreEqual(1, engine.ResultStack.Count);
                Assert.AreEqual(2, engine.Notifications.Count);

                Assert.IsInstanceOfType(engine.ResultStack.Peek(), typeof(VM.Types.Array));

                var array = (VM.Types.Array)engine.ResultStack.Pop();

                // Check syscall result

                AssertNotification(array[0], scriptHash2, "test");

                // Check notifications

                Assert.AreEqual(scriptHash2, engine.Notifications[1].ScriptHash);
                Assert.AreEqual("test", engine.Notifications[1].State.GetString());

                Assert.AreEqual(currentScriptHash, engine.Notifications[0].ScriptHash);
                Assert.AreEqual(13, engine.Notifications[0].State.GetBigInteger());
            }

            // Clean storage

            snapshot.Contracts.Delete(scriptHash2);
        }

        private void AssertNotification(StackItem stackItem, UInt160 scriptHash, string notification)
        {
            Assert.IsInstanceOfType(stackItem, typeof(VM.Types.Array));

            var array = (VM.Types.Array)stackItem;
            Assert.AreEqual(2, array.Count);
            CollectionAssert.AreEqual(scriptHash.ToArray(), array[0].GetByteArray());
            Assert.AreEqual(notification, array[1].GetString());
        }

        private void AssertNotification(StackItem stackItem, UInt160 scriptHash, int notification)
        {
            Assert.IsInstanceOfType(stackItem, typeof(VM.Types.Array));

            var array = (VM.Types.Array)stackItem;
            Assert.AreEqual(2, array.Count);
            CollectionAssert.AreEqual(scriptHash.ToArray(), array[0].GetByteArray());
            Assert.AreEqual(notification, array[1].GetBigInteger());
        }

        [TestMethod]
        public void TestExecutionEngine_GetScriptContainer()
        {
            var engine = GetEngine();
            var ret = InteropService.Invoke(engine, "System.ExecutionEngine.GetScriptContainer".ToInteropMethodHash());
            engine.CurrentContext.EvaluationStack.Pop().Should().Be(StackItem.FromInterface(engine.ScriptContainer));
            ret.Should().BeTrue();
        }

        [TestMethod]
        public void TestExecutionEngine_GetExecutingScriptHash()
        {
            var engine = GetEngine();
            var ret = InteropService.Invoke(engine, "System.ExecutionEngine.GetExecutingScriptHash".ToInteropMethodHash());
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString()
                .Should().Be(engine.CurrentScriptHash.ToArray().ToHexString());
            ret.Should().BeTrue();
        }

        [TestMethod]
        public void TestExecutionEngine_GetCallingScriptHash()
        {
            var tx = TestUtils.GetTransaction();
            var engine = new ApplicationEngine(TriggerType.Application, tx, null, 0);
            var script = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            engine.LoadScript(script);
            var ret = InteropService.Invoke(engine, "System.ExecutionEngine.GetCallingScriptHash".ToInteropMethodHash());
            ByteArray stack = (ByteArray)engine.CurrentContext.EvaluationStack.Pop();
            stack.Should().Be(new ByteArray(new byte[0]));
            ret.Should().BeTrue();

            engine = new ApplicationEngine(TriggerType.Application, tx, null, 0);
            engine.LoadScript(script);
            engine.LoadScript(new byte[] { 0x01 });
            ret = InteropService.Invoke(engine, "System.ExecutionEngine.GetCallingScriptHash".ToInteropMethodHash());
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString()
                .Should().Be(engine.CallingScriptHash.ToArray().ToHexString());
            ret.Should().BeTrue();
        }

        [TestMethod]
        public void TestExecutionEngine_GetEntryScriptHash()
        {
            var engine = GetEngine();
            var ret = InteropService.Invoke(engine, "System.ExecutionEngine.GetEntryScriptHash".ToInteropMethodHash());
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString()
                .Should().Be(engine.EntryScriptHash.ToArray().ToHexString());
            ret.Should().BeTrue();
        }

        [TestMethod]
        public void TestRuntime_Platform()
        {
            var engine = GetEngine();
            var ret = InteropService.Invoke(engine, "System.Runtime.Platform".ToInteropMethodHash());
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString()
                .Should().Be(Encoding.ASCII.GetBytes("NEO").ToHexString());
            ret.Should().BeTrue();
        }

        [TestMethod]
        public void TestRuntime_GetTrigger()
        {
            var engine = GetEngine();
            var ret = InteropService.Invoke(engine, "System.Runtime.GetTrigger".ToInteropMethodHash());
            engine.CurrentContext.EvaluationStack.Pop().GetBigInteger()
                .Should().Be((int)engine.Trigger);
            ret.Should().BeTrue();
        }

        [TestMethod]
        public void TestRuntime_CheckWitness()
        {
            byte[] privateKey = { 0x01,0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01};
            KeyPair keyPair = new KeyPair(privateKey);
            ECPoint pubkey = keyPair.PublicKey;

            var tx = TestUtils.GetTransaction();
            tx.Sender = Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash();
            var engine = new ApplicationEngine(TriggerType.Application, tx, null, 0);
            var script = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            engine.LoadScript(script);
            engine.CurrentContext.EvaluationStack.Push(pubkey.EncodePoint(true));
            var ret = InteropService.Invoke(engine, "System.Runtime.CheckWitness".ToInteropMethodHash());
            engine.CurrentContext.EvaluationStack.Pop().GetBoolean().Should().Be(true);
            ret.Should().BeTrue();

            engine.CurrentContext.EvaluationStack.Push(tx.Sender.ToArray());
            ret = InteropService.Invoke(engine, "System.Runtime.CheckWitness".ToInteropMethodHash());
            engine.CurrentContext.EvaluationStack.Pop().GetBoolean().Should().Be(true);
            ret.Should().BeTrue();

            engine.CurrentContext.EvaluationStack.Push(new byte[0]);
            ret = InteropService.Invoke(engine, "System.Runtime.CheckWitness".ToInteropMethodHash());
            ret.Should().BeFalse();
        }

        [TestMethod]
        public void TestRuntime_Log()
        {
            var tx = TestUtils.GetTransaction();
            var engine = new ApplicationEngine(TriggerType.Application, tx, null, 0);
            var script = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            engine.LoadScript(script);
            string message = "hello";
            engine.CurrentContext.EvaluationStack.Push(Encoding.UTF8.GetBytes(message));
            ApplicationEngine.Log += LogEvent;
            var ret = InteropService.Invoke(engine, "System.Runtime.Log".ToInteropMethodHash());
            tx.Script.ToHexString().Should().Be(new byte[] { 0x01, 0x02, 0x03 }.ToHexString());
            ret.Should().BeTrue();
        }

        [TestMethod]
        public void TestRuntime_GetTime()
        {
            Block block = new Block();
            UInt256 val256 = UInt256.Zero;
            TestUtils.SetupBlockWithValues(block, val256, out var merkRootVal, out var val160, out var timestampVal, out var indexVal, out var scriptVal, out var transactionsVal, 0);
            var snapshot = TestBlockchain.GetStore().GetSnapshot();
            snapshot.PersistingBlock = block;
            var tx = TestUtils.GetTransaction();
            var engine = new ApplicationEngine(TriggerType.Application, tx, snapshot, 0);
            var script = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            engine.LoadScript(script);
            var ret = InteropService.Invoke(engine, "System.Runtime.GetTime".ToInteropMethodHash());
            engine.CurrentContext.EvaluationStack.Pop().GetBigInteger().Should().Be(block.Timestamp);
            ret.Should().BeTrue();
        }

        [TestMethod]
        public void TestRuntime_Serialize()
        {
            var engine = GetEngine();
            engine.CurrentContext.EvaluationStack.Push(100);
            var ret = InteropService.Invoke(engine, "System.Runtime.Serialize".ToInteropMethodHash());
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString()
                .Should().Be(new byte[] { 0x02, 0x01, 0x64 }.ToHexString());
            ret.Should().BeTrue();

            engine.CurrentContext.EvaluationStack.Push(new byte[1024 * 1024 * 2]); //Larger than MaxItemSize
            ret = InteropService.Invoke(engine, "System.Runtime.Serialize".ToInteropMethodHash());
            ret.Should().BeFalse();

            engine.CurrentContext.EvaluationStack.Push(new TestInteropInterface());  //NotSupportedException
            ret = InteropService.Invoke(engine, "System.Runtime.Serialize".ToInteropMethodHash());
            ret.Should().BeFalse();
        }

        [TestMethod]
        public void TestRuntime_Deserialize()
        {
            var engine = GetEngine();
            engine.CurrentContext.EvaluationStack.Push(100);
            var ret = InteropService.Invoke(engine, "System.Runtime.Serialize".ToInteropMethodHash());
            ret.Should().BeTrue();
            ret = InteropService.Invoke(engine, "System.Runtime.Deserialize".ToInteropMethodHash());
            ret.Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBigInteger().Should().Be(100);

            engine.CurrentContext.EvaluationStack.Push(new byte[] { 0xfa, 0x01 }); //FormatException
            ret = InteropService.Invoke(engine, "System.Runtime.Deserialize".ToInteropMethodHash());
            ret.Should().BeFalse();
        }

        [TestMethod]
        public void TestRuntime_GetInvocationCounter()
        {
            var engine = GetEngine();
            var ret = InteropService.Invoke(engine, "System.Runtime.GetInvocationCounter".ToInteropMethodHash());
            ret.Should().BeFalse();
            engine.InvocationCounter.TryAdd(engine.CurrentScriptHash, 10);
            ret = InteropService.Invoke(engine, "System.Runtime.GetInvocationCounter".ToInteropMethodHash());
            engine.CurrentContext.EvaluationStack.Pop().GetBigInteger().Should().Be(10);
            ret.Should().BeTrue();
        }

        [TestMethod]
        public void TestCrypto_Verify()
        {
            var tx = TestUtils.GetTransaction();
            var engine = new ApplicationEngine(TriggerType.Application, tx, null, 0);
            var script = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            engine.LoadScript(script);
            IVerifiable iv = tx;
            byte[] message = iv.GetHashData();
            byte[] privateKey = { 0x01,0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01};
            KeyPair keyPair = new KeyPair(privateKey);
            ECPoint pubkey = keyPair.PublicKey;
            byte[] signature = Crypto.Default.Sign(message, privateKey, pubkey.EncodePoint(false).Skip(1).ToArray());

            engine.CurrentContext.EvaluationStack.Push(signature);
            engine.CurrentContext.EvaluationStack.Push(pubkey.EncodePoint(false));
            engine.CurrentContext.EvaluationStack.Push(message);
            var ret = InteropService.Invoke(engine, "System.Crypto.Verify".ToInteropMethodHash());
            ret.Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBoolean().Should().BeTrue();

            byte[] wrongkey = pubkey.EncodePoint(false);
            wrongkey[0] = 5;
            engine.CurrentContext.EvaluationStack.Push(signature);
            engine.CurrentContext.EvaluationStack.Push(wrongkey);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<Transaction>(tx));
            ret = InteropService.Invoke(engine, "System.Crypto.Verify".ToInteropMethodHash());
            ret.Should().BeFalse();
        }

        [TestMethod]
        public void TestBlockchain_GetHeight()
        {
            var snapshot = TestBlockchain.GetStore().GetSnapshot();
            var tx = TestUtils.GetTransaction();
            var engine = new ApplicationEngine(TriggerType.Application, tx, snapshot, 0);
            var script = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            engine.LoadScript(script);
            var ret = InteropService.Invoke(engine, "System.Blockchain.GetHeight".ToInteropMethodHash());
            ret.Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBigInteger().Should().Be(0);
        }

        public static void LogEvent(object sender, LogEventArgs args)
        {
            Transaction tx = (Transaction)args.ScriptContainer;
            tx.Script = new byte[] { 0x01, 0x02, 0x03 };
        }

        private static ApplicationEngine GetEngine()
        {
            var tx = TestUtils.GetTransaction();
            var engine = new ApplicationEngine(TriggerType.Application, tx, null, 0);
            var script = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            engine.LoadScript(script);
            return engine;
        }
    }

    class TestInteropInterface : InteropInterface
    {
        public override bool Equals(StackItem other) => true;
        public override bool GetBoolean() => true;
        public override T GetInterface<T>() => throw new NotImplementedException();
    }
}