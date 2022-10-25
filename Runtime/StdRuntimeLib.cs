/* 
 *   StdRuntimeLib: 标准运行时 
 *       包含运行时常用方法
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using NyaLang.Core;
using StdExprs = System.Linq.Expressions;
using StdExpr = System.Linq.Expressions.Expression;

namespace NyaLang.Runtime
{
    internal static class StdRuntimeLib
    {
        // 随机数生成器，随机种子取时间 Ticks 的低 32 位
        private static Random _random = new((int)(DateTime.UtcNow.Ticks & 0x000000ffffffff)); 






        // 【Array】 ----------------------------------------------------------------------------
        /// <summary>
        /// 拼接数组，其中第一个变量必须是数组类型；第二个变量若是数组，则拼接两者，否则，将第二个变量添加到末尾
        /// </summary>
        public static DynamicTypedef Concat(DynamicTypedef l, DynamicTypedef r)
        {
            if(l.Value is DynamicTypedef[] l_array )
            {
                if (r.Value is DynamicTypedef[] r_array)
                    return new DynamicTypedef(l_array.Concat(r_array));
                else 
                    return new DynamicTypedef(l_array.Concat(new DynamicTypedef[]{ r }));
            }

            // 左操作数必须是数组，否则警告
            NyaRuntimeWarning.Log($"In static method [$Concat]: the first argument should be array or tuple, but what have given is '{l.Value?.GetType()}'.");
            return new DynamicTypedef(null);
        }
        /// <summary>
        /// 获取字符串、数组、元组、字典的长度
        /// </summary>
        public static int Len(DynamicTypedef v)
        {
            if (v.Value == null) throw new Core.NyaRuntimeError("In static method [$len]: NULL argument error.");
            if (v.Value is Array array)
                return array.Length;
            if (v.Value is string str)
                return str.Length;
            if (!v.Value is Dictionary<string, DynamicTypedef> dict)
                return dict.Count;
            // 不是合理的取长度对象，抛出警告
            NyaRuntimeWarning.Log($"In static method [$len]: '{v.Value.GetType()}' is not a enumerable type.");
            return 1;
        }
        /// <summary>
        /// 转为字符串
        /// </summary>
        public static string ToString(DynamicTypedef v)
            => v.ToString();


        // 【Input & Output】 --------------------------------------------------------------------

        public static void DebugLog(DynamicTypedef v)
            => Console.Write(v.ToString());
        public static void DebugLogLine(DynamicTypedef v)
            => Console.WriteLine(v.ToString());
        public static string DebugRead()
        {
            int key = Console.Read();
            if (key < 0)
                return "";
            else
                return Convert.ToChar(key).ToString();
        }
        public static string DebugReadLine()
            => Console.ReadLine()?? "";
        public static int DebugReadKey()
            => (int)Console.ReadKey(true).Key; ///<see cref="ConsoleKey"/>        
        public static void DebugClear()
            => Console.Clear();



        // 【File】 ------------------------------------------------------------------------------
        /// <summary>
        /// 读取文件内容为字符串
        /// </summary>
        public static string FileReadAllText(DynamicTypedef path)
        {
            if(path.Value is string p)
            {
                string txt;
                try
                {
                    txt = File.ReadAllText(p);
                }
                catch (PathTooLongException) // 路径过长
                {
                    NyaRuntimeWarning.Log(
                        "In static method[$FileReadAllText]: the specified path or file name \"" +
                        (p.Length <= 32 ? p : p[..29] + " ...") +
                        "\" exceed the system-defined maximum length.");
                    goto errorDeal;
                }
                catch (DirectoryNotFoundException) // 路径不存在
                {
                    NyaRuntimeWarning.Log(
                        "In static method[$FileReadAllText]: the specified directory \"" +
                        (p.Length <= 32 ? p : p[..29] + " ...") +
                        "\" doesn't exist.");
                    goto errorDeal;
                }
                catch (FileNotFoundException) // 文件不存在
                {
                    NyaRuntimeWarning.Log(
                        "In static method[$FileReadAllText]: the specified file \"" +
                        (p.Length <= 32 ? p : p[..29] + " ...") +
                        "\" doesn't exist.");
                    goto errorDeal;
                }
                catch (UnauthorizedAccessException) // 权限不足
                {
                    NyaRuntimeWarning.Log(
                        "In static method[$FileReadAllText]: no permission to access the specified file or directory \"" +
                        (p.Length <= 32 ? p : p[..29] + " ...") + "\"");
                    goto errorDeal;
                }
                catch (NotSupportedException) // 不是可解析的路径
                {
                    NyaRuntimeWarning.Log(
                        "In static method[$FileReadAllText]: the path string \"" +
                        (p.Length <= 32 ? p : p[..29] + " ...") +
                        "\" is in an invalid format.");
                    goto errorDeal;
                }
                catch(Exception e) // 其它错误
                {
                    NyaRuntimeWarning.Log(
                        "In static method[$FileReadAllText]: where the path string is \"" +
                        (p.Length <= 32 ? p : p[..29] + " ...") + "\" occured exception:" +
                        e.Message);
                    goto errorDeal;
                }
                return txt;
            }                
            else // 不是string
                NyaRuntimeWarning.Log($"In static method[$FileReadAllText]: the type of '{path.Value?.GetType()}' is not string.");
            
            errorDeal:
            return "";
        }
        /// <summary>
        /// 读取文件内容为字符串数组，每条为一行
        /// </summary>
        public static DynamicTypedef FileReadAllLines(DynamicTypedef path)
        {
            if (path.Value is string p)
            {
                string[] txt;
                try
                {
                    txt = File.ReadAllLines(p);
                }
                catch (PathTooLongException) // 路径过长
                {
                    NyaRuntimeWarning.Log(
                        "In static method[$FileReadAllLines]: the specified path or file name \"" +
                        (p.Length <= 32 ? p : p[..29] + " ...") +
                        "\" exceed the system-defined maximum length.");
                    goto errorDeal;
                }
                catch (DirectoryNotFoundException) // 路径不存在
                {
                    NyaRuntimeWarning.Log(
                        "In static method[$FileReadAllLines]: the specified directory \"" +
                        (p.Length <= 32 ? p : p[..29] + " ...") +
                        "\" doesn't exist.");
                    goto errorDeal;
                }
                catch (FileNotFoundException) // 文件不存在
                {
                    NyaRuntimeWarning.Log(
                        "In static method[$FileReadAllLines]: the specified file \"" +
                        (p.Length <= 32 ? p : p[..29] + " ...") +
                        "\" doesn't exist.");
                    goto errorDeal;
                }
                catch (UnauthorizedAccessException) // 权限不足
                {
                    NyaRuntimeWarning.Log(
                        "In static method[$FileReadAllLines]: no permission to access the specified file or directory \"" +
                        (p.Length <= 32 ? p : p[..29] + " ...") + "\"");
                    goto errorDeal;
                }
                catch (NotSupportedException) // 不是可解析的路径
                {
                    NyaRuntimeWarning.Log(
                        "In static method[$FileReadAllLines]: the path string \"" +
                        (p.Length <= 32 ? p : p[..29] + " ...") +
                        "\" is in an invalid format.");
                    goto errorDeal;
                }
                catch (Exception e) // 其它错误
                {
                    NyaRuntimeWarning.Log(
                        "In static method[$FileReadAllLines]: where the path string is \"" +
                        (p.Length <= 32 ? p : p[..29] + " ...") + "\" occured exception:" +
                        e.Message);
                    goto errorDeal;
                }

                // 包装
                DynamicTypedef[] stringArray = new DynamicTypedef[txt.Length];
                for (int i = 0; i < txt.Length; i++)
                    stringArray[i].Value = txt[i];

                return new DynamicTypedef(stringArray);
            }
            else // 不是string
                NyaRuntimeWarning.Log($"In static method[$FileReadAllLines]: the type of '{path.Value?.GetType()}' is not string.");

            errorDeal:
            return new DynamicTypedef(null);
        }
        /// <summary>
        /// 写入字符串到文件
        /// </summary>
        public static void FileWriteAllText(DynamicTypedef path, DynamicTypedef text)
        {
            if(path.Value is string p)
            {
                if (text.Value is string s)
                {
                    try
                    {
                        File.WriteAllText(p, s);
                    }
                    catch (PathTooLongException) // 路径过长
                    {
                        NyaRuntimeWarning.Log(
                            "In static method[$FileWriteAllText]: the specified path or file name \"" +
                            (p.Length <= 32 ? p : p[..29] + " ...") +
                            "\" exceed the system-defined maximum length.");
                    }
                    catch (DirectoryNotFoundException) // 路径不存在
                    {
                        NyaRuntimeWarning.Log(
                            "In static method[$FileWriteAllText]: the specified directory \"" +
                            (p.Length <= 32 ? p : p[..29] + " ...") +
                            "\" doesn't exist.");
                    }
                    catch (UnauthorizedAccessException) // 权限不足
                    {
                        NyaRuntimeWarning.Log(
                            "In static method[$FileWriteAllText]: no permission to access the specified file or directory \"" +
                            (p.Length <= 32 ? p : p[..29] + " ...") + "\"");
                    }
                    catch (NotSupportedException) // 不是可解析的路径
                    {
                        NyaRuntimeWarning.Log(
                            "In static method[$FileWriteAllText]: the path string \"" +
                            (p.Length <= 32 ? p : p[..29] + " ...") +
                            "\" is in an invalid format.");
                    }
                    catch (Exception e) // 其它错误
                    {
                        NyaRuntimeWarning.Log(
                            "In static method[$FileWriteAllText]: where the path string is \"" +
                            (p.Length <= 32 ? p : p[..29] + " ...") + "\" occured exception:" +
                            e.Message);
                    }
                }
                else // 文本内容不对
                    NyaRuntimeWarning.Log($"In static method[$FileWriteAllText]: the argument 'text' should be string, but given '{path.Value?.GetType()}'.");
            }
            else // 地址类型不对
                NyaRuntimeWarning.Log($"In static method[$FileWriteAllText]: the argument 'path' should be string, but given '{path.Value?.GetType()}'.");
        }
        /// <summary>
        /// 写入字符串数组到文件，每条为一行
        /// </summary>
        public static void FileWriteAllLines(DynamicTypedef path, DynamicTypedef texts)
        {
            if (path.Value is string p)
            {
                if (texts.Value is DynamicTypedef[] textArray)
                {
                    try
                    {
                        string[] stringArray = new string[textArray.Length];

                        for(int i = 0; i < stringArray.Length; i++)
                        {
                            stringArray[i] = textArray[i].ToString();
                        }

                        File.WriteAllLines(p, stringArray);
                    }
                    catch (PathTooLongException) // 路径过长
                    {
                        NyaRuntimeWarning.Log(
                            "In static method[$FileWriteAllLines]: the specified path or file name \"" +
                            (p.Length <= 32 ? p : p[..29] + " ...") +
                            "\" exceed the system-defined maximum length.");
                    }
                    catch (DirectoryNotFoundException) // 路径不存在
                    {
                        NyaRuntimeWarning.Log(
                            "In static method[$FileWriteAllLines]: the specified directory \"" +
                            (p.Length <= 32 ? p : p[..29] + " ...") +
                            "\" doesn't exist.");
                    }
                    catch (UnauthorizedAccessException) // 权限不足
                    {
                        NyaRuntimeWarning.Log(
                            "In static method[$FileWriteAllLines]: no permission to access the specified file or directory \"" +
                            (p.Length <= 32 ? p : p[..29] + " ...") + "\"");
                    }
                    catch (NotSupportedException) // 不是可解析的路径
                    {
                        NyaRuntimeWarning.Log(
                            "In static method[$FileWriteAllLines]: the path string \"" +
                            (p.Length <= 32 ? p : p[..29] + " ...") +
                            "\" is in an invalid format.");
                    }
                    catch (Exception e) // 其它错误
                    {
                        NyaRuntimeWarning.Log(
                            "In static method[$FileWriteAllLines]: where the path string is \"" +
                            (p.Length <= 32 ? p : p[..29] + " ...") + "\" occured exception:" +
                            e.Message);
                    }
                }
                else // 文本内容不对
                    NyaRuntimeWarning.Log($"In static method[$FileWriteAllLines]: the argument 'texts' should be array, but given '{path.Value?.GetType()}'.");
            }
            else // 地址类型不对
                NyaRuntimeWarning.Log($"In static method[$FileWriteAllLines]: the argument 'path' should be string, but given '{path.Value?.GetType()}'.");
        }


        // 【Random】 ----------------------------------------------------------------------------
        /// <summary>
        /// 获取一个 [0, 1) 之间的随机数
        /// </summary>
        public static double Random()
            => _random.NextDouble();
        /// <summary>
        /// 获取一个 [0, maxVal) 之间的随机整数，如果出错，返回 -1
        /// </summary>
        public static int RandomInt(DynamicTypedef maxVal)
        {
            if (maxVal.Value is int v)
                return _random.Next(v);
            else if (maxVal.Value is double u)
                return _random.Next((int)u);
            else
                NyaRuntimeWarning.Log(
                    $"In static method[$RandomInt]: the argument 'maxVal' should be int our double, but given '{maxVal.Value?.GetType()}'.");
            return -1;
        }
        /// <summary>
        /// 获取一个 [minVal, maxVal) 之间的随机整数，如果出错，返回 -1
        /// </summary>
        public static int RandomRange(DynamicTypedef minVal, DynamicTypedef maxVal)
        {
            int min, max;
            if (minVal.Value is int)
                min = minVal.Value;
            else if (minVal.Value is double)
                min = (int)(minVal.Value);
            else
            {
                NyaRuntimeWarning.Log(
                    $"In static method[$RandomRange]: the argument 'minVal' should be int our double, but given '{minVal.Value?.GetType()}'.");
                goto errorDeal;
            }
            if (maxVal.Value is int)
                max = maxVal.Value;
            else if (maxVal.Value is double)
                max = (int)(maxVal.Value);
            else
            {
                NyaRuntimeWarning.Log(
                    $"In static method[$RandomRange]: the argument 'maxVal' should be int our double, but given '{maxVal.Value?.GetType()}'.");
                goto errorDeal;
            }
            return _random.Next(min, max);

            errorDeal:
            return -1;
        }


        // 【Time】 ------------------------------------------------------------------------------
        /// <summary>
        /// 获取 DateTime.Now.Ticks
        /// </summary>
        public static long Ticks()
            => DateTime.Now.Ticks;


        // 【Parser】 ----------------------------------------------------------------------------
        /// <summary>
        /// 调用解释器解释自己，
        /// 注意 NyaScript 的 Eval 函数会编译一个独立的委托，
        /// 因此不能调用宿主脚本里的数据。
        /// </summary>
        /// <remarks>——玩具而已。</remarks>
        public static DynamicTypedef Eval(DynamicTypedef v)
        {
            if (v.Value is string codeString)
            {
                try
                {
                    Scanner scanner = new Scanner(codeString);
                    Parser parser = new Parser(scanner.Execute());
                    Dictionary<string, StdExprs.ParameterExpression?> innerParam; // 储存脚本中的全局变量
                    var codeList = parser.Execute(out innerParam, null, true); // 执行严格语法解析
                    var codeBlock = StdExpr.Block(
                        (from rec in innerParam.Values.ToList() where rec != null select rec).ToList(), 
                        codeList);
                    // 返回值不是 dy 时，进行包装
                    if (codeBlock.Type != typeof(DynamicTypedef))
                    {
                        var tmpV = StdExpr.Variable(typeof(DynamicTypedef), "_evalDyTmp_");

                        // 如果是void类型，返回null
                        if (codeBlock.Type == typeof(void))
                            codeBlock = StdExpr.Block(
                                new StdExprs.ParameterExpression[] { tmpV },
                                codeBlock,
                                DynamicTypedef.SetValueExpr_Smart(tmpV, StdExpr.Constant(null)),
                                tmpV);
                        else
                            codeBlock = StdExpr.Block(
                                new StdExprs.ParameterExpression[] { tmpV },
                                DynamicTypedef.SetValueExpr_Smart(tmpV, codeBlock),
                                tmpV);

                    }
                    var lambdaFun = StdExpr.Lambda<Func<DynamicTypedef>>(codeBlock);
                    // 编译运行
                    return lambdaFun.Compile()();
                }
                catch (Parser.ParseError e)
                {
                    // 不可解析，失败
                    e.Report();
                    // 尽可能提示用户出错的字符串内容
                    NyaRuntimeWarning.Log(
                        "In static method [$Eval]: \"" +
                        (codeString.Length <= 32 ? codeString : codeString[..29] + " ...") +
                        "\" is not a parsable string.");
                    return new DynamicTypedef(null);
                }
            }
            else
            {
                // 不是字符串，抛出警告
                NyaRuntimeWarning.Log($"In static method [$Eval]: the type of '{v.Value?.GetType()}' is not string.");
                return new DynamicTypedef(null);
            }
        }


        // 【Environment】 -----------------------------------------------------------------------
        /// <summary>
        /// 手动抛出警告
        /// </summary>
        public static void Warning(DynamicTypedef v)
            => NyaRuntimeWarning.Log(v.ToString());


    }
}
