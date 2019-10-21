using Akka.Actor;
using Akka.Configuration;
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
            //如果是需要校验的交易，就来校验
            if (verify && !tx.Verify(context.Snapshot, context.Transactions.Values))
            {
                Log($"Invalid transaction: {tx.Hash}{Environment.NewLine}{tx.ToArray().ToHexString()}", LogLevel.Warning);
                RequestChangeView(ChangeViewReason.TxInvalid);
                return false;
            }
            //校验账户中是否由被block的账户
            if (!NativeContract.Policy.CheckPolicy(tx, context.Snapshot))
            {
                Log($"reject tx: {tx.Hash}{Environment.NewLine}{tx.ToArray().ToHexString()}", LogLevel.Warning);
                RequestChangeView(ChangeViewReason.TxRejectedByPolicy);
                return false;
            }
            //添加交易到context中
            context.Transactions[tx.Hash] = tx;
            //检查并发送prepareResponse
            return CheckPrepareResponse();
        }

        private bool CheckPrepareResponse()
        {
            //如果凑齐了所有需要的交易，则发送prepareResponse
            if (context.TransactionHashes.Length == context.Transactions.Count)
            {
                // if we are the primary for this view, but acting as a backup because we recovered our own
                // previously sent prepare request, then we don't want to send a prepare response.
                //如果自己是议长节点，则不需要发送prepare response
                if (context.IsPrimary || context.WatchOnly) return true;

                // Check maximum block size via Native Contract policy
                //预估区块大小如果超出最大上限，则changeview
                if (context.GetExpectedBlockSize() > NativeContract.Policy.GetMaxBlockSize(context.Snapshot))
                {
                    Log($"rejected block: {context.Block.Index}{Environment.NewLine} The size exceed the policy", LogLevel.Warning);
                    RequestChangeView(ChangeViewReason.BlockRejectedByPolicy);
                    return false;
                }

                // Timeout extension due to prepare response sent
                // around 2*15/M=30.0/5 ~ 40% block time (for M=5)
                //延长40%的区块时间：6s
                ExtendTimerByFactor(2);

                Log($"send prepare response");
                //发送prepare response
                localNode.Tell(new LocalNode.SendDirectly { Inventory = context.MakePrepareResponse() });
                CheckPreparations();
            }
            return true;
        }

        private void ChangeTimer(TimeSpan delay)
        {
            clock_started = TimeProvider.Current.UtcNow;
            expected_delay = delay;
            //取消上个定时器
            timer_token.CancelIfNotNull();
            //重新设置一次性定时器
            timer_token = Context.System.Scheduler.ScheduleTellOnceCancelable(delay, Self, new Timer
            {
                Height = context.Block.Index,
                ViewNumber = context.ViewNumber
            }, ActorRefs.NoSender);
        }

        private void CheckCommits()
        {
            //收集到足够多的commit，每一个view号都等于自己的view号，交易已经收集齐
            if (context.CommitPayloads.Count(p => p?.ConsensusMessage.ViewNumber == context.ViewNumber) >= context.M && context.TransactionHashes.All(p => context.Transactions.ContainsKey(p)))
            {
                //区块填入Witness（多方议长、员签名）和transaction并广播
                Block block = context.CreateBlock();
                Log($"relay block: height={block.Index} hash={block.Hash} tx={block.Transactions.Length}");
                localNode.Tell(new LocalNode.Relay { Inventory = block });
            }
        }

        private void CheckExpectedView(byte viewNumber)
        {
            //原来的viewNumber不能大于等于最新的viewNumber
            if (context.ViewNumber >= viewNumber) return;
            // if there are `M` change view payloads with NewViewNumber greater than viewNumber, then, it is safe to move
            if (context.ChangeViewPayloads.Count(p => p != null && p.GetDeserializedMessage<ChangeView>().NewViewNumber >= viewNumber) >= context.M)
            {
                if (!context.WatchOnly)
                {
                    //如果收到的changeview的view号比自己期待的view号大或者自己还没准备changeView，则广播同意移动到的viewNumber号（bug，实际上移动到的是自己的view+1号）
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
            //如果收到的PrepareResonse超过2/3，并且交易全部收集齐了
            if (context.PreparationPayloads.Count(p => p != null) >= context.M && context.TransactionHashes.All(p => context.Transactions.ContainsKey(p)))
            {
                //制作commit消息,如果有commit就不制作了
                ConsensusPayload payload = context.MakeCommit();
                Log($"send commit");
                //序列化保存ConsensusContext，此时以后就不会再对此个提案块做更改了，也不会changeView
                context.Save();
                //广播commit
                localNode.Tell(new LocalNode.SendDirectly { Inventory = payload });
                // Set timer, so we will resend the commit in case of a networking issue
                ChangeTimer(TimeSpan.FromMilliseconds(Blockchain.MillisecondsPerBlock));
                //检查CommitPayloads，足够多了就继续往下执行
                CheckCommits();
            }
        }

        private void InitializeConsensus(byte viewNumber)
        {
            //重置context的内容
            context.Reset(viewNumber);
            if (viewNumber > 0)
                Log($"changeview: view={viewNumber} primary={context.Validators[context.GetPrimaryIndex((byte)(viewNumber - 1u))]}", LogLevel.Warning);
            Log($"initialize: height={context.Block.Index} view={viewNumber} index={context.MyIndex} role={(context.IsPrimary ? "Primary" : context.WatchOnly ? "WatchOnly" : "Backup")}");
            if (context.WatchOnly) return;
            //议长节点行为
            if (context.IsPrimary)
            {
                //处理recoveryMessage是会为true
                if (isRecovering)
                {
                    ChangeTimer(TimeSpan.FromMilliseconds(Blockchain.MillisecondsPerBlock << (viewNumber + 1)));
                }
                else
                {
                    //如果是上个区块完成后立刻调用的此方法，则span不会超过Blockchain.TimePerBlock，如果是超时changeview，则会导致span过大，则使定时任务立即发送一条Timer消息，开始发起一轮PrepareRequest。
                    TimeSpan span = TimeProvider.Current.UtcNow - block_received_time;
                    if (span >= Blockchain.TimePerBlock)
                        ChangeTimer(TimeSpan.Zero);
                    else
                        ChangeTimer(Blockchain.TimePerBlock - span);
                }
            }
            else
            //非议长节点会设置超时时间作为changeview的发起时间，最小为两倍出快时间，即30s
            {
                ChangeTimer(TimeSpan.FromMilliseconds(Blockchain.MillisecondsPerBlock << (viewNumber + 1)));
            }
        }

        private void Log(string message, LogLevel level = LogLevel.Info)
        {
            Plugin.Log(nameof(ConsensusService), level, message);
        }

        private void OnChangeViewReceived(ConsensusPayload payload, ChangeView message)
        {
            //如果发现请求的view号比当前节点上下文的view号还小，则直接给请求节点发送RecoveryMessage
            if (message.NewViewNumber <= context.ViewNumber)
                OnRecoveryRequestReceived(payload);
            //如果已经发送过commit则不会处理changeView
            if (context.CommitSent) return;

            //检查发送者的changeView有没有重复发送，重复发送的话就忽略
            var expectedView = context.ChangeViewPayloads[payload.ValidatorIndex]?.GetDeserializedMessage<ChangeView>().NewViewNumber ?? (byte)0;
            if (message.NewViewNumber <= expectedView)
                return;

            Log($"{nameof(OnChangeViewReceived)}: height={payload.BlockIndex} view={message.ViewNumber} index={payload.ValidatorIndex} nv={message.NewViewNumber} reason={message.Reason}");
            //保存changeView消息
            context.ChangeViewPayloads[payload.ValidatorIndex] = payload;
            //检查changeView消息，继续向下执行
            CheckExpectedView(message.NewViewNumber);
        }

        private void OnCommitReceived(ConsensusPayload payload, Commit commit)
        {
            //如果已经收到过发送者的commit消息，而且新收到的消息hash和已收到的commithash对不上，则不处理。
            ref ConsensusPayload existingCommitPayload = ref context.CommitPayloads[payload.ValidatorIndex];
            if (existingCommitPayload != null)
            {
                if (existingCommitPayload.Hash != payload.Hash)
                    Log($"{nameof(OnCommitReceived)}: different commit from validator! height={payload.BlockIndex} index={payload.ValidatorIndex} view={commit.ViewNumber} existingView={existingCommitPayload.ConsensusMessage.ViewNumber}", LogLevel.Warning);
                return;
            }

            // Timeout extension: commit has been received with success
            // around 4*15s/M=60.0s/5=12.0s ~ 80% block time (for M=5)
            //延长12s的处理时间
            ExtendTimerByFactor(4);

            //必须是commit的视图号是当前视图号才能处理，否则就等着节点超时
            if (commit.ViewNumber == context.ViewNumber)
            {
                Log($"{nameof(OnCommitReceived)}: height={payload.BlockIndex} view={commit.ViewNumber} index={payload.ValidatorIndex} nc={context.CountCommitted} nf={context.CountFailed}");

                byte[] hashData = context.EnsureHeader()?.GetHashData();
                //当block中txHashes为空的时候，说明还没收到过PrepareRequest，不做处理，只保存commit
                if (hashData == null)
                {
                    existingCommitPayload = payload;
                }
                //校验commit的签名，继续下个流程
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
            //如果区块已经发送，则忽略
            if (context.BlockSent) return;
            if (payload.Version != context.Block.Version) return;
            //当前块高对不上，上一个块的hash对不上，则忽略
            if (payload.PrevHash != context.Block.PrevHash || payload.BlockIndex != context.Block.Index)
            {
                if (context.Block.Index < payload.BlockIndex)
                {
                    Log($"chain sync: expected={payload.BlockIndex} current={context.Block.Index - 1} nodes={LocalNode.Singleton.ConnectedCount}", LogLevel.Warning);
                }
                return;
            }
            //发送者的id超出验证者数量，则忽略
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
            //更新发送者的上一条发送信息为payload中的块高
            context.LastSeenMessage[payload.ValidatorIndex] = (int)payload.BlockIndex;
            foreach (IP2PPlugin plugin in Plugin.P2PPlugins)
                if (!plugin.OnConsensusMessage(payload))
                    return;
            switch (message)
            {
                //done
                case ChangeView view:
                    OnChangeViewReceived(payload, view);
                    break;
                //done
                case PrepareRequest request:
                    OnPrepareRequestReceived(payload, request);
                    break;
                //done
                case PrepareResponse response:
                    OnPrepareResponseReceived(payload, response);
                    break;
                //done
                case Commit commit:
                    OnCommitReceived(payload, commit);
                    break;
                //done
                case RecoveryRequest _:
                    OnRecoveryRequestReceived(payload);
                    break;
                    
                case RecoveryMessage recovery:
                    OnRecoveryMessageReceived(payload, recovery);
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
                //当recovery消息的viewNumber大于context的viewNumber时
                if (message.ViewNumber > context.ViewNumber)
                {
                    //已经发送过commit的话，忽略
                    if (context.CommitSent) return;
                    ConsensusPayload[] changeViewPayloads = message.GetChangeViewPayloads(context, payload);
                    totalChangeViews = changeViewPayloads.Length;
                    //循环校验和处理changeView的消息，即如果全处理完，并通过，会进行changeView动作，viewNumber相应增加
                    foreach (ConsensusPayload changeViewPayload in changeViewPayloads)
                        if (ReverifyAndProcessPayload(changeViewPayload)) validChangeViews++;
                }
                //如果视图版本号和消息中的相等，并且不处于changeView中，并且没有发送commit
                if (message.ViewNumber == context.ViewNumber && !context.NotAcceptingPayloadsDueToViewChanging && !context.CommitSent)
                {
                    //如果没有发送或收到PrepareRequest说明刚刚changeView
                    if (!context.RequestSentOrReceived)
                    {
                        ConsensusPayload prepareRequestPayload = message.GetPrepareRequestPayload(context, payload);
                        //如果有PrepareRequest，则进行OnPrepareRequestReceived的处理
                        if (prepareRequestPayload != null)
                        {
                            totalPrepReq = 1;
                            if (ReverifyAndProcessPayload(prepareRequestPayload)) validPrepReq++;
                        }
                        //如果没有PrepareRequest，且为主节点，则发送PrepareRequest
                        else if (context.IsPrimary)
                            SendPrepareRequest();
                    }
                    //赋值除了议长节点其他的议员节点的prepareResponse
                    ConsensusPayload[] prepareResponsePayloads = message.GetPrepareResponsePayloads(context, payload);
                    totalPrepResponses = prepareResponsePayloads.Length;
                    //校验并处理prepareResponse
                    foreach (ConsensusPayload prepareResponsePayload in prepareResponsePayloads)
                        if (ReverifyAndProcessPayload(prepareResponsePayload)) validPrepResponses++;
                }
                if (message.ViewNumber <= context.ViewNumber)
                {
                    // Ensure we know about all commits from lower view numbers.
                    //存储所有commit，如果消息视图号小于当前上下文编号，则只记录，不处理commit
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
            //防止相同时间戳的RecoverRequest的攻击
            if (!knownHashes.Add(payload.Hash)) return;

            Log($"On{payload.ConsensusMessage.GetType().Name}Received: height={payload.BlockIndex} index={payload.ValidatorIndex} view={payload.ConsensusMessage.ViewNumber}");
            if (context.WatchOnly) return;
            //如果该节点已经发送过commit，则直接广播RecoveryMessage
            if (!context.CommitSent)
            {
                bool shouldSendRecovery = false;
                int allowedRecoveryNodeCount = context.F;
                // Limit recoveries to be sent from an upper limit of `f` nodes
                //并不是所有未发送commit的节点收到recoveryRequest之后会发送RecoveryMessage，只有算法指定的节点才会发送，节点个数等于F个数。
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
            //制作RecoveryMessage并广播
            localNode.Tell(new LocalNode.SendDirectly { Inventory = context.MakeRecoveryMessage() });
        }

        //只会接收到一条 PrepareRequest
        private void OnPrepareRequestReceived(ConsensusPayload payload, PrepareRequest message)
        {
            //交易处于已发送或接受prepare状态或者处于changeview状态，则忽略
            if (context.RequestSentOrReceived || context.NotAcceptingPayloadsDueToViewChanging) return;
            //如果不是议长节点发的或者视图编号不等，则忽略
            if (payload.ValidatorIndex != context.Block.ConsensusData.PrimaryIndex || message.ViewNumber != context.ViewNumber) return;
            Log($"{nameof(OnPrepareRequestReceived)}: height={payload.BlockIndex} view={message.ViewNumber} index={payload.ValidatorIndex} tx={message.TransactionHashes.Length}");
            //校验时间戳
            if (message.Timestamp <= context.PrevHeader.Timestamp || message.Timestamp > TimeProvider.Current.UtcNow.AddMinutes(10).ToTimestampMS())
            {
                Log($"Timestamp incorrect: {message.Timestamp}", LogLevel.Warning);
                return;
            }
            //校验数据库中是否包含当前的新交易
            if (message.TransactionHashes.Any(p => context.Snapshot.ContainsTransaction(p)))
            {
                Log($"Invalid request: transaction already exists", LogLevel.Warning);
                return;
            }

            // Timeout extension: prepare request has been received with success
            // around 2*15/M=30.0/5 ~ 40% block time (for M=5)
            //议员节点继上次设置changeView时间后重新延长定时，延长40%的区块时间：6s
            ExtendTimerByFactor(2);

            //接收prepareRequest，赋值context
            context.Block.Timestamp = message.Timestamp;
            context.Block.ConsensusData.Nonce = message.Nonce;
            //此时TransactionHashes接收到的一定不为null，因为议长对其进行了数组初始化
            context.TransactionHashes = message.TransactionHashes;
            context.Transactions = new Dictionary<UInt256, Transaction>();
            //存储PrepareRequest之前 清空PreparationPayloads
            for (int i = 0; i < context.PreparationPayloads.Length; i++)
                if (context.PreparationPayloads[i] != null)
                    if (!context.PreparationPayloads[i].GetDeserializedMessage<PrepareResponse>().PreparationHash.Equals(payload.Hash))
                        context.PreparationPayloads[i] = null;
            //赋值PrepareRequest给议长的PreparationPayloads
            context.PreparationPayloads[payload.ValidatorIndex] = payload;
            //计算默克尔树根之后，计算context hash
            byte[] hashData = context.EnsureHeader().GetHashData();
            //排除提前收到commit的情况，如果存在commit且校验不通过，则删除
            for (int i = 0; i < context.CommitPayloads.Length; i++)
                if (context.CommitPayloads[i]?.ConsensusMessage.ViewNumber == context.ViewNumber)
                    if (!Crypto.Default.VerifySignature(hashData, context.CommitPayloads[i].GetDeserializedMessage<Commit>().Signature, context.Validators[i].EncodePoint(false)))
                        context.CommitPayloads[i] = null;

            //如果没有交易，就直接向下执行了
            if (context.TransactionHashes.Length == 0)
            {
                // There are no tx so we should act like if all the transactions were filled
                CheckPrepareResponse();
                return;
            }
            //有交易的话要封装交易
            Dictionary<UInt256, Transaction> mempoolVerified = Blockchain.Singleton.MemPool.GetVerifiedTransactions().ToDictionary(p => p.Hash);
            List<Transaction> unverified = new List<Transaction>();
            foreach (UInt256 hash in context.TransactionHashes)
            {
                //从已校验池里获取交易添加到context中
                if (mempoolVerified.TryGetValue(hash, out Transaction tx))
                {
                    if (!AddTransaction(tx, false))
                        return;
                }
                else
                {
                    //从未校验池里获取交易添加到临时list unverified中
                    if (Blockchain.Singleton.MemPool.TryGetValue(hash, out tx))
                        unverified.Add(tx);
                }
            }
            //把unverified的tx经校验后添加到context中
            foreach (Transaction tx in unverified)
                if (!AddTransaction(tx, true))
                    return;
            //本地交易池没有的交易，需要找其他节点去要
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
            //viewnumber要对应的上
            if (message.ViewNumber != context.ViewNumber) return;
            //发送者的消息不能已存在，不能是changeView中
            if (context.PreparationPayloads[payload.ValidatorIndex] != null || context.NotAcceptingPayloadsDueToViewChanging) return;
            //当已经收到request后校验request的hash和response的hash是否对的上
            if (context.PreparationPayloads[context.Block.ConsensusData.PrimaryIndex] != null && !message.PreparationHash.Equals(context.PreparationPayloads[context.Block.ConsensusData.PrimaryIndex].Hash))
                return;

            // Timeout extension: prepare response has been received with success
            // around 2*15/M=30.0/5 ~ 40% block time (for M=5)
            //延长6s超时时间
            ExtendTimerByFactor(2);

            Log($"{nameof(OnPrepareResponseReceived)}: height={payload.BlockIndex} view={message.ViewNumber} index={payload.ValidatorIndex}");
            //接收PrepareResponse
            context.PreparationPayloads[payload.ValidatorIndex] = payload;
            //如果是watchonly节点，或者已经发送了commit的就不再处理了
            if (context.WatchOnly || context.CommitSent) return;
            //如果已经收到过或发出过PrepareRequest，则检查Prepare，并往下一步走
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
                    //未使用
                    case SetViewNumber setView:
                        InitializeConsensus(setView.ViewNumber);
                        break;
                    //共识议长打包、议员changeview的超时定时任务
                    case Timer timer:
                        OnTimer(timer);
                        break;
                    //共识消息
                    case ConsensusPayload payload:
                        OnConsensusPayload(payload);
                        break;
                    //补充发送本地缺失的transaction
                    case Transaction transaction:
                        OnTransaction(transaction);
                        break;
                    //持久化完成的消息
                    case Blockchain.PersistCompleted completed:
                        OnPersistCompleted(completed.Block);
                        break;
                }
            }
        }

        private void RequestRecovery()
        {
            //正在共识的提案块为当前数据库区块头高+1时才做recovery，说明此前没有收到过该提案块的对应区块
            if (context.Block.Index == Blockchain.Singleton.HeaderHeight + 1)
                //recoveryRequest中会包含时间戳
                localNode.Tell(new LocalNode.SendDirectly { Inventory = context.MakeRecoveryRequest() });
        }

        private void OnStart(Start options)
        {
            Log("OnStart");
            started = true;
            //加载序列化的consensusContext
            if (!options.IgnoreRecoveryLogs && context.Load())
            {
                //将context中的交易放入内存池，同步等待放入完成
                if (context.Transactions != null)
                {
                    Sender.Ask<Blockchain.FillCompleted>(new Blockchain.FillMemoryPool
                    {
                        Transactions = context.Transactions.Values
                    }).Wait();
                }
                //如果已经发送过commit，则检查prepareResponse，并重发commit
                if (context.CommitSent)
                {
                    CheckPreparations();
                    return;
                }
            }
            //初始化共识
            InitializeConsensus(0);
            // Issue a ChangeView with NewViewNumber of 0 to request recovery messages on start-up.
            if (!context.WatchOnly)
                RequestRecovery();
        }

        private void OnTimer(Timer timer)
        {
            //观察者节点或者block已经组装完成并发送，则返回（blockSent应对该节点为议员节点超时的情形，如果是议长节点，刚初始化block不可能blockSent）
            if (context.WatchOnly || context.BlockSent) return;
            //timer的块高和视图号已经跟不上当前的状态，则返回
            if (timer.Height != context.Block.Index || timer.ViewNumber != context.ViewNumber) return;
            Log($"timeout: height={timer.Height} view={timer.ViewNumber}");
            //议长未发送过prepareRequest
            if (context.IsPrimary && !context.RequestSentOrReceived)
            {
                SendPrepareRequest();
            }
            //非议长节点或者已经发送过prepareRequest
            else if ((context.IsPrimary && context.RequestSentOrReceived) || context.IsBackup)
            {
                //无论是议长还是议员，发送过commit，发送RecoveryMessage
                if (context.CommitSent)
                {
                    // Re-send commit periodically by sending recover message in case of a network issue.
                    Log($"send recovery to resend commit");
                    localNode.Tell(new LocalNode.SendDirectly { Inventory = context.MakeRecoveryMessage() });
                    ChangeTimer(TimeSpan.FromMilliseconds(Blockchain.MillisecondsPerBlock << 1));
                }
                //没法送过commit，发送changeView
                else
                {
                    var reason = ChangeViewReason.Timeout;

                    if (context.Block != null && context.TransactionHashes?.Count() > context.Transactions?.Count)
                    {
                        reason = ChangeViewReason.TxNotFound;
                    }

                    //请求changeView
                    RequestChangeView(reason);
                }
            }
        }

        private void OnTransaction(Transaction transaction)
        {
            //接收交易条件，非议长节点、不能在changeview期间、必须接收到Prepare request、不能发送过Prepare response，更不能发送过区块
            if (!context.IsBackup || context.NotAcceptingPayloadsDueToViewChanging || !context.RequestSentOrReceived || context.ResponseSent || context.BlockSent)
                return;
            //该交易不能在context中存在，且必须存在于TxHash集合中。
            if (context.Transactions.ContainsKey(transaction.Hash)) return;
            if (!context.TransactionHashes.Contains(transaction.Hash)) return;
            //新接收的交易需要校验
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

        //请求changeView的原因：超时，交易找不到，交易校验不通过，交易规则校验不通过，区块规则校验不通过
        private void RequestChangeView(ChangeViewReason reason)
        {
            if (context.WatchOnly) return;
            // Request for next view is always one view more than the current context.ViewNumber
            // Nodes will not contribute for changing to a view higher than (context.ViewNumber+1), unless they are recovered
            // The latter may happen by nodes in higher views with, at least, `M` proofs
            byte expectedView = context.ViewNumber;
            expectedView++;
            //定时器至少延长60s
            ChangeTimer(TimeSpan.FromMilliseconds(Blockchain.MillisecondsPerBlock << (expectedView + 1)));
            //在不能拥有达成ChangeView的节点个数时，先申请恢复。
            if ((context.CountCommitted + context.CountFailed) > context.F)
            {
                Log($"Skip requesting change view to nv={expectedView} because nc={context.CountCommitted} nf={context.CountFailed}");
                RequestRecovery();
                return;
            }
            Log($"request change view: height={context.Block.Index} view={context.ViewNumber} nv={expectedView} nc={context.CountCommitted} nf={context.CountFailed}");
            //制作广播changeView消息，并把自己的消息储存到ChangeViewPayloads中
            localNode.Tell(new LocalNode.SendDirectly { Inventory = context.MakeChangeView(reason) });
            //检查changeView消息有没有收齐，收齐则继续向下执行
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
            //生成PrepareRequest并广播
            localNode.Tell(new LocalNode.SendDirectly { Inventory = context.MakePrepareRequest() });

            //如果是单共识节点，直接检查PreparationPayloads集合
            if (context.Validators.Length == 1)
                CheckPreparations();

            //广播交易Inv，交易hash打包分组发送，每组500条交易hash
            if (context.TransactionHashes.Length > 0)
            {
                foreach (InvPayload payload in InvPayload.CreateGroup(InventoryType.TX, context.TransactionHashes))
                    localNode.Tell(Message.Create(MessageCommand.Inv, payload));
            }
            //计时器更新，最少15s
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
