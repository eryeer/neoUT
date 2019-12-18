using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Neo.IO.Caching
{
    internal class FIFOSet<T> : IEnumerable<T> where T : IEquatable<T>
    {
        private readonly int maxCapacity;
        private readonly int removeCount;
        private readonly OrderedDictionary dictionary;

        public int Size => dictionary.Count;

        public object StopWatch { get; private set; }

        public FIFOSet(int maxCapacity, decimal batchSize = 0.1m)
        {
            if (maxCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(maxCapacity));
            if (batchSize <= 0 || batchSize > 1) throw new ArgumentOutOfRangeException(nameof(batchSize));

            this.maxCapacity = maxCapacity;
            this.removeCount = Math.Max((int)(maxCapacity * batchSize), 1);
            this.dictionary = new OrderedDictionary(maxCapacity);
        }

        public bool Add(T item)
        {
            if (dictionary.Contains(item)) return false;

            if (dictionary.Count >= maxCapacity)
            {
                if (removeCount == maxCapacity)
                {
                    dictionary.Clear();
                }
                else
                {
                    for (int i = 0; i < removeCount; i++)
                        dictionary.RemoveAt(0);
                }
            }
            dictionary.Add(item, null);
            return true;
        }

        public bool Contains(T item)
        {
            return dictionary.Contains(item);
        }

        public void ExceptWith(IEnumerable<UInt256> hashes)
        {
            Console.WriteLine("start to Execpt fifoset");
            int count = 0;
            Stopwatch watch = new Stopwatch();
            watch.Start();
            foreach (var hash in hashes)
            {
                count++;
                dictionary.Remove(hash);
                if (count % 500 == 0)
                    Console.WriteLine($"Remove tx: count: {count}");
            }
            watch.Stop();
            Console.WriteLine($"FIFOSet Exceptwitch timspan: {watch.Elapsed.TotalSeconds}");
            watch.Reset();
        }

        public IEnumerator<T> GetEnumerator()
        {
            var entries = dictionary.Keys.Cast<T>().ToArray();
            foreach (var entry in entries) yield return entry;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
