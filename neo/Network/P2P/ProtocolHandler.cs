using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Neo.Consensus;
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
        public static bool countSwitch = true;
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
        public System.Diagnostics.Stopwatch stopwatchGetDataHighPriority = new System.Diagnostics.Stopwatch();
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
        public static long countGetDataHighPriority = 0;
        public static long countGetHeaders = 0;
        public static long countHeaders = 0;
        public static long countInv = 0;
        public static long countMempool = 0;
        public static long countPing = 0;
        public static long countPong = 0;
        public static long countTransaction = 0;

        public static long countDuplicateTX = 0;

        public static double totalTimeAddr = 0;
        public static double totalTimeBlock = 0;
        public static double totalTimeConsensus = 0;
        public static double totalTimeFilterAdd = 0;
        public static double totalTimeFilterClear = 0;
        public static double totalTimeFilterLoad = 0;
        public static double totalTimeGetAddr = 0;
        public static double totalTimeGetBlocks = 0;
        public static double totalTimeGetData = 0;
        public static double totalTimeGetDataHighPriority = 0;
        public static double totalTimeGetHeaders = 0;
        public static double totalTimeHeaders = 0;
        public static double totalTimeInv = 0;
        public static double totalTimeMempool = 0;
        public static double totalTimePing = 0;
        public static double totalTimePong = 0;
        public static double totalTimeTransaction = 0;

        public class SetFilter { public BloomFilter Filter; }

        private readonly NeoSystem system;
        private readonly HashSetCache<UInt256> knownHashes;
        private readonly HashSetCache<UInt256> sentHashes;
        private VersionPayload version;
        private bool verack = false;
        private BloomFilter bloom_filter;

        public ProtocolHandler(NeoSystem system)
        {
            this.system = system;
            this.knownHashes = new HashSetCache<UInt256>(30_000);
            this.sentHashes = new HashSetCache<UInt256>(30_000);
        }

        public static long countReturnedPhase1 = 0;
        public static long countReturnedPhase2 = 0;
        public static long countReturnedPhase3 = 0;
        public static long countReturnedPhase4 = 0;
        public static long countEntryGetData = 0;

        protected override void OnReceive(object message)
        {
            //phase1
            if (!(message is Message msg))
            {
                if(countSwitch) Interlocked.Increment(ref countReturnedPhase1);
                return;
            }
            if (((Message)message).Command == MessageCommand.GetData) Interlocked.Increment(ref countEntryGetData);
            if (((Message)message).Command == MessageCommand.GetDataHighPriority)
            {
                Console.WriteLine("ProtocolHandler OnReceive TOP receive GetDataHighPriority");
            }
            if (((Message)message).Command == MessageCommand.TransactionHighPriority)
            {
                Console.WriteLine("ProtocolHandler OnReceive TOP receive TransactionHighPriority");
            }
            //phase2
            foreach (IP2PPlugin plugin in Plugin.P2PPlugins)
                if (!plugin.OnP2PMessage(msg))
                {
                    if(msg.Command == MessageCommand.GetData && countSwitch) Interlocked.Increment(ref countReturnedPhase2);
                    return;
                }
            //phase3
            if (version == null)
            {
                if (msg.Command == MessageCommand.GetData && countSwitch) Interlocked.Increment(ref countReturnedPhase3);
                if (msg.Command != MessageCommand.Version)
                {
                    
                    throw new ProtocolViolationException();
                }
                OnVersionMessageReceived((VersionPayload)msg.Payload);
                return;
            }
            //phase4
            if (!verack)
            {
                if (msg.Command == MessageCommand.GetData && countSwitch) Interlocked.Increment(ref countReturnedPhase4);
                if (msg.Command != MessageCommand.Verack)
                    throw new ProtocolViolationException();
                OnVerackMessageReceived();
                return;
            }

            double timespan = 0;
            double initialValue, computedValue;
            switch (msg.Command)
            {
                case MessageCommand.Addr:
                    stopwatchAddr.Start();
                    OnAddrMessageReceived((AddrPayload)msg.Payload);
                    stopwatchAddr.Stop();
                    timespan = stopwatchAddr.Elapsed.TotalSeconds;
                    stopwatchAddr.Reset();
                    if (watchSwitch)
                    {
                        AkkaLog.Info($"Class:ProtocolHandler Type: Addr TimeSpan:{timespan}");
                    }
                    if (countSwitch)
                    {
                        Interlocked.Increment(ref countAddr);
                        do
                        {
                            initialValue = totalTimeAddr;
                            computedValue = initialValue + timespan;
                        }
                        while (initialValue != Interlocked.CompareExchange(ref totalTimeAddr, computedValue, initialValue));
                    }
                    break;
                case MessageCommand.Block:
                    stopwatchBlock.Start();
                    OnInventoryReceived((Block)msg.Payload);
                    stopwatchBlock.Stop();
                    timespan = stopwatchBlock.Elapsed.TotalSeconds;
                    stopwatchBlock.Reset();
                    if (watchSwitch)
                    {
                        AkkaLog.Info($"Class:ProtocolHandler Type: Block TimeSpan:{timespan}");
                    }
                    if (countSwitch)
                    {
                        Interlocked.Add(ref countBlock, 1);
                        do
                        {
                            initialValue = totalTimeBlock;
                            computedValue = initialValue + timespan;
                        }
                        while (initialValue != Interlocked.CompareExchange(ref totalTimeBlock, computedValue, initialValue));
                    }
                    break;
                case MessageCommand.Consensus:
                    stopwatchConsensus.Start();
                    OnInventoryReceived((ConsensusPayload)msg.Payload);
                    stopwatchConsensus.Stop();
                    timespan = stopwatchConsensus.Elapsed.TotalSeconds;
                    stopwatchConsensus.Reset();
                    if (watchSwitch)
                    {
                        AkkaLog.Info($"Class:ProtocolHandler Type: Consensus TimeSpan:{timespan}");
                    }
                    if (countSwitch)
                    {
                        Interlocked.Add(ref countConsensus, 1);
                        do
                        {
                            initialValue = totalTimeConsensus;
                            computedValue = initialValue + timespan;
                        }
                        while (initialValue != Interlocked.CompareExchange(ref totalTimeConsensus, computedValue, initialValue));
                    }
                    break;
                case MessageCommand.FilterAdd:
                    stopwatchFilterAdd.Start();
                    OnFilterAddMessageReceived((FilterAddPayload)msg.Payload);
                    stopwatchFilterAdd.Stop();
                    timespan = stopwatchFilterAdd.Elapsed.TotalSeconds;
                    stopwatchFilterAdd.Reset();
                    if (watchSwitch)
                    {
                        AkkaLog.Info($"Class:ProtocolHandler Type: FilterAdd TimeSpan:{timespan}");
                    }
                    if (countSwitch)
                    {
                        Interlocked.Add(ref countFilterAdd, 1);
                        do
                        {
                            initialValue = totalTimeFilterAdd;
                            computedValue = initialValue + timespan;
                        }
                        while (initialValue != Interlocked.CompareExchange(ref totalTimeFilterAdd, computedValue, initialValue));
                    }
                    break;
                case MessageCommand.FilterClear:
                    stopwatchFilterClear.Start();
                    OnFilterClearMessageReceived();
                    stopwatchFilterClear.Stop();
                    timespan = stopwatchFilterClear.Elapsed.TotalSeconds;
                    stopwatchFilterClear.Reset();
                    if (watchSwitch)
                    {
                        AkkaLog.Info($"Class:ProtocolHandler Type: FilterClear TimeSpan:{timespan}");
                    }
                    if (countSwitch)
                    {
                        Interlocked.Add(ref countFilterClear, 1);
                        do
                        {
                            initialValue = totalTimeFilterClear;
                            computedValue = initialValue + timespan;
                        }
                        while (initialValue != Interlocked.CompareExchange(ref totalTimeFilterClear, computedValue, initialValue));
                    }
                    break;
                case MessageCommand.FilterLoad:
                    stopwatchFilterLoad.Start();
                    OnFilterLoadMessageReceived((FilterLoadPayload)msg.Payload);
                    stopwatchFilterLoad.Stop();
                    timespan = stopwatchFilterLoad.Elapsed.TotalSeconds;
                    stopwatchFilterLoad.Reset();
                    if (watchSwitch)
                    {
                        AkkaLog.Info($"Class:ProtocolHandler Type: FilterLoad TimeSpan:{timespan}");
                    }
                    if (countSwitch)
                    {
                        Interlocked.Add(ref countFilterLoad, 1);
                        do
                        {
                            initialValue = totalTimeFilterLoad;
                            computedValue = initialValue + timespan;
                        }
                        while (initialValue != Interlocked.CompareExchange(ref totalTimeFilterLoad, computedValue, initialValue));
                    }
                    break;
                case MessageCommand.GetAddr:
                    stopwatchGetAddr.Start();
                    OnGetAddrMessageReceived();
                    stopwatchGetAddr.Stop();
                    timespan = stopwatchGetAddr.Elapsed.TotalSeconds;
                    stopwatchGetAddr.Reset();
                    if (watchSwitch)
                    {
                        AkkaLog.Info($"Class:ProtocolHandler Type: GetAddr TimeSpan:{timespan}");
                    }
                    if (countSwitch)
                    {
                        Interlocked.Add(ref countGetAddr, 1);
                        do
                        {
                            initialValue = totalTimeGetAddr;
                            computedValue = initialValue + timespan;
                        }
                        while (initialValue != Interlocked.CompareExchange(ref totalTimeGetAddr, computedValue, initialValue));
                    }
                    break;
                case MessageCommand.GetBlocks:
                    stopwatchGetBlocks.Start();
                    OnGetBlocksMessageReceived((GetBlocksPayload)msg.Payload);
                    stopwatchGetBlocks.Stop();
                    timespan = stopwatchGetBlocks.Elapsed.TotalSeconds;
                    stopwatchGetBlocks.Reset();
                    if (watchSwitch)
                    {
                        AkkaLog.Info($"Class:ProtocolHandler Type: GetBlocks TimeSpan:{timespan}");
                    }
                    if (countSwitch)
                    {
                        Interlocked.Add(ref countGetBlocks, 1);
                        do
                        {
                            initialValue = totalTimeGetBlocks;
                            computedValue = initialValue + timespan;
                        }
                        while (initialValue != Interlocked.CompareExchange(ref totalTimeGetBlocks, computedValue, initialValue));
                    }
                    break;
                case MessageCommand.GetData:
                    stopwatchGetData.Start();
                    OnGetDataMessageReceived((InvPayload)msg.Payload);
                    stopwatchGetData.Stop();
                    timespan = stopwatchGetData.Elapsed.TotalSeconds;
                    stopwatchGetData.Reset();
                    if (watchSwitch)
                    {
                        AkkaLog.Info($"Class:ProtocolHandler Type: GetData TimeSpan:{timespan}");
                    }
                    if (countSwitch)
                    {
                        Interlocked.Increment(ref countGetData);
                        do
                        {
                            initialValue = totalTimeGetData;
                            computedValue = initialValue + timespan;
                        }
                        while (initialValue != Interlocked.CompareExchange(ref totalTimeGetData, computedValue, initialValue));
                    }
                    break;
                case MessageCommand.GetDataHighPriority:
                    AkkaLog.Info("ProtocolHandler OnReceive GetDataHighPriority");
                    stopwatchGetDataHighPriority.Start();
                    OnGetDataMessageHighPriorityReceived((InvPayload)msg.Payload);
                    stopwatchGetDataHighPriority.Stop();
                    timespan = stopwatchGetDataHighPriority.Elapsed.TotalSeconds;
                    stopwatchGetDataHighPriority.Reset();
                    if (watchSwitch)
                    {
                        AkkaLog.Info($"Class:ProtocolHandler Type: GetDataHighPriority TimeSpan:{timespan}");
                    }
                    if (countSwitch)
                    {
                        Interlocked.Increment(ref countGetDataHighPriority);
                        do
                        {
                            initialValue = totalTimeGetData;
                            computedValue = initialValue + timespan;
                        }
                        while (initialValue != Interlocked.CompareExchange(ref totalTimeGetDataHighPriority, computedValue, initialValue));
                    }
                    break;
                case MessageCommand.GetHeaders:
                    stopwatchGetHeaders.Start();
                    OnGetHeadersMessageReceived((GetBlocksPayload)msg.Payload);
                    stopwatchGetHeaders.Stop();
                    timespan = stopwatchGetHeaders.Elapsed.TotalSeconds;
                    stopwatchGetHeaders.Reset();
                    if (watchSwitch)
                    {
                        AkkaLog.Info($"Class:ProtocolHandler Type: GetHeaders TimeSpan:{timespan}");
                    }
                    if (countSwitch)
                    {
                        Interlocked.Add(ref countGetHeaders, 1);
                        do
                        {
                            initialValue = totalTimeGetHeaders;
                            computedValue = initialValue + timespan;
                        }
                        while (initialValue != Interlocked.CompareExchange(ref totalTimeGetHeaders, computedValue, initialValue));
                    }
                    break;
                case MessageCommand.Headers:
                    stopwatchHeaders.Start();
                    OnHeadersMessageReceived((HeadersPayload)msg.Payload);
                    stopwatchHeaders.Stop();
                    timespan = stopwatchHeaders.Elapsed.TotalSeconds;
                    stopwatchHeaders.Reset();
                    if (watchSwitch)
                    {
                        AkkaLog.Info($"Class:ProtocolHandler Type: Headers TimeSpan:{timespan}");
                    }
                    if (countSwitch)
                    {
                        Interlocked.Add(ref countHeaders, 1);
                        do
                        {
                            initialValue = totalTimeHeaders;
                            computedValue = initialValue + timespan;
                        }
                        while (initialValue != Interlocked.CompareExchange(ref totalTimeHeaders, computedValue, initialValue));
                    }
                    break;
                case MessageCommand.Inv:
                    stopwatchInv.Start();
                    OnInvMessageReceived((InvPayload)msg.Payload);
                    stopwatchInv.Stop();
                    timespan = stopwatchInv.Elapsed.TotalSeconds;
                    stopwatchInv.Reset();
                    if (watchSwitch)
                    {
                        AkkaLog.Info($"Class:ProtocolHandler Type: Inv TimeSpan:{timespan}");
                    }
                    if (countSwitch)
                    {
                        Interlocked.Increment(ref countInv);
                        do
                        {
                            initialValue = totalTimeInv;
                            computedValue = initialValue + timespan;
                        }
                        while (initialValue != Interlocked.CompareExchange(ref totalTimeInv, computedValue, initialValue));
                    }
                    break;
                case MessageCommand.Mempool:
                    stopwatchMempool.Start();
                    OnMemPoolMessageReceived();
                    stopwatchMempool.Stop();
                    timespan = stopwatchMempool.Elapsed.TotalSeconds;
                    stopwatchMempool.Reset();
                    if (watchSwitch)
                    {
                        AkkaLog.Info($"Class:ProtocolHandler Type: Mempool TimeSpan:{timespan}");
                    }
                    if (countSwitch)
                    {
                        Interlocked.Add(ref countMempool, 1);
                        do
                        {
                            initialValue = totalTimeMempool;
                            computedValue = initialValue + timespan;
                        }
                        while (initialValue != Interlocked.CompareExchange(ref totalTimeMempool, computedValue, initialValue));
                    }
                    break;
                case MessageCommand.Ping:
                    stopwatchPing.Start();
                    OnPingMessageReceived((PingPayload)msg.Payload);
                    stopwatchPing.Stop();
                    timespan = stopwatchPing.Elapsed.TotalSeconds;
                    stopwatchPing.Reset();
                    if (watchSwitch)
                    {
                        AkkaLog.Info($"Class:ProtocolHandler Type: Ping TimeSpan:{timespan}");
                    }
                    if (countSwitch)
                    {
                        Interlocked.Add(ref countPing, 1);
                        do
                        {
                            initialValue = totalTimePing;
                            computedValue = initialValue + timespan;
                        }
                        while (initialValue != Interlocked.CompareExchange(ref totalTimePing, computedValue, initialValue));
                    }
                    break;
                case MessageCommand.Pong:
                    stopwatchPong.Start();
                    OnPongMessageReceived((PingPayload)msg.Payload);
                    stopwatchPong.Stop();
                    timespan = stopwatchPong.Elapsed.TotalSeconds;
                    stopwatchPong.Reset();
                    if (watchSwitch)
                    {
                        AkkaLog.Info($"Class:ProtocolHandler Type: Pong TimeSpan:{timespan}");
                    }
                    if (countSwitch)
                    {
                        Interlocked.Add(ref countPong, 1);
                        do
                        {
                            initialValue = totalTimePong;
                            computedValue = initialValue + timespan;
                        }
                        while (initialValue != Interlocked.CompareExchange(ref totalTimePong, computedValue, initialValue));
                    }
                    break;
                case MessageCommand.Transaction:
                case MessageCommand.TransactionHighPriority:
                    stopwatchTransaction.Start();
                    if (msg.Payload.Size <= Transaction.MaxTransactionSize)
                        OnInventoryReceived((Transaction)msg.Payload);
                    stopwatchTransaction.Stop();
                    timespan = stopwatchTransaction.Elapsed.TotalSeconds;
                    stopwatchTransaction.Reset();
                    if (watchSwitch)
                    {
                        AkkaLog.Info($"Class:ProtocolHandler Type: Transaction TimeSpan:{timespan}");
                    }
                    if (countSwitch)
                    {
                        Interlocked.Add(ref countTransaction, 1);
                        do
                        {
                            initialValue = totalTimeTransaction;
                            computedValue = initialValue + timespan;
                        }
                        while (initialValue != Interlocked.CompareExchange(ref totalTimeTransaction, computedValue, initialValue));
                    }
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

        private void OnGetDataMessageHighPriorityReceived(InvPayload payload)
        {
            UInt256[] hashes = payload.Hashes.Where(p => sentHashes.Add(p)).ToArray();
            foreach (UInt256 hash in hashes)
            {
                switch (payload.Type)
                {
                    case InventoryType.TX:
                        Transaction tx = Blockchain.Singleton.GetTransaction(hash);
                        if (tx != null)
                            Context.Parent.Tell(Message.Create(MessageCommand.TransactionHighPriority, tx));
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
            UInt256[] temphashes2=payload.Hashes.Where(p => knownHashes.Contains(p)).ToArray();
            if (temphashes2.Length > 0) {
                if(countSwitch) Interlocked.Increment(ref countDuplicateTX);
                //AkkaLog.Info($"Class:ProtocolHandler Type: Inv ：重复消息个数" + temphashes2.Length);
            }
            UInt256[] hashes = payload.Hashes.Where(p => knownHashes.Add(p) && !sentHashes.Contains(p)).ToArray();
            if (hashes.Length == 0) return;
            switch (payload.Type)
            {
                case InventoryType.Block:
                    using (Snapshot snapshot = Blockchain.Singleton.GetSnapshot())
                        hashes = hashes.Where(p => !snapshot.ContainsBlock(p)).ToArray();
                    break;
                case InventoryType.TX:
                    using (Snapshot snapshot = Blockchain.Singleton.GetSnapshot()) {
                        UInt256[] temphashes = hashes.Where(p => !snapshot.ContainsTransaction(p)).ToArray();
                        if (temphashes.Length < hashes.Length) {
                            if (countSwitch)
                            {
                                long initialValue, computedValue;
                                do
                                {
                                    initialValue = countDuplicateTX;
                                    computedValue = initialValue + (hashes.Length - temphashes.Length);
                                }
                                while (initialValue != Interlocked.CompareExchange(ref countDuplicateTX, computedValue, initialValue));
                            }
                            //AkkaLog.Info($"Class:ProtocolHandler Type: Inv :重复消息已经被过滤,重复个数"+ (hashes.Length- temphashes.Length));
                        }
                    }
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
                case MessageCommand.GetDataHighPriority:
                case MessageCommand.TransactionHighPriority:
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
                case MessageCommand.GetHeaders:
                case MessageCommand.Mempool:
                    return queue.OfType<Message>().Any(p => p.Command == msg.Command);
                default:
                    return false;
            }
        }
    }
}
