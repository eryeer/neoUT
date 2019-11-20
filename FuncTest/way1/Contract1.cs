using System;
using System.Collections.Generic;
using System.Text;

namespace FuncTest.way1
{
    public class Contract1:NativeContract
    {
        private static Contract1 singleInstance=new Contract1();
        private Contract1() { }

        public static Contract1 getInstance()
        {
            return singleInstance;
        }
        
        public override bool Invoke(string parameter, int method)
        {
            var ret = false;
            switch (method)
            {
                case 11:
                    ret = Func1(parameter);
                    break;
                case 12:
                    ret = Func2(parameter);
                    break;
                case 13:
                    ret = Func3(parameter);
                    break;
                default:
                    break;
            }
            return ret;
        }

        public bool Func1(string parameter)
        {
            return true;
        }
        public bool Func2(string parameter)
        {
            return true;
        }
        public bool Func3(string parameter)
        {
            return true;
        }
    }


}
