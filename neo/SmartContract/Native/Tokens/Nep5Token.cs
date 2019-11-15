#pragma warning disable IDE0060

using Neo.Ledger;
using Neo.Persistence;
using Neo.SmartContract.Manifest;
using Neo.VM;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using VMArray = Neo.VM.Types.Array;

namespace Neo.SmartContract.Native.Tokens
{
    public abstract class Nep5Token<TState> : NativeContract
        where TState : Nep5AccountState, new()
    {
        public override string[] SupportedStandards { get; } = { "NEP-5", "NEP-10" };
        public abstract string Name { get; }
        public abstract string Symbol { get; }
        public abstract byte Decimals { get; }
        public BigInteger Factor { get; }

        protected const byte Prefix_TotalSupply = 11;
        protected const byte Prefix_Account = 20;

        protected Nep5Token()
        {
            this.Factor = BigInteger.Pow(10, Decimals);

            Manifest.Features = ContractFeatures.HasStorage;

            var events = new List<ContractEventDescriptor>(Manifest.Abi.Events)
            {
                new ContractMethodDescriptor()
                {
                    Name = "Transfer",
                    Parameters = new ContractParameterDefinition[]
                    {
                        new ContractParameterDefinition()
                        {
                            Name = "from",
                            Type = ContractParameterType.Hash160
                        },
                        new ContractParameterDefinition()
                        {
                            Name = "to",
                            Type = ContractParameterType.Hash160
                        },
                        new ContractParameterDefinition()
                        {
                            Name = "amount",
                            Type = ContractParameterType.Integer
                        }
                    },
                    ReturnType = ContractParameterType.Boolean
                }
            };

            Manifest.Abi.Events = events.ToArray();
        }

        protected StorageKey CreateAccountKey(UInt160 account)
        {
            return CreateStorageKey(Prefix_Account, account);
        }

        internal protected virtual void Mint(ApplicationEngine engine, UInt160 account, BigInteger amount)
        {
            if (amount.Sign < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (amount.IsZero) return;
            StorageItem storage = engine.Snapshot.Storages.GetAndChange(CreateAccountKey(account), () => new StorageItem
            {
                Value = new TState().ToByteArray()
            });
            TState state = new TState();
            state.FromByteArray(storage.Value);
            OnBalanceChanging(engine, account, state, amount);
            state.Balance += amount;
            storage.Value = state.ToByteArray();
            storage = engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_TotalSupply), () => new StorageItem
            {
                Value = BigInteger.Zero.ToByteArray()
            });
            BigInteger totalSupply = new BigInteger(storage.Value);
            totalSupply += amount;
            storage.Value = totalSupply.ToByteArray();
            engine.SendNotification(Hash, new StackItem[] { "Transfer", StackItem.Null, account.ToArray(), amount });
        }

        internal protected virtual void Burn(ApplicationEngine engine, UInt160 account, BigInteger amount)
        {
            if (amount.Sign < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (amount.IsZero) return;
            StorageKey key = CreateAccountKey(account);
            StorageItem storage = engine.Snapshot.Storages.GetAndChange(key);
            TState state = new TState();
            state.FromByteArray(storage.Value);
            if (state.Balance < amount) throw new InvalidOperationException();
            OnBalanceChanging(engine, account, state, -amount);
            if (state.Balance == amount)
            {
                engine.Snapshot.Storages.Delete(key);
            }
            else
            {
                state.Balance -= amount;
                storage.Value = state.ToByteArray();
            }
            storage = engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_TotalSupply));
            BigInteger totalSupply = new BigInteger(storage.Value);
            totalSupply -= amount;
            storage.Value = totalSupply.ToByteArray();
            engine.SendNotification(Hash, new StackItem[] { "Transfer", account.ToArray(), StackItem.Null, amount });
        }

        [ContractMethod(0, ContractParameterType.String, Name = "name", SafeMethod = true)]
        protected StackItem NameMethod(ApplicationEngine engine, VMArray args)
        {
            return Name;
        }

        [ContractMethod(0, ContractParameterType.String, Name = "symbol", SafeMethod = true)]
        protected StackItem SymbolMethod(ApplicationEngine engine, VMArray args)
        {
            return Symbol;
        }

        [ContractMethod(0, ContractParameterType.Integer, Name = "decimals", SafeMethod = true)]
        protected StackItem DecimalsMethod(ApplicationEngine engine, VMArray args)
        {
            return (uint)Decimals;
        }

        [ContractMethod(0_01000000, ContractParameterType.Integer, SafeMethod = true)]
        protected StackItem TotalSupply(ApplicationEngine engine, VMArray args)
        {
            return TotalSupply(engine.Snapshot);
        }

        public virtual BigInteger TotalSupply(Snapshot snapshot)
        {
            StorageItem storage = snapshot.Storages.TryGet(CreateStorageKey(Prefix_TotalSupply));
            if (storage is null) return BigInteger.Zero;
            return new BigInteger(storage.Value);
        }

        [ContractMethod(0_01000000, ContractParameterType.Integer, ParameterTypes = new[] { ContractParameterType.Hash160 }, ParameterNames = new[] { "account" }, SafeMethod = true)]
        protected StackItem BalanceOf(ApplicationEngine engine, VMArray args)
        {
            return BalanceOf(engine.Snapshot, new UInt160(args[0].GetByteArray()));
        }

        public virtual BigInteger BalanceOf(Snapshot snapshot, UInt160 account)
        {
            StorageItem storage = snapshot.Storages.TryGet(CreateAccountKey(account));
            if (storage is null) return BigInteger.Zero;
            Nep5AccountState state = new Nep5AccountState(storage.Value);
            return state.Balance;
        }
        public  System.Diagnostics.Stopwatch stopwatchTransferToTal = new System.Diagnostics.Stopwatch();
        public static double timeSpanTransferToTal = 0;
        public static long countTransferToTal = 0;
        [ContractMethod(0_08000000, ContractParameterType.Boolean, ParameterTypes = new[] { ContractParameterType.Hash160, ContractParameterType.Hash160, ContractParameterType.Integer }, ParameterNames = new[] { "from", "to", "amount" })]
        protected StackItem Transfer(ApplicationEngine engine, VMArray args)
        {
            stopwatchTransferToTal.Start();
            UInt160 from = new UInt160(args[0].GetByteArray());
            UInt160 to = new UInt160(args[1].GetByteArray());
            BigInteger amount = args[2].GetBigInteger();
            bool result=Transfer(engine, from, to, amount);
           
            stopwatchTransferToTal.Stop();
            double timeSpan= stopwatchTransferToTal.Elapsed.TotalSeconds;
            stopwatchTransferToTal.Reset();

            double initialValue = 0;
            double computedValue = 0;
            Interlocked.Add(ref countTransferToTal, 1);
            do
            {
                initialValue = timeSpanTransferToTal;
                computedValue = initialValue + timeSpan;
            }
            while (initialValue != Interlocked.CompareExchange(ref timeSpanTransferToTal, computedValue, initialValue));

            return result;
        }

        public  System.Diagnostics.Stopwatch stopwatchTransferPhase1 = new System.Diagnostics.Stopwatch();
        public static double timeSpanTransferPhase1 = 0;
        public static long countTransferPhase1 = 0;

        public  System.Diagnostics.Stopwatch stopwatchTransferPhase2 = new System.Diagnostics.Stopwatch();
        public static double timeSpanTransferPhase2 = 0;
        public static long countTransferPhase2 = 0;

        public  System.Diagnostics.Stopwatch stopwatchTransferPhase3 = new System.Diagnostics.Stopwatch();
        public static double timeSpanTransferPhase3 = 0;
        public static long countTransferPhase3 = 0;

        public  System.Diagnostics.Stopwatch stopwatchTransferPhase4 = new System.Diagnostics.Stopwatch();
        public static double timeSpanTransferPhase4 = 0;
        public static long countTransferPhase4 = 0;
        protected virtual bool Transfer(ApplicationEngine engine, UInt160 from, UInt160 to, BigInteger amount)
        {
            //TransferPhase1
            stopwatchTransferPhase1.Start();
            if (amount.Sign < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (!from.Equals(engine.CallingScriptHash) && !InteropService.CheckWitness(engine, from))
                return false;
            stopwatchTransferPhase1.Stop();
            double timeSpan= stopwatchTransferPhase1.Elapsed.TotalSeconds;
            stopwatchTransferPhase1.Reset();

            double initialValue = 0;
            double computedValue = 0;
            Interlocked.Add(ref countTransferPhase1, 1);
            do
            {
                initialValue = timeSpanTransferPhase1;
                computedValue = initialValue + timeSpan;
            }
            while (initialValue != Interlocked.CompareExchange(ref timeSpanTransferPhase1, computedValue, initialValue));

            //TransferPhase2
            stopwatchTransferPhase2.Start();
            ContractState contract_to = engine.Snapshot.Contracts.TryGet(to);
            if (contract_to?.Payable == false) return false;
            stopwatchTransferPhase2.Stop();
            double timeSpan2= stopwatchTransferPhase2.Elapsed.TotalSeconds;
            stopwatchTransferPhase2.Reset();

            double initialValue2 = 0;
            double computedValue2 = 0;
            Interlocked.Add(ref countTransferPhase2, 1);
            do
            {
                initialValue2 = timeSpanTransferPhase2;
                computedValue2 = initialValue2 + timeSpan2;
            }
            while (initialValue2 != Interlocked.CompareExchange(ref timeSpanTransferPhase2, computedValue2, initialValue2));

            //TransferPhase3
            stopwatchTransferPhase3.Start();
            StorageKey key_from = CreateAccountKey(from);
            StorageItem storage_from = engine.Snapshot.Storages.TryGet(key_from);
            if (amount.IsZero)
            {
                if (storage_from != null)
                {
                    TState state_from = new TState();
                    state_from.FromByteArray(storage_from.Value);
                    OnBalanceChanging(engine, from, state_from, amount);
                }
            }
            else
            {
                if (storage_from is null) return false;
                TState state_from = new TState();
                state_from.FromByteArray(storage_from.Value);
                if (state_from.Balance < amount) return false;
                if (from.Equals(to))
                {
                    OnBalanceChanging(engine, from, state_from, BigInteger.Zero);
                }
                else
                {
                    OnBalanceChanging(engine, from, state_from, -amount);
                    if (state_from.Balance == amount)
                    {
                        engine.Snapshot.Storages.Delete(key_from);
                    }
                    else
                    {
                        state_from.Balance -= amount;
                        storage_from = engine.Snapshot.Storages.GetAndChange(key_from);
                        storage_from.Value = state_from.ToByteArray();
                    }
                    StorageKey key_to = CreateAccountKey(to);
                    StorageItem storage_to = engine.Snapshot.Storages.GetAndChange(key_to, () => new StorageItem
                    {
                        Value = new TState().ToByteArray()
                    });
                    TState state_to = new TState();
                    state_to.FromByteArray(storage_to.Value);
                    OnBalanceChanging(engine, to, state_to, amount);
                    state_to.Balance += amount;
                    storage_to.Value = state_to.ToByteArray();
                }
            }
            stopwatchTransferPhase3.Stop();
            double timeSpan3= stopwatchTransferPhase3.Elapsed.TotalSeconds;
            stopwatchTransferPhase3.Reset();

            double initialValue3 = 0;
            double computedValue3 = 0;
            Interlocked.Add(ref countTransferPhase3, 1);
            do
            {
                initialValue3 = timeSpanTransferPhase3;
                computedValue3 = initialValue3 + timeSpan3;
            }
            while (initialValue3 != Interlocked.CompareExchange(ref timeSpanTransferPhase3, computedValue3, initialValue3));


            //TransferPhase4
            stopwatchTransferPhase4.Start();
            engine.SendNotification(Hash, new StackItem[] { "Transfer", from.ToArray(), to.ToArray(), amount });
            stopwatchTransferPhase4.Stop();
            double timeSpan4= stopwatchTransferPhase4.Elapsed.TotalSeconds;
            stopwatchTransferPhase4.Reset();

            double initialValue4 = 0;
            double computedValue4 = 0;
            Interlocked.Add(ref countTransferPhase4, 1);
            do
            {
                initialValue4 = timeSpanTransferPhase4;
                computedValue4 = initialValue4 + timeSpan4;
            }
            while (initialValue4 != Interlocked.CompareExchange(ref timeSpanTransferPhase4, computedValue4, initialValue4));


            return true;
        }

        protected virtual void OnBalanceChanging(ApplicationEngine engine, UInt160 account, TState state, BigInteger amount)
        {
        }
    }
}
