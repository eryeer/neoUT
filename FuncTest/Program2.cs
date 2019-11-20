using Neo.SmartContract;
using System;
using System.Collections.Generic;

namespace FuncTest
{
    class Program2
    {
        //private static readonly Dictionary<int, InteropDescriptor1> methods = new Dictionary<int, InteropDescriptor1>();
        static void Main2(string[] args)
        {
            /*            Neo.SmartContract.InteropDescriptor1 descriptor = new InteropDescriptor1(0, doOnceWork);
                        for (int i = 0; i < 50; i++) {
                            methods.Add(i, descriptor);
                        }
                        System.Diagnostics.Stopwatch stopwatchFunc = new System.Diagnostics.Stopwatch();
                        stopwatchFunc.Start();
                        if (!methods.TryGetValue(0, out Neo.SmartContract.InteropDescriptor1 descriptor1))
                        {
                            return;
                        }
                        for (int i = 0; i < 10000; i++)
                        {
                            descriptor1.Handler(null);
                        }
                        stopwatchFunc.Stop();
                        Console.WriteLine("FunInv timeSpan：" + stopwatchFunc.Elapsed.TotalSeconds);
                        stopwatchFunc.Reset();
                        stopwatchFunc.Start();
                        int a = 50;
                        for (int i = 0; i < 10000; i++)
                        {
                            switch (a) {
                                case 0:
                                    doOnceWork(null);
                                    continue;
                                case 1:
                                    doOnceWork(null);
                                    continue;
                                case 2:
                                    doOnceWork(null);
                                    continue;
                                case 3:
                                    doOnceWork(null);
                                    continue;
                                case 4:
                                    doOnceWork(null);
                                    continue;
                                case 5:
                                    doOnceWork(null);
                                    continue;
                                case 6:
                                    doOnceWork(null);
                                    continue;
                                case 7:
                                    doOnceWork(null);
                                    continue;
                                case 8:
                                    doOnceWork(null);
                                    continue;
                                case 9:
                                    doOnceWork(null);
                                    continue;
                                case 10:
                                    doOnceWork(null);
                                    continue;
                                case 11:
                                    doOnceWork(null);
                                    continue;
                                case 12:
                                    doOnceWork(null);
                                    continue;
                                case 13:
                                    doOnceWork(null);
                                    continue;
                                case 14:
                                    doOnceWork(null);
                                    continue;
                                case 15:
                                    doOnceWork(null);
                                    continue;
                                case 16:
                                    doOnceWork(null);
                                    continue;
                                case 17:
                                    doOnceWork(null);
                                    continue;
                                case 18:
                                    doOnceWork(null);
                                    continue;
                                case 19:
                                    doOnceWork(null);
                                    continue;
                                case 20:
                                    doOnceWork(null);
                                    continue;
                                case 21:
                                    doOnceWork(null);
                                    continue;
                                case 22:
                                    doOnceWork(null);
                                    continue;
                                case 23:
                                    doOnceWork(null);
                                    continue;
                                case 24:
                                    doOnceWork(null);
                                    continue;
                                case 25:
                                    doOnceWork(null);
                                    continue;
                                case 26:
                                    doOnceWork(null);
                                    continue;
                                case 27:
                                    doOnceWork(null);
                                    continue;
                                case 28:
                                    doOnceWork(null);
                                    continue;
                                case 29:
                                    doOnceWork(null);
                                    continue;
                                case 30:
                                    doOnceWork(null);
                                    continue;
                                case 31:
                                    doOnceWork(null);
                                    continue;
                                case 32:
                                    doOnceWork(null);
                                    continue;
                                case 33:
                                    doOnceWork(null);
                                    continue;
                                case 34:
                                    doOnceWork(null);
                                    continue;
                                case 35:
                                    doOnceWork(null);
                                    continue;
                                case 36:
                                    doOnceWork(null);
                                    continue;
                                case 37:
                                    doOnceWork(null);
                                    continue;
                                case 38:
                                    doOnceWork(null);
                                    continue;
                                case 39:
                                    doOnceWork(null);
                                    continue;
                                case 40:
                                    doOnceWork(null);
                                    continue;
                                case 41:
                                    doOnceWork(null);
                                    continue;
                                case 42:
                                    doOnceWork(null);
                                    continue;
                                case 43:
                                    doOnceWork(null);
                                    continue;
                                case 44:
                                    doOnceWork(null);
                                    continue;
                                case 45:
                                    doOnceWork(null);
                                    continue;
                                case 46:
                                    doOnceWork(null);
                                    continue;
                                case 47:
                                    doOnceWork(null);
                                    continue;
                                case 48:
                                    doOnceWork(null);
                                    continue;
                                case 49:
                                    doOnceWork(null);
                                    continue;
                                case 50:
                                    doOnceWork(null);
                                    continue;
                                default:
                                    continue;
                            }
                        }
                        stopwatchFunc.Stop();
                        Console.WriteLine("Normal timeSpan" + stopwatchFunc.Elapsed.TotalSeconds);
                        stopwatchFunc.Reset();*/
            way1.InteropService service = new way1.InteropService();
            System.Diagnostics.Stopwatch stopwatchFunc = new System.Diagnostics.Stopwatch();
            stopwatchFunc.Start();
            for (int i = 0; i < 1000000; i++) {
                service.Invoke("aaa", 11);
            }
            stopwatchFunc.Stop();
            Console.WriteLine("Func timeSpan:" + stopwatchFunc.Elapsed.TotalSeconds);

            way2.InteropService service2 = new way2.InteropService();
            System.Diagnostics.Stopwatch stopwatchFunc2 = new System.Diagnostics.Stopwatch();
            stopwatchFunc2.Start();
            for (int i = 0; i < 1000000; i++)
            {
                service2.Invoke("aaa", 11);
            }
            stopwatchFunc2.Stop();
            Console.WriteLine("Func timeSpan:" + stopwatchFunc2.Elapsed.TotalSeconds);


        }
        public static bool doOnceWork(string engine)
        {
            return true;
        }
    }
}
