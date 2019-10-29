using Akka.Actor;
using Akka.Event;
using Akka.IO;
using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;

namespace Neo.Network.P2P
{
    public abstract class Connection : UntypedActor
    {
        public static bool watchSwitch = false;
        public static bool countSwitch = false;
        public ILoggingAdapter Log { get; } = Context.GetLogger();

        public static System.Diagnostics.Stopwatch stopwatchTimer = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchAck = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchReceived = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchConnectionClosed = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchTPSTimer = new System.Diagnostics.Stopwatch();

        public static long countTimer = 0;
        public static long countAck = 0;
        public static long countReceived = 0;
        public static long countConnectionClosed = 0;
        public static long countTPSTimer = 0;


        internal class Timer { public static Timer Instance = new Timer(); }
        internal class Ack : Tcp.Event { public static Ack Instance = new Ack(); }

        /// <summary>
        /// connection initial timeout (in seconds) before any package has been accepted
        /// </summary>
        private const int connectionTimeoutLimitStart = 10;
        /// <summary>
        /// connection timeout (in seconds) after every `OnReceived(ByteString data)` event
        /// </summary>
        private const int connectionTimeoutLimit = 60;

        public IPEndPoint Remote { get; }
        public IPEndPoint Local { get; }

        private ICancelable timer;
        private readonly IActorRef tcp;
        private readonly WebSocket ws;
        private bool disconnected = false;
        protected Connection(object connection, IPEndPoint remote, IPEndPoint local)
        {
            this.Remote = remote;
            this.Local = local;
            this.timer = Context.System.Scheduler.ScheduleTellOnceCancelable(TimeSpan.FromSeconds(connectionTimeoutLimitStart), Self, Timer.Instance, ActorRefs.NoSender);
            switch (connection)
            {
                case IActorRef tcp:
                    this.tcp = tcp;
                    break;
                case WebSocket ws:
                    this.ws = ws;
                    WsReceive();
                    break;
            }
        }

        private void WsReceive()
        {
            byte[] buffer = new byte[512];
            ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
            ws.ReceiveAsync(segment, CancellationToken.None).PipeTo(Self,
                success: p =>
                {
                    switch (p.MessageType)
                    {
                        case WebSocketMessageType.Binary:
                            return new Tcp.Received(ByteString.FromBytes(buffer, 0, p.Count));
                        case WebSocketMessageType.Close:
                            return Tcp.PeerClosed.Instance;
                        default:
                            ws.Abort();
                            return Tcp.Aborted.Instance;
                    }
                },
                failure: ex => new Tcp.ErrorClosed(ex.Message));
        }

        public void Disconnect(bool abort = false)
        {
            disconnected = true;
            if (tcp != null)
            {
                tcp.Tell(abort ? (Tcp.CloseCommand)Tcp.Abort.Instance : Tcp.Close.Instance);
            }
            else
            {
                ws.Abort();
            }
            Context.Stop(Self);
        }

        protected virtual void OnAck()
        {
        }

        protected abstract void OnData(ByteString data);

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Timer _:
                    if (watchSwitch)
                    {
                        stopwatchTimer.Start();
                    }
                    Disconnect(true);
                    if (watchSwitch)
                    {
                        stopwatchTimer.Stop();
                        Log.Info($"Class: Connection Type: Timer TimeSpan:{stopwatchTimer.Elapsed.TotalSeconds}");
                        stopwatchTimer.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countTimer, 1); 
                    break;
                case Ack _:
                    if (watchSwitch)
                    {
                        stopwatchAck.Start();
                    }
                    OnAck();
                    if (watchSwitch)
                    {
                        stopwatchAck.Stop();
                        Log.Info($"Class: Connection Type: Ack TimeSpan:{stopwatchAck.Elapsed.TotalSeconds}");
                        stopwatchAck.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countAck, 1);
                    break;
                case Tcp.Received received:
                    if (watchSwitch)
                    {
                        stopwatchReceived.Start();
                    }
                    OnReceived(received.Data);
                    if (watchSwitch)
                    {
                        stopwatchReceived.Stop();
                        Log.Info($"Class: Connection Type: Received TimeSpan:{stopwatchReceived.Elapsed.TotalSeconds}");
                        stopwatchReceived.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countReceived, 1); 
                    break;
                case Tcp.ConnectionClosed _:
                    if (watchSwitch)
                    {
                        stopwatchConnectionClosed.Start();
                    }
                    Context.Stop(Self);
                    if (watchSwitch)
                    {
                        stopwatchConnectionClosed.Stop();
                        Log.Info($"Class: Connection Type: ConnectionClosed TimeSpan:{stopwatchConnectionClosed.Elapsed.TotalSeconds}");
                        stopwatchConnectionClosed.Reset();
                    }
                    if (countSwitch) Interlocked.Add(ref countConnectionClosed, 1); 
                    break;
            }
        }

        private void OnReceived(ByteString data)
        {
            timer.CancelIfNotNull();
            timer = Context.System.Scheduler.ScheduleTellOnceCancelable(TimeSpan.FromSeconds(connectionTimeoutLimit), Self, Timer.Instance, ActorRefs.NoSender);
            try
            {
                OnData(data);
            }
            catch
            {
                Disconnect(true);
            }
        }

        protected override void PostStop()
        {
            if (!disconnected)
                tcp?.Tell(Tcp.Close.Instance);
            timer.CancelIfNotNull();
            ws?.Dispose();
            base.PostStop();
        }

        protected void SendData(ByteString data)
        {
            if (tcp != null)
            {
                tcp.Tell(Tcp.Write.Create(data, Ack.Instance));
            }
            else
            {
                ArraySegment<byte> segment = new ArraySegment<byte>(data.ToArray());
                ws.SendAsync(segment, WebSocketMessageType.Binary, true, CancellationToken.None).PipeTo(Self,
                    success: () => Ack.Instance,
                    failure: ex => new Tcp.ErrorClosed(ex.Message));
            }
        }
    }
}
