/* 
 *   Parser：语法分析器，将 Token 数组展开为 AST 树
 *   
 *      这里需要用到 System.Linq.Expressions 下的内容
 *   
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// 标准表达式库（C# lambda表达式等方法都由此生成）
using StdExprs = System.Linq.Expressions;
using StdExpr = System.Linq.Expressions.Expression;

namespace NyaLang.Core
{
    public class Parser
    {

        private List<Token> tokens;  // 待分析的语素数组
        private int current = 0;     // 数组下标

        private bool parseInStrictMode = false; // 以严格语法模式进行解析
        // 以严格模式进行解析时，解析器会在遇到第一个 ParseError 时退出

        // 变量命名空间
        // presentEnv 字段会随着进入和退出局部作用域而改变，它会跟随当前环境。
        // globalEnv 字段则固定指向最外层的全局作用域。
        private valNameEnv globalEnv;
        private valNameEnv presentEnv;
        // linkedVariableTable 是链接文件的环境，如果编译器编译的文件不止一个，
        //     则在该命名空间内储存来自其它文件的变量名
        private valNameEnv linkedVariableTable;

        public Parser(List<Token> tokens)
        {
            this.tokens = tokens;

            // 在开始解析前，先初始化命名空间环境
            linkedVariableTable = new valNameEnv();
            globalEnv = new valNameEnv(linkedVariableTable);
            presentEnv = globalEnv;
        }


        /// <summary>
        /// 启动解析器解析 token 列表
        /// </summary>
        /// <remarks>
        /// 如果要执行表达式列表，
        /// 使用 <see cref="StdExpr.Block(IEnumerable{StdExpr})"/> 为列表添加根节点，
        /// 再使用 <see cref="StdExpr.Lambda{TDelegate}(StdExpr, StdExprs.ParameterExpression[]?)"/> 包装后编译执行
        /// </remarks>
        /// <param name="linkedVariables">
        /// 预声明的变量名，如果当前文件引用了来自其它文件定义的变量，
        /// 则使用该参数引入这些变量名，使该文件能够正常索引这些变量
        /// </param>
        /// <param name="newDefinedGloable">
        /// 在该文件中新定义的全局变量，可以将这些变量输入给其它文件来实现引用
        /// </param>
        /// <param name="strictMode">
        /// 是否启用严格语法模式，
        /// 如果严格语法模式被启用，解析器会在遇到第一个语法错误时抛出 ParseError；
        /// 否则解析器只会提示错误，并忽略错误的语句继续解析
        /// </param>
        /// <returns>
        /// 解析得到的表达式列表
        /// </returns>
        /// <exception cref="ParseError">在严格语法模式下发现语法错误</exception>
        /// 
        public List<StdExpr> Execute(
            out Dictionary<string, StdExprs.ParameterExpression?> newDefinedGloable,
            in Dictionary<string, StdExprs.ParameterExpression?>? linkedVariables = null,            
            bool strictMode = false)
        {
            parseInStrictMode = strictMode;
            // 如果有链接环境，向linked中注册
            if(linkedVariables != null)
                linkedVariableTable.ImportValIdfDict(linkedVariables);

            // 执行解析
            List<StdExpr> statements = new();

            try
            {
                while (!isAtEnd())
                    statements.Add(declaration());
            }
            catch (ParseError)
            {
                throw; // 继续上抛
            }

            /*  注意当上述语句执行完毕后，所有局部变量都声明在了其block中，
             *  但是没有被任何大括号包裹的变量（全局变量）仍未被声明
             *  此时若包装表达式列表进行编译，对全局变量仍然会出现 in scope 错误
             */
            // 因此传出变量列表
            newDefinedGloable = globalEnv.ExportValIdfDict();

            //如果要重复使用这个对象，则解析完成后要还原环境
            linkedVariableTable = new valNameEnv();
            globalEnv = new valNameEnv(linkedVariableTable);
            presentEnv = globalEnv;

            return statements;
        }

        #region 解析时变量命名空间
        /* 
         *  使用 StdExpr.Assign 对变量赋值时，其要求的左值是 ParameterExpression 对象
         *  该对象在 StdExpr.Variable 时取得，并对同一个变量一直不变
         *  因此可以理解为它是编译时为变量分配地址的唯一标识符，
         *  因此原本解释器执行的环境控制操作提前到分析时进行，
         *  原本储存变量值的环境变成储存地址的环境
         */
        /*  
         *  什么时候需要创建子环境？
         *      每个由左右大括号括起来的区域 Block 即是一个子环境
         *      此外，标签 Label 也有类似的性质，而 Label 在循环的实现中是必要的，
         *      因此在分析循环和函数时也需要
         *  哪些东西需要存入环境？
         *      变量 StdExpr.Variable
         *      标签 StdExpr.Label
         *      函数 生成一个给同名变量赋值为 Delegate 的语句，
         *           然后将该变量存入变量区
         *  变量名不可以取那些？
         *      不可使用："break", "continue"，"return", "this"（尽管是保留字）
         *          这几个标签固定为循环尾、循环头、函数尾、容器访问自身
         *  如何使用变量名列表？
         *      1. 在检查到标识符类型 token 时，用列表里对应的地址标记替换；
         *      2. 在创建 block, loop 等表达式时，初始化函数中有 param 参数，
         *          将当前环境的 name2ValIdf 中的值字段传进去，
         *          表达式树自身会实现环境嵌套（即子环境可以调用父环境变量，
         *          但是如果在表达式初始化中没有声明 param，
         *          则会报 param in scope 错误）
         */

        /// <summary>
        /// 等效于解释器模式下的 Environment 对象，
        /// 储存当前子环境下的变量名称(string) -> 地址标识符(StdExpr.Variable/Label)映射
        /// </summary>
        protected class valNameEnv
        {
            // 变量/标签/函数名的空间
            protected Dictionary<string, StdExprs.ParameterExpression?> name2ValIdf = new();
            protected Dictionary<string, StdExprs.LabelTarget?> name2LabIdf = new();
            // 嵌套环境中指向外层环境的指针，用于实现变量作用域分割
            public valNameEnv? enclosing;

            public valNameEnv()
            {
                enclosing = null;
            }
            public valNameEnv(valNameEnv enclosing)
            {
                this.enclosing = enclosing;
            }

            /// <summary>
            /// 引入一个新变量
            /// </summary>
            public void Define(string name, StdExprs.ParameterExpression addrIdf)
            {
                name2ValIdf.Add(name, addrIdf);
            }
            /// <summary>
            /// 引入一个新标签
            /// </summary>
            public void Define(string name, StdExprs.LabelTarget addrIdf)
            {
                name2LabIdf.Add(name, addrIdf);
            }
            /// <summary>
            /// 删除该环境中的一个标签
            /// </summary>
            /// <returns>如果成功删除，返回true</returns>
            public bool DeleteLab(string name)
            {
                return name2LabIdf.Remove(name);
            }
            /// <summary>
            /// 读取一个变量标识
            /// </summary>
            /// <returns>如果找到，返回之；否则返回 null</returns>
            public StdExprs.ParameterExpression? GetValIdf(Token name)
            {
                if (name2ValIdf.ContainsKey(name.lexeme))
                {
                    StdExprs.ParameterExpression? tmp;
                    if (name2ValIdf.TryGetValue(name.lexeme, out tmp)) return tmp;
                    return null;
                }
                if (enclosing != null) return enclosing.GetValIdf(name);
                else 
                    return null;
            }
            /// <summary>
            /// 读取一个标签标识
            /// </summary>
            /// <returns>如果找到，返回之；否则返回 null</returns>
            public StdExprs.LabelTarget? GetLabIdf(Token name)
            {
                if (name2LabIdf.ContainsKey(name.lexeme))
                {
                    StdExprs.LabelTarget? tmp;
                    if (name2LabIdf.TryGetValue(name.lexeme, out tmp)) return tmp;
                    return null;
                }
                if (enclosing != null) return enclosing.GetLabIdf(name);
                else return null;
            }
            /// <summary>
            /// 限定在当前子环境中，读取一个标签标识
            /// </summary>
            /// <returns>如果找到，返回之；否则返回 null</returns>
            public StdExprs.LabelTarget? GetLabIdf_Strict(Token name)
            {
                if (name2LabIdf.ContainsKey(name.lexeme))
                {
                    StdExprs.LabelTarget? tmp;
                    if (name2LabIdf.TryGetValue(name.lexeme, out tmp)) return tmp;
                    return null;
                }
                return null;
            }
            // --------------------------------------------------------------------------------
            /// <summary>
            /// 用给出的变量名字典替换内部字典
            /// </summary>
            public void ImportValIdfDict(Dictionary<string, StdExprs.ParameterExpression?> dict)
                => name2ValIdf = dict;
            /// <summary>
            /// 导出环境中的变量名字典
            /// </summary>
            public Dictionary<string, StdExprs.ParameterExpression?> ExportValIdfDict()
                => name2ValIdf;
            // --------------------------------------------------------------------------------
            /// <summary>
            /// 搜索 return 标签
            /// </summary>
            /// <param name="lineMark">如果发生异常，标识异常所在的行数</param>
            /// <exception cref="ParseError"></exception>
            public StdExprs.LabelTarget? GetReturnTarget(Token? lineMark = null)
            {
                if (name2LabIdf.ContainsKey("return"))
                {
                    StdExprs.LabelTarget? tmp;
                    if (name2LabIdf.TryGetValue("return", out tmp)) return tmp;
                    return null;
                }
                if (enclosing != null) return enclosing.GetReturnTarget(lineMark);

                throw new ParseError(lineMark, $"Cannot find RETURN label.");
            }
            /// <summary>
            /// 搜索 break 标签
            /// </summary>
            /// <param name="lineMark">如果发生异常，标识异常所在的行数</param>
            /// <exception cref="ParseError"></exception>
            public StdExprs.LabelTarget? GetBreakTarget(Token? lineMark = null)
            {
                if (name2LabIdf.ContainsKey("break"))
                {
                    StdExprs.LabelTarget? tmp;
                    if (name2LabIdf.TryGetValue("break", out tmp)) return tmp;
                    return null;
                }
                if (enclosing != null) return enclosing.GetBreakTarget(lineMark);

                throw new ParseError(lineMark, $"Cannot find BREAK label.");
            }
            /// <summary>
            /// 搜索 continue 标签
            /// </summary>
            /// <param name="lineMark">如果发生异常，标识异常所在的行数</param>
            /// <exception cref="ParseError"></exception>
            public StdExprs.LabelTarget? GetContinueTarget(Token? lineMark = null)
            {
                if (name2LabIdf.ContainsKey("continue"))
                {
                    StdExprs.LabelTarget? tmp;
                    if (name2LabIdf.TryGetValue("continue", out tmp)) return tmp;
                    return null;
                }
                if (enclosing != null) return enclosing.GetContinueTarget(lineMark);

                throw new ParseError(lineMark, $"Cannot find CONTINUE label.");
            }
            /// <summary>
            /// 获取环境中所有变量地址的列表
            /// </summary>
            public List<StdExprs.ParameterExpression>? GetValIdfList()
            {
                /*    C# ×    SQL √    */
                return (from v in name2ValIdf.Values.ToList() where v != null select v).ToList();
            }

        }

        /// <summary>
        /// 创建并步入子环境
        /// </summary>
        protected void runIntoSubEnv()
        {
            this.presentEnv = new valNameEnv(presentEnv);
        }
        /// <summary>
        /// 从子环境返回父环境
        /// </summary>
        /// <exception cref="ParseError">父环境为NULL</exception>
        protected void runIntoBaseEnv()
        {
            if(this.presentEnv.enclosing == null) // linked环境
                throw new ApplicationException(
                    "变量环境指针不正确：presentEnv指针不应当指向链接文件变量名空间");
            else if (this.presentEnv.enclosing.enclosing == null) // global环境
                throw new ParseError("Cannot step to base environment form GLOBAL_ENV");
            else
                this.presentEnv = this.presentEnv.enclosing;   
        }

        #endregion
        #region 异常处理类型
        /// <summary>
        ///  解析器异常类
        /// 【note】除非启用严格语法模式，
        ///         否则所有此类异常都应该在 Parser.declaration 的 
        ///         try-catch 块中得到处理，
        ///         在其他位置无法捕获该异常
        /// </summary>
        public class ParseError : Exception
        {
            public Token? token;
            public ParseError(Token? token, string msg) : base(msg)
            {
                this.token = token;
            }
            public ParseError(string msg) : base(msg)
            {
                this.token = null;
            }
            public ParseError(string msg, Exception? e):base(msg, e)
            {
                this.token = null;
            }

            public void Report()
            {
                if (token == null)
                {
                    Console.Error.WriteLine($"[ ParserError ] : {base.Message}");
                }
                else if(token.type == TokenType.EOF){
                    Console.Error.WriteLine($"[ ParserError ] at line [{token.line}] in [end] : {base.Message}");
                }
                else if(token.type == TokenType.SELF_ADD)  // 为大家解决 i++ 和 ++i 不分的情况
                {
                    Console.Error.WriteLine($"[ ParserError ] at line [{token.line}] in [{token.lexeme}] : {base.Message}");
                    Console.Error.WriteLine("   [ Note ] NyaLang doesn't suport expressions like ' i++ ', do you mean ' ++i '?");
                }
                else if (token.type == TokenType.SELF_DEC)
                {
                    Console.Error.WriteLine($"[ ParserError ] at line [{token.line}] in [{token.lexeme}] : {base.Message}");
                    Console.Error.WriteLine("   [ Note ] NyaLang doesn't suport expressions like ' i-- ', do you mean ' --i '?");
                }
                else
                {
                    Console.Error.WriteLine($"[ ParserError ] at line [{token.line}] in [{token.lexeme}] : {base.Message}");
                }
            }
        }
        #endregion
        #region 常量列表
        /// <summary>
        /// 即 typeof(DynamicTypedef)
        /// </summary>
        static readonly Type anyType = typeof(DynamicTypedef);
        /// <summary>
        /// 解析器发现语法错误时返回的魔数 StdExpr.Constant(Magic)
        /// </summary>
        public static readonly int errorFoundMagic = "MioKirie".GetHashCode();
        #endregion

        // ---- 递归器 ---------------------------------
        #region 上下文无关语法
        /*
         * 递归下降分析：
         * 
         * -------------------------------------------------------------
         * program        → declaration* EOF ;
         * declaration    → varDecl
         *                |  constDecl
         *                |  funDecl
         *                |  labelDecl
         *                |  statement ;
         * varDecl        → "var" IDENTIFIER ( "=" expression )? ";" ;
         * constDecl      → "let" IDENTIFIER "=" expression ";" ;
         * funDecl        → "fun" function ;
         * function       → IDENTIFIER "(" parameters? ")" block ;
         * parameters     → IDENTIFIER ( "," IDENTIFIER )* ;
         * labelDecl      → "label" IDENTIFIER ";" ;
         * statement      → exprStmt
         *                |  block   
         *                |  ifStmt
         *                |  switchStmt
         *                |  forStmt
         *                |  whileStmt
         *                |  breakStmt
         *                |  continueStmt
         *                |  gotoStmt
         *                |  returnStmt
         *                | 【other stmt】 ;
         * exprStmt       → expression ";" ;
         * block          → "{" declaration* "}" ;
         * ifStmt         → "if" "(" expression ")" statement
         *                    ( "else" statement )? ;
         * switchStmt     → "switch"  expression ":" 
         *                   "case" ":" expression statement 
         *                   ("case" ":" expression statement )* 
         *                   ("default" ":" statement )? ;
         * forStmt        → "for" "(" ( varDecl | exprStmt | ";" )
         *                   expression? ";"
         *                   expression? ")" statement ;
         * whileStmt      → "while" "(" expression ")" statement ;
         * breakStmt      → "break" ";" ;
         * continueStmt   → "continue" ";" ;
         * gotoStmt       → "goto" IDENTIFIER ";" ;
         * returnStmt     → "return" expression? ";" ;
         * 【other stmt】
         * 
         * -------------------------------------------------------------
         * expression     → assignment 
         *                 | condition ;
         * assignment     → IDENTIFIER ( "=" | "+=" | "-=" | "*=" | "/=" | "%=" ) assignment
         *                 | logic_xor ;
         * logic_xor      → logic_or ( "xor" | "^^" logic_or )* ;    
         * logic_or       → logic_and ( "or" | "||" logic_and )* ;
         * logic_and      → equality ( "and" | "&&" equality )* ;
         *     【note】上述三个逻辑运算不是同一优先级，节点无法合并
         * equality       → comparison ( ( "!=" | "==" ) comparison )* ;
         * comparison     → bit_logic ( ( ">" | ">=" | "<" | "<=" ) bit_logic )* ;
         * bit_logic      → bit_shift ( ( "&" | "|" | "^" ) bit_shift )*
         * bit_shift      → term ( ( "<<" | ">>" ) term )*         
         * term           → factor ( ( "-" | "+" ) factor )* ;
         * factor         → unary ( ( "/" | "*" | "%" ) unary )* ;
         * unary          → ( "!" | "-" | "~" | "++" | "--" | "not" ) unary
         *                 | callable ;
         * callable       → static_invoke? ( invoke | index | field )* 
         *                 | tuple
         * invoke         → tuple "(" expression? ( "," expression )* ")" ;
         * index          → tuple "[" expression ( "," expression )* "]" ;
         * field          → tuple "." IDENTIFIER ;
         * static_invoke  → "$" IDENTIFIER "(" expression? ( "," expression )* ")" ;
         * tuple          → "[" expression? ( "," expression )* "]"
         *                 |  "{" ( IDENTIFIER ":" expression ))? ( "," IDENTIFIER ":" expression ))* "}" 
         *                 |  lambdaFunc
         *                 |  primary
         * lambdaFunc     → "fun" IDENTIFIER? "(" parameters? ")" ( block | "=>" expression ) ;
         * primary        → NUMBER | STRING | "true" | "false" | "nil"
         *                 | "(" expression ")" 
         *                 | IDENTIFIER ;
         */
        #endregion

        // declaration    → varDecl
        //                |  constDecl
        //                |  funDecl
        //                |  labelDecl
        //                |  statement ;
        private StdExpr declaration()
        {
            try
            {
                if (match(TokenType.VAR)) return   varDecl();
                if (match(TokenType.LET)) return   constDecl();
                if (match(TokenType.FUN)) return   funDecl();
                if (match(TokenType.LABEL)) return labelDecl();

                return statement();
            }
            // 【注意】这个地方会吞异常，
            //   除非打开了严格语法模式，否则所有 ParseError 异常不应该在此处之外的地方处理
            catch (ParseError e) 
            {

                e.Report(); // 向命令行报告

                synchronize();

                // 如果选择了严格语法模式，则向上抛出异常
                if (parseInStrictMode)
                    throw new ParseError("Parsing canceled due to strict mode.", e);

                // return null;
                /* 
                 *  原本 return null 这样是可以的，
                 *  使得解析器可以继续分析后面的代码
                 *  但是返回值可 null 导致后面出现一大堆类型不匹配问题
                 *  因此修改为返回魔数
                 */
                return StdExpr.Constant(errorFoundMagic);                
            }
        }
        // varDecl        → "var" IDENTIFIER ( "=" expression )? ";" ;
        private StdExpr varDecl()
        {
            // 获取变量名
            Token name = consume(TokenType.IDENTIFIER, "Expect variable name.");
            // 将变量初始化为 DynamicTypedef 类型
                // StdExpr.Variable 返回的对象是所定义的变量在 AST 树中的唯一标识符，
                // 如果要对该变量重新赋值，StdExpr.Assign 的左侧就必须传入此时得到的值
                // （重新 StdExpr.Variable(typeof(object), name.lexeme) 没有作用，因为
                //   该方法传入的第二个参数不定义变量名，只在报错时起提示作用）
                // 因此原本解释器执行的环境控制操作应该在此时进行 
            var _declare = StdExpr.Variable(anyType, name.lexeme);
            // 向环境里添加变量
            presentEnv.Define(name.lexeme, _declare);

            // 初始化器
            StdExpr? initializer = null;
            // 如果找到“=”标记，解析器就知道后面有一个初始化表达式，并对其进行解析
            //    否则，它会将初始化器保持为null
            if (match(TokenType.EQUAL))
            {
                initializer = expression();
            }

            // 结尾分号
            consume(TokenType.SEMICOLON, "Expect ';' after variable declaration.");

            // 返回赋值语句
            return DynamicTypedef.SetValueExpr_Smart(_declare, initializer ?? StdExpr.Constant(null));
        }
        // constDecl      → "let" IDENTIFIER "=" expression ";" ;
        private StdExpr constDecl()
        {
            // 获取常量名
            Token name = consume(TokenType.IDENTIFIER, "Expect constant value name.");
            // 常量必须初始化
            consume(TokenType.EQUAL, "Constant value must have initialize expression.");
            // 初始化器
            StdExpr initializer = expression(); 
            // 与变量不同，如果初始化器是 dynamic，那么必须将它拆包
            if(initializer.Type == anyType)
                initializer = StdExpr.Field(initializer, "Value");
            // 变量类型与初始化器一致
            var _declare = StdExpr.Variable(initializer.Type, name.lexeme);
            // 向环境里添加变量
            presentEnv.Define(name.lexeme, _declare);

            // 结尾分号
            consume(TokenType.SEMICOLON, "Expect ';' after variable declaration.");

            return StdExpr.Assign(_declare, initializer);
        }

        // funDecl        → "fun" function ;
        // function       → IDENTIFIER "(" parameters? ")" block ;
        // parameters     → IDENTIFIER ( "," IDENTIFIER )* ;
        private StdExpr funDecl()
        {
            /* 
             * 使用lambda表达式来实现函数定义
             * 
             *     注意lambda函数始终将其最后一个语句的值作为返回，
             *     因此我们将一个具有值的 "return" 标签拼接到函数尾部
             * 
             */

            // 获取函数名
            Token name = consume(TokenType.IDENTIFIER, "Expect function name."); 
            // 首先我们预先注册函数名，这样在函数递归时就不需要把函数自己作为参数传进去了
            var funName = StdExpr.Variable(anyType, $"func[{name.lexeme}]");
            presentEnv.Define(name.lexeme, funName); // 定义在闭包的环境

            // 创建一个子环境来储存参数名称（主要是防止参数和外环境变量同名的情况）-------------
            runIntoSubEnv();
            /* 
             *  由于 ParameterExpression 的地址分配是编译是进行的，
             *  所以此时会自动完成闭包
             */

            // 置于函数尾部，具有 DynamicType 类型值的标签
            StdExprs.LabelTarget _return = StdExpr.Label(anyType, "_return");
            // 将标签置入环境
            presentEnv.Define("return", _return);
   
            consume(TokenType.LEFT_PAREN, $"Expect '(' after function name.");    // 左括号
            List<StdExprs.ParameterExpression> parameters = new();              // 参数列表

            // 外部的if语句用于处理零参数的情况，
            // 内部的while会循环解析参数，只要能找到分隔参数的逗号。
            // 其结果是包含每个参数名称的标记列表
            if (!check(TokenType.RIGHT_PAREN))
            {
                do
                {
                    Token para = consume(TokenType.IDENTIFIER, "Expect parameter name.");
                    var paraAddr = StdExpr.Variable(anyType, para.lexeme);
                    presentEnv.Define(para.lexeme, paraAddr);
                    parameters.Add(paraAddr); // 创建lambda表达式时需要将参数地址标签传进去

                } while (match(TokenType.COMMA));
            }
            consume(TokenType.RIGHT_PAREN, "Expect ')' after parameters.");

            consume(TokenType.LEFT_BRACE, "Expect '{' before function body.");

            var funBody = block();

            // 在函数体末尾拼接结束语句
            funBody = StdExpr.Block(
                funBody,
                // 函数结尾标签，如果函数中没有返回，则返回null
                StdExpr.Label(_return, StdExpr.Constant(new DynamicTypedef(null)))  
                );
            /*  
             *  StdExpr.Label 是咱见过最离谱的函数之一了
             *      如果参数是空或类型，它返回 LabelTarget （创建标签对象）
             *      如果参数是 LabelTarget，它返回 LabelExpression : StdExpr （设置标签）
             */

            // 退出子环境 ---------------------------------------------------------------------
            runIntoBaseEnv();

            // 创建lambda函数
            /*     咱想起一些好笑的事情（不是）
             *     C# 自身也没办法解决动态参数数量委托的问题，
             *     他的委托构造函数是从0个参数到16个参数全部重载了一遍
             *     所以这里我们也只好穷举
             */
            StdExpr lambdaFun;
            switch (parameters.Count)
            {
                case 0:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef>>(funBody, parameters);
                    break;
                case 1:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 2:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 3:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 4:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 5:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 6:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 7:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 8:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 9:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 10:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 11:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 12:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 13:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 14:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 15:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 16:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                default:
                    throw new ParseError(name, $"Function[{name.lexeme}] has too many ( > 16 ) arguements");
            }

            /* 
             *  注意如果要调用未编译的lambda表达式，使用 StdExpr.Invoke(lambda, params)
             *      如： StdExpr.Invoke(lambdaFun, StdExpr.Constant(1));
             *  如果调用委托对象，则将其储存入 DynamicType 变量中，调用方法
             *      InvokeExpr(StdExpr dy_func, List<StdExpr> paras)
             */

            /* 
             *  之后是重点：
             *      在正常程序中，函数名实际上是指向方法地址的指针
             *      也就是说，函数名是一个变量，函数声明语句是对变量赋值的语句
             *      因此当我们分析完函数体之后，应该执行一个对变量赋值的语句
             */
            // 将赋值语句返回出去，
            // 此处的右值是 StdExpr<Delegate>，在编译时，它会被转化为对应的委托对象
            return DynamicTypedef.SetValueExpr(funName, lambdaFun);

        }
        // labelDecl      → "label" IDENTIFIER ";" ;
        private StdExpr labelDecl()
        {
            // 标签命名
            Token name = consume(TokenType.IDENTIFIER, "Expect label name.");
            // 分号检查
            consume(TokenType.SEMICOLON, "Expect ';' after label declaration.");

            /* 
             *  goto 语句存在这样的情况，即label在goto语句后面，
             *  此时在 goto 语句中，我们先假定目标标签存在，申请一个该标签的地址；
             *  考虑到 goto 有如下限制：
             *      1. 允许从子环境跳到父环境
             *      2. 禁止从父环境跳到子环境
             *  由于(2)在编译时会由 label 的作用域自动实现， 
             *  因此我们先假定目标标签在基环境 global 中，将其 define；
             *  
             *  此后，每当遇到 label 声明，考虑该 label 可能在之前已被引用，
             *  则在 global 中查找之；注意已引用而未定义的标签只可能存在于 global 中。
             *  之后，从 global 中删除该标签。
             *  
             *  注意由上所述方法，如果出现类似下列情况：
             *      {
             *          goto target;
             *          {
             *              label target;
             *          }
             *          label target;
             *      }
             *  则外层 goto 将无法实现，因为内层 label 声明时使用了外层 goto 的预定义标签，
             *  导致外层 goto 的目标丢失。
             *  因此，原则上我们要求【同一环境及其子环境下的 label 不能重名】  
             */

            // 在 global 中查找标签，如果找不到，新建一个；找到了，删除 global 里的记录
            StdExprs.LabelTarget? _label = globalEnv.GetLabIdf_Strict(name);
            if (_label == null)
                _label = StdExpr.Label(name.lexeme);
            else
                globalEnv.DeleteLab(name.lexeme);
            // 向当前环境添加标签
            presentEnv.Define(name.lexeme, _label);
            // 返回标签定位语句
            return StdExpr.Label(_label);
        }
        // statement      → exprStmt
        //                |  block 
        //                |  ifStmt
        //                |  switchStmt
        //                |  forStmt
        //                |  whileStmt
        //                |  breakStmt
        //                |  continueStmt
        //                |  gotoStmt
        //                |  returnStmt
        //                | 【other stmt】 ;
        private StdExpr statement()
        {
            if (match(TokenType.LEFT_BRACE))    return block();              
            else if (match(TokenType.IF))       return ifStatement();
            else if (match(TokenType.SWITCH))   return switchStmt();
            else if (match(TokenType.FOR))      return forStatement();
            else if (match(TokenType.WHILE))    return whileStatement();
            else if (match(TokenType.BREAK))    return breakStmt();
            else if (match(TokenType.CONTINUE)) return continueStmt();
            else if (match(TokenType.GOTO))     return gotoStmt();
            else if (match(TokenType.RETURN))   return returnStatement();
            else if (match(TokenType.PRINT))    return printStatement();

            return expressionStatement();
        }
        // exprStmt       → expression ";" ;
        private StdExpr expressionStatement()
        {
            StdExpr expr = expression();
            consume(TokenType.SEMICOLON, "Expect ';' after expression."); // 记得行尾分号检查

            return  expr;
        }
        // block          → "{" declaration* "}" ;
        private StdExpr block()
        {
            // 每一个代码块都需要创建一个新的子环境来储存局部变量
            // 并且代码块分析完成后，要记得退出到父环境

            // 新建一个子环境储存局部变量
            runIntoSubEnv();

            List<StdExpr> statements = new();

            while (!check(TokenType.RIGHT_BRACE) && !isAtEnd())
            {
                statements.Add(declaration());
            }

            consume(TokenType.RIGHT_BRACE, "Expect '}' after block.");

            // 保存变量名称表
            List<StdExprs.ParameterExpression>? paramList = presentEnv.GetValIdfList();

            // 必须注意每当处理完一个 block，都应当推出当前子环境
            runIntoBaseEnv();

            // 直接用 StdExpr.Block 封装成一个对象
            return StdExpr.Block(paramList, statements);
        }
        // ifStmt         → "if" "(" expression ")" statement
        //                    ( "else" statement )? ;
        private StdExpr ifStatement()
        {
            consume(TokenType.LEFT_PAREN, "Expect '(' after 'if'.");
            StdExpr condition = expression();
            consume(TokenType.RIGHT_PAREN, "Expect ')' after if condition.");

            StdExpr thenBranch = statement();
            // 【else与前面最近的if绑定在一起】
            //    想要和别的部分绑定？用大括号就好了
            StdExpr? elseBranch = match(TokenType.ELSE) ? statement() : null;

            return elseBranch == null ?
                StdExpr.IfThen(StdExpr.IsTrue(condition), thenBranch) : 
                StdExpr.IfThenElse(StdExpr.IsTrue(condition), thenBranch, elseBranch);
            // 使用 IsTrue 可以调用 dynamicType 的 true() 方法重载，
            //     后面 for 和 while 里同样
        }
        // switchStmt     → "switch" expression ":" 
        //                   "case" expression ":" statement 
        //                   ("case" expression ":" statement )* 
        //                   ("default" ":" statement )? ;
        private StdExpr switchStmt()
        {
            consume(TokenType.LEFT_PAREN, "Expect '(' after switch.");
            StdExpr testVal = expression();
            consume(TokenType.RIGHT_PAREN, "Expect ')' after switch test value.");
            consume(TokenType.LEFT_BRACE, "Expect '{' before any 'case' expression.");

            // C# 原生的 switch 要求被比较值是常量，且需要不断 break，过于麻烦
            // 因此咱把它做成 if-else 的语法糖

            // 至少需要一条 case
            consume(TokenType.CASE, "Switch statment should have at least one case.");
            StdExpr case0 = expression();
            consume(TokenType.COLON, "Expect ':' after the expression of a case.");
            StdExpr stat0 = statement();

            // 递归查找结束位置
            StdExpr? nextCase = caseLoop(testVal);

            // 检查结束大括号
            consume(TokenType.RIGHT_BRACE, "Expect '}' after switch-case statment.");

            if (nextCase != null)
                return StdExpr.IfThenElse(
                    StdExpr.Equal(testVal, case0),
                    stat0,
                    nextCase);
            else
                return StdExpr.IfThen(
                    StdExpr.Equal(testVal, case0),
                    stat0);
        }
        private StdExpr? caseLoop(StdExpr testVal)
        {
            // 使用递归循环查找 switch 语句的结束位置
            if (match(TokenType.CASE))
            {
                StdExpr _case = expression();
                consume(TokenType.COLON, "Expect ':' after the expression of a case.");
                StdExpr _stat = statement();
                // 下一个 case
                StdExpr? nextCase = caseLoop(testVal);

                if (nextCase != null)
                    return StdExpr.IfThenElse(
                        StdExpr.Equal(testVal, _case),
                        _stat,
                        nextCase);
                else
                    return StdExpr.IfThen(
                        StdExpr.Equal(testVal, _case),
                        _stat);
            }
            else if (match(TokenType.DEFAULT))
            {
                consume(TokenType.COLON, "Expect ':' after the default expression.");
                return statement();
            }                
            else
                return null;
        }

        // forStmt        → "for" "(" ( varDecl | exprStmt | ";" )
        //                   expression? ";"
        //                   expression? ")" statement ;
        private StdExpr forStatement()
        {
            // 现在 for 没必要作为 while 的语法糖了

            // 由于需要声明标签，所以此时需要进一个新环境
            //     这些标签在循环体中 break、continue 时也需要调用，故是局部变量
            runIntoSubEnv();
            // 声明循环头尾 Label 并储存
            var _breakLabel = StdExpr.Label(); // 使用无参Label即可
            var _continueLabel = StdExpr.Label();
            presentEnv.Define("break", _breakLabel);
            presentEnv.Define("continue", _continueLabel);

            // 读取一个左括号
            consume(TokenType.LEFT_PAREN, "Expect '(' after 'for'.");
            // 如果(后面的标记是分号，那么初始化式就被省略了。
            // 否则，我们就检查var关键字，看它是否是一个变量声明。
            // 如果这两者都不符合，那么它一定是一个表达式。
            // 我们对其进行解析，并将其封装在一个表达式语句中，
            // 这样初始化器就必定属于Stmt类型（实现为StdExpr）。
            StdExpr? initializer;  // 如 var i = 0
            if (match(TokenType.SEMICOLON))
                initializer = null;
            else if (match(TokenType.VAR))
                initializer = varDecl();
            else
                initializer = expressionStatement();
            // 同样，我们查找分号检查子句是否被忽略。最后一个子句是增量语句。
            StdExpr? condition = null;  // 如 i <= 10
            if (!check(TokenType.SEMICOLON))
                condition = expression();
            consume(TokenType.SEMICOLON, "Expect ';' after loop condition.");

            StdExpr? increment = null;  // 如 i ++
            if (!check(TokenType.RIGHT_PAREN))
                increment = expression();
            consume(TokenType.RIGHT_PAREN, "Expect ')' after for clauses.");

            // 循环主体 ( 写在 for 里面的部分 )
            StdExpr innerBody = statement();
            // 把for括号里的第三句（“i++”）添到循环体里面
            StdExpr loopBody = increment == null ? innerBody : StdExpr.Block(innerBody, increment); // 实际循环的部分（多一个增量语句）
            // 如果条件式没写就视为恒true（“for(;;)”之类）
            if (condition == null) condition = StdExpr.Constant(true);
            // 创建完整循环语句
            //      使用 IsTrue 可以调用 dynamicType 的 true() 方法重载
            StdExpr fullBody = StdExpr.IfThenElse(StdExpr.IsTrue(condition), loopBody, StdExpr.Break(_breakLabel));
            // 创建循环        
            StdExpr loopStmt = StdExpr.Loop(fullBody, _breakLabel, _continueLabel);
            // 把初始化句子添上
            //      param 段里面储存的就是在循环头声明的变量（如果有）
            if (initializer != null)
                loopStmt = StdExpr.Block(presentEnv.GetValIdfList(), initializer, loopStmt);

            // 记得退出环境
            runIntoBaseEnv();
            
            return loopStmt;
        }
        // whileStmt      → "while" "(" expression ")" statement ;
        private StdExpr whileStatement()
        {
            // 由于需要声明标签，所以此时需要进一个新环境
            //     这些标签在循环体中 break、continue 时也需要调用，故是局部变量
            runIntoSubEnv();
            // 声明循环头尾 Label 并储存
            var _breakLabel = StdExpr.Label(); // 使用无参Label即可
            var _continueLabel = StdExpr.Label();
            presentEnv.Define("break", _breakLabel);
            presentEnv.Define("continue", _continueLabel);


            consume(TokenType.LEFT_PAREN, "Expect '(' after 'while'.");
            StdExpr condition = expression(); // 循环条件
            consume(TokenType.RIGHT_PAREN, "Expect ')' after condition.");
            StdExpr innerBody = statement(); // 循环体

            // 由于Loop语句只有死循环，故把循环内容改为
            //     if( condition ){ innerBody } else{ break; }
            // 形式：
            //      【note】使用 IsTrue 可以调用 dynamicType 的 true() 方法重载
            StdExpr fullBody = StdExpr.IfThenElse(StdExpr.IsTrue(condition), innerBody, StdExpr.Break(_breakLabel));
            // 创建循环体
            //      理论上这里的环境中应该没有变量
            //      （该环境中只储存了两个label，然后就步入了循环体的子环境）
            //      因此不用传 param
            var fullLoop = StdExpr.Loop(fullBody, _breakLabel, _continueLabel);

            // 循环创建结束退出环境
            runIntoBaseEnv();

            return fullLoop;
        }
        // breakStmt      → "break" ";" ;
        private StdExpr breakStmt()
        {
            Token breakToken = previous();
            consume(TokenType.SEMICOLON, "Expect ';' after break statment.");

            var _break = presentEnv.GetBreakTarget(breakToken);
            if (_break == null)
                throw new ParseError(breakToken, "No loops to break.");

            return StdExpr.Break(_break);
        }
        // continueStmt   → "continue" ";" ;
        private StdExpr continueStmt()
        {
            Token continueToken = previous();
            consume(TokenType.IDENTIFIER, "Expect ';' after continue statment.");

            var _continue = presentEnv.GetContinueTarget(continueToken);
            if (_continue == null)
                throw new ParseError(continueToken, "No loops to continue.");

            return StdExpr.Continue(_continue);
        }
        // gotoStmt       → "goto" IDENTIFIER ";" ;
        private StdExpr gotoStmt()
        {
            // 读取goto目标标签名
            Token name = consume(TokenType.IDENTIFIER, "Expect goto label.");

            // 关于 goto 与 label 的实现
            ///<see cref="labelDecl()"/>

            // 从环境里找到 goto 标签，
            var _goto = presentEnv.GetLabIdf(name);
            if (_goto == null) // 如果没有，假定标签在后面，
            {
                _goto = StdExpr.Label(name.lexeme); // 预申请一个
                globalEnv.Define(name.lexeme, _goto); // 存到 global
            }

            // 记得行尾分号检查
            consume(TokenType.SEMICOLON, "Expect ';' after goto statment.");

            return StdExpr.Goto(_goto);
        }
        // returnStmt     → "return" expression? ";" ;
        private StdExpr returnStatement()
        {
            Token keyword = previous();
            StdExpr? value = null;
            if (!check(TokenType.SEMICOLON))
            {
                value = expression();
            }

            consume(TokenType.SEMICOLON, "Expect ';' after return value.");

            // 从环境里找到 return 标签
            var _return = presentEnv.GetReturnTarget(keyword);

            if (_return == null)
                throw new ParseError(keyword, "Unable to return to null label.");

            if(value == null)
            {
                // 如果返回值为null，则可以简化
                return StdExpr.Return(_return, StdExpr.Constant(new DynamicTypedef(null)));
            }
            else
            {
                // 函数返回值必须是 DynamicType，考虑 value 类型可能不一致的情况，使用 tmp 变量进行封装
                StdExprs.ParameterExpression tmp = StdExpr.Variable(anyType, "_returnValTmp_");
                StdExpr returnStat = StdExpr.Block(
                    new[] { tmp },
                    DynamicTypedef.SetValueExpr_Smart(tmp, value),
                    StdExpr.Return(_return, tmp)
                );
                return returnStat;
            }            
        }
        // 内嵌打印方法
        private StdExpr printStatement()
        {
            StdExpr value = expression();
            consume(TokenType.SEMICOLON, "Expect ';' after value.");

            var printMethod =
                typeof(Console).GetMethod("WriteLine", new Type[] { typeof(object) }) ??
                throw new ParseError(
                    "Can not find Console.WriteLine method, " +
                    "check if NyaLang.Core is running in correct environment.");

            // Console.WriteLine(value);
            return StdExpr.Call(
                null,
                printMethod,
                StdExpr.Convert(value, typeof(object))
                );
        }

        // -----------------------------------------------------------------------
        // expression     → assignment  
        //                 | condition ;
        private StdExpr expression()
        {
            var expr = assignment();
            if (match(TokenType.QUESTION)) // 三目算符
            {
                expr = condition(expr);
            }
            return expr;
        }
        // condition      → expression "?" expression ":" expression ;
        private StdExpr condition(StdExpr testExpr)
        {
            /* 三目条件表达式：
             *     testExpr ? ifTrue : ifFalse
             */

            // 条件为真/假时的值表达式
            var ifTrue = expression();
            consume(TokenType.COLON, "Expect ':' in condition expression.");
            var ifFalse = expression();

            // 创建一个临时变量储存条件表达式的返回值
            var conditionReturnTmp = StdExpr.Variable(anyType, "_conditionReturnTmp_");
            // 考虑到 ifTrue / ifFalse 表达式可能不返回值
            // （例如在本代码中经常 ifFalse 后面跟 throw）
            // 因此对于不返回的情况，返回 null
            var ifTrueReturn = ifTrue.Type == typeof(void) ?
                DynamicTypedef.SetValueExpr_Smart(conditionReturnTmp, StdExpr.Constant(null)) :
                DynamicTypedef.SetValueExpr_Smart(conditionReturnTmp, ifTrue);
            var ifFalseReturn = ifFalse.Type == typeof(void) ?
                DynamicTypedef.SetValueExpr_Smart(conditionReturnTmp, StdExpr.Constant(null)) :
                DynamicTypedef.SetValueExpr_Smart(conditionReturnTmp, ifFalse);
            // 包装到 if - else
            var conditionBlock = StdExpr.Block(
                new StdExprs.ParameterExpression[] { conditionReturnTmp },
                StdExpr.IfThenElse(
                    StdExpr.IsTrue(testExpr),
                    ifTrueReturn,
                    ifFalseReturn),
                conditionReturnTmp
                );

            return conditionBlock;
        }
        // assignment     → IDENTIFIER ( "=" | "+=" | "-=" | "*=" | "/=" | "%=" ) assignment
        //                 | logic_xor ;
        private StdExpr assignment()
        {
            StdExpr expr = logic_xor();

            if (match(TokenType.EQUAL))
            {
                Token equals = previous();
                StdExpr value = assignment();

                // 只有变量和索引能够被赋值
                if (expr is StdExprs.ParameterExpression)
                    return DynamicTypedef.SetValueExpr_Smart(expr, value);
                else if (expr is StdExprs.IndexExpression)
                    return DynamicTypedef.SetValueExpr_Strong(expr, value);
                    // 对于索引类表达式，由于 get/set 方法保护，需要使用强赋值

                // 变量一定是 dynamicType 类型
                // 由于 DynamicTypedef 重载了数学运算符，因此 +=，-= 等都不需要重新改写，
                // 只有直接赋值语句需要修改

                throw new ParseError(equals, "Invalid assignment target.");
            }
            else if (match(TokenType.ADD_EQL))
            {
                Token plus_equals = previous();
                StdExpr value = assignment();

                // 只有变量和索引能够被赋值
                if (expr is StdExprs.ParameterExpression || expr is StdExprs.IndexExpression)
                    return StdExpr.AddAssign(expr, value);

                throw new ParseError(plus_equals, "Invalid add_assign target.");
            }
            else if (match(TokenType.SUB_EQL))
            {
                Token minus_equals = previous();
                StdExpr value = assignment();

                // 只有变量和索引能够被赋值
                if (expr is StdExprs.ParameterExpression || expr is StdExprs.IndexExpression)
                    return StdExpr.SubtractAssign(expr, value);

                throw new ParseError(minus_equals, "Invalid substruct_assign target.");
            }
            else if (match(TokenType.MUL_EQL))
            {
                Token equals = previous();
                StdExpr value = assignment();

                // 只有变量和索引能够被赋值
                if (expr is StdExprs.ParameterExpression || expr is StdExprs.IndexExpression)
                    return StdExpr.MultiplyAssign(expr, value);

                throw new ParseError(equals, "Invalid multipy_assign target.");
            }
            else if (match(TokenType.DIV_EQL))
            {
                Token equals = previous();
                StdExpr value = assignment();

                // 只有变量和索引能够被赋值
                if (expr is StdExprs.ParameterExpression || expr is StdExprs.IndexExpression)
                    return StdExpr.DivideAssign(expr, value);

                throw new ParseError(equals, "Invalid divide_assign target.");
            }
            else if (match(TokenType.MOD_EQL))
            {
                Token equals = previous();
                StdExpr value = assignment();

                // 只有变量和索引能够被赋值
                if (expr is StdExprs.ParameterExpression || expr is StdExprs.IndexExpression)
                    return StdExpr.ModuloAssign(expr, value);

                throw new ParseError(equals, "Invalid modulo_assign target.");
            }

            return expr;
        }
        // logic_xor      → logic_or ( "xor" | "^^" logic_or )* ;
        private StdExpr logic_xor()
        {
            StdExpr expr = logic_or();

            while (match(TokenType.XOR))
            {
                StdExpr right = logic_or();
                // 糖
                //      Not 是位运算，用 IsFalse 替代
                // 在逻辑运算中使用 IsTrue 包装避免出现类型不匹配问题
                expr = StdExpr.OrElse(
                    StdExpr.AndAlso(StdExpr.IsFalse(expr), StdExpr.IsTrue(right)),
                    StdExpr.AndAlso(StdExpr.IsTrue(expr), StdExpr.IsFalse(right))
                    );
            }

            return expr;
        }
        // logic_or       → logic_and ( "or" | "||" logic_and )* ;
        private StdExpr logic_or()
        {
            StdExpr expr = logic_and();

            while (match(TokenType.OR))
            {
                // Token oprtr = previous(); // 如果需要定位错误，则传入这个token
                StdExpr right = logic_and();
                // StdExpr.Or 是位运算，OrElse 才是逻辑运算
                // 在逻辑运算中使用 IsTrue 包装避免出现类型不匹配问题
                expr = StdExpr.OrElse(StdExpr.IsTrue(expr), StdExpr.IsTrue(right));
            }

            return expr;
        }
        // logic_and      → equality ( "and" | "&&" equality )* ;
        private StdExpr logic_and()
        {
            StdExpr expr = equality();

            while (match(TokenType.AND))
            {
                // Token oprtr = previous(); // 如果需要定位错误，则传入这个token
                StdExpr right = equality();
                // StdExpr.And 是位运算，AndAlso 才是逻辑运算
                // 在逻辑运算中使用 IsTrue 包装避免出现类型不匹配问题
                expr = StdExpr.AndAlso(StdExpr.IsTrue(expr), StdExpr.IsTrue(right));
            }

            return expr;
        }
        // equality       → comparison ( ( "!=" | "==" ) comparison )* ;
        private StdExpr equality()
        {
            StdExpr expr = comparison();

            while (match(TokenType.BANG_EQUAL, TokenType.EQUAL_EQUAL))
            {
                Token oprtr = previous(); // 如果需要定位错误，则传入这个token
                StdExpr right = comparison();

                if(oprtr.type == TokenType.EQUAL_EQUAL)
                    expr = StdExpr.Equal(expr, right);
                else
                    expr = StdExpr.NotEqual(expr, right);
            }
            return expr;
        }
        // comparison     → bit_logic ( ( ">" | ">=" | "<" | "<=" ) bit_logic )* 
        private StdExpr comparison()
        {
            StdExpr expr = bit_logic();

            while (match(TokenType.GREATER, TokenType.GREATER_EQUAL, TokenType.LESS, TokenType.LESS_EQUAL))
            {
                Token oprtr = previous();
                StdExpr right = bit_logic();

                if (oprtr.type == TokenType.GREATER)
                    expr = StdExpr.GreaterThan(expr, right);
                else if (oprtr.type == TokenType.GREATER_EQUAL)
                    expr = StdExpr.GreaterThanOrEqual(expr, right);
                else if (oprtr.type == TokenType.LESS)
                    expr = StdExpr.LessThan(expr, right);
                else if (oprtr.type == TokenType.LESS_EQUAL)
                    expr = StdExpr.LessThanOrEqual(expr, right);
            }

            return expr;
        }
        // bit_logic      → bit_shift ( ( "&" | "|" | "^" ) bit_shift )*
        private StdExpr bit_logic()
        {
            StdExpr expr = bit_shift();

            while (match(TokenType.BIT_AND, TokenType.BIT_OR, TokenType.BIT_XOR))
            {
                Token oprtr = previous();
                StdExpr right = bit_shift();
                if (oprtr.type == TokenType.BIT_AND)
                    expr = StdExpr.And(expr, right);
                else if (oprtr.type == TokenType.BIT_OR)
                    expr = StdExpr.Or(expr, right);
                else
                    expr = StdExpr.ExclusiveOr(expr, right);
            }

            return expr;
        }
        // bit_logic      → term ( ( "<<" | ">>" ) term )*
        private StdExpr bit_shift()
        {
            StdExpr expr = term();

            while (match(TokenType.LSHIFT, TokenType.RSHIFT))
            {
                Token oprtr = previous();
                StdExpr right = term();
                if (oprtr.type == TokenType.LSHIFT)
                    expr = StdExpr.LeftShift(expr, right);
                else
                    expr = StdExpr.RightShift(expr, right);
            }

            return expr;
        }
        // term           → factor ( ( "-" | "+" ) factor )* ;
        //     按照优先级顺序，先做加减法（递归下降分析 = 压栈，故低优先级先分析）
        private StdExpr term()
        {
            StdExpr expr = factor();

            while (match(TokenType.MINUS, TokenType.PLUS))
            {
                Token oprtr = previous();
                StdExpr right = factor();
                if(oprtr.type == TokenType.PLUS)
                {
                    // 加法对字符串存在重载
                    if(expr.Type == typeof(string))
                    {
                        right = StdExpr.Call(right, "ToString", null, null);
                        var concatMethod = typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(string) });                        
                        expr = StdExpr.Add(expr, right, concatMethod);
                    }
                    else
                        expr = StdExpr.Add(expr, right);
                }                    
                else
                    expr = StdExpr.Subtract(expr, right);
            }

            return expr;
        }
        // factor         → unary ( ( "/" | "*" | "%" ) unary )* ;
        private StdExpr factor()
        {
            StdExpr expr = unary();

            while (match(TokenType.SLASH, TokenType.STAR, TokenType.MOD))
            {
                Token oprtr = previous();
                StdExpr right = unary();
                if (oprtr.type == TokenType.STAR)
                    expr = StdExpr.Multiply(expr, right);
                else if (oprtr.type == TokenType.SLASH)
                    expr = StdExpr.Divide(expr, right);
                else
                    expr = StdExpr.Modulo(expr, right);
            }
            return expr;
        }
        // unary          → ( "!" | "-" | "~" | "++" | "--" | "not" ) unary
        //                 | callable ;    
        private StdExpr unary()
        {
            if (match(TokenType.BANG))
            {
                //Token oprtr = previous(); // 如果需要定位错误，则传入这个token
                StdExpr right = unary();
                return StdExpr.IsFalse(right);
            }
            else if (match(TokenType.MINUS))
            {
                StdExpr right = unary();
                return StdExpr.Negate(right);
            }
            else if (match(TokenType.BIT_NOT))
            {
                StdExpr right = unary();
                return StdExpr.Not(right);
            }
            /* 
             * StdExpr.Increment / Decrement，不会更改传递给它的对象的值
             * 所以改用 StdExpr.PreIncrementAssign
             */
            else if (match(TokenType.SELF_ADD))
            {
                Token oprtr = previous();
                StdExpr right = unary();

                // 只有变量和索引能够被赋值
                if (right is StdExprs.ParameterExpression || right is StdExprs.IndexExpression)
                    return StdExpr.PreIncrementAssign(right);

                throw new ParseError(oprtr, "Invalid increment target.");
            }
            else if (match(TokenType.SELF_DEC))
            {
                Token oprtr = previous();
                StdExpr right = unary();

                // 只有变量和索引能够被赋值
                if (right is StdExprs.ParameterExpression || right is StdExprs.IndexExpression)
                    return StdExpr.PreDecrementAssign(right);

                throw new ParseError(oprtr, "Invalid decrement target.");
            }
            
            return callable();
        }
        //  callable      → static_invoke? ( invoke | index | field )* 
        //                 | tuple
        private StdExpr callable()
        {
            // callable 包含了四种最高级运算符操作：
            //      函数调用  tuple( expression )
            //      数组索引  tuple[ expression ]
            //      成员索引  tuple.IDENTIFIER
            //      静态方法  $IDENTIFIER( expression )

            // 三种操作都需要一个左操作数
            StdExpr expr;
            // 其中静态方法(宿主方法)不可能是其它方法的返回值
            // 因此只可能在头部出现一次
            if (match(TokenType.STATIC_SYM))
            {
                Token methodToken =
                    consume(TokenType.IDENTIFIER, "Expect static method name.");
                expr = static_invoke(methodToken);
            }
            else
            {
                // 如果不是静态方法，那么左操作数是 tuple      
                expr = tuple();
            }

            // 循环查找三种运算符
            /* 为什么要把查找放在while里：
             *   解决连续出现的情况，以 invoke 为例，
             *      一个函数的返回值可能是另一个函数名，例如
             *      当  fun1(){ return fun2; }
             *          fun2(){ do anything; }
             *      此时就会有如
             *          fun1()();
             *      的语句，等价于
             *          fun2();
             *      因为finishCall里面会解析到右括号，因此
             *      如果finishCall解析完成之后后面还有一个左括号，
             *      就应该把finishCall的结果作为函数名继续解析 
             */
            while (check(
                TokenType.LEFT_PAREN,  // invoke
                TokenType.LEFT_SQRBRA, // index
                TokenType.DOT))        // field
            {
                // 所有标记请留到子函数里吞，这里一律用 check
                if (check(TokenType.LEFT_PAREN))
                    expr = invoke(expr);
                if (check(TokenType.LEFT_SQRBRA))
                    expr = index(expr);
                if (check(TokenType.DOT))
                    expr = field(expr);
            }

            return expr;
        }
        // invoke         → tuple "(" expression? ( "," expression )* ")" ;
        private StdExpr invoke(StdExpr tupleExpr)
        {
            // 从callable传入的表达式，即调用的左操作数
            StdExpr expr = tupleExpr;

            // 当看到(，我们就解析调用表达式，
            // 并使用之前解析出的表达式作为被调用者
            if (match(TokenType.LEFT_PAREN))
            {
                // 查找所有传入的参数，然后创建 Expr.Call 对象
                List<StdExpr> arguments = new();
                if (!check(TokenType.RIGHT_PAREN))
                {
                    do
                    {
                        arguments.Add(expression());

                    } while (match(TokenType.COMMA));
                }
                consume(TokenType.RIGHT_PAREN, "Expect ')' after arguments.");

                // 用 invoke 调用 lambda 表达式
                //  return StdExpr.Invoke(callee, arguments);
                //  为啥不能直接 invoke，而要使用类型方法：
                //      只有运行时才知道委托类型，此处无法类型转化      
                expr = DynamicTypedef.InvokeExpr(expr, arguments);
            }

            return expr;
        }
        // index          → tuple "[" expression ( "," expression )* "]" ;
        private StdExpr index(StdExpr tupleExpr)
        {
            /* 
             *  index 有两种写法：
             *      正常的：array[1][2][3]
             *      糖：    array[1, 2, 3]
             *      混沌恶：array[1, 2][3]
             *     主要是写同一个方括号里分析器检索方便
             */
            // 从callable传入的表达式，即调用的左操作数
            StdExpr expr = tupleExpr;

            // 如果有左方括号，则取索引
            if (match(TokenType.LEFT_SQRBRA)) 
            {
                // 如果没有参数，跟着就是一个右括号，那么丢出去，因为索引至少要有一个参数
                if (check(TokenType.RIGHT_SQRBRA))
                    throw new ParseError(advance(), "Too less argument for array index.");

                do
                {
                    StdExpr indexExpr = expression();
                    // 因为通常数字类型有可能是 double（尽管存的是整数），无法用作索引
                    //     所以即使是常量也要做类型转化
                    StdExpr int32indexExpr = indexExpr.Type == anyType ?
                        StdExpr.Convert(indexExpr, typeof(int)):                     // 变量的情况
                        // 事实上我也不知道为什么下面语句会报错
                        //DynamicTypedef.GetValueExpr_Smart(indexExpr, typeof(int)) :  
                        (indexExpr.Type == typeof(int) ?                             // 常量的情况
                            indexExpr :
                            StdExpr.Convert(indexExpr, typeof(int)));

                    if (expr.Type == anyType) // 左值是变量
                    {
                        // 交给内置方法
                        expr = DynamicTypedef.AccessExpr(expr, int32indexExpr);
                    }
                    else if (expr.Type == typeof(string)) // 左值是字符串
                    {
                        // 反射 ToCharArray 方法
                        var toCharArrayMethod =
                            typeof(string).GetMethod("ToCharArray", Array.Empty<Type>()) ??
                            throw new ParseError(
                                "Can not find String.ToCharArray method, " +
                                "check if NyaLang.Core is running in correct environment.");
                        // 获取 char 对象
                        expr = StdExpr.ArrayAccess(
                            StdExpr.Call(expr, toCharArrayMethod),
                            int32indexExpr);
                        // 但是 char 很麻烦（等同于uint16），我们更喜欢string类型
                        // 反射 ToString 方法
                        var toStringMethod =
                            typeof(char).GetMethod("ToString", Array.Empty<Type>()) ??
                            throw new ParseError(
                                "Can not find Char.ToString method, " +
                                "check if NyaLang.Core is running in correct environment.");
                        // 转为 string 返回出去
                        expr = StdExpr.Call(expr, toStringMethod);
                    }
                    else // 不是可索引的对象
                        throw new ParseError(peek(), "Not an accessable array.");

                } while (match(TokenType.COMMA));

                // 吞掉右方括号
                consume(TokenType.RIGHT_SQRBRA, "Expect ']' for array index.");
            }

            return expr;
        }
        // field          → tuple "." IDENTIFIER
        //                 | tuple "." "@" expression ;
        private StdExpr field(StdExpr tupleExpr)
        {
            // 从callable传入的表达式，即调用的左操作数
            StdExpr expr = tupleExpr;

            /*  支持两种字段提取方式： 
             *      1. Container.FieldName;      // 直接法
             *      2. Container.@("FieldName"); // 反射法
             *  后者原理是Container实际上是一个字典，
             *  只要有正确的字符串key就可以查找
             */

            // 如果有"."符号，则提取字段
            if (check(TokenType.DOT))
            {
                Token dot = advance(); // 吞符号，且如果有错误可以传出去
                // 判断左值是否合法
                if (expr.Type != anyType)
                    throw new ParseError(dot, $"Type of [{expr.Type}] doesn's have any field.");

                // field 并不是真正的类内字段，而是字典索引
                // 因此我们要先找到索引键
                StdExpr fieldNameExpr; // 索引键可能是表达式，因此这样储存之

                // 判断使用了哪种方法进行提取
                if (check(TokenType.IDENTIFIER)) // 直接法
                {       
                    Token fieldName = advance(); // 获取字段名 
                    fieldNameExpr = StdExpr.Constant(fieldName.lexeme); // 索引键是一个常量
                }
                else if (check(TokenType.AT_SYM)) // 反射法
                {
                    advance(); // 吞掉开头的"@"标记
                    StdExpr refExpr = expression(); // 获取跟随的表达式

                    // 键值必须是字符串类型，如果不是，转化之
                    fieldNameExpr = refExpr.Type == anyType ?
                        DynamicTypedef.GetValueExpr_Smart(refExpr, typeof(string)) :
                        refExpr.Type == typeof(string) ?
                            refExpr :
                            StdExpr.Convert(refExpr, typeof(string));
                }
                else // 不是可作键值的对象
                    throw new ParseError(peek(), "Expect field name.");

                // 现在左右值都合法了
                expr = DynamicTypedef.FieldExpr(expr, fieldNameExpr);
            }

            return expr;
        }
        // static_invoke  → "$" IDENTIFIER "(" expression? ( "," expression )* ")" ;
        private StdExpr static_invoke(Token methodName)
        {
            // 静态方法（native 方法）调用

            // 从 Runtime 接口获取方法
            System.Reflection.MethodInfo methodInfo =
                Runtime.RuntimeInterface.GetMethodViaName(methodName.lexeme) ??
                throw new ParseError(methodName, "Invalid static method.");

            consume(TokenType.LEFT_PAREN, "Expect '(' after static method name.");

            // 查找所有传入的参数
            List<StdExpr> arguments = new();
            if (!check(TokenType.RIGHT_PAREN))
            {
                do
                {
                    StdExpr arg = expression();
                    // 宿主接口要求参数类型必须是 DynamicType
                    // 如果不是，转化之
                    if(arg.Type != anyType)
                    {
                        var tmpV = StdExpr.Variable(anyType, "_StaticArgTmp_");
                        arg = StdExpr.Block(
                            new StdExprs.ParameterExpression[] { tmpV },
                            DynamicTypedef.SetValueExpr_Smart(tmpV, arg),
                            tmpV);
                    }
                    arguments.Add(arg);

                } while (match(TokenType.COMMA));
            }
            consume(TokenType.RIGHT_PAREN, "Expect ')' after arguments.");

            // 生成一个静态的调用语句
            return StdExpr.Call(null, methodInfo, arguments);
        }
        // tuple          → "[" expression? ( "," expression )* "]"
        //                |  "{" ( IDENTIFIER ":" expression )? ( "," IDENTIFIER ":" expression )* "}" 
        //                |  lambdaFunc
        //                |  primary ;
        private StdExpr tuple()
        {
            // 都将到 primary 这一步了还遇到了<del>大括号</del>，
            // 显然这不是block，而是元组
            //     p.s. 屈服于js的代码提示，把元组标记改为方括号
            //      再p.s. 因此我们可以做出词典对象了
            const TokenType tupleLeftMark = TokenType.LEFT_SQRBRA;
            const TokenType tupleRightMark = TokenType.RIGHT_SQRBRA;
            // 下面是左右大括号定义<del>词典</del>容器
            const TokenType cntnrLeftMark = TokenType.LEFT_BRACE;
            const TokenType cntnrRightMark = TokenType.RIGHT_BRACE;

            // 元组
            if (match(tupleLeftMark))
            {
                List<StdExpr> initElements = new();
                // 参考 call 里面找参数，一样的方法
                if (!check(tupleRightMark))
                {
                    do
                    {
                        // 将 DynamicType 类型的变量做成数组实现元组
                        // 因此如果表达式值不是 DynamicType 则需要包装
                        StdExpr exprPara = expression();
                        if(exprPara.Type != anyType)
                        {
                            var tmp = StdExpr.Variable(anyType, "_TupleElementTmp_");
                            var tmpBlock = StdExpr.Block(
                                new StdExprs.ParameterExpression[] { tmp },
                                DynamicTypedef.SetValueExpr_Smart(tmp, exprPara),
                                tmp);
                            initElements.Add(tmpBlock);
                        }
                        else
                        {
                            initElements.Add(exprPara);
                        }
                    } while (match(TokenType.COMMA));
                }
                // 吞一个结束括号
                consume(tupleRightMark, "Expect ']' after tuple defination.");

                // 得到数组表达式
                var arrayExpr = StdExpr.NewArrayInit(anyType, initElements);

                // 包装数组
                var tmpTuple = StdExpr.Variable(anyType, "_TupleArrayTmp_");
                return StdExpr.Block(
                    new StdExprs.ParameterExpression[] { tmpTuple },
                    DynamicTypedef.SetValueExpr_Smart(tmpTuple, arrayExpr),
                    tmpTuple);
            }
            // 容器
            else if (match(cntnrLeftMark))
            {
                
                List<StdExpr> initFields = new();                         // 储存字段初始化器的列表
                List<string> fieldNames = new();                          // 储存字段名的列表

                // 容器两个大括号之间应当是一个子命名空间，
                // 因为考虑到容器内可能定义函数，需要让这些函数能够访问容器内的字段，
                // 故要在该空间中储存一个访问 "this" 的变量 -----------------------------------------------
                runIntoSubEnv();

                // 创建新的词典对象
                var tmpDict = StdExpr.Variable(typeof(Dictionary<string, DynamicTypedef>), "_ContainerDictTmp_");
                // 词典的包装对象
                var dictBox = StdExpr.Variable(anyType, "_ContainerDynamicTmp_");
                presentEnv.Define("this", dictBox); // 定义到空间

                if (!check(cntnrRightMark))
                {
                    do
                    {
                        // 字段必须有名称，可以是标识符定义(js方式)或字符串定义(json方式)
                        if(check(TokenType.IDENTIFIER)) // 标识符
                            fieldNames.Add(advance().lexeme);
                        else if (check(TokenType.STRING)) // 字符串
                        {
                            Token strFieldName = advance();
                            fieldNames.Add(
                                strFieldName.literal?.ToString() ??
                                throw new ParseError(strFieldName, "A token was taken as string but with null value.")); // 正常情况不会有这问题
                        }
                        else
                        {
                            throw new ParseError(advance(), "Field name must be declared.");
                        }

                        // 吞一个冒号
                        consume(TokenType.COLON, "Expect ':' after field name.");

                        /*   <del> 考虑到 js 中可以直接将容器字段的内容设置为函数，
                         *   因此这里添加在容器中直接声明函数的支持，
                         *   尽管这样 cfg 会显得有点奇怪XD </del>
                         *   
                         *   添加了 lambda 函数表达式，现在无需区分函数成员的问题了
                         */

                        // 初始化器
                        StdExpr fieldIniter;

                        fieldIniter = expression();
                        // 如果表达式值不是 DynamicType 则需要包装
                        if (fieldIniter.Type != anyType)
                        {
                            var tmp = StdExpr.Variable(anyType, "_ContainerFieldTmp_");
                            var tmpBlock = StdExpr.Block(
                                new StdExprs.ParameterExpression[] { tmp },
                                DynamicTypedef.SetValueExpr_Smart(tmp, fieldIniter),
                                tmp);
                            initFields.Add(tmpBlock);
                        }
                        else
                        {
                            initFields.Add(fieldIniter);
                        }
                       
                    } while (match(TokenType.COMMA));                    
                }

                // 退出子环境 -------------------------------------------------------------------------------
                runIntoBaseEnv();

                // 吞一个结束括号
                consume(cntnrRightMark, "Expect '}' after container defination.");

                // 获取 Add 方法
                System.Reflection.MethodInfo addMethod = typeof(Dictionary<string, DynamicTypedef>).GetMethod("Add")??
                    throw new ApplicationException("Cannot find Dict.Add.");    

                // 为每个初始化field写一句Add
                List<StdExpr> addList = new();
                for (int i = 0; i < initFields.Count; i++)
                    addList.Add(
                        StdExpr.Call(
                            tmpDict,
                            addMethod,
                            StdExpr.Constant(fieldNames[i]),
                            initFields[i]));
                var addBlock = StdExpr.Block(addList);

                // 包装对象
                return StdExpr.Block(
                    new StdExprs.ParameterExpression[] {tmpDict, dictBox},
                    StdExpr.Assign(tmpDict, StdExpr.New(typeof(Dictionary<string, DynamicTypedef>))),
                    addBlock,
                    DynamicTypedef.SetValueExpr_Smart(dictBox, tmpDict),
                    dictBox);
            }
            // Lambda 函数
            else if (check(TokenType.FUN)) return lambdaFunc(advance());
            else
                return primary();
        }
        // lambdaFunc     → "fun" IDENTIFIER? "(" parameters? ")" ( block | "=>" expression ) ;
        private StdExpr lambdaFunc(Token declareToken)
        {
            /*  Lambda Function: 匿名函数
             *      允许以 function () { body } 的方式声明函数，
             *      
             *      参数 declareToken 用于反馈错误信息
             */

            // 匿名函数不应该有函数名，或者应该使用匿名 "_"，
            // 如果出现了函数名，进行检查
            string fun_name = "self";
            if (check(TokenType.IDENTIFIER))
            {
                Token unexpectName = advance();
                if (unexpectName.lexeme != "_")
                    throw new ParseError(
                        unexpectName,
                        "Lambda function should not be named, or should be named as anonymous '_'; " +
                        "To recurring lambda function, use 'self()' for nameless, or '_()' for anonymous.");
                fun_name = unexpectName.lexeme;
            }

            // 创建一个子环境来储存参数名称（主要是防止参数和外环境变量同名的情况）------------------------------------
            runIntoSubEnv();
            //     匿名函数的函数名变量仅限函数内部使用，不应该暴露给父环境，故应该在此环境中定义
            //     注册函数名，这样在函数递归时就不需要把函数自己作为参数传进去了
            var funName = StdExpr.Variable(anyType, $"lambda_func[{fun_name}]");
            presentEnv.Define(fun_name, funName); // 定义在函数内部的环境【相关：普通函数的函数名定义在闭包环境】

            // 置于函数尾部，具有 DynamicType 类型值的标签
            StdExprs.LabelTarget _return = StdExpr.Label(anyType, "_return");
            // 将标签置入环境
            presentEnv.Define("return", _return);

            consume(TokenType.LEFT_PAREN, $"Expect '(' after function name.");    // 左括号
            List<StdExprs.ParameterExpression> parameters = new();              // 参数列表

            // 外部的if语句用于处理零参数的情况，
            // 内部的while会循环解析参数，只要能找到分隔参数的逗号。
            // 其结果是包含每个参数名称的标记列表
            if (!check(TokenType.RIGHT_PAREN))
            {
                do
                {
                    Token para = consume(TokenType.IDENTIFIER, "Expect parameter name.");
                    var paraAddr = StdExpr.Variable(anyType, para.lexeme);
                    presentEnv.Define(para.lexeme, paraAddr);
                    parameters.Add(paraAddr); // 创建lambda表达式时需要将参数地址标签传进去

                } while (match(TokenType.COMMA));
            }
            consume(TokenType.RIGHT_PAREN, "Expect ')' after parameters.");


            /* lambda 函数允许以块形式 function () {body}
             *              或语句形式 function () => body;
             *        进行声明：
             */
            StdExpr funBody;
            if (match(TokenType.LAMBDA_POINTER)) // 语句 lambda
            {
                funBody = expression();
                // 语句lambda默认最后一条语句的值用于返回
                if (funBody.Type != typeof(void))
                {
                    var lambdaReturnTmp = StdExpr.Variable(anyType, "_lambdaReturnTmp_");
                    funBody = StdExpr.Block(
                        new StdExprs.ParameterExpression[] { lambdaReturnTmp },
                        DynamicTypedef.SetValueExpr_Smart(lambdaReturnTmp, funBody),
                        StdExpr.Return(_return, lambdaReturnTmp));
                }
            }
            else // 块 lambda
            {
                consume(TokenType.LEFT_BRACE, "Expect '{' before function body.");
                funBody = block();
            }

            // 在函数体末尾拼接结束语句
            funBody = StdExpr.Block(
                funBody,
                // 函数结尾标签，如果函数中没有返回，则返回null
                StdExpr.Label(_return, StdExpr.Constant(new DynamicTypedef(null)))
                );

            // 退出函数体子环境 ---------------------------------------------------------------------------------------
            runIntoBaseEnv();

            // 创建lambda函数
            StdExpr lambdaFun;
            switch (parameters.Count)
            {
                case 0:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef>>(funBody, parameters);
                    break;
                case 1:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 2:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 3:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 4:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 5:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 6:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 7:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 8:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 9:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 10:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 11:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 12:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 13:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 14:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 15:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                case 16:
                    lambdaFun = StdExpr.Lambda<Func<DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef, DynamicTypedef>>(funBody, parameters);
                    break;
                default:
                    throw new ParseError(declareToken, "Lambda function has too many ( > 16 ) arguements");
            }

            // 将lambda函数赋值给匿名临时变量，然后将该变量值返回出去
            return StdExpr.Block(
                new StdExprs.ParameterExpression[] { funName }, // funName 在此处封闭
                DynamicTypedef.SetValueExpr(funName, lambdaFun),
                funName);
            /*   我们在完成定义后将函数名变量返回出去，
             *   这样就使得以下声明可以实现：
             *   
             *      var someContainer = {
             *          value: 1,
             *          set_method: function _(val){
             *                          vale = val;
             *                      }
             *      };
             */
        }
        // 递归结束位置：基本表达式  
        // primary        → NUMBER | STRING | "true" | "false" | "nil"
        //                |  "(" expression ")" 
        //                |  IDENTIFIER ;
        private StdExpr primary()
        {
            // 常量值
            if      (match(TokenType.FALSE))                    return StdExpr.Constant(false);
            else if (match(TokenType.TRUE))                     return StdExpr.Constant(true);
            else if (match(TokenType.NULL))                     return StdExpr.Constant(null);
            else if (match(TokenType.NUMBER, TokenType.STRING)) return StdExpr.Constant(previous().literal);
            // 标识符
            else if (match(TokenType.IDENTIFIER))
            {
                Token name = previous();
                StdExprs.ParameterExpression? addr = presentEnv.GetValIdf(name);
                if (addr == null)
                    throw new ParseError(name, $"Cannot use variable {name.lexeme} before Declaration");
                return addr;
            }
            // 括号表达式
            else if (match(TokenType.LEFT_PAREN))
            {
                StdExpr expr = expression();
                consume(TokenType.RIGHT_PAREN, "Expect ')' after expression.");
                return expr;  
            }
            // this
            else if (match(TokenType.THIS))
            {
                Token name = previous();
                // this 实际上是储存在命名空间里名为 "this" 的变量
                StdExprs.ParameterExpression? addr = presentEnv.GetValIdf(name);
                if (addr == null)
                    throw new ParseError(name, "Unexpected keyword 'this'.");
                return addr;
            }
            // 解析错误
            else
                throw new ParseError(peek(), "Expect expression.");
        }

        // ---- 恐慌模式错误恢复 ----------------------------
        #region 恐慌模式
        // 它和 match()方法类似，检查下一个标记是否是预期的类型。如果是，它就会消费该标记，一切都很顺利。如果是其它的标记，那么我们就遇到了错误
        //    说白了，就是用来匹配左右括号
        private Token consume(TokenType type, string message)
        {
            if (check(type)) return advance();
            else throw new ParseError(peek(), message);
        }
        // 该方法会不断丢弃标记，直到它发现一个语句的边界
        //    在捕获一个ParseError后，我们会调用该方法，然后我们就有望回到同步状态。
        //    当它工作顺利时，我们就已经丢弃了无论如何都可能会引起级联错误的语法标记，现在我们可以从下一条语句开始解析文件的其余部分。
        private void synchronize()
        {
            advance();
            while (!isAtEnd())
            {
                if (previous().type == TokenType.SEMICOLON) return;
                switch (peek().type)
                {
                    case TokenType.CLASS:
                    case TokenType.FUN:
                    case TokenType.VAR:
                    case TokenType.FOR:
                    case TokenType.IF:
                    case TokenType.WHILE:
                    case TokenType.PRINT:
                    case TokenType.RETURN:
                        return;
                }
                advance();
            }
        }
        #endregion

        // ---- 内部方法 ---------------------------
        #region 内部方法
        // 这个检查会判断当前的token是否属于给定的类型之一。如果是，则消费该token并返回true；否则，就返回false并保留当前token
        private bool match(params TokenType[] types)
        {
            if (check(types))
            {
                advance();
                return true;
            }
            return false;
        }
        // 如果当前token属于给定类型之一，则check()方法返回true。与match()不同的是，它从不消费token，只是读取
        private bool check(params TokenType[] types)
        {
            if (isAtEnd()) return false; // 如果EOF了那肯定不是
            foreach (TokenType type in types)
            {
                if(peek().type == type)
                    return true;
            }            
            return false;
        }
        // advance()方法会消费当前的token并返回它，类似于扫描器中对应方法处理字符的方式
        private Token advance()
        {
            if (!isAtEnd()) current++;
            return previous();
        }
        // isAtEnd()检查我们是否处理完了待解析的标记。
        private bool isAtEnd()
            => (peek().type == TokenType.EOF);
        // peek()方法返回我们还未消费的当前标记
        private Token peek()
            => tokens[current];
        // previous()会返回最近消费的标记
        private Token previous()
            => tokens[current - 1];
        #endregion

    }
}
