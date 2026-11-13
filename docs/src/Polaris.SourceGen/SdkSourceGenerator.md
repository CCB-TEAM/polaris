# `Polaris.SourceGen/SdkSourceGenerator`

**职责**：Roslyn 增量源生成器——从 `AdditionalFiles` 里的 `*.usmap` + `functions.json` 生成 SDK，逻辑与 CLI `sdk` 共用 `SdkEmitter`。
**对外入口**：`[Generator] SdkSourceGenerator.Initialize`。
**内部结构**：`AdditionalTextsProvider` 收集 usmap 路径与 catalog 文本 → 组合成可增量缓存的输入 → `SdkEmitter.Emit` → `AddSource`。
**依赖**：`Microsoft.CodeAnalysis`（SDK 自带 Roslyn）、`Polaris.SdkGen` 源码（**直接编进生成器**，保证自包含）。
**取舍**：生成器目标 `net10.0`（本机 SDK 10 的 Roslyn 能加载；netstandard2.0 离线还原不了）。usmap 是二进制，`AdditionalText` 拿不到字节，因此按 `Path` 读文件。

### `Initialize(context: IncrementalGeneratorInitializationContext): void`

**状态：✅ 已实现**（`samples/SdkGenConsumer` 编译期生成 485 文件并零错误构建；增量 ~2 s）

- **签名与契约**：注册一个 `RegisterSourceOutput`；没有输入时不生成、不报错。
- **输入输出**：AdditionalFiles 里必须有 `*.usmap` 与 `functions.json`（同名文件取第一个）；输出 hintName = 生成文件相对路径（`/`→`.`）。
- **关键分支（伪代码）**：
  ```
  usmapPaths = AdditionalFiles where .usmap → Path.Collect()
  catalogs   = AdditionalFiles where functions.json → GetText.Collect()
  RegisterSourceOutput(Combine): 无输入 → return
      UsmapReader.Read(File.ReadAllBytes(path)); JmapReader.Read(text)
      foreach file: AddSource(hint, text)
  失败 → POLARIS001 错误诊断（带异常全文）
  ```
- **边界与失败**：usmap 路径失效 → `POLARIS001`；生成器异常不会中断构建，但会报错——不要吞。
- **调用者与被调用者**：编译器调用；调用 `UsmapReader`、`JmapReader`、`SdkEmitter`。

## 接入方式（实测）

```xml
<ItemGroup>
  <AdditionalFiles Include="<kards.usmap>" />
  <AdditionalFiles Include="<functions.json>" />
  <Analyzer Include="<repo>/src/Polaris.SourceGen/bin/Debug/net10.0/Polaris.SourceGen.dll" />
</ItemGroup>
<ItemGroup>
  <Reference Include="Polaris.Sdk"><HintPath>.../Polaris.Sdk.dll</HintPath></Reference>
</ItemGroup>
```

注意：analyzer 的依赖 DLL **不会**被自动解析，所以 `Polaris.SdkGen` 的源码以 `<Compile Include="..\Polaris.SdkGen\*.cs" Link="SdkGen\..."/>` 编进生成器程序集。
