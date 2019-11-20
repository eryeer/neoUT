using FuncTest.way1;
using System;
using System.Collections.Generic;
using System.Text;

namespace FuncTest.way1
{
    public abstract class NativeContract : IFuncInvoke
    {
        public virtual bool Invoke(string parameter, int method)
        {
            throw new NotImplementedException();
        }
    }
}
