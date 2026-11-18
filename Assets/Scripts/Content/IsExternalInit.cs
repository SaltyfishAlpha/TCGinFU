#if !NET5_0_OR_GREATER
// 和 Core/Primitives/IsExternalInit.cs 同样的原因：本程序集用 C# 9 的 init 访问器，
// 而 init 需要这个类型——它在 .NET 5+ 的 BCL 里，不在 Unity 走的 netstandard2.1 里。
//
// 为什么不复用 Core 那份：那个是 internal，跨程序集看不见。每个用 init 的程序集
// 各自声明一份 internal 的即可，互不冲突。
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
