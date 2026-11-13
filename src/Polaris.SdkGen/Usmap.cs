// .usmap 二进制解析（只取 SDK 生成需要的部分：名字表 / 枚举 / schema / 扩展里的 module path + struct kind）。
//
// 格式依据 UAssetAPI v1.1.0 的 Usmap.Read（见 polaris-research 的记录）：
//   u16 magic(0x30C4) u8 version
//   [version>=1] i32 hasVersioning → [i32 UE4ver, i32 UE5ver, i32 customCount, (guid16+i32)*, u32 NetCL]
//   u8 compression; u32 compressedSize; u32 decompressedSize   （只支持 None）
//   i32 nameCount; name*（version>=2 用 i16 长度，否则 u8；ASCII，无结尾 0）
//   i32 enumCount; enum*（name 用 i32 索引；version>=3 用 i16 计数；version>=4 每条 i64 值 + name）
//   i32 schemaCount; schema*
//     name(i32) super(i32) u16 numProps u16 serializablePropCount
//     prop*: u16 schemaIndex u8 arraySize name(i32) propData
//   [CEXT] u32 magic(0x54584543) u8 layout i32 count (id[4] u32 len payload)*
using System;
using System.Collections.Generic;
using System.Text;

namespace Polaris.SdkGen;

public enum EUsmapPropertyType : byte
{
    ByteProperty,
    BoolProperty,
    IntProperty,
    FloatProperty,
    ObjectProperty,
    NameProperty,
    DelegateProperty,
    DoubleProperty,
    ArrayProperty,
    StructProperty,
    StrProperty,
    TextProperty,
    InterfaceProperty,
    MulticastDelegateProperty,
    WeakObjectProperty,
    LazyObjectProperty,
    AssetObjectProperty,
    SoftObjectProperty,
    UInt64Property,
    UInt32Property,
    UInt16Property,
    Int64Property,
    Int16Property,
    Int8Property,
    MapProperty,
    SetProperty,
    EnumProperty,
    FieldPathProperty,
    OptionalProperty,
    Utf8StrProperty,
    AnsiStrProperty,
    Unknown = 0xFF,
}

public sealed class UsmapEnumInfo
{
    public string Name = "";
    public string ModulePath = "";
    public readonly List<KeyValuePair<long, string>> Values = new();
}

public sealed class UsmapPropertyData
{
    public EUsmapPropertyType Type;
    public string? StructType;
    public string? EnumName;
    public UsmapPropertyData? Inner;
    public UsmapPropertyData? Value;

    public override string ToString() => Type.ToString();
}

public sealed class UsmapPropertyInfo
{
    public string Name = "";
    public int SchemaIndex;
    public int SerializedIndex;
    public int ArraySize;
    public ulong Flags;
    public UsmapPropertyData Data = new();
}

public sealed class UsmapSchemaInfo
{
    public string Name = "";
    public string ModulePath = "";
    public string SuperName = "";
    public string SuperModulePath = "";
    public int PropCount;
    public int StructKind; // 0 未知 / 1 UScriptStruct / 2 UClass（EATR 扩展）
    public readonly List<UsmapPropertyInfo> Properties = new();

    public string FullPath => string.IsNullOrEmpty(ModulePath) ? Name : ModulePath + "." + Name;
}

public sealed class UsmapFile
{
    public readonly List<string> Names = new();
    public readonly Dictionary<string, UsmapEnumInfo> Enums = new(StringComparer.Ordinal);
    public readonly Dictionary<string, UsmapSchemaInfo> Schemas = new(StringComparer.Ordinal);
    public int Version;
}

public static class UsmapReader
{
    public static UsmapFile Read(byte[] data)
    {
        var r = new Reader(data);
        var file = new UsmapFile();

        var magic = r.U16();
        if (magic != 0x30C4)
        {
            throw new FormatException($".usmap magic 不匹配: 0x{magic:X4}");
        }

        file.Version = r.U8();
        if (file.Version >= 1)
        {
            var hasVersioning = r.I32() > 0;
            if (hasVersioning)
            {
                r.I32(); // UE4 version
                r.I32(); // UE5 version
                var custom = r.I32();
                for (var i = 0; i < custom; i++)
                {
                    r.Skip(16);
                    r.I32();
                }

                r.U32(); // NetCL
            }
        }

        var compression = r.U8();
        if (compression != 0)
        {
            throw new NotSupportedException($".usmap 压缩方式 {compression} 未支持（KARDS 为 0）");
        }

        r.U32(); // compressedSize
        r.U32(); // decompressedSize

        var nameCount = r.I32();
        for (var i = 0; i < nameCount; i++)
        {
            var len = file.Version >= 2 ? r.U16() : r.U8();
            file.Names.Add(r.Ascii(len));
        }

        var enumCount = r.I32();
        var enumByIndex = new UsmapEnumInfo?[enumCount];
        for (var i = 0; i < enumCount; i++)
        {
            var name = r.Name(file.Names);
            var info = new UsmapEnumInfo { Name = name };
            var entryCount = file.Version >= 3 ? r.U16() : r.U8();
            if (file.Version >= 4)
            {
                for (var j = 0; j < entryCount; j++)
                {
                    var value = r.I64();
                    info.Values.Add(new KeyValuePair<long, string>(value, r.Name(file.Names)));
                }
            }
            else
            {
                for (var j = 0; j < entryCount; j++)
                {
                    info.Values.Add(new KeyValuePair<long, string>(j, r.Name(file.Names)));
                }
            }

            enumByIndex[i] = info;
            if (!file.Enums.ContainsKey(name))
            {
                file.Enums[name] = info;
            }
        }

        var schemaCount = r.I32();
        var schemaByIndex = new UsmapSchemaInfo?[schemaCount];
        for (var i = 0; i < schemaCount; i++)
        {
            var schema = new UsmapSchemaInfo
            {
                Name = r.Name(file.Names),
                SuperName = r.Name(file.Names) ?? "",
                PropCount = r.U16(),
            };
            var serializable = r.U16();
            for (var j = 0; j < serializable; j++)
            {
                var prop = new UsmapPropertyInfo
                {
                    SchemaIndex = r.U16(),
                    SerializedIndex = j,
                    ArraySize = r.U8(),
                    Name = r.Name(file.Names) ?? "",
                };
                prop.Data = ReadPropData(r, file.Names);
                // 数组元素展开（UAssetAPI 的行为）：只保留第 0 个，其余标记跳过
                schema.Properties.Add(prop);
                for (var k = 1; k < prop.ArraySize; k++)
                {
                    schema.Properties.Add(new UsmapPropertyInfo
                    {
                        SchemaIndex = prop.SchemaIndex + k,
                        SerializedIndex = prop.SerializedIndex,
                        ArraySize = prop.ArraySize,
                        Name = prop.Name,
                        Data = prop.Data,
                    });
                }
            }

            schemaByIndex[i] = schema;
            file.Schemas[schema.FullPath] = schema;
        }

        // 扩展：CEXT / PPTH（module path）/ EATR（struct kind + flags）
        while (r.Remaining > 0)
        {
            var magic2 = r.U32();
            if (magic2 == 0x54584543) // "CEXT"
            {
                var layout = r.U8();
                if (layout != 0)
                {
                    break;
                }

                var extCount = r.I32();
                for (var i = 0; i < extCount; i++)
                {
                    var id = r.Ascii(4);
                    var len = (int)r.U32();
                    var end = r.Position + len;
                    ApplyExtension(id, r, file, enumByIndex, schemaByIndex);
                    if (r.Position > end)
                    {
                        throw new FormatException($".usmap 扩展 {id} 越界");
                    }

                    r.Position = end;
                }
            }
            else if (magic2 == 1) // legacy MODL：module path 列表 + 每个 schema 的 u8/u16 索引
            {
                var numModulePaths = r.U16();
                var modulePaths = new string[numModulePaths];
                for (var i = 0; i < numModulePaths; i++)
                {
                    modulePaths[i] = r.Name(file.Names) ?? "";
                }

                var wide = numModulePaths > byte.MaxValue;
                foreach (var schema in schemaByIndex)
                {
                    if (schema is null)
                    {
                        continue;
                    }

                    var idx = wide ? r.U16() : r.U8();
                    schema.ModulePath = modulePaths[idx];
                }
            }
            else
            {
                break;
            }
        }

        // 有 module path 的 schema 重新登记成 /Script/X.Name 键
        file.Schemas.Clear();
        foreach (var schema in schemaByIndex)
        {
            if (schema is null)
            {
                continue;
            }

            file.Schemas[schema.FullPath] = schema;
        }

        foreach (var e in enumByIndex)
        {
            if (e is not null && !string.IsNullOrEmpty(e.ModulePath) && !file.Enums.ContainsKey(e.ModulePath + "." + e.Name))
            {
                file.Enums[e.ModulePath + "." + e.Name] = e;
            }
        }

        return file;
    }

    private static void ApplyExtension(
        string id,
        Reader r,
        UsmapFile file,
        UsmapEnumInfo?[] enumByIndex,
        UsmapSchemaInfo?[] schemaByIndex)
    {
        switch (id)
        {
            case "PPTH":
            {
                var ver = r.U8();
                if (ver > 0)
                {
                    return;
                }

                var numEnums = r.I32();
                for (var i = 0; i < numEnums; i++)
                {
                    var module = r.Name(file.Names) ?? "";
                    if (i < enumByIndex.Length && enumByIndex[i] is { } e)
                    {
                        e.ModulePath = module;
                    }
                }

                var numSchemas = r.I32();
                for (var i = 0; i < numSchemas; i++)
                {
                    // PPTH 每个 schema 只读一个 module path；短名沿用 schema 自身的 Name
                    var module = r.Name(file.Names) ?? "";
                    if (i < schemaByIndex.Length && schemaByIndex[i] is { } s)
                    {
                        s.ModulePath = module;
                    }
                }

                break;
            }

            case "EATR":
            {
                var ver = r.U8();
                if (ver > 0)
                {
                    return;
                }

                var numEnums = r.I32();
                for (var i = 0; i < numEnums; i++)
                {
                    r.I32();
                }

                var numSchemas = r.I32();
                for (var i = 0; i < numSchemas; i++)
                {
                    var kind = r.U8();
                    r.I32(); // class flags
                    var numProps = r.I32();
                    if (i < schemaByIndex.Length && schemaByIndex[i] is { } s)
                    {
                        s.StructKind = kind;
                    }

                    for (var j = 0; j < numProps; j++)
                    {
                        var flags = r.U64();
                        if (i < schemaByIndex.Length && schemaByIndex[i] is { } schema)
                        {
                            for (var k = 0; k < schema.Properties.Count; k++)
                            {
                                if (schema.Properties[k].SerializedIndex == j)
                                {
                                    schema.Properties[k].Flags = flags;
                                }
                            }
                        }
                    }
                }

                break;
            }
        }
    }

    private static UsmapPropertyData ReadPropData(Reader r, List<string> names)
    {
        var type = (EUsmapPropertyType)r.U8();
        var data = new UsmapPropertyData { Type = type };
        switch (type)
        {
            case EUsmapPropertyType.EnumProperty:
                data.Inner = ReadPropData(r, names);
                data.EnumName = r.Name(names);
                break;
            case EUsmapPropertyType.StructProperty:
                data.StructType = r.Name(names);
                break;
            case EUsmapPropertyType.SetProperty:
            case EUsmapPropertyType.ArrayProperty:
            case EUsmapPropertyType.OptionalProperty:
                data.Inner = ReadPropData(r, names);
                break;
            case EUsmapPropertyType.MapProperty:
                data.Inner = ReadPropData(r, names);
                data.Value = ReadPropData(r, names);
                break;
        }

        return data;
    }

    private sealed class Reader(byte[] data)
    {
        public int Position { get; set; }
        public int Remaining => data.Length - Position;

        public byte U8() => data[Position++];

        public ushort U16()
        {
            var v = BitConverter.ToUInt16(data, Position);
            Position += 2;
            return v;
        }

        public uint U32()
        {
            var v = BitConverter.ToUInt32(data, Position);
            Position += 4;
            return v;
        }

        public int I32()
        {
            var v = BitConverter.ToInt32(data, Position);
            Position += 4;
            return v;
        }

        public long I64()
        {
            var v = BitConverter.ToInt64(data, Position);
            Position += 8;
            return v;
        }

        public ulong U64()
        {
            var v = BitConverter.ToUInt64(data, Position);
            Position += 8;
            return v;
        }

        public void Skip(int n) => Position += n;

        public string Ascii(int len)
        {
            var s = Encoding.ASCII.GetString(data, Position, len);
            Position += len;
            return s;
        }

        public string? Name(List<string> names)
        {
            var idx = I32();
            if (idx < 0) return null;
            if (idx >= names.Count)
            {
                throw new FormatException($".usmap 名字索引越界: {idx} >= {names.Count} @{Position - 4}");
            }

            return names[idx];
        }
    }
}
