using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.Ledger;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.VM;
using Neo.VM.Types;
using Neo.Wallets;
using System;
using System.Linq;
using System.Text;

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
            var engine = GetEngine(true);
            InteropService.Invoke(engine, "System.ExecutionEngine.GetScriptContainer".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().Should().Be(StackItem.FromInterface(engine.ScriptContainer));
        }

        [TestMethod]
        public void TestExecutionEngine_GetExecutingScriptHash()
        {
            var engine = GetEngine();
            InteropService.Invoke(engine, "System.ExecutionEngine.GetExecutingScriptHash".ToInteropMethodHash()).Should().BeTrue(); ;
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString()
                .Should().Be(engine.CurrentScriptHash.ToArray().ToHexString());
        }

        [TestMethod]
        public void TestExecutionEngine_GetCallingScriptHash()
        {
            var engine = GetEngine(true);
            InteropService.Invoke(engine, "System.ExecutionEngine.GetCallingScriptHash".ToInteropMethodHash()).Should().BeTrue();
            ByteArray stack = (ByteArray)engine.CurrentContext.EvaluationStack.Pop();
            stack.Should().Be(new ByteArray(new byte[0]));

            engine = GetEngine(true);
            engine.LoadScript(new byte[] { 0x01 });
            InteropService.Invoke(engine, "System.ExecutionEngine.GetCallingScriptHash".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString()
                .Should().Be(engine.CallingScriptHash.ToArray().ToHexString());
        }

        [TestMethod]
        public void TestExecutionEngine_GetEntryScriptHash()
        {
            var engine = GetEngine();
            InteropService.Invoke(engine, "System.ExecutionEngine.GetEntryScriptHash".ToInteropMethodHash()).Should().BeTrue(); ;
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString()
                .Should().Be(engine.EntryScriptHash.ToArray().ToHexString());
        }

        [TestMethod]
        public void TestRuntime_Platform()
        {
            var engine = GetEngine();
            InteropService.Invoke(engine, "System.Runtime.Platform".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString()
                .Should().Be(Encoding.ASCII.GetBytes("NEO").ToHexString());
        }

        [TestMethod]
        public void TestRuntime_GetTrigger()
        {
            var engine = GetEngine();
            InteropService.Invoke(engine, "System.Runtime.GetTrigger".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBigInteger()
                .Should().Be((int)engine.Trigger);
        }

        [TestMethod]
        public void TestRuntime_CheckWitness()
        {
            byte[] privateKey = { 0x01,0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01};
            KeyPair keyPair = new KeyPair(privateKey);
            ECPoint pubkey = keyPair.PublicKey;

            var engine = GetEngine(true);
            ((Transaction)engine.ScriptContainer).Sender = Contract.CreateSignatureRedeemScript(pubkey).ToScriptHash();

            engine.CurrentContext.EvaluationStack.Push(pubkey.EncodePoint(true));
            InteropService.Invoke(engine, "System.Runtime.CheckWitness".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBoolean().Should().Be(true);

            engine.CurrentContext.EvaluationStack.Push(((Transaction)engine.ScriptContainer).Sender.ToArray());
            InteropService.Invoke(engine, "System.Runtime.CheckWitness".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBoolean().Should().Be(true);

            engine.CurrentContext.EvaluationStack.Push(new byte[0]);
            InteropService.Invoke(engine, "System.Runtime.CheckWitness".ToInteropMethodHash()).Should().BeFalse();
        }

        [TestMethod]
        public void TestRuntime_Log()
        {
            var engine = GetEngine(true);
            string message = "hello";
            engine.CurrentContext.EvaluationStack.Push(Encoding.UTF8.GetBytes(message));
            ApplicationEngine.Log += LogEvent;
            InteropService.Invoke(engine, "System.Runtime.Log".ToInteropMethodHash()).Should().BeTrue();
            ((Transaction)engine.ScriptContainer).Script.ToHexString().Should().Be(new byte[] { 0x01, 0x02, 0x03 }.ToHexString());
        }

        [TestMethod]
        public void TestRuntime_GetTime()
        {
            Block block = new Block();
            TestUtils.SetupBlockWithValues(block, UInt256.Zero, out var merkRootVal, out var val160, out var timestampVal, out var indexVal, out var scriptVal, out var transactionsVal, 0);
            var engine = GetEngine(true, true);
            engine.Snapshot.PersistingBlock = block;

            InteropService.Invoke(engine, "System.Runtime.GetTime".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBigInteger().Should().Be(block.Timestamp);
        }

        [TestMethod]
        public void TestRuntime_Serialize()
        {
            var engine = GetEngine();
            engine.CurrentContext.EvaluationStack.Push(100);
            InteropService.Invoke(engine, "System.Runtime.Serialize".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString()
                .Should().Be(new byte[] { 0x02, 0x01, 0x64 }.ToHexString());

            engine.CurrentContext.EvaluationStack.Push(new byte[1024 * 1024 * 2]); //Larger than MaxItemSize
            InteropService.Invoke(engine, "System.Runtime.Serialize".ToInteropMethodHash()).Should().BeFalse();

            engine.CurrentContext.EvaluationStack.Push(new TestInteropInterface());  //NotSupportedException
            InteropService.Invoke(engine, "System.Runtime.Serialize".ToInteropMethodHash()).Should().BeFalse();
        }

        [TestMethod]
        public void TestRuntime_Deserialize()
        {
            var engine = GetEngine();
            engine.CurrentContext.EvaluationStack.Push(100);
            InteropService.Invoke(engine, "System.Runtime.Serialize".ToInteropMethodHash()).Should().BeTrue();
            InteropService.Invoke(engine, "System.Runtime.Deserialize".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBigInteger().Should().Be(100);

            engine.CurrentContext.EvaluationStack.Push(new byte[] { 0xfa, 0x01 }); //FormatException
            InteropService.Invoke(engine, "System.Runtime.Deserialize".ToInteropMethodHash()).Should().BeFalse();
        }

        [TestMethod]
        public void TestRuntime_GetInvocationCounter()
        {
            var engine = GetEngine();
            InteropService.Invoke(engine, "System.Runtime.GetInvocationCounter".ToInteropMethodHash()).Should().BeFalse();
            engine.InvocationCounter.TryAdd(engine.CurrentScriptHash, 10);
            InteropService.Invoke(engine, "System.Runtime.GetInvocationCounter".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBigInteger().Should().Be(10);
        }

        [TestMethod]
        public void TestCrypto_Verify()
        {
            var engine = GetEngine(true);
            IVerifiable iv = engine.ScriptContainer;
            byte[] message = iv.GetHashData();
            byte[] privateKey = { 0x01,0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01};
            KeyPair keyPair = new KeyPair(privateKey);
            ECPoint pubkey = keyPair.PublicKey;
            byte[] signature = Crypto.Default.Sign(message, privateKey, pubkey.EncodePoint(false).Skip(1).ToArray());

            engine.CurrentContext.EvaluationStack.Push(signature);
            engine.CurrentContext.EvaluationStack.Push(pubkey.EncodePoint(false));
            engine.CurrentContext.EvaluationStack.Push(message);
            InteropService.Invoke(engine, "System.Crypto.Verify".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBoolean().Should().BeTrue();

            byte[] wrongkey = pubkey.EncodePoint(false);
            wrongkey[0] = 5;
            engine.CurrentContext.EvaluationStack.Push(signature);
            engine.CurrentContext.EvaluationStack.Push(wrongkey);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<IVerifiable>(engine.ScriptContainer));
            InteropService.Invoke(engine, "System.Crypto.Verify".ToInteropMethodHash()).Should().BeFalse();

        }

        [TestMethod]
        public void TestBlockchain_GetHeight()
        {
            var engine = GetEngine(true, true);
            InteropService.Invoke(engine, "System.Blockchain.GetHeight".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBigInteger().Should().Be(0);
        }

        [TestMethod]
        public void TestBlockchain_GetHeader()
        {
            TestBlockchain.InitializeMockNeoSystem();
            var engine = GetEngine(true, true);

            engine.CurrentContext.EvaluationStack.Push(new byte[] { 0x01 });
            InteropService.Invoke(engine, "System.Blockchain.GetHeader".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString().Should().Be(new byte[0].ToHexString());

            byte[] data1 = new byte[] { 0x01, 0x01, 0x01 ,0x01, 0x01, 0x01, 0x01, 0x01,
                                        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                                        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                                        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01};
            engine.CurrentContext.EvaluationStack.Push(data1);
            InteropService.Invoke(engine, "System.Blockchain.GetHeader".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBoolean().Should().BeFalse();

            byte[] data2 = new byte[] { 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01 };
            engine.CurrentContext.EvaluationStack.Push(data2);
            InteropService.Invoke(engine, "System.Blockchain.GetHeader".ToInteropMethodHash()).Should().BeFalse();
        }

        [TestMethod]
        public void TestBlockchain_GetBlock()
        {
            TestBlockchain.InitializeMockNeoSystem();
            var engine = GetEngine(true, true);

            engine.CurrentContext.EvaluationStack.Push(new byte[] { 0x01 });
            InteropService.Invoke(engine, "System.Blockchain.GetBlock".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString().Should().Be(new byte[0].ToHexString());

            byte[] data1 = new byte[] { 0x01, 0x01, 0x01 ,0x01, 0x01, 0x01, 0x01, 0x01,
                                        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                                        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                                        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01};
            engine.CurrentContext.EvaluationStack.Push(data1);
            InteropService.Invoke(engine, "System.Blockchain.GetBlock".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBoolean().Should().BeFalse();

            byte[] data2 = new byte[] { 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01 };
            engine.CurrentContext.EvaluationStack.Push(data2);
            InteropService.Invoke(engine, "System.Blockchain.GetBlock".ToInteropMethodHash()).Should().BeFalse();
        }

        [TestMethod]
        public void TestBlockchain_GetTransaction()
        {
            var engine = GetEngine(true, true);
            byte[] data1 = new byte[] { 0x01, 0x01, 0x01 ,0x01, 0x01, 0x01, 0x01, 0x01,
                                        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                                        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                                        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01};
            engine.CurrentContext.EvaluationStack.Push(data1);
            InteropService.Invoke(engine, "System.Blockchain.GetTransaction".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBoolean().Should().BeFalse();
        }

        [TestMethod]
        public void TestBlockchain_GetTransactionHeight()
        {
            var engine = GetEngine(true, true);
            byte[] data1 = new byte[] { 0x01, 0x01, 0x01 ,0x01, 0x01, 0x01, 0x01, 0x01,
                                        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                                        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                                        0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01};
            engine.CurrentContext.EvaluationStack.Push(data1);
            InteropService.Invoke(engine, "System.Blockchain.GetTransactionHeight".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBigInteger().Should().Be(-1);
        }

        [TestMethod]
        public void TestBlockchain_GetContract()
        {
            var engine = GetEngine(true, true);
            byte[] data1 = new byte[] { 0x01, 0x01, 0x01 ,0x01, 0x01,
                                        0x01, 0x01, 0x01, 0x01, 0x01,
                                        0x01, 0x01, 0x01, 0x01, 0x01,
                                        0x01, 0x01, 0x01, 0x01, 0x01 };
            engine.CurrentContext.EvaluationStack.Push(data1);
            InteropService.Invoke(engine, "System.Blockchain.GetContract".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString().Should().Be(new byte[0].ToHexString());

            var mockSnapshot = new Mock<Snapshot>();
            var state = TestUtils.GetContract();
            mockSnapshot.SetupGet(p => p.Contracts).Returns(new TestDataCache<UInt160, ContractState>(state));
            engine = new ApplicationEngine(TriggerType.Application, null, mockSnapshot.Object, 0);
            engine.LoadScript(new byte[] { 0x01, 0x02, 0x03, 0x04 });
            engine.CurrentContext.EvaluationStack.Push(data1);
            InteropService.Invoke(engine, "System.Blockchain.GetContract".ToInteropMethodHash()).Should().BeTrue();
            var contractState = ((InteropInterface<ContractState>)engine.CurrentContext.EvaluationStack.Pop()).GetInterface<ContractState>();
            contractState.ScriptHash.ToArray().ToHexString().Should().Be(state.ScriptHash.ToArray().ToHexString());
        }

        [TestMethod]
        public void TestHeader_GetIndex()
        {
            var engine = GetEngine();
            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, "System.Header.GetIndex".ToInteropMethodHash()).Should().BeFalse();

            Block block = new Block();
            TestUtils.SetupBlockWithValues(block, UInt256.Zero, out var merkRootVal, out var val160, out var timestampVal, out var indexVal, out var scriptVal, out var transactionsVal, 0);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(block));
            InteropService.Invoke(engine, "System.Header.GetIndex".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBigInteger().Should().Be(block.Index);

            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(null));
            InteropService.Invoke(engine, "System.Header.GetIndex".ToInteropMethodHash()).Should().BeFalse();
        }

        [TestMethod]
        public void TestHeader_GetHash()
        {
            var engine = GetEngine();
            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, "System.Header.GetHash".ToInteropMethodHash()).Should().BeFalse();

            Block block = new Block();
            TestUtils.SetupBlockWithValues(block, UInt256.Zero, out var merkRootVal, out var val160, out var timestampVal, out var indexVal, out var scriptVal, out var transactionsVal, 0);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(block));
            InteropService.Invoke(engine, "System.Header.GetHash".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString().Should().Be(block.Hash.ToArray().ToHexString());

            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(null));
            InteropService.Invoke(engine, "System.Header.GetHash".ToInteropMethodHash()).Should().BeFalse();
        }

        [TestMethod]
        public void TestHeader_GetPrevHash()
        {
            var engine = GetEngine();
            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, "System.Header.GetPrevHash".ToInteropMethodHash()).Should().BeFalse();

            Block block = new Block();
            TestUtils.SetupBlockWithValues(block, UInt256.Zero, out var merkRootVal, out var val160, out var timestampVal, out var indexVal, out var scriptVal, out var transactionsVal, 0);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(block));
            InteropService.Invoke(engine, "System.Header.GetPrevHash".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString().Should().Be(block.PrevHash.ToArray().ToHexString());

            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(null));
            InteropService.Invoke(engine, "System.Header.GetPrevHash".ToInteropMethodHash()).Should().BeFalse();
        }

        [TestMethod]
        public void TestHeader_GetTimestamp()
        {
            var engine = GetEngine();
            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, "System.Header.GetTimestamp".ToInteropMethodHash()).Should().BeFalse();

            Block block = new Block();
            TestUtils.SetupBlockWithValues(block, UInt256.Zero, out var merkRootVal, out var val160, out var timestampVal, out var indexVal, out var scriptVal, out var transactionsVal, 0);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(block));
            InteropService.Invoke(engine, "System.Header.GetTimestamp".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBigInteger().Should().Be(block.Timestamp);

            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(null));
            InteropService.Invoke(engine, "System.Header.GetTimestamp".ToInteropMethodHash()).Should().BeFalse();
        }

        [TestMethod]
        public void TestBlock_GetTransactionCount()
        {
            var engine = GetEngine();
            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, "System.Block.GetTransactionCount".ToInteropMethodHash()).Should().BeFalse();

            Block block = new Block();
            TestUtils.SetupBlockWithValues(block, UInt256.Zero, out var merkRootVal, out var val160, out var timestampVal, out var indexVal, out var scriptVal, out var transactionsVal, 1);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(block));
            InteropService.Invoke(engine, "System.Block.GetTransactionCount".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBigInteger().Should().Be(block.Transactions.Length);

            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(null));
            InteropService.Invoke(engine, "System.Block.GetTransactionCount".ToInteropMethodHash()).Should().BeFalse();
        }

        [TestMethod]
        public void TestBlock_GetTransactions()
        {
            var engine = GetEngine();
            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, "System.Block.GetTransactions".ToInteropMethodHash()).Should().BeFalse();

            Block block = new Block();
            TestUtils.SetupBlockWithValues(block, UInt256.Zero, out var merkRootVal, out var val160, out var timestampVal, out var indexVal, out var scriptVal, out var transactionsVal, 1);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(block));
            InteropService.Invoke(engine, "System.Block.GetTransactions".ToInteropMethodHash()).Should().BeTrue();
            var ret = (Neo.VM.Types.Array)engine.CurrentContext.EvaluationStack.Pop();
            ret.Count.Should().Be(1);
            ((InteropInterface<Transaction>)ret[0]).GetInterface<Transaction>().Hash.ToArray().ToHexString().Should().Be(TestUtils.GetTransaction().Hash.ToArray().ToHexString());

            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(null));
            InteropService.Invoke(engine, "System.Block.GetTransactions".ToInteropMethodHash()).Should().BeFalse();

            Block block1 = new Block();
            TestUtils.SetupBlockWithValues(block1, UInt256.Zero, out var merkRootVal1, out var val1601, out var timestampVal1, out var indexVal1, out var scriptVal1, out var transactionsVal1, 1025);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(block1));
            InteropService.Invoke(engine, "System.Block.GetTransactions".ToInteropMethodHash()).Should().BeFalse();
        }

        [TestMethod]
        public void TestBlock_GetTransaction()
        {
            var engine = GetEngine();
            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, "System.Block.GetTransaction".ToInteropMethodHash()).Should().BeFalse();

            Block block = new Block();
            TestUtils.SetupBlockWithValues(block, UInt256.Zero, out var merkRootVal, out var val160, out var timestampVal, out var indexVal, out var scriptVal, out var transactionsVal, 1);
            engine.CurrentContext.EvaluationStack.Push(0);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(block));
            InteropService.Invoke(engine, "System.Block.GetTransaction".ToInteropMethodHash()).Should().BeTrue();
            var ret = (InteropInterface<Transaction>)engine.CurrentContext.EvaluationStack.Pop();
            ret.GetInterface<Transaction>().Hash.ToArray().ToHexString().Should().Be(TestUtils.GetTransaction().Hash.ToArray().ToHexString());

            engine.CurrentContext.EvaluationStack.Push(0);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(null));
            InteropService.Invoke(engine, "System.Block.GetTransaction".ToInteropMethodHash()).Should().BeFalse();

            engine.CurrentContext.EvaluationStack.Push(-1);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(block));
            InteropService.Invoke(engine, "System.Block.GetTransaction".ToInteropMethodHash()).Should().BeFalse();

            engine.CurrentContext.EvaluationStack.Push(10);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(block));
            InteropService.Invoke(engine, "System.Block.GetTransaction".ToInteropMethodHash()).Should().BeFalse();
        }

        [TestMethod]
        public void TestTransaction_GetHash()
        {
            var engine = GetEngine();
            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, "System.Transaction.GetHash".ToInteropMethodHash()).Should().BeFalse();

            var tx = TestUtils.GetTransaction();
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<Transaction>(tx));
            InteropService.Invoke(engine, "System.Transaction.GetHash".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString().Should().Be(tx.Hash.ToArray().ToHexString());

            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<Transaction>(null));
            InteropService.Invoke(engine, "System.Transaction.GetHash".ToInteropMethodHash()).Should().BeFalse();
        }

        [TestMethod]
        public void TestStorage_GetContext()
        {
            var engine = GetEngine();
            InteropService.Invoke(engine, "System.Storage.GetContext".ToInteropMethodHash()).Should().BeTrue();
            var ret = (InteropInterface<StorageContext>)engine.CurrentContext.EvaluationStack.Pop();
            ret.GetInterface<StorageContext>().ScriptHash.Should().Be(engine.CurrentScriptHash);
            ret.GetInterface<StorageContext>().IsReadOnly.Should().BeFalse();
        }

        [TestMethod]
        public void TestStorage_GetReadOnlyContext()
        {
            var engine = GetEngine();
            InteropService.Invoke(engine, "System.Storage.GetReadOnlyContext".ToInteropMethodHash()).Should().BeTrue();
            var ret = (InteropInterface<StorageContext>)engine.CurrentContext.EvaluationStack.Pop();
            ret.GetInterface<StorageContext>().ScriptHash.Should().Be(engine.CurrentScriptHash);
            ret.GetInterface<StorageContext>().IsReadOnly.Should().BeTrue();
        }

        [TestMethod]
        public void TestStorage_Get()
        {
            var mockSnapshot = new Mock<Snapshot>();
            var state = TestUtils.GetContract();
            state.Manifest.Features = ContractFeatures.HasStorage;

            var storageItem = new StorageItem
            {
                Value = new byte[] { 0x01, 0x02, 0x03, 0x04 },
                IsConstant = true
            };
            mockSnapshot.SetupGet(p => p.Contracts).Returns(new TestDataCache<UInt160, ContractState>(state));
            mockSnapshot.SetupGet(p => p.Storages).Returns(new TestDataCache<StorageKey, StorageItem>(storageItem));
            var engine = new ApplicationEngine(TriggerType.Application, null, mockSnapshot.Object, 0);
            engine.LoadScript(new byte[] { 0x01, 0x02, 0x03, 0x04 });

            engine.CurrentContext.EvaluationStack.Push(new byte[] { 0x01 });
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<StorageContext>(new StorageContext
            {
                ScriptHash = state.ScriptHash,
                IsReadOnly = false
            }));
            InteropService.Invoke(engine, "System.Storage.Get".ToInteropMethodHash()).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString().Should().Be(storageItem.Value.ToHexString());

            mockSnapshot.SetupGet(p => p.Contracts).Returns(new TestDataCache<UInt160, ContractState>());
            engine = new ApplicationEngine(TriggerType.Application, null, mockSnapshot.Object, 0);
            engine.LoadScript(new byte[] { 0x01, 0x02, 0x03, 0x04 });
            engine.CurrentContext.EvaluationStack.Push(new byte[] { 0x01 });
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<StorageContext>(new StorageContext
            {
                ScriptHash = state.ScriptHash,
                IsReadOnly = false
            }));
            InteropService.Invoke(engine, "System.Storage.Get".ToInteropMethodHash()).Should().BeFalse();

            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, "System.Storage.Get".ToInteropMethodHash()).Should().BeFalse();
        }

        [TestMethod]
        public void TestStorage_Put()
        {
            var engine = GetEngine(false, true);
            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, "System.Storage.Put".ToInteropMethodHash()).Should().BeFalse();

            //CheckStorageContext fail
            var key = new byte[] { 0x01 };
            var value = new byte[] { 0x02 };
            engine.CurrentContext.EvaluationStack.Push(value);
            engine.CurrentContext.EvaluationStack.Push(key);
            var state = TestUtils.GetContract();
            var storageContext = new StorageContext
            {
                ScriptHash = state.ScriptHash,
                IsReadOnly = false
            };
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<StorageContext>(storageContext));
            InteropService.Invoke(engine, "System.Storage.Put".ToInteropMethodHash()).Should().BeFalse();

            //key.Length > MaxStorageKeySize
            key = new byte[InteropService.MaxStorageKeySize + 1];
            value = new byte[] { 0x02 };
            engine.CurrentContext.EvaluationStack.Push(value);
            engine.CurrentContext.EvaluationStack.Push(key);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<StorageContext>(storageContext));
            InteropService.Invoke(engine, "System.Storage.Put".ToInteropMethodHash()).Should().BeFalse();

            //value.Length > MaxStorageValueSize
            key = new byte[] { 0x01 };
            value = new byte[ushort.MaxValue + 1];
            engine.CurrentContext.EvaluationStack.Push(value);
            engine.CurrentContext.EvaluationStack.Push(key);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<StorageContext>(storageContext));
            InteropService.Invoke(engine, "System.Storage.Put".ToInteropMethodHash()).Should().BeFalse();

            //context.IsReadOnly
            key = new byte[] { 0x01 };
            value = new byte[] { 0x02 };
            storageContext.IsReadOnly = true;
            engine.CurrentContext.EvaluationStack.Push(value);
            engine.CurrentContext.EvaluationStack.Push(key);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<StorageContext>(storageContext));
            InteropService.Invoke(engine, "System.Storage.Put".ToInteropMethodHash()).Should().BeFalse();

            //storage value is constant
            var mockSnapshot = new Mock<Snapshot>();
            state.Manifest.Features = ContractFeatures.HasStorage;
            var storageItem = new StorageItem
            {
                Value = new byte[] { 0x01, 0x02, 0x03, 0x04 },
                IsConstant = true
            };
            mockSnapshot.SetupGet(p => p.Contracts).Returns(new TestDataCache<UInt160, ContractState>(state));
            mockSnapshot.SetupGet(p => p.Storages).Returns(new TestDataCache<StorageKey, StorageItem>(storageItem));
            engine = new ApplicationEngine(TriggerType.Application, null, mockSnapshot.Object, 0);
            engine.LoadScript(new byte[] { 0x01, 0x02, 0x03, 0x04 });
            key = new byte[] { 0x01 };
            value = new byte[] { 0x02 };
            storageContext.IsReadOnly = false;
            engine.CurrentContext.EvaluationStack.Push(value);
            engine.CurrentContext.EvaluationStack.Push(key);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<StorageContext>(storageContext));
            InteropService.Invoke(engine, "System.Storage.Put".ToInteropMethodHash()).Should().BeFalse();

            //success
            storageItem.IsConstant = false;
            engine.CurrentContext.EvaluationStack.Push(value);
            engine.CurrentContext.EvaluationStack.Push(key);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<StorageContext>(storageContext));
            InteropService.Invoke(engine, "System.Storage.Put".ToInteropMethodHash()).Should().BeTrue();

            //value length == 0
            key = new byte[] { 0x01 };
            value = new byte[0];
            engine.CurrentContext.EvaluationStack.Push(value);
            engine.CurrentContext.EvaluationStack.Push(key);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<StorageContext>(storageContext));
            InteropService.Invoke(engine, "System.Storage.Put".ToInteropMethodHash()).Should().BeTrue();
        }

        [TestMethod]
        public void TestStorage_PutEx()
        {
            var engine = GetEngine(false, true);
            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, "System.Storage.PutEx".ToInteropMethodHash()).Should().BeFalse();

            var mockSnapshot = new Mock<Snapshot>();
            var state = TestUtils.GetContract();
            state.Manifest.Features = ContractFeatures.HasStorage;
            var storageItem = new StorageItem
            {
                Value = new byte[] { 0x01, 0x02, 0x03, 0x04 },
                IsConstant = false
            };
            mockSnapshot.SetupGet(p => p.Contracts).Returns(new TestDataCache<UInt160, ContractState>(state));
            mockSnapshot.SetupGet(p => p.Storages).Returns(new TestDataCache<StorageKey, StorageItem>(storageItem));
            engine = new ApplicationEngine(TriggerType.Application, null, mockSnapshot.Object, 0);
            engine.LoadScript(new byte[] { 0x01, 0x02, 0x03, 0x04 });
            var key = new byte[] { 0x01 };
            var value = new byte[] { 0x02 };
            var storageContext = new StorageContext
            {
                ScriptHash = state.ScriptHash,
                IsReadOnly = false
            };
            engine.CurrentContext.EvaluationStack.Push((int)StorageFlags.None);
            engine.CurrentContext.EvaluationStack.Push(value);
            engine.CurrentContext.EvaluationStack.Push(key);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<StorageContext>(storageContext));
            InteropService.Invoke(engine, "System.Storage.PutEx".ToInteropMethodHash()).Should().BeTrue();
        }

        [TestMethod]
        public void TestStorage_Delete()
        {
            var engine = GetEngine(false, true);
            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, "System.Storage.Delete".ToInteropMethodHash()).Should().BeFalse();


            var mockSnapshot = new Mock<Snapshot>();
            var state = TestUtils.GetContract();
            state.Manifest.Features = ContractFeatures.HasStorage;
            var storageItem = new StorageItem
            {
                Value = new byte[] { 0x01, 0x02, 0x03, 0x04 },
                IsConstant = false
            };
            mockSnapshot.SetupGet(p => p.Contracts).Returns(new TestDataCache<UInt160, ContractState>(state));
            mockSnapshot.SetupGet(p => p.Storages).Returns(new TestDataCache<StorageKey, StorageItem>(storageItem));
            engine = new ApplicationEngine(TriggerType.Application, null, mockSnapshot.Object, 0);
            engine.LoadScript(new byte[] { 0x01, 0x02, 0x03, 0x04 });
            state.Manifest.Features = ContractFeatures.HasStorage;
            var key = new byte[] { 0x01 };
            var storageContext = new StorageContext
            {
                ScriptHash = state.ScriptHash,
                IsReadOnly = false
            };
            engine.CurrentContext.EvaluationStack.Push(key);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<StorageContext>(storageContext));
            InteropService.Invoke(engine, "System.Storage.Delete".ToInteropMethodHash()).Should().BeTrue();

            //context is readonly
            storageContext.IsReadOnly = true;
            engine.CurrentContext.EvaluationStack.Push(key);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<StorageContext>(storageContext));
            InteropService.Invoke(engine, "System.Storage.Delete".ToInteropMethodHash()).Should().BeFalse();

            //CheckStorageContext fail
            storageContext.IsReadOnly = false;
            state.Manifest.Features = ContractFeatures.NoProperty;
            engine.CurrentContext.EvaluationStack.Push(key);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<StorageContext>(storageContext));
            InteropService.Invoke(engine, "System.Storage.Delete".ToInteropMethodHash()).Should().BeFalse();
        }

        [TestMethod]
        public void TestStorageContext_AsReadOnly()
        {
            var engine = GetEngine();
            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, "System.StorageContext.AsReadOnly".ToInteropMethodHash()).Should().BeFalse();

            var state = TestUtils.GetContract();
            var storageContext = new StorageContext
            {
                ScriptHash = state.ScriptHash,
                IsReadOnly = false
            };
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<StorageContext>(storageContext));
            InteropService.Invoke(engine, "System.StorageContext.AsReadOnly".ToInteropMethodHash()).Should().BeTrue();
            var ret = (InteropInterface<StorageContext>)engine.CurrentContext.EvaluationStack.Pop();
            ret.GetInterface<StorageContext>().IsReadOnly.Should().Be(true);
        }

        [TestMethod]
        public void TestInvoke()
        {
            var engine = new ApplicationEngine(TriggerType.Verification, null, null, 0);
            InteropService.Invoke(engine, 10000).Should().BeFalse();
            InteropService.Invoke(engine, "System.StorageContext.AsReadOnly".ToInteropMethodHash()).Should().BeFalse();
        }

        public static void LogEvent(object sender, LogEventArgs args)
        {
            Transaction tx = (Transaction)args.ScriptContainer;
            tx.Script = new byte[] { 0x01, 0x02, 0x03 };
        }

        private static ApplicationEngine GetEngine(bool hasContainer = false, bool hasSnapshot = false)
        {
            var tx = TestUtils.GetTransaction();
            var snapshot = TestBlockchain.GetStore().GetSnapshot();
            ApplicationEngine engine;
            if (hasContainer && hasSnapshot)
            {
                engine = new ApplicationEngine(TriggerType.Application, tx, snapshot, 0);
            }
            else if (hasContainer && !hasSnapshot)
            {
                engine = new ApplicationEngine(TriggerType.Application, tx, null, 0);
            }
            else if (!hasContainer && hasSnapshot)
            {
                engine = new ApplicationEngine(TriggerType.Application, null, snapshot, 0);
            }
            else
            {
                engine = new ApplicationEngine(TriggerType.Application, null, null, 0);
            }
            engine.LoadScript(new byte[] { 0x01, 0x02, 0x03, 0x04 });
            return engine;
        }
    }

    internal class TestInteropInterface : InteropInterface
    {
        public override bool Equals(StackItem other) => true;
        public override bool GetBoolean() => true;
        public override T GetInterface<T>() => throw new NotImplementedException();
    }
}