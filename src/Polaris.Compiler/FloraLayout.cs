using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.Kismet.Bytecode.Expressions;

namespace Polaris.Compiler;

/// <summary>
/// Kismet 的「内存偏移」布局（Flora / CUE4Parse 的 Visit 模型）：
/// 属性指针按 8 字节、FPackageIndex/StackNode 按 8 字节、FName 按 8 字节计，
/// 而不是 UAssetAPI 序列化出来的实际字节数。跳转 CodeOffset 用的是这个坐标系，
/// 套用文件字节偏移会写出「目标落不到语句起点」的代码（Flora 会报 unreached instructions）。
/// </summary>
public static class FloraLayout
{
    public static int Size(KismetExpression expression) => expression switch
    {
        EX_Let let => 1 + 8 + Size(let.Variable) + Size(let.Expression),
        EX_LetBase letBase => 1 + Size(letBase.VariableExpression) + Size(letBase.AssignmentExpression),
        EX_LetValueOnPersistentFrame persistent => 1 + 8 + Size(persistent.AssignmentExpression),
        EX_LocalVariable or EX_InstanceVariable or EX_DefaultVariable or EX_LocalOutVariable or EX_ClassSparseDataVariable => 1 + 8,
        EX_Self => 1,
        EX_Context context => 1 + Size(context.ObjectExpression) + 12 + Size(context.ContextExpression),
        EX_VirtualFunction virtualFunction => 1 + 8 + Size(virtualFunction.Parameters) + 1,
        EX_FinalFunction finalFunction => 1 + 8 + Size(finalFunction.Parameters) + 1,
        EX_IntConst or EX_FloatConst => 5,
        EX_DoubleConst or EX_Int64Const or EX_UInt64Const => 9,
        EX_ByteConst or EX_IntConstByte => 2,
        EX_StringConst text => 1 + (text.Value?.Length ?? 0) + 1,
        EX_UnicodeStringConst text => 1 + (text.Value?.Length ?? 0) * 2 + 2,
        EX_NameConst => 1 + 8,
        EX_True or EX_False or EX_Nothing or EX_EndOfScript => 1,
        EX_Return ret => 1 + Size(ret.ReturnExpression),
        EX_Jump => 5,
        EX_JumpIfNot jumpIfNot => 1 + 4 + Size(jumpIfNot.BooleanExpression),
        EX_StructConst structConst => 1 + 8 + 4 + Size(structConst.Value ?? []) + 1,
        _ => throw new NotSupportedException($"FloraLayout 未覆盖 {expression.Token}"),
    };

    public static int Size(IReadOnlyList<KismetExpression> expressions)
    {
        var total = 0;
        foreach (var expression in expressions)
        {
            total += Size(expression);
        }

        return total;
    }
}
