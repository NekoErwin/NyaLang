/* 
 *   Scanner：扫描器类，负责将字符串转化为 Token 数组
 *   
 *      有的时候它也被称作 Lexer（词法分析器）
 *      大概是因为有个 x 在中间显得比较酷炫
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NyaLang.Core
{
    /// <summary>
    /// 扫描器：将字符串转为 Token 列表
    ///     虽然比较简陋但又不是不能用
    ///     只在编译阶段运行一次的东西不那么影响性能
    /// </summary>
    public class Scanner
    {
        private readonly string source;
        private List<Token> tokens = new List<Token>();
        private int start = 0;
        private int current = 0;
        private int line = 1;

        /// <summary>
        /// 传入字符串来新建扫描器
        /// </summary>
        /// <param name="source">可以是一行命令，也可以是整个文档内容</param>
        public Scanner(string source)
        {
            this.source = source;
        }
        /// <summary>
        /// 对扫描器储存的文件进行扫描
        /// </summary>
        /// <returns>扫描结果</returns>
        public List<Token> Execute()
        {
            while (!isAtEnd())
            {
                // We are at the beginning of the next lexeme.
                start = current;
                scanToken();
            }

            tokens.Add(new Token(TokenType.EOF, "", null, line));
            return tokens;
        }

        private void scanToken()
        {
            char c = advance();
            switch (c)
            {
                // 符号
                case '(': addToken(TokenType.LEFT_PAREN); break;
                case ')': addToken(TokenType.RIGHT_PAREN); break;
                case '{': addToken(TokenType.LEFT_BRACE); break;
                case '}': addToken(TokenType.RIGHT_BRACE); break;
                case '[': addToken(TokenType.LEFT_SQRBRA); break;
                case ']': addToken(TokenType.RIGHT_SQRBRA); break;
                case ',': addToken(TokenType.COMMA); break;
                case '.': addToken(TokenType.DOT); break;
                case ';': addToken(TokenType.SEMICOLON); break;
                case ':': addToken(TokenType.COLON); break;
                case '?': addToken(TokenType.QUESTION); break;

                case 'λ': addToken(TokenType.FUN); break; // 彩蛋，可以使用 lambda 符号代替 function

                // 五则
                case '+': addToken(match('=') ? TokenType.ADD_EQL : match('+') ? TokenType.SELF_ADD : TokenType.PLUS); break;
                case '-': addToken(match('=') ? TokenType.SUB_EQL : 
                                   match('-') ? TokenType.SELF_DEC : 
                                   match('>') ? TokenType.POINTER :
                                   TokenType.MINUS); break;        
                case '*': addToken(match('=') ? TokenType.MUL_EQL : TokenType.STAR); break;      
                case '/': 
                    if (match('/')) // 短注释
                    {
                        // A comment goes until the end of the line.
                        while (peek() != '\n' && !isAtEnd()) advance();
                    }
                    else if (match('*')) // 长注释
                    {
                        TryToFindEndMark:
                        while (!match('*') && !isAtEnd())
                        {
                            if (peek() == '\n') line++;
                            advance();
                        }                            
                        if (match('/')) 
                            advance();
                        else 
                            goto TryToFindEndMark;
                    }
                    else
                        addToken(match('=') ? TokenType.DIV_EQL : TokenType.SLASH);
                    break;
                case '%': addToken(match('=') ? TokenType.MOD_EQL : TokenType.MOD); break;

                // 逻辑
                case '&': addToken(match('&') ? TokenType.AND : TokenType.BIT_AND); break;
                case '|': addToken(match('|') ? TokenType.OR : TokenType.BIT_OR); break;
                case '^': addToken(match('^') ? TokenType.XOR : TokenType.BIT_XOR); break;
                case '~': addToken(TokenType.BIT_NOT); break;

                // 比较
                case '!': addToken(match('=') ? TokenType.BANG_EQUAL : TokenType.BANG); break;
                case '=': addToken(match('=') ? TokenType.EQUAL_EQUAL : 
                                   match('>') ? TokenType.LAMBDA_POINTER :
                                   TokenType.EQUAL); break;
                case '<': addToken(match('=') ? TokenType.LESS_EQUAL : TokenType.LESS); break;
                case '>': addToken(match('=') ? TokenType.GREATER_EQUAL : TokenType.GREATER); break;

                // 扩展符号
                case '@': addToken(TokenType.AT_SYM); break;
                case '$': addToken(TokenType.STATIC_SYM); break;
                case '#': addToken(TokenType.MACRO_SYM); break;

                // 无意义字符
                case ' ':
                case '\r':
                case '\t':
                    break;
                // 换行
                case '\n':
                    line++;
                    break;

                // 字符串变量
                case '"': _string(); break;

                default:
                    if (isDigit(c)) // 数字变量
                        _number();
                    else if (isAlphaOrNoneASCII(c)) // 标识符
                        _identifier();
                    else
                        Console.WriteLine($"Unexpected character [{c}].");
                    break;
            }
        }

        #region 数据操作
        private bool isAtEnd()
        {
            return current >= source.Length;
        }

        private char advance()
        {
            current++;
            return source[current - 1];
        }

        private void addToken(TokenType? type)
        {
            addToken(type, null);
        }

        private void addToken(TokenType? type, object? literal)
        {
            String text = source.Substring(start, current - start);
            tokens.Add(new Token(type, text, literal, line));
        }

        private bool match(char expected)
        {
            if (isAtEnd()) return false;
            if (source[current] != expected) return false;

            current++;
            return true;
        }

        private char peek()
        {
            if (isAtEnd()) return '\0';
            return source[current];
        }
        private char peekNext()
        {
            if (current + 1 >= source.Length) return '\0';
            return source[current + 1];
        }
        #endregion

        #region 静态方法
        private static bool isDigit(char c)
        {
            return c >= '0' && c <= '9';
        }
        private static bool isNoneASCII(char c)
        {
            // ASCII结束于0x7f，但是0x7f到0xa1之间还有一串控制字符
            ///<see cref="https://unicode-table.com/en/blocks/latin-1-supplement/"/>
            return c >= 0xa1;
        }
        /// <summary>
        /// 判断字符是否为字母,'_',或非ASCII字符；
        /// Nya中，非ASCII字符同样允许用于声明标识符；
        /// 【note】C# 中，char类型为【16位的UTF16编码】
        /// </summary>
        private static bool isAlphaOrNoneASCII(char c)
        {
            return (c >= 'a' && c <= 'z') ||
                   (c >= 'A' && c <= 'Z') ||
                    c == '_' || isNoneASCII(c);
        }
        /// <summary>
        /// 判断字符是否为字母、数字、'_'或非ASCII字符
        /// </summary>
        private static bool isAlphaNumeric(char c)
        {
            return isAlphaOrNoneASCII(c) || isDigit(c);
        }
        #endregion

        #region 对象识别
        private void _string()
        {
            List<char> strVal = new();

            while (peek() != '"' && !isAtEnd())
            {
                char thisChar = advance();
                if (thisChar == '\n') line++;
                // 转义字符
                else if (thisChar == '\\')
                {
                    // 吞掉转义字符标记，获取下一个字符
                    thisChar = advance();

                    switch (thisChar)
                    {
                        case '"': thisChar = '"'; break; // 嵌套双引号
                        case '\\': thisChar = '\\'; break; // 反斜杠
                        case 'n': thisChar = '\n'; break; // 换行
                        case 't': thisChar = '\t'; break; // tab
                        default:
                            Console.WriteLine($"Unexpected character [{thisChar}] after '\\'.");
                            break;
                    }
                }
                strVal.Add(thisChar);
            }

            if (isAtEnd())
            {
                Console.WriteLine("Unterminated string.");
                return;
            }

            // The closing ".
            advance();

            // Trim the surrounding quotes.
            string value = string.Concat(strVal);
            addToken(TokenType.STRING, value);
        }

        private void _number()
        {
            while (isDigit(peek())) advance();

            // Look for a fractional part.
            if (peek() == '.' && isDigit(peekNext()))
            {
                // Consume the "."
                advance();

                while (isDigit(peek())) advance();
            }
            
            addToken(TokenType.NUMBER, double.Parse(source.Substring(start, current - start)));
        }

        private void _identifier()
        {
            while (isAlphaNumeric(peek())) advance();

            String text = source.Substring(start, current - start);
            TokenType type;
            if (!TokenTypeRemap.str2tokenType.TryGetValue(text, out type)) // 不在保留字中，是自定义符号
            {
                type = TokenType.IDENTIFIER;
            }
            addToken(type);
        }
        #endregion
    }
}
