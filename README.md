*\# This software is still under construction, all features beneath are not guaranteed to work properly.*

*\# 本项目仍在开发中，当前代码并不保证能够实现下述所有功能.*

# NyaLang

<br>

**NyaLang** 是基于 **DLR** 的 NyaScript 运行时  
**NyaScript** 则是为 **ADV 游戏开发** 而设计的一种易于上手的脚本语言

>关于 NyaLang 的文档正在撰写中  
>关于 NyaScript 的语法，请参考下述文档：  

## NyaScript 语法

<br>

大致上，NyaScript 是 **ECMA262脚本** 的一个 **子集**  
因此，如果你熟悉任何一种ECMA262脚本语言的变体，  
例如 *Javascript*, *Typescript*, 或者 krkr 引擎的 *TJS* 等，则可以轻松的开始使用 NyaScript.

一段 NyaScript 可能看起来像**这样**：
```js
{
    let t0 = $Ticks();
   
    var fib = λ(v) {
        if(v <= 2) return v;
        else return self(v-1) + self(v-2);
    };
    
    function run_fib() { 
        for(var i = 1; i <= 25; ++i){
            print (fib(i));
    }
        
    run_fib();
        
    print("used time: " + ($Ticks() - t0) + " ticks");
}
```

>这段代码输出了斐波那契数列前25项以及其运行时间（ticks），  
>并且涉及了常量和变量、函数和Lambda函数的定义方式.

下面的内容将逐一介绍 NyaScript 的语法：

<br>

### 代码格式
<br>

NyaScript 中，每个 **语句** 都由`;`结尾：  
```js 
statment ; 
```  
>在 js 中，行尾分号是可选的，但 NyaScript 不允许分号省略  

对于 **语句块**，使用大括号`{}`包裹：  
```js
 { block } 
 ```
 总之写起来就和 js 类似，建议直接参考 js 的书写格式.

<br>

### 变量与常量
<br>

使用`var`关键字来声明一个 **变量**，就像：
```js
var a;
var b = 1;
var c = "hello " + "world!";
```
你可以使用类似`=`, `+=`, `++`等 **赋值操作符** 修改一个变量的值，例如
```js
var a;
a = 0;
a += 1;
a %= 4;
--a;
```
>***[Note]***  NyaScript 中的自增/自减操作符是**前缀的**，像`i++`这样后缀操作符无法通过编译

NyaScript 的变量没有类型约束，你可以给一个变量赋不同类型的值；  
但是对于运算符，NyaScript 只提供有限的隐式转换，不合法的运算表达式会产生运行时错误

```js
var dynamic = 114;
dynamic = true;
dynamic = "EraBasic is out of time, ";
dynamic += "and NyaScript is state of art (误"; // string + string 是合法的
dynamic += 514;                                 // string + number 会将 number 隐式转换为 string，是合法的
dynamic *= 2;                                   // => Error：没有定义 string 与 number 之间的 '*' 运算 
```

<br>

使用`let`关键字来声明一个 **常量**[^1]，  
它与变量不同，一旦声明，数值就不能改变，就像其它语言的`const`关键字一样  

[^1]: 当前的版本暂时不能完全保护常量不被重新赋值，常量的声明是由将创建的变量类型由动态(dynamic)改为静态(int, double, string, etc.)实现的，但是在后续版本中将会改进这个方法，因此即使在当前版本中对常量赋值可能不会报错，也务必不要这样操作.

```js
let pi = 3.14;
pi = 3; // => Error：非法的赋值目标
```

>***[Note]***  在 Javascript 中，`let`关键字用于声明一个局部变量，而 NyaScript 与其不同；NyaScript 中使用`var`语句声明的变量天生有其作用域，请参考 **变量作用域** 章节.

NyaScript 不允许隐式变量声明，无论是变量还是常量都应当在 **声明后引用**：
```js
// 'value' 是一个未声明的变量
value = 1919; // Error：未声明的变量
```

<br>

### 函数与匿名函数
<br>

使用`function`关键字来声明 **函数**：[^2]
```js
function say_something(something){
    print(something);
}

say_something("AhAhAhAhhhhhhh!"); // => AhAhAhAhhhhhhh!
```

[^2]: 事实上，这种声明恰恰是 Lambda 函数的一个*语法糖*，这个语句声明了一个 Lambda 函数，然后将其赋值给与声明的函数名同名的变量.

<br>

与任何一种函数式语言一样，函数是 NyaScript 中的 *第一公民*；  
可以像下面这样使用希腊字母`λ`声明一个 **Lambda 函数**：  
语句Lambda： `λ(parameters) => expression`  
块Lambda： `λ(parameters) { block }` 

```js
var lam_1 = λ(x) => x * x;
var lam_2 = λ(x) {
    print(x);
};
```

>在不支持 Unicode 的环境，或不方便打出 *λ* 字母的情况下，可以使用 `function` 关键字来代替它；  
>尽管如此，我们仍建议尽量使用 *λ* 符号，这会增强代码的可读性.

如果你的 Lambda 函数需要递归，从数学角度上说通常需要使用到 *Y组合子*，
但这会给代码添加一堆难懂的符号并且加大程序的运算负担.  
*不要忘了，NyaScript 是以 ADV 游戏脚本为目标设计的语言*  
因此在 NyaScript 中，可以使用`self`关键字来 **递归** Lambda 函数，就像我们最上面的例程中一样：
```js
var fib = λ(v) {
        if(v <= 2) return v;
        else return self(v-1) + self(v-2);
    };
```

NyaScript 也支持 **闭包、柯里化** 等特性，具体实现可以参考 ***./ExampleScript/*** 目录下的文件.

<br>

### 元组和容器
<br>

它们的定义方法就和 js 中的一样：
```js
// 元组
var testTuple = [
        "hello",
        "world",
        [ "how", "are", "you" ]
    ];
print (testTuple[2][1]); // => are

// 容器
var testContainer = {
        uname: "_name",
        level: 128,
        skill: ["Attack", "Defend"]
    };

print (testContainer.skill[0]); // => Attack

```
因此，NyaScript 也可以使用 **Eval()** 方法直接从 .json 文件中导入数据；  
但是索引元组或容器成员的方法与 js 略有不同.

下列方法在 js 中也支持：
```js
tuple[index];
container.field;
```
下列方法则为 NyaScript 特有：
```js
tuple[index1, index2, ...];
container.@"field_name";  // 取代了 js 中 container["field_name"] 的索引方式
container.@string_variable;
```

<br>

### 数据类型
<br>

NyaScript 中，数据具有7种**基本类型**：  
`null, number, bool, string, tuple, container, lambda`[^3]

**null** : 空对象，一个声明了而未被赋值的变量会被赋值为`null`，一个没有返回值或错误返回的函数也会返回`null`；可以理解为`null`同时代表了其它语言中`null`, `undefined`, `void`的含义.

**number** : 数值类型，包括整数和浮点数（但不包括 bigInt，NyaScript 不支持 bigInt，请尽量将数据控制在64位浮点能表达的范围）；

**bool** : 布尔类型，包含`true`和`false`两种类型；在逻辑运算中，`null`可以隐式转换为`false`，*除 null 和 false 外的所有值* 可以隐式转换为`true`；布尔类型不能向数值类型隐式转换.
>在 NyaScript 中，`0`是 `true`，因为它不是 `null` 或者 `false`

**string** : 字符串类型，*所有其它类型都可以隐式转换为字符串*，下列是例子：
```js
print(
    "" + null + true + 191 + "981"
); // => nulltrue191981
```
>隐式转换由表达式的第一个参数决定转换的类型，
>因此在上面代码中，如果删除开头的 `""`，则无法转换，因为 bool 无法转换为 null

**tuple 和 container** : 即上述的元组和容器；
>把 tuple 和 container 直接转换为 string 可能会不尽人意，这两者分别会返回 `DynamicTypedef[]` 和 `Dictionary`

**lambda** : 函数类型，相当于C语言的函数指针或C#的委托类型.

NyaScript 的数据类型并不完全遵照ECMA262规范，下表列出了 NyaScript 中的类型与 ECMAScript 中类型的对应关系，以及该类型在其宿主语言(C#)中的实际实现方式：
| NyaScript   | ECMAScript  | C#         |
| ----------- | ----------- | ---------- |
| null        | null        | null       |
| null        | undefined   | null       |
| number      | number      | int, double, long |
| bool        | boolean     | bool       |
| string      | string      | string     |
| tuple       | object      | DynamicTypedef [ ]|
| container   | object      | Dictionary |
| lambda      | function    | Delegate   |
| (symbol)    | symbol      | string     |

<br>

### 变量作用域
<br>

在 NyaScript 中，一个变量的**作用域**是从该变量声明位置直到该变量所处的语句块结束；
在任何地方，你都可以使用大括号包裹代码块来创建一个语句块，来限定一些变量的作用域：
```js
{
    var outer;                    // <----------┐
                                  //            |
    /* do something */            //        outer 的
                                  //         作用域
    {                             //            |
        var inner;                // <----┐     |
                                  //   inner 的 |
        /* do other thing */      //    作用域  |
    }                             // <----┘     |
    print(inner); // => 变量未声明 //            |
}                                 // <----------┘
```

如果内层语句块中的变量与外层的某个变量同名，则会**遮蔽**外层变量，使得在其作用域内无法访问外层的同名变量：
```js
{
    var a = "outer var";
    {
        var a = "inner var";
        print(a); // => inner var
    }
    print(a); // => outer var
}
```

如果一个变量没有被任何大括号包裹，那么它就会被定义为**全局变量**；  
一个全局变量不仅其所在的脚本能够访问，与该脚本同时编译的其它脚本也能够访问：
```js
/* definer.nya */

var Age = 24; // 这是全局变量

function Growl(){  // 这个函数是全局变量
    return "Ahahahahhhhhh!";
}

{
    var name = "Senbai"; // 这是局部变量
}
```
```js
/* caller.nya */

print(Age); // => 24
print(Growl()); // => Ahahahahhhhhh!
print(name); // => Error: 标识符未定义
```
>因为全局变量能够跨脚本访问的特性，一个脚本应该把其业务逻辑包裹在**至少一个大括号中**，像下面的脚本这样：
```js
/* mio-ev10-3.nya */

// Gloable
let MioLoveProgressInprovementViaEv103 = 10; // 由事件 10-3 提供的好感度上升量
var Ev103Trigger = false; // 事件的触发开关，如果玩家经历了 ev11-1，则在那个脚本中将该变量置为 true

// Local
{
    // 读取文本
    var eventText = $Eval($FileReadAllText("./Event/mio-ev10-3.json"));

    // 执行演出
    $PushLine(eventText[0]);
    $WaitUserInput();
    ...
    ...

    // 好感度增长
    MioLoveProgress += MioLoveProgressInprovementViaEv103; // 这个全局变量声明在 mio-character.nya
}
```
<br>

### 控制流
<br>

#### 条件分支

和常见的编程语言一样，使用 `if - else if - else` 来实现条件分支：
```js
if (condition) {
    do_when_true();
} else if (another_condition){
    do_when_another_true();
} else {
    do_when_false();
}
```
也可以用 `switch - case - default` 来对同一参数的不同情况进行分支；  
在 NyaScript 中，`switch - case` 是 `if - else` 的 *语法糖*，因此在 `case` 语句块中不需要使用 `break` 跳出：
```js
switch ($WaitUserInput()){ // 可能返回 0，1，2
    case 0: {
        $PushLine("进入事件：");
        run_ev_1_1();
    }
    case 1: back_to_menu();
    case 2: run_bad_end();
    default: {
        $PushLine("按钮返回不正确");
        back_to_menu();
    }
}
```
<br>

#### 循环

使用 `for` 和 `while` 语句实现循环：
```js
for (var i = 0; i < 64; ++i){
    do_loop();
}

var i = 0;
while (i < 128){
    do_another_loop();
    ++i;
}
```
使用 `break` 和 `continue` 来退出或继续循环.  
<br>

#### goto 语法

很多语言不喜欢 `goto` 这样的语法，这往往使得程序逻辑混乱；  
然而对于 ADV 游戏这样的线性流程，`goto - label` 语法能够保持代码以线性状态排布，阻止了 *嵌套 `if`* 和 *缩进地狱* 之类的存在. 因此尽管 js 和 python 都不支持 `goto` 语法，如 Ren'py 和 EraBasic 等 ADV 脚本语言仍然保留了这一语句.

在 NyaScript 中，使用 `label IEDENTIFIER;` 声明一个标签，  
使用 `goto LABEL;` 跳转到指定标签：
```cs
label GO_HERE;
goto GO_HERE;
```

与变量不同，标签可以声明在其调用位置之后：
```cs
goto GO_HERE;
print("这段语句被跳过了"); // <= 这句没有被执行
label GO_HERE;
```

但是与变量一样，标签也有其作用域；标签的作用域是其所处的 **整个语句块**；  
`goto` 语句只能在标签的作用域内实现跳转：
```cs
{
    {
        label INNER_LABEL;
        /* do something */
        if (condition) {
            goto INNER_LABEL; // => 可以跳转
        }
    }
    goto INNER_LABEL; // => 不能跳转
}

{
    label OUTER_LABEL;
    {
        /* do something */
        if (condition) {
            goto OUTER_LABEL; // => 可以跳转
        }
    }
    goto OUTER_LABEL; // => 可以跳转
}
```
另外，即使标签被声明在了全局变量区域，也 **不允许** 跨脚本跳转标签
>这与 Ren'py 是不同的.

<br>

### 静态方法
<br>

静态方法是由宿主语言提供的方法，它不能像函数一样赋值给变量；
由 `$` 开头的方法即是静态方法，你也可以使用 C# 代码编写运行时库来提供自定义的静态方法.  
>关于自定义运行时方法，参考 **NyaScript 输出重定向** 章节 [ *Todo: 撰写中* ]

下面列出了部分已实现的静态方法

##### 通用方法
| 原型 | 介绍  | 输出 | 备注 |
| ----------- | ----------- | ---------- | ---------- |
| $Concat ( tuple, dynamic ) | 拼接数组，其中第一个变量必须是数组类型；第二个变量若是数组，则拼接两者，否则，将第二个变量添加到末尾 | tuple : 拼接后的数组 | 拼接字符串请使用 string + string |
| $Len ( dynamic ) | 获取字符串、数组、元组、容器的长度 | number : 对应的长度 | 对于容器，输出的是其字段数 |
| $ToString ( dynamic ) | 转为字符串 | string : 字符串 | 
| $DebugLog ( dynamic ) | 打印指定内容到控制台，所给参数会隐式转换到字符串 | | 注意打印目标是控制台，如果要打印到游戏界面，参考 **重定向方法** |
| $DebugLogLine ( dynamic ) | 同上，会在打印后换行 || 同上 |
| $DebugRead () | 从控制台读取一个用户输入字符 | string : 输入的字符 ||
| $DebugReadLine () | 从控制台读取一行用户输入字符 | string : 输入的字符串 ||
| $DebugReadKey () | 从控制台读取一个用户输入的**按键** | number : 按键编号 | 按键编号参考： [ConsoleKey 枚举](https://learn.microsoft.com/zh-cn/dotnet/api/system.consolekey?view=net-6.0) |
| $DebugClear () | 清空控制台输出 |||
| $FileReadAllText ( string ) | 读取指定路径的文件内容为字符串 | string : 文件内容 |||
| $FileReadAllLines ( string ) | 读取文件内容为字符串数组，每条为一行 | string [ ] : 文件内容 |||
| $FileWriteAllText ( path : string, text : string ) | 写入字符串到文件 |||
| $FileWriteAllLines ( path : string, text : string [ ] ) | 写入字符串数组到文件，每条为一行 |||
| $Random () | 获取一个 [0, 1) 之间的随机数 | number : 随机数 | 随机数种子固定为程序初始化时的 Ticks 后 32 位 |
| $RandomRange ( minVal : number, maxVal : number ) | 获取一个 [minVal, maxVal) 之间的随机整数，如果出错，返回 -1 | number : 随机数 | 同上 |
| $Ticks () | 获取 DateTime.Now.Ticks | number(long) : Ticks | 关于 Ticks 的具体含义参考： [DateTime.Ticks 属性](https://learn.microsoft.com/zh-cn/dotnet/api/system.datetime.ticks?view=net-7.0) |
| $Eval ( string ) | 将传入的字符串视为代码，输出这段代码的返回值 | dynamic : 代码返回；如果代码没有返回值，返回 `null` | 可以用于解析 .json 文件，参考 js 的 `eval( )` 方法 |
| $Warning( dynamic ) | 让控制台显示一条警告 || 警告并非异常，NyaScript 暂时不支持用户自定义的异常处理 |

<br>

##### 重定向方法
这些方法的输出和输入由运行 NyaLang 的软件决定，以 MudBox 为例，所有重定向方法都接收来自 GameView 的输入输出.
| 原型 | 介绍  | 输出 | 备注 |
| ----------- | ----------- | ---------- | ---------- |
| $PushLine ( string ) | 推送一行文字到重定向目标 | | |
| $PushFormatLine ( string ) | 推送格式化字符串到重定向目标 | | |
| $WaitInput ( string ) | 等待重定向目标输入 | int : 返回的内容 | 这个方法接受的返回必须包装为整形 |
| $ClearView () | 清空重定向目标的输出内容 | | |
