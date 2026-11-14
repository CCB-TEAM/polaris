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

        FProperty created = propertyType == "BoolProperty" ? new FBoolProperty() : new FGenericProperty();
        created.SerializedType = new FName(asset, propertyType);
        created.Name = new FName(asset, name);
        created.Flags = template?.Flags ?? EObjectFlags.RF_NoFlags;
        created.ArrayDim = template?.ArrayDim ?? EArrayDim.NotAnArray;
        created.ElementSize = template?.ElementSize ?? elementSize;
        created.PropertyFlags = template?.PropertyFlags
            ?? (EPropertyFlags.CPF_ZeroConstructor | EPropertyFlags.CPF_IsPlainOldData | EPropertyFlags.CPF_NoDestructor);
        created.RepIndex = 0;
        created.RepNotifyFunc = new FName(asset, "None");
        created.BlueprintReplicationCondition = ELifetimeCondition.COND_None;

        if (created is FBoolProperty boolean)
        {
            boolean.FieldSize = 1;
            boolean.ByteOffset = 0;
            boolean.ByteMask = 255;
            boolean.FieldMask = 255;
            boolean.NativeBool = true;
            boolean.Value = false;
        }

        function.LoadedProperties = function.LoadedProperties.Append(created).ToArray();
        return created;
    }
}
