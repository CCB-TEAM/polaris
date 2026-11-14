using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UAssetAPI;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.Kismet.Bytecode.Expressions;
using UAssetAPI.UnrealTypes;

namespace Polaris.Compiler;

/// <summary>编译器内部错误（带源码位置）。</summary>
public sealed class CompileException(string message, SyntaxNode? node = null) : Exception(
    node is null ? message : $"{message} @{node.GetLocation().GetLineSpan().StartLinePosition}")
{
    public SyntaxNode? Node { get; } = node;
}

/// <summary>UE 函数（类全路径 + 函数名）→ 目标资产里的 FPackageIndex。</summary>
public interface IFunctionResolver
{
    FPackageIndex Resolve(string ownerPath, string functionName);
}

/// <summary>
/// C# 方法体 → Kismet 字节码。覆盖：静态调用、实例调用（EX_Context）、if/else、return、字面量。
/// **不经过任何私有 IR**：直接构造 UAssetAPI 的 <see cref="KismetExpression"/>。
/// </summary>
public static class Translator
{
    public static KismetExpression[] Compile(
        Compilation compilation,
        IMethodSymbol method,
        UAsset asset,
        IFunctionResolver resolver,
        FPackageIndex? functionIndex = null,
        FPackageIndex? classIndex = null)
    {
        var syntax = method.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault()
            ?? throw new CompileException($"{method.Name} 没有方法体（只支持源码里的方法）");

        var statements = new List<StatementSyntax>();
        if (syntax.Body is { } body)
        {
            statements.AddRange(body.Statements);
        }
        else if (syntax.ExpressionBody is { } expressionBody)
        {
            statements.Add(SyntaxFactory.ExpressionStatement(expressionBody.Expression));
        }
        else
        {
            throw new CompileException($"{method.Name} 没有方法体", syntax);
        }

        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var builder = new BytecodeBuilder();
        var context = new Context(
            model,
            asset,
            resolver,
            functionIndex ?? new FPackageIndex(1),
            classIndex ?? new FPackageIndex(2));

        var terminated = EmitStatements(statements, builder, context);
        if (!terminated)
        {
            builder.Emit(new EX_Return { ReturnExpression = new EX_Nothing() });
        }

        builder.Emit(new EX_EndOfScript());
        return builder.Build(asset);
    }

    private sealed record Context(
        SemanticModel Model,
        UAsset Asset,
        IFunctionResolver Resolver,
        FPackageIndex FunctionIndex,
        FPackageIndex ClassIndex);

    private static bool EmitStatements(
        IEnumerable<StatementSyntax> statements,
        BytecodeBuilder builder,
        Context context)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case ReturnStatementSyntax:
                    builder.Emit(new EX_Return { ReturnExpression = new EX_Nothing() });
                    return true;

                case IfStatementSyntax ifStatement:
                    EmitIf(ifStatement, builder, context);
                    break;

                case ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation }:
                    builder.Emit(EmitCall(invocation, context));
                    break;

                case ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment }:
                    EmitAssignment(assignment, builder, context);
                    break;

                case BlockSyntax block:
                    if (EmitStatements(block.Statements, builder, context))
                    {
                        return true;
                    }

                    break;

                default:
                    throw new CompileException($"暂不支持的语句: {statement.Kind()}", statement);
            }
        }

        return false;
    }

    private static void EmitIf(IfStatementSyntax ifStatement, BytecodeBuilder builder, Context context)
    {
        var condition = EmitCondition(ifStatement.Condition);
        var elseLabel = builder.NewLabel();
        var endLabel = builder.NewLabel();

        builder.EmitJumpIfNot(condition, elseLabel);
        EmitStatements(ifStatement.Statement is BlockSyntax thenBlock
            ? thenBlock.Statements
            : [ifStatement.Statement], builder, context);

        if (ifStatement.Else is null)
        {
            builder.MarkLabel(elseLabel);
            return;
        }

        builder.EmitJump(endLabel);
        builder.MarkLabel(elseLabel);
        EmitStatements(ifStatement.Else.Statement is BlockSyntax elseBlock
            ? elseBlock.Statements
            : [ifStatement.Else.Statement], builder, context);
        builder.MarkLabel(endLabel);
    }

    private static KismetExpression EmitCondition(ExpressionSyntax expression)
    {
        if (expression.IsKind(SyntaxKind.TrueLiteralExpression))
        {
            return new EX_True();
        }

        if (expression.IsKind(SyntaxKind.FalseLiteralExpression))
        {
            return new EX_False();
        }

        throw new CompileException($"if 条件暂只支持 true/false 字面量: {expression}", expression);
    }

    private static KismetExpression EmitCall(InvocationExpressionSyntax invocation, Context context)
    {
        var symbol = context.Model.GetSymbolInfo(invocation).Symbol as IMethodSymbol
            ?? throw new CompileException($"无法解析调用: {invocation}", invocation);

        var ownerPath = GetUClassPath(symbol.ContainingType)
            ?? throw new CompileException($"{symbol.ContainingType} 没有 [UClass] 特性", invocation);

        var parameters = new List<KismetExpression>();
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            parameters.Add(EmitExpression(argument.Expression, context));
        }

        var call = new EX_FinalFunction
        {
            StackNode = context.Resolver.Resolve(ownerPath, symbol.Name),
            Parameters = parameters.ToArray(),
        };

        if (symbol.IsStatic)
        {
            return call;
        }

        var receiverSyntax = invocation.Expression is MemberAccessExpressionSyntax member
            ? member.Expression
            : null;
        if (receiverSyntax is null)
        {
            throw new CompileException("实例调用缺少接收者", invocation);
        }

        var receiver = EmitReceiver(receiverSyntax, context);
        return new EX_Context
        {
            ObjectExpression = receiver,
            Offset = BytecodeBuilder.Measure(call, context.Asset),
            RValuePointer = EmptyPointer(context.Asset),
            ContextExpression = call,
        };
    }

    private static KismetExpression EmitReceiver(ExpressionSyntax receiver, Context context)
    {
        if (receiver.IsKind(SyntaxKind.ThisExpression))
        {
            return new EX_Self();
        }

        var symbol = context.Model.GetSymbolInfo(receiver).Symbol;
        return symbol switch
        {
            IFieldSymbol field => new EX_InstanceVariable
            {
                Variable = Pointer(field.Name, context.ClassIndex, context.Asset),
            },
            IParameterSymbol parameter => new EX_LocalVariable
            {
                Variable = Pointer(parameter.Name, context.FunctionIndex, context.Asset),
            },
            ILocalSymbol local => new EX_LocalVariable
            {
                Variable = Pointer(local.Name, context.FunctionIndex, context.Asset),
            },
            _ => throw new CompileException($"接收者只支持 this/字段/参数/局部变量: {receiver}", receiver),
        };
    }

    private static KismetPropertyPointer Pointer(string name, FPackageIndex owner, UAsset asset)
        => new()
        {
            New = new FFieldPath([new FName(asset, name)], owner),
        };

    private static KismetPropertyPointer EmptyPointer(UAsset asset)
        => new()
        {
            New = new FFieldPath([], new FPackageIndex(0)),
        };

    private static void EmitAssignment(AssignmentExpressionSyntax assignment, BytecodeBuilder builder, Context context)
    {
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            throw new CompileException("只支持 = 赋值", assignment);
        }

        var target = EmitReceiver(assignment.Left, context);
        var value = EmitExpression(assignment.Right, context);
        var pointer = target is EX_VariableBase variable
            ? variable.Variable
            : throw new CompileException("赋值目标必须是变量", assignment.Left);
        builder.Emit(new EX_Let
        {
            Value = pointer,
            Variable = target,
            Expression = value,
        });
    }

    private static KismetExpression EmitExpression(ExpressionSyntax expression, Context context)
    {
        var constant = context.Model.GetConstantValue(expression);
        if (constant.HasValue)
        {
            return constant.Value switch
            {
                int i => new EX_IntConst { Value = i },
                bool b => b ? new EX_True() : new EX_False(),
                string s => new EX_StringConst { Value = s },
                float f => new EX_FloatConst { Value = f },
                double d => new EX_DoubleConst { Value = d },
                _ => throw new CompileException($"不支持的字面量类型 {constant.Value?.GetType()}", expression),
            };
        }

        if (expression is InvocationExpressionSyntax invocation)
        {
            return EmitCall(invocation, context);
        }

        return EmitReceiver(expression, context);
    }

    private static string? GetUClassPath(INamedTypeSymbol type)
    {
        var attribute = type.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name == "UClassAttribute");
        return attribute?.ConstructorArguments.FirstOrDefault().Value as string;
    }
}
