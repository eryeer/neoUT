
using FuncTest.way2;
using System;
using System.Collections.Generic;

namespace FuncTest.way2
{
    public class InteropService
    {
        internal class Func1 : IFuncInvoke
        {
            public bool Invoke(string parameter)
            {
                return true;
            }
        }

        internal class Func2 : IFuncInvoke
        {
            public bool Invoke(string parameter)
            {
                return true;
            }
        }

        internal class Func3 : IFuncInvoke
        {
            public bool Invoke(string parameter)
            {
                return true;
            }
        }


        public bool Invoke(string parameter, int method)
        {
            if (!methods.TryGetValue(method, out IFuncInvoke func))
            {
                return false;
            }
            var ret=func.Invoke(parameter);
            return ret;
        }

        public InteropService() {
            Register(1, new Func1());
            Register(2, new Func2());
            Register(3, new Func3());
            foreach (KeyValuePair<int, IFuncInvoke> item in NativeContract.methods) {
                Register(item.Key, item.Value);
            }
        }

        private static Dictionary<int, IFuncInvoke> methods = new Dictionary<int, IFuncInvoke>();

        private static void Register(int name, IFuncInvoke func)
        {
            methods.Add(name, func);
        }
    }
}
