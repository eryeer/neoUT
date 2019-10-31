using Akka.Actor;
using Akka.Event;
using Akka.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Neo.IO;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace Neo.Network.P2P
{
    public abstract class Peer : UntypedActor
    {
        public static bool watchSwitchPeer = false;
        public static bool countSwitchPeer = true;
        public ILoggingAdapter Log { get; } = Context.GetLogger();
        public class Peers { public IEnumerable<IPEndPoint> EndPoints; }
        public class Connect { public IPEndPoint EndPoint; public bool IsTrusted = false; }
        private class Timer { }
        private class WsConnected { public WebSocket Socket; public IPEndPoint Remote; public IPEndPoint Local; }

        public const int DefaultMinDesiredConnections = 10;
        public const int DefaultMaxConnections = DefaultMinDesiredConnections * 4;

        private static readonly IActorRef tcp_manager = Context.System.Tcp();
        private IActorRef tcp_listener;
        private IWebHost ws_host;
        private ICancelable timer;
        protected ActorSelection Connections => Context.ActorSelection("connection_*");

        private static readonly HashSet<IPAddress> localAddresses = new HashSet<IPAddress>();
        private readonly Dictionary<IPAddress, int> ConnectedAddresses = new Dictionary<IPAddress, int>();
        protected readonly ConcurrentDictionary<IActorRef, IPEndPoint> ConnectedPeers = new ConcurrentDictionary<IActorRef, IPEndPoint>();
        protected ImmutableHashSet<IPEndPoint> UnconnectedPeers = ImmutableHashSet<IPEndPoint>.Empty;
        protected ImmutableHashSet<IPEndPoint> ConnectingPeers = ImmutableHashSet<IPEndPoint>.Empty;
        protected HashSet<IPAddress> TrustedIpAddresses { get; } = new HashSet<IPAddress>();

        public int ListenerTcpPort { get; private set; }
        public int ListenerWsPort { get; private set; }
        public int MaxConnectionsPerAddress { get; private set; } = 3;
        public int MinDesiredConnections { get; private set; } = DefaultMinDesiredConnections;
        public int MaxConnections { get; private set; } = DefaultMaxConnections;
        protected int UnconnectedMax { get; } = 1000;
        protected virtual int ConnectingMax
        {
            get
            {
                var allowedConnecting = MinDesiredConnections * 4;
                allowedConnecting = MaxConnections != -1 && allowedConnecting > MaxConnections
                    ? MaxConnections : allowedConnecting;
                return allowedConnecting - ConnectedPeers.Count;
            }
        }

        static Peer()
        {
            localAddresses.UnionWith(NetworkInterface.GetAllNetworkInterfaces().SelectMany(p => p.GetIPProperties().UnicastAddresses).Select(p => p.Address.Unmap()));
        }

        protected void AddPeers(IEnumerable<IPEndPoint> peers)
        {
            if (UnconnectedPeers.Count < UnconnectedMax)
            {
                peers = peers.Where(p => p.Port != ListenerTcpPort || !localAddresses.Contains(p.Address));
                ImmutableInterlocked.Update(ref UnconnectedPeers, p => p.Union(peers));
            }
        }

        protected void ConnectToPeer(IPEndPoint endPoint, bool isTrusted = false)
        {
            endPoint = endPoint.Unmap();
            if (endPoint.Port == ListenerTcpPort && localAddresses.Contains(endPoint.Address)) return;

            if (isTrusted) TrustedIpAddresses.Add(endPoint.Address);
            if (ConnectedAddresses.TryGetValue(endPoint.Address, out int count) && count >= MaxConnectionsPerAddress)
                return;
            if (ConnectedPeers.Values.Contains(endPoint)) return;
            ImmutableInterlocked.Update(ref ConnectingPeers, p =>
            {
                if ((p.Count >= ConnectingMax && !isTrusted) || p.Contains(endPoint)) return p;
                tcp_manager.Tell(new Tcp.Connect(endPoint));
                return p.Add(endPoint);
            });
        }

        private static bool IsIntranetAddress(IPAddress address)
        {
            byte[] data = address.MapToIPv4().GetAddressBytes();
            Array.Reverse(data);
            uint value = data.ToUInt32(0);
            return (value & 0xff000000) == 0x0a000000 || (value & 0xff000000) == 0x7f000000 || (value & 0xfff00000) == 0xac100000 || (value & 0xffff0000) == 0xc0a80000 || (value & 0xffff0000) == 0xa9fe0000;
        }

        protected abstract void NeedMorePeers(int count);

        public System.Diagnostics.Stopwatch stopwatchChannelsConfig = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchTimer = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchPeers = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchConnect = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchWsConnected = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchTcpConnected = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchTcpBound = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchTcpCommandFailed = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchTerminated = new System.Diagnostics.Stopwatch();

        public static long countChannelsConfig = 0;
        public static long countTimer = 0;
        public static long countPeers = 0;
        public static long countConnect = 0;
        public static long countWsConnected = 0;
        public static long countTcpConnected = 0;
        public static long countTcpBound = 0;
        public static long countTcpCommandFailed = 0;
        public static long countTerminated = 0;

        public static double totalTimeChannelsConfig = 0;
        public static double totalTimeTimer = 0;
        public static double totalTimePeers = 0;
        public static double totalTimeConnect = 0;
        public static double totalTimeWsConnected = 0;
        public static double totalTimeTcpConnected = 0;
        public static double totalTimeTcpBound = 0;
        public static double totalTimeTcpCommandFailed = 0;
        public static double totalTimeTerminated = 0;

        protected override void OnReceive(object message)
        {
            double timespan = 0;
            switch (message)
            {
                case ChannelsConfig config:
                    stopwatchChannelsConfig.Start();
                    OnStart(config);
                    stopwatchChannelsConfig.Stop();
                    timespan = stopwatchChannelsConfig.Elapsed.TotalSeconds;
                    stopwatchChannelsConfig.Reset();
                    if (watchSwitchPeer)
                    {
                        Log.Info($"Class:Peer Type: Message TimeSpan:{timespan}");
                    }
                    if (countSwitchPeer) {
                        countChannelsConfig++;
                        totalTimeChannelsConfig += timespan;
                    } 
                    break;
                case Timer _:
                    stopwatchTimer.Start();
                    OnTimer();
                    stopwatchTimer.Stop();
                    timespan = stopwatchTimer.Elapsed.TotalSeconds;
                    stopwatchTimer.Reset();
                    if (watchSwitchPeer)
                    {
                        Log.Info($"Class:Peer Type: Timer TimeSpan:{timespan}");
                    }
                    if (countSwitchPeer) {
                        countTimer++;
                        totalTimeTimer += timespan;
                    }
                    break;
                case Peers peers:
                    stopwatchPeers.Start();
                    AddPeers(peers.EndPoints);
                    stopwatchPeers.Stop();
                    timespan = stopwatchPeers.Elapsed.TotalSeconds;
                    stopwatchPeers.Reset();
                    if (watchSwitchPeer)
                    {
                        Log.Info($"Class:Peer Type: Peers TimeSpan:{timespan}");
                    }
                    if (countSwitchPeer) {
                        countPeers++;
                        totalTimePeers += timespan;
                    } 
                    break;
                case Connect connect:
                    stopwatchConnect.Start();
                    ConnectToPeer(connect.EndPoint, connect.IsTrusted);
                    stopwatchConnect.Stop();
                    timespan = stopwatchConnect.Elapsed.TotalSeconds;
                    stopwatchConnect.Reset();
                    if (watchSwitchPeer)
                    {
                        Log.Info($"Class:Peer Type: Connect TimeSpan:{timespan}");
                    }
                    if (countSwitchPeer) {
                        countConnect++;
                        totalTimeConnect += timespan;
                    }
                    break;
                case WsConnected ws:
                    stopwatchWsConnected.Start();
                    OnWsConnected(ws.Socket, ws.Remote, ws.Local);
                    stopwatchWsConnected.Stop();
                    timespan= stopwatchWsConnected.Elapsed.TotalSeconds;
                    stopwatchWsConnected.Reset();
                    if (watchSwitchPeer)
                    {
                        Log.Info($"Class:Peer Type: WsConnected TimeSpan:{timespan}");
                    }
                    if (countSwitchPeer) {
                        countWsConnected++;
                        totalTimeWsConnected += timespan;
                    }
                    break;
                case Tcp.Connected connected:
                    stopwatchTcpConnected.Start();
                    OnTcpConnected(((IPEndPoint)connected.RemoteAddress).Unmap(), ((IPEndPoint)connected.LocalAddress).Unmap());
                    stopwatchTcpConnected.Stop();
                    timespan = stopwatchTcpConnected.Elapsed.TotalSeconds;
                    stopwatchTcpConnected.Reset();
                    if (watchSwitchPeer)
                    {
                        Log.Info($"Class:Peer Type: TcpConnected TimeSpan:{timespan}");
                    }
                    if (countSwitchPeer) {
                        countTcpConnected++;
                        totalTimeTcpConnected+= timespan;
                    }
                    break;
                case Tcp.Bound _:
                    stopwatchTcpBound.Start();
                    tcp_listener = Sender;
                    stopwatchTcpBound.Stop();
                    timespan = stopwatchTcpBound.Elapsed.TotalSeconds;
                    stopwatchTcpBound.Reset();
                    if (watchSwitchPeer)
                    {
                        Log.Info($"Class:Peer Type: TcpBound TimeSpan:{timespan}");
                    }
                    if (countSwitchPeer) {
                        countTcpBound++;
                        totalTimeTcpBound += timespan;
                    } 
                    break;
                case Tcp.CommandFailed commandFailed:
                    stopwatchTcpCommandFailed.Start();
                    OnTcpCommandFailed(commandFailed.Cmd);
                    stopwatchTcpCommandFailed.Stop();
                    timespan= stopwatchTcpCommandFailed.Elapsed.TotalSeconds;
                    stopwatchTcpCommandFailed.Reset();
                    if (watchSwitchPeer)
                    {
                        Log.Info($"Class:Peer Type: TcpCommandFailed TimeSpan:{timespan}");
                    }
                    if (countSwitchPeer) {
                        countTcpCommandFailed++;
                        totalTimeTcpCommandFailed += timespan;
                    }
                    break;
                case Terminated terminated:
                    stopwatchTerminated.Start();
                    OnTerminated(terminated.ActorRef);
                    stopwatchTerminated.Stop();
                    timespan= stopwatchTerminated.Elapsed.TotalSeconds;
                    stopwatchTerminated.Reset();
                    if (watchSwitchPeer)
                    {
                        Log.Info($"Class:Peer Type: Terminated TimeSpan:{timespan}");
                    }
                    if (countSwitchPeer) {
                        countTerminated++;
                        totalTimeTerminated += timespan;
                    }
                    break;
            }
        }

        private void OnStart(ChannelsConfig config)
        {
            ListenerTcpPort = config.Tcp?.Port ?? 0;
            ListenerWsPort = config.WebSocket?.Port ?? 0;

            MinDesiredConnections = config.MinDesiredConnections;
            MaxConnections = config.MaxConnections;
            MaxConnectionsPerAddress = config.MaxConnectionsPerAddress;

            timer = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(0, 5000, Context.Self, new Timer(), ActorRefs.NoSender);
            if ((ListenerTcpPort > 0 || ListenerWsPort > 0)
                && localAddresses.All(p => !p.IsIPv4MappedToIPv6 || IsIntranetAddress(p))
                && UPnP.Discover())
            {
                try
                {
                    localAddresses.Add(UPnP.GetExternalIP());

                    if (ListenerTcpPort > 0) UPnP.ForwardPort(ListenerTcpPort, ProtocolType.Tcp, "NEO Tcp");
                    if (ListenerWsPort > 0) UPnP.ForwardPort(ListenerWsPort, ProtocolType.Tcp, "NEO WebSocket");
                }
                catch { }
            }
            if (ListenerTcpPort > 0)
            {
                tcp_manager.Tell(new Tcp.Bind(Self, config.Tcp, options: new[] { new Inet.SO.ReuseAddress(true) }));
            }
            if (ListenerWsPort > 0)
            {
                var host = "*";

                if (!config.WebSocket.Address.GetAddressBytes().SequenceEqual(IPAddress.Any.GetAddressBytes()))
                {
                    // Is not for all interfaces
                    host = config.WebSocket.Address.ToString();
                }

                ws_host = new WebHostBuilder().UseKestrel().UseUrls($"http://{host}:{ListenerWsPort}").Configure(app => app.UseWebSockets().Run(ProcessWebSocketAsync)).Build();
                ws_host.Start();
            }
        }

        private void OnTcpConnected(IPEndPoint remote, IPEndPoint local)
        {
            ImmutableInterlocked.Update(ref ConnectingPeers, p => p.Remove(remote));
            if (MaxConnections != -1 && ConnectedPeers.Count >= MaxConnections && !TrustedIpAddresses.Contains(remote.Address))
            {
                Sender.Tell(Tcp.Abort.Instance);
                return;
            }

            ConnectedAddresses.TryGetValue(remote.Address, out int count);
            if (count >= MaxConnectionsPerAddress)
            {
                Sender.Tell(Tcp.Abort.Instance);
            }
            else
            {
                ConnectedAddresses[remote.Address] = count + 1;
                IActorRef connection = Context.ActorOf(ProtocolProps(Sender, remote, local), $"connection_{Guid.NewGuid()}");
                Context.Watch(connection);
                Sender.Tell(new Tcp.Register(connection));
                ConnectedPeers.TryAdd(connection, remote);
                Console.WriteLine("TcpConnected: " + remote.ToString() + "Sender" + Sender.ToString());
            }
        }

        private void OnTcpCommandFailed(Tcp.Command cmd)
        {
            switch (cmd)
            {
                case Tcp.Connect connect:
                    ImmutableInterlocked.Update(ref ConnectingPeers, p => p.Remove(((IPEndPoint)connect.RemoteAddress).Unmap()));
                    break;
            }
        }

        private void OnTerminated(IActorRef actorRef)
        {
            if (ConnectedPeers.TryRemove(actorRef, out IPEndPoint endPoint))
            {
                ConnectedAddresses.TryGetValue(endPoint.Address, out int count);
                if (count > 0) count--;
                if (count == 0)
                    ConnectedAddresses.Remove(endPoint.Address);
                else
                    ConnectedAddresses[endPoint.Address] = count;
            }
        }

        private void OnTimer()
        {
            if (ConnectedPeers.Count >= MinDesiredConnections) return;
            if (UnconnectedPeers.Count == 0)
                NeedMorePeers(MinDesiredConnections - ConnectedPeers.Count);
            IPEndPoint[] endpoints = UnconnectedPeers.Take(MinDesiredConnections - ConnectedPeers.Count).ToArray();
            ImmutableInterlocked.Update(ref UnconnectedPeers, p => p.Except(endpoints));
            foreach (IPEndPoint endpoint in endpoints)
            {
                ConnectToPeer(endpoint);
            }
        }

        private void OnWsConnected(WebSocket ws, IPEndPoint remote, IPEndPoint local)
        {
            ConnectedAddresses.TryGetValue(remote.Address, out int count);
            if (count >= MaxConnectionsPerAddress)
            {
                ws.Abort();
            }
            else
            {
                ConnectedAddresses[remote.Address] = count + 1;
                Context.ActorOf(ProtocolProps(ws, remote, local), $"connection_{Guid.NewGuid()}");
            }
        }

        protected override void PostStop()
        {
            timer.CancelIfNotNull();
            ws_host?.Dispose();
            tcp_listener?.Tell(Tcp.Unbind.Instance);
            base.PostStop();
        }

        private async Task ProcessWebSocketAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest) return;
            WebSocket ws = await context.WebSockets.AcceptWebSocketAsync();
            Self.Tell(new WsConnected
            {
                Socket = ws,
                Remote = new IPEndPoint(context.Connection.RemoteIpAddress, context.Connection.RemotePort),
                Local = new IPEndPoint(context.Connection.LocalIpAddress, context.Connection.LocalPort)
            });
        }

        protected abstract Props ProtocolProps(object connection, IPEndPoint remote, IPEndPoint local);
    }
}
