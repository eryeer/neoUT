using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.IO;
using Neo.Cryptography;
using Neo.IO;
using Neo.IO.Actors;
using Neo.Ledger;
using Neo.Network.P2P.Capabilities;
using Neo.Network.P2P.Payloads;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Neo.Network.P2P
{
    public class RemoteNode : Connection
    {
        public static bool watchSwitch = false;
        public ILoggingAdapter Log { get; } = Context.GetLogger();

        public static System.Diagnostics.Stopwatch stopwatchMessage = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchIInventory = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchRelay = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchVersionPayload = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchVerack = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchSetFilter = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchPingPayload = new System.Diagnostics.Stopwatch();

        public static long countMessage = 0;
        public static long countIInventory = 0;
        public static long countRelay = 0;
        public static long countVersionPayload = 0;
        public static long countVerack = 0;
        public static long countSetFilter = 0;
        public static long countPingPayload = 0;

        internal class Relay { public IInventory Inventory; }

        private readonly NeoSystem system;
        private readonly IActorRef protocol;
        private readonly Queue<Message> message_queue_high = new Queue<Message>();
        private readonly Queue<Message> message_queue_low = new Queue<Message>();
        private ByteString msg_buffer = ByteString.Empty;
        private BloomFilter bloom_filter;
        private bool ack = true;
        private bool verack = false;

        public IPEndPoint Listener => new IPEndPoint(Remote.Address, ListenerTcpPort);
        public int ListenerTcpPort { get; private set; } = 0;
        public VersionPayload Version { get; private set; }
        public uint LastBlockIndex { get; private set; } = 0;
        public bool IsFullNode { get; private set; } = false;

        public RemoteNode(NeoSystem system, object connection, IPEndPoint remote, IPEndPoint local)
            : base(connection, remote, local)
        {
            this.system = system;
            this.protocol = Context.ActorOf(ProtocolHandler.Props(system));
            LocalNode.Singleton.RemoteNodes.TryAdd(Self, this);

            var capabilities = new List<NodeCapability>
            {
                new FullNodeCapability(Blockchain.Singleton.Height)
            };

            if (LocalNode.Singleton.ListenerTcpPort > 0) capabilities.Add(new ServerCapability(NodeCapabilityType.TcpServer, (ushort)LocalNode.Singleton.ListenerTcpPort));
            if (LocalNode.Singleton.ListenerWsPort > 0) capabilities.Add(new ServerCapability(NodeCapabilityType.WsServer, (ushort)LocalNode.Singleton.ListenerWsPort));

            SendMessage(Message.Create(MessageCommand.Version, VersionPayload.Create(LocalNode.Nonce, LocalNode.UserAgent, capabilities.ToArray())));
        }

        private void CheckMessageQueue()
        {
            if (!verack || !ack) return;
            Queue<Message> queue = message_queue_high;
            if (queue.Count == 0)
            {
                queue = message_queue_low;
                if (queue.Count == 0) return;
            }
            SendMessage(queue.Dequeue());
        }

        private void EnqueueMessage(MessageCommand command, ISerializable payload = null)
        {
            EnqueueMessage(Message.Create(command, payload));
        }

        private void EnqueueMessage(Message message)
        {
            bool is_single = false;
            switch (message.Command)
            {
                case MessageCommand.Addr:
                case MessageCommand.GetAddr:
                case MessageCommand.GetBlocks:
                case MessageCommand.GetHeaders:
                case MessageCommand.Mempool:
                case MessageCommand.Ping:
                case MessageCommand.Pong:
                    is_single = true;
                    break;
            }
            Queue<Message> message_queue;
            switch (message.Command)
            {
                case MessageCommand.Alert:
                case MessageCommand.Consensus:
                case MessageCommand.FilterAdd:
                case MessageCommand.FilterClear:
                case MessageCommand.FilterLoad:
                case MessageCommand.GetAddr:
                case MessageCommand.Mempool:
                    message_queue = message_queue_high;
                    break;
                default:
                    message_queue = message_queue_low;
                    break;
            }
            if (!is_single || message_queue.All(p => p.Command != message.Command))
                message_queue.Enqueue(message);
            CheckMessageQueue();
        }

        protected override void OnAck()
        {
            ack = true;
            CheckMessageQueue();
        }

        protected override void OnData(ByteString data)
        {
            msg_buffer = msg_buffer.Concat(data);

            for (Message message = TryParseMessage(); message != null; message = TryParseMessage())
                protocol.Tell(message);
        }

        protected override void OnReceive(object message)
        {
            base.OnReceive(message);
            switch (message)
            {
                case Message msg:
                    if (watchSwitch)
                    {
                        stopwatchMessage.Start();
                    }
                    EnqueueMessage(msg);
                    if (watchSwitch)
                    {
                        stopwatchMessage.Stop();
                        Log.Info($"Class:RemoteNode Type: Message TimeSpan:{stopwatchMessage.Elapsed.TotalSeconds}");
                        stopwatchMessage.Reset();
                        countMessage++;
                    }
                    break;
                case IInventory inventory:
                    if (watchSwitch)
                    {
                        stopwatchIInventory.Start();
                    }
                    OnSend(inventory);
                    if (watchSwitch)
                    {
                        stopwatchIInventory.Stop();
                        Log.Info($"Class:RemoteNode Type: IInventory TimeSpan:{stopwatchIInventory.Elapsed.TotalSeconds}");
                        stopwatchIInventory.Reset();
                        countIInventory++;
                    }
                    break;
                case Relay relay:
                    if (watchSwitch)
                    {
                        stopwatchRelay.Start();
                    }
                    OnRelay(relay.Inventory);
                    if (watchSwitch)
                    {
                        stopwatchRelay.Stop();
                        Log.Info($"Class:RemoteNode Type: Relay TimeSpan:{stopwatchRelay.Elapsed.TotalSeconds}");
                        stopwatchRelay.Reset();
                        countRelay++;
                    }
                    break;
                case VersionPayload payload:
                    if (watchSwitch)
                    {
                        stopwatchVersionPayload.Start();
                    }
                    OnVersionPayload(payload);
                    if (watchSwitch)
                    {
                        stopwatchVersionPayload.Stop();
                        Log.Info($"Class:RemoteNode Type: VersionPayload TimeSpan:{stopwatchVersionPayload.Elapsed.TotalSeconds}");
                        stopwatchVersionPayload.Reset();
                        countVersionPayload++;
                    }
                    break;
                case MessageCommand.Verack:
                    if (watchSwitch)
                    {
                        stopwatchVerack.Start();
                    }
                    OnVerack();
                    if (watchSwitch)
                    {
                        stopwatchVerack.Stop();
                        Log.Info($"Class:RemoteNode Type: Verack TimeSpan:{stopwatchVerack.Elapsed.TotalSeconds}");
                        stopwatchVerack.Reset();
                        countVerack++;
                    }
                    break;
                case ProtocolHandler.SetFilter setFilter:
                    if (watchSwitch)
                    {
                        stopwatchSetFilter.Start();
                    }
                    OnSetFilter(setFilter.Filter);
                    if (watchSwitch)
                    {
                        stopwatchSetFilter.Stop();
                        Log.Info($"Class:RemoteNode Type: SetFilter TimeSpan:{stopwatchSetFilter.Elapsed.TotalSeconds}");
                        stopwatchVerack.Reset();
                        countSetFilter++;
                    }
                    break;
                case PingPayload payload:
                    if (watchSwitch)
                    {
                        stopwatchPingPayload.Start();
                    }
                    OnPingPayload(payload);
                    if (watchSwitch)
                    {
                        stopwatchPingPayload.Stop();
                        Log.Info($"Class:RemoteNode Type: SetFilter TimeSpan:{stopwatchPingPayload.Elapsed.TotalSeconds}");
                        stopwatchPingPayload.Reset();
                        countPingPayload++;
                    }
                    break;
            }
        }

        private void OnPingPayload(PingPayload payload)
        {
            if (payload.LastBlockIndex > LastBlockIndex)
                LastBlockIndex = payload.LastBlockIndex;
        }

        private void OnRelay(IInventory inventory)
        {
            if (!IsFullNode) return;
            if (inventory.InventoryType == InventoryType.TX)
            {
                if (bloom_filter != null && !bloom_filter.Test((Transaction)inventory))
                    return;
            }
            EnqueueMessage(MessageCommand.Inv, InvPayload.Create(inventory.InventoryType, inventory.Hash));
        }

        private void OnSend(IInventory inventory)
        {
            if (!IsFullNode) return;
            if (inventory.InventoryType == InventoryType.TX)
            {
                if (bloom_filter != null && !bloom_filter.Test((Transaction)inventory))
                    return;
            }
            EnqueueMessage(inventory.InventoryType.ToMessageCommand(), inventory);
        }

        private void OnSetFilter(BloomFilter filter)
        {
            bloom_filter = filter;
        }

        private void OnVerack()
        {
            verack = true;
            system.TaskManager.Tell(new TaskManager.Register { Version = Version });
            CheckMessageQueue();
        }

        private void OnVersionPayload(VersionPayload version)
        {
            Version = version;
            foreach (NodeCapability capability in version.Capabilities)
            {
                switch (capability)
                {
                    case FullNodeCapability fullNodeCapability:
                        IsFullNode = true;
                        LastBlockIndex = fullNodeCapability.StartHeight;
                        break;
                    case ServerCapability serverCapability:
                        if (serverCapability.Type == NodeCapabilityType.TcpServer)
                            ListenerTcpPort = serverCapability.Port;
                        break;
                }
            }
            if (version.Nonce == LocalNode.Nonce || version.Magic != ProtocolSettings.Default.Magic)
            {
                Disconnect(true);
                return;
            }
            if (LocalNode.Singleton.RemoteNodes.Values.Where(p => p != this).Any(p => p.Remote.Address.Equals(Remote.Address) && p.Version?.Nonce == version.Nonce))
            {
                Disconnect(true);
                return;
            }
            SendMessage(Message.Create(MessageCommand.Verack));
        }

        protected override void PostStop()
        {
            LocalNode.Singleton.RemoteNodes.TryRemove(Self, out _);
            base.PostStop();
        }

        internal static Props Props(NeoSystem system, object connection, IPEndPoint remote, IPEndPoint local)
        {
            return Akka.Actor.Props.Create(() => new RemoteNode(system, connection, remote, local)).WithMailbox("remote-node-mailbox");
        }

        private void SendMessage(Message message)
        {
            ack = false;
            SendData(ByteString.FromBytes(message.ToArray()));
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(ex =>
            {
                Disconnect(true);
                return Directive.Stop;
            }, loggingEnabled: false);
        }

        private Message TryParseMessage()
        {
            var length = Message.TryDeserialize(msg_buffer, out var msg);
            if (length <= 0) return null;

            msg_buffer = msg_buffer.Slice(length).Compact();
            return msg;
        }
    }

    internal class RemoteNodeMailbox : PriorityMailbox
    {
        public RemoteNodeMailbox(Settings settings, Config config) : base(settings, config) { }

        internal protected override bool IsHighPriority(object message)
        {
            switch (message)
            {
                case Tcp.ConnectionClosed _:
                case Connection.Timer _:
                case Connection.Ack _:
                    return true;
                default:
                    return false;
            }
        }
    }
}
