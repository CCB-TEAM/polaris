// Roslyn 增量源生成器：读 AdditionalFiles 里的 *.usmap 与 functions.json，
// 用 Polaris.SdkGen 生成 SDK 源码（与 CLI `sdk` 用同一份 emitter）。
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
        var usmap = context.AdditionalTextsProvider
            .Where(t => t.Path.EndsWith(".usmap", StringComparison.OrdinalIgnoreCase))
            .Select((t, _) => t.Path)
            .Collect();

        var catalogs = context.AdditionalTextsProvider
            .Where(t => string.Equals(Path.GetFileName(t.Path), "functions.json", StringComparison.OrdinalIgnoreCase))
            .Select((t, ct) => t.GetText(ct)?.ToString() ?? string.Empty)
            .Collect();

        var input = usmap.Combine(catalogs);

        context.RegisterSourceOutput(input, (spc, pair) =>
        {
            var (usmapPaths, catalogsText) = pair;
            var usmapPath = usmapPaths.FirstOrDefault();
            var catalog = catalogsText.FirstOrDefault();
            if (string.IsNullOrEmpty(usmapPath) || string.IsNullOrEmpty(catalog))
            {
                return; // 没有输入就不生成（不报错，允许其他项目引用同一个生成器）
            }

            try
            {
                var usmapBytes = File.ReadAllBytes(usmapPath);
                var usmapFile = UsmapReader.Read(usmapBytes);
                var owners = JmapReader.Read(catalog);
                var result = SdkEmitter.Emit(usmapFile, owners);

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
