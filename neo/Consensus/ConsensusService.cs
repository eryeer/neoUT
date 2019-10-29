using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Neo.Cryptography;
using Neo.IO;
using Neo.IO.Actors;
using Neo.Ledger;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.Plugins;
using Neo.SmartContract.Native;
using Neo.Wallets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Neo.Consensus
{
    public sealed class ConsensusService : UntypedActor
    {

        public static bool watchSwitch = true;
        public static bool countSwitch = false;
        public Akka.Event.ILoggingAdapter AkkaLog { get; } = Context.GetLogger();
        private DateTime lasttime = DateTime.Now;

        public System.Diagnostics.Stopwatch stopwatchSetViewNumber = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchTimer = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchConsensusPayloadCommon = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchChangeView = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchPrepareRequest = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchPrepareResponse = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchCommit = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchRecoveryRequest = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchRecoveryMessage = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchTransaction = new System.Diagnostics.Stopwatch();
        public System.Diagnostics.Stopwatch stopwatchPersistCompleted = new System.Diagnostics.Stopwatch();

        public static long countSetViewNumber = 0;
        public static long countTimer = 0;
        public static long countConsensusPayloadCommon = 0;
        public static long countChangeView = 0;
        public static long countPrepareRequest = 0;
        public static long countPrepareResponse = 0;
        public static long countCommit = 0;
        public static long countRecoveryRequest = 0;
        public static long countRecoveryMessage = 0;
        public static long countTransaction = 0;
        public static long countPersistCompleted = 0;



        public class Start { public bool IgnoreRecoveryLogs; }
        public class SetViewNumber { public byte ViewNumber; }
        internal class Timer { public uint Height; public byte ViewNumber; }

        private readonly ConsensusContext context;
        private readonly IActorRef localNode;
        private readonly IActorRef taskManager;
        private ICancelable timer_token;
        private DateTime block_received_time;
        private bool started = false;

        /// <summary>
        /// This will record the information from last scheduled timer
        /// </summary>
        private DateTime clock_started = TimeProvider.Current.UtcNow;
        private TimeSpan expected_delay = TimeSpan.Zero;

        /// <summary>
        /// This will be cleared every block (so it will not grow out of control, but is used to prevent repeatedly
        /// responding to the same message.
        /// </summary>
        private readonly HashSet<UInt256> knownHashes = new HashSet<UInt256>();
        /// <summary>
        /// This variable is only true during OnRecoveryMessageReceived
        /// </summary>
        private bool isRecovering = false;

        public ConsensusService(IActorRef localNode, IActorRef taskManager, Store store, Wallet wallet)
            : this(localNode, taskManager, new ConsensusContext(wallet, store))
        {
        }

        internal ConsensusService(IActorRef localNode, IActorRef taskManager, ConsensusContext context)
        {
            this.localNode = localNode;
            this.taskManager = taskManager;
            this.context = context;
            Context.System.EventStream.Subscribe(Self, typeof(Blockchain.PersistCompleted));
        }

        private bool AddTransaction(Transaction tx, bool verify)
        {
            if (verify && !tx.Verify(context.Snapshot, context.Transactions.Values))
            {
                Log($"Invalid transaction: {tx.Hash}{Environment.NewLine}{tx.ToArray().ToHexString()}", Plugins.LogLevel.Warning);
                RequestChangeView(ChangeViewReason.TxInvalid);
                return false;
            }
            if (!NativeContract.Policy.CheckPolicy(tx, context.Snapshot))
            {
                Log($"reject tx: {tx.Hash}{Environment.NewLine}{tx.ToArray().ToHexString()}", Plugins.LogLevel.Warning);
                RequestChangeView(ChangeViewReason.TxRejectedByPolicy);
                return false;
            }
            context.Transactions[tx.Hash] = tx;
            return CheckPrepareResponse();
        }

        private bool CheckPrepareResponse()
        {
            if (context.TransactionHashes.Length == context.Transactions.Count)
            {
                // if we are the primary for this view, but acting as a backup because we recovered our own
                // previously sent prepare request, then we don't want to send a prepare response.
                if (context.IsPrimary || context.WatchOnly) return true;

                // Check maximum block size via Native Contract policy
                if (context.GetExpectedBlockSize() > NativeContract.Policy.GetMaxBlockSize(context.Snapshot))
                {
                    Log($"rejected block: {context.Block.Index}{Environment.NewLine} The size exceed the policy", Plugins.LogLevel.Warning);
                    RequestChangeView(ChangeViewReason.BlockRejectedByPolicy);
                    return false;
                }

                // Timeout extension due to prepare response sent
                // around 2*15/M=30.0/5 ~ 40% block time (for M=5)
                ExtendTimerByFactor(2);

                Log($"send prepare response");
                localNode.Tell(new LocalNode.SendDirectly { Inventory = context.MakePrepareResponse() });
                CheckPreparations();
            }
            return true;
        }

        private void ChangeTimer(TimeSpan delay)
        {
            clock_started = TimeProvider.Current.UtcNow;
            expected_delay = delay;
            timer_token.CancelIfNotNull();
            timer_token = Context.System.Scheduler.ScheduleTellOnceCancelable(delay, Self, new Timer
            {
                Height = context.Block.Index,
                ViewNumber = context.ViewNumber
            }, ActorRefs.NoSender);
        }

        private void CheckCommits()
        {
            if (context.CommitPayloads.Count(p => p?.ConsensusMessage.ViewNumber == context.ViewNumber) >= context.M && context.TransactionHashes.All(p => context.Transactions.ContainsKey(p)))
            {
                Block block = context.CreateBlock();
                Log($"relay block: height={block.Index} hash={block.Hash} tx={block.Transactions.Length}");
                localNode.Tell(new LocalNode.Relay { Inventory = block });
                CheckCount(block);

            }
        }

        private void CheckCount(Block block)
        {
            //print block timespan and TPS
            double timespan = (DateTime.Now - lasttime).TotalSeconds;
            lasttime = DateTime.Now;
            Console.WriteLine("Time spent since last relay = " + timespan + ", TPS = " + block.Transactions.Length / timespan);

            //Connection
            if (Connection.countSwitch)
            {
                AkkaLog.Info($"Class: Connection Type: Timer Count: {Connection.countTimer}");
                AkkaLog.Info($"Class: Connection Type: Ack Count: {Connection.countAck}");
                AkkaLog.Info($"Class: Connection Type: Received Count: {Connection.countReceived}");
                AkkaLog.Info($"Class: Connection Type: ConnectionClosed Count: {Connection.countConnectionClosed}");
                AkkaLog.Info($"Class: Connection Type: TPSTimer Count: {Connection.countTPSTimer}");
                Connection.countTimer = 0;
                Connection.countAck = 0;
                Connection.countReceived = 0;
                Connection.countConnectionClosed = 0;
                Connection.countTPSTimer = 0;
            }

            //RemoteNode
            if (RemoteNode.countSwitchRemoteNode)
            {
                AkkaLog.Info($"Class: RemoteNode Type: Message Count: {RemoteNode.countMessage}");
                AkkaLog.Info($"Class: RemoteNode Type: IInventory Count: {RemoteNode.countIInventory}");
                AkkaLog.Info($"Class: RemoteNode Type: Relay Count: {RemoteNode.countRelay}");
                AkkaLog.Info($"Class: RemoteNode Type: VersionPayload Count: {RemoteNode.countVersionPayload}");
                AkkaLog.Info($"Class: RemoteNode Type: Verack Count: {RemoteNode.countVerack}");
                AkkaLog.Info($"Class: RemoteNode Type: SetFilter Count: {RemoteNode.countSetFilter}");
                AkkaLog.Info($"Class: RemoteNode Type: PingPayload Count: {RemoteNode.countPingPayload}");
                RemoteNode.countMessage = 0;
                RemoteNode.countIInventory = 0;
                RemoteNode.countRelay = 0;
                RemoteNode.countVersionPayload = 0;
                RemoteNode.countVerack = 0;
                RemoteNode.countSetFilter = 0;
                RemoteNode.countPingPayload = 0;
            }

            //ConsensusService
            if (ConsensusService.countSwitch)
            {
                AkkaLog.Info($"Class: ConsensusService Type: SetViewNumber Count: {ConsensusService.countSetViewNumber}");
                AkkaLog.Info($"Class: ConsensusService Type: Timer Count: {ConsensusService.countTimer}");
                AkkaLog.Info($"Class: ConsensusService Type: ConsensusPayloadCommon Count: {ConsensusService.countConsensusPayloadCommon}");
                AkkaLog.Info($"Class: ConsensusService Type: ChangeView Count: {ConsensusService.countChangeView}");
                AkkaLog.Info($"Class: ConsensusService Type: PrepareRequest Count: {ConsensusService.countPrepareRequest}");
                AkkaLog.Info($"Class: ConsensusService Type: PrepareResponse Count: {ConsensusService.countPrepareResponse}");
                AkkaLog.Info($"Class: ConsensusService Type: Commit Count: {ConsensusService.countCommit}");
                AkkaLog.Info($"Class: ConsensusService Type: RecoveryRequest Count: {ConsensusService.countRecoveryRequest}");
                AkkaLog.Info($"Class: ConsensusService Type: RecoveryMessage Count: {ConsensusService.countRecoveryMessage}");
                AkkaLog.Info($"Class: ConsensusService Type: Transaction Count: {ConsensusService.countTransaction}");
                AkkaLog.Info($"Class: ConsensusService Type: PersistCompleted Count: {ConsensusService.countPersistCompleted}");
                ConsensusService.countSetViewNumber = 0;
                ConsensusService.countTimer = 0;
                ConsensusService.countConsensusPayloadCommon = 0;
                ConsensusService.countChangeView = 0;
                ConsensusService.countPrepareRequest = 0;
                ConsensusService.countPrepareResponse = 0;
                ConsensusService.countCommit = 0;
                ConsensusService.countRecoveryRequest = 0;
                ConsensusService.countRecoveryMessage = 0;
                ConsensusService.countTransaction = 0;
                ConsensusService.countPersistCompleted = 0;
            }

            //ProtocolHandler
            if (ProtocolHandler.countSwitch)
            {
                AkkaLog.Info($"Class: ProtocolHandler Type: Addr Count: {ProtocolHandler.countAddr}");
                AkkaLog.Info($"Class: ProtocolHandler Type: Block Count: {ProtocolHandler.countBlock}");
                AkkaLog.Info($"Class: ProtocolHandler Type: Consensus Count: {ProtocolHandler.countConsensus}");
                AkkaLog.Info($"Class: ProtocolHandler Type: FilterAdd Count: {ProtocolHandler.countFilterAdd}");
                AkkaLog.Info($"Class: ProtocolHandler Type: FilterClear Count: {ProtocolHandler.countFilterClear}");
                AkkaLog.Info($"Class: ProtocolHandler Type: FilterLoad Count: {ProtocolHandler.countFilterLoad}");
                AkkaLog.Info($"Class: ProtocolHandler Type: GetAddr Count: {ProtocolHandler.countGetAddr}");
                AkkaLog.Info($"Class: ProtocolHandler Type: GetBlocks Count: {ProtocolHandler.countGetBlocks}");
                AkkaLog.Info($"Class: ProtocolHandler Type: GetData Count: {ProtocolHandler.countGetData}");
                AkkaLog.Info($"Class: ProtocolHandler Type: GetHeaders Count: {ProtocolHandler.countGetHeaders}");
                AkkaLog.Info($"Class: ProtocolHandler Type: Headers Count: {ProtocolHandler.countHeaders}");
                AkkaLog.Info($"Class: ProtocolHandler Type: Inv Count: {ProtocolHandler.countInv}");
                AkkaLog.Info($"Class: ProtocolHandler Type: Mempool Count: {ProtocolHandler.countMempool}");
                AkkaLog.Info($"Class: ProtocolHandler Type: Ping Count: {ProtocolHandler.countPing}");
                AkkaLog.Info($"Class: ProtocolHandler Type: Pong Count: {ProtocolHandler.countPong}");
                AkkaLog.Info($"Class: ProtocolHandler Type: Transaction Count: {ProtocolHandler.countTransaction}");
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
            }

            //TaskManager
            if (TaskManager.countSwitch)
            {
                AkkaLog.Info($"Class: TaskManager Type: Register Count: {TaskManager.countRegister}");
                AkkaLog.Info($"Class: TaskManager Type: NewTasks Count: {TaskManager.countNewTasks}");
                AkkaLog.Info($"Class: TaskManager Type: TaskCompleted Count: {TaskManager.countTaskCompleted}");
                AkkaLog.Info($"Class: TaskManager Type: HeaderTaskCompleted Count: {TaskManager.countHeaderTaskCompleted}");
                AkkaLog.Info($"Class: TaskManager Type: RestartTasks Count: {TaskManager.countRestartTasks}");
                AkkaLog.Info($"Class: TaskManager Type: Timer Count: {TaskManager.countTimer}");
                AkkaLog.Info($"Class: TaskManager Type: Terminated Count: {TaskManager.countTerminated}");
                TaskManager.countRegister = 0;
                TaskManager.countNewTasks = 0;
                TaskManager.countTaskCompleted = 0;
                TaskManager.countHeaderTaskCompleted = 0;
                TaskManager.countRestartTasks = 0;
                TaskManager.countTimer = 0;
                TaskManager.countTerminated = 0;
            }
            if (Blockchain.countSwitchBlockchain) {
                AkkaLog.Info($"Class: Blockchain Type: Import Count: {Blockchain.countImport}");
                AkkaLog.Info($"Class: Blockchain Type: FillMemoryPool Count: {Blockchain.countFillMemoryPool}");
                AkkaLog.Info($"Class: Blockchain Type: HeaderArray Count: {Blockchain.countHeaderArray}");
                AkkaLog.Info($"Class: Blockchain Type: Block Count: {Blockchain.countBlock}");
                AkkaLog.Info($"Class: Blockchain Type: TransactionArray Count: {Blockchain.countTransactionArray}");
                AkkaLog.Info($"Class: Blockchain Type: Transaction Count: {Blockchain.countTransaction}");
                AkkaLog.Info($"Class: Blockchain Type: ConsensusPayload Count: {Blockchain.countConsensusPayload}");
                AkkaLog.Info($"Class: Blockchain Type: Idle Count: {Blockchain.countIdle}");
                Blockchain.countImport = 0;
                Blockchain.countFillMemoryPool = 0;
                Blockchain.countHeaderArray = 0;
                Blockchain.countBlock = 0;
                Blockchain.countTransactionArray = 0;
                Blockchain.countTransaction = 0;
                Blockchain.countConsensusPayload = 0;
                Blockchain.countIdle = 0;
            }
            if (Peer.countSwitchPeer)
            {
                AkkaLog.Info($"Class: Peer Type: ChannelsConfig Count: {Peer.countChannelsConfig}");
                AkkaLog.Info($"Class: Peer Type: Timer Count: {Peer.countTimer}");
                AkkaLog.Info($"Class: Peer Type: Peers Count: {Peer.countPeers}");
                AkkaLog.Info($"Class: Peer Type: Connect Count: {Peer.countConnect}");
                AkkaLog.Info($"Class: Peer Type: WsConnected Count: {Peer.countWsConnected}");
                AkkaLog.Info($"Class: Peer Type: TcpConnected Count: {Peer.countTcpConnected}");
                AkkaLog.Info($"Class: Peer Type: TcpBound Count: {Peer.countTcpBound}");
                AkkaLog.Info($"Class: Peer Type: TcpCommandFailed Count: {Peer.countTcpCommandFailed}");
                AkkaLog.Info($"Class: Peer Type: Terminated Count: {Peer.countTerminated}");
                Peer.countChannelsConfig = 0;
                Peer.countTimer = 0;
                Peer.countPeers = 0;
                Peer.countConnect = 0;
                Peer.countWsConnected = 0;
                Peer.countTcpConnected = 0;
                Peer.countTcpBound = 0;
                Peer.countTcpCommandFailed = 0;
                Peer.countTerminated = 0;
            }
            if (LocalNode.countSwitchLocalNode)
            {
                AkkaLog.Info($"Class: LocalNode Type: Message Count: {LocalNode.countMessage}");
                AkkaLog.Info($"Class: LocalNode Type: Relay Count: {LocalNode.countRelay}");
                AkkaLog.Info($"Class: LocalNode Type: RelayDirectly Count: {LocalNode.countRelayDirectly}");
                AkkaLog.Info($"Class: LocalNode Type: SendDirectly Count: {LocalNode.countSendDirectly}");
                LocalNode.countMessage = 0;
                LocalNode.countRelay = 0;
                LocalNode.countRelayDirectly = 0;
                LocalNode.countSendDirectly = 0;
            }


        }

        private void CheckExpectedView(byte viewNumber)
        {
            if (context.ViewNumber >= viewNumber) return;
            // if there are `M` change view payloads with NewViewNumber greater than viewNumber, then, it is safe to move
            if (context.ChangeViewPayloads.Count(p => p != null && p.GetDeserializedMessage<ChangeView>().NewViewNumber >= viewNumber) >= context.M)
            {
                if (!context.WatchOnly)
                {
                    ChangeView message = context.ChangeViewPayloads[context.MyIndex]?.GetDeserializedMessage<ChangeView>();
                    // Communicate the network about my agreement to move to `viewNumber`
                    // if my last change view payload, `message`, has NewViewNumber lower than current view to change
                    if (message is null || message.NewViewNumber < viewNumber)
                        localNode.Tell(new LocalNode.SendDirectly { Inventory = context.MakeChangeView(ChangeViewReason.ChangeAgreement) });
                }
                InitializeConsensus(viewNumber);
            }
        }

        private void CheckPreparations()
        {
            if (context.PreparationPayloads.Count(p => p != null) >= context.M && context.TransactionHashes.All(p => context.Transactions.ContainsKey(p)))
            {
                ConsensusPayload payload = context.MakeCommit();
                Log($"send commit");
                context.Save();
                localNode.Tell(new LocalNode.SendDirectly { Inventory = payload });
                // Set timer, so we will resend the commit in case of a networking issue
                ChangeTimer(TimeSpan.FromMilliseconds(Blockchain.MillisecondsPerBlock));
                CheckCommits();
            }
        }

        private void InitializeConsensus(byte viewNumber)
        {
            context.Reset(viewNumber);
            if (viewNumber > 0)
                Log($"changeview: view={viewNumber} primary={context.Validators[context.GetPrimaryIndex((byte)(viewNumber - 1u))]}", Plugins.LogLevel.Warning);
            Log($"initialize: height={context.Block.Index} view={viewNumber} index={context.MyIndex} role={(context.IsPrimary ? "Primary" : context.WatchOnly ? "WatchOnly" : "Backup")}");
            if (context.WatchOnly) return;
            if (context.IsPrimary)
            {
                if (isRecovering)
                {
                    ChangeTimer(TimeSpan.FromMilliseconds(Blockchain.MillisecondsPerBlock << (viewNumber + 1)));
                }
                else
                {
                    TimeSpan span = TimeProvider.Current.UtcNow - block_received_time;
                    if (span >= Blockchain.TimePerBlock)
                        ChangeTimer(TimeSpan.Zero);
                    else
                        ChangeTimer(Blockchain.TimePerBlock - span);
                }
            }
            else
            {
                ChangeTimer(TimeSpan.FromMilliseconds(Blockchain.MillisecondsPerBlock << (viewNumber + 1)));
            }
        }

        private void Log(string message, Plugins.LogLevel level = Plugins.LogLevel.Info)
        {
            Plugin.Log(nameof(ConsensusService), level, message);
        }

        private void OnChangeViewReceived(ConsensusPayload payload, ChangeView message)
        {
            if (message.NewViewNumber <= context.ViewNumber)
                OnRecoveryRequestReceived(payload);

            if (context.CommitSent) return;

            var expectedView = context.ChangeViewPayloads[payload.ValidatorIndex]?.GetDeserializedMessage<ChangeView>().NewViewNumber ?? (byte)0;
            if (message.NewViewNumber <= expectedView)
                return;

            Log($"{nameof(OnChangeViewReceived)}: height={payload.BlockIndex} view={message.ViewNumber} index={payload.ValidatorIndex} nv={message.NewViewNumber} reason={message.Reason}");
            context.ChangeViewPayloads[payload.ValidatorIndex] = payload;
            CheckExpectedView(message.NewViewNumber);
        }

        private void OnCommitReceived(ConsensusPayload payload, Commit commit)
        {
            ref ConsensusPayload existingCommitPayload = ref context.CommitPayloads[payload.ValidatorIndex];
            if (existingCommitPayload != null)
            {
                if (existingCommitPayload.Hash != payload.Hash)
                    Log($"{nameof(OnCommitReceived)}: different commit from validator! height={payload.BlockIndex} index={payload.ValidatorIndex} view={commit.ViewNumber} existingView={existingCommitPayload.ConsensusMessage.ViewNumber}", Plugins.LogLevel.Warning);
                return;
            }

            // Timeout extension: commit has been received with success
            // around 4*15s/M=60.0s/5=12.0s ~ 80% block time (for M=5)
            ExtendTimerByFactor(4);

            if (commit.ViewNumber == context.ViewNumber)
            {
                Log($"{nameof(OnCommitReceived)}: height={payload.BlockIndex} view={commit.ViewNumber} index={payload.ValidatorIndex} nc={context.CountCommitted} nf={context.CountFailed}");

                byte[] hashData = context.EnsureHeader()?.GetHashData();
                if (hashData == null)
                {
                    existingCommitPayload = payload;
                }
                else if (Crypto.Default.VerifySignature(hashData, commit.Signature,
                    context.Validators[payload.ValidatorIndex].EncodePoint(false)))
                {
                    existingCommitPayload = payload;
                    CheckCommits();
                }
                return;
            }
            // Receiving commit from another view
            Log($"{nameof(OnCommitReceived)}: record commit for different view={commit.ViewNumber} index={payload.ValidatorIndex} height={payload.BlockIndex}");
            existingCommitPayload = payload;
        }

        // this function increases existing timer (never decreases) with a value proportional to `maxDelayInBlockTimes`*`Blockchain.MillisecondsPerBlock`
        private void ExtendTimerByFactor(int maxDelayInBlockTimes)
        {
            TimeSpan nextDelay = expected_delay - (TimeProvider.Current.UtcNow - clock_started) + TimeSpan.FromMilliseconds(maxDelayInBlockTimes * Blockchain.MillisecondsPerBlock / context.M);
            if (!context.WatchOnly && !context.ViewChanging && !context.CommitSent && (nextDelay > TimeSpan.Zero))
                ChangeTimer(nextDelay);
        }

        private void OnConsensusPayload(ConsensusPayload payload)
        {
            if (watchSwitch)
            {
                stopwatchConsensusPayloadCommon.Start();
            }
            if (context.BlockSent) return;
            if (payload.Version != context.Block.Version) return;
            if (payload.PrevHash != context.Block.PrevHash || payload.BlockIndex != context.Block.Index)
            {
                if (context.Block.Index < payload.BlockIndex)
                {
                    Log($"chain sync: expected={payload.BlockIndex} current={context.Block.Index - 1} nodes={LocalNode.Singleton.ConnectedCount}", Plugins.LogLevel.Warning);
                }
                return;
            }
            if (payload.ValidatorIndex >= context.Validators.Length) return;
            ConsensusMessage message;
            try
            {
                message = payload.ConsensusMessage;
            }
            catch (FormatException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }
            context.LastSeenMessage[payload.ValidatorIndex] = (int)payload.BlockIndex;
            foreach (IP2PPlugin plugin in Plugin.P2PPlugins)
                if (!plugin.OnConsensusMessage(payload))
                    return;

            if (watchSwitch)
            {
                stopwatchConsensusPayloadCommon.Stop();
                AkkaLog.Info($"Class: ConsensusService Type: ConsensusPayloadCommon TimeSpan:{stopwatchConsensusPayloadCommon.Elapsed.TotalSeconds}");
                stopwatchConsensusPayloadCommon.Reset();
            }
            if (countSwitch) countConsensusPayloadCommon++;
            switch (message)
            {
                case ChangeView view:
                    if (watchSwitch)
                    {
                        stopwatchChangeView.Start();
                    }
                    OnChangeViewReceived(payload, view);
                    if (watchSwitch)
                    {
                        stopwatchChangeView.Stop();
                        AkkaLog.Info($"Class: ConsensusService Type: ChangeView TimeSpan:{stopwatchChangeView.Elapsed.TotalSeconds}");
                        stopwatchChangeView.Reset();
                    }
                    if (countSwitch) countChangeView++;
                    break;
                case PrepareRequest request:
                    if (watchSwitch)
                    {
                        stopwatchPrepareRequest.Start();
                    }
                    OnPrepareRequestReceived(payload, request);
                    if (watchSwitch)
                    {
                        stopwatchPrepareRequest.Stop();
                        AkkaLog.Info($"Class: ConsensusService Type: PrepareRequest TimeSpan:{stopwatchPrepareRequest.Elapsed.TotalSeconds}");
                        stopwatchPrepareRequest.Reset();
                    }
                    if (countSwitch) countPrepareRequest++;
                    break;
                case PrepareResponse response:
                    if (watchSwitch)
                    {
                        stopwatchPrepareResponse.Start();
                    }
                    OnPrepareResponseReceived(payload, response);
                    if (watchSwitch)
                    {
                        stopwatchPrepareResponse.Stop();
                        AkkaLog.Info($"Class: ConsensusService Type: PrepareResponse TimeSpan:{stopwatchPrepareResponse.Elapsed.TotalSeconds}");
                        stopwatchPrepareResponse.Reset();
                    }
                    if (countSwitch) countPrepareResponse++;
                    break;
                case Commit commit:
                    if (watchSwitch)
                    {
                        stopwatchCommit.Start();
                    }
                    OnCommitReceived(payload, commit);
                    if (watchSwitch)
                    {
                        stopwatchCommit.Stop();
                        AkkaLog.Info($"Class: ConsensusService Type: Commit TimeSpan:{stopwatchCommit.Elapsed.TotalSeconds}");
                        stopwatchCommit.Reset();
                    }
                    if (countSwitch) countCommit++;
                    break;
                case RecoveryRequest _:
                    if (watchSwitch)
                    {
                        stopwatchRecoveryRequest.Start();
                    }
                    OnRecoveryRequestReceived(payload);
                    if (watchSwitch)
                    {
                        stopwatchRecoveryRequest.Stop();
                        AkkaLog.Info($"Class: ConsensusService Type: RecoveryRequest TimeSpan:{stopwatchRecoveryRequest.Elapsed.TotalSeconds}");
                        stopwatchRecoveryRequest.Reset();
                    }
                    if (countSwitch) countRecoveryRequest++;
                    break;
                case RecoveryMessage recovery:
                    if (watchSwitch)
                    {
                        stopwatchRecoveryMessage.Start();
                    }
                    OnRecoveryMessageReceived(payload, recovery);
                    if (watchSwitch)
                    {
                        stopwatchRecoveryMessage.Stop();
                        AkkaLog.Info($"Class: ConsensusService Type: RecoveryMessage TimeSpan:{stopwatchRecoveryMessage.Elapsed.TotalSeconds}");
                        stopwatchRecoveryMessage.Reset();
                    }
                    if (countSwitch) countRecoveryMessage++;
                    break;
            }
        }

        private void OnPersistCompleted(Block block)
        {
            Log($"persist block: height={block.Index} hash={block.Hash} tx={block.Transactions.Length}");
            block_received_time = TimeProvider.Current.UtcNow;
            knownHashes.Clear();
            InitializeConsensus(0);
        }

        private void OnRecoveryMessageReceived(ConsensusPayload payload, RecoveryMessage message)
        {
            // isRecovering is always set to false again after OnRecoveryMessageReceived
            isRecovering = true;
            int validChangeViews = 0, totalChangeViews = 0, validPrepReq = 0, totalPrepReq = 0;
            int validPrepResponses = 0, totalPrepResponses = 0, validCommits = 0, totalCommits = 0;

            Log($"{nameof(OnRecoveryMessageReceived)}: height={payload.BlockIndex} view={message.ViewNumber} index={payload.ValidatorIndex}");
            try
            {
                if (message.ViewNumber > context.ViewNumber)
                {
                    if (context.CommitSent) return;
                    ConsensusPayload[] changeViewPayloads = message.GetChangeViewPayloads(context, payload);
                    totalChangeViews = changeViewPayloads.Length;
                    foreach (ConsensusPayload changeViewPayload in changeViewPayloads)
                        if (ReverifyAndProcessPayload(changeViewPayload)) validChangeViews++;
                }
                if (message.ViewNumber == context.ViewNumber && !context.NotAcceptingPayloadsDueToViewChanging && !context.CommitSent)
                {
                    if (!context.RequestSentOrReceived)
                    {
                        ConsensusPayload prepareRequestPayload = message.GetPrepareRequestPayload(context, payload);
                        if (prepareRequestPayload != null)
                        {
                            totalPrepReq = 1;
                            if (ReverifyAndProcessPayload(prepareRequestPayload)) validPrepReq++;
                        }
                        else if (context.IsPrimary)
                            SendPrepareRequest();
                    }
                    ConsensusPayload[] prepareResponsePayloads = message.GetPrepareResponsePayloads(context, payload);
                    totalPrepResponses = prepareResponsePayloads.Length;
                    foreach (ConsensusPayload prepareResponsePayload in prepareResponsePayloads)
                        if (ReverifyAndProcessPayload(prepareResponsePayload)) validPrepResponses++;
                }
                if (message.ViewNumber <= context.ViewNumber)
                {
                    // Ensure we know about all commits from lower view numbers.
                    ConsensusPayload[] commitPayloads = message.GetCommitPayloadsFromRecoveryMessage(context, payload);
                    totalCommits = commitPayloads.Length;
                    foreach (ConsensusPayload commitPayload in commitPayloads)
                        if (ReverifyAndProcessPayload(commitPayload)) validCommits++;
                }
            }
            finally
            {
                Log($"{nameof(OnRecoveryMessageReceived)}: finished (valid/total) " +
                    $"ChgView: {validChangeViews}/{totalChangeViews} " +
                    $"PrepReq: {validPrepReq}/{totalPrepReq} " +
                    $"PrepResp: {validPrepResponses}/{totalPrepResponses} " +
                    $"Commits: {validCommits}/{totalCommits}");
                isRecovering = false;
            }
        }

        private void OnRecoveryRequestReceived(ConsensusPayload payload)
        {
            // We keep track of the payload hashes received in this block, and don't respond with recovery
            // in response to the same payload that we already responded to previously.
            // ChangeView messages include a Timestamp when the change view is sent, thus if a node restarts
            // and issues a change view for the same view, it will have a different hash and will correctly respond
            // again; however replay attacks of the ChangeView message from arbitrary nodes will not trigger an
            // additional recovery message response.
            if (!knownHashes.Add(payload.Hash)) return;

            Log($"On{payload.ConsensusMessage.GetType().Name}Received: height={payload.BlockIndex} index={payload.ValidatorIndex} view={payload.ConsensusMessage.ViewNumber}");
            if (context.WatchOnly) return;
            if (!context.CommitSent)
            {
                bool shouldSendRecovery = false;
                int allowedRecoveryNodeCount = context.F;
                // Limit recoveries to be sent from an upper limit of `f` nodes
                for (int i = 1; i <= allowedRecoveryNodeCount; i++)
                {
                    var chosenIndex = (payload.ValidatorIndex + i) % context.Validators.Length;
                    if (chosenIndex != context.MyIndex) continue;
                    shouldSendRecovery = true;
                    break;
                }

                if (!shouldSendRecovery) return;
            }
            Log($"send recovery: view={context.ViewNumber}");
            localNode.Tell(new LocalNode.SendDirectly { Inventory = context.MakeRecoveryMessage() });
        }

        private void OnPrepareRequestReceived(ConsensusPayload payload, PrepareRequest message)
        {
            if (context.RequestSentOrReceived || context.NotAcceptingPayloadsDueToViewChanging) return;
            if (payload.ValidatorIndex != context.Block.ConsensusData.PrimaryIndex || message.ViewNumber != context.ViewNumber) return;
            Log($"{nameof(OnPrepareRequestReceived)}: height={payload.BlockIndex} view={message.ViewNumber} index={payload.ValidatorIndex} tx={message.TransactionHashes.Length}");
            if (message.Timestamp <= context.PrevHeader.Timestamp || message.Timestamp > TimeProvider.Current.UtcNow.AddMinutes(10).ToTimestampMS())
            {
                Log($"Timestamp incorrect: {message.Timestamp}", Plugins.LogLevel.Warning);
                return;
            }
            if (message.TransactionHashes.Any(p => context.Snapshot.ContainsTransaction(p)))
            {
                Log($"Invalid request: transaction already exists", Plugins.LogLevel.Warning);
                return;
            }

            // Timeout extension: prepare request has been received with success
            // around 2*15/M=30.0/5 ~ 40% block time (for M=5)
            ExtendTimerByFactor(2);

            context.Block.Timestamp = message.Timestamp;
            context.Block.ConsensusData.Nonce = message.Nonce;
            context.TransactionHashes = message.TransactionHashes;
            context.Transactions = new Dictionary<UInt256, Transaction>();
            for (int i = 0; i < context.PreparationPayloads.Length; i++)
                if (context.PreparationPayloads[i] != null)
                    if (!context.PreparationPayloads[i].GetDeserializedMessage<PrepareResponse>().PreparationHash.Equals(payload.Hash))
                        context.PreparationPayloads[i] = null;
            context.PreparationPayloads[payload.ValidatorIndex] = payload;
            byte[] hashData = context.EnsureHeader().GetHashData();
            for (int i = 0; i < context.CommitPayloads.Length; i++)
                if (context.CommitPayloads[i]?.ConsensusMessage.ViewNumber == context.ViewNumber)
                    if (!Crypto.Default.VerifySignature(hashData, context.CommitPayloads[i].GetDeserializedMessage<Commit>().Signature, context.Validators[i].EncodePoint(false)))
                        context.CommitPayloads[i] = null;

            if (context.TransactionHashes.Length == 0)
            {
                // There are no tx so we should act like if all the transactions were filled
                CheckPrepareResponse();
                return;
            }

            Dictionary<UInt256, Transaction> mempoolVerified = Blockchain.Singleton.MemPool.GetVerifiedTransactions().ToDictionary(p => p.Hash);
            List<Transaction> unverified = new List<Transaction>();
            foreach (UInt256 hash in context.TransactionHashes)
            {
                if (mempoolVerified.TryGetValue(hash, out Transaction tx))
                {
                    if (!AddTransaction(tx, false))
                        return;
                }
                else
                {
                    if (Blockchain.Singleton.MemPool.TryGetValue(hash, out tx))
                        unverified.Add(tx);
                }
            }
            foreach (Transaction tx in unverified)
                if (!AddTransaction(tx, true))
                    return;
            if (context.Transactions.Count < context.TransactionHashes.Length)
            {
                UInt256[] hashes = context.TransactionHashes.Where(i => !context.Transactions.ContainsKey(i)).ToArray();
                taskManager.Tell(new TaskManager.RestartTasks
                {
                    Payload = InvPayload.Create(InventoryType.TX, hashes)
                });
            }
        }

        private void OnPrepareResponseReceived(ConsensusPayload payload, PrepareResponse message)
        {
            if (message.ViewNumber != context.ViewNumber) return;
            if (context.PreparationPayloads[payload.ValidatorIndex] != null || context.NotAcceptingPayloadsDueToViewChanging) return;
            if (context.PreparationPayloads[context.Block.ConsensusData.PrimaryIndex] != null && !message.PreparationHash.Equals(context.PreparationPayloads[context.Block.ConsensusData.PrimaryIndex].Hash))
                return;

            // Timeout extension: prepare response has been received with success
            // around 2*15/M=30.0/5 ~ 40% block time (for M=5)
            ExtendTimerByFactor(2);

            Log($"{nameof(OnPrepareResponseReceived)}: height={payload.BlockIndex} view={message.ViewNumber} index={payload.ValidatorIndex}");
            context.PreparationPayloads[payload.ValidatorIndex] = payload;
            if (context.WatchOnly || context.CommitSent) return;
            if (context.RequestSentOrReceived)
                CheckPreparations();
        }

        protected override void OnReceive(object message)
        {
            if (message is Start options)
            {
                if (started) return;
                OnStart(options);
            }
            else
            {
                if (!started) return;
                switch (message)
                {
                    case SetViewNumber setView:
                        if (watchSwitch)
                        {
                            stopwatchSetViewNumber.Start();
                        }
                        InitializeConsensus(setView.ViewNumber);
                        if (watchSwitch)
                        {
                            stopwatchSetViewNumber.Stop();
                            AkkaLog.Info($"Class: ConsensusService Type: SetViewNumber TimeSpan:{stopwatchSetViewNumber.Elapsed.TotalSeconds}");
                            stopwatchSetViewNumber.Reset();
                        }
                        if (countSwitch) countSetViewNumber++;
                        break;
                    case Timer timer:
                        if (watchSwitch)
                        {
                            stopwatchTimer.Start();
                        }
                        OnTimer(timer);
                        if (watchSwitch)
                        {
                            stopwatchTimer.Stop();
                            AkkaLog.Info($"Class: ConsensusService Type: Timer TimeSpan:{stopwatchTimer.Elapsed.TotalSeconds}");
                            stopwatchTimer.Reset();
                        }
                        if (countSwitch) countTimer++;
                        break;
                    case ConsensusPayload payload:
                        OnConsensusPayload(payload);
                        break;
                    case Transaction transaction:
                        if (watchSwitch)
                        {
                            stopwatchTransaction.Start();
                        }
                        OnTransaction(transaction);
                        if (watchSwitch)
                        {
                            stopwatchTransaction.Stop();
                            AkkaLog.Info($"Class: ConsensusService Type: Transaction TimeSpan:{stopwatchTransaction.Elapsed.TotalSeconds}");
                            stopwatchTransaction.Reset();
                        }
                        if (countSwitch) countTransaction++;
                        break;
                    case Blockchain.PersistCompleted completed:
                        if (watchSwitch)
                        {
                            stopwatchPersistCompleted.Start();
                        }
                        OnPersistCompleted(completed.Block);
                        if (watchSwitch)
                        {
                            stopwatchPersistCompleted.Stop();
                            AkkaLog.Info($"Class: ConsensusService Type: PersistCompleted TimeSpan:{stopwatchPersistCompleted.Elapsed.TotalSeconds}");
                            stopwatchPersistCompleted.Reset();
                        }
                        if (countSwitch) countPersistCompleted++;
                        break;
                }
            }
        }

        private void RequestRecovery()
        {
            if (context.Block.Index == Blockchain.Singleton.HeaderHeight + 1)
                localNode.Tell(new LocalNode.SendDirectly { Inventory = context.MakeRecoveryRequest() });
        }

        private void OnStart(Start options)
        {
            Log("OnStart");
            started = true;
            if (!options.IgnoreRecoveryLogs && context.Load())
            {
                if (context.Transactions != null)
                {
                    Sender.Ask<Blockchain.FillCompleted>(new Blockchain.FillMemoryPool
                    {
                        Transactions = context.Transactions.Values
                    }).Wait();
                }
                if (context.CommitSent)
                {
                    CheckPreparations();
                    return;
                }
            }
            InitializeConsensus(0);
            // Issue a ChangeView with NewViewNumber of 0 to request recovery messages on start-up.
            if (!context.WatchOnly)
                RequestRecovery();
        }

        private void OnTimer(Timer timer)
        {
            if (context.WatchOnly || context.BlockSent) return;
            if (timer.Height != context.Block.Index || timer.ViewNumber != context.ViewNumber) return;
            Log($"timeout: height={timer.Height} view={timer.ViewNumber}");
            if (context.IsPrimary && !context.RequestSentOrReceived)
            {
                SendPrepareRequest();
            }
            else if ((context.IsPrimary && context.RequestSentOrReceived) || context.IsBackup)
            {
                if (context.CommitSent)
                {
                    // Re-send commit periodically by sending recover message in case of a network issue.
                    Log($"send recovery to resend commit");
                    localNode.Tell(new LocalNode.SendDirectly { Inventory = context.MakeRecoveryMessage() });
                    ChangeTimer(TimeSpan.FromMilliseconds(Blockchain.MillisecondsPerBlock << 1));
                }
                else
                {
                    var reason = ChangeViewReason.Timeout;

                    if (context.Block != null && context.TransactionHashes?.Count() > context.Transactions?.Count)
                    {
                        reason = ChangeViewReason.TxNotFound;
                    }

                    RequestChangeView(reason);
                }
            }
        }

        private void OnTransaction(Transaction transaction)
        {
            if (!context.IsBackup || context.NotAcceptingPayloadsDueToViewChanging || !context.RequestSentOrReceived || context.ResponseSent || context.BlockSent)
                return;
            if (context.Transactions.ContainsKey(transaction.Hash)) return;
            if (!context.TransactionHashes.Contains(transaction.Hash)) return;
            AddTransaction(transaction, true);
        }

        protected override void PostStop()
        {
            Log("OnStop");
            started = false;
            Context.System.EventStream.Unsubscribe(Self);
            context.Dispose();
            base.PostStop();
        }

        public static Props Props(IActorRef localNode, IActorRef taskManager, Store store, Wallet wallet)
        {
            return Akka.Actor.Props.Create(() => new ConsensusService(localNode, taskManager, store, wallet)).WithMailbox("consensus-service-mailbox");
        }

        private void RequestChangeView(ChangeViewReason reason)
        {
            if (context.WatchOnly) return;
            // Request for next view is always one view more than the current context.ViewNumber
            // Nodes will not contribute for changing to a view higher than (context.ViewNumber+1), unless they are recovered
            // The latter may happen by nodes in higher views with, at least, `M` proofs
            byte expectedView = context.ViewNumber;
            expectedView++;
            ChangeTimer(TimeSpan.FromMilliseconds(Blockchain.MillisecondsPerBlock << (expectedView + 1)));
            if ((context.CountCommitted + context.CountFailed) > context.F)
            {
                Log($"Skip requesting change view to nv={expectedView} because nc={context.CountCommitted} nf={context.CountFailed}");
                RequestRecovery();
                return;
            }
            Log($"request change view: height={context.Block.Index} view={context.ViewNumber} nv={expectedView} nc={context.CountCommitted} nf={context.CountFailed}");
            localNode.Tell(new LocalNode.SendDirectly { Inventory = context.MakeChangeView(reason) });
            CheckExpectedView(expectedView);
        }

        private bool ReverifyAndProcessPayload(ConsensusPayload payload)
        {
            if (!payload.Verify(context.Snapshot)) return false;
            OnConsensusPayload(payload);
            return true;
        }

        private void SendPrepareRequest()
        {
            Log($"send prepare request: height={context.Block.Index} view={context.ViewNumber}");
            localNode.Tell(new LocalNode.SendDirectly { Inventory = context.MakePrepareRequest() });

            if (context.Validators.Length == 1)
                CheckPreparations();

            if (context.TransactionHashes.Length > 0)
            {
                foreach (InvPayload payload in InvPayload.CreateGroup(InventoryType.TX, context.TransactionHashes))
                    localNode.Tell(Message.Create(MessageCommand.Inv, payload));
            }
            ChangeTimer(TimeSpan.FromMilliseconds((Blockchain.MillisecondsPerBlock << (context.ViewNumber + 1)) - (context.ViewNumber == 0 ? Blockchain.MillisecondsPerBlock : 0)));
        }
    }

    internal class ConsensusServiceMailbox : PriorityMailbox
    {
        public ConsensusServiceMailbox(Akka.Actor.Settings settings, Config config)
            : base(settings, config)
        {
        }

        internal protected override bool IsHighPriority(object message)
        {
            switch (message)
            {
                case ConsensusPayload _:
                case ConsensusService.SetViewNumber _:
                case ConsensusService.Timer _:
                case Blockchain.PersistCompleted _:
                    return true;
                default:
                    return false;
            }
        }
    }
}
