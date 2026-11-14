using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.FieldTypes;
using UAssetAPI.UnrealTypes;

namespace Polaris.Compiler;

/// <summary>
/// 为函数补一个局部变量 FProperty（编译器在写回前调用）。
/// 优先克隆同资产里同类型的属性模板（flags/ElementSize 与真实资产一致）；没有模板时按类型给默认值。
/// </summary>
public static class LocalVariables
{
    public static FProperty Ensure(FunctionExport function, UAsset asset, string name, string propertyType, int elementSize)
    {
        foreach (var property in function.LoadedProperties)
        {
            if (property.Name.ToString() == name)
            {
                return property;
            }
        }

        var template = asset.Exports.OfType<FunctionExport>()
            .SelectMany(f => f.LoadedProperties)
            .FirstOrDefault(p => p.SerializedType?.Value?.Value == propertyType);

        var created = new FGenericProperty
        {
            SerializedType = new FName(asset, propertyType),
            Name = new FName(asset, name),
            Flags = template?.Flags ?? EObjectFlags.RF_NoFlags,
            ArrayDim = template?.ArrayDim ?? EArrayDim.NotAnArray,
            ElementSize = template?.ElementSize ?? elementSize,
            PropertyFlags = template?.PropertyFlags
                ?? (EPropertyFlags.CPF_ZeroConstructor | EPropertyFlags.CPF_IsPlainOldData | EPropertyFlags.CPF_NoDestructor),
            RepIndex = 0,
            RepNotifyFunc = new FName(asset, "None"),
            BlueprintReplicationCondition = ELifetimeCondition.COND_None,
        };

        function.LoadedProperties = function.LoadedProperties.Append(created).ToArray();
        return created;
    }
}
