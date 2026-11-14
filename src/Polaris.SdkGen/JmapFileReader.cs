// 纯 jmap（trumank/jmap 的 .jmap）读取：直接从游戏进程 dump 的 objects 里取
// 类/结构/枚举/函数签名，不再依赖 usmap 或 jmap extractor 的中间产物。
using System.Text.Json;

namespace Polaris.SdkGen;

public static class JmapFileReader
{
    public sealed record Result(UsmapFile Usmap, Dictionary<string, JmapOwner> Owners);

    public static Result ReadFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        var root = doc.RootElement;
        if (!root.TryGetProperty("objects", out var objects) || objects.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("jmap 缺少 objects");
        }

        var usmap = new UsmapFile();
        var owners = new Dictionary<string, JmapOwner>(StringComparer.Ordinal);

        foreach (var entry in objects.EnumerateObject())
        {
            var value = entry.Value;
            if (value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var type = GetString(value, "type");
            switch (type)
            {
                case "Class":
                case "Struct":
                case "ScriptStruct":
                case "Function":
                    ReadSchema(entry.Name, value, usmap, owners, type);
                    break;

                case "Enum":
                    ReadEnum(entry.Name, value, usmap);
                    break;
            }
        }

        return new Result(usmap, owners);
    }

    private static void ReadSchema(
        string path,
        JsonElement value,
        UsmapFile usmap,
        Dictionary<string, JmapOwner> owners,
        string type)
    {
        var outer = GetString(value, "outer");
        var super = GetString(value, "super_struct");
        var module = ModuleOf(path);

        if (type == "Class" || type == "Struct" || type == "ScriptStruct")
        {
            var schema = new UsmapSchemaInfo
            {
                Name = ShortName(path),
                ModulePath = module,
                SuperName = super is null ? "" : ShortName(super),
                SuperModulePath = super is null ? "" : ModuleOf(super),
                StructKind = type == "Class" ? 2 : 1,
            };

            var index = 0;
            foreach (var property in GetArray(value, "properties"))
            {
                schema.Properties.Add(ReadProperty(property, index++));
            }

            schema.PropCount = schema.Properties.Count;
            usmap.Schemas[schema.FullPath] = schema;
        }

        if (type == "Function" && !string.IsNullOrEmpty(outer))
        {
            var function = new JmapFunction
            {
                Name = ShortName(path),
                Flags = GetString(value, "function_flags") ?? "",
            };

            foreach (var property in GetArray(value, "properties"))
            {
                var flags = GetString(property, "flags") ?? "";
                var dir = "in";
                if (flags.Contains("CPF_ReturnParm", StringComparison.Ordinal))
                {
                    dir = "return";
                }
                else if (flags.Contains("CPF_OutParm", StringComparison.Ordinal))
                {
                    dir = "out";
                }

                function.Params.Add(new JmapParam
                {
                    Name = GetString(property, "name") ?? "",
                    Dir = dir,
                    Type = ReadParamType(property),
                });
            }

            if (!owners.TryGetValue(outer, out var owner))
            {
                owner = new JmapOwner { Path = outer, Type = "Class" };
                owners[outer] = owner;
            }

            owner.Functions.Add(function);
        }

        // 包级 __DelegateSignature 之类的空 outer 函数直接跳过（和旧 catalog 一致）
    }

    private static void ReadEnum(string path, JsonElement value, UsmapFile usmap)
    {
        var module = ModuleOf(path);
        var name = ShortName(path);
        var info = new UsmapEnumInfo { Name = name, ModulePath = module };

        if (value.TryGetProperty("names", out var names) && names.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in names.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() < 2)
                {
                    continue;
                }

                var full = entry[0].GetString() ?? "";
                var numeric = entry[1].GetInt64();
                var shortName = full.StartsWith(name + "::", StringComparison.Ordinal)
                    ? full[(name.Length + 2)..]
                    : full[(full.LastIndexOf("::", StringComparison.Ordinal) + 2)..];
                info.Values.Add(new KeyValuePair<long, string>(numeric, shortName));
            }
        }

        usmap.Enums[module + "." + name] = info;
    }

    private static JmapType ReadParamType(JsonElement property)
    {
        var type = GetString(property, "type") ?? "";
        var result = new JmapType
        {
            Type = type,
            ClassPath = GetString(property, "property_class") ?? GetString(property, "class"),
            EnumPath = GetString(property, "enum"),
        };

        if (property.TryGetProperty("inner", out var inner))
        {
            result.Inner = ReadParamType(inner);
        }

        if (property.TryGetProperty("key", out var key))
        {
            result.Key = ReadParamType(key);
        }

        if (property.TryGetProperty("value", out var val))
        {
            result.Value = ReadParamType(val);
        }

        return result;
    }

    private static UsmapPropertyInfo ReadProperty(JsonElement property, int index)
    {
        var info = new UsmapPropertyInfo
        {
            Name = GetString(property, "name") ?? "",
            SchemaIndex = index,
            SerializedIndex = index,
            ArraySize = property.TryGetProperty("array_dim", out var dim) ? dim.GetInt32() : 1,
            Flags = ParseCpfFlags(GetString(property, "flags") ?? ""),
            Data = ReadType(property),
        };

        return info;
    }

    private static UsmapPropertyData ReadType(JsonElement property)
    {
        var typeName = GetString(property, "type") ?? "";
        var data = new UsmapPropertyData { Type = MapType(typeName) };

        switch (data.Type)
        {
            case EUsmapPropertyType.ObjectProperty:
            case EUsmapPropertyType.InterfaceProperty:
            case EUsmapPropertyType.WeakObjectProperty:
            case EUsmapPropertyType.LazyObjectProperty:
            case EUsmapPropertyType.AssetObjectProperty:
            case EUsmapPropertyType.SoftObjectProperty:
                data.StructType = GetString(property, "property_class") ?? GetString(property, "class");
                break;

            case EUsmapPropertyType.StructProperty:
                data.StructType = GetString(property, "struct");
                break;

            case EUsmapPropertyType.EnumProperty:
                data.EnumName = GetString(property, "enum");
                break;

            case EUsmapPropertyType.ByteProperty:
                data.EnumName = GetString(property, "enum");
                break;

            case EUsmapPropertyType.ArrayProperty:
            case EUsmapPropertyType.SetProperty:
                if (property.TryGetProperty("inner", out var inner))
                {
                    data.Inner = ReadType(inner);
                }

                break;

            case EUsmapPropertyType.MapProperty:
                if (property.TryGetProperty("key", out var key))
                {
                    data.Inner = ReadType(key);
                }

                if (property.TryGetProperty("value", out var val))
                {
                    data.Value = ReadType(val);
                }

                break;
        }

        return data;
    }

    private static EUsmapPropertyType MapType(string type) => type switch
    {
        "BoolProperty" => EUsmapPropertyType.BoolProperty,
        "IntProperty" => EUsmapPropertyType.IntProperty,
        "Int8Property" => EUsmapPropertyType.Int8Property,
        "Int16Property" => EUsmapPropertyType.Int16Property,
        "Int64Property" => EUsmapPropertyType.Int64Property,
        "UInt16Property" => EUsmapPropertyType.UInt16Property,
        "UInt32Property" => EUsmapPropertyType.UInt32Property,
        "UInt64Property" => EUsmapPropertyType.UInt64Property,
        "ByteProperty" => EUsmapPropertyType.ByteProperty,
        "FloatProperty" => EUsmapPropertyType.FloatProperty,
        "DoubleProperty" => EUsmapPropertyType.DoubleProperty,
        "NameProperty" => EUsmapPropertyType.NameProperty,
        "StrProperty" => EUsmapPropertyType.StrProperty,
        "Utf8StrProperty" => EUsmapPropertyType.Utf8StrProperty,
        "TextProperty" => EUsmapPropertyType.TextProperty,
        "ObjectProperty" => EUsmapPropertyType.ObjectProperty,
        "WeakObjectProperty" => EUsmapPropertyType.WeakObjectProperty,
        "SoftObjectProperty" => EUsmapPropertyType.SoftObjectProperty,
        "SoftClassProperty" => EUsmapPropertyType.SoftObjectProperty,
        "LazyObjectProperty" => EUsmapPropertyType.LazyObjectProperty,
        "ClassProperty" => EUsmapPropertyType.ObjectProperty,
        "InterfaceProperty" => EUsmapPropertyType.InterfaceProperty,
        "StructProperty" => EUsmapPropertyType.StructProperty,
        "ArrayProperty" => EUsmapPropertyType.ArrayProperty,
        "SetProperty" => EUsmapPropertyType.SetProperty,
        "MapProperty" => EUsmapPropertyType.MapProperty,
        "EnumProperty" => EUsmapPropertyType.EnumProperty,
        "FieldPathProperty" => EUsmapPropertyType.FieldPathProperty,
        "DelegateProperty" => EUsmapPropertyType.DelegateProperty,
        "MulticastDelegateProperty" => EUsmapPropertyType.MulticastDelegateProperty,
        "MulticastInlineDelegateProperty" => EUsmapPropertyType.MulticastDelegateProperty,
        "MulticastSparseDelegateProperty" => EUsmapPropertyType.MulticastDelegateProperty,
        _ => EUsmapPropertyType.Unknown,
    };

    private static ulong ParseCpfFlags(string flags)
    {
        ulong value = 0;
        foreach (var part in flags.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            value |= part switch
            {
                "CPF_Edit" => 0x0000000000000001UL,
                "CPF_ConstParm" => 0x0000000000000002UL,
                "CPF_BlueprintVisible" => 0x0000000000000004UL,
                "CPF_ExportObject" => 0x0000000000000008UL,
                "CPF_BlueprintReadOnly" => 0x0000000000000010UL,
                "CPF_Net" => 0x0000000000000020UL,
                "CPF_EditFixedSize" => 0x0000000000000040UL,
                "CPF_Parm" => 0x0000000000000080UL,
                "CPF_OutParm" => 0x0000000000000100UL,
                "CPF_ZeroConstructor" => 0x0000000000000200UL,
                "CPF_ReturnParm" => 0x0000000000000400UL,
                "CPF_DisableEditOnTemplate" => 0x0000000000000800UL,
                "CPF_Transient" => 0x0000000000002000UL,
                "CPF_Config" => 0x0000000000004000UL,
                "CPF_DisableEditOnInstance" => 0x0000000000010000UL,
                "CPF_EditConst" => 0x0000000000020000UL,
                "CPF_GlobalConfig" => 0x0000000000040000UL,
                "CPF_InstancedReference" => 0x0000000000080000UL,
                "CPF_BlueprintAssignable" => 0x0000000000100000UL,
                "CPF_Atomic" => 0x0000000000200000UL,
                "CPF_SaveGame" => 0x0000000001000000UL,
                "CPF_NoClear" => 0x0000000002000000UL,
                "CPF_ReferenceParm" => 0x0000000008000000UL,
                "CPF_BlueprintCallable" => 0x0000000010000000UL,
                "CPF_BlueprintAuthorityOnly" => 0x0000000020000000UL,
                "CPF_SimpleDisplay" => 0x0000000100000000UL,
                "CPF_AdvancedDisplay" => 0x0000000200000000UL,
                "CPF_RepSkip" => 0x0000000400000000UL,
                "CPF_NonTransactional" => 0x0000000800000000UL,
                "CPF_EditorOnly" => 0x0000001000000000UL,
                "CPF_NoDestructor" => 0x0000002000000000UL,
                "CPF_HasGetValueTypeHash" => 0x0000080000000000UL,
                "CPF_NativeAccessSpecifierPublic" => 0x0000100000000000UL,
                "CPF_NativeAccessSpecifierProtected" => 0x0000200000000000UL,
                "CPF_NativeAccessSpecifierPrivate" => 0x0000400000000000UL,
                _ => 0UL,
            };
        }

        return value;
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IEnumerable<JsonElement> GetArray(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    private static string ShortName(string path)
    {
        var colon = path.LastIndexOf(':');
        var dot = path.LastIndexOf('.');
        var cut = Math.Max(colon, dot);
        return cut < 0 ? path : path[(cut + 1)..];
    }

    private static string ModuleOf(string path)
    {
        var cut = path.LastIndexOf('.');
        return cut < 0 ? "" : path[..cut];
    }
}
