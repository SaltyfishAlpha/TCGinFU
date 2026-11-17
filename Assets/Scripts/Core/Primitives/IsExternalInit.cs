#if !NET5_0_OR_GREATER
// Core 大量使用 C# 9 的 init 访问器做不可变卡面定义，而 init 需要这个类型。
// 它在 .NET 5+ 的 BCL 里，但不在 Unity 走的 netstandard2.1 里，所以自备一份。
// 用 dotnet 把 Core 单独编译成 net8.0 跑测试时，条件编译会让它让位给 BCL 版本。
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
