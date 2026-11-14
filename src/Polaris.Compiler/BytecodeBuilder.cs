using UAssetAPI;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.Kismet.Bytecode.Expressions;

namespace Polaris.Compiler;

/// <summary>
/// 扁平表达式序列 + 标签跳转。编译完成后一次性把标签解析成**字节偏移**
/// （Kismet 的 CodeOffset 是函数内绝对字节偏移，不是表达式下标）。
/// </summary>
public sealed class BytecodeBuilder
{
    private readonly List<KismetExpression> _expressions = new();
    private readonly Dictionary<int, int> _labelToIndex = new();
    private readonly List<(int Index, int Label)> _patches = new();
    private readonly Dictionary<int, int> _jumpToLabel = new();
    private int _nextLabel;

    public int NewLabel() => _nextLabel++;

    public void MarkLabel(int label) => _labelToIndex[label] = _expressions.Count;

    public void Emit(KismetExpression expression) => _expressions.Add(expression);

    public void EmitJump(int label)
    {
        var jump = new EX_Jump();
        _expressions.Add(jump);
        _jumpToLabel[_expressions.Count - 1] = label;
    }

    public void EmitJumpIfNot(KismetExpression condition, int label)
    {
        var jump = new EX_JumpIfNot { BooleanExpression = condition };
        _expressions.Add(jump);
        _jumpToLabel[_expressions.Count - 1] = label;
    }

    public IReadOnlyList<KismetExpression> Expressions => _expressions;

    public KismetExpression[] Build(UAsset asset)
    {
        // 每条指令的起始偏移，用 Flora/CUE4Parse 的 Visit 布局（属性指针 8、StackNode 8）。
        // 不能用序列化长度：那是文件字节数，跟跳转用的 CodeOffset 坐标系不同。
        var starts = new uint[_expressions.Count + 1];
        uint position = 0;
        for (var i = 0; i < _expressions.Count; i++)
        {
            starts[i] = position;
            position += SizeForLayout(_expressions[i], asset);
        }

        starts[_expressions.Count] = position;

        foreach (var (index, label) in _jumpToLabel)
        {
            if (!_labelToIndex.TryGetValue(label, out var targetIndex))
            {
                throw new InvalidOperationException($"跳转标签 {label} 未定义");
            }

            var target = starts[targetIndex];
            switch (_expressions[index])
            {
                case EX_Jump jump:
                    jump.CodeOffset = target;
                    break;
                case EX_JumpIfNot jumpIfNot:
                    jumpIfNot.CodeOffset = target;
                    break;
                default:
                    throw new InvalidOperationException("只有 Jump/JumpIfNot 支持标签");
            }

            _patches.Add((index, label));
        }

        return _expressions.ToArray();
    }

    private static uint SizeForLayout(KismetExpression expression, UAsset asset)
    {
        try
        {
            return (uint)FloraLayout.Size(expression);
        }
        catch (NotSupportedException)
        {
            // 原始资产里的表达式种类我们不一定生成；没有标签跳转时这个值不参与任何改写
            return Measure(expression, asset);
        }
    }

    public static uint Measure(KismetExpression expression, UAsset asset)
    {
        var stream = new MemoryStream();
        var writer = new AssetBinaryWriter(stream, asset);
        ExpressionSerializer.WriteExpression(expression, writer);
        writer.Flush();
        var length = (uint)stream.Length;
        return length;
    }
}
