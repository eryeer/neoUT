using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.IO.Caching;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Neo.UnitTests
{
    [TestClass]
    public class UT_UInt256
    {
        [TestMethod]
        public void TestFIFOSet()
        {
            Stopwatch stopwatch = new Stopwatch();
            var fifoSet = new FIFOSet<UInt256>(150_000);
            var hashes = new UInt256[10000];
            for (int i = 0; i < 135_000; i++)
            {
                var transaction = TestUtils.CreateRandomHashTransaction();
                fifoSet.Add(transaction.Hash);
                if (i < 10000) hashes[i] = transaction.Hash;
            }
            Console.WriteLine($"fifoset size: {fifoSet.Size}");
            stopwatch.Start();
            fifoSet.ExceptWith(hashes);
            stopwatch.Stop();
            Console.WriteLine($"timespan:{stopwatch.Elapsed.TotalSeconds}");
            Console.WriteLine($"fifoset size: {fifoSet.Size}");
        }
    }
}
