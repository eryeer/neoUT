using Akka.Actor;
using Akka.Util.Internal;
using Neo.Consensus;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.Plugins;
using Neo.SmartContract.Native;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Neo.Ledger
{
    public class MemoryPool : IReadOnlyCollection<Transaction>
    {
        public static int PersitTxCount8_2_2_1 = 0;
        public static int PersitTxCount8_2_2_2 = 0;
        public static int PersitTxCount8_2_2_1_1 = 0;
        public static int PersitTxCount8_2_2_1_2 = 0;
        public static int PersitTxCount8_2_2_1_3 = 0;
        public static System.Diagnostics.Stopwatch stopwatchPersistPhase8_2_2_1 = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchPersistPhase8_2_2_2 = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchPersistPhase8_2_2_1_1 = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchPersistPhase8_2_2_1_2 = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchPersistPhase8_2_2_1_3 = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchTxTryAdd5_1 = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchTxTryAdd5_2 = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchTxTryAdd5_3 = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchTxTryAdd5_4 = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchTxTryAdd5_5 = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchTxTryAdd5_6 = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchTxTryAdd5_7 = new System.Diagnostics.Stopwatch();
        public static System.Diagnostics.Stopwatch stopwatchTxTryAdd5_8 = new System.Diagnostics.Stopwatch();

        public static double totalTimeTxTryAdd5_1 = 0;
        public static double totalTimeTxTryAdd5_2 = 0;
        public static double totalTimeTxTryAdd5_3 = 0;
        public static double totalTimeTxTryAdd5_4 = 0;
        public static double totalTimeTxTryAdd5_5 = 0;
        public static double totalTimeTxTryAdd5_6 = 0;
        public static double totalTimeTxTryAdd5_7 = 0;
        public static double totalTimeTxTryAdd5_8 = 0;
        public static double totalTimePersitTxCount8_2_2_1 = 0;
        public static double totalTimePersitTxCount8_2_2_2 = 0;
        public static double totalTimePersitTxCount8_2_2_1_1 = 0;
        public static double totalTimePersitTxCount8_2_2_1_2 = 0;
        public static double totalTimePersitTxCount8_2_2_1_3 = 0;
        // Allow a reverified transaction to be rebroadcasted if it has been this many block times since last broadcast.
        private const int BlocksTillRebroadcastLowPriorityPoolTx = 30;
        private const int BlocksTillRebroadcastHighPriorityPoolTx = 10;
        private int RebroadcastMultiplierThreshold => 10_000;

        private static readonly double MaxMillisecondsToReverifyTx = (double)Blockchain.MillisecondsPerBlock / 3;

        // These two are not expected to be hit, they are just safegaurds.
        private static readonly double MaxMillisecondsToReverifyTxPerIdle = (double)Blockchain.MillisecondsPerBlock / 15;

        private readonly NeoSystem _system;

        //
        /// <summary>
        /// Guarantees consistency of the pool data structures.
        ///
        /// Note: The data structures are only modified from the `Blockchain` actor; so operations guaranteed to be
        ///       performed by the blockchain actor do not need to acquire the read lock; they only need the write
        ///       lock for write operations.
        /// </summary>
        private readonly ReaderWriterLockSlim _txRwLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

        /// <summary>
        /// Store all verified unsorted transactions' senders' fee currently in the pool.
        /// </summary>
        private readonly Dictionary<UInt160, BigInteger> _senderFee = new Dictionary<UInt160, BigInteger>();

        /// <summary>
        /// Store all verified unsorted transactions currently in the pool.
        /// </summary>
        private readonly Dictionary<UInt256, PoolItem> _unsortedTransactions = new Dictionary<UInt256, PoolItem>();
        /// <summary>
        /// Stores the verified sorted transactins currently in the pool.
        /// </summary>
        private readonly SortedSet<PoolItem> _sortedTransactions = new SortedSet<PoolItem>();

        /// <summary>
        /// Store the unverified transactions currently in the pool.
        ///
        /// Transactions in this data structure were valid in some prior block, but may no longer be valid.
        /// The top ones that could make it into the next block get verified and moved into the verified data structures
        /// (_unsortedTransactions, and _sortedTransactions) after each block.
        /// </summary>
        private readonly Dictionary<UInt256, PoolItem> _unverifiedTransactions = new Dictionary<UInt256, PoolItem>();
        private readonly SortedSet<PoolItem> _unverifiedSortedTransactions = new SortedSet<PoolItem>();

        // Internal methods to aid in unit testing
        internal int SortedTxCount => _sortedTransactions.Count;
        internal int UnverifiedSortedTxCount => _unverifiedSortedTransactions.Count;

        private int _maxTxPerBlock;
        private long _feePerByte;

        /// <summary>
        /// Total maximum capacity of transactions the pool can hold.
        /// </summary>
        public int Capacity { get; }

        /// <summary>
        /// Total count of transactions in the pool.
        /// </summary>
        public int Count
        {
            get
            {
                _txRwLock.EnterReadLock();
                try
                {
                    return _unsortedTransactions.Count + _unverifiedTransactions.Count;
                }
                finally
                {
                    _txRwLock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Total count of verified transactions in the pool.
        /// </summary>
        public int VerifiedCount => _unsortedTransactions.Count; // read of 32 bit type is atomic (no lock)

        public int UnVerifiedCount => _unverifiedTransactions.Count;

        public MemoryPool(NeoSystem system, int capacity)
        {
            _system = system;
            Capacity = capacity;
            ConsensusService.mempool = this;
        }

        internal bool LoadPolicy(Snapshot snapshot)
        {
            _maxTxPerBlock = (int)NativeContract.Policy.GetMaxTransactionsPerBlock(snapshot);
            long newFeePerByte = NativeContract.Policy.GetFeePerByte(snapshot);
            bool policyChanged = newFeePerByte > _feePerByte;
            _feePerByte = newFeePerByte;
            return policyChanged;
        }

        /// <summary>
        /// Determine whether the pool is holding this transaction and has at some point verified it.
        /// Note: The pool may not have verified it since the last block was persisted. To get only the
        ///       transactions that have been verified during this block use GetVerifiedTransactions()
        /// </summary>
        /// <param name="hash">the transaction hash</param>
        /// <returns>true if the MemoryPool contain the transaction</returns>
        public bool ContainsKey(UInt256 hash)
        {
            _txRwLock.EnterReadLock();
            try
            {
                return _unsortedTransactions.ContainsKey(hash) || _unverifiedTransactions.ContainsKey(hash);
            }
            finally
            {
                _txRwLock.ExitReadLock();
            }
        }

        public bool TryGetValue(UInt256 hash, out Transaction tx)
        {
            _txRwLock.EnterReadLock();
            try
            {
                bool ret = _unsortedTransactions.TryGetValue(hash, out PoolItem item)
                           || _unverifiedTransactions.TryGetValue(hash, out item);
                tx = ret ? item.Tx : null;
                return ret;
            }
            finally
            {
                _txRwLock.ExitReadLock();
            }
        }

        // Note: This isn't used in Fill during consensus, fill uses GetSortedVerifiedTransactions()
        public IEnumerator<Transaction> GetEnumerator()
        {
            _txRwLock.EnterReadLock();
            try
            {
                return _unsortedTransactions.Select(p => p.Value.Tx)
                    .Concat(_unverifiedTransactions.Select(p => p.Value.Tx))
                    .ToList()
                    .GetEnumerator();
            }
            finally
            {
                _txRwLock.ExitReadLock();
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public BigInteger GetSenderFee(UInt160 sender)
        {
            _txRwLock.EnterReadLock();
            try
            {
                if (_senderFee.ContainsKey(sender))
                    return _senderFee[sender];
                else
                    return BigInteger.Zero;
            }
            finally
            {
                _txRwLock.ExitReadLock();
            }
        }

        private void AddSenderFee(Transaction tx)
        {
            if (!_senderFee.ContainsKey(tx.Sender))
                _senderFee.Add(tx.Sender, tx.SystemFee + tx.NetworkFee);
            else
                _senderFee[tx.Sender] += tx.SystemFee + tx.NetworkFee;
        }

        private void RemoveSenderFee(Transaction tx)
        {
            _senderFee[tx.Sender] -= tx.SystemFee + tx.NetworkFee;
            if (_senderFee[tx.Sender] == 0) _senderFee.Remove(tx.Sender);
        }
    
        public IEnumerable<Transaction> GetVerifiedTransactions()
        {
            _txRwLock.EnterReadLock();
            try
            {
                return _unsortedTransactions.Select(p => p.Value.Tx).ToArray();
            }
            finally
            {
                _txRwLock.ExitReadLock();
            }
        }

        public void GetVerifiedAndUnverifiedTransactions(out IEnumerable<Transaction> verifiedTransactions,
            out IEnumerable<Transaction> unverifiedTransactions)
        {
            _txRwLock.EnterReadLock();
            try
            {
                verifiedTransactions = _sortedTransactions.Reverse().Select(p => p.Tx).ToArray();
                unverifiedTransactions = _unverifiedSortedTransactions.Reverse().Select(p => p.Tx).ToArray();
            }
            finally
            {
                _txRwLock.ExitReadLock();
            }
        }

        public IEnumerable<Transaction> GetSortedVerifiedTransactions()
        {
            _txRwLock.EnterReadLock();
            try
            {
                return _sortedTransactions.Reverse().Select(p => p.Tx).ToArray();
            }
            finally
            {
                _txRwLock.ExitReadLock();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private PoolItem GetLowestFeeTransaction(SortedSet<PoolItem> verifiedTxSorted,
            SortedSet<PoolItem> unverifiedTxSorted, out SortedSet<PoolItem> sortedPool)
        {
            PoolItem minItem = unverifiedTxSorted.Min;
            sortedPool = minItem != null ? unverifiedTxSorted : null;

            PoolItem verifiedMin = verifiedTxSorted.Min;
            if (verifiedMin == null) return minItem;

            if (minItem != null && verifiedMin.CompareTo(minItem) >= 0)
                return minItem;

            sortedPool = verifiedTxSorted;
            minItem = verifiedMin;

            return minItem;
        }

        private PoolItem GetLowestFeeTransaction(out Dictionary<UInt256, PoolItem> unsortedTxPool, out SortedSet<PoolItem> sortedPool)
        {
            sortedPool = null;

            try
            {
                return GetLowestFeeTransaction(_sortedTransactions, _unverifiedSortedTransactions, out sortedPool);
            }
            finally
            {
                unsortedTxPool = Object.ReferenceEquals(sortedPool, _unverifiedSortedTransactions)
                   ? _unverifiedTransactions : _unsortedTransactions;
            }
        }

        // Note: this must only be called from a single thread (the Blockchain actor)
        internal bool CanTransactionFitInPool(Transaction tx)
        {
            if (Count < Capacity) return true;

            return GetLowestFeeTransaction(out _, out _).CompareTo(tx) <= 0;
        }

        /// <summary>
        /// Adds an already verified transaction to the memory pool.
        ///
        /// Note: This must only be called from a single thread (the Blockchain actor). To add a transaction to the pool
        ///       tell the Blockchain actor about the transaction.
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        internal bool TryAdd(UInt256 hash, Transaction tx)
        {
            if (Blockchain.countSwitchBlockchain)
            {
                //phase5-1
                stopwatchTxTryAdd5_1.Start();
                var poolItem = new PoolItem(tx);
                stopwatchTxTryAdd5_1.Stop();
                totalTimeTxTryAdd5_1 += stopwatchTxTryAdd5_1.Elapsed.TotalSeconds;
                stopwatchTxTryAdd5_1.Reset();
                //phase5-2
                stopwatchTxTryAdd5_2.Start();
                if (_unsortedTransactions.ContainsKey(hash))
                {
                    stopwatchTxTryAdd5_2.Stop();
                    totalTimeTxTryAdd5_2 += stopwatchTxTryAdd5_2.Elapsed.TotalSeconds;
                    stopwatchTxTryAdd5_2.Reset();
                    return false;
                }
                List<Transaction> removedTransactions = null;
                _txRwLock.EnterWriteLock();
                stopwatchTxTryAdd5_2.Stop();
                totalTimeTxTryAdd5_2 += stopwatchTxTryAdd5_2.Elapsed.TotalSeconds;
                stopwatchTxTryAdd5_2.Reset();
                try
                {
                    //phase5-3
                    stopwatchTxTryAdd5_3.Start();
                    _unsortedTransactions.Add(hash, poolItem);
                    stopwatchTxTryAdd5_3.Stop();
                    totalTimeTxTryAdd5_3 += stopwatchTxTryAdd5_3.Elapsed.TotalSeconds;
                    stopwatchTxTryAdd5_3.Reset();
                    //phase5-4
                    stopwatchTxTryAdd5_4.Start();
                    AddSenderFee(tx);
                    stopwatchTxTryAdd5_4.Stop();
                    totalTimeTxTryAdd5_4 += stopwatchTxTryAdd5_4.Elapsed.TotalSeconds;
                    stopwatchTxTryAdd5_4.Reset();
                    //phase5-5
                    stopwatchTxTryAdd5_5.Start();
                    _sortedTransactions.Add(poolItem);
                    stopwatchTxTryAdd5_5.Stop();
                    totalTimeTxTryAdd5_5 += stopwatchTxTryAdd5_5.Elapsed.TotalSeconds;
                    stopwatchTxTryAdd5_5.Reset();
                    //phase5-6
                    stopwatchTxTryAdd5_6.Start();
                    if (Count > Capacity)
                        removedTransactions = RemoveOverCapacity();
                    stopwatchTxTryAdd5_6.Stop();
                    totalTimeTxTryAdd5_6 += stopwatchTxTryAdd5_6.Elapsed.TotalSeconds;
                    stopwatchTxTryAdd5_6.Reset();
                }
                finally
                {
                    _txRwLock.ExitWriteLock();
                }
                //phase5-7
                stopwatchTxTryAdd5_7.Start();
                foreach (IMemoryPoolTxObserverPlugin plugin in Plugin.TxObserverPlugins)
                {
                    plugin.TransactionAdded(poolItem.Tx);
                    if (removedTransactions != null)
                        plugin.TransactionsRemoved(MemoryPoolTxRemovalReason.CapacityExceeded, removedTransactions);
                }
                stopwatchTxTryAdd5_7.Stop();
                totalTimeTxTryAdd5_7 += stopwatchTxTryAdd5_7.Elapsed.TotalSeconds;
                stopwatchTxTryAdd5_7.Reset();
                //phase5-8
                stopwatchTxTryAdd5_8.Start();
                var ret = _unsortedTransactions.ContainsKey(hash);
                stopwatchTxTryAdd5_8.Stop();
                totalTimeTxTryAdd5_8 += stopwatchTxTryAdd5_8.Elapsed.TotalSeconds;
                stopwatchTxTryAdd5_8.Reset();
                return ret;
            }
            else
            {
                var poolItem = new PoolItem(tx);

                if (_unsortedTransactions.ContainsKey(hash)) return false;
                //phase5-2
                List<Transaction> removedTransactions = null;
                _txRwLock.EnterWriteLock();
                try
                {
                    //phase5-3
                    _unsortedTransactions.Add(hash, poolItem);
                    //phase5-4
                    AddSenderFee(tx);
                    //phase5-5
                    _sortedTransactions.Add(poolItem);
                    //phase5-6
                    if (Count > Capacity)
                        removedTransactions = RemoveOverCapacity();
                }
                finally
                {
                    _txRwLock.ExitWriteLock();
                }
                //phase5-7
                foreach (IMemoryPoolTxObserverPlugin plugin in Plugin.TxObserverPlugins)
                {
                    plugin.TransactionAdded(poolItem.Tx);
                    if (removedTransactions != null)
                        plugin.TransactionsRemoved(MemoryPoolTxRemovalReason.CapacityExceeded, removedTransactions);
                }
                //phase5-8
                var ret = _unsortedTransactions.ContainsKey(hash);
                return ret;
            }
        }

        private List<Transaction> RemoveOverCapacity()
        {
            List<Transaction> removedTransactions = new List<Transaction>();
            do
            {
                PoolItem minItem = GetLowestFeeTransaction(out var unsortedPool, out var sortedPool);

                unsortedPool.Remove(minItem.Tx.Hash);
                sortedPool.Remove(minItem);
                removedTransactions.Add(minItem.Tx);
            } while (Count > Capacity);

            return removedTransactions;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryRemoveVerified(UInt256 hash, out PoolItem item)
        {
            //8-2-2-1-1
            stopwatchPersistPhase8_2_2_1_1.Start();
            if (!_unsortedTransactions.TryGetValue(hash, out item))
            {
                stopwatchPersistPhase8_2_2_1_1.Stop();
                totalTimePersitTxCount8_2_2_1_1 += stopwatchPersistPhase8_2_2_1_1.Elapsed.TotalSeconds;
                stopwatchPersistPhase8_2_2_1_1.Reset();
                Console.WriteLine("!!!! Cannot get tx from pool");
                return false;
            }
            _unsortedTransactions.Remove(hash);
            stopwatchPersistPhase8_2_2_1_1.Stop();
            totalTimePersitTxCount8_2_2_1_1 += stopwatchPersistPhase8_2_2_1_1.Elapsed.TotalSeconds;
            stopwatchPersistPhase8_2_2_1_1.Reset();
            //8-2-2-1-2
            stopwatchPersistPhase8_2_2_1_2.Start();
            RemoveSenderFee(item.Tx);
            stopwatchPersistPhase8_2_2_1_2.Stop();
            totalTimePersitTxCount8_2_2_1_2 += stopwatchPersistPhase8_2_2_1_2.Elapsed.TotalSeconds;
            stopwatchPersistPhase8_2_2_1_2.Reset();
            //8-2-2-1-3
            stopwatchPersistPhase8_2_2_1_3.Start();
            _sortedTransactions.Remove(item);
            stopwatchPersistPhase8_2_2_1_3.Stop();
            totalTimePersitTxCount8_2_2_1_3 += stopwatchPersistPhase8_2_2_1_3.Elapsed.TotalSeconds;
            stopwatchPersistPhase8_2_2_1_3.Reset();
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryRemoveUnVerified(UInt256 hash, out PoolItem item)
        {
            if (!_unverifiedTransactions.TryGetValue(hash, out item))
                return false;

            _unverifiedTransactions.Remove(hash);
            _unverifiedSortedTransactions.Remove(item);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void InvalidateVerifiedTransactions()
        {
            foreach (PoolItem item in _sortedTransactions)
            {
                if (_unverifiedTransactions.TryAdd(item.Tx.Hash, item))
                    _unverifiedSortedTransactions.Add(item);
            }

            // Clear the verified transactions now, since they all must be reverified.
            _unsortedTransactions.Clear();
            _senderFee.Clear();
            _sortedTransactions.Clear();
        }

        // Note: this must only be called from a single thread (the Blockchain actor)


        internal void UpdatePoolForBlockPersisted(Block block, Snapshot snapshot)
        {
            if (Blockchain.countSwitchBlockchain)
            {
                //8-2-1
                Blockchain.stopwatchPersistBlock8_2_1.Start();
                bool policyChanged = LoadPolicy(snapshot);
                _txRwLock.EnterWriteLock();
                Blockchain.stopwatchPersistBlock8_2_1.Stop();
                Blockchain.totalTimePersistBlock8_2_1 += Blockchain.stopwatchPersistBlock8_2_1.Elapsed.TotalSeconds;
                Blockchain.stopwatchPersistBlock8_2_1.Reset();
                try
                {
                    //8-2-2
                    // First remove the transactions verified in the block.
                    Blockchain.stopwatchPersistBlock8_2_2.Start();
                    foreach (Transaction tx in block.Transactions)
                    {
                        //8-2-2-1
                        PersitTxCount8_2_2_1++;
                        stopwatchPersistPhase8_2_2_1.Start();
                        if (TryRemoveVerified(tx.Hash, out _))
                        {
                            stopwatchPersistPhase8_2_2_1.Stop();
                            totalTimePersitTxCount8_2_2_1 += stopwatchPersistPhase8_2_2_1.Elapsed.TotalSeconds;
                            stopwatchPersistPhase8_2_2_1.Reset();
                            continue;
                        }
                        stopwatchPersistPhase8_2_2_1.Stop();
                        totalTimePersitTxCount8_2_2_1 += stopwatchPersistPhase8_2_2_1.Elapsed.TotalSeconds;
                        stopwatchPersistPhase8_2_2_1.Reset();
                        //8-2-2-2
                        stopwatchPersistPhase8_2_2_2.Start();
                        TryRemoveUnVerified(tx.Hash, out _);
                        stopwatchPersistPhase8_2_2_2.Stop();
                        totalTimePersitTxCount8_2_2_2 += stopwatchPersistPhase8_2_2_2.Elapsed.TotalSeconds;
                        stopwatchPersistPhase8_2_2_2.Reset();
                        PersitTxCount8_2_2_2++;
                    }
                    Blockchain.stopwatchPersistBlock8_2_2.Stop();
                    Blockchain.totalTimePersistBlock8_2_2 += Blockchain.stopwatchPersistBlock8_2_2.Elapsed.TotalSeconds;
                    Blockchain.stopwatchPersistBlock8_2_2.Reset();
                    //8-2-3
                    // Add all the previously verified transactions back to the unverified transactions
                    Blockchain.stopwatchPersistBlock8_2_3.Start();
                    InvalidateVerifiedTransactions();
                    Blockchain.stopwatchPersistBlock8_2_3.Stop();
                    Blockchain.totalTimePersistBlock8_2_3 += Blockchain.stopwatchPersistBlock8_2_3.Elapsed.TotalSeconds;
                    Blockchain.stopwatchPersistBlock8_2_3.Reset();
                    //8-2-4
                    Blockchain.stopwatchPersistBlock8_2_4.Start();
                    if (policyChanged)
                    {
                        var tx = new List<Transaction>();
                        foreach (PoolItem item in _unverifiedSortedTransactions.Reverse())
                            if (item.Tx.FeePerByte >= _feePerByte)
                                tx.Add(item.Tx);

                        _unverifiedTransactions.Clear();
                        _unverifiedSortedTransactions.Clear();

                        if (tx.Count > 0)
                            _system.Blockchain.Tell(tx.ToArray(), ActorRefs.NoSender);
                    }
                }
                finally
                {
                    _txRwLock.ExitWriteLock();
                }
                // If we know about headers of future blocks, no point in verifying transactions from the unverified tx pool
                // until we get caught up.
                if (block.Index > 0 && block.Index < Blockchain.Singleton.HeaderHeight || policyChanged)
                    return;
                Blockchain.stopwatchPersistBlock8_2_4.Stop();
                Blockchain.totalTimePersistBlock8_2_4 += Blockchain.stopwatchPersistBlock8_2_4.Elapsed.TotalSeconds;
                Blockchain.stopwatchPersistBlock8_2_4.Reset();
                //8-2-5
                Blockchain.stopwatchPersistBlock8_2_5.Start();
                ReverifyTransactions(_sortedTransactions, _unverifiedSortedTransactions,
                _maxTxPerBlock, MaxMillisecondsToReverifyTx, snapshot);
                Blockchain.stopwatchPersistBlock8_2_5.Stop();
                Blockchain.totalTimePersistBlock8_2_5 += Blockchain.stopwatchPersistBlock8_2_5.Elapsed.TotalSeconds;
                Blockchain.stopwatchPersistBlock8_2_5.Reset();
            }
            else
            {
                //8-2-1
                bool policyChanged = LoadPolicy(snapshot);

                _txRwLock.EnterWriteLock();
                try
                {
                    //8-2-2
                    // First remove the transactions verified in the block.
                    foreach (Transaction tx in block.Transactions)
                    {
                        if (TryRemoveVerified(tx.Hash, out _)) continue;
                        TryRemoveUnVerified(tx.Hash, out _);
                    }
                    //8-2-3
                    // Add all the previously verified transactions back to the unverified transactions
                    InvalidateVerifiedTransactions();
                    //8-2-4
                    if (policyChanged)
                    {
                        var tx = new List<Transaction>();
                        foreach (PoolItem item in _unverifiedSortedTransactions.Reverse())
                            if (item.Tx.FeePerByte >= _feePerByte)
                                tx.Add(item.Tx);

                        _unverifiedTransactions.Clear();
                        _unverifiedSortedTransactions.Clear();

                        if (tx.Count > 0)
                            _system.Blockchain.Tell(tx.ToArray(), ActorRefs.NoSender);
                    }
                }
                finally
                {
                    _txRwLock.ExitWriteLock();
                }
                // If we know about headers of future blocks, no point in verifying transactions from the unverified tx pool
                // until we get caught up.
                if (block.Index > 0 && block.Index < Blockchain.Singleton.HeaderHeight || policyChanged)
                    return;


                ReverifyTransactions(_sortedTransactions, _unverifiedSortedTransactions,
                    _maxTxPerBlock, MaxMillisecondsToReverifyTx, snapshot);
            }
        }

        internal void InvalidateAllTransactions()
        {
            _txRwLock.EnterWriteLock();
            try
            {
                InvalidateVerifiedTransactions();
            }
            finally
            {
                _txRwLock.ExitWriteLock();
            }
        }

        private int ReverifyTransactions(SortedSet<PoolItem> verifiedSortedTxPool,
            SortedSet<PoolItem> unverifiedSortedTxPool, int count, double millisecondsTimeout, Snapshot snapshot)
        {
            DateTime reverifyCutOffTimeStamp = DateTime.UtcNow.AddMilliseconds(millisecondsTimeout);
            List<PoolItem> reverifiedItems = new List<PoolItem>(count);
            List<PoolItem> invalidItems = new List<PoolItem>();

            // Since unverifiedSortedTxPool is ordered in an ascending manner, we take from the end.
            foreach (PoolItem item in unverifiedSortedTxPool.Reverse().Take(count))
            {
                if (item.Tx.Reverify(snapshot, GetSenderFee(item.Tx.Sender)))
                    reverifiedItems.Add(item);
                else // Transaction no longer valid -- it will be removed from unverifiedTxPool.
                    invalidItems.Add(item);

                if (DateTime.UtcNow > reverifyCutOffTimeStamp) break;
            }

            _txRwLock.EnterWriteLock();
            try
            {
                int blocksTillRebroadcast = Object.ReferenceEquals(unverifiedSortedTxPool, _sortedTransactions)
                    ? BlocksTillRebroadcastHighPriorityPoolTx : BlocksTillRebroadcastLowPriorityPoolTx;

                if (Count > RebroadcastMultiplierThreshold)
                    blocksTillRebroadcast = blocksTillRebroadcast * Count / RebroadcastMultiplierThreshold;

                var rebroadcastCutOffTime = DateTime.UtcNow.AddMilliseconds(
                    -Blockchain.MillisecondsPerBlock * blocksTillRebroadcast);
                foreach (PoolItem item in reverifiedItems)
                {
                    if (_unsortedTransactions.TryAdd(item.Tx.Hash, item))
                    {
                        AddSenderFee(item.Tx);
                        verifiedSortedTxPool.Add(item);

                        if (item.LastBroadcastTimestamp < rebroadcastCutOffTime)
                        {
                            _system.LocalNode.Tell(new LocalNode.RelayDirectly { Inventory = item.Tx }, _system.Blockchain);
                            item.LastBroadcastTimestamp = DateTime.UtcNow;
                        }
                    }

                    _unverifiedTransactions.Remove(item.Tx.Hash);
                    unverifiedSortedTxPool.Remove(item);
                }

                foreach (PoolItem item in invalidItems)
                {
                    _unverifiedTransactions.Remove(item.Tx.Hash);
                    unverifiedSortedTxPool.Remove(item);
                }
            }
            finally
            {
                _txRwLock.ExitWriteLock();
            }

            var invalidTransactions = invalidItems.Select(p => p.Tx).ToArray();
            foreach (IMemoryPoolTxObserverPlugin plugin in Plugin.TxObserverPlugins)
                plugin.TransactionsRemoved(MemoryPoolTxRemovalReason.NoLongerValid, invalidTransactions);

            return reverifiedItems.Count;
        }

        /// <summary>
        /// Reverify up to a given maximum count of transactions. Verifies less at a time once the max that can be
        /// persisted per block has been reached.
        ///
        /// Note: this must only be called from a single thread (the Blockchain actor)
        /// </summary>
        /// <param name="maxToVerify">Max transactions to reverify, the value passed cam be >=1</param>
        /// <param name="snapshot">The snapshot to use for verifying.</param>
        /// <returns>true if more unsorted messages exist, otherwise false</returns>
        internal bool ReVerifyTopUnverifiedTransactionsIfNeeded(int maxToVerify, Snapshot snapshot)
        {
            if (Blockchain.Singleton.Height < Blockchain.Singleton.HeaderHeight)
                return false;

            if (_unverifiedSortedTransactions.Count > 0)
            {
                int verifyCount = _sortedTransactions.Count > _maxTxPerBlock ? 1 : maxToVerify;
                ReverifyTransactions(_sortedTransactions, _unverifiedSortedTransactions,
                    verifyCount, MaxMillisecondsToReverifyTxPerIdle, snapshot);
            }

            return _unverifiedTransactions.Count > 0;
        }
    }
}
