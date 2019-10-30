using Akka.Actor;
using Akka.Event;
using Neo.IO;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;

namespace Neo.Network.P2P
{
    public class LocalNode : Peer
    {
        public static bool watchSwitchLocalNode = false;
        public static bool countSwitchLocalNode = false;
        public ILoggingAdapter Log { get; } = Context.GetLogger();
        public class Relay { public IInventory Inventory; }
        internal class RelayDirectly { public IInventory Inventory; }
        internal class SendDirectly { public IInventory Inventory; }

        public const uint ProtocolVersion = 0;

        private static readonly object lockObj = new object();
        private readonly NeoSystem system;
        internal readonly ConcurrentDictionary<IActorRef, RemoteNode> RemoteNodes = new ConcurrentDictionary<IActorRef, RemoteNode>();

        public int ConnectedCount => RemoteNodes.Count;
        public int UnconnectedCount => UnconnectedPeers.Count;
        public static readonly uint Nonce;
        public static string UserAgent { get; set; }

        private static LocalNode singleton;
        public static LocalNode Singleton
        {
            get
            {
                while (singleton == null) Thread.Sleep(10);
                return singleton;
            }
        }

        static LocalNode()
        {
            Random rand = new Random();
            Nonce = (uint)rand.Next();
            UserAgent = $"/{Assembly.GetExecutingAssembly().GetName().Name}:{Assembly.GetExecutingAssembly().GetVersion()}/";
        }

        public LocalNode(NeoSystem system)
        {
            lock (lockObj)
            {
                if (singleton != null)
                    throw new InvalidOperationException();
                this.system = system;
                singleton = this;
            }
        }

        private void BroadcastMessage(MessageCommand command, ISerializable payload = null)
        {
            BroadcastMessage(Message.Create(command, payload));
        }

        private void BroadcastMessage(Message message)
        {
            Connections.Tell(message);
        }

        private static IPEndPoint GetIPEndpointFromHostPort(string hostNameOrAddress, int port)
        {
            if (IPAddress.TryParse(hostNameOrAddress, out IPAddress ipAddress))
                return new IPEndPoint(ipAddress, port);
            IPHostEntry entry;
            try
            {
                entry = Dns.GetHostEntry(hostNameOrAddress);
            }
            catch (SocketException)
            {
                return null;
            }
            ipAddress = entry.AddressList.FirstOrDefault(p => p.AddressFamily == AddressFamily.InterNetwork || p.IsIPv6Teredo);
            if (ipAddress == null) return null;
            return new IPEndPoint(ipAddress, port);
        }

        private static IEnumerable<IPEndPoint> GetIPEndPointsFromSeedList(int seedsToTake)
        {
            if (seedsToTake > 0)
            {
                Random rand = new Random();
                foreach (string hostAndPort in ProtocolSettings.Default.SeedList.OrderBy(p => rand.Next()))
                {
                    if (seedsToTake == 0) break;
                    string[] p = hostAndPort.Split(':');
                    IPEndPoint seed;
                    try
                    {
                        seed = GetIPEndpointFromHostPort(p[0], int.Parse(p[1]));
                    }
                    catch (AggregateException)
                    {
                        continue;
                    }
                    if (seed == null) continue;
                    seedsToTake--;
                    yield return seed;
                }
            }
        }

        public IEnumerable<RemoteNode> GetRemoteNodes()
        {
            return RemoteNodes.Values;
        }

        public IEnumerable<IPEndPoint> GetUnconnectedPeers()
        {
            return UnconnectedPeers;
        }

        protected override void NeedMorePeers(int count)
        {
            count = Math.Max(count, 5);
            if (ConnectedPeers.Count > 0)
            {
                BroadcastMessage(MessageCommand.GetAddr);
            }
            else
            {
                AddPeers(GetIPEndPointsFromSeedList(count));
            }
        }
        public System.Diagnostics.Stopwatch stopwatchMessage = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchRelay = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchRelayDirectly = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchSendDirectly = new System.Diagnostics.Stopwatch();

        public static long countMessage = 0;
        public static long countRelay = 0;
        public static long countRelayDirectly = 0;
        public static long countSendDirectly = 0;

        public static double totalTimeMessage = 0;
        public static double totalTimeRelay = 0;
        public static double totalTimeRelayDirectly = 0;
        public static double totalTimeSendDirectly = 0;
        protected override void OnReceive(object message)
        {
            base.OnReceive(message);
            double timespan = 0;
            switch (message)
            {
                case Message msg:
                    stopwatchMessage.Start();
                    BroadcastMessage(msg);
                    stopwatchMessage.Stop();
                    timespan= stopwatchMessage.Elapsed.TotalSeconds;
                    stopwatchMessage.Reset();
                    if (watchSwitchLocalNode)
                    {
                        Log.Info($"Class:LocalNode Type: Message TimeSpan:{timespan}");
                    }
                    if (countSwitchLocalNode)
                    {
                        countMessage++;
                        totalTimeMessage += timespan;
                    }
                    break;
                case Relay relay:
                    stopwatchRelay.Start();
                    OnRelay(relay.Inventory);
                    stopwatchRelay.Stop();
                    timespan = stopwatchRelay.Elapsed.TotalSeconds;
                    stopwatchRelay.Reset();
                    if (watchSwitchLocalNode)
                    {
                        Log.Info($"Class:LocalNode Type: Relay TimeSpan:{timespan}");
                    }
                    if (countSwitchLocalNode) {
                        countRelay++;
                        totalTimeRelay += timespan;
                    } 
                    break;
                case RelayDirectly relay:
                    stopwatchRelayDirectly.Start();
                    OnRelayDirectly(relay.Inventory);
                    stopwatchRelayDirectly.Stop();
                    timespan = stopwatchRelayDirectly.Elapsed.TotalSeconds;
                    stopwatchRelayDirectly.Reset();
                    if (watchSwitchLocalNode)
                    {
                        Log.Info($"Class:LocalNode Type: RelayDirectly TimeSpan:{timespan}");
                    }
                    if (countSwitchLocalNode) {
                        countRelayDirectly++;
                        totalTimeRelayDirectly += timespan;
                    }
                    break;
                case SendDirectly send:
                    stopwatchSendDirectly.Start();
                    OnSendDirectly(send.Inventory);
                    stopwatchSendDirectly.Stop();
                    timespan = stopwatchSendDirectly.Elapsed.TotalSeconds;
                    stopwatchSendDirectly.Reset();
                    if (watchSwitchLocalNode)
                    {
                        Log.Info($"Class:LocalNode Type: SendDirectly TimeSpan:{timespan}");
                    }
                    if (countSwitchLocalNode) {
                        countSendDirectly++;
                        totalTimeSendDirectly += timespan;
                    } 
                    break;
                case RelayResultReason _:
                    break;
            }
        }

        private void OnRelay(IInventory inventory)
        {
            if (inventory is Transaction transaction)
                system.Consensus?.Tell(transaction);
            system.Blockchain.Tell(inventory);
        }

        private void OnRelayDirectly(IInventory inventory)
        {
            Connections.Tell(new RemoteNode.Relay { Inventory = inventory });
        }

        private void OnSendDirectly(IInventory inventory)
        {
            Connections.Tell(inventory);
        }

        public static Props Props(NeoSystem system)
        {
            return Akka.Actor.Props.Create(() => new LocalNode(system));
        }

        protected override Props ProtocolProps(object connection, IPEndPoint remote, IPEndPoint local)
        {
            return RemoteNode.Props(system, connection, remote, local);
        }
    }
}
