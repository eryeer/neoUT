using System;
using System.Collections.Generic;
using System.Text;

namespace FuncTest.way2
{
    public class NativeContract
    {
       public static Dictionary<int, IFuncInvoke> methods = new Dictionary<int, IFuncInvoke>();

       public static Contract1 contract1=new Contract1();

       public static Contract2 contract2=new Contract2();

       public void Register(int name,IFuncInvoke func) {
            methods.Add(name, func);
       }
      
    }
}
