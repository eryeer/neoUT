using System;
using System.Collections.Generic;
using System.Text;

namespace FuncTest.way2
{
    public class Contract1:NativeContract
    {
        public  Contract1()
        {
            Register(11, new NativeFunc1());
            Register(12, new NativeFunc2());
            Register(13, new NativeFunc3());
        }
        internal class NativeFunc1 : IFuncInvoke
        {
            public bool Invoke(string parameter)
            {
                return true;
            }
        }

        internal class NativeFunc2 : IFuncInvoke
        {
            public bool Invoke(string parameter)
            {
                return true;
            }
        }

        internal class NativeFunc3 : IFuncInvoke
        {
            public bool Invoke(string parameter)
            {
                return true;
            }
        }
    }


}
