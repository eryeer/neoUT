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
using Neo.VM.Types;
using Neo.Wallets;
using System.Linq;
using VMArray = Neo.VM.Types.Array;

namespace Neo.UnitTests.SmartContract
{
    [TestClass]
    public class UT_InteropService_NEO
    {
        [TestMethod]
        public void TestCheckSig()
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
            InteropService.Invoke(engine, InteropService.Neo_Crypto_CheckSig).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBoolean().Should().BeTrue();

            engine.CurrentContext.EvaluationStack.Push(signature);
            engine.CurrentContext.EvaluationStack.Push(new byte[70]);
            InteropService.Invoke(engine, InteropService.Neo_Crypto_CheckSig).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBoolean().Should().BeFalse();
        }


        [TestMethod]
        public void TestCrypto_CheckMultiSig()
        {
            var engine = GetEngine(true);
            IVerifiable iv = engine.ScriptContainer;
            byte[] message = iv.GetHashData();

            byte[] privkey1 = { 0x01,0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01};
            KeyPair key1 = new KeyPair(privkey1);
            ECPoint pubkey1 = key1.PublicKey;
            byte[] signature1 = Crypto.Default.Sign(message, privkey1, pubkey1.EncodePoint(false).Skip(1).ToArray());

            byte[] privkey2 = { 0x01,0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x02};
            KeyPair key2 = new KeyPair(privkey2);
            ECPoint pubkey2 = key2.PublicKey;
            byte[] signature2 = Crypto.Default.Sign(message, privkey2, pubkey2.EncodePoint(false).Skip(1).ToArray());

            var pubkeys = new VMArray
            {
                pubkey1.EncodePoint(false),
                pubkey2.EncodePoint(false)
            };
            var signatures = new VMArray
            {
                signature1,
                signature2
            };
            engine.CurrentContext.EvaluationStack.Push(signatures);
            engine.CurrentContext.EvaluationStack.Push(pubkeys);
            InteropService.Invoke(engine, InteropService.Neo_Crypto_CheckMultiSig).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBoolean().Should().BeTrue();

            pubkeys = new VMArray();
            engine.CurrentContext.EvaluationStack.Push(signatures);
            engine.CurrentContext.EvaluationStack.Push(pubkeys);
            InteropService.Invoke(engine, InteropService.Neo_Crypto_CheckMultiSig).Should().BeFalse();

            pubkeys = new VMArray
            {
                pubkey1.EncodePoint(false),
                pubkey2.EncodePoint(false)
            };
            signatures = new VMArray();
            engine.CurrentContext.EvaluationStack.Push(signatures);
            engine.CurrentContext.EvaluationStack.Push(pubkeys);
            InteropService.Invoke(engine, InteropService.Neo_Crypto_CheckMultiSig).Should().BeFalse();

            pubkeys = new VMArray
            {
                pubkey1.EncodePoint(false),
                pubkey2.EncodePoint(false)
            };
            signatures = new VMArray
            {
                signature1,
                new byte[] { 0x01 }
            };
            engine.CurrentContext.EvaluationStack.Push(signatures);
            engine.CurrentContext.EvaluationStack.Push(pubkeys);
            InteropService.Invoke(engine, InteropService.Neo_Crypto_CheckMultiSig).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBoolean().Should().BeFalse();

            pubkeys = new VMArray
            {
                pubkey1.EncodePoint(false),
                new byte[70]
            };
            signatures = new VMArray
            {
                signature1,
                signature2
            };
            engine.CurrentContext.EvaluationStack.Push(signatures);
            engine.CurrentContext.EvaluationStack.Push(pubkeys);
            InteropService.Invoke(engine, InteropService.Neo_Crypto_CheckMultiSig).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBoolean().Should().BeFalse();
        }

        [TestMethod]
        public void TestHeader_GetVersion()
        {
            var engine = GetEngine();
            Block block = new Block();
            TestUtils.SetupBlockWithValues(block, UInt256.Zero, out var merkRootVal, out var val160, out var timestampVal, out var indexVal, out var scriptVal, out var transactionsVal, 0);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(block));
            InteropService.Invoke(engine, InteropService.Neo_Header_GetVersion).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBigInteger().Should().Be(block.Version);

            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, InteropService.Neo_Header_GetVersion).Should().BeFalse();

            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(null));
            InteropService.Invoke(engine, InteropService.Neo_Header_GetVersion).Should().BeFalse();
        }
        
        [TestMethod]
        public void TestHeader_GetMerkleRoot()
        {
            var engine = GetEngine();
            Block block = new Block();
            TestUtils.SetupBlockWithValues(block, UInt256.Zero, out var merkRootVal, out var val160, out var timestampVal, out var indexVal, out var scriptVal, out var transactionsVal, 0);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(block));
            InteropService.Invoke(engine, InteropService.Neo_Header_GetMerkleRoot).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString()
                .Should().Be(block.MerkleRoot.ToArray().ToHexString());

            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, InteropService.Neo_Header_GetMerkleRoot).Should().BeFalse();

            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(null));
            InteropService.Invoke(engine, InteropService.Neo_Header_GetMerkleRoot).Should().BeFalse();
        }

        [TestMethod]
        public void TestHeader_GetNextConsensus()
        {
            var engine = GetEngine();
            Block block = new Block();
            TestUtils.SetupBlockWithValues(block, UInt256.Zero, out var merkRootVal, out var val160, out var timestampVal, out var indexVal, out var scriptVal, out var transactionsVal, 0);
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(block));
            InteropService.Invoke(engine, InteropService.Neo_Header_GetNextConsensus).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString()
                .Should().Be(block.NextConsensus.ToArray().ToHexString());

            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, InteropService.Neo_Header_GetNextConsensus).Should().BeFalse();

            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<BlockBase>(null));
            InteropService.Invoke(engine, InteropService.Neo_Header_GetNextConsensus).Should().BeFalse();
        }

        [TestMethod]
        public void TestTransaction_GetScript()
        {
            var engine = GetEngine();
            var tx = TestUtils.GetTransaction();
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<Transaction>(tx));
            InteropService.Invoke(engine, InteropService.Neo_Transaction_GetScript).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString()
                .Should().Be(tx.Script.ToHexString());

            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, InteropService.Neo_Transaction_GetScript).Should().BeFalse();

            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<Transaction>(null));
            InteropService.Invoke(engine, InteropService.Neo_Transaction_GetScript).Should().BeFalse();
        }
        
        [TestMethod]
        public void TestTransaction_GetWitnesses()
        {
            var engine = GetEngine();
            var tx = TestUtils.GetTransaction();
            tx.Witnesses[0].VerificationScript = new byte[] { 0x01, 0x01, 0x01, 0x01 };
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<Transaction>(tx));
            InteropService.Invoke(engine, InteropService.Neo_Transaction_GetWitnesses).Should().BeTrue();
            var ret = (InteropInterface<WitnessWrapper>)((VMArray)engine.CurrentContext.EvaluationStack.Pop())[0];
            ret.GetInterface<WitnessWrapper>().VerificationScript.ToHexString()
                .Should().Be(tx.Witnesses[0].VerificationScript.ToHexString());

            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, InteropService.Neo_Transaction_GetWitnesses).Should().BeFalse();

            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<Transaction>(null));
            InteropService.Invoke(engine, InteropService.Neo_Transaction_GetWitnesses).Should().BeFalse();

            tx.Witnesses = new Witness[engine.MaxArraySize + 1];
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<Transaction>(tx));
            InteropService.Invoke(engine, InteropService.Neo_Transaction_GetWitnesses).Should().BeFalse();
        }

        [TestMethod]
        public void TestAccount_IsStandard()
        {
            var engine = GetEngine(false, true);
            var hash = new byte[] { 0x01, 0x01, 0x01 ,0x01, 0x01,
                                    0x01, 0x01, 0x01, 0x01, 0x01,
                                    0x01, 0x01, 0x01, 0x01, 0x01,
                                    0x01, 0x01, 0x01, 0x01, 0x01 };
            engine.CurrentContext.EvaluationStack.Push(hash);
            InteropService.Invoke(engine, InteropService.Neo_Account_IsStandard).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBoolean().Should().BeTrue();

            var mockSnapshot = new Mock<Snapshot>();
            var state = TestUtils.GetContract();
            mockSnapshot.SetupGet(p => p.Contracts).Returns(new TestDataCache<UInt160, ContractState>(state));
            engine = new ApplicationEngine(TriggerType.Application, null, mockSnapshot.Object, 0);
            engine.LoadScript(new byte[] { 0x01 });
            engine.CurrentContext.EvaluationStack.Push(hash);
            InteropService.Invoke(engine, InteropService.Neo_Account_IsStandard).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBoolean().Should().BeFalse();
        }
        
        [TestMethod]
        public void TestContract_Create()
        {
            var engine = GetEngine(false, true);
            var script = new byte[1024 * 1024 + 1];
            engine.CurrentContext.EvaluationStack.Push(script);
            InteropService.Invoke(engine, InteropService.Neo_Contract_Create).Should().BeFalse();

            string manifestStr = new string(new char[ContractManifest.MaxLength + 1]);
            script = new byte[] { 0x01 };
            engine.CurrentContext.EvaluationStack.Push(manifestStr);
            engine.CurrentContext.EvaluationStack.Push(script);
            InteropService.Invoke(engine, InteropService.Neo_Contract_Create).Should().BeFalse();

            var manifest = ContractManifest.CreateDefault(UInt160.Parse("0xa400ff00ff00ff00ff00ff00ff00ff00ff00ff01"));
            engine.CurrentContext.EvaluationStack.Push(manifest.ToString());
            engine.CurrentContext.EvaluationStack.Push(script);
            InteropService.Invoke(engine, InteropService.Neo_Contract_Create).Should().BeFalse();

            manifest.Abi.Hash = script.ToScriptHash();
            engine.CurrentContext.EvaluationStack.Push(manifest.ToString());
            engine.CurrentContext.EvaluationStack.Push(script);
            InteropService.Invoke(engine, InteropService.Neo_Contract_Create).Should().BeTrue();

            var mockSnapshot = new Mock<Snapshot>();
            var state = TestUtils.GetContract();
            mockSnapshot.SetupGet(p => p.Contracts).Returns(new TestDataCache<UInt160, ContractState>(state));
            engine = new ApplicationEngine(TriggerType.Application, null, mockSnapshot.Object, 0);
            engine.LoadScript(new byte[] { 0x01 });
            engine.CurrentContext.EvaluationStack.Push(manifest.ToString());
            engine.CurrentContext.EvaluationStack.Push(script);
            InteropService.Invoke(engine, InteropService.Neo_Contract_Create).Should().BeFalse();
        }

        [TestMethod]
        public void TestContract_Update()
        {
            var engine = GetEngine(false, true);
            var script = new byte[1024 * 1024 + 1];
            engine.CurrentContext.EvaluationStack.Push(script);
            InteropService.Invoke(engine, InteropService.Neo_Contract_Update).Should().BeFalse();

            string manifestStr = new string(new char[ContractManifest.MaxLength + 1]);
            script = new byte[] { 0x01 };
            engine.CurrentContext.EvaluationStack.Push(manifestStr);
            engine.CurrentContext.EvaluationStack.Push(script);
            InteropService.Invoke(engine, InteropService.Neo_Contract_Update).Should().BeFalse();

            manifestStr = "";
            engine.CurrentContext.EvaluationStack.Push(manifestStr);
            engine.CurrentContext.EvaluationStack.Push(script);
            InteropService.Invoke(engine, InteropService.Neo_Contract_Update).Should().BeFalse();

            var manifest = ContractManifest.CreateDefault(script.ToScriptHash());
            byte[] privkey = { 0x01,0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01};
            KeyPair key = new KeyPair(privkey);
            ECPoint pubkey = key.PublicKey;
            byte[] signature = Crypto.Default.Sign(script.ToScriptHash().ToArray(), privkey, pubkey.EncodePoint(false).Skip(1).ToArray());
            manifest.Groups = new ContractGroup[]
            {
                new ContractGroup()
                {
                    PubKey = pubkey,
                    Signature = signature
                }
            };
            var mockSnapshot = new Mock<Snapshot>();
            var state = TestUtils.GetContract();
            state.Manifest.Features = ContractFeatures.HasStorage;
            var storageItem = new StorageItem
            {
                Value = new byte[] { 0x01 },
                IsConstant = false
            };

            var storageKey = new StorageKey
            {
                ScriptHash = state.ScriptHash,
                Key = new byte[] { 0x01 }
            };
            mockSnapshot.SetupGet(p => p.Contracts).Returns(new DicDataCache<UInt160, ContractState>(state.ScriptHash, state));
            mockSnapshot.SetupGet(p => p.Storages).Returns(new DicDataCache<StorageKey, StorageItem>(storageKey, storageItem));
            engine = new ApplicationEngine(TriggerType.Application, null, mockSnapshot.Object, 0);
            engine.LoadScript(state.Script);
            engine.CurrentContext.EvaluationStack.Push(manifest.ToString());
            engine.CurrentContext.EvaluationStack.Push(script);
            InteropService.Invoke(engine, InteropService.Neo_Contract_Update).Should().BeTrue();
        }

        [TestMethod]
        public void TestContract_GetScript()
        {
            var engine = GetEngine();
            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, InteropService.Neo_Contract_GetScript).Should().BeFalse();

            var state = TestUtils.GetContract();
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<ContractState>(state));
            InteropService.Invoke(engine, InteropService.Neo_Contract_GetScript).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetByteArray().ToHexString()
                .Should().Be(state.Script.ToHexString());

            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<ContractState>(null));
            InteropService.Invoke(engine, InteropService.Neo_Contract_GetScript).Should().BeFalse();
        }

        [TestMethod]
        public void TestContract_IsPayable()
        {
            var engine = GetEngine();
            engine.CurrentContext.EvaluationStack.Push(1);
            InteropService.Invoke(engine, InteropService.Neo_Contract_IsPayable).Should().BeFalse();

            var state = TestUtils.GetContract();
            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<ContractState>(state));
            InteropService.Invoke(engine, InteropService.Neo_Contract_IsPayable).Should().BeTrue();
            engine.CurrentContext.EvaluationStack.Pop().GetBoolean()
                .Should().BeFalse();

            engine.CurrentContext.EvaluationStack.Push(new InteropInterface<ContractState>(null));
            InteropService.Invoke(engine, InteropService.Neo_Contract_IsPayable).Should().BeFalse();
        }

        [TestMethod]
        public void TestEnumerator_Create()
        {
            var engine = GetEngine();
            var arr =  new VMArray {
                new byte[]{ 0x01},
                new byte[]{ 0x02}
            };
            engine.CurrentContext.EvaluationStack.Push(arr);
            InteropService.Invoke(engine, InteropService.Neo_Enumerator_Create).Should().BeTrue();
            var ret = engine.CurrentContext.EvaluationStack.Pop();


        }
    

        private static ApplicationEngine GetEngine(bool hasContainer = false, bool hasSnapshot = false)
        {
            var tx = TestUtils.GetTransaction();
            var snapshot = TestBlockchain.GetStore().GetSnapshot().Clone();
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
            engine.LoadScript(new byte[] { 0x01 });
            return engine;
        }
    }
}