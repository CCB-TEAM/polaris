// Roslyn 增量源生成器：优先读 AdditionalFiles 里的 *.jmap（纯 jmap），
// 否则退回 *.usmap + functions.json。两种输入都用同一个 SdkEmitter。
using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Polaris.SdkGen;

namespace Polaris.SourceGen;

[Generator(LanguageNames.CSharp)]
public sealed class SdkSourceGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor EmitFailed = new(
        id: "POLARIS001",
        title: "Polaris SDK 生成失败",
        messageFormat: "{0}",
        category: "Polaris",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var jmaps = context.AdditionalTextsProvider
            .Where(t => t.Path.EndsWith(".jmap", StringComparison.OrdinalIgnoreCase))
            .Select((t, _) => t.Path)
            .Collect();

        var usmaps = context.AdditionalTextsProvider
            .Where(t => t.Path.EndsWith(".usmap", StringComparison.OrdinalIgnoreCase))
            .Select((t, _) => t.Path)
            .Collect();

        var catalogs = context.AdditionalTextsProvider
            .Where(t => string.Equals(Path.GetFileName(t.Path), "functions.json", StringComparison.OrdinalIgnoreCase))
            .Select((t, ct) => t.GetText(ct)?.ToString() ?? string.Empty)
            .Collect();

        var input = jmaps.Combine(usmaps).Combine(catalogs);

        context.RegisterSourceOutput(input, (spc, pair) =>
        {
            var (jmapPaths, usmapPaths) = pair.Left;
            var catalogsText = pair.Right;

            try
            {
                SdkResult? result = null;

                if (jmapPaths.FirstOrDefault() is { } jmapPath)
                {
                    var parsed = JmapFileReader.ReadFile(jmapPath);
                    result = SdkEmitter.Emit(parsed.Usmap, parsed.Owners);
                }
                else if (usmapPaths.FirstOrDefault() is { } usmapPath && catalogsText.FirstOrDefault() is { } catalog)
                {
                    var usmapFile = UsmapReader.Read(File.ReadAllBytes(usmapPath));
                    var owners = JmapReader.Read(catalog);
                    result = SdkEmitter.Emit(usmapFile, owners);
                }

                if (result is null)
                {
                    return; // 没有输入就不生成（不报错，允许其他项目引用同一个生成器）
                }

                foreach (var file in result.Files)
                {
                    var hintName = file.RelativePath
                        .Replace('\\', '.')
                        .Replace('/', '.');
                    spc.AddSource(hintName, file.Text);
                }
            }
            catch (Exception ex)
            {
                spc.ReportDiagnostic(Diagnostic.Create(EmitFailed, Location.None, ex.ToString()));
            }
        });
    }
}
