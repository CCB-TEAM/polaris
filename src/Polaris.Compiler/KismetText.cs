// 把反编译出的节点树渲染成可读的 C# 风格伪代码（用于人工核对，不参与回编译）。
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.Kismet.Bytecode.Expressions;

namespace Polaris.Compiler;

public static class KismetText
{
    public static string Format(IReadOnlyList<KNode> nodes, UAsset asset)
    {
        var lines = new List<string>();
        foreach (var node in nodes)
        {
            Write(node, asset, 0, lines);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void Write(KNode node, UAsset asset, int indent, List<string> lines)
    {
        var pad = new string(' ', indent * 4);
        switch (node)
        {
            case KConst c:
                lines.Add(pad + Const(c.Expression) + ";");
                break;
            case KVar v:
                lines.Add(pad + Var(v) + ";");
                break;
            case KCall call:
                lines.Add(pad + Call(call, asset) + ";");
                break;
            case KLet let:
                lines.Add(pad + $"{PointerName(let.Target)} = {Expr(let.Value, asset)};");
                break;
            case KPersist persist:
                lines.Add(pad + $"persist[{PointerName(persist.Destination)}] = {Expr(persist.Value, asset)};");
                break;
            case KReturn ret:
                lines.Add(pad + (ret.Value is null ? "return;" : $"return {Expr(ret.Value, asset)};"));
                break;
            case KEnd:
                break;
            case KSeq seq:
                foreach (var item in seq.Items)
                {
                    Write(item, asset, indent, lines);
                }

                break;
            case KIf conditional:
                lines.Add(pad + $"if ({Expr(conditional.Condition, asset)})");
                lines.Add(pad + "{");
                Write(conditional.Then, asset, indent + 1, lines);
                lines.Add(pad + "}");
                if (conditional.Else is not null)
                {
                    lines.Add(pad + "else");
                    lines.Add(pad + "{");
                    Write(conditional.Else, asset, indent + 1, lines);
                    lines.Add(pad + "}");
                }

                break;
            case KRaw raw:
                lines.Add(pad + $"// {raw.Expression.Token}");
                break;
        }
    }

    private static string Expr(KNode node, UAsset asset) => node switch
    {
        KConst c => Const(c.Expression),
        KVar v => Var(v),
        KCall call => Call(call, asset),
        KRaw raw => $"/* {raw.Expression.Token} */",
        _ => "/* ? */",
    };

    private static string Const(KismetExpression expression) => expression switch
    {
        EX_IntConst i => i.Value.ToString(),
        EX_ByteConst b => b.Value.ToString(),
        EX_Int64Const l => l.Value.ToString() + "L",
        EX_UInt64Const ul => ul.Value.ToString() + "UL",
        EX_True => "true",
        EX_False => "false",
        EX_StringConst s => "\"" + s.Value.Replace("\"", "\\\"") + "\"",
        EX_FloatConst f => f.Value.ToString("R") + "f",
        EX_DoubleConst d => d.Value.ToString("R"),
        EX_Nothing => "void",
        EX_Self => "this",
        _ => $"/* {expression.Token} */",
    };

    private static string Var(KVar v)
        => PointerName(v.Pointer) + (v.IsInstance ? "" : "");

    private static string PointerName(KismetPropertyPointer pointer)
    {
        var path = pointer.New?.Path;
        if (path is null || path.Length == 0)
        {
            return "this";
        }

        return string.Join(".", path.Select(p => p.ToString()));
    }

    private static string Call(KCall call, UAsset asset)
    {
        var name = Resolve(call.Callee, asset);
        var args = string.Join(", ", call.Arguments.Select(a => Expr(a, asset)));
        var receiver = call.Receiver is null ? "" : Expr(call.Receiver, asset) + ".";
        return $"{receiver}{name}({args})";
    }

    private static string Resolve(UAssetAPI.UnrealTypes.FPackageIndex index, UAsset asset)
    {
        if (index.Index > 0 && index.Index <= asset.Exports.Count)
        {
            return asset.Exports[index.Index - 1].ObjectName.ToString();
        }

        if (index.Index < 0 && -index.Index <= asset.Imports.Count)
        {
            return asset.Imports[-index.Index - 1].ObjectName.ToString();
        }

        return "func#" + index.Index;
    }
}
