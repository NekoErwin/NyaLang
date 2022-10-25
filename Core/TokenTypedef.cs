/* 
 *   TokenTypedef：定义了脚本语言语素（Token）相关的类型及方法
 *   
 *      Token 是编程语言的最小单元，
 *      例如一个运算符号或者一个常量、一个变量名就是一个Token；
 *      一般将 Token 译为“词组”或“单词”，
 *      但咱觉得用语言学上对语言最小语义单元的称呼“语素”来翻译它更合适
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NyaLang.Core
{
    /// <summary>
    /// 定义了 Nya 脚本使用的基本语素
    /// </summary>
    public enum TokenType
    {
        // 运算符
        LEFT_PAREN, RIGHT_PAREN,   // 左右小括号       ( )
        LEFT_BRACE, RIGHT_BRACE,   // 左右大括号       { }
        LEFT_SQRBRA, RIGHT_SQRBRA, // 左右方括号       [ ]
        COMMA, DOT,                // 逗号，点         , .  
        SEMICOLON, COLON,          // 分号，冒号       ; :
        QUESTION,                  // 问号             ?
        PLUS, MINUS, STAR, SLASH,  // 四则运算符       + - * /        

        ADD_EQL, SUB_EQL,          // 加等，减等       += -=
        MUL_EQL, DIV_EQL,          // 乘等，除等       *= /=
        SELF_ADD, SELF_DEC,        // 自增，自减       ++ --

        /* 
         *   注意 NyaScript 中的自增减是前置运算符，如
         *      正确： ++ i; -- i;
         *      错误： j ++; j --;
         */

        MOD, MOD_EQL,              // 取余，余等       %  %=

        BIT_AND, BIT_OR,           // 位操作           & |
        BIT_XOR, BIT_NOT,          // 位操作           ^ ~
        LSHIFT, RSHIFT,            // 位移运算         << >>
                                   
        AND, OR, XOR,              // 逻辑运算         && || ^^

        BANG,                      // 取非             !
        EQUAL,                     // 赋值             =
        EQUAL_EQUAL, BANG_EQUAL,   // 等于，不等于     == !=
        GREATER, GREATER_EQUAL,    // 大于，大于等于   > >=
        LESS, LESS_EQUAL,          // 小于，小于等于   < <=  

        // 扩展符号
        AT_SYM,                    // 反射             @
        STATIC_SYM,                // 静态方法         $
        MACRO_SYM,                 // 宏               #
        RVS_SLASH,                 // 反斜杠           \
        POINTER,                   // 指针             ->
        LAMBDA_POINTER,            // Lambda指针       =>

        // 字面量 Literals.
        IDENTIFIER,                // 标识符           变量名，函数名 etc
        STRING,                    // 字符串           string
        NUMBER,                    // 数字             double

        // 保留字 Keywords.
        NULL,                      // 空对象        
        TRUE, FALSE,               // 逻辑值
        IF, ELSE, WHILE, FOR,      // 控制流
        SWITCH, CASE, DEFAULT,     // 糖类控制流
        BREAK, CONTINUE,           // 循环控制
        LABEL, GOTO,               //  灵 魂
        VAR, LET,                  // 变量声明
        FUN, RETURN,               // 函数声明和返回
        CLASS, BASE, THIS,         // 对象（CLASS，BASE 未实现）

        // 其它
        PRINT,

        EOF
    }

    /// <summary>
    /// 将字符串映射到 TokenType，或将 TokenType 映射到 StdExprs.ExpressionType
    /// </summary>
    internal static class TokenTypeRemap
    {
        public static readonly Dictionary<string, TokenType> str2tokenType = 
            new Dictionary<string, TokenType>
            {
                {"var",      TokenType.VAR},
                {"let",      TokenType.LET},
                {"const",    TokenType.LET},
                {"function", TokenType.FUN},
                {"fun",      TokenType.FUN},
                {"return",   TokenType.RETURN},

                {"true",   TokenType.TRUE},
                {"false",  TokenType.FALSE},

                {"and",    TokenType.AND},
                {"or",     TokenType.OR},
                {"xor",    TokenType.XOR},
                {"not",    TokenType.BANG},

                {"if",      TokenType.IF},
                {"else",    TokenType.ELSE},
                {"switch",  TokenType.SWITCH},
                {"case",    TokenType.CASE},
                {"default", TokenType.DEFAULT},
                {"for",     TokenType.FOR},
                {"while",   TokenType.WHILE},
                {"break",   TokenType.BREAK},
                {"continue",TokenType.CONTINUE},

                {"label", TokenType.LABEL},
                {"goto", TokenType.GOTO},

                {"null",  TokenType.NULL},
                                
                {"class", TokenType.CLASS},
                {"base",  TokenType.BASE},
                {"this",  TokenType.THIS},

                {"print", TokenType.PRINT},
            };
    }

    /// <summary>
    /// 定义了语素实例的类型
    /// </summary>
    public class Token
    {
        public readonly TokenType? type;  // token 类型
        public readonly string lexeme;    // 字面写法（如"+", "var", "variableName"）
        public readonly object? literal;  // 如果是数字或字符串，在这里储存它的值
        public readonly int line;         // 所在行

        public Token(TokenType? type, string lexeme, object? literal, int line)
        {
            this.type = type;
            this.lexeme = lexeme;
            this.literal = literal;
            this.line = line;
        }

        public override string ToString()
        {
            return $"{type} {lexeme} {literal}";
        }
    }
}
