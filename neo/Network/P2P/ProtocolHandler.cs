using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Neo.Cryptography;
using Neo.IO;
using Neo.IO.Actors;
using Neo.IO.Caching;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.Plugins;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

namespace Neo.Network.P2P
{
    internal class ProtocolHandler : UntypedActor
    {
        public static bool watchSwitch = false;
        public static bool countSwitch = false;
        public Akka.Event.ILoggingAdapter AkkaLog { get; } = Context.GetLogger();

        public System.Diagnostics.Stopwatch stopwatchAddr = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchBlock = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchConsensus = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchFilterAdd = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchFilterClear = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchFilterLoad = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchGetAddr = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchGetBlocks = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchGetData = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchGetHeaders = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchHeaders = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchInv = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchMempool = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchPing = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchPong = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchTransaction = new System.Diagnostics.Stopwatch();

        public static long countAddr = 0;
        public static long countBlock = 0;
        public static long countConsensus = 0;
        public static long countFilterAdd = 0;
        public static long countFilterClear = 0;
        public static long countFilterLoad = 0;
        public static long countGetAddr = 0;
        public static long countGetBlocks = 0;
        public static long countGetData = 0;
        public static long countGetHeaders = 0;
        public static long countHeaders = 0;
        public static long countInv = 0;
        public static long countMempool = 0;
        public static long countPing = 0;
        public static long countPong = 0;
        public static long countTransaction = 0;

        public class SetFilter { public BloomFilter Filter; }

        private readonly NeoSystem system;
        private readonly FIFOSet<UInt256> knownHashes;
        private readonly FIFOSet<UInt256> sentHashes;
        private VersionPayload version;
        private bool verack = false;
        private BloomFilter bloom_filter;

        public ProtocolHandler(NeoSystem system)
        {
            this.system = system;
            this.knownHashes = new FIFOSet<UInt256>(Blockchain.Singleton.MemPool.Capacity * 2);
            this.sentHashes = new FIFOSet<UInt256>(Blockchain.Singleton.MemPool.Capacity * 2);
        }

        protected override void OnReceive(object message)
        {
            if (!(message is Message msg)) return;
            foreach (IP2PPlugin plugin in Plugin.P2PPlugins)
                if (!plugin.OnP2PMessage(msg))
                    return;
            if (version == null)
            {
                if (msg.Command != MessageCommand.Version)
                    throw new ProtocolViolationException();
                OnVersionMessageReceived((VersionPayload)msg.Payload);
                return;
            }
            if (!verack)
            {
                if (msg.Command != MessageCommand.Verack)
                    throw new ProtocolViolationException();
                OnVerackMessageReceived();
                return;
            }
            switch (msg.Command)
            {
                case MessageCommand.Addr:
                    if (watchSwitch)
                    {
                        stopwatchAddr.Start();
                    }
                    OnAddrMessageReceived((AddrPayload)msg.Payload);
                    if (watchSwitch)
                    {
                        stopwatchAddr.Stop();
                        AkkaLog.Info($"Class:ProtocolHandler Type: Addr TimeSpan:{stopwatchAddr.Elapsed.TotalSeconds}");
                        stopwatchAddr.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countAddr,1);
                    break;
                case MessageCommand.Block:
                    if (watchSwitch)
                    {
                        stopwatchBlock.Start();
                    }
                    OnInventoryReceived((Block)msg.Payload);
                    if (watchSwitch)
                    {
                        stopwatchBlock.Stop();
                        AkkaLog.Info($"Class:ProtocolHandler Type: Block TimeSpan:{stopwatchBlock.Elapsed.TotalSeconds}");
                        stopwatchBlock.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countBlock, 1);
                    break;
                case MessageCommand.Consensus:
                    if (watchSwitch)
                    {
                        stopwatchConsensus.Start();
                    }
                    OnInventoryReceived((ConsensusPayload)msg.Payload);
                    if (watchSwitch)
                    {
                        stopwatchConsensus.Stop();
                        AkkaLog.Info($"Class:ProtocolHandler Type: Consensus TimeSpan:{stopwatchConsensus.Elapsed.TotalSeconds}");
                        stopwatchConsensus.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countConsensus, 1);
                    break;
                case MessageCommand.FilterAdd:
                    if (watchSwitch)
                    {
                        stopwatchFilterAdd.Start();
                    }
                    OnFilterAddMessageReceived((FilterAddPayload)msg.Payload);
                    if (watchSwitch)
                    {
                        stopwatchFilterAdd.Stop();
                        AkkaLog.Info($"Class:ProtocolHandler Type: FilterAdd TimeSpan:{stopwatchFilterAdd.Elapsed.TotalSeconds}");
                        stopwatchFilterAdd.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countFilterAdd, 1); 
                    break;
                case MessageCommand.FilterClear:
                    if (watchSwitch)
                    {
                        stopwatchFilterClear.Start();
                    }
                    OnFilterClearMessageReceived();
                    if (watchSwitch)
                    {
                        stopwatchFilterClear.Stop();
                        AkkaLog.Info($"Class:ProtocolHandler Type: FilterClear TimeSpan:{stopwatchFilterClear.Elapsed.TotalSeconds}");
                        stopwatchFilterClear.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countFilterClear, 1); 
                    break;
                case MessageCommand.FilterLoad:
                    if (watchSwitch)
                    {
                        stopwatchFilterLoad.Start();
                    }
                    OnFilterLoadMessageReceived((FilterLoadPayload)msg.Payload);
                    if (watchSwitch)
                    {
                        stopwatchFilterLoad.Stop();
                        AkkaLog.Info($"Class:ProtocolHandler Type: FilterLoad TimeSpan:{stopwatchFilterLoad.Elapsed.TotalSeconds}");
                        stopwatchFilterLoad.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countFilterLoad, 1);
                    break;
                case MessageCommand.GetAddr:
                    if (watchSwitch)
                    {
                        stopwatchGetAddr.Start();
                    }
                    OnGetAddrMessageReceived();
                    if (watchSwitch)
                    {
                        stopwatchGetAddr.Stop();
                        AkkaLog.Info($"Class:ProtocolHandler Type: GetAddr TimeSpan:{stopwatchGetAddr.Elapsed.TotalSeconds}");
                        stopwatchGetAddr.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countGetAddr, 1); 
                    break;
                case MessageCommand.GetBlocks:
                    if (watchSwitch)
                    {
                        stopwatchGetBlocks.Start();
                    }
                    OnGetBlocksMessageReceived((GetBlocksPayload)msg.Payload);
                    if (watchSwitch)
                    {
                        stopwatchGetBlocks.Stop();
                        AkkaLog.Info($"Class:ProtocolHandler Type: GetBlocks TimeSpan:{stopwatchGetBlocks.Elapsed.TotalSeconds}");
                        stopwatchGetBlocks.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countGetBlocks, 1);
                    break;
                case MessageCommand.GetData:
                    if (watchSwitch)
                    {
                        stopwatchGetData.Start();
                    }
                    OnGetDataMessageReceived((InvPayload)msg.Payload);
                    if (watchSwitch)
                    {
                        stopwatchGetData.Stop();
                        AkkaLog.Info($"Class:ProtocolHandler Type: GetData TimeSpan:{stopwatchGetData.Elapsed.TotalSeconds}");
                        stopwatchGetData.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countGetData, 1); 
                    break;
                case MessageCommand.GetHeaders:
                    if (watchSwitch)
                    {
                        stopwatchGetHeaders.Start();
                    }
                    OnGetHeadersMessageReceived((GetBlocksPayload)msg.Payload);
                    if (watchSwitch)
                    {
                        stopwatchGetHeaders.Stop();
                        AkkaLog.Info($"Class:ProtocolHandler Type: GetHeaders TimeSpan:{stopwatchGetHeaders.Elapsed.TotalSeconds}");
                        stopwatchGetHeaders.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countGetHeaders, 1); 
                    break;
                case MessageCommand.Headers:
                    if (watchSwitch)
                    {
                        stopwatchHeaders.Start();
                    }
                    OnHeadersMessageReceived((HeadersPayload)msg.Payload);
                    if (watchSwitch)
                    {
                        stopwatchHeaders.Stop();
                        AkkaLog.Info($"Class:ProtocolHandler Type: Headers TimeSpan:{stopwatchHeaders.Elapsed.TotalSeconds}");
                        stopwatchHeaders.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countHeaders, 1);
                    break;
                case MessageCommand.Inv:
                    if (watchSwitch)
                    {
                        stopwatchInv.Start();
                    }
                    OnInvMessageReceived((InvPayload)msg.Payload);
                    if (watchSwitch)
                    {
                        stopwatchInv.Stop();
                        AkkaLog.Info($"Class:ProtocolHandler Type: Inv TimeSpan:{stopwatchInv.Elapsed.TotalSeconds}");
                        stopwatchInv.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countInv, 1); 
                    break;
                case MessageCommand.Mempool:
                    if (watchSwitch)
                    {
                        stopwatchMempool.Start();
                    }
                    OnMemPoolMessageReceived();
                    if (watchSwitch)
                    {
                        stopwatchMempool.Stop();
                        AkkaLog.Info($"Class:ProtocolHandler Type: Mempool TimeSpan:{stopwatchMempool.Elapsed.TotalSeconds}");
                        stopwatchMempool.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countMempool, 1); 
                    break;
                case MessageCommand.Ping:
                    if (watchSwitch)
                    {
                        stopwatchPing.Start();
                    }
                    OnPingMessageReceived((PingPayload)msg.Payload);
                    if (watchSwitch)
                    {
                        stopwatchPing.Stop();
                        AkkaLog.Info($"Class:ProtocolHandler Type: Ping TimeSpan:{stopwatchPing.Elapsed.TotalSeconds}");
                        stopwatchPing.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countPing, 1);
                    break;
                case MessageCommand.Pong:
                    if (watchSwitch)
                    {
                        stopwatchPong.Start();
                    }
                    OnPongMessageReceived((PingPayload)msg.Payload);
                    if (watchSwitch)
                    {
                        stopwatchPong.Stop();
                        AkkaLog.Info($"Class:ProtocolHandler Type: Pong TimeSpan:{stopwatchPong.Elapsed.TotalSeconds}");
                        stopwatchPong.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countPong, 1); 
                    break;
                case MessageCommand.Transaction:
                    if (watchSwitch)
                    {
                        stopwatchTransaction.Start();
                    }
                    if (msg.Payload.Size <= Transaction.MaxTransactionSize)
                        OnInventoryReceived((Transaction)msg.Payload);
                    if (watchSwitch)
                    {
                        stopwatchTransaction.Stop();
                        AkkaLog.Info($"Class:ProtocolHandler Type: Transaction TimeSpan:{stopwatchTransaction.Elapsed.TotalSeconds}");
                        stopwatchTransaction.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countTransaction, 1); 
                    break;
                case MessageCommand.Verack:
                case MessageCommand.Version:
                    throw new ProtocolViolationException();
                case MessageCommand.Alert:
                case MessageCommand.MerkleBlock:
                case MessageCommand.NotFound:
                case MessageCommand.Reject:
                default: break;
            }
        }

        private void OnAddrMessageReceived(AddrPayload payload)
        {
            system.LocalNode.Tell(new Peer.Peers
            {
                EndPoints = payload.AddressList.Select(p => p.EndPoint).Where(p => p.Port > 0)
            });
        }

        private void OnFilterAddMessageReceived(FilterAddPayload payload)
        {
            if (bloom_filter != null)
                bloom_filter.Add(payload.Data);
        }

        private void OnFilterClearMessageReceived()
        {
            bloom_filter = null;
            Context.Parent.Tell(new SetFilter { Filter = null });
        }

        private void OnFilterLoadMessageReceived(FilterLoadPayload payload)
        {
            bloom_filter = new BloomFilter(payload.Filter.Length * 8, payload.K, payload.Tweak, payload.Filter);
            Context.Parent.Tell(new SetFilter { Filter = bloom_filter });
        }

        private void OnGetAddrMessageReceived()
        {
            Random rand = new Random();
            IEnumerable<RemoteNode> peers = LocalNode.Singleton.RemoteNodes.Values
                .Where(p => p.ListenerTcpPort > 0)
                .GroupBy(p => p.Remote.Address, (k, g) => g.First())
                .OrderBy(p => rand.Next())
                .Take(AddrPayload.MaxCountToSend);
            NetworkAddressWithTime[] networkAddresses = peers.Select(p => NetworkAddressWithTime.Create(p.Listener.Address, p.Version.Timestamp, p.Version.Capabilities)).ToArray();
            if (networkAddresses.Length == 0) return;
            Context.Parent.Tell(Message.Create(MessageCommand.Addr, AddrPayload.Create(networkAddresses)));
        }

        private void OnGetBlocksMessageReceived(GetBlocksPayload payload)
        {
            UInt256 hash = payload.HashStart;
            int count = payload.Count < 0 ? InvPayload.MaxHashesCount : payload.Count;
            TrimmedBlock state = Blockchain.Singleton.Store.GetBlocks().TryGet(hash);
            if (state == null) return;
            List<UInt256> hashes = new List<UInt256>();
            for (uint i = 1; i <= count; i++)
            {
                uint index = state.Index + i;
                if (index > Blockchain.Singleton.Height)
                    break;
                hash = Blockchain.Singleton.GetBlockHash(index);
                if (hash == null) break;
                hashes.Add(hash);
            }
            if (hashes.Count == 0) return;
            Context.Parent.Tell(Message.Create(MessageCommand.Inv, InvPayload.Create(InventoryType.Block, hashes.ToArray())));
        }

        private void OnGetDataMessageReceived(InvPayload payload)
        {
            UInt256[] hashes = payload.Hashes.Where(p => sentHashes.Add(p)).ToArray();
            foreach (UInt256 hash in hashes)
            {
                switch (payload.Type)
                {
                    case InventoryType.TX:
                        Transaction tx = Blockchain.Singleton.GetTransaction(hash);
                        if (tx != null)
                            Context.Parent.Tell(Message.Create(MessageCommand.Transaction, tx));
                        break;
                    case InventoryType.Block:
                        Block block = Blockchain.Singleton.GetBlock(hash);
                        if (block != null)
                        {
                            if (bloom_filter == null)
                            {
                                Context.Parent.Tell(Message.Create(MessageCommand.Block, block));
                            }
                            else
                            {
                                BitArray flags = new BitArray(block.Transactions.Select(p => bloom_filter.Test(p)).ToArray());
                                Context.Parent.Tell(Message.Create(MessageCommand.MerkleBlock, MerkleBlockPayload.Create(block, flags)));
                            }
                        }
                        break;
                    case InventoryType.Consensus:
                        if (Blockchain.Singleton.ConsensusRelayCache.TryGet(hash, out IInventory inventoryConsensus))
                            Context.Parent.Tell(Message.Create(MessageCommand.Consensus, inventoryConsensus));
                        break;
                }
            }
        }

        private void OnGetHeadersMessageReceived(GetBlocksPayload payload)
        {
            UInt256 hash = payload.HashStart;
            int count = payload.Count < 0 ? HeadersPayload.MaxHeadersCount : payload.Count;
            DataCache<UInt256, TrimmedBlock> cache = Blockchain.Singleton.Store.GetBlocks();
            TrimmedBlock state = cache.TryGet(hash);
            if (state == null) return;
            List<Header> headers = new List<Header>();
            for (uint i = 1; i <= count; i++)
            {
                uint index = state.Index + i;
                hash = Blockchain.Singleton.GetBlockHash(index);
                if (hash == null) break;
                Header header = cache.TryGet(hash)?.Header;
                if (header == null) break;
                headers.Add(header);
            }
            if (headers.Count == 0) return;
            Context.Parent.Tell(Message.Create(MessageCommand.Headers, HeadersPayload.Create(headers)));
        }

        private void OnHeadersMessageReceived(HeadersPayload payload)
        {
            if (payload.Headers.Length == 0) return;
            system.Blockchain.Tell(payload.Headers, Context.Parent);
        }

        private void OnInventoryReceived(IInventory inventory)
        {
            system.TaskManager.Tell(new TaskManager.TaskCompleted { Hash = inventory.Hash }, Context.Parent);
            system.LocalNode.Tell(new LocalNode.Relay { Inventory = inventory });
        }

        private void OnInvMessageReceived(InvPayload payload)
        {
            UInt256[] hashes = payload.Hashes.Where(p => knownHashes.Add(p) && !sentHashes.Contains(p)).ToArray();
            if (hashes.Length == 0) return;
            switch (payload.Type)
            {
                case InventoryType.Block:
                    using (Snapshot snapshot = Blockchain.Singleton.GetSnapshot())
                        hashes = hashes.Where(p => !snapshot.ContainsBlock(p)).ToArray();
                    break;
                case InventoryType.TX:
                    using (Snapshot snapshot = Blockchain.Singleton.GetSnapshot())
                        hashes = hashes.Where(p => !snapshot.ContainsTransaction(p)).ToArray();
                    break;
            }
            if (hashes.Length == 0) return;
            system.TaskManager.Tell(new TaskManager.NewTasks { Payload = InvPayload.Create(payload.Type, hashes) }, Context.Parent);
        }

        private void OnMemPoolMessageReceived()
        {
            foreach (InvPayload payload in InvPayload.CreateGroup(InventoryType.TX, Blockchain.Singleton.MemPool.GetVerifiedTransactions().Select(p => p.Hash).ToArray()))
                Context.Parent.Tell(Message.Create(MessageCommand.Inv, payload));
        }

        private void OnPingMessageReceived(PingPayload payload)
        {
            Context.Parent.Tell(payload);
            Context.Parent.Tell(Message.Create(MessageCommand.Pong, PingPayload.Create(Blockchain.Singleton.Height, payload.Nonce)));
        }

        private void OnPongMessageReceived(PingPayload payload)
        {
            Context.Parent.Tell(payload);
        }

        private void OnVerackMessageReceived()
        {
            verack = true;
            Context.Parent.Tell(MessageCommand.Verack);
        }

        private void OnVersionMessageReceived(VersionPayload payload)
        {
            version = payload;
            Context.Parent.Tell(payload);
        }

        public static Props Props(NeoSystem system)
        {
            return Akka.Actor.Props.Create(() => new ProtocolHandler(system)).WithMailbox("protocol-handler-mailbox");
        }
    }

    internal class ProtocolHandlerMailbox : PriorityMailbox
    {
        public ProtocolHandlerMailbox(Settings settings, Config config)
            : base(settings, config)
        {
        }

        internal protected override bool IsHighPriority(object message)
        {
            if (!(message is Message msg)) return false;
            switch (msg.Command)
            {
                case MessageCommand.Consensus:
                case MessageCommand.FilterAdd:
                case MessageCommand.FilterClear:
                case MessageCommand.FilterLoad:
                case MessageCommand.Verack:
                case MessageCommand.Version:
                case MessageCommand.Alert:
                    return true;
                default:
                    return false;
            }
        }

        internal protected override bool ShallDrop(object message, IEnumerable queue)
        {
            if (!(message is Message msg)) return true;
            switch (msg.Command)
            {
                case MessageCommand.GetAddr:
                case MessageCommand.GetBlocks:
                case MessageCommand.GetData:
                case MessageCommand.GetHeaders:
                case MessageCommand.Mempool:
                    return queue.OfType<Message>().Any(p => p.Command == msg.Command);
                default:
                    return false;
            }
        }
    }
}
