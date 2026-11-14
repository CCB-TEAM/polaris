// jmap functions.json 的读取：所有者（原生类）→ 函数 → 参数。
// 注意：本 catalog 没有 ref/const 字段（老 extractor），参数方向只能靠 dir。
using System;
using System.Collections.Generic;

namespace Polaris.SdkGen;

public sealed class JmapType
{
    public string Type = "";
    public string? ClassPath;
    public string? EnumPath;
    public JmapType? Inner;
    public JmapType? Key;
    public JmapType? Value;

    public static JmapType FromJson(JsonValue? value)
    {
        var t = new JmapType();
        var o = value?.AsObject();
        if (o is null)
        {
            return t;
        }

        t.Type = o["type"]?.AsString() ?? "";
        t.ClassPath = o["class"]?.AsString();
        t.EnumPath = o["enum"]?.AsString();
        t.Inner = o["inner"] is null ? null : FromJson(o["inner"]);
        t.Key = o["key"] is null ? null : FromJson(o["key"]);
        t.Value = o["value"] is null ? null : FromJson(o["value"]);
        return t;
    }
}

public sealed class JmapParam
{
    public string Name = "";
    public string Dir = "in";
    public bool Ref;
    public bool Const;
    public JmapType Type = new();
}

public sealed class JmapFunction
{
    public string Name = "";
    public string Flags = "";
    public readonly List<JmapParam> Params = new();
}

public sealed class JmapOwner
{
    public string Path = "";
    public string Type = "";
    public string? Super;
    public readonly List<JmapFunction> Functions = new();
}

public static class JmapReader
{
    public static IReadOnlyDictionary<string, JmapOwner> Read(string json)
    {
        var root = Json.Parse(json).AsObject()
                   ?? throw new FormatException("functions.json 顶层不是对象");
        var objects = root["objects"]?.AsObject()
                      ?? throw new FormatException("functions.json 缺少 objects");

        var owners = new Dictionary<string, JmapOwner>(StringComparer.Ordinal);
        foreach (var (path, ownerValue) in objects.Members)
        {
            var ownerObj = ownerValue.AsObject();
            if (ownerObj is null)
            {
                continue;
            }

            var owner = new JmapOwner
            {
                Path = path,
                Type = ownerObj["type"]?.AsString() ?? "",
                Super = ownerObj["super"]?.AsString(),
            };

            var functions = ownerObj["functions"]?.AsObject();
            if (functions is not null)
            {
                foreach (var (name, fnValue) in functions.Members)
                {
                    var fnObj = fnValue.AsObject();
                    if (fnObj is null)
                    {
                        continue;
                    }

                    var fn = new JmapFunction
                    {
                        Name = name,
                        Flags = fnObj["flags"]?.AsString() ?? "",
                    };

                    var ps = fnObj["params"]?.AsArray();
                    if (ps is not null)
                    {
                        foreach (var p in ps.Items)
                        {
                            var po = p.AsObject();
                            if (po is null)
                            {
                                continue;
                            }

                            fn.Params.Add(new JmapParam
                            {
                                Name = po["name"]?.AsString() ?? "",
                                Dir = po["dir"]?.AsString() ?? "in",
                                Type = JmapType.FromJson(po),
                            });
                        }
                    }

                    owner.Functions.Add(fn);
                }
            }

            owners[path] = owner;
        }

        return owners;
    }
}
