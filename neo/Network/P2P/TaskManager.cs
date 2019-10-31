using Akka.Actor;
using Akka.Configuration;
using Neo.IO.Actors;
using Neo.IO.Caching;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Neo.Network.P2P
{
    internal class TaskManager : UntypedActor
    {
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
        //全局task集合，包含了各个taskSession中的task
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

        //收到Header了之后，删除task的session和global任务
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
            //如果没有会话则忽略
            if (!sessions.TryGetValue(Sender, out TaskSession session))
                return;
            //如果是交易，但是区块高度不是最新，则获取区块，不处理交易
            if (payload.Type == InventoryType.TX && Blockchain.Singleton.Height < Blockchain.Singleton.HeaderHeight)
            {
                RequestTasks(session);
                return;
            }
            //已执行完成的task要剔除
            HashSet<UInt256> hashes = new HashSet<UInt256>(payload.Hashes);
            hashes.ExceptWith(knownHashes);
            //要获取区块的话，则添加到session.AvailableTasks中去
            if (payload.Type == InventoryType.Block)
                session.AvailableTasks.UnionWith(hashes.Where(p => globalTasks.ContainsKey(p)));

            //排除全局在执行中的task
            hashes.ExceptWith(globalTasks.Keys);
            if (hashes.Count == 0)
            {
                RequestTasks(session);
                return;
            }
            //添加到全局执行任务中
            foreach (UInt256 hash in hashes)
            {
                IncrementGlobalTask(hash);
                session.Tasks[hash] = DateTime.UtcNow;
            }
            //获取数据
            foreach (InvPayload group in InvPayload.CreateGroup(payload.Type, hashes.ToArray()))
                Sender.Tell(Message.Create(MessageCommand.GetData, group));
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                //done
                case Register register:
                    OnRegister(register.Version);
                    break;
                //done
                case NewTasks tasks:
                    OnNewTasks(tasks.Payload);
                    break;
                //done
                case TaskCompleted completed:
                    OnTaskCompleted(completed.Hash);
                    break;
                //done
                case HeaderTaskCompleted _:
                    OnHeaderTaskCompleted();
                    break;
                //done
                case RestartTasks restart:
                    OnRestartTasks(restart.Payload);
                    break;
                //done
                case Timer _:
                    OnTimer();
                    break;
                //done
                case Terminated terminated:
                    OnTerminated(terminated.ActorRef);
                    break;
            }
        }

        private void OnRegister(VersionPayload version)
        {
            //监视remoteNode
            Context.Watch(Sender);
            //给remoteNode建一个对应的任务会话
            TaskSession session = new TaskSession(Sender, version);
            //添加到sessions字典（remoteNode，session）中
            sessions.Add(Sender, session);
            RequestTasks(session);
        }

        //用于普通交易inv形式发送没有获取到或阻塞住时，共识流程中需要tx交易，直接重启task，删除globalTask执行任务列表，直接发送过去交易数据。
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
            //添加已经执行的hash到knownHashes
            knownHashes.Add(hash);
            //全局task中删除hash
            globalTasks.Remove(hash);
            //遍历所有的session的可执行session，删除该task
            foreach (TaskSession ms in sessions.Values)
                ms.AvailableTasks.Remove(hash);
            //删除发送消息对应会话中的正在执行的该hash记录，并重新处理该会话中的其他task
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

        //添加到全局执行中的任务集合
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IncrementGlobalTask(UInt256 hash)
        {
            if (!globalTasks.ContainsKey(hash))
            {
                globalTasks[hash] = 1;
                return true;
            }
            //并发执行相同task的数量不能超过3
            if (globalTasks[hash] >= MaxConncurrentTasks)
                return false;

            globalTasks[hash]++;

            return true;
        }

        private void OnTerminated(IActorRef actor)
        {
            //连接断开时删除sessions中的session，清楚进行中的globalTask
            if (!sessions.TryGetValue(actor, out TaskSession session))
                return;
            sessions.Remove(actor);
            foreach (UInt256 hash in session.Tasks.Keys)
                DecrementGlobalTask(hash);
        }

        private void OnTimer()
        {
            //遍历在执行中的task集合，有超时1min以上的就把它停掉
            foreach (TaskSession session in sessions.Values)
                foreach (var task in session.Tasks.ToArray())
                    if (DateTime.UtcNow - task.Value > TaskTimeout)
                    {
                        if (session.Tasks.Remove(task.Key))
                            DecrementGlobalTask(task.Key);
                    }
            //重新执行区块同步
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

        //执行获取区块和区块头的任务
        private void RequestTasks(TaskSession session)
        {
            //如果seesion中已经有task了，就忽略
            if (session.HasTask) return;
            //AvailableTask中包含的都是获取区块的任务
            if (session.AvailableTasks.Count > 0)
            {
                //排除了已经执行过的任务
                session.AvailableTasks.ExceptWith(knownHashes);
                //排除了已经校验过的或持久化的blockHash   
                session.AvailableTasks.RemoveWhere(p => Blockchain.Singleton.ContainsBlock(p));
                HashSet<UInt256> hashes = new HashSet<UInt256>(session.AvailableTasks);
                if (hashes.Count > 0)
                {
                    foreach (UInt256 hash in hashes.ToArray())
                    {
                        //添加task的hash到全局task集合中去，如果添加不进去，就把它从临时hashes中删掉
                        if (!IncrementGlobalTask(hash))
                            hashes.Remove(hash);
                    }
                    //AvailableTasks中删除添加到全局task的hash，即要执行这些task
                    session.AvailableTasks.ExceptWith(hashes);
                    //将执行的task标记时间戳记录到session.Tasks字典中
                    foreach (UInt256 hash in hashes)
                        session.Tasks[hash] = DateTime.UtcNow;
                    //执行获取区块的任务
                    foreach (InvPayload group in InvPayload.CreateGroup(InventoryType.Block, hashes.ToArray()))
                        session.RemoteNode.Tell(Message.Create(MessageCommand.GetData, group));
                    return;
                }
            }
            //当本地区块header高度小于起始会话的区块高度，并且globalTask可以执行获取header的任务时，执行获取header的任务，TODO（session什么时候断开）
            if ((!HasHeaderTask || globalTasks[HeaderTaskHash] < MaxConncurrentTasks) && Blockchain.Singleton.HeaderHeight < session.StartHeight)
            {
                session.Tasks[HeaderTaskHash] = DateTime.UtcNow;
                IncrementGlobalTask(HeaderTaskHash);
                session.RemoteNode.Tell(Message.Create(MessageCommand.GetHeaders, GetBlocksPayload.Create(Blockchain.Singleton.CurrentHeaderHash)));
            }
            //如果Header高度已经保持最新了，或者已经有task在执行获取header了，就检查区块高度
            else if (Blockchain.Singleton.Height < session.StartHeight)
            {
                UInt256 hash = Blockchain.Singleton.CurrentBlockHash;
                for (uint i = Blockchain.Singleton.Height + 1; i <= Blockchain.Singleton.HeaderHeight; i++)
                {
                    hash = Blockchain.Singleton.GetBlockHash(i);
                    //如果globalTask中不包含所需block的任务执行，则设置block的起始高度，并进行区块获取。
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
