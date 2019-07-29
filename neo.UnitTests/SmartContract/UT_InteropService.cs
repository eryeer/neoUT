using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.VM;
using Neo.VM.Types;

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
            var tx = TestUtils.GetTransaction();
            var engine = new ApplicationEngine(TriggerType.Application, tx, null, 0);
            var script = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            engine.LoadScript(script);
            InteropService.Invoke(engine, "System.ExecutionEngine.GetScriptContainer".ToInteropMethodHash());
            engine.CurrentContext.EvaluationStack.Pop().Should().Be(StackItem.FromInterface(engine.ScriptContainer));
        }

        [TestMethod]
        public void TestExecutionEngine_GetExecutingScriptHash()
        {
            var tx = TestUtils.GetTransaction();
            var engine = new ApplicationEngine(TriggerType.Application, tx, null, 0);
            var script = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            engine.LoadScript(script);
            InteropService.Invoke(engine, "System.ExecutionEngine.GetExecutingScriptHash".ToInteropMethodHash());
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString()
                .Should().Be(engine.CurrentScriptHash.ToArray().ToHexString());
        }

        [TestMethod]
        public void TestExecutionEngine_GetCallingScriptHash()
        {
            var tx = TestUtils.GetTransaction();
            var engine = new ApplicationEngine(TriggerType.Application, tx, null, 0);
            var script = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            engine.LoadScript(script);
            InteropService.Invoke(engine, "System.ExecutionEngine.GetCallingScriptHash".ToInteropMethodHash());
            ByteArray stack = (ByteArray)engine.CurrentContext.EvaluationStack.Pop();
            stack.Should().Be(new ByteArray(new byte[0]));

            engine = new ApplicationEngine(TriggerType.Application, tx, null, 0);
            engine.LoadScript(script);
            engine.LoadScript(new byte[] { 0x01 });
            InteropService.Invoke(engine, "System.ExecutionEngine.GetCallingScriptHash".ToInteropMethodHash());
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString()
                .Should().Be(engine.CallingScriptHash.ToArray().ToHexString());
        }

        [TestMethod]
        public void TestExecutionEngine_GetEntryScriptHash()
        {
            var tx = TestUtils.GetTransaction();
            var engine = new ApplicationEngine(TriggerType.Application, tx, null, 0);
            var script = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            engine.LoadScript(script);
            InteropService.Invoke(engine, "System.ExecutionEngine.GetEntryScriptHash".ToInteropMethodHash());
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString()
                .Should().Be(engine.EntryScriptHash.ToArray().ToHexString());
        }
    }
}