using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Neo.Cryptography.ECC;
using Neo.IO;
using Neo.IO.Actors;
using Neo.IO.Caching;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.Plugins;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Neo.Ledger
{
    public sealed partial class Blockchain : UntypedActor
    {
        public static bool watchSwitchBlockchain = false;
        public static bool countSwitchBlockchain = true;
        public ILoggingAdapter AkkaLog { get; } = Context.GetLogger();
        public partial class ApplicationExecuted { }
        public class PersistCompleted { public Block Block; }
        public class Import { public IEnumerable<Block> Blocks; }
        public class ImportCompleted { }
        public class FillMemoryPool { public IEnumerable<Transaction> Transactions; }
        public class FillCompleted { }

        public static readonly uint MillisecondsPerBlock = ProtocolSettings.Default.MillisecondsPerBlock;
        public const uint DecrementInterval = 2000000;
        public const int MaxValidators = 1024;
        public static readonly uint[] GenerationAmount = { 6, 5, 4, 3, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };
        public static readonly TimeSpan TimePerBlock = TimeSpan.FromMilliseconds(MillisecondsPerBlock);
        public static readonly ECPoint[] StandbyValidators = ProtocolSettings.Default.StandbyValidators.OfType<string>().Select(p => ECPoint.DecodePoint(p.HexToBytes(), ECCurve.Secp256r1)).ToArray();

        public static readonly Block GenesisBlock = new Block
        {
            PrevHash = UInt256.Zero,
            Timestamp = (new DateTime(2016, 7, 15, 15, 8, 21, DateTimeKind.Utc)).ToTimestampMS(),
            Index = 0,
            NextConsensus = GetConsensusAddress(StandbyValidators),
            Witness = new Witness
            {
                InvocationScript = new byte[0],
                VerificationScript = new[] { (byte)OpCode.PUSHT }
            },
            ConsensusData = new ConsensusData
            {
                PrimaryIndex = 0,
                Nonce = 2083236893
            },
            Transactions = new[] { DeployNativeContracts() }
        };

        private readonly static byte[] onPersistNativeContractScript;
        private const int MaxTxToReverifyPerIdle = 10;
        private static readonly object lockObj = new object();
        private readonly NeoSystem system;
        private readonly List<UInt256> header_index = new List<UInt256>();
        private uint stored_header_count = 0;
        private readonly Dictionary<UInt256, Block> block_cache = new Dictionary<UInt256, Block>();
        private readonly Dictionary<uint, LinkedList<Block>> block_cache_unverified = new Dictionary<uint, LinkedList<Block>>();
        internal readonly RelayCache ConsensusRelayCache = new RelayCache(100);
        private Snapshot currentSnapshot;

        public Store Store { get; }
        public MemoryPool MemPool { get; }
        public uint Height => currentSnapshot.Height;
        public uint HeaderHeight => currentSnapshot.HeaderHeight;
        public UInt256 CurrentBlockHash => currentSnapshot.CurrentBlockHash;
        public UInt256 CurrentHeaderHash => currentSnapshot.CurrentHeaderHash;

        private DateTime lasttime = DateTime.Now;
        private bool isNormalNode = false;

        public void CheckCount(Block block)
        {
            //print block timespan and TPS
            double timespan = (DateTime.Now - lasttime).TotalSeconds;
            lasttime = DateTime.Now;
            Console.WriteLine($"Block Height: {block.Index} Time spent since last relay = " + timespan + ", TPS = " + block.Transactions.Length / timespan);

            //Connection
            if (Connection.countSwitch)
            {
                AkkaLog.Info($"Class: Connection Type: Timer Count: {Connection.countTimer} averageTimespan: {Connection.totalTimeTimer / Connection.countTimer}");
                AkkaLog.Info($"Class: Connection Type: Ack Count: {Connection.countAck} averageTimespan: {Connection.totalTimeAck / Connection.countAck}");
                AkkaLog.Info($"Class: Connection Type: Received Count: {Connection.countReceived} averageTimespan: {Connection.totalTimeReceived / Connection.countReceived}");
                AkkaLog.Info($"Class: Connection Type: ConnectionClosed Count: {Connection.countConnectionClosed} averageTimespan: {Connection.totalTimeConnectionClosed / Connection.countConnectionClosed}");
                AkkaLog.Info($"Class: Connection Type: TPSTimer Count: {Connection.countTPSTimer} averageTimespan: {Connection.totalTimeTPSTimer / Connection.countTPSTimer}");
                Connection.countTimer = 0;
                Connection.countAck = 0;
                Connection.countReceived = 0;
                Connection.countConnectionClosed = 0;
                Connection.countTPSTimer = 0;

                Connection.totalTimeTimer = 0;
                Connection.totalTimeAck = 0;
                Connection.totalTimeReceived = 0;
                Connection.totalTimeConnectionClosed = 0;
                Connection.totalTimeTPSTimer = 0;
            }

            //RemoteNode
            if (RemoteNode.countSwitchRemoteNode)
            {
                AkkaLog.Info($"Class: RemoteNode Type: Message Count: {RemoteNode.countMessage} averageTimespan: {RemoteNode.totalTimeMessage / RemoteNode.countMessage}");
                AkkaLog.Info($"Class: RemoteNode Type: IInventory Count: {RemoteNode.countIInventory} averageTimespan: {RemoteNode.totalTimeIInventory / RemoteNode.countIInventory}");
                AkkaLog.Info($"Class: RemoteNode Type: Relay Count: {RemoteNode.countRelay} averageTimespan: {RemoteNode.totalTimeRelay / RemoteNode.countRelay}");
                AkkaLog.Info($"Class: RemoteNode Type: VersionPayload Count: {RemoteNode.countVersionPayload} averageTimespan: {RemoteNode.totalTimeVersionPayload / RemoteNode.countVersionPayload}");
                AkkaLog.Info($"Class: RemoteNode Type: Verack Count: {RemoteNode.countVerack} averageTimespan: {RemoteNode.totalTimeVerack / RemoteNode.countVerack}");
                AkkaLog.Info($"Class: RemoteNode Type: SetFilter Count: {RemoteNode.countSetFilter} averageTimespan: {RemoteNode.totalTimeSetFilter / RemoteNode.countSetFilter}");
                AkkaLog.Info($"Class: RemoteNode Type: PingPayload Count: {RemoteNode.countPingPayload} averageTimespan: {RemoteNode.totalTimePingPayload / RemoteNode.countPingPayload}");
                RemoteNode.countMessage = 0;
                RemoteNode.countIInventory = 0;
                RemoteNode.countRelay = 0;
                RemoteNode.countVersionPayload = 0;
                RemoteNode.countVerack = 0;
                RemoteNode.countSetFilter = 0;
                RemoteNode.countPingPayload = 0;

                RemoteNode.totalTimeMessage = 0;
                RemoteNode.totalTimeIInventory = 0;
                RemoteNode.totalTimeRelay = 0;
                RemoteNode.totalTimeVersionPayload = 0;
                RemoteNode.totalTimeVerack = 0;
                RemoteNode.totalTimeSetFilter = 0;
                RemoteNode.totalTimePingPayload = 0;
            }

            //ProtocolHandler
            if (ProtocolHandler.countSwitch)
            {
                AkkaLog.Info($"Class: ProtocolHandler Type: Addr Count: {ProtocolHandler.countAddr} averageTimespan: {ProtocolHandler.totalTimeAddr / ProtocolHandler.countAddr}");
                AkkaLog.Info($"Class: ProtocolHandler Type: Block Count: {ProtocolHandler.countBlock} averageTimespan: {ProtocolHandler.totalTimeBlock / ProtocolHandler.countBlock}");
                AkkaLog.Info($"Class: ProtocolHandler Type: Consensus Count: {ProtocolHandler.countConsensus} averageTimespan: {ProtocolHandler.totalTimeConsensus / ProtocolHandler.countConsensus}");
                AkkaLog.Info($"Class: ProtocolHandler Type: FilterAdd Count: {ProtocolHandler.countFilterAdd} averageTimespan: {ProtocolHandler.totalTimeFilterAdd / ProtocolHandler.countFilterAdd}");
                AkkaLog.Info($"Class: ProtocolHandler Type: FilterClear Count: {ProtocolHandler.countFilterClear} averageTimespan: {ProtocolHandler.totalTimeFilterClear / ProtocolHandler.countFilterClear}");
                AkkaLog.Info($"Class: ProtocolHandler Type: FilterLoad Count: {ProtocolHandler.countFilterLoad} averageTimespan: {ProtocolHandler.totalTimeFilterLoad / ProtocolHandler.countFilterLoad}");
                AkkaLog.Info($"Class: ProtocolHandler Type: GetAddr Count: {ProtocolHandler.countGetAddr} averageTimespan: {ProtocolHandler.totalTimeGetAddr / ProtocolHandler.countGetAddr}");
                AkkaLog.Info($"Class: ProtocolHandler Type: GetBlocks Count: {ProtocolHandler.countGetBlocks} averageTimespan: {ProtocolHandler.totalTimeGetBlocks / ProtocolHandler.countGetBlocks}");
                AkkaLog.Info($"Class: ProtocolHandler Type: GetData Count: {ProtocolHandler.countGetData} averageTimespan: {ProtocolHandler.totalTimeGetData / ProtocolHandler.countGetData}");
                AkkaLog.Info($"Class: ProtocolHandler Type: GetHeaders Count: {ProtocolHandler.countGetHeaders} averageTimespan: {ProtocolHandler.totalTimeGetHeaders / ProtocolHandler.countGetHeaders}");
                AkkaLog.Info($"Class: ProtocolHandler Type: Headers Count: {ProtocolHandler.countHeaders} averageTimespan: {ProtocolHandler.totalTimeHeaders / ProtocolHandler.countHeaders}");
                AkkaLog.Info($"Class: ProtocolHandler Type: Inv Count: {ProtocolHandler.countInv} averageTimespan: {ProtocolHandler.totalTimeInv / ProtocolHandler.countInv}");
                AkkaLog.Info($"Class: ProtocolHandler Type: Mempool Count: {ProtocolHandler.countMempool} averageTimespan: {ProtocolHandler.totalTimeMempool / ProtocolHandler.countMempool}");
                AkkaLog.Info($"Class: ProtocolHandler Type: Ping Count: {ProtocolHandler.countPing} averageTimespan: {ProtocolHandler.totalTimePing / ProtocolHandler.countPing}");
                AkkaLog.Info($"Class: ProtocolHandler Type: Pong Count: {ProtocolHandler.countPong} averageTimespan: {ProtocolHandler.totalTimePong / ProtocolHandler.countPong}");
                AkkaLog.Info($"Class: ProtocolHandler Type: Transaction Count: {ProtocolHandler.countTransaction} averageTimespan: {ProtocolHandler.totalTimeTransaction / ProtocolHandler.countTransaction}");
                ProtocolHandler.countAddr = 0;
                ProtocolHandler.countBlock = 0;
                ProtocolHandler.countConsensus = 0;
                ProtocolHandler.countFilterAdd = 0;
                ProtocolHandler.countFilterClear = 0;
                ProtocolHandler.countFilterLoad = 0;
                ProtocolHandler.countGetAddr = 0;
                ProtocolHandler.countGetBlocks = 0;
                ProtocolHandler.countGetData = 0;
                ProtocolHandler.countGetHeaders = 0;
                ProtocolHandler.countHeaders = 0;
                ProtocolHandler.countInv = 0;
                ProtocolHandler.countMempool = 0;
                ProtocolHandler.countPing = 0;
                ProtocolHandler.countPong = 0;
                ProtocolHandler.countTransaction = 0;

                ProtocolHandler.totalTimeAddr = 0;
                ProtocolHandler.totalTimeBlock = 0;
                ProtocolHandler.totalTimeConsensus = 0;
                ProtocolHandler.totalTimeFilterAdd = 0;
                ProtocolHandler.totalTimeFilterClear = 0;
                ProtocolHandler.totalTimeFilterLoad = 0;
                ProtocolHandler.totalTimeGetAddr = 0;
                ProtocolHandler.totalTimeGetBlocks = 0;
                ProtocolHandler.totalTimeGetData = 0;
                ProtocolHandler.totalTimeGetHeaders = 0;
                ProtocolHandler.totalTimeHeaders = 0;
                ProtocolHandler.totalTimeInv = 0;
                ProtocolHandler.totalTimeMempool = 0;
                ProtocolHandler.totalTimePing = 0;
                ProtocolHandler.totalTimePong = 0;
                ProtocolHandler.totalTimeTransaction = 0;
            }

            //TaskManager
            if (TaskManager.countSwitch)
            {
                AkkaLog.Info($"Class: TaskManager Type: Register Count: {TaskManager.countRegister} averageTimespan: {TaskManager.totalTimeRegister / TaskManager.countRegister}");
                AkkaLog.Info($"Class: TaskManager Type: NewTasks Count: {TaskManager.countNewTasks} averageTimespan: {TaskManager.totalTimeNewTasks / TaskManager.countNewTasks}");
                AkkaLog.Info($"Class: TaskManager Type: TaskCompleted Count: {TaskManager.countTaskCompleted} averageTimespan: {TaskManager.totalTimeTaskCompleted / TaskManager.countTaskCompleted}");
                AkkaLog.Info($"Class: TaskManager Type: HeaderTaskCompleted Count: {TaskManager.countHeaderTaskCompleted} averageTimespan: {TaskManager.totalTimeHeaderTaskCompleted / TaskManager.countHeaderTaskCompleted}");
                AkkaLog.Info($"Class: TaskManager Type: RestartTasks Count: {TaskManager.countRestartTasks} averageTimespan: {TaskManager.totalTimeRestartTasks / TaskManager.countRestartTasks}");
                AkkaLog.Info($"Class: TaskManager Type: Timer Count: {TaskManager.countTimer} averageTimespan: {TaskManager.totalTimeTimer / TaskManager.countTimer}");
                AkkaLog.Info($"Class: TaskManager Type: Terminated Count: {TaskManager.countTerminated} averageTimespan: {TaskManager.totalTimeTerminated / TaskManager.countTerminated}");
                TaskManager.countRegister = 0;
                TaskManager.countNewTasks = 0;
                TaskManager.countTaskCompleted = 0;
                TaskManager.countHeaderTaskCompleted = 0;
                TaskManager.countRestartTasks = 0;
                TaskManager.countTimer = 0;
                TaskManager.countTerminated = 0;

                TaskManager.totalTimeRegister = 0;
                TaskManager.totalTimeNewTasks = 0;
                TaskManager.totalTimeTaskCompleted = 0;
                TaskManager.totalTimeHeaderTaskCompleted = 0;
                TaskManager.totalTimeRestartTasks = 0;
                TaskManager.totalTimeTimer = 0;
                TaskManager.totalTimeTerminated = 0;
            }

            if (Blockchain.countSwitchBlockchain)
            {
                AkkaLog.Info($"Class: Blockchain Type: Import Count: {Blockchain.countImport} averageTimespan: {Blockchain.totalTimeImport / Blockchain.countImport}");
                AkkaLog.Info($"Class: Blockchain Type: FillMemoryPool Count: {Blockchain.countFillMemoryPool} averageTimespan: {Blockchain.totalTimeFillMemoryPool / Blockchain.countFillMemoryPool}");
                AkkaLog.Info($"Class: Blockchain Type: HeaderArray Count: {Blockchain.countHeaderArray} averageTimespan: {Blockchain.totalTimeHeaderArray / Blockchain.countHeaderArray}");
                AkkaLog.Info($"Class: Blockchain Type: Block Count: {Blockchain.countBlock} averageTimespan: {Blockchain.totalTimeBlock / Blockchain.countBlock}");
                AkkaLog.Info($"Class: Blockchain Type: TransactionArray Count: {Blockchain.countTransactionArray} averageTimespan: {Blockchain.totalTimeTransactionArray / Blockchain.countTransactionArray}");
                AkkaLog.Info($"Class: Blockchain Type: Transaction Count: {Blockchain.countTransaction} averageTimespan: {Blockchain.totalTimeTransaction / Blockchain.countTransaction}");
                AkkaLog.Info($"Class: Blockchain Type: ConsensusPayload Count: {Blockchain.countConsensusPayload} averageTimespan: {Blockchain.totalTimeConsensusPayload / Blockchain.countConsensusPayload}");
                AkkaLog.Info($"Class: Blockchain Type: Idle Count: {Blockchain.countIdle} averageTimespan: {Blockchain.totalTimeIdle / Blockchain.countIdle}");
                Blockchain.countImport = 0;
                Blockchain.countFillMemoryPool = 0;
                Blockchain.countHeaderArray = 0;
                Blockchain.countBlock = 0;
                Blockchain.countTransactionArray = 0;
                Blockchain.countTransaction = 0;
                Blockchain.countConsensusPayload = 0;
                Blockchain.countIdle = 0;

                Blockchain.totalTimeImport = 0;
                Blockchain.totalTimeFillMemoryPool = 0;
                Blockchain.totalTimeHeaderArray = 0;
                Blockchain.totalTimeBlock = 0;
                Blockchain.totalTimeTransactionArray = 0;
                Blockchain.totalTimeTransaction = 0;
                Blockchain.totalTimeConsensusPayload = 0;
                Blockchain.totalTimeIdle = 0;
            }
            if (Peer.countSwitchPeer)
            {
                AkkaLog.Info($"Class: Peer Type: ChannelsConfig Count: {Peer.countChannelsConfig} averageTimespan: {Peer.totalTimeChannelsConfig / Peer.countChannelsConfig}");
                AkkaLog.Info($"Class: Peer Type: Timer Count: {Peer.countTimer} averageTimespan: {Peer.totalTimeTimer / Peer.countTimer}");
                AkkaLog.Info($"Class: Peer Type: Peers Count: {Peer.countPeers} averageTimespan: {Peer.totalTimePeers / Peer.countPeers}");
                AkkaLog.Info($"Class: Peer Type: Connect Count: {Peer.countConnect} averageTimespan: {Peer.totalTimeConnect / Peer.countConnect}");
                AkkaLog.Info($"Class: Peer Type: WsConnected Count: {Peer.countWsConnected} averageTimespan: {Peer.totalTimeWsConnected / Peer.countWsConnected}");
                AkkaLog.Info($"Class: Peer Type: TcpConnected Count: {Peer.countTcpConnected} averageTimespan: {Peer.totalTimeTcpConnected / Peer.countTcpConnected}");
                AkkaLog.Info($"Class: Peer Type: TcpBound Count: {Peer.countTcpBound} averageTimespan: {Peer.totalTimeTcpBound / Peer.countTcpBound}");
                AkkaLog.Info($"Class: Peer Type: TcpCommandFailed Count: {Peer.countTcpCommandFailed} averageTimespan: {Peer.totalTimeTcpCommandFailed / Peer.countTcpCommandFailed}");
                AkkaLog.Info($"Class: Peer Type: Terminated Count: {Peer.countTerminated} averageTimespan: {Peer.totalTimeTerminated / Peer.countTerminated}");
                Peer.countChannelsConfig = 0;
                Peer.countTimer = 0;
                Peer.countPeers = 0;
                Peer.countConnect = 0;
                Peer.countWsConnected = 0;
                Peer.countTcpConnected = 0;
                Peer.countTcpBound = 0;
                Peer.countTcpCommandFailed = 0;
                Peer.countTerminated = 0;

                Peer.totalTimeChannelsConfig = 0;
                Peer.totalTimeTimer = 0;
                Peer.totalTimePeers = 0;
                Peer.totalTimeConnect = 0;
                Peer.totalTimeWsConnected = 0;
                Peer.totalTimeTcpConnected = 0;
                Peer.totalTimeTcpBound = 0;
                Peer.totalTimeTcpCommandFailed = 0;
                Peer.totalTimeTerminated = 0;
            }

            if (LocalNode.countSwitchLocalNode)
            {
                AkkaLog.Info($"Class: LocalNode Type: Message Count: {LocalNode.countMessage} averageTimespan: {LocalNode.totalTimeMessage / LocalNode.countMessage}");
                AkkaLog.Info($"Class: LocalNode Type: Relay Count: {LocalNode.countRelay} averageTimespan: {LocalNode.totalTimeRelay / LocalNode.countRelay}");
                AkkaLog.Info($"Class: LocalNode Type: RelayDirectly Count: {LocalNode.countRelayDirectly} averageTimespan: {LocalNode.totalTimeRelayDirectly / LocalNode.countRelayDirectly}");
                AkkaLog.Info($"Class: LocalNode Type: SendDirectly Count: {LocalNode.countSendDirectly} averageTimespan: {LocalNode.totalTimeSendDirectly / LocalNode.countSendDirectly}");
                LocalNode.countMessage = 0;
                LocalNode.countRelay = 0;
                LocalNode.countRelayDirectly = 0;
                LocalNode.countSendDirectly = 0;

                LocalNode.totalTimeMessage = 0;
                LocalNode.totalTimeRelay = 0;
                LocalNode.totalTimeRelayDirectly = 0;
                LocalNode.totalTimeSendDirectly = 0;
            }


        }

        private static Blockchain singleton;
        public static Blockchain Singleton
        {
            get
            {
                while (singleton == null) Thread.Sleep(10);
                return singleton;
            }
        }

        static Blockchain()
        {
            GenesisBlock.RebuildMerkleRoot();

            NativeContract[] contracts = { NativeContract.GAS, NativeContract.NEO };
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                foreach (NativeContract contract in contracts)
                    sb.EmitAppCall(contract.Hash, "onPersist");

                onPersistNativeContractScript = sb.ToArray();
            }
        }

        public Blockchain(NeoSystem system, Store store)
        {
            this.system = system;
            this.MemPool = new MemoryPool(system, ProtocolSettings.Default.MemoryPoolMaxTransactions);
            this.Store = store;
            lock (lockObj)
            {
                if (singleton != null)
                    throw new InvalidOperationException();
                header_index.AddRange(store.GetHeaderHashList().Find().OrderBy(p => (uint)p.Key).SelectMany(p => p.Value.Hashes));
                stored_header_count += (uint)header_index.Count;
                if (stored_header_count == 0)
                {
                    header_index.AddRange(store.GetBlocks().Find().OrderBy(p => p.Value.Index).Select(p => p.Key));
                }
                else
                {
                    HashIndexState hashIndex = store.GetHeaderHashIndex().Get();
                    if (hashIndex.Index >= stored_header_count)
                    {
                        DataCache<UInt256, TrimmedBlock> cache = store.GetBlocks();
                        for (UInt256 hash = hashIndex.Hash; hash != header_index[(int)stored_header_count - 1];)
                        {
                            header_index.Insert((int)stored_header_count, hash);
                            hash = cache[hash].PrevHash;
                        }
                    }
                }
                if (header_index.Count == 0)
                {
                    Persist(GenesisBlock);
                }
                else
                {
                    UpdateCurrentSnapshot();
                    MemPool.LoadPolicy(currentSnapshot);
                }
                singleton = this;
            }
        }

        public bool ContainsBlock(UInt256 hash)
        {
            if (block_cache.ContainsKey(hash)) return true;
            return Store.ContainsBlock(hash);
        }

        public bool ContainsTransaction(UInt256 hash)
        {
            if (MemPool.ContainsKey(hash)) return true;
            return Store.ContainsTransaction(hash);
        }

        private static Transaction DeployNativeContracts()
        {
            byte[] script;
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitSysCall(InteropService.Neo_Native_Deploy);
                script = sb.ToArray();
            }
            return new Transaction
            {
                Version = 0,
                Script = script,
                Sender = (new[] { (byte)OpCode.PUSHT }).ToScriptHash(),
                SystemFee = 0,
                Attributes = new TransactionAttribute[0],
                Cosigners = new Cosigner[0],
                Witnesses = new[]
                {
                    new Witness
                    {
                        InvocationScript = new byte[0],
                        VerificationScript = new[] { (byte)OpCode.PUSHT }
                    }
                }
            };
        }

        public Block GetBlock(UInt256 hash)
        {
            if (block_cache.TryGetValue(hash, out Block block))
                return block;
            return Store.GetBlock(hash);
        }

        public UInt256 GetBlockHash(uint index)
        {
            if (header_index.Count <= index) return null;
            return header_index[(int)index];
        }

        public static UInt160 GetConsensusAddress(ECPoint[] validators)
        {
            return Contract.CreateMultiSigRedeemScript(validators.Length - (validators.Length - 1) / 3, validators).ToScriptHash();
        }

        public Snapshot GetSnapshot()
        {
            return Store.GetSnapshot();
        }

        public Transaction GetTransaction(UInt256 hash)
        {
            if (MemPool.TryGetValue(hash, out Transaction transaction))
                return transaction;
            return Store.GetTransaction(hash);
        }

        private void OnImport(IEnumerable<Block> blocks)
        {
            foreach (Block block in blocks)
            {
                if (block.Index <= Height) continue;
                if (block.Index != Height + 1)
                    throw new InvalidOperationException();
                Persist(block);
                SaveHeaderHashList();
            }
            Sender.Tell(new ImportCompleted());
        }

        private void AddUnverifiedBlockToCache(Block block)
        {
            if (!block_cache_unverified.TryGetValue(block.Index, out LinkedList<Block> blocks))
            {
                blocks = new LinkedList<Block>();
                block_cache_unverified.Add(block.Index, blocks);
            }

            blocks.AddLast(block);
        }

        private void OnFillMemoryPool(IEnumerable<Transaction> transactions)
        {
            // Invalidate all the transactions in the memory pool, to avoid any failures when adding new transactions.
            MemPool.InvalidateAllTransactions();

            // Add the transactions to the memory pool
            foreach (var tx in transactions)
            {
                if (Store.ContainsTransaction(tx.Hash))
                    continue;
                if (!NativeContract.Policy.CheckPolicy(tx, currentSnapshot))
                    continue;
                // First remove the tx if it is unverified in the pool.
                MemPool.TryRemoveUnVerified(tx.Hash, out _);
                // Verify the the transaction
                if (!tx.Verify(currentSnapshot, MemPool.GetSenderFee(tx.Sender)))
                    continue;
                // Add to the memory pool
                MemPool.TryAdd(tx.Hash, tx);
            }
            // Transactions originally in the pool will automatically be reverified based on their priority.

            Sender.Tell(new FillCompleted());
        }

        private RelayResultReason OnNewBlock(Block block)
        {
            if (block.Index <= Height)
                return RelayResultReason.AlreadyExists;
            if (block_cache.ContainsKey(block.Hash))
                return RelayResultReason.AlreadyExists;
            if (block.Index - 1 >= header_index.Count)
            {
                AddUnverifiedBlockToCache(block);
                return RelayResultReason.UnableToVerify;
            }
            if (block.Index == header_index.Count)
            {
                if (!block.Verify(currentSnapshot))
                    return RelayResultReason.Invalid;
            }
            else
            {
                if (!block.Hash.Equals(header_index[(int)block.Index]))
                    return RelayResultReason.Invalid;
            }
            if (block.Index == Height + 1)
            {
                Block block_persist = block;
                List<Block> blocksToPersistList = new List<Block>();
                while (true)
                {
                    blocksToPersistList.Add(block_persist);
                    if (block_persist.Index + 1 >= header_index.Count) break;
                    UInt256 hash = header_index[(int)block_persist.Index + 1];
                    if (!block_cache.TryGetValue(hash, out block_persist)) break;
                }

                int blocksPersisted = 0;
                foreach (Block blockToPersist in blocksToPersistList)
                {
                    block_cache_unverified.Remove(blockToPersist.Index);
                    Persist(blockToPersist);

                    // 15000 is the default among of seconds per block, while MilliSecondsPerBlock is the current
                    uint extraBlocks = (15000 - MillisecondsPerBlock) / 1000;

                    if (blocksPersisted++ < blocksToPersistList.Count - (2 + Math.Max(0, extraBlocks))) continue;
                    // Empirically calibrated for relaying the most recent 2 blocks persisted with 15s network
                    // Increase in the rate of 1 block per second in configurations with faster blocks

                    if (blockToPersist.Index + 100 >= header_index.Count)
                        system.LocalNode.Tell(new LocalNode.RelayDirectly { Inventory = blockToPersist });
                }
                SaveHeaderHashList();

                if (block_cache_unverified.TryGetValue(Height + 1, out LinkedList<Block> unverifiedBlocks))
                {
                    foreach (var unverifiedBlock in unverifiedBlocks)
                        Self.Tell(unverifiedBlock, ActorRefs.NoSender);
                    block_cache_unverified.Remove(Height + 1);
                }
            }
            else
            {
                block_cache.Add(block.Hash, block);
                if (block.Index + 100 >= header_index.Count)
                    system.LocalNode.Tell(new LocalNode.RelayDirectly { Inventory = block });
                if (block.Index == header_index.Count)
                {
                    header_index.Add(block.Hash);
                    using (Snapshot snapshot = GetSnapshot())
                    {
                        snapshot.Blocks.Add(block.Hash, block.Header.Trim());
                        snapshot.HeaderHashIndex.GetAndChange().Hash = block.Hash;
                        snapshot.HeaderHashIndex.GetAndChange().Index = block.Index;
                        SaveHeaderHashList(snapshot);
                        snapshot.Commit();
                    }
                    UpdateCurrentSnapshot();
                }
            }
            return RelayResultReason.Succeed;
        }

        private RelayResultReason OnNewConsensus(ConsensusPayload payload)
        {
            if (!payload.Verify(currentSnapshot)) return RelayResultReason.Invalid;
            system.Consensus?.Tell(payload);
            ConsensusRelayCache.Add(payload);
            system.LocalNode.Tell(new LocalNode.RelayDirectly { Inventory = payload });
            return RelayResultReason.Succeed;
        }

        private void OnNewHeaders(Header[] headers)
        {
            using (Snapshot snapshot = GetSnapshot())
            {
                foreach (Header header in headers)
                {
                    if (header.Index - 1 >= header_index.Count) break;
                    if (header.Index < header_index.Count) continue;
                    if (!header.Verify(snapshot)) break;
                    header_index.Add(header.Hash);
                    snapshot.Blocks.Add(header.Hash, header.Trim());
                    snapshot.HeaderHashIndex.GetAndChange().Hash = header.Hash;
                    snapshot.HeaderHashIndex.GetAndChange().Index = header.Index;
                }
                SaveHeaderHashList(snapshot);
                snapshot.Commit();
            }
            UpdateCurrentSnapshot();
            system.TaskManager.Tell(new TaskManager.HeaderTaskCompleted(), Sender);
        }

        private RelayResultReason OnNewTransaction(Transaction transaction, bool relay)
        {
            if (ContainsTransaction(transaction.Hash))
                return RelayResultReason.AlreadyExists;
            if (!MemPool.CanTransactionFitInPool(transaction))
                return RelayResultReason.OutOfMemory;
            if (!transaction.Verify(currentSnapshot, MemPool.GetSenderFee(transaction.Sender)))
                return RelayResultReason.Invalid;
            if (!NativeContract.Policy.CheckPolicy(transaction, currentSnapshot))
                return RelayResultReason.PolicyFail;

            if (!MemPool.TryAdd(transaction.Hash, transaction))
                return RelayResultReason.OutOfMemory;
            if (relay)
                system.LocalNode.Tell(new LocalNode.RelayDirectly { Inventory = transaction });
            return RelayResultReason.Succeed;
        }

        private void OnPersistCompleted(Block block)
        {
            block_cache.Remove(block.Hash);
            MemPool.UpdatePoolForBlockPersisted(block, currentSnapshot);
            Context.System.EventStream.Publish(new PersistCompleted { Block = block });
        }

        public System.Diagnostics.Stopwatch stopwatchImport = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchFillMemoryPool = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchHeaderArray = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchBlock = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchTransactionArray = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchTransaction = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchConsensusPayload = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchIdle = new System.Diagnostics.Stopwatch();

        public static long countImport = 0;
        public static long countFillMemoryPool = 0;
        public static long countHeaderArray = 0;
        public static long countBlock = 0;
        public static long countTransactionArray = 0;
        public static long countTransaction = 0;
        public static long countConsensusPayload = 0;
        public static long countIdle = 0;

        public static double totalTimeImport = 0;
        public static double totalTimeFillMemoryPool = 0;
        public static double totalTimeHeaderArray = 0;
        public static double totalTimeBlock = 0;
        public static double totalTimeTransactionArray = 0;
        public static double totalTimeTransaction = 0;
        public static double totalTimeConsensusPayload = 0;
        public static double totalTimeIdle = 0;
        protected override void OnReceive(object message)
        {
            double timespan = 0;
            switch (message)
            {
                case Import import:
                    stopwatchImport.Start();
                    OnImport(import.Blocks);
                    stopwatchImport.Stop();
                    timespan=stopwatchImport.Elapsed.TotalSeconds;
                    stopwatchImport.Reset();
                    if (watchSwitchBlockchain)
                    {
                        AkkaLog.Info($"Class:Blockchain Type: Import TimeSpan:{timespan}");
                    }
                    if (countSwitchBlockchain) {
                        countImport++;
                        totalTimeImport+= timespan;
                    }
                    break;
                case FillMemoryPool fill:
                    stopwatchFillMemoryPool.Start();
                    OnFillMemoryPool(fill.Transactions);
                    stopwatchFillMemoryPool.Stop();
                    timespan= stopwatchFillMemoryPool.Elapsed.TotalSeconds;
                    stopwatchFillMemoryPool.Reset();
                    if (watchSwitchBlockchain)
                    {
                        AkkaLog.Info($"Class:Blockchain Type: FillMemoryPool TimeSpan:{timespan}");
                    }
                    if (countSwitchBlockchain) {
                        countFillMemoryPool++;
                        totalTimeFillMemoryPool += timespan;
                    }
                    break;
                case Header[] headers:
                    stopwatchHeaderArray.Start();
                    OnNewHeaders(headers);
                    stopwatchHeaderArray.Stop();
                    timespan = stopwatchHeaderArray.Elapsed.TotalSeconds;
                    stopwatchHeaderArray.Reset();
                    if (watchSwitchBlockchain)
                    {
                        AkkaLog.Info($"Class:Blockchain Type: Header TimeSpan:{timespan}");
                    }
                    if (countSwitchBlockchain) {
                        countHeaderArray++;
                        totalTimeHeaderArray += timespan;
                    }
                    break;
                case Block block:
                    stopwatchBlock.Start();
                    if(isNormalNode) CheckCount(block);
                    Sender.Tell(OnNewBlock(block));
                    stopwatchBlock.Stop();
                    timespan = stopwatchBlock.Elapsed.TotalSeconds;
                    stopwatchBlock.Reset();
                    if (watchSwitchBlockchain)
                    {
                        AkkaLog.Info($"Class:Blockchain Type: Block TimeSpan:{timespan}");
                    }
                    if (countSwitchBlockchain) {
                        countBlock++;
                        totalTimeBlock += timespan;
                    }
                    break;
                case Transaction[] transactions:
                    {
                        stopwatchTransactionArray.Start();
                        foreach (var tx in transactions) OnNewTransaction(tx, false);
                        stopwatchTransactionArray.Stop();
                        timespan= stopwatchTransactionArray.Elapsed.TotalSeconds;
                        stopwatchTransactionArray.Reset();
                        if (watchSwitchBlockchain)
                        {
                            AkkaLog.Info($"Class:Blockchain Type: TransactionArray TimeSpan:{timespan}");
                        }
                        if (countSwitchBlockchain) {
                            countTransactionArray++;
                            totalTimeTransactionArray += timespan;
                        }
                        break;
                    }
                case Transaction transaction:
                    stopwatchTransaction.Start();
                    Sender.Tell(OnNewTransaction(transaction, true));
                    stopwatchTransaction.Stop();
                    timespan= stopwatchTransaction.Elapsed.TotalSeconds;
                    stopwatchTransaction.Reset();
                    if (watchSwitchBlockchain)
                    {
                        AkkaLog.Info($"Class:Blockchain Type: Transaction TimeSpan:{timespan}");
                    }
                    if (countSwitchBlockchain) {
                        countTransaction++;
                        totalTimeTransaction += timespan;
                    }
                    break;
                case ConsensusPayload payload:
                    stopwatchConsensusPayload.Start();
                    Sender.Tell(OnNewConsensus(payload));
                    stopwatchConsensusPayload.Stop();
                    timespan= stopwatchConsensusPayload.Elapsed.TotalSeconds;
                    stopwatchConsensusPayload.Reset();
                    if (watchSwitchBlockchain)
                    {
                        AkkaLog.Info($"Class:Blockchain Type: ConsensusPayload TimeSpan:{timespan}");
                    }
                    if (countSwitchBlockchain) {
                        countConsensusPayload++;
                        totalTimeConsensusPayload += timespan;
                    }
                    break;
                case Idle _:
                    stopwatchIdle.Start();
                    if (MemPool.ReVerifyTopUnverifiedTransactionsIfNeeded(MaxTxToReverifyPerIdle, currentSnapshot))
                        Self.Tell(Idle.Instance, ActorRefs.NoSender);
                    stopwatchIdle.Stop();
                    timespan= stopwatchIdle.Elapsed.TotalSeconds;
                    stopwatchIdle.Reset();
                    if (watchSwitchBlockchain)
                    {
                        AkkaLog.Info($"Class:Blockchain Type: Idle TimeSpan:{timespan}");
                    }
                    if (countSwitchBlockchain) {
                        countIdle++;
                        totalTimeIdle += timespan;
                    }
                    break;
            }
        }

        private void Persist(Block block)
        {
            using (Snapshot snapshot = GetSnapshot())
            {
                List<ApplicationExecuted> all_application_executed = new List<ApplicationExecuted>();
                snapshot.PersistingBlock = block;
                if (block.Index > 0)
                {
                    using (ApplicationEngine engine = new ApplicationEngine(TriggerType.System, null, snapshot, 0, true))
                    {
                        engine.LoadScript(onPersistNativeContractScript);
                        if (engine.Execute() != VMState.HALT) throw new InvalidOperationException();
                        ApplicationExecuted application_executed = new ApplicationExecuted(engine);
                        Context.System.EventStream.Publish(application_executed);
                        all_application_executed.Add(application_executed);
                    }
                }
                snapshot.Blocks.Add(block.Hash, block.Trim());
                foreach (Transaction tx in block.Transactions)
                {
                    var state = new TransactionState
                    {
                        BlockIndex = block.Index,
                        Transaction = tx
                    };

                    snapshot.Transactions.Add(tx.Hash, state);

                    using (ApplicationEngine engine = new ApplicationEngine(TriggerType.Application, tx, snapshot.Clone(), tx.SystemFee))
                    {
                        engine.LoadScript(tx.Script);
                        state.VMState = engine.Execute();
                        if (state.VMState == VMState.HALT)
                        {
                            engine.Snapshot.Commit();
                        }
                        ApplicationExecuted application_executed = new ApplicationExecuted(engine);
                        Context.System.EventStream.Publish(application_executed);
                        all_application_executed.Add(application_executed);
                    }
                }
                snapshot.BlockHashIndex.GetAndChange().Set(block);
                if (block.Index == header_index.Count)
                {
                    header_index.Add(block.Hash);
                    snapshot.HeaderHashIndex.GetAndChange().Set(block);
                }
                foreach (IPersistencePlugin plugin in Plugin.PersistencePlugins)
                    plugin.OnPersist(snapshot, all_application_executed);
                snapshot.Commit();
                List<Exception> commitExceptions = null;
                foreach (IPersistencePlugin plugin in Plugin.PersistencePlugins)
                {
                    try
                    {
                        plugin.OnCommit(snapshot);
                    }
                    catch (Exception ex)
                    {
                        if (plugin.ShouldThrowExceptionFromCommit(ex))
                        {
                            if (commitExceptions == null)
                                commitExceptions = new List<Exception>();

                            commitExceptions.Add(ex);
                        }
                    }
                }
                if (commitExceptions != null) throw new AggregateException(commitExceptions);
            }
            UpdateCurrentSnapshot();
            OnPersistCompleted(block);
        }

        protected override void PostStop()
        {
            base.PostStop();
            currentSnapshot?.Dispose();
        }

        public static Props Props(NeoSystem system, Store store)
        {
            return Akka.Actor.Props.Create(() => new Blockchain(system, store)).WithMailbox("blockchain-mailbox");
        }

        private void SaveHeaderHashList(Snapshot snapshot = null)
        {
            if ((header_index.Count - stored_header_count < 2000))
                return;
            bool snapshot_created = snapshot == null;
            if (snapshot_created) snapshot = GetSnapshot();
            try
            {
                while (header_index.Count - stored_header_count >= 2000)
                {
                    snapshot.HeaderHashList.Add(stored_header_count, new HeaderHashList
                    {
                        Hashes = header_index.Skip((int)stored_header_count).Take(2000).ToArray()
                    });
                    stored_header_count += 2000;
                }
                if (snapshot_created) snapshot.Commit();
            }
            finally
            {
                if (snapshot_created) snapshot.Dispose();
            }
        }

        private void UpdateCurrentSnapshot()
        {
            Interlocked.Exchange(ref currentSnapshot, GetSnapshot())?.Dispose();
        }
    }

    internal class BlockchainMailbox : PriorityMailbox
    {
        public BlockchainMailbox(Akka.Actor.Settings settings, Config config)
            : base(settings, config)
        {
        }

        internal protected override bool IsHighPriority(object message)
        {
            switch (message)
            {
                case Header[] _:
                case Block _:
                case ConsensusPayload _:
                case Terminated _:
                    return true;
                default:
                    return false;
            }
        }
    }
}
