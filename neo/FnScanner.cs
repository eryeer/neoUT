using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

public class FnScanner
{
    public FnScanner()
    {
    }
    public static void Main()
    {
        Console.WriteLine("begin");
        Type[] types =  Assembly.GetExecutingAssembly().GetTypes();
        DateTime now = DateTime.Now;
       

        string path = @"D:\WorkDoc\a_work\UT\UT_functions_" + now.ToString("yyyy-MM-dd")+".txt";
        using (var tw = new StreamWriter(path, true))
        {
     
        for (int i = 0; i < types.Length; i++)
        {      
            Type type = types[i];
            try
            {
                if (!type.Name.StartsWith("<") && type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Length!=0)
                {
                    Console.WriteLine("--------"+ type.FullName + ".cs--------------------");
                    foreach (MethodInfo info in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {

                        if (info.DeclaringType == type && !type.FullName.Equals("FnScanner"))
                        {
                           tw.Write(type.FullName);
                           tw.Write("\t");
                      
                           Console.Write(info.Name + "(" );
                            tw.Write(info.Name + "(");
                            ParameterInfo[] parameterInfos = info.GetParameters();
                            for (int j = 0; j < parameterInfos.Length; j++)
                            {
                                Console.Write(parameterInfos[j].ParameterType.Name);
                                    tw.Write(parameterInfos[j].ParameterType.Name);
                                if (j+1 < parameterInfos.Length)
                                {
                                    Console.Write(",");
                                        tw.Write(",");
                                }
                            }                  
                            Console.WriteLine(")");
                                tw.WriteLine(")");
                            }
                    }
                }      
            }
            catch (System.IO.FileNotFoundException e)
            {
            }
            catch (NullReferenceException e)
            {
            }
        }    
           tw.Close();
        }
        Console.WriteLine("------Finish---------");
        Console.ReadKey();
    }
}
