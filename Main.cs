using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

// 标准表达式库（C# lambda表达式等方法都由此生成）

using StdExprs = System.Linq.Expressions;
using StdExpr = System.Linq.Expressions.Expression;
using NyaLang.Core;

/*  如何优化解释器：
 *      1. 使用 System.Linq 库代替 stmt 和 expr 类型
 *              见 IronPython
 *      2. 使用泛型 T func<T>(T para) 代替 object? func(object? para)
 *              泛型转化是在编译阶段进行的，而对象封装/拆封是在运行时进行的
 *      3. 该 unsafe 的时候 unsafe 
 *              跳过 gc，进入 c++ 模式，stackalloc 等效于 cpp 的 malloc
 *              【官网注释】：建议尽可能使用 Span<T> 或 ReadOnlySpan<T> 类型来处理堆栈中分配的内存
 *              当指针对象被释放时，stackalloc 的内存会被 GC
 *              P.S. 真正的 C++ 模式，GC 管不到：
 *                  System.Runtime.InteropServices.Marshal.AllocHGlobal
 *                  System.Runtime.InteropServices.Marshal.FreeHGlobal
 *              P.S.2. C# 纯内存对象：Span
 *      4. 使用 struct 替代小的 class
 *              struct 储存在栈中，class 储存在堆中；
 *              同样 unsafe 里的 stackalloc 也可以避免程序将数据存到堆里
 */


class OptimizedInterpreter
{

    static void Main(string[] args)
    {
        println("<< DLR - NyaLang / Core version 1.0.2 / Runtime Package 0.3 >>");
        if (args.Length == 1)
        {
            runFile(args[0]);
        }
        else
        {
            runFile("C:\\Users\\34479\\Desktop\\NyaLang\\ExampleScript\\SimpleExample.js"); // Unicode
        }

    }

    private static void runFile(string path)
    {
        string chars = File.ReadAllText(path);
        run(chars);
        // 不要像下面这样写，会出现编码问题而无法正常编译代码
        //byte[] bytes = File.ReadAllBytes(path);
        //run(Encoding.Unicode.GetString(bytes));
    }

    private static void run(string source)
    {
        Scanner scanner = new Scanner(source);

        println("Scanning...");
        List<Token> tokens = scanner.Execute();

        println("Parsing...");
        Parser parser = new Parser(tokens);
        Func<int>? ScriptLam = null;
        try
        {
            Dictionary<string, StdExprs.ParameterExpression?> innerParam; // 储存脚本中的全局变量
            List<StdExpr> statements = parser.Execute(out innerParam);

            statements.Add(StdExpr.Constant(0)); // 经典 return 0 （编译到lambda必须指定一个返回值类型）

            // 将全局变量命名空间传进去
            var codeBlock = StdExpr.Block(
                (from rec in innerParam.Values.ToList() where rec != null select rec).ToList(),
                statements);
            var lbd = StdExpr.Lambda<Func<int>>(codeBlock);

            println("Compiling...");
            ScriptLam = lbd.Compile();

            println("Done.");
        }
        catch (Parser.ParseError e)
        {
            e.Report();
            println("Canceled.");
        }

        int ScriptReturn = ScriptLam != null? ScriptLam() : 1;

        println($"Script exit with code {ScriptReturn}.");
        
    }



    public static void printf(dynamic? s)
    {
        Console.Write($"{s}");
    }
    public static void println(dynamic? s)
    {
        Console.WriteLine($"{s}");
    }

}
