/* 
 *   NyaRuntimeWarning: 运行时警告 
 *       如果在运行时抛出 Exception 会导致进程终止
 *       但有的时候我们希望程序继续而只抛出警告
 *       该类定义了运行时警告的执行方法
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NyaLang.Runtime
{
    internal static class NyaRuntimeWarning
    {
        public static void Log(string msg)
        {
            Console.WriteLine("[ Runtime Warning ]: " + msg);
        }
    }
}
