// UHT 风格的 SDK 运行时声明：只提供「类型系统 + 元数据特性」，不含引擎逻辑。
// 生成代码里的属性/方法都是声明（属性是字段、方法体 throw UhtStub.Call()），
// 真正的语义在 Polaris 编译器里（C# → Kismet 字节码）。
using System;

namespace Polaris.Sdk;

/// <summary>UE UObject 基类（UHT: <c>class UObject</c>）。</summary>
public class UObject
{
}

/// <summary>UE UStruct 基类（UHT: 结构体的公共基类）。</summary>
public class UStruct
{
}

/// <summary>UHT 的 TArray&lt;T&gt;。</summary>
public sealed class TArray<T>
{
}

/// <summary>UHT 的 TSet&lt;T&gt;。</summary>
public sealed class TSet<T>
{
}

/// <summary>UHT 的 TMap&lt;K,V&gt;。</summary>
public sealed class TMap<K, V>
{
}

/// <summary>FName。</summary>
public readonly struct FName
{
    private readonly string? _value;
    public FName(string value) => _value = value;
    public override string ToString() => _value ?? "None";
}

/// <summary>FText。</summary>
public sealed class FText
{
    private readonly string? _value;
    public FText(string value) => _value = value;
    public override string ToString() => _value ?? "";
}

/// <summary>FFieldPath。</summary>
public sealed class FFieldPath
{
}

/// <summary>UHT 的 TDelegate / 多播委托占位。</summary>
public sealed class TDelegate
{
}

/// <summary>声明面占位：被真的执行到说明调用链有 bug。</summary>
public static class UhtStub
{
    public static Exception Call()
        => throw new InvalidOperationException(
            "Polaris SDK 声明面被执行了：这些类型只用于编译期翻译，不应在运行时调用");
}

/// <summary>UHT: <c>UCLASS(...)</c>；路径为 <c>/Script/Engine.Actor</c>。</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class UClassAttribute(string path) : Attribute
{
    public string Path { get; } = path;
}

/// <summary>UHT: <c>USTRUCT(...)</c>。</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class UStructAttribute(string path) : Attribute
{
    public string Path { get; } = path;
}

/// <summary>UHT: <c>UENUM(...)</c>。</summary>
[AttributeUsage(AttributeTargets.Enum, Inherited = false)]
public sealed class UEnumAttribute(string path) : Attribute
{
    public string Path { get; } = path;
}

/// <summary>UHT: <c>UPROPERTY(...)</c>；保存 UE 的 EPropertyFlags 位。</summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class UPropertyAttribute(ulong flags) : Attribute
{
    public ulong Flags { get; } = flags;
}

/// <summary>Polaris mod 注解：目标资产（相对 Content 或绝对路径）。</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PolarisAssetAttribute(string path) : Attribute
{
    public string Path { get; } = path;
}

/// <summary>Polaris mod 注解：这个方法要替换的目标 UFunction 名。</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class PolarisReplaceAttribute(string function) : Attribute
{
    public string Function { get; } = function;
}

/// <summary>UHT: <c>UFUNCTION(...)</c>；保留原始 flags 字符串。</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class UFunctionAttribute(string flags) : Attribute
{
    public string Flags { get; } = flags;
}
