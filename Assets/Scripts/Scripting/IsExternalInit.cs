// netstandard2.1 没有 IsExternalInit，而 `init` 访问器要求它存在。
// 每个 noEngineReferences 的程序集都得自备一份——internal 跨程序集看不见。
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
