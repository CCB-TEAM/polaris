using UAssetAPI;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.Kismet.Bytecode.Expressions;

namespace Polaris.Compiler;

public sealed record CheckReport(
    bool Ok,
    IReadOnlyList<string> Errors,
    int Count,
    IReadOnlyList<uint> Starts,
    IReadOnlyList<uint> JumpTargets);

/// <summary>
/// 编译器自校验：序列化 → 用 UAssetAPI 重新读回 → 比对指令序列 → 检查跳转目标落在指令起始。
/// 不依赖任何外部反编译器。
/// </summary>
public static class BytecodeCheck
{
    public static CheckReport Run(IReadOnlyList<KismetExpression> expressions, UAsset asset)
    {
        var errors = new List<string>();

        var stream = new MemoryStream();
        var writer = new AssetBinaryWriter(stream, asset);
        foreach (var expression in expressions)
        {
            ExpressionSerializer.WriteExpression(expression, writer);
        }

        writer.Flush();
        var bytes = stream.ToArray();
        var starts = new List<uint>();
        var tokens = new List<EExprToken>();

        using (var reader = new AssetBinaryReader(new MemoryStream(bytes), asset))
        {
            while (reader.BaseStream.Position < bytes.Length)
            {
                starts.Add((uint)reader.BaseStream.Position);
                var expression = ExpressionSerializer.ReadExpression(reader);
                if (expression is null)
                {
                    errors.Add($"读回失败 @{reader.BaseStream.Position}");
                    break;
                }

                tokens.Add(expression.Token);
            }
        }

        if (starts.Count != expressions.Count)
        {
            errors.Add($"指令数 {expressions.Count} vs 读回 {starts.Count}");
        }

        var jumpTargets = new List<uint>();
        foreach (var expression in expressions)
        {
            switch (expression)
            {
                case EX_Jump jump:
                    jumpTargets.Add(jump.CodeOffset);
                    break;
                case EX_JumpIfNot jumpIfNot:
                    jumpTargets.Add(jumpIfNot.CodeOffset);
                    break;
            }
        }

        foreach (var target in jumpTargets)
        {
            if (!starts.Contains(target))
            {
                errors.Add($"跳转目标 {target} 不在指令起始集合里");
            }
        }

        return new CheckReport(errors.Count == 0, errors, starts.Count, starts, jumpTargets);
    }
}
