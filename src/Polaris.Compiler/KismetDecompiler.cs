// 反编译（表达式 → 自有节点树）+ 回编译（节点树 → 表达式）。
// 只覆盖编译器当前支持的子集；遇到不认识的指令直接抛 NotSupportedException（不静默降级）。
using UAssetAPI;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.Kismet.Bytecode.Expressions;
using UAssetAPI.UnrealTypes;

namespace Polaris.Compiler;

public abstract record KNode;

public sealed record KConst(KismetExpression Expression) : KNode;

public sealed record KVar(KismetPropertyPointer Pointer, bool IsInstance) : KNode;

public sealed record KCall(FPackageIndex Callee, KNode? Receiver, IReadOnlyList<KNode> Arguments, bool IsLocalFinal = false, uint? OriginalOffset = null, KismetPropertyPointer? OriginalRValue = null, bool IsCallMath = false) : KNode;

public sealed record KLet(KismetPropertyPointer Target, KismetExpression Variable, KNode Value) : KNode;

public sealed record KPersist(KismetPropertyPointer Destination, KNode Value) : KNode;

public sealed record KReturn(KNode? Value) : KNode;

public sealed record KEnd : KNode;

public sealed record KSeq(IReadOnlyList<KNode> Items) : KNode;

public sealed record KIf(KNode Condition, KNode Then, KNode? Else) : KNode;

/// <summary>暂不做语义解码的表达式：原样保留，保证往返字节不变（覆盖/一致性优先）。</summary>
public sealed record KRaw(KismetExpression Expression) : KNode;

public sealed record RoundTripReport(bool Ok, string? Error, int DiffOffset = -1);

public static class KismetDecompiler
{
    public static IReadOnlyList<KNode> Decompile(IReadOnlyList<KismetExpression> expressions, UAsset asset)
        => Decompile(expressions, asset, out _);

    public static IReadOnlyList<KNode> Decompile(
        IReadOnlyList<KismetExpression> expressions,
        UAsset asset,
        out bool decodedControlFlow)
    {
        var decoded = false;
        var nodes = DecompileRange(expressions, 0, expressions.Count, asset, Starts(expressions, asset), ref decoded, allowDecode: true);
        decodedControlFlow = decoded;
        return nodes;
    }

    /// <summary>跳转一律原样保留（用于往返校验，保证字节不变）。</summary>
    public static IReadOnlyList<KNode> DecompileRaw(IReadOnlyList<KismetExpression> expressions, UAsset asset)
    {
        var decoded = false;
        return DecompileRange(expressions, 0, expressions.Count, asset, Starts(expressions, asset), ref decoded, allowDecode: false);
    }

    private static IReadOnlyList<KNode> DecompileRange(
        IReadOnlyList<KismetExpression> expressions,
        int start,
        int end,
        UAsset asset,
        IReadOnlyList<uint> starts,
        ref bool decoded,
        bool allowDecode)
    {
        var nodes = new List<KNode>();
        var i = start;
        while (i < end)
        {
            if (allowDecode && expressions[i] is EX_JumpIfNot conditional)
            {
                if (TryDecodeIf(expressions, i, end, asset, starts, ref decoded, out var node, out var next) && allowDecode)
                {
                    nodes.Add(node!);
                    i = next;
                    continue;
                }

                nodes.Add(new KRaw(expressions[i]));
                i++;
                continue;
            }

            if (expressions[i] is EX_Jump)
            {
                nodes.Add(new KRaw(expressions[i]));
                i++;
                continue;
            }

            nodes.Add(DecompileExpression(expressions[i]));
            i++;
        }

        return nodes;
    }

    private static bool TryDecodeIf(
        IReadOnlyList<KismetExpression> expressions,
        int index,
        int end,
        UAsset asset,
        IReadOnlyList<uint> starts,
        ref bool decoded,
        out KNode? node,
        out int next)
    {
        node = null;
        next = index + 1;
        var conditional = (EX_JumpIfNot)expressions[index];

        int target;
        try
        {
            target = IndexOf(starts, conditional.CodeOffset);
        }
        catch (NotSupportedException)
        {
            return false;
        }

        if (target <= index + 1)
        {
            return false;
        }

        var jump = index + 1;
        while (jump < end && expressions[jump] is not EX_Jump)
        {
            jump++;
        }

        if (jump < end && expressions[jump] is EX_Jump jumpExpression)
        {
            int finish;
            try
            {
                finish = IndexOf(starts, jumpExpression.CodeOffset);
            }
            catch (NotSupportedException)
            {
                return false;
            }

            if (finish <= jump + 1 || finish > end)
            {
                return false;
            }

            var thenNodes = DecompileRange(expressions, index + 1, jump, asset, starts, ref decoded, allowDecode: true);
            var elseNodes = DecompileRange(expressions, jump + 1, finish, asset, starts, ref decoded, allowDecode: true);
            node = new KIf(DecompileExpression(conditional.BooleanExpression), new KSeq(thenNodes), new KSeq(elseNodes));
            next = finish;
            decoded = true;
            return true;
        }

        if (target <= index + 1)
        {
            return false;
        }

        var thenOnly = DecompileRange(expressions, index + 1, target, asset, starts, ref decoded, allowDecode: true);
        node = new KIf(DecompileExpression(conditional.BooleanExpression), new KSeq(thenOnly), null);
        next = target;
        decoded = true;
        return true;
    }

    private static IReadOnlyList<uint> Starts(IReadOnlyList<KismetExpression> expressions, UAsset asset)
    {
        var starts = new uint[expressions.Count];
        uint position = 0;
        for (var i = 0; i < expressions.Count; i++)
        {
            starts[i] = position;
            position += BytecodeBuilder.Measure(expressions[i], asset);
        }

        return starts;
    }

    private static int IndexOf(IReadOnlyList<uint> starts, uint offset)
    {
        for (var i = 0; i < starts.Count; i++)
        {
            if (starts[i] == offset)
            {
                return i;
            }
        }

        if (offset == (starts.Count == 0 ? 0 : starts[^1]))
        {
            return starts.Count - 1;
        }

        throw new NotSupportedException($"跳转目标 {offset} 不是指令起始（starts {starts.Count}，末 {starts[^1]}）");
    }

    private static KNode DecompileExpression(KismetExpression expression)
    {
        switch (expression)
        {
            case EX_IntConst i:
                return new KConst(new EX_IntConst { Value = i.Value });
            case EX_True:
                return new KConst(new EX_True());
            case EX_False:
                return new KConst(new EX_False());
            case EX_StringConst s:
                return new KConst(new EX_StringConst { Value = s.Value });
            case EX_FloatConst f:
                return new KConst(new EX_FloatConst { Value = f.Value });
            case EX_DoubleConst d:
                return new KConst(new EX_DoubleConst { Value = d.Value });
            case EX_Nothing:
                return new KConst(new EX_Nothing());
            case EX_LocalVariable v:
                return new KVar(v.Variable, IsInstance: false);
            case EX_InstanceVariable v:
                return new KVar(v.Variable, IsInstance: true);
            case EX_Self:
                return new KConst(new EX_Self());
            case EX_CallMath callMath:
                return new KCall(callMath.StackNode, null, callMath.Parameters.Select(DecompileExpression).ToList(), IsCallMath: true);
            case EX_LocalFinalFunction local when local.GetType() == typeof(EX_LocalFinalFunction):
                return new KCall(local.StackNode, null, local.Parameters.Select(DecompileExpression).ToList(), IsLocalFinal: true);
            case EX_FinalFunction call when call.GetType() == typeof(EX_FinalFunction):
                return new KCall(call.StackNode, null, call.Parameters.Select(DecompileExpression).ToList());
            case EX_Context context when context.ContextExpression is EX_LocalFinalFunction local && local.GetType() == typeof(EX_LocalFinalFunction):
                return new KCall(local.StackNode, DecompileExpression(context.ObjectExpression),
                    local.Parameters.Select(DecompileExpression).ToList(), IsLocalFinal: true, OriginalOffset: context.Offset, OriginalRValue: context.RValuePointer);
            case EX_Context context when context.ContextExpression is EX_FinalFunction inner && inner.GetType() == typeof(EX_FinalFunction):
                return new KCall(inner.StackNode, DecompileExpression(context.ObjectExpression),
                    inner.Parameters.Select(DecompileExpression).ToList(), OriginalOffset: context.Offset, OriginalRValue: context.RValuePointer);
            case EX_Let let:
                return new KLet(let.Value, let.Variable, DecompileExpression(let.Expression));
            case EX_LetValueOnPersistentFrame persist:
                return new KPersist(persist.DestinationProperty, DecompileExpression(persist.AssignmentExpression));
            case EX_Return ret:
                return new KReturn(ret.ReturnExpression is EX_Nothing ? null : DecompileExpression(ret.ReturnExpression));
            case EX_EndOfScript:
                return new KEnd();
            default:
                return new KRaw(expression);
        }
    }
}

public static class KismetRecompiler
{
    public static KismetExpression[] Recompile(IReadOnlyList<KNode> nodes, UAsset asset)
    {
        var builder = new BytecodeBuilder();
        foreach (var node in nodes)
        {
            Emit(node, builder, asset);
        }

        return builder.Build(asset);
    }

    private static void Emit(KNode node, BytecodeBuilder builder, UAsset asset)
    {
        switch (node)
        {
            case KConst c:
                builder.Emit(c.Expression);
                break;
            case KVar v:
                builder.Emit(v.IsInstance
                    ? new EX_InstanceVariable { Variable = v.Pointer }
                    : new EX_LocalVariable { Variable = v.Pointer });
                break;
            case KCall call:
                builder.Emit(BuildCall(call, asset));
                break;
            case KPersist persist:
                builder.Emit(new EX_LetValueOnPersistentFrame
                {
                    DestinationProperty = persist.Destination,
                    AssignmentExpression = BuildExpression(persist.Value, asset),
                });
                break;
            case KLet let:
                builder.Emit(new EX_Let
                {
                    Value = let.Target,
                    Variable = let.Variable,
                    Expression = BuildExpression(let.Value, asset),
                });
                break;
            case KReturn ret:
                builder.Emit(new EX_Return
                {
                    ReturnExpression = ret.Value is null ? new EX_Nothing() : BuildExpression(ret.Value, asset),
                });
                break;
            case KEnd:
                builder.Emit(new EX_EndOfScript());
                break;
            case KRaw raw:
                builder.Emit(raw.Expression);
                break;
            case KSeq seq:
                foreach (var item in seq.Items)
                {
                    Emit(item, builder, asset);
                }

                break;
            case KIf conditional:
                var elseLabel = builder.NewLabel();
                var endLabel = builder.NewLabel();
                builder.EmitJumpIfNot(BuildExpression(conditional.Condition, asset), elseLabel);
                Emit(conditional.Then, builder, asset);
                if (conditional.Else is null)
                {
                    builder.MarkLabel(elseLabel);
                }
                else
                {
                    builder.EmitJump(endLabel);
                    builder.MarkLabel(elseLabel);
                    Emit(conditional.Else, builder, asset);
                    builder.MarkLabel(endLabel);
                }

                break;
            default:
                throw new NotSupportedException($"回编译器暂不支持 {node.GetType().Name}");
        }
    }

    private static KismetExpression BuildExpression(KNode node, UAsset asset) => node switch
    {
        KConst c => c.Expression,
        KVar v => v.IsInstance
            ? new EX_InstanceVariable { Variable = v.Pointer }
            : new EX_LocalVariable { Variable = v.Pointer },
        KCall call => BuildCall(call, asset),
        KRaw raw => raw.Expression,
        _ => throw new NotSupportedException($"回编译表达式暂不支持 {node.GetType().Name}"),
    };

    private static KismetExpression BuildCall(KCall call, UAsset asset)
    {
        EX_FinalFunction inner = call.IsCallMath
            ? new EX_CallMath
            {
                StackNode = call.Callee,
                Parameters = call.Arguments.Select(a => BuildExpression(a, asset)).ToArray(),
            }
            : call.IsLocalFinal
            ? new EX_LocalFinalFunction
            {
                StackNode = call.Callee,
                Parameters = call.Arguments.Select(a => BuildExpression(a, asset)).ToArray(),
            }
            : new EX_FinalFunction
            {
                StackNode = call.Callee,
                Parameters = call.Arguments.Select(a => BuildExpression(a, asset)).ToArray(),
            };

        if (call.Receiver is null)
        {
            return inner;
        }

        return new EX_Context
        {
            ObjectExpression = BuildExpression(call.Receiver, asset),
            Offset = call.OriginalOffset ?? BytecodeBuilder.Measure(inner, asset),
            RValuePointer = call.OriginalRValue ?? EmptyPointer(),
            ContextExpression = inner,
        };
    }

    private static KismetPropertyPointer EmptyPointer()
        => new() { New = new FFieldPath([], new FPackageIndex(0)) };
}

public static class RoundTrip
{
    public static RoundTripReport Verify(IReadOnlyList<KismetExpression> expressions, UAsset asset)
    {
        try
        {
            var nodes = KismetDecompiler.DecompileRaw(expressions, asset);
            var rebuilt = KismetRecompiler.Recompile(nodes, asset);
            var before = Serialize(expressions, asset);
            var after = Serialize(rebuilt, asset);
            if (before.SequenceEqual(after))
            {
                return new RoundTripReport(true, null);
            }

            var diff = 0;
            while (diff < before.Length && diff < after.Length && before[diff] == after[diff])
            {
                diff++;
            }

            var where = "";
            uint at = 0;
            foreach (var expression in expressions)
            {
                var size = BytecodeBuilder.Measure(expression, asset);
                if (diff >= at && diff < at + size)
                {
                    where = $" 落在 {expression.Token}(start {at})";
                    break;
                }

                at += size;
            }

            return new RoundTripReport(false, $"回编译字节不一致 @{diff}（原 {before.Length} / 新 {after.Length}）{where}", diff);
        }
        catch (Exception ex)
        {
            return new RoundTripReport(false, ex.Message);
        }
    }

    private static byte[] Serialize(IReadOnlyList<KismetExpression> expressions, UAsset asset)
    {
        var stream = new MemoryStream();
        var writer = new AssetBinaryWriter(stream, asset);
        foreach (var expression in expressions)
        {
            ExpressionSerializer.WriteExpression(expression, writer);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
