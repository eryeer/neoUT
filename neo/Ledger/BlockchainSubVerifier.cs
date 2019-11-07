using Akka.Actor;
using Akka.Event;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;

namespace Neo.Ledger
{
    public class BlockchainSubVerifier : UntypedActor
    {
        public ILoggingAdapter Log { get; } = Context.GetLogger();
        private Snapshot currentSnapshot;

        public BlockchainSubVerifier(Snapshot snapshot)
        {
            currentSnapshot = snapshot;
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Transaction tx:
                    OnTransactionReceived(tx);
                    break;
            }
        }

        private void OnTransactionReceived(Transaction tx)
        {
            if (CheckWitnesses(tx))
            {
                Context.Parent.Tell(new ParallelVerifiedTransaction(tx), Sender);
            }
            else
            {
                Sender.Tell(RelayResultReason.Invalid);
            }
        }

        private bool CheckWitnesses(Transaction transaction)
        {
            int size = transaction.Size;
            if (size > Transaction.MaxTransactionSize) return false;
            long net_fee = transaction.NetworkFee - size * NativeContract.Policy.GetFeePerByte(currentSnapshot);
            if (net_fee < 0) return false;
            return transaction.VerifyWitnesses(currentSnapshot, net_fee);
        }

        public static Props Props(Snapshot snapshot) => Akka.Actor.Props.Create(() => new BlockchainSubVerifier(snapshot));
    }

}
