using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace Polaris.Compiler;

/// <summary>
/// 目标资产的 Import 表操作（自研，不依赖其他项目）：
/// 把「UE 类全路径 + 函数名」解析成包内局部 <see cref="FPackageIndex"/>，缺失时按模板克隆补导入。
/// </summary>
public sealed class AssetImportTable : IFunctionResolver
{
    private readonly UAsset _asset;

    public AssetImportTable(UAsset asset) => _asset = asset;

    public FPackageIndex Resolve(string ownerPath, string functionName)
        => EnsureFunction(ownerPath, functionName);

    public FPackageIndex? ResolveClass(string ownerPath) => EnsureClass(ownerPath);

    /// <summary>确保 <c>/Script/Module.Class</c> 的类导入存在，返回其索引。</summary>
    public FPackageIndex EnsureClass(string ownerPath)
    {
        var dot = ownerPath.LastIndexOf('.');
        if (dot <= 0)
        {
            throw new InvalidOperationException($"类全路径不合法: {ownerPath}");
        }

        var packagePath = ownerPath[..dot];
        var className = ownerPath[(dot + 1)..];
        var package = EnsurePackage(packagePath);

        for (var i = 0; i < _asset.Imports.Count; i++)
        {
            var import = _asset.Imports[i];
            if (import.ClassName?.ToString() == "Class"
                && import.ObjectName?.ToString() == className
                && import.OuterIndex?.Index == package.Index)
            {
                return new FPackageIndex(-(i + 1));
            }
        }

        var template = FindImportByClass("Class")
            ?? throw new InvalidOperationException("资产里没有可作模板的 Class 导入");
        var clone = new Import(
            template.ClassPackage,
            template.ClassName,
            package,
            new FName(_asset, className),
            template.bImportOptional);
        _asset.Imports.Add(clone);
        return new FPackageIndex(-_asset.Imports.Count);
    }

    /// <summary>确保包导入存在（<c>ClassName=Package</c>、outer=0）。</summary>
    public FPackageIndex EnsurePackage(string packagePath)
    {
        for (var i = 0; i < _asset.Imports.Count; i++)
        {
            var import = _asset.Imports[i];
            if (import.ClassName?.ToString() == "Package"
                && import.ObjectName?.ToString() == packagePath)
            {
                return new FPackageIndex(-(i + 1));
            }
        }

        var template = _asset.Imports.FirstOrDefault(im =>
            im.ClassName?.ToString() == "Package" && im.OuterIndex?.Index == 0)
            ?? throw new InvalidOperationException("资产里没有可作模板的 Package 导入");
        var clone = new Import(
            template.ClassPackage,
            template.ClassName,
            new FPackageIndex(0),
            new FName(_asset, packagePath),
            template.bImportOptional);
        _asset.Imports.Add(clone);
        return new FPackageIndex(-_asset.Imports.Count);
    }

    /// <summary>确保函数导入存在（outer = 类导入）。</summary>
    public FPackageIndex EnsureFunction(string ownerPath, string functionName)
    {
        var classIndex = EnsureClass(ownerPath);

        for (var i = 0; i < _asset.Imports.Count; i++)
        {
            var import = _asset.Imports[i];
            if (import.ClassName?.ToString() == "Function"
                && import.ObjectName?.ToString() == functionName
                && import.OuterIndex?.Index == classIndex.Index)
            {
                return new FPackageIndex(-(i + 1));
            }
        }

        var template = FindImportByClass("Function")
            ?? throw new InvalidOperationException("资产里没有可作模板的 Function 导入");
        var clone = new Import(
            template.ClassPackage,
            template.ClassName,
            classIndex,
            new FName(_asset, functionName),
            template.bImportOptional);
        _asset.Imports.Add(clone);
        return new FPackageIndex(-_asset.Imports.Count);
    }

    private Import? FindImportByClass(string className)
        => _asset.Imports.FirstOrDefault(im => im.ClassName?.ToString() == className);
}
