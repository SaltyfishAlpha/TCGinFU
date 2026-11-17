using System.Runtime.CompilerServices;

// 测试需要直接摆出「上一轮造成伤害」「AP 剩余」「本局已消耗充能数」这类
// 本该由引擎推进的中间状态，否则每条断言都要先跑完一整轮。
[assembly: InternalsVisibleTo("TCGinFU.Core.Tests")]
