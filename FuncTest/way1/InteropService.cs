using System;
using System.Collections.Generic;

namespace FuncTest.way1
{
    public class InteropService
    {
        Contract1 conttract1 = Contract1.getInstance();
        public bool Func1(string parameter)
        {
            Console.WriteLine("invoke InteropService Fun1");
            return true;
        }
        public bool Func2(string parameter)
        {
            Console.WriteLine("invoke InteropService Fun2");
            return true;
        }
        public bool Func3(string parameter) {
            Console.WriteLine("invoke InteropService Fun3");
            return true;
        }

        public bool Invoke(string parameter, int method)
        {
            var ret = false;
            switch (method) {
                case 1:
                    ret=Func1(parameter);
                    break;
                case 2:
                    ret = Func2(parameter);
                    break;
                case 3:
                    ret = Func3(parameter);
                    break;
                case 11:
                    ret = conttract1.Invoke(parameter,method);
                    break;
                case 12:
                    ret = conttract1.Invoke(parameter, method);
                    break;
                case 13:
                    ret = conttract1.Invoke(parameter, method);
                    break;
                default:

                    break;
            }
            return ret;
        }
    }
}
