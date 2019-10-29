using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Neo.IO.Actors;
using Neo.IO.Caching;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Neo.Network.P2P
{
    internal class TaskManager : UntypedActor
    {
        public static bool watchSwitch = false;
        public static bool countSwitch = false;
        public Akka.Event.ILoggingAdapter AkkaLog { get; } = Context.GetLogger();

        public System.Diagnostics.Stopwatch stopwatchRegister = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchNewTasks = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchTaskCompleted = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchHeaderTaskCompleted = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchRestartTasks = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchTimer = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchTerminated = new System.Diagnostics.Stopwatch();

        public static long countRegister = 0;
        public static long countNewTasks = 0;
        public static long countTaskCompleted = 0;
        public static long countHeaderTaskCompleted = 0;
        public static long countRestartTasks = 0;
        public static long countTimer = 0;
        public static long countTerminated = 0;

        public class Register { public VersionPayload Version; }
        public class NewTasks { public InvPayload Payload; }
        public class TaskCompleted { public UInt256 Hash; }
        public class HeaderTaskCompleted { }
        public class RestartTasks { public InvPayload Payload; }
        private class Timer { }

        private static readonly TimeSpan TimerInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan TaskTimeout = TimeSpan.FromMinutes(1);

        private readonly NeoSystem system;
        private const int MaxConncurrentTasks = 3;
        private readonly FIFOSet<UInt256> knownHashes;
        private readonly Dictionary<UInt256, int> globalTasks = new Dictionary<UInt256, int>();
        private readonly Dictionary<IActorRef, TaskSession> sessions = new Dictionary<IActorRef, TaskSession>();
        private readonly ICancelable timer = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(TimerInterval, TimerInterval, Context.Self, new Timer(), ActorRefs.NoSender);

        private readonly UInt256 HeaderTaskHash = UInt256.Zero;
        private bool HasHeaderTask => globalTasks.ContainsKey(HeaderTaskHash);

        public TaskManager(NeoSystem system)
        {
            this.system = system;
            this.knownHashes = new FIFOSet<UInt256>(Blockchain.Singleton.MemPool.Capacity * 2);
        }

        private void OnHeaderTaskCompleted()
        {
            if (!sessions.TryGetValue(Sender, out TaskSession session))
                return;
            session.Tasks.Remove(HeaderTaskHash);
            DecrementGlobalTask(HeaderTaskHash);
            RequestTasks(session);
        }

        private void OnNewTasks(InvPayload payload)
        {
            if (!sessions.TryGetValue(Sender, out TaskSession session))
                return;
            if (payload.Type == InventoryType.TX && Blockchain.Singleton.Height < Blockchain.Singleton.HeaderHeight)
            {
                RequestTasks(session);
                return;
            }
            HashSet<UInt256> hashes = new HashSet<UInt256>(payload.Hashes);
            hashes.ExceptWith(knownHashes);
            if (payload.Type == InventoryType.Block)
                session.AvailableTasks.UnionWith(hashes.Where(p => globalTasks.ContainsKey(p)));

            hashes.ExceptWith(globalTasks.Keys);
            if (hashes.Count == 0)
            {
                RequestTasks(session);
                return;
            }

            foreach (UInt256 hash in hashes)
            {
                IncrementGlobalTask(hash);
                session.Tasks[hash] = DateTime.UtcNow;
            }

            foreach (InvPayload group in InvPayload.CreateGroup(payload.Type, hashes.ToArray()))
                Sender.Tell(Message.Create(MessageCommand.GetData, group));
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Register register:
                    if (watchSwitch)
                    {
                        stopwatchRegister.Start();
                    }
                    OnRegister(register.Version);
                    if (watchSwitch)
                    {
                        stopwatchRegister.Stop();
                        AkkaLog.Info($"Class:TaskManager Type: Register TimeSpan:{stopwatchRegister.Elapsed.TotalSeconds}");
                        stopwatchRegister.Reset();
                    }
                    if (countSwitch) countRegister++; 
                    break;
                case NewTasks tasks:
                    if (watchSwitch)
                    {
                        stopwatchNewTasks.Start();
                    }
                    OnNewTasks(tasks.Payload);
                    if (watchSwitch)
                    {
                        stopwatchNewTasks.Stop();
                        AkkaLog.Info($"Class:TaskManager Type: NewTasks TimeSpan:{stopwatchNewTasks.Elapsed.TotalSeconds}");
                        stopwatchNewTasks.Reset();
                    }
                    if (countSwitch) countNewTasks++;
                    break;
                case TaskCompleted completed:
                    if (watchSwitch)
                    {
                        stopwatchTaskCompleted.Start();
                    }
                    OnTaskCompleted(completed.Hash);
                    if (watchSwitch)
                    {
                        stopwatchTaskCompleted.Stop();
                        AkkaLog.Info($"Class:TaskManager Type: TaskCompleted TimeSpan:{stopwatchTaskCompleted.Elapsed.TotalSeconds}");
                        stopwatchTaskCompleted.Reset();
                    }
                    if (countSwitch) countTaskCompleted++;
                    break;
                case HeaderTaskCompleted _:
                    if (watchSwitch)
                    {
                        stopwatchHeaderTaskCompleted.Start();
                    }
                    OnHeaderTaskCompleted();
                    if (watchSwitch)
                    {
                        stopwatchHeaderTaskCompleted.Stop();
                        AkkaLog.Info($"Class:TaskManager Type: HeaderTaskCompleted TimeSpan:{stopwatchHeaderTaskCompleted.Elapsed.TotalSeconds}");
                        stopwatchHeaderTaskCompleted.Reset();
                    }
                    if (countSwitch) countHeaderTaskCompleted++;
                    break;
                case RestartTasks restart:
                    if (watchSwitch)
                    {
                        stopwatchRestartTasks.Start();
                    }
                    OnRestartTasks(restart.Payload);
                    if (watchSwitch)
                    {
                        stopwatchRestartTasks.Stop();
                        AkkaLog.Info($"Class:TaskManager Type: RestartTasks TimeSpan:{stopwatchRestartTasks.Elapsed.TotalSeconds}");
                        stopwatchRestartTasks.Reset();
                    }
                    if (countSwitch) countRestartTasks++; 
                    break;
                case Timer _:
                    if (watchSwitch)
                    {
                        stopwatchTimer.Start();
                    }
                    OnTimer();
                    if (watchSwitch)
                    {
                        stopwatchTimer.Stop();
                        AkkaLog.Info($"Class:TaskManager Type: Timer TimeSpan:{stopwatchTimer.Elapsed.TotalSeconds}");
                        stopwatchTimer.Reset();
                    }
                    if (countSwitch) countTimer++; 
                    break;
                case Terminated terminated:
                    if (watchSwitch)
                    {
                        stopwatchTerminated.Start();
                    }
                    OnTerminated(terminated.ActorRef);
                    if (watchSwitch)
                    {
                        stopwatchTerminated.Stop();
                        AkkaLog.Info($"Class:TaskManager Type: Terminated TimeSpan:{stopwatchTerminated.Elapsed.TotalSeconds}");
                        stopwatchTerminated.Reset();
                    }
                    if (countSwitch) countTerminated++;
                    break;
            }
        }

        private void OnRegister(VersionPayload version)
        {
            Context.Watch(Sender);
            TaskSession session = new TaskSession(Sender, version);
            sessions.Add(Sender, session);
            RequestTasks(session);
        }

        private void OnRestartTasks(InvPayload payload)
        {
            knownHashes.ExceptWith(payload.Hashes);
            foreach (UInt256 hash in payload.Hashes)
                globalTasks.Remove(hash);
            foreach (InvPayload group in InvPayload.CreateGroup(payload.Type, payload.Hashes))
                system.LocalNode.Tell(Message.Create(MessageCommand.GetData, group));
        }

        private void OnTaskCompleted(UInt256 hash)
        {
            knownHashes.Add(hash);
            globalTasks.Remove(hash);
            foreach (TaskSession ms in sessions.Values)
                ms.AvailableTasks.Remove(hash);
            if (sessions.TryGetValue(Sender, out TaskSession session))
            {
                session.Tasks.Remove(hash);
                RequestTasks(session);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DecrementGlobalTask(UInt256 hash)
        {
            if (globalTasks.ContainsKey(hash))
            {
                if (globalTasks[hash] == 1)
                    globalTasks.Remove(hash);
                else
                    globalTasks[hash]--;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IncrementGlobalTask(UInt256 hash)
        {
            if (!globalTasks.ContainsKey(hash))
            {
                globalTasks[hash] = 1;
                return true;
            }
            if (globalTasks[hash] >= MaxConncurrentTasks)
                return false;

            globalTasks[hash]++;

            return true;
        }

        private void OnTerminated(IActorRef actor)
        {
            if (!sessions.TryGetValue(actor, out TaskSession session))
                return;
            sessions.Remove(actor);
            foreach (UInt256 hash in session.Tasks.Keys)
                DecrementGlobalTask(hash);
        }

        private void OnTimer()
        {
            foreach (TaskSession session in sessions.Values)
                foreach (var task in session.Tasks.ToArray())
                    if (DateTime.UtcNow - task.Value > TaskTimeout)
                    {
                        if (session.Tasks.Remove(task.Key))
                            DecrementGlobalTask(task.Key);
                    }
            foreach (TaskSession session in sessions.Values)
                RequestTasks(session);
        }

        protected override void PostStop()
        {
            timer.CancelIfNotNull();
            base.PostStop();
        }

        public static Props Props(NeoSystem system)
        {
            return Akka.Actor.Props.Create(() => new TaskManager(system)).WithMailbox("task-manager-mailbox");
        }

        private void RequestTasks(TaskSession session)
        {
            if (session.HasTask) return;
            if (session.AvailableTasks.Count > 0)
            {
                session.AvailableTasks.ExceptWith(knownHashes);
                session.AvailableTasks.RemoveWhere(p => Blockchain.Singleton.ContainsBlock(p));
                HashSet<UInt256> hashes = new HashSet<UInt256>(session.AvailableTasks);
                if (hashes.Count > 0)
                {
                    foreach (UInt256 hash in hashes.ToArray())
                    {
                        if (!IncrementGlobalTask(hash))
                            hashes.Remove(hash);
                    }
                    session.AvailableTasks.ExceptWith(hashes);
                    foreach (UInt256 hash in hashes)
                        session.Tasks[hash] = DateTime.UtcNow;
                    foreach (InvPayload group in InvPayload.CreateGroup(InventoryType.Block, hashes.ToArray()))
                        session.RemoteNode.Tell(Message.Create(MessageCommand.GetData, group));
                    return;
                }
            }
            if ((!HasHeaderTask || globalTasks[HeaderTaskHash] < MaxConncurrentTasks) && Blockchain.Singleton.HeaderHeight < session.StartHeight)
            {
                session.Tasks[HeaderTaskHash] = DateTime.UtcNow;
                IncrementGlobalTask(HeaderTaskHash);
                session.RemoteNode.Tell(Message.Create(MessageCommand.GetHeaders, GetBlocksPayload.Create(Blockchain.Singleton.CurrentHeaderHash)));
            }
            else if (Blockchain.Singleton.Height < session.StartHeight)
            {
                UInt256 hash = Blockchain.Singleton.CurrentBlockHash;
                for (uint i = Blockchain.Singleton.Height + 1; i <= Blockchain.Singleton.HeaderHeight; i++)
                {
                    hash = Blockchain.Singleton.GetBlockHash(i);
                    if (!globalTasks.ContainsKey(hash))
                    {
                        hash = Blockchain.Singleton.GetBlockHash(i - 1);
                        break;
                    }
                }
                session.RemoteNode.Tell(Message.Create(MessageCommand.GetBlocks, GetBlocksPayload.Create(hash)));
            }
        }
    }

    internal class TaskManagerMailbox : PriorityMailbox
    {
        public TaskManagerMailbox(Akka.Actor.Settings settings, Config config)
            : base(settings, config)
        {
        }

        internal protected override bool IsHighPriority(object message)
        {
            switch (message)
            {
                case TaskManager.Register _:
                case TaskManager.RestartTasks _:
                    return true;
                case TaskManager.NewTasks tasks:
                    if (tasks.Payload.Type == InventoryType.Block || tasks.Payload.Type == InventoryType.Consensus)
                        return true;
                    return false;
                default:
                    return false;
            }
        }
    }
}
