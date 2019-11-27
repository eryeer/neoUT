using Neo.Network.P2P.Payloads;
using System;

namespace Neo.Ledger
{
    /// <summary>
    /// Represents an item in the Memory Pool.
    ///
    //  Note: PoolItem objects don't consider transaction priority (low or high) in their compare CompareTo method.
    ///       This is because items of differing priority are never added to the same sorted set in MemoryPool.
    /// </summary>
    internal class PoolItem : IComparable<PoolItem>
    {
        /// <summary>
        /// Internal transaction for PoolItem
        /// </summary>
        public readonly Transaction Tx;

        /// <summary>
        /// Timestamp when transaction was stored on PoolItem
        /// </summary>
        public readonly DateTime Timestamp;

        /// <summary>
        /// Timestamp when this transaction was last broadcast to other nodes
        /// </summary>
        public DateTime LastBroadcastTimestamp;

        public long TxFeePerByte;

        internal PoolItem(Transaction tx)
        {
            Tx = tx;
            Timestamp = TimeProvider.Current.UtcNow;
            LastBroadcastTimestamp = Timestamp;
            TxFeePerByte = tx.FeePerByte;
        }

        public int CompareTo(Transaction otherTx)
        {
            if (otherTx == null) return 1;
            // Fees sorted ascending
            int ret = Tx.FeePerByte.CompareTo(otherTx.FeePerByte);
            if (ret != 0) return ret;
            ret = Tx.NetworkFee.CompareTo(otherTx.NetworkFee);
            if (ret != 0) return ret;
            //Transaction hash sorted descending
            return otherTx.Hash.CompareTo(Tx.Hash);
        }

        public int CompareTo(PoolItem otherItem)
        {
            if (otherItem == null) return 1;
            if (otherItem.Tx == null) return 1;
            int ret = TxFeePerByte.CompareTo(otherItem.TxFeePerByte);
            if (ret != 0) return ret;
            ret = Tx.NetworkFee.CompareTo(otherItem.Tx.NetworkFee);
            if (ret != 0) return ret;
            return otherItem.Tx.Hash.CompareTo(Tx.Hash);
        }
    }
}
