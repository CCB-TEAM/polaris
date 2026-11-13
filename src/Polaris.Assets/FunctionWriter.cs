using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.Kismet.Bytecode;

namespace Polaris.Assets;

/// <summary>
/// 把一个 UFunction 的字节码整体替换掉（写回由 <see cref="CardAsset.Save"/> 负责）。
/// UAssetAPI 的 <c>StructExport.Write</c> 会重算 iCode 总长与磁盘大小，不需要手工改 size 字段。
/// </summary>
public static class FunctionWriter
{
    /// <summary>替换函数字节码；返回替换前的表达式条数。</summary>
    public static int Replace(UAsset asset, string functionName, IReadOnlyList<KismetExpression> bytecode)
    {
        var function = asset.Exports.OfType<FunctionExport>()
            .FirstOrDefault(f => string.Equals(f.ObjectName.ToString(), functionName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"资产里没有函数 {functionName}");

        var before = function.ScriptBytecode?.Length ?? 0;
        function.ScriptBytecode = bytecode.ToArray();
        function.ScriptBytecodeRaw = null;
        function.ScriptBytecodeSize = 0;
        return before;
    }

    /// <summary>读函数的字节码条数（不存在返回 null）。</summary>
    public static int? Count(UAsset asset, string functionName)
        => asset.Exports.OfType<FunctionExport>()
            .FirstOrDefault(f => string.Equals(f.ObjectName.ToString(), functionName, StringComparison.Ordinal))
            ?.ScriptBytecode?.Length;
}
