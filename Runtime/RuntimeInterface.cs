/* 
 *   RuntimeInterface: 宿主语言运行时接口
 *       
 *       提供 native 方法接口
 *       所有宿主方法都在此类中暴露给解析器
 *       
 *       【native 方法规范】
 *          所有暴露给该接口的方法都应当：
 *            1. 是 static 方法；
 *            2. 只接受 DynamicTypedef 类型参数；
 *            3. 返回值应当为
 *                 void
 *                 int
 *                 double
 *                 string
 *                 DynamicTypedef
 *                 DynamicTypedef[]                                     // 元组类型
 *                 DynamicTypedef( Dictionary<string, DynamicTypedef> ) // 容器类型
 *               中的一种，
 *               如果是上述类型之外的，使用 DynamicTypedef 包装它，
 *               但是 DynamicTypedef 不能包装 DynamicTypedef；
 *            4. 接上条，如果返回的数组或字典不是上述 元组类型 或 容器类型
 *               （如：int[]，Dictionary<string, DynamicTypedef>），
 *               则不保证 NyaScript 能够正确的索引它。
 *       
 *       P.S. 在调用native方法时，是可以命中native代码里的断点的
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using StdExprs = System.Linq.Expressions;
using StdExpr = System.Linq.Expressions.Expression;


namespace NyaLang.Runtime
{
    /// <summary>
    /// native 方法接口，所有宿主方法都在此类中暴露给解析器
    /// </summary>
    internal static class RuntimeInterface
    {
        /// <summary>
        /// 通过字符串方法名搜索暴露给分析器的 native 方法
        /// </summary>
        /// <remarks>
        /// 只有通过该方法能获得 MethodInfo 的方法会暴露给分析器
        /// </remarks>
        /// <param name="methodName">native 方法名</param>
        /// <returns>如果方法存在，返回对应方法；否则返回 null</returns>
        /// <see cref="Core.Parser.static_invoke"/> 实现方法
        public static MethodInfo? GetMethodViaName(string methodName)
        {
            /* 【note】所有暴露出来的方法都应该为 static，且只接受 DynamicTypedef 类型参数 */

            // 枚举几个方法库，查找是否有匹配的方法
            return 
                typeof(StdRuntimeLib).GetMethod(methodName)??
                typeof(InteractRedirectInterface).GetMethod(methodName)??
                null;
        }
    }
}
