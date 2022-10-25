/* 
 *   DynamicRuntime：动态运行时
 *   
 *      定义了动态变量类型 DynamicTypedef，以及变量的最小运行时方法
 *      与 .Runtime namespace 下的运行时方法库不同，该文件主要解决的是动态类型的问题，
 *      .Runtime 对于脚本运行不是必要的，而 .Core.DynamicRuntime 则必须包含
 *      
 *      又：DynamicRuntime 同时相当于是 NyaScript 和 .Net DLR 的交互环境
 *   
 */
//#define EnableRuntimeDiagnosis
//#undef EnableRuntimeDiagnosis
/* 
 *   EnableRuntimeDiagnosis：
 *      启用该宏会将 DynamicTypedef 的部分方法实现转为纯运行时，
 *      从而可以精确反馈部分运行时错误如函数参数不符合、不合法 Invoke、下标越界等
 *      【note】目前只有函数调用能够使用运行时诊断，其它方法由于出现属性只读问题暂时弃用
 */

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using StdExprs = System.Linq.Expressions;
using StdExpr = System.Linq.Expressions.Expression;

namespace NyaLang.Core
{
    /* 
     *   问题是什么 ？
     *      使用 StdExpr 建立表达式树是强类型的，并且无法自动装箱和拆箱
     *      包括 StdExpr.Assign( object, double ) 都是不允许的
     *      因此我们需要一种类型满足其类型转换   
     *      
     *   什么是 dynamic ？
     *      一种被编译器优化的 object 类型，
     *      可以节省装箱/拆箱开销
     *      
     *   铁Py是怎么做的 ？
     *      Ast.Assign(variable, AstUtils.Convert(right, variable.Type));
     *      
     *   关于 lambda 函数闭包：
     *      lambda 函数在 Parse 时直接编译的话不能捕获外部变量，
     *      但是如果以 StdExpr<Func> 形式给变量赋值，然后与整个代码同时编译，
     *      则表达式树会自动实现捕获和闭包
     */

    /// <summary>
    /// 动态类型支持
    /// </summary>
    public struct DynamicTypedef
    {
        public dynamic? Value; 
        // 必须要设置为字段而非属性，只有字段能够被直接访问

        public DynamicTypedef()
        {
            Value = null; 
        }
        public DynamicTypedef(dynamic? value)
        {
            Value = value;
        }

        // 【编译时】 --------------------------------------------------------------------------
        //      note：所有编译时方法都应当设置为静态类型

        /// <summary>
        /// 生成一条取值语句，获取 DynamicTypedef 结构体的 Value 字段
        /// </summary>
        /// <param name="dy">类型为 DynamicTypedef 的表达式</param>
        /// <returns>等价于 dy.Value 的表达式</returns>
        /// <exception cref="Parser.ParseError">传入的表达式类型不为 DynamicTypedef</exception>
        public static StdExprs.MemberExpression GetValueExpr(StdExpr dy)
        {
            if(dy.Type != typeof(DynamicTypedef)) // 类型检查
            {
                throw new Parser.ParseError(
                    "[GetValueExpr] method can only work when the param type is DynamicTypedef, " +
                    "check the AST generator");
            }
            var valueInfo = 
                typeof(DynamicTypedef).GetField(nameof(Value))?? 
                throw new Exception("不可能报错的这里，纯粹避免编译警告");

            return StdExpr.Field(dy, valueInfo);
        }
        /// <summary>
        /// 生成一条赋值语句，修改 DynamicTypedef 结构体的 Value 字段
        /// </summary>
        /// <remarks>
        /// 需注意如果 value 同为 DynamicTypedef 则会出现结构嵌套的问题，建议使用 SetValueExpr_Smart
        /// </remarks>
        /// <param name="dy">类型为 DynamicTypedef 的表达式</param>
        /// <param name="value">期望修改的值表达式，【不应当为 DynamicTypedef 类型】</param>
        /// <returns>等价于 dy.Value = (object) value 的表达式</returns>
        /// <exception cref="Parser.ParseError">传入的表达式 dy 类型不为 DynamicTypedef</exception>
        public static StdExpr SetValueExpr(StdExpr dy, StdExpr value)
        {
            if (dy.Type != typeof(DynamicTypedef)) // 类型检查
            {
                throw new Parser.ParseError(
                    "[SetValueExpr] method can only work when the param type is DynamicTypedef, " +
                    "check the AST generator");
            }
            var valueInfo =                                      //获取字段
                typeof(DynamicTypedef).GetField(nameof(Value)) ??
                throw new Exception("不可能报错的这里，纯粹避免编译警告");

            // 在表达式树中，dynamic 类型被视为 object，因此需要类型转换才能赋值
            return StdExpr.Assign(StdExpr.Field(dy, valueInfo), StdExpr.Convert(value, typeof(object)));
        }
        /// <summary>
        /// 生成一条赋值语句，给 DynamicTypedef 变量赋值
        /// </summary>
        /// <remarks>
        /// 该函数与 SetValueExpr 的区别见函数内部注释；
        /// 同时该函数也具有 SetValueExpr_Smart 转换 value 类型的功能
        /// </remarks>
        /// <param name="dy">类型为 DynamicTypedef 的表达式</param>
        /// <param name="value">期望修改的值表达式</param>
        /// <returns>如果 value 不为 DynamicTypedef，返回" dy = ConvertFrom(value) "；
        ///          否则，返回" dy = value "</returns>
        /// <exception cref="Parser.ParseError">传入的表达式 dy 类型不为 DynamicTypedef</exception>
        public static StdExpr SetValueExpr_Strong(StdExpr dy, StdExpr value)
        {
            /* 为什么要有强赋值语句
             *     SetValueExpr 方法等效于 Dynamic.Value = anyvalue，
             *     对字段直接赋值来减小开销，
             *     但并非使用地方都可以对字段直接赋值：
             *     
             *     想象一下有这样一个对象：
             *         struct atom{
             *             public int Value;
             *         }
             *         class boxed_atom{
             *             public atom ProtectedValue { get; set; }
             *         }
             *     现在我们需要修改 boxed_atom.ProtectedValue.Value 的值
             *     然而我们发现 boxed_atom.ProtectedValue.Value = anyvalue 之后，
             *     boxed_atom.ProtectedValue.Value 的值并没有改变
             *     
             *     这是因为
             *         1. atom 是一个 struct 对象，它像值类型一样传递值而非引用
             *         2. ProtectedValue 是一个属性，当我们调用 boxed_atom.ProtectedValue 时，
             *            实际上是调用了其 get 方法，得到了 ProtectedValue 的一个副本
             *     也就是说，boxed_atom.ProtectedValue.Value = anyvalue 应化为
             *              (boxed_atom.ProtectedValue).Value = anyvalue
             *     其中 (boxed_atom.ProtectedValue) 是 ProtectedValue 的副本，
             *     当我们修改其 Value 字段时，修改的是副本的字段，
             *     储存在 boxed_atom 中的内容并没有改变。
             *     
             *     强赋值语句相当于
             *         atom tmp;
             *         tmp.Value = anyvalue;
             *         boxed_atom.ProtectedValue = tmp;
             *     此时 ProtectedValue 后面直接跟 "="，调用其 set 方法，
             *     就能够修改属性内结构体的字段了（即生成一个需要的结构体替换他）
             *     
             */
            if (dy.Type != typeof(DynamicTypedef)) // 类型检查
            {
                throw new Parser.ParseError(
                    "[SetValueExpr] method can only work when the param type is DynamicTypedef, " +
                    "check the AST generator");
            }
            // 临时变量用于整体替换目标对象
            var tmpDy = StdExpr.Variable(typeof(DynamicTypedef), "_StrongSetTmp_");
            // 该方法在替换字典索引时尤其要用到
            return StdExpr.Block(
                new StdExprs.ParameterExpression[] { tmpDy },
                SetValueExpr_Smart(tmpDy, value),
                StdExpr.Assign(dy, tmpDy));
        }
        /// <summary>
        /// 生成一条赋值语句，根据 dy 和 value 的类型生成对应的语句
        /// </summary>
        /// <param name="dy">期望被修改的表达式</param>
        /// <param name="value">期望修改的值表达式</param>
        /// <returns>如果 dy 为 DynamicTypedef，且 value 不为 DynamicTypedef，返回" dy.Value = (object) value "；
        ///          如果 value 为 DynamicTypedef，且 dy 不为 DynamicTypedef，返回" dy = (dy.Type) value.Value "；
        ///          否则，返回" dy = value "</returns>       
        public static StdExpr SetValueExpr_Smart(StdExpr dy, StdExpr value)
            => dy.Type == typeof(DynamicTypedef) ?
                    (value.Type == typeof(DynamicTypedef) ?
                        StdExpr.Assign(dy, value) :
                        SetValueExpr(dy, value)) :
                    (value.Type == typeof(DynamicTypedef) ?
                        StdExpr.Assign(dy, StdExpr.Convert(GetValueExpr(value), dy.Type)) :
                        StdExpr.Assign(dy, value));
        /// <summary>
        /// 生成一条取值语句，根据 dy 和 value 的类型生成对应的语句
        /// </summary>
        /// <param name="dy">希望取值的表达式</param>
        /// <param name="t">当 dy 是 DynamicTypedef 时，期望的返回值类型，为 null 代表不进行转换，
        ///                 注意对于不同类型的 dy 和 t，类型转换方式也不同，请参考返回值列表</param>
        /// <returns>
        ///     当 dy 的类型及 t 的值分别为下列组合( dy.T , t )时，有其返回值：
        ///         ( DynamicTypedef, DynamicTypedef ) => " dy "；
        ///         (     _Any_     , DynamicTypedef ) => " dy "；
        ///         ( DynamicTypedef,     _Any_      ) => " (t)dy.Value "；
        ///         (     _Any_     ,     _Any_      ) => " dy "
        /// </returns>
        public static StdExpr GetValueExpr_Smart(StdExpr dy, Type? t = null)
            => dy.Type == typeof(DynamicTypedef) ?
                    ((t == null || t == typeof(DynamicTypedef)) ?
                        GetValueExpr(dy) :
                        StdExpr.Convert(GetValueExpr(dy), t)) :
                    dy;
        /// <summary>
        /// 生成一条语句，调用 dy_func 所指向的函数委托
        /// </summary>
        /// <remarks>
        /// 根据是否启用 RuntimeDiagnosis，代码会编译为静态调用或运行时调用
        /// </remarks>
        /// <param name="dy_func">类型为 DynamicTypedef，储存值应为委托的表达式</param>
        /// <param name="paras">函数参数列表</param>
        /// <returns>等价于" dy_func(paras) "的表达式</returns>
        public static StdExpr InvokeExpr(StdExpr dy_func, List<StdExpr> paras)
        {
            // 函数参数必须是 DynamicType，
            // 考虑 value 类型可能不一致的情况，使用 tmp 变量进行封装
            for (int i = 0; i < paras.Count; i++)
            {
                StdExpr para = paras[i];
                if (para.Type != typeof(DynamicTypedef))
                {
                    StdExprs.ParameterExpression tmp = StdExpr.Variable(typeof(DynamicTypedef), "_paraValTmp_");
                    paras[i] = StdExpr.Block(
                        new[] { tmp },
                        SetValueExpr_Smart(tmp, para),
                        tmp);
                }
            }

        #if EnableRuntimeDiagnosis
            ///<see cref="Invoke(DynamicTypedef[]?)"/>
            // 如果启用 RuntimeDiagnosis，编译为运行时方法 DynamicTypedef.Invoke
            MethodInfo callMethod =   
                typeof(DynamicTypedef).GetMethod("Invoke") ??
                throw new Exception("不可能报错的这里，纯粹避免编译警告");
            // 在 Call 的时候，params array 参数类型会失去 params 关键字支持，
            // 所以只能把参数转为数组再传进去
            var paraList = StdExpr.NewArrayInit(typeof(DynamicTypedef), paras);
            // 运行时 Invoke
            return StdExpr.Call(dy_func, callMethod, paraList);
        #endif

            // 不启用 RuntimeDiagnosis，则编译为静态方法，根据参数数量判断委托类型
            return StdExpr.Invoke(GetValueExpr_Smart(dy_func, DelegateType.WithArgCounts[paras.Count]), paras); 
        }
        /// <summary>
        /// 生成一条语句，索引 dy_array 在指定位置的元素
        /// </summary>
        /// <remarks>
        /// 索引表达式的类型必须是 int32，否则本函数抛出异常；
        /// 【本函数的运行时方法已弃用】
        /// </remarks>
        /// <param name="dy_array">类型为 DynamicTypedef，储存值应为数组的表达式</param>
        /// <param name="index">索引值</param>
        /// <returns>等价于" dy_array[index] "的表达式</returns>
        /// <exception cref="ApplicationException">索引表达式的类型不为 int32</exception>
        public static StdExpr AccessExpr(StdExpr dy_array, StdExpr index)
        {
            // 索引参数必须是 int32，但是应该在调用本函数之前就处理好，
            // 本函数只检查类型是否匹配，不匹配就报错
            if (index.Type != typeof(int))
                throw new ApplicationException(
                    "Type of the Index Expr is not INT32 [索引表达式的类型不为 int32]");

            return StdExpr.ArrayAccess(
                GetValueExpr_Smart(dy_array, typeof(DynamicTypedef[])), 
                index);
        }
        /// <summary>
        /// 生成一条语句，索引 dy_dict 指定键值字段的元素
        /// </summary>
        /// <remarks>
        /// 键值参数必须是 string，否则本函数抛出异常；
        /// 【本函数的运行时方法已弃用】
        /// </remarks>
        /// <param name="dy_dict">类型为 DynamicTypedef，储存值应为字典的表达式</param>
        /// <param name="string_key">键值</param>
        /// <returns>等价于" dy_dict[string_key] "的表达式</returns>
        /// <exception cref="ApplicationException">键值表达式的类型不为 string</exception>
        public static StdExpr FieldExpr(StdExpr dy_dict, StdExpr string_key)
        {
            // 键值参数必须是 string，但是应该在调用本函数之前就处理好，
            // 本函数只检查类型是否匹配，不匹配就报错
            if (string_key.Type != typeof(string))
                throw new ApplicationException(
                    "Type of the Key Expr is not STRING [键值表达式的类型不为 string]");

            // 拥有索引器方法的属性
            PropertyInfo indexer = typeof(Dictionary<string, DynamicTypedef>).GetProperty("Item") ??
                throw new Exception("wrong indexer");

            return StdExpr.Property(
                GetValueExpr_Smart(dy_dict, typeof(Dictionary<string, DynamicTypedef>)),
                indexer,
                string_key);   
        }

        // 【运行时】 --------------------------------------------------------------------------
        //      note：只有启用 RuntimeDiagnosis 时，这些方法才会被调用

    #if EnableRuntimeDiagnosis
        /// <summary>
        /// 【运行时】如果Value是委托，调用之；否则抛出异常
        /// </summary>
        /// <param name="paras">参数列表</param>
        /// <returns>委托对应的返回值</returns>
        /// <exception cref="NyaRuntimeError">Value是不可调用对象，或者参数数量不匹配</exception>
        public DynamicTypedef Invoke(params DynamicTypedef[]? paras)
        {
            if (Value != null)
            {
                Type delegateType = Value.GetType();

                try
                {
        #region 枚举委托类型
                    if (paras == null || delegateType == DelegateType.WithArgCounts[0])
                        return Value.Invoke();
                    if (delegateType == DelegateType.WithArgCounts[1])
                        return Value.Invoke(paras[0]);
                    if (delegateType == DelegateType.WithArgCounts[2])
                        return Value.Invoke(paras[0], paras[1]);
                    if (delegateType == DelegateType.WithArgCounts[3])
                        return Value.Invoke(paras[0], paras[1], paras[2]);
                    if (delegateType == DelegateType.WithArgCounts[4])
                        return Value.Invoke(paras[0], paras[1], paras[2], paras[3]);
                    if (delegateType == DelegateType.WithArgCounts[5])
                        return Value.Invoke(paras[0], paras[1], paras[2], paras[3], paras[4]);
                    if (delegateType == DelegateType.WithArgCounts[6])
                        return Value.Invoke(paras[0], paras[1], paras[2], paras[3], paras[4], paras[5]);
                    if (delegateType == DelegateType.WithArgCounts[7])
                        return Value.Invoke(paras[0], paras[1], paras[2], paras[3], paras[4], paras[5], paras[6]);
                    if (delegateType == DelegateType.WithArgCounts[8])
                        return Value.Invoke(paras[0], paras[1], paras[2], paras[3], paras[4], paras[5], paras[6], paras[7]);
                    if (delegateType == DelegateType.WithArgCounts[9])
                        return Value.Invoke(paras[0], paras[1], paras[2], paras[3], paras[4], paras[5], paras[6], paras[7], paras[8]);
                    if (delegateType == DelegateType.WithArgCounts[10])
                        return Value.Invoke(paras[0], paras[1], paras[2], paras[3], paras[4], paras[5], paras[6], paras[7], paras[8], paras[9]);
                    if (delegateType == DelegateType.WithArgCounts[11])
                        return Value.Invoke(paras[0], paras[1], paras[2], paras[3], paras[4], paras[5], paras[6], paras[7], paras[8], paras[9], paras[10]);
                    if (delegateType == DelegateType.WithArgCounts[12])
                        return Value.Invoke(paras[0], paras[1], paras[2], paras[3], paras[4], paras[5], paras[6], paras[7], paras[8], paras[9], paras[10], paras[11]);
                    if (delegateType == DelegateType.WithArgCounts[13])
                        return Value.Invoke(paras[0], paras[1], paras[2], paras[3], paras[4], paras[5], paras[6], paras[7], paras[8], paras[9], paras[10], paras[11], paras[12]);
                    if (delegateType == DelegateType.WithArgCounts[14])
                        return Value.Invoke(paras[0], paras[1], paras[2], paras[3], paras[4], paras[5], paras[6], paras[7], paras[8], paras[9], paras[10], paras[11], paras[12], paras[13]);
                    if (delegateType == DelegateType.WithArgCounts[15])
                        return Value.Invoke(paras[0], paras[1], paras[2], paras[3], paras[4], paras[5], paras[6], paras[7], paras[8], paras[9], paras[10], paras[11], paras[12], paras[13], paras[14]);
                    if (delegateType == DelegateType.WithArgCounts[16])
                        return Value.Invoke(paras[0], paras[1], paras[2], paras[3], paras[4], paras[5], paras[6], paras[7], paras[8], paras[9], paras[10], paras[11], paras[12], paras[13], paras[14], paras[15]);
        #endregion
                }
                catch (IndexOutOfRangeException)
                {
                    // 数组索引越界，证明参数给少了
                    throw new NyaRuntimeError(
                        $"Argument given is LESS THAN demanded for function type [{delegateType}].");
                }
                // 不在上面类型中，则是不可调用对象
                throw new NyaRuntimeError($"Can not INVOKE uncallable object type [{delegateType}].");
            }
            // 值为 null
            throw new NyaRuntimeError($"Can not INVOKE uncallable object [NULL].");
        }

        // 弃用：无法解决属性只读问题
        /// <summary>
        /// 【运行时】如果Value是数组，索引之；否则抛出异常
        /// </summary>
        /// <param name="index">索引值</param>
        /// <returns>对应的数组元素</returns>
        /// <exception cref="NyaRuntimeError">Value不是可索引对象，或者下标越界</exception>
        public DynamicTypedef Access(int index)
        {
            if (Value != null)
            {
                Type arrayType = Value.GetType();
                string s = "123";
                var aa = s[0];
                try
                {
                    // 把数组索引放进运行时就是解决Value类型是string这一个问题，剩下都是附带的
                    if (arrayType == typeof(string))
                    {
                        ReadOnlyRef.DynamicEnity = ConvertFrom(((string)Value).ToCharArray()[index].ToString());
                        return ReadOnlyRef.DynamicEnity;
                    }
                    // 注意不能出现嵌套 DynamicType，如果 Value 类型是 DynamicTypedef[]，则直接返回索引内容
                    else if (arrayType == typeof(DynamicTypedef[]))
                        return Value[index];
                    // 如果是原始类型，则包装后返回
                    else if (Value is Array)
                    {
                        ReadOnlyRef.DynamicEnity = ConvertFrom(Value[index]);
                        return ReadOnlyRef.DynamicEnity;
                    }
                    // 不是数组
                    else
                        throw new NyaRuntimeError(
                            $"Type of [{arrayType}] is NOT an accessable type.");
                }
                catch (IndexOutOfRangeException)
                {
                    // 数组索引越界
                    throw new NyaRuntimeError(
                        $"Index [{index}] is OUT OF RANGE when accessing array [{arrayType}].");
                }
            }
            // 值为 null
            throw new NyaRuntimeError($"Type of [NULL] is NOT an accessable type.");
        }

        /// <summary>
        /// 【运行时】如果Value是字典，索引之；否则抛出异常
        /// </summary>
        /// <param name="fieldName">索引值</param>
        /// <returns>对应的词典元素</returns>
        /// <exception cref="NyaRuntimeError">Value不是可索引对象，或者字典中没有对应的索引键</exception>
        public DynamicTypedef AccessField(string fieldName)
        {
            if (Value != null)
            {
                Type containerType = Value.GetType();

                // 注意不能出现嵌套 DynamicType，
                // 如果 Value 类型是 Dictionary<string, DynamicTypedef>，则直接返回索引内容
                if (containerType == typeof(Dictionary<string, DynamicTypedef>))
                {
                    DynamicTypedef fieldVal;
                    return Value.TryGetValue(fieldName, out fieldVal) ?
                        fieldVal :
                        throw new NyaRuntimeError($"Not fieled named as [{fieldName}].");
                }
                // 如果是别的什么字典
                // 这个反射...淦
                else if (containerType.GetInterfaces().Where(o => o == typeof(System.Collections.IDictionary)).Count() > 0)
                {
                    dynamic fieldVal;
                    return Value.TryGetValue(fieldName, out fieldVal) ?
                        ConvertFrom(fieldVal) :
                        throw new NyaRuntimeError($"Not fieled named as [{fieldName}].");
                }
                else
                {
                    throw new NyaRuntimeError($"Type of [{containerType}] doesn't have fields.");
                }
            }
            // 值为 null
            throw new NyaRuntimeError("Type of [NULL] is NOT an accessable type.");
        }
    #endif

        // 【公共方法】 ------------------------------------------------------------------------

        /// <summary>
        /// 【运行时】将随便什么东西转为 DynamicTypedef
        /// </summary>
        public static DynamicTypedef ConvertFrom(object? v)
        {
            DynamicTypedef dy = new()
            {
                // 【当 object 被赋值给 dynamic 时，object 会（相当于）被拆箱】
                //  dynamic 默认对象支持任何方法，
                //  将 object 向 dynamic 转换等价于动态拆箱
                //  以便于不通过反射直接调用对象方法
                Value = v
            };
            return dy;
        }

        // 【重载方法】 ------------------------------------------------------------------------

        #region 父类重载
        public override string ToString()
        {
            return Value?.ToString() ?? "null";
        }
        public static explicit operator string(DynamicTypedef dy)
            => dy.ToString();
        public static explicit operator int(DynamicTypedef dy)
            => (int)dy.Value;

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
        #endregion

        #region 运算符重载
        // 重载 true 和 false 可以让这个类型能够向 bool 隐式转换
        //     虽然我很想让 0 为 false，但是这样就会再引入一个类型判断，所以还是所以了Ruby法则
        public static bool operator true(DynamicTypedef dy)    // 如果被认为是true，返回true
            => dy.Value == null ? false : dy.Value is bool ? dy.Value : true;
        public static bool operator false(DynamicTypedef dy)   // 如果被认为是false，返回true
            => dy.Value == null ? true : dy.Value is bool ? dy.Value : false;
        // 单目运算符
        public static DynamicTypedef operator -(DynamicTypedef rt)
            => ConvertFrom(-rt.Value);
        public static DynamicTypedef operator !(DynamicTypedef rt)
            => ConvertFrom(!rt.Value);
        public static DynamicTypedef operator ~(DynamicTypedef rt)
            => ConvertFrom(~rt.Value);
        public static DynamicTypedef operator ++(DynamicTypedef rt)
            => ConvertFrom(++rt.Value);
        public static DynamicTypedef operator --(DynamicTypedef rt)
            => ConvertFrom(--rt.Value);
        // 数学运算符
        public static DynamicTypedef operator +(DynamicTypedef lf, DynamicTypedef rt)
            => ConvertFrom(lf.Value + rt.Value);
        public static DynamicTypedef operator -(DynamicTypedef lf, DynamicTypedef rt)
            => ConvertFrom(lf.Value - rt.Value);
        public static DynamicTypedef operator *(DynamicTypedef lf, DynamicTypedef rt)
            => ConvertFrom(lf.Value * rt.Value);
        public static DynamicTypedef operator /(DynamicTypedef lf, DynamicTypedef rt)
            => ConvertFrom(lf.Value / rt.Value);
        public static DynamicTypedef operator %(DynamicTypedef lf, DynamicTypedef rt)
            => ConvertFrom(lf.Value % rt.Value);
        public static DynamicTypedef operator &(DynamicTypedef lf, DynamicTypedef rt)
            => ConvertFrom(lf.Value & rt.Value);
        public static DynamicTypedef operator |(DynamicTypedef lf, DynamicTypedef rt)
            => ConvertFrom(lf.Value | rt.Value);
        public static DynamicTypedef operator ^(DynamicTypedef lf, DynamicTypedef rt)
            => ConvertFrom(lf.Value ^ rt.Value);
        public static DynamicTypedef operator +(DynamicTypedef lf, double rt)
            => ConvertFrom(lf.Value + rt);
        public static DynamicTypedef operator -(DynamicTypedef lf, double rt)
            => ConvertFrom(lf.Value - rt);
        public static DynamicTypedef operator *(DynamicTypedef lf, double rt)
            => ConvertFrom(lf.Value * rt);
        public static DynamicTypedef operator /(DynamicTypedef lf, double rt)
            => ConvertFrom(lf.Value / rt);
        public static DynamicTypedef operator +(DynamicTypedef lf, int rt)
            => ConvertFrom(lf.Value + rt);
        public static DynamicTypedef operator -(DynamicTypedef lf, int rt)
            => ConvertFrom(lf.Value - rt);
        public static DynamicTypedef operator *(DynamicTypedef lf, int rt)
            => ConvertFrom(lf.Value * rt);
        public static DynamicTypedef operator /(DynamicTypedef lf, int rt)
            => ConvertFrom(lf.Value / rt);
        public static DynamicTypedef operator %(DynamicTypedef lf, int rt)
            => ConvertFrom(lf.Value % rt);
        public static DynamicTypedef operator &(DynamicTypedef lf, int rt)
            => ConvertFrom(lf.Value & rt);
        public static DynamicTypedef operator |(DynamicTypedef lf, int rt)
            => ConvertFrom(lf.Value | rt);
        public static DynamicTypedef operator ^(DynamicTypedef lf, int rt)
            => ConvertFrom(lf.Value ^ rt);
        public static DynamicTypedef operator +(DynamicTypedef lf, long rt)
            => ConvertFrom(lf.Value + rt);
        public static DynamicTypedef operator -(DynamicTypedef lf, long rt)
            => ConvertFrom(lf.Value - rt);
        public static DynamicTypedef operator *(DynamicTypedef lf, long rt)
            => ConvertFrom(lf.Value * rt);
        public static DynamicTypedef operator /(DynamicTypedef lf, long rt)
            => ConvertFrom(lf.Value / rt);
        public static DynamicTypedef operator %(DynamicTypedef lf, long rt)
            => ConvertFrom(lf.Value % rt);
        public static DynamicTypedef operator &(DynamicTypedef lf, long rt)
            => ConvertFrom(lf.Value & rt);
        public static DynamicTypedef operator |(DynamicTypedef lf, long rt)
            => ConvertFrom(lf.Value | rt);
        public static DynamicTypedef operator ^(DynamicTypedef lf, long rt)
            => ConvertFrom(lf.Value ^ rt);
        public static DynamicTypedef operator +(DynamicTypedef lf, string rt)
            => ConvertFrom(lf.Value + rt);
        public static DynamicTypedef operator +(double lf, DynamicTypedef rt)
            => ConvertFrom(lf + rt.Value);
        public static DynamicTypedef operator -(double lf, DynamicTypedef rt)
            => ConvertFrom(lf - rt.Value);
        public static DynamicTypedef operator *(double lf, DynamicTypedef rt)
            => ConvertFrom(lf * rt.Value);
        public static DynamicTypedef operator /(double lf, DynamicTypedef rt)
            => ConvertFrom(lf / rt.Value);
        public static DynamicTypedef operator +(int lf, DynamicTypedef rt)
            => ConvertFrom(lf + rt.Value);
        public static DynamicTypedef operator -(int lf, DynamicTypedef rt)
            => ConvertFrom(lf - rt.Value);
        public static DynamicTypedef operator *(int lf, DynamicTypedef rt)
            => ConvertFrom(lf * rt.Value);
        public static DynamicTypedef operator /(int lf, DynamicTypedef rt)
            => ConvertFrom(lf / rt.Value);
        public static DynamicTypedef operator %(int lf, DynamicTypedef rt)
            => ConvertFrom(lf % rt.Value);
        public static DynamicTypedef operator &(int lf, DynamicTypedef rt)
            => ConvertFrom(lf & rt.Value);
        public static DynamicTypedef operator |(int lf, DynamicTypedef rt)
            => ConvertFrom(lf | rt.Value);
        public static DynamicTypedef operator ^(int lf, DynamicTypedef rt)
            => ConvertFrom(lf ^ rt.Value);
        public static DynamicTypedef operator +(long lf, DynamicTypedef rt)
            => ConvertFrom(lf + rt.Value);
        public static DynamicTypedef operator -(long lf, DynamicTypedef rt)
            => ConvertFrom(lf - rt.Value);
        public static DynamicTypedef operator *(long lf, DynamicTypedef rt)
            => ConvertFrom(lf * rt.Value);
        public static DynamicTypedef operator /(long lf, DynamicTypedef rt)
            => ConvertFrom(lf / rt.Value);
        public static DynamicTypedef operator %(long lf, DynamicTypedef rt)
            => ConvertFrom(lf % rt.Value);
        public static DynamicTypedef operator &(long lf, DynamicTypedef rt)
            => ConvertFrom(lf & rt.Value);
        public static DynamicTypedef operator |(long lf, DynamicTypedef rt)
            => ConvertFrom(lf | rt.Value);
        public static DynamicTypedef operator ^(long lf, DynamicTypedef rt)
            => ConvertFrom(lf ^ rt.Value);
        public static DynamicTypedef operator +(string lf, DynamicTypedef rt)
            => ConvertFrom(lf + rt.Value);
        // 比较运算
        public static bool operator ==(DynamicTypedef lf, DynamicTypedef rt)
            => lf.Value == rt.Value;
        public static bool operator !=(DynamicTypedef lf, DynamicTypedef rt)
            => lf.Value != rt.Value;
        public static bool operator >(DynamicTypedef lf, DynamicTypedef rt)
            => lf.Value > rt.Value;
        public static bool operator <(DynamicTypedef lf, DynamicTypedef rt)
            => lf.Value < rt.Value;
        public static bool operator >=(DynamicTypedef lf, DynamicTypedef rt)
            => lf.Value >= rt.Value;
        public static bool operator <=(DynamicTypedef lf, DynamicTypedef rt)
            => lf.Value <= rt.Value;
        public static bool operator ==(DynamicTypedef lf, double rt)
            => lf.Value == rt;
        public static bool operator !=(DynamicTypedef lf, double rt)
            => lf.Value != rt;
        public static bool operator >(DynamicTypedef lf, double rt)
            => lf.Value > rt;
        public static bool operator <(DynamicTypedef lf, double rt)
            => lf.Value < rt;
        public static bool operator >=(DynamicTypedef lf, double rt)
            => lf.Value >= rt;
        public static bool operator <=(DynamicTypedef lf, double rt)
            => lf.Value <= rt;
        public static bool operator ==(DynamicTypedef lf, int rt)
           => lf.Value == rt;
        public static bool operator !=(DynamicTypedef lf, int rt)
            => lf.Value != rt;
        public static bool operator >(DynamicTypedef lf, int rt)
            => lf.Value > rt;
        public static bool operator <(DynamicTypedef lf, int rt)
            => lf.Value < rt;
        public static bool operator >=(DynamicTypedef lf, int rt)
            => lf.Value >= rt;
        public static bool operator <=(DynamicTypedef lf, int rt)
            => lf.Value <= rt;
        public static bool operator ==(DynamicTypedef lf, long rt)
            => lf.Value == rt;
        public static bool operator !=(DynamicTypedef lf, long rt)
            => lf.Value != rt;
        public static bool operator >(DynamicTypedef lf, long rt)
            => lf.Value > rt;
        public static bool operator <(DynamicTypedef lf, long rt)
            => lf.Value < rt;
        public static bool operator >=(DynamicTypedef lf, long rt)
            => lf.Value >= rt;
        public static bool operator <=(DynamicTypedef lf, long rt)
            => lf.Value <= rt;
        public static bool operator ==(DynamicTypedef lf, string rt)
           => lf.Value == rt;
        public static bool operator !=(DynamicTypedef lf, string rt)
            => lf.Value != rt;
        public static bool operator ==(DynamicTypedef lf, bool rt)
            => (lf.Value == null ? false : lf.Value is bool ? lf.Value : true) == rt; // 和 true/false 重载一样
        public static bool operator !=(DynamicTypedef lf, bool rt)
            => (lf.Value == null ? false : lf.Value is bool ? lf.Value : true) != rt;
        public static bool operator ==(double lf, DynamicTypedef rt)
            => lf == rt.Value;
        public static bool operator !=(double lf, DynamicTypedef rt)
            => lf != rt.Value;
        public static bool operator >(double lf, DynamicTypedef rt)
            => lf > rt.Value;
        public static bool operator <(double lf, DynamicTypedef rt)
            => lf < rt.Value;
        public static bool operator >=(double lf, DynamicTypedef rt)
            => lf >= rt.Value;
        public static bool operator <=(double lf, DynamicTypedef rt)
            => lf <= rt.Value;
        public static bool operator ==(int lf, DynamicTypedef rt)
            => lf == rt.Value;
        public static bool operator !=(int lf, DynamicTypedef rt)
            => lf != rt.Value;
        public static bool operator >(int lf, DynamicTypedef rt)
            => lf > rt.Value;
        public static bool operator <(int lf, DynamicTypedef rt)
            => lf < rt.Value;
        public static bool operator >=(int lf, DynamicTypedef rt)
            => lf >= rt.Value;
        public static bool operator <=(int lf, DynamicTypedef rt)
            => lf <= rt.Value;
        public static bool operator ==(long lf, DynamicTypedef rt)
            => lf == rt.Value;
        public static bool operator !=(long lf, DynamicTypedef rt)
            => lf != rt.Value;
        public static bool operator >(long lf, DynamicTypedef rt)
            => lf > rt.Value;
        public static bool operator <(long lf, DynamicTypedef rt)
            => lf < rt.Value;
        public static bool operator >=(long lf, DynamicTypedef rt)
            => lf >= rt.Value;
        public static bool operator <=(long lf, DynamicTypedef rt)
            => lf <= rt.Value;
        public static bool operator ==(string lf, DynamicTypedef rt)
           => lf == rt.Value;
        public static bool operator !=(string lf, DynamicTypedef rt)
            => lf != rt.Value;
        public static bool operator ==(bool lf, DynamicTypedef rt)
            => (rt.Value == null ? false : rt.Value is bool ? rt.Value : true) == lf; // 和 true/false 重载一样
        public static bool operator !=(bool lf, DynamicTypedef rt)
            => (rt.Value == null ? false : rt.Value is bool ? rt.Value : true) != lf;
        #endregion

    }


    /// <summary>
    /// 运行时异常
    /// </summary>
    class NyaRuntimeError : ApplicationException
    {
        public NyaRuntimeError() : base() { }
        public NyaRuntimeError(string msg) : base(msg) { }
    }

    /// <summary>
    /// 简化委托的类型名称
    /// </summary>
    static class DelegateType
    {
        /// <summary>
        /// 委托输入的参数数
        /// </summary>
        public static readonly Type[] WithArgCounts =
        {
            typeof(Func<DynamicTypedef>),
            typeof(Func<DynamicTypedef, DynamicTypedef>),
            typeof(Func<DynamicTypedef, DynamicTypedef, DynamicTypedef>),
            typeof(Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>),
            typeof(Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>),
            typeof(Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>),
            typeof(Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>),
            typeof(Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>),
            typeof(Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>),
            typeof(Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>),
            typeof(Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>),
            typeof(Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>),
            typeof(Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>),
            typeof(Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>),
            typeof(Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>),
            typeof(Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>),
            typeof(Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>)
        };
     }

}
