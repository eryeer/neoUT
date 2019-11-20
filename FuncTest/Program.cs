using Neo.SmartContract;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FuncTest
{
    class Program
    {
        delegate bool delegateCall(string engine);
        static int MaxTestCount = 10000000;
        static void Main()
        {
            // init to avoid initialization cost

            doOnceWork(null);
            var descriptor = (Func<string, bool>)doOnceWork;
            var del = new delegateCall(doOnceWork);
            descriptor.Invoke(null);
            del.Invoke(null);
            del(null);

            // Benchmarks

            var stopwatchFunc = Stopwatch.StartNew();
            for (var i = 0; i < MaxTestCount; i++)
            {
                descriptor.Invoke(null);
            }
            stopwatchFunc.Stop();
            Console.WriteLine("Invoke：" + stopwatchFunc.Elapsed.TotalMilliseconds);

            stopwatchFunc = Stopwatch.StartNew();
            for (var i = 0; i < MaxTestCount; i++)
            {
                del.Invoke(null);
            }
            stopwatchFunc.Stop();
            Console.WriteLine("Delegate-Invoke：" + stopwatchFunc.Elapsed.TotalMilliseconds);

            stopwatchFunc = Stopwatch.StartNew();
            for (var i = 0; i < MaxTestCount; i++)
            {
                del(null);
            }
            stopwatchFunc.Stop();
            Console.WriteLine("Delegate-Call：" + stopwatchFunc.Elapsed.TotalMilliseconds);

            stopwatchFunc = Stopwatch.StartNew();
            for (var i = 0; i < MaxTestCount; i++)
            {
                doOnceWork(null);
            }
            stopwatchFunc.Stop();
            Console.WriteLine("Normal:" + stopwatchFunc.Elapsed.TotalMilliseconds);
            stopwatchFunc.Reset();

            way2.InteropService service2 = new way2.InteropService();
            System.Diagnostics.Stopwatch stopwatchFunc2 = new System.Diagnostics.Stopwatch();
            stopwatchFunc2.Start();
            for (int i = 0; i < MaxTestCount; i++)
            {
                service2.Invoke("aaa", 11);
            }
            stopwatchFunc2.Stop();
            Console.WriteLine("Func timeSpan:" + stopwatchFunc2.Elapsed.TotalMilliseconds);
        }

        public static bool doOnceWork(string engine) => true;
    }
}
