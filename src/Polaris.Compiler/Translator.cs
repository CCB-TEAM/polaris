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
        FPackageIndex? classIndex = null,
        UAssetAPI.ExportTypes.FunctionExport? targetFunction = null)
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
            classIndex ?? new FPackageIndex(2),
            targetFunction);

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
        FPackageIndex ClassIndex,
        UAssetAPI.ExportTypes.FunctionExport? TargetFunction)
    {
        public Stack<int> BreakTargets { get; } = new();
        public Stack<int> ContinueTargets { get; } = new();
    }

    private static bool EmitStatements(
        IEnumerable<StatementSyntax> statements,
        BytecodeBuilder builder,
        Context context)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case ReturnStatementSyntax ret:
                    builder.Emit(new EX_Return
                    {
                        ReturnExpression = ret.Expression is null ? new EX_Nothing() : EmitExpression(ret.Expression, context),
                    });
                    return true;

                case IfStatementSyntax ifStatement:
                    EmitIf(ifStatement, builder, context);
                    break;

                case WhileStatementSyntax whileStatement:
                    EmitWhile(whileStatement, builder, context);
                    break;

                case SwitchStatementSyntax switchStatement:
                    EmitSwitch(switchStatement, builder, context);
                    break;

                case ForStatementSyntax forStatement:
                    EmitFor(forStatement, builder, context);
                    break;

                case ExpressionStatementSyntax { Expression: PostfixUnaryExpressionSyntax or PrefixUnaryExpressionSyntax } stepStatement:
                    EmitStepStatement(stepStatement.Expression, builder, context);
                    break;

                case BreakStatementSyntax:
                    if (context.BreakTargets.Count == 0)
                    {
                        throw new CompileException("break 不在循环/switch 里", statement);
                    }

                    builder.EmitJump(context.BreakTargets.Peek());
                    break;

                case ContinueStatementSyntax:
                    if (context.ContinueTargets.Count == 0)
                    {
                        throw new CompileException("continue 不在循环里", statement);
                    }

                    builder.EmitJump(context.ContinueTargets.Peek());
                    break;

                case LocalDeclarationStatementSyntax local:
                    EmitLocalDeclaration(local, builder, context);
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
        var condition = EmitCondition(ifStatement.Condition, context);
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

    private static void EmitWhile(WhileStatementSyntax whileStatement, BytecodeBuilder builder, Context context)
    {
        var startLabel = builder.NewLabel();
        var endLabel = builder.NewLabel();

        builder.MarkLabel(startLabel);
        builder.EmitJumpIfNot(EmitCondition(whileStatement.Condition, context), endLabel);
        context.BreakTargets.Push(endLabel);
        context.ContinueTargets.Push(startLabel);
        EmitStatements(whileStatement.Statement is BlockSyntax block
            ? block.Statements
            : [whileStatement.Statement], builder, context);
        context.ContinueTargets.Pop();
        context.BreakTargets.Pop();
        builder.EmitJump(startLabel);
        builder.MarkLabel(endLabel);
    }

    private static void EmitFor(ForStatementSyntax forStatement, BytecodeBuilder builder, Context context)
    {
        if (forStatement.Declaration is { } declaration)
        {
            foreach (var variable in declaration.Variables)
            {
                EmitLocalVariable(variable, builder, context);
            }
        }

        foreach (var initializer in forStatement.Initializers)
        {
            EmitStepStatement(initializer, builder, context);
        }

        var startLabel = builder.NewLabel();
        var endLabel = builder.NewLabel();
        builder.MarkLabel(startLabel);
        builder.EmitJumpIfNot(EmitCondition(forStatement.Condition, context), endLabel);
        context.BreakTargets.Push(endLabel);
        context.ContinueTargets.Push(startLabel);
        EmitStatements(forStatement.Statement is BlockSyntax block
            ? block.Statements
            : [forStatement.Statement], builder, context);
        context.ContinueTargets.Pop();
        context.BreakTargets.Pop();

        foreach (var incrementor in forStatement.Incrementors)
        {
            EmitStepStatement(incrementor, builder, context);
        }

        builder.EmitJump(startLabel);
        builder.MarkLabel(endLabel);
    }

    private static void EmitStepStatement(ExpressionSyntax expression, BytecodeBuilder builder, Context context)
    {
        if (expression is PostfixUnaryExpressionSyntax or PrefixUnaryExpressionSyntax)
        {
            var operand = expression is PostfixUnaryExpressionSyntax post ? post.Operand : ((PrefixUnaryExpressionSyntax)expression).Operand;
            var delta = expression.IsKind(SyntaxKind.PostIncrementExpression) || expression.IsKind(SyntaxKind.PreIncrementExpression) ? 1 : -1;
            EmitStep(operand, delta, expression, builder, context);
            return;
        }

        if (expression is InvocationExpressionSyntax invocation)
        {
            builder.Emit(EmitCall(invocation, context));
            return;
        }

        if (expression is AssignmentExpressionSyntax assignment)
        {
            EmitAssignment(assignment, builder, context);
            return;
        }

        throw new CompileException($"不支持的 for 子句: {expression}", expression);
    }

    private static void EmitStep(
        ExpressionSyntax target,
        int delta,
        SyntaxNode node,
        BytecodeBuilder builder,
        Context context)
    {
        if (context.TargetFunction is null || context.Model.GetSymbolInfo(target).Symbol is not ILocalSymbol local)
        {
            throw new CompileException("++/-- 只支持局部变量", node);
        }

        var pointer = new EX_LocalVariable { Variable = Pointer(local.Name, context.FunctionIndex, context.Asset) };
        var operandType = context.Model.GetTypeInfo(target).Type;
        var (suffix, one) = operandType?.SpecialType switch
        {
            SpecialType.System_Int32 => ("IntInt", (KismetExpression)new EX_IntConst { Value = 1 }),
            SpecialType.System_Single => ("FloatFloat", new EX_FloatConst { Value = 1 }),
            SpecialType.System_Double => ("DoubleDouble", new EX_DoubleConst { Value = 1 }),
            _ => throw new CompileException($"++/-- 不支持的类型 {operandType}", node),
        };

        builder.Emit(new EX_Let
        {
            Value = Pointer(local.Name, context.FunctionIndex, context.Asset),
            Variable = new EX_LocalVariable { Variable = Pointer(local.Name, context.FunctionIndex, context.Asset) },
            Expression = new EX_CallMath
            {
                StackNode = context.Resolver.Resolve("/Script/Engine.KismetMathLibrary", (delta > 0 ? "Add_" : "Subtract_") + suffix),
                Parameters = [pointer, one],
            },
        });
    }

    private static void EmitSwitch(SwitchStatementSyntax switchStatement, BytecodeBuilder builder, Context context)
    {
        var endLabel = builder.NewLabel();
        var switchType = context.Model.GetTypeInfo(switchStatement.Expression).Type;
        var suffix = switchType?.SpecialType switch
        {
            SpecialType.System_Int32 => "IntInt",
            SpecialType.System_String => "StrStr",
            SpecialType.System_Boolean => "BoolBool",
            _ => throw new CompileException($"switch 暂不支持 {switchType}", switchStatement),
        };

        foreach (var section in switchStatement.Sections)
        {
            if (section.Labels.Any(l => l is DefaultSwitchLabelSyntax))
            {
                context.BreakTargets.Push(endLabel);
                EmitStatements(section.Statements, builder, context);
                context.BreakTargets.Pop();
                continue;
            }

            var caseLabels = section.Labels.OfType<CaseSwitchLabelSyntax>().ToList();
            if (caseLabels.Count == 0)
            {
                continue;
            }

            var nextLabel = builder.NewLabel();
            KismetExpression? condition = null;
            foreach (var label in caseLabels)
            {
                var equals = new EX_CallMath
                {
                    StackNode = context.Resolver.Resolve("/Script/Engine.KismetMathLibrary", "EqualEqual_" + suffix),
                    Parameters = [EmitExpression(switchStatement.Expression, context), EmitExpression(label.Value, context)],
                };
                condition = condition is null
                    ? equals
                    : new EX_CallMath
                    {
                        StackNode = context.Resolver.Resolve("/Script/Engine.KismetMathLibrary", "Or_BoolBool"),
                        Parameters = [condition, equals],
                    };
            }

            builder.EmitJumpIfNot(condition!, nextLabel);
            context.BreakTargets.Push(endLabel);
            EmitStatements(section.Statements, builder, context);
            context.BreakTargets.Pop();
            builder.EmitJump(endLabel);
            builder.MarkLabel(nextLabel);
        }

        builder.MarkLabel(endLabel);
    }

    private static KismetExpression EmitCondition(ExpressionSyntax expression, Context context)
    {
        if (expression.IsKind(SyntaxKind.TrueLiteralExpression))
        {
            return new EX_True();
        }

        if (expression.IsKind(SyntaxKind.FalseLiteralExpression))
        {
            return new EX_False();
        }

        return EmitExpression(expression, context);
    }

    private static KismetExpression EnumConstant(IFieldSymbol field, Context context)
    {
        var enumType = (INamedTypeSymbol)field.Type;
        var raw = Convert.ToInt64(field.ConstantValue);
        return enumType.EnumUnderlyingType?.SpecialType switch
        {
            SpecialType.System_Byte => new EX_ByteConst { Value = (byte)raw },
            SpecialType.System_SByte => new EX_ByteConst { Value = (byte)(sbyte)raw },
            SpecialType.System_Int64 => new EX_Int64Const { Value = raw },
            _ => new EX_IntConst { Value = (int)raw },
        };
    }

    private static KismetExpression EmitBinary(BinaryExpressionSyntax binary, Context context)
    {
        var op = binary.Kind() switch
        {
            SyntaxKind.AddExpression => "Add",
            SyntaxKind.SubtractExpression => "Subtract",
            SyntaxKind.MultiplyExpression => "Multiply",
            SyntaxKind.DivideExpression => "Divide",
            SyntaxKind.GreaterThanExpression => "Greater",
            SyntaxKind.LessThanExpression => "Less",
            SyntaxKind.GreaterThanOrEqualExpression => "GreaterEqual",
            SyntaxKind.LessThanOrEqualExpression => "LessEqual",
            SyntaxKind.EqualsExpression => "EqualEqual",
            SyntaxKind.NotEqualsExpression => "NotEqual",
            SyntaxKind.ModuloExpression => "Percent",
            SyntaxKind.BitwiseAndExpression => "BitwiseAnd",
            SyntaxKind.BitwiseOrExpression => "BitwiseOr",
            SyntaxKind.ExclusiveOrExpression => "BitwiseXor",
            SyntaxKind.LeftShiftExpression => "LeftShift",
            SyntaxKind.RightShiftExpression => "RightShift",
            SyntaxKind.LogicalAndExpression => "And",
            SyntaxKind.LogicalOrExpression => "Or",
            _ => throw new CompileException($"暂不支持的运算符 {binary.OperatorToken.Text}", binary),
        };

        var operandType = context.Model.GetTypeInfo(binary.Left).Type;
        var intOnly = binary.Kind() is SyntaxKind.ModuloExpression
            or SyntaxKind.BitwiseAndExpression
            or SyntaxKind.BitwiseOrExpression
            or SyntaxKind.ExclusiveOrExpression
            or SyntaxKind.LeftShiftExpression
            or SyntaxKind.RightShiftExpression;

        var kind = binary.Kind();
        var suffix = operandType?.SpecialType switch
        {
            SpecialType.System_Int32 => "IntInt",
            SpecialType.System_Single when !intOnly => "FloatFloat",
            SpecialType.System_Double when !intOnly => "DoubleDouble",
            SpecialType.System_Boolean when kind is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression
                or SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression => "BoolBool",
            SpecialType.System_String when kind is SyntaxKind.AddExpression or SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression => "StrStr",
            _ => throw new CompileException($"暂不支持的运算数类型 {operandType}", binary),
        };

        return new EX_CallMath
        {
            StackNode = context.Resolver.Resolve("/Script/Engine.KismetMathLibrary", op + "_" + suffix),
            Parameters =
            [
                EmitExpression(binary.Left, context),
                EmitExpression(binary.Right, context),
            ],
        };
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

    private static void EmitLocalDeclaration(
        LocalDeclarationStatementSyntax declaration,
        BytecodeBuilder builder,
        Context context)
    {
        if (declaration.Declaration.Variables.Count != 1)
        {
            throw new CompileException("一次只支持声明一个局部变量", declaration);
        }

        EmitLocalVariable(declaration.Declaration.Variables[0], builder, context);
    }

    private static void EmitLocalVariable(
        VariableDeclaratorSyntax variable,
        BytecodeBuilder builder,
        Context context)
    {
        if (context.TargetFunction is null)
        {
            throw new CompileException("局部变量声明需要目标函数导出（写回模式）", variable);
        }

        var symbol = context.Model.GetDeclaredSymbol(variable) as ILocalSymbol
            ?? throw new CompileException("无法解析局部变量", variable);
        var (propertyType, elementSize) = PropertyTypeOf(symbol.Type, variable);

        LocalVariables.Ensure(context.TargetFunction, context.Asset, symbol.Name, propertyType, elementSize);
        if (variable.Initializer is null)
        {
            return;
        }

        var pointer = Pointer(symbol.Name, context.FunctionIndex, context.Asset);
        builder.Emit(new EX_Let
        {
            Value = pointer,
            Variable = new EX_LocalVariable { Variable = pointer },
            Expression = EmitExpression(variable.Initializer.Value, context),
        });
    }

    private static (string PropertyType, int ElementSize) PropertyTypeOf(ITypeSymbol type, SyntaxNode node) => type.SpecialType switch
    {
        SpecialType.System_Int32 => ("IntProperty", 4),
        SpecialType.System_Boolean => ("BoolProperty", 1),
        SpecialType.System_Single => ("FloatProperty", 4),
        SpecialType.System_Double => ("DoubleProperty", 8),
        SpecialType.System_Int64 => ("Int64Property", 8),
        SpecialType.System_String => ("StrProperty", 16),
        _ => throw new CompileException($"暂不支持的局部变量类型 {type}", node),
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
                byte by => new EX_ByteConst { Value = by },
                sbyte sb => new EX_ByteConst { Value = (byte)sb },
                short sh => new EX_IntConst { Value = sh },
                ushort us => new EX_IntConst { Value = us },
                long l => new EX_Int64Const { Value = l },
                uint ui => new EX_IntConst { Value = (int)ui },
                ulong ul => new EX_Int64Const { Value = (long)ul },
                _ => throw new CompileException($"不支持的字面量类型 {constant.Value?.GetType()}", expression),
            };
        }

        if (context.Model.GetSymbolInfo(expression).Symbol is IFieldSymbol { HasConstantValue: true } enumField
            && enumField.Type.TypeKind == TypeKind.Enum
            && enumField.ConstantValue is not null)
        {
            return EnumConstant(enumField, context);
        }

        if (expression is InvocationExpressionSyntax invocation)
        {
            return EmitCall(invocation, context);
        }

        if (expression is BinaryExpressionSyntax binary)
        {
            return EmitBinary(binary, context);
        }

        if (expression is PrefixUnaryExpressionSyntax unary)
        {
            return EmitUnary(unary, context);
        }

        return EmitReceiver(expression, context);
    }

    private static KismetExpression EmitUnary(PrefixUnaryExpressionSyntax unary, Context context)
    {
        if (unary.IsKind(SyntaxKind.LogicalNotExpression))
        {
            return new EX_CallMath
            {
                StackNode = context.Resolver.Resolve("/Script/Engine.KismetMathLibrary", "Not_PreBool"),
                Parameters = [EmitExpression(unary.Operand, context)],
            };
        }

        if (unary.IsKind(SyntaxKind.UnaryMinusExpression))
        {
            var operandType = context.Model.GetTypeInfo(unary.Operand).Type;
            KismetExpression zero = operandType?.SpecialType switch
            {
                SpecialType.System_Int32 => new EX_IntConst { Value = 0 },
                SpecialType.System_Single => new EX_FloatConst { Value = 0 },
                SpecialType.System_Double => new EX_DoubleConst { Value = 0 },
                _ => throw new CompileException($"暂不支持的一元取负类型 {operandType}", unary),
            };
            var suffix = operandType!.SpecialType switch
            {
                SpecialType.System_Int32 => "IntInt",
                SpecialType.System_Single => "FloatFloat",
                _ => "DoubleDouble",
            };

            return new EX_CallMath
            {
                StackNode = context.Resolver.Resolve("/Script/Engine.KismetMathLibrary", "Subtract_" + suffix),
                Parameters = [zero, EmitExpression(unary.Operand, context)],
            };
        }

        throw new CompileException($"暂不支持的一元运算符 {unary.OperatorToken.Text}", unary);
    }

    private static string? GetUClassPath(INamedTypeSymbol type)
    {
        var attribute = type.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name == "UClassAttribute");
        return attribute?.ConstructorArguments.FirstOrDefault().Value as string;
    }
}
