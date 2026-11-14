using Microsoft.CodeAnalysis;

namespace Polaris.Compiler;

/// <summary>一条「C# 方法 → 目标资产的 UFunction」绑定（来自 mod 源码上的注解）。</summary>
public sealed record ModTarget(
    INamedTypeSymbol Class,
    IMethodSymbol Method,
    string AssetPath,
    string FunctionName);

/// <summary>
/// 扫描编译单元里的数据注解，产出 mod 计划：
/// <c>[PolarisAsset("...uasset")]</c> 标在类上，<c>[PolarisReplace("函数名")]</c> 标在方法上。
/// 目标资产/函数名不再散落在 CLI 参数或硬编码里。
/// </summary>
public static class ModPlanBuilder
{
    public static IReadOnlyList<ModTarget> FindTargets(Compilation compilation)
    {
        var targets = new List<ModTarget>();
        foreach (var type in AllTypes(compilation.Assembly.GlobalNamespace))
        {
            var assetPath = GetString(type.GetAttributes(), "PolarisAssetAttribute");
            if (assetPath is null)
            {
                continue;
            }

            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            {
                var function = GetString(method.GetAttributes(), "PolarisReplaceAttribute");
                if (function is null)
                {
                    continue;
                }

                targets.Add(new ModTarget(type, method, assetPath, function));
            }
        }

        return targets;
    }

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            foreach (var nested in AllNested(type))
            {
                yield return nested;
            }
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in AllTypes(child))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> AllNested(INamedTypeSymbol type)
    {
        yield return type;
        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var t in AllNested(nested))
            {
                yield return t;
            }
        }
    }

    private static string? GetString(IEnumerable<AttributeData> attributes, string attributeName)
        => attributes
            .FirstOrDefault(a => a.AttributeClass?.Name == attributeName)
            ?.ConstructorArguments.FirstOrDefault().Value as string;
}
