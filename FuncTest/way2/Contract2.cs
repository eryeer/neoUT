using System;
using System.Collections.Generic;
using System.Text;

namespace FuncTest.way2
{
    public class Contract2:NativeContract
    {
        public Contract2()
        {
            Register(21, new NativeFunc1());
            Register(22, new NativeFunc2());
            Register(23, new NativeFunc3());
        }

        internal class NativeFunc1 : IFuncInvoke
        {
            public bool Invoke(string parameter)
            {
                Console.WriteLine("invoke Contract2 NativeFun1");
                return true;
            }
        }

        internal class NativeFunc2 : IFuncInvoke
        {
            public bool Invoke(string parameter)
            {
                Console.WriteLine("invoke Contract2 NativeFun2");
                return true;
            }
        }

        internal class NativeFunc3 : IFuncInvoke
        {
            public bool Invoke(string parameter)
            {
                Console.WriteLine("invoke Contract2 NativeFun3");
                return true;
            }
        }
    }
}
