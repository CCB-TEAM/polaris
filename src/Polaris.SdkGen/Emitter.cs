// UHT 风格 SDK 生成：类继承真实父类、属性是 [UProperty] 字段、方法是 [UFunction] 声明。
// 不引入 KArg/KNode 之类的中间模型——生成的代码就是「C# 版 UHT 头文件」。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Polaris.SdkGen;

public sealed record GeneratedFile(string RelativePath, string Text);

public sealed record SdkOptions(IReadOnlyCollection<string>? ModuleFilter = null, bool EmitMembers = true);

public sealed class SdkResult
{
    public List<GeneratedFile> Files { get; } = new();
    public int Classes;
    public int Structs;
    public int Enums;
    public int Methods;
    public int Members;
}

public static class SdkEmitter
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
        "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
        "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
        "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    public static SdkResult Emit(
        UsmapFile usmap,
        IReadOnlyDictionary<string, JmapOwner> owners,
        SdkOptions? options = null)
    {
        options ??= new SdkOptions();
        var names = new Names(usmap, owners, options.ModuleFilter);
        var result = new SdkResult();

        var enums = new SortedDictionary<string, StringBuilder>(StringComparer.Ordinal);
        var structs = new SortedDictionary<string, StringBuilder>(StringComparer.Ordinal);
        var classes = new SortedDictionary<string, StringBuilder>(StringComparer.Ordinal);

        // ---- 枚举 ----
        foreach (var e in usmap.Enums.Values.Distinct()
                     .Where(e => names.Included(e.ModulePath))
                     .OrderBy(e => e.ModulePath, StringComparer.Ordinal)
                     .ThenBy(e => e.Name, StringComparer.Ordinal))
        {
            var typeName = names.TypeName(e.ModulePath + "." + e.Name);
            if (typeName is null)
            {
                continue;
            }

            EmitEnum(Get(enums, names.NamespaceOf(e.ModulePath)), typeName, e);
            result.Enums++;
        }

        // ---- 结构体 ----
        foreach (var s in usmap.Schemas.Values
                     .Where(s => s.StructKind == 1 && names.Included(s.ModulePath))
                     .OrderBy(s => s.FullPath, StringComparer.Ordinal))
        {
            var typeName = names.TypeName(s.FullPath);
            if (typeName is null)
            {
                continue;
            }

            var sb = Get(structs, names.NamespaceOf(s.ModulePath));
            var super = names.QualifiedType(s.SuperName) ?? "global::Polaris.Sdk.UStruct";
            sb.AppendLine($"/// <summary>{Escape(s.FullPath)}</summary>");
            sb.AppendLine($"[UStruct(\"{Escape(s.FullPath)}\")]");
            sb.AppendLine($"public partial class {typeName} : {super}");
            sb.AppendLine("{");
            EmitProperties(sb, s, typeName, names, options, result);
            sb.AppendLine("}");
            sb.AppendLine();
            result.Structs++;
        }

        // ---- 类：usmap 的 UClass ∪ jmap 的 owner ----
        var classPaths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var s in usmap.Schemas.Values.Where(s => s.StructKind == 2 && names.Included(s.ModulePath)))
        {
            classPaths.Add(s.FullPath);
        }

        foreach (var o in owners.Values.Where(o => o.Functions.Count > 0 && ModulePathOf(o.Path).Length > 0 && names.Included(ModulePathOf(o.Path))))
        {
            classPaths.Add(o.Path);
        }

        foreach (var path in classPaths)
        {
            var typeName = names.TypeName(path);
            if (typeName is null)
            {
                continue;
            }

            var sb = Get(classes, names.NamespaceOf(ModulePathOf(path)));
            var super = usmap.Schemas.TryGetValue(path, out var schema) && !string.IsNullOrEmpty(schema.SuperName)
                ? names.QualifiedType(schema.SuperName) ?? "global::Polaris.Sdk.UObject"
                : "global::Polaris.Sdk.UObject";

            sb.AppendLine($"/// <summary>{Escape(path)}</summary>");
            sb.AppendLine($"[UClass(\"{Escape(path)}\")]");
            sb.AppendLine($"public partial class {typeName} : {super}");
            sb.AppendLine("{");

            var used = new HashSet<string>(StringComparer.Ordinal) { typeName };
            if (schema is not null)
            {
                used.UnionWith(EmitProperties(sb, schema, typeName, names, options, result));
            }

            if (owners.TryGetValue(path, out var owner))
            {
                EmitMethods(sb, owner, typeName, used, names, result);
            }

            sb.AppendLine("}");
            sb.AppendLine();
            result.Classes++;
        }

        foreach (var (ns, sb) in enums)
        {
            result.Files.Add(new GeneratedFile($"Polaris.Sdk/{ns}/{ns}.Enums.g.cs", sb.ToString()));
        }

        foreach (var (ns, sb) in structs)
        {
            result.Files.Add(new GeneratedFile($"Polaris.Sdk/{ns}/{ns}.Structs.g.cs", sb.ToString()));
        }

        foreach (var (ns, sb) in classes)
        {
            result.Files.Add(new GeneratedFile($"Polaris.Sdk/{ns}/{ns}.Classes.g.cs", sb.ToString()));
        }

        return result;
    }

    private static StringBuilder Get(SortedDictionary<string, StringBuilder> map, string ns)
    {
        if (map.TryGetValue(ns, out var sb))
        {
            return sb;
        }

        sb = new StringBuilder();
        sb.AppendLine("// <auto-generated /> Polaris UHT-style SDK");
        sb.AppendLine("#nullable disable");
        sb.AppendLine("using Polaris.Sdk;");
        sb.AppendLine();
        sb.AppendLine($"namespace Polaris.Sdk.{ns};");
        sb.AppendLine();
        map[ns] = sb;
        return sb;
    }

    private static void EmitEnum(StringBuilder sb, string typeName, UsmapEnumInfo e)
    {
        var values = e.Values.OrderBy(kv => kv.Key).ToList();
        var max = values.Count == 0 ? 0 : values.Max(kv => kv.Key);
        var min = values.Count == 0 ? 0 : values.Min(kv => kv.Key);
        var underlying = min >= 0 && max <= byte.MaxValue ? "byte" : "long";
        sb.AppendLine($"/// <summary>{Escape(e.ModulePath)}.{Escape(e.Name)}</summary>");
        sb.AppendLine($"[UEnum(\"{Escape(e.ModulePath)}.{Escape(e.Name)}\")]");
        sb.AppendLine($"public enum {typeName} : {underlying}");
        sb.AppendLine("{");
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kv in values)
        {
            var name = UniqueMember(Sanitize(kv.Value), used);
            if (name == typeName)
            {
                name += "_";
            }

            var suffix = underlying == "byte" ? kv.Key.ToString() : kv.Key + "L";
            sb.AppendLine($"    {name} = {suffix},");
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static HashSet<string> EmitProperties(
        StringBuilder sb,
        UsmapSchemaInfo schema,
        string typeName,
        Names names,
        SdkOptions options,
        SdkResult result)
    {
        var emitted = new HashSet<string>(StringComparer.Ordinal) { typeName };
        if (!options.EmitMembers)
        {
            return emitted;
        }

        foreach (var p in schema.Properties.OrderBy(p => p.SchemaIndex))
        {
            var memberName = Sanitize(p.Name);
            if (!emitted.Add(memberName))
            {
                continue; // 数组展开的重复，或与类型同名
            }

            var type = names.RenderUsmapType(p.Data);
            sb.AppendLine($"    [UProperty(0x{p.Flags:X16})]");
            sb.AppendLine($"    public {type} {memberName};");
            result.Members++;
        }

        return emitted;
    }

    private static void EmitMethods(
        StringBuilder sb,
        JmapOwner owner,
        string typeNameOfClass,
        HashSet<string> usedNames,
        Names names,
        SdkResult result)
    {
        var usedSignatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fn in owner.Functions.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var isStatic = fn.Flags.Contains("FUNC_Static", StringComparison.Ordinal);
            var ret = fn.Params.FirstOrDefault(p => p.Dir == "return");
            var retType = ret is null ? "void" : names.RenderJmapType(ret.Type);
            var ps = fn.Params.Where(p => p.Dir != "return").ToList();
            var paramText = string.Join(", ", ps.Select(p =>
            {
                var prefix = p.Dir == "out" ? "out " : "";
                return $"{prefix}{names.RenderJmapType(p.Type)} {Sanitize(p.Name)}";
            }));

            var name = Sanitize(fn.Name);
            var signature = name + "(" + paramText + ")";
            if (!usedSignatures.Add(signature))
            {
                name += "_" + usedSignatures.Count;
            }

            if (usedNames.Contains(name))
            {
                name += "_Fn";
            }

            sb.AppendLine($"    [UFunction(\"{Escape(fn.Flags)}\")]");
            sb.AppendLine($"    public {(isStatic ? "static " : "")}{retType} {name}({paramText}) => throw UhtStub.Call();");
            result.Methods++;
        }
    }

    private static string ModulePathOf(string ownerPath)
    {
        var dot = ownerPath.LastIndexOf('.');
        return dot < 0 ? "" : ownerPath[..dot];
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Sanitize(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "_";
        }

        var sb = new StringBuilder(name.Length + 1);
        foreach (var c in name)
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        var result = sb.ToString();
        if (char.IsDigit(result[0]))
        {
            result = "_" + result;
        }

        return Keywords.Contains(result) ? "@" + result : result;
    }

    private static string UniqueMember(string name, HashSet<string> used)
    {
        var candidate = name;
        var i = 2;
        while (!used.Add(candidate))
        {
            candidate = name + "_" + i++;
        }

        return candidate;
    }

    /// <summary>全路径 → namespace + 类型名；namespace 内重名时加后缀（确定性，按排序登记）。</summary>
    private sealed class Names
    {
        private readonly IReadOnlyCollection<string>? _filter;
        private readonly UsmapFile _usmap;
        private readonly Dictionary<string, string> _typeByPath = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _nsByModule = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _usedByNs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _shortCandidates = new(StringComparer.Ordinal);

        public Names(UsmapFile usmap, IReadOnlyDictionary<string, JmapOwner> owners, IReadOnlyCollection<string>? filter)
        {
            _usmap = usmap;
            _filter = filter;

            foreach (var s in usmap.Schemas.Values.OrderBy(s => s.FullPath, StringComparer.Ordinal))
            {
                if (Included(s.ModulePath))
                {
                    Register(s.FullPath, s.ModulePath);
                }
            }

            foreach (var e in usmap.Enums.Values.Distinct().OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                if (Included(e.ModulePath))
                {
                    Register(e.ModulePath + "." + e.Name, e.ModulePath);
                }
            }

            foreach (var o in owners.Values.OrderBy(o => o.Path, StringComparer.Ordinal))
            {
                var module = ModulePathOf(o.Path);
                if (module.Length > 0 && Included(module) && !_typeByPath.ContainsKey(o.Path))
                {
                    Register(o.Path, module);
                }
            }
        }

        public bool Included(string modulePath)
        {
            if (_filter is null || _filter.Count == 0)
            {
                return true;
            }

            var shortName = ModuleShort(modulePath);
            return _filter.Any(f => string.Equals(f, shortName, StringComparison.OrdinalIgnoreCase));
        }

        public string NamespaceOf(string modulePath)
        {
            if (_nsByModule.TryGetValue(modulePath, out var ns))
            {
                return ns;
            }

            ns = SanitizeNamespace(ModuleShort(modulePath));
            _nsByModule[modulePath] = ns;
            return ns;
        }

        public string? QualifiedType(string? path)
        {
            var full = ResolvePath(path);
            if (full is null)
            {
                return null;
            }

            var type = full[(full.LastIndexOf('.') + 1)..];
            return Qualified(full, type);
        }

        public string? TypeName(string? path)
        {
            var full = ResolvePath(path);
            return full is null ? null : full[(full.LastIndexOf('.') + 1)..];
        }

        private string? ResolvePath(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            if (_typeByPath.ContainsKey(path))
            {
                return path;
            }

            if (_shortCandidates.TryGetValue(path, out var list) && list.Count == 1)
            {
                return list[0];
            }

            return null;
        }

        public string RenderJmapType(JmapType t)
        {
            switch (t.Type)
            {
                case "ObjectProperty":
                case "ClassProperty":
                case "InterfaceProperty":
                case "WeakObjectProperty":
                case "LazyObjectProperty":
                case "SoftObjectProperty":
                case "AssetObjectProperty":
                    return Ref(t.ClassPath) ?? "global::Polaris.Sdk.UObject";
                case "StructProperty":
                    return Struct(t.ClassPath) ?? "global::Polaris.Sdk.UStruct";
                case "EnumProperty":
                    return Enum(t.EnumPath) ?? "long";
                case "ArrayProperty":
                    return $"global::Polaris.Sdk.TArray<{RenderJmapType(t.Inner ?? new JmapType())}>";
                case "SetProperty":
                    return $"global::Polaris.Sdk.TSet<{RenderJmapType(t.Inner ?? new JmapType())}>";
                case "MapProperty":
                    return $"global::Polaris.Sdk.TMap<{RenderJmapType(t.Key ?? new JmapType())}, {RenderJmapType(t.Value ?? new JmapType())}>";
                case "ByteProperty":
                    return t.EnumPath is null ? "byte" : Enum(t.EnumPath) ?? "byte";
                default:
                    return Scalar(t.Type);
            }
        }

        public string RenderUsmapType(UsmapPropertyData d)
        {
            switch (d.Type)
            {
                case EUsmapPropertyType.ObjectProperty:
                case EUsmapPropertyType.InterfaceProperty:
                case EUsmapPropertyType.WeakObjectProperty:
                case EUsmapPropertyType.LazyObjectProperty:
                case EUsmapPropertyType.AssetObjectProperty:
                case EUsmapPropertyType.SoftObjectProperty:
                    return Ref(d.StructType) ?? "global::Polaris.Sdk.UObject";
                case EUsmapPropertyType.StructProperty:
                    return Struct(d.StructType) ?? "global::Polaris.Sdk.UStruct";
                case EUsmapPropertyType.EnumProperty:
                    return Enum(d.EnumName) ?? "long";
                case EUsmapPropertyType.ArrayProperty:
                    return $"global::Polaris.Sdk.TArray<{RenderUsmapType(d.Inner ?? new UsmapPropertyData())}>";
                case EUsmapPropertyType.SetProperty:
                    return $"global::Polaris.Sdk.TSet<{RenderUsmapType(d.Inner ?? new UsmapPropertyData())}>";
                case EUsmapPropertyType.MapProperty:
                    return $"global::Polaris.Sdk.TMap<{RenderUsmapType(d.Inner ?? new UsmapPropertyData())}, {RenderUsmapType(d.Value ?? new UsmapPropertyData())}>";
                case EUsmapPropertyType.ByteProperty:
                    return d.EnumName is null ? "byte" : Enum(d.EnumName) ?? "byte";
                default:
                    return Scalar(d.Type.ToString());
            }
        }

        private string? Ref(string? path) => QualifiedType(path);

        private string? Struct(string? path) => QualifiedType(path);

        private string? Enum(string? path)
        {
            if (path is null)
            {
                return null;
            }

            if (_usmap.Enums.ContainsKey(path))
            {
                return QualifiedType(path);
            }

            var candidates = _usmap.Enums.Keys
                .Where(k => k.EndsWith("." + path, StringComparison.Ordinal) || k == path)
                .Distinct()
                .ToList();
            return candidates.Count == 1 ? QualifiedType(candidates[0]) : null;
        }

        private string Qualified(string path, string type)
        {
            var full = ResolvePath(path);
            if (full is not null && _typeByPath.TryGetValue(full, out var registered))
            {
                var registeredName = registered[(registered.LastIndexOf('.') + 1)..];
                return "global::Polaris.Sdk." + registered[..registered.LastIndexOf('.')] + "." + registeredName;
            }

            return type;
        }

        private static string Scalar(string type) => type switch
        {
            "IntProperty" => "int",
            "Int64Property" => "long",
            "UInt64Property" => "ulong",
            "Int32Property" => "int",
            "UInt32Property" => "uint",
            "Int16Property" => "short",
            "UInt16Property" => "ushort",
            "Int8Property" => "sbyte",
            "ByteProperty" => "byte",
            "FloatProperty" => "float",
            "DoubleProperty" => "double",
            "BoolProperty" => "bool",
            "StrProperty" => "string",
            "Utf8StrProperty" => "string",
            "AnsiStrProperty" => "string",
            "NameProperty" => "global::Polaris.Sdk.FName",
            "TextProperty" => "global::Polaris.Sdk.FText",
            "FieldPathProperty" => "global::Polaris.Sdk.FFieldPath",
            "DelegateProperty" => "global::Polaris.Sdk.TDelegate",
            "MulticastDelegateProperty" => "global::Polaris.Sdk.TDelegate",
            _ => "global::Polaris.Sdk.UStruct",
        };

        private void Register(string path, string modulePath)
        {
            var ns = NamespaceOf(modulePath);
            if (!_usedByNs.TryGetValue(ns, out var used))
            {
                used = new HashSet<string>(StringComparer.Ordinal);
                _usedByNs[ns] = used;
            }

            var shortName = path[(path.LastIndexOf('.') + 1)..];
            var baseName = Sanitize(shortName);
            var candidate = baseName;
            var i = 2;
            while (!used.Add(candidate))
            {
                candidate = baseName + "_" + i++;
            }

            _typeByPath[path] = ns + "." + candidate;

            if (!_shortCandidates.TryGetValue(shortName, out var list))
            {
                list = new List<string>();
                _shortCandidates[shortName] = list;
            }

            list.Add(path);
        }

        private static string ModuleShort(string modulePath)
        {
            var s = modulePath.TrimStart('/');
            var slash = s.LastIndexOf('/');
            return slash < 0 ? s : s[(slash + 1)..];
        }

        private static string SanitizeNamespace(string module)
        {
            var sb = new StringBuilder(module.Length);
            foreach (var c in module)
            {
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            }

            var s = sb.ToString();
            if (s.Length == 0)
            {
                return "Unknown";
            }

            if (char.IsDigit(s[0]))
            {
                s = "_" + s;
            }

            return char.ToUpperInvariant(s[0]) + s[1..];
        }
    }
}
