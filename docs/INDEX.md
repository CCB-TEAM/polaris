# Polaris 文档总览

**当前阶段：M1 原生 SDK + M2 CDO + mod pak 导出已落地（静态闸全绿，运行期待验）。** 最近更新：2026-12。

- 写 mod：[mod-authoring.md](mod-authoring.md) · 路线与闸门：[PLAN.md](PLAN.md) · 卡属性字段：[card-fields.md](card-fields.md) · 现在的 mod 怎么写：[mod-cs.md](mod-cs.md) · 数据流向：[dataflow.md](dataflow.md)
- 架构与数据流：[architecture.md](architecture.md)
- 拍板记录：[decisions/](decisions/)
- 调研原始材料（不在本仓库）：`D:\files\projects\polaris-research\`

## 1. 模块清单

| 模块 | 文档 | 状态汇总 | 一句话 |
|---|---|---|---|
| `Polaris.Assets` | [CardAsset](src/Polaris.Assets/CardAsset.md) · [CdoEditor](src/Polaris.Assets/CdoEditor.md) · [AssetDiff](src/Polaris.Assets/AssetDiff.md) · [FunctionBytes](src/Polaris.Assets/FunctionBytes.md) · [FunctionWriter](src/Polaris.Assets/FunctionWriter.md) · [AssetIndex](src/Polaris.Assets/AssetIndex.md) · [ClassPatcher](src/Polaris.Assets/ClassPatcher.md) | ✅ 7+5 · 🚧 4+5 | 读卡资产、改 CDO、函数字节校验/替换；bmap 与注入待做 |
| `Polaris.Pak` | [ModPak](src/Polaris.Pak/ModPak.md) · [VENDORED](../../src/Polaris.Pak/VENDORED.md) | ✅ 3 | 继承 solstice-pak，输出 KARDS v3 mod pak |
| `Polaris.TypeSystem` | [TypeMap](src/Polaris.TypeSystem/TypeMap.md) | 🚧 3 | C# 类型 ⇄ FProperty |
| `Polaris.Compiler` | [Translator](src/Polaris.Compiler/Translator.md) · [BytecodeBuilder](src/Polaris.Compiler/BytecodeBuilder.md) · [BytecodeCheck](src/Polaris.Compiler/BytecodeCheck.md) · [AssetImportTable](src/Polaris.Compiler/AssetImportTable.md) · [ModPlanBuilder](src/Polaris.Compiler/ModPlanBuilder.md) · [KismetDecompiler](src/Polaris.Compiler/KismetDecompiler.md) · [KismetText](src/Polaris.Compiler/KismetText.md) · [LocalVariables](src/Polaris.Compiler/LocalVariables.md) · [UbergraphBuilder](src/Polaris.Compiler/UbergraphBuilder.md) | ✅ 2+1+1+4+1+3+1+1 · 🚧 2+3 | C# → 字节码（变量读写/out-ref/常量/局部变量）+ 注解 + 写回 + 反编译/伪代码；ubergraph 待做 |
| `Polaris.Decompile` | [CSharpEmitter](src/Polaris.Decompile/CSharpEmitter.md) | 🚧 3 | 字节码 → 可读 C# |
| `Polaris.SdkGen` | [SdkGenerator](src/Polaris.SdkGen/SdkGenerator.md) · [UsmapReader](src/Polaris.SdkGen/UsmapReader.md) · [JmapReader](src/Polaris.SdkGen/JmapReader.md) · [JmapFileReader](src/Polaris.SdkGen/JmapFileReader.md) · [Json](src/Polaris.SdkGen/Json.md) | ✅ 5 | 纯 jmap（或 usmap+jmap）→ C# SDK |
| `Polaris.SourceGen` | [SdkSourceGenerator](src/Polaris.SourceGen/SdkSourceGenerator.md) | ✅ 1 | 增量源生成器（编译期生成 SDK） |
| `Polaris.Sdk` | [Runtime](src/Polaris.Sdk/Runtime.md) | ✅ 1 | 生成代码引用的运行时声明 |
| `Polaris.Cli` | [Program](src/Polaris.Cli/Program.md) | ✅ 12 · 🚧 1 | `cards/pak/sdk/apply/check/decompile/diff/ubergraph/offsets`；`scan` 待做 |

构建与验证：`.\run.ps1 build`（Solstice.Kismet → Sdk → SdkGen → SourceGen → Assets → Pak → Cli → Tests）、`.\run.ps1 verify`（25 个用例 + 编译闸 + 真实资产 roundtrip）；`check-all` 全量 6492/6492。

## 2. 函数状态看板

| 模块 | 函数 | 状态 | 职责 |
|---|---|---|---|
| Assets | [`Open`](src/Polaris.Assets/CardAsset.md#openuassetpath-string-usmappath-string-workdir-string-cardasset) | ✅ | 打开卡资产（类 + CDO + 原生父类） |
| Assets | [`ResolveMember`](src/Polaris.Assets/CardAsset.md#resolvemembername-string-nativemember) | ✅ | 成员 → 类型与声明类 |
| Assets | [`Snapshot`](src/Polaris.Assets/CardAsset.md#snapshot-ireadonlydictionarystring-string) | ✅ | CDO 属性快照 |
| Assets | [`Save`](src/Polaris.Assets/CardAsset.md#saveoutdir-string-filename-string-savedasset) | ✅ | 写两半到新目录 |
| Assets | [`Set`](src/Polaris.Assets/CdoEditor.md#setcard-cardasset-name-string-value-string-cdopatch) | ✅ | 有则改、无则补 |
| Assets | [`Reset`](src/Polaris.Assets/CdoEditor.md#resetcard-cardasset-name-string-cdopatch) | ✅ | 删差量条目 |
| Assets | [`Apply`](src/Polaris.Assets/CdoEditor.md#applycard-cardasset-patches-ireadonlylistpatchspec-outdir-string-workdir-string-editreport) | ✅ | 改 → 存 → 校验 |
| Assets | [`Verify`](src/Polaris.Assets/CdoEditor.md#verifycard-cardasset-before-ireadonlydictionarystringstring-patches-ireadonlylistpatchspec-saved-savedasset-workdir-string-verifyreport) | ✅ | 重开比对 + 函数逐字节 |
| Assets | [`Compare`](src/Polaris.Assets/AssetDiff.md#comparebefore-byte-after-byte-bytediff) | ✅ | 字节差异摘要 |
| Assets | [`Hashes`](src/Polaris.Assets/FunctionBytes.md#hashesasset-uasset-ireadonlydictionarystring-string) | ✅ | 函数字节 SHA-256 |
| Assets | [`Replace`](src/Polaris.Assets/FunctionWriter.md#replaceasset-uasset-functionname-string-bytecode-ireadonlylistkismetexpression-int) | ✅ | 替换函数字节码 |
| Compiler | [`FindTargets`](src/Polaris.Compiler/ModPlanBuilder.md#findtargetscompilation-compilation-ireadonlylistmodtarget) | ✅ | 注解 → mod 计划 |
| Compiler | [`Decompile`](src/Polaris.Compiler/KismetDecompiler.md#decompileexpressions-ireadonlylistkismetexpression-ireadonlylistknode) | ✅ | 字节码 → 节点树 |
| Compiler | [`Recompile`](src/Polaris.Compiler/KismetDecompiler.md#recompilenodes-ireadonlylistknode-asset-uasset-kismetexpression) | ✅ | 节点树 → 字节码 |
| Compiler | [`Verify`](src/Polaris.Compiler/KismetDecompiler.md#verifyexpressions-ireadonlylistkismetexpression-asset-uasset-roundtripreport) | ✅ | 往返字节一致 |
| Compiler | [`Resolve`](src/Polaris.Compiler/AssetImportTable.md#resolveownerpath-string-functionname-string-fpackageindex) | ✅ | 类/函数 → 导入索引 |
| Compiler | [`FindPatches`](src/Polaris.Compiler/ModPlanBuilder.md#findpatchescompilation-compilation-ireadonlylistmodpatch) | ✅ | 注解 → CDO 计划 |
| Compiler | [`EnsureClass`](src/Polaris.Compiler/AssetImportTable.md#ensureclassownerpath-string-fpackageindex) | ✅ | 补类导入 |
| Compiler | [`EnsurePackage`](src/Polaris.Compiler/AssetImportTable.md#ensurepackagepackagepath-string-fpackageindex) | ✅ | 补包导入 |
| Compiler | [`EnsureFunction`](src/Polaris.Compiler/AssetImportTable.md#ensurefunctionownerpath-string-functionname-string-fpackageindex) | ✅ | 补函数导入 |
| Pak | [`RelativeContentPath`](src/Polaris.Pak/ModPak.md#relativecontentpathfilepath-string-contentroot-string-string) | ✅ | 绝对路径 → pak 内路径 |
| Pak | [`Build`](src/Polaris.Pak/ModPak.md#buildfiles-ienumerablepakfile-pakpath-string-mountpoint-string-builtpak) | ✅ | 打包 + 读回 + 确定性自检 |
| Pak | [`BuildAssetPair`](src/Polaris.Pak/ModPak.md#buildassetpairsourceuassetpath-string-contentroot-string-uasset-byte-uexp-byte-pakpath-string-mountpoint-string-builtpak) | ✅ | 一对 .uasset/.uexp → pak |
| Assets | [`Scan`](src/Polaris.Assets/AssetIndex.md#scanassetpaths-ireadonlyliststring-usmappath-string-assetindexresult) | 🚧 | 资产目录 → bmap |
| Assets | [`ReadUbergraphEntries`](src/Polaris.Assets/AssetIndex.md#readubergraphentriesws-kismetworkspace-classname-string-ireadonlydictionarystringint) | 🚧 | 存根名 → 入口偏移 |
| Assets | [`ReadCdo`](src/Polaris.Assets/AssetIndex.md#readcdoasset-uasset-classexport-classexport-cdosnapshot) | 🚧 | CDO 快照（bmap 用） |
| Assets | [`Dump`](src/Polaris.Assets/AssetIndex.md#dumpbmap-assetindexresult-path-string-void) | 🚧 | 确定性 JSON |
| Assets | [`WriteCdoValue`](src/Polaris.Assets/ClassPatcher.md#writecdovalueasset-uasset-classname-string-member-string-value-propertydata-patchresult) | 🚧 | 未来由 CardAsset 取代 |
| Assets | [`AddFunction`](src/Polaris.Assets/ClassPatcher.md#addfunctionasset-uasset-classname-string-spec-functionspec-fpackageindex) | 🚧 | 既有类新增 UFunction |
| Assets | [`AddMember`](src/Polaris.Assets/ClassPatcher.md#addmemberasset-uasset-classname-string-spec-memberspec-void) | 🚧 | 新增成员 + 默认值 |
| Assets | [`RewriteStubEntry`](src/Polaris.Assets/ClassPatcher.md#rewritestubentryasset-uasset-stubname-string-newoffset-uint-void) | 🚧 | 改存根入口常量 |
| Assets | [`Save`](src/Polaris.Assets/ClassPatcher.md#saveasset-uasset-outdir-string-filename-string-savedasset) | 🚧 | 四闸后写盘（字节码路径） |
| Types | [`ToProperty`](src/Polaris.TypeSystem/TypeMap.md#topropertyasset-uasset-type-typesyntax-model-semanticmodel-fproperty) | 🚧 | C# → FProperty |
| Types | [`ToCSharp`](src/Polaris.TypeSystem/TypeMap.md#tocsharpproperty-fproperty-string) | 🚧 | FProperty → C# |
| Types | [`CreateDefault`](src/Polaris.TypeSystem/TypeMap.md#createdefaultasset-uasset-spec-memberspec-propertydata) | 🚧 | 新成员默认值 |
| Compiler | [`Compile`](src/Polaris.Compiler/Translator.md#compilecompilation-compilation-method-imethodsymbol-asset-uasset-resolver-ifunctionresolver-kismetexpression) | ✅ | 方法体 → 字节码 |
| Compiler | [`ScanLocals`](src/Polaris.Compiler/Translator.md#scanlocalsmethod-imethodsymbol-ireadonlylistlocalvar) | 🚧 | 局部变量 + CallFunc_* |
| Compiler | [`ResolveVariable`](src/Polaris.Compiler/Translator.md#resolvevariablesymbol-isymbol-isout-bool-pointerarg) | 🚧 | 变量令牌 |
| Compiler | [`EmitCall`](src/Polaris.Compiler/Translator.md#emitcallcall-invocationexpressionsyntax-model-semanticmodel-kismetexpression) | ✅ | 静态/实例调用 |
| Compiler | [`Build`](src/Polaris.Compiler/BytecodeBuilder.md#buildasset-uasset-kismetexpression) | ✅ | 标签 → 字节偏移 |
| Compiler | [`Run`](src/Polaris.Compiler/BytecodeCheck.md#runexpressions-ireadonlylistkismetexpression-asset-uasset-checkreport) | ✅ | 序列化回读自校验 |
| Compiler | [`Build`](src/Polaris.Compiler/UbergraphBuilder.md#buildevents-ireadonlylisteventmethod-ctx-blueprintcontext-ubergraphnode) | 🚧 | 事件体 → 规范化段 |
| Compiler | [`RewriteStubEntries`](src/Polaris.Compiler/UbergraphBuilder.md#rewritestubentriesws-kismetworkspace-ubername-string-entries-ireadonlylistuint-stubnames-ireadonlyliststring-rewritereport) | 🚧 | 回填所有存根 |
| Compiler | [`VerifyEntries`](src/Polaris.Compiler/UbergraphBuilder.md#verifyentriesws-kismetworkspace-ubername-string-ireadonlylistentrycheck) | 🚧 | 入口边界对账 |
| Decompile | [`EmitClass`](src/Polaris.Decompile/CSharpEmitter.md#emitclassmodel-blueprintclassmodel-string) | 🚧 | 蓝图类 → C# 基座 |
| Decompile | [`EmitMethod`](src/Polaris.Decompile/CSharpEmitter.md#emitmethodfn-functionmodel-string) | 🚧 | 函数 → C# 方法体 |
| Decompile | [`NameSectionsByEntries`](src/Polaris.Decompile/CSharpEmitter.md#namesectionsbyentriesuber-ubergraphnode-entries-ireadonlydictionarystringint-ireadonlyliststring) | 🚧 | 用入口表命名段 |
| SdkGen | [`Read`](src/Polaris.SdkGen/UsmapReader.md#readdata-byte-usmapfile) | ✅ | usmap 二进制 → schema |
| SdkGen | [`Read`](src/Polaris.SdkGen/JmapReader.md#readjson-string-ireadonlydictionarystring-jmapowner) | ✅ | functions.json → owner/函数 |
| SdkGen | [`ReadFile`](src/Polaris.SdkGen/JmapFileReader.md#readfilepath-string-result) | ✅ | 纯 jmap → usmap 模型 |
| SdkGen | [`Parse`](src/Polaris.SdkGen/Json.md#parsetext-string-jsonvalue) | ✅ | 最小 JSON 解析 |
| SdkGen | [`Emit`](src/Polaris.SdkGen/SdkGenerator.md#emitusmap-usmapfile-owners-ireadonlydictionarystring-jmapowner-options-sdkoptions-sdkresult) | ✅ | 生成 SDK 声明面 |
| SourceGen | [`Initialize`](src/Polaris.SourceGen/SdkSourceGenerator.md#initializecontext-incrementalgeneratorinitializationcontext-void) | ✅ | 增量生成器入口 |
| Sdk | [`Call`](src/Polaris.Sdk/Runtime.md#sdkstubcall-exception) | ✅ | 声明面占位异常 |
| Cli | [`Main`](src/Polaris.Cli/Program.md#mainargs-string-int) | ✅ | 命令行入口 |
| Cli | [`List`](src/Polaris.Cli/Program.md#listopts-options-int) | ✅ | `cards list` |
| Cli | [`Edit`](src/Polaris.Cli/Program.md#editopts-options-int) | ✅ | `cards set/reset` |
| Cli | [`Pak`](src/Polaris.Cli/Program.md#pakopts-options-int) | ✅ | `pak --file ... --pak ...` |
| Cli | [`Sdk`](src/Polaris.Cli/Program.md#sdkopts-options-int) | ✅ | `sdk --usmap --catalog --out` |
| Cli | [`Apply`](src/Polaris.Cli/Program.md#applyopts-options-int) | ✅ | 注解 mod → 写回 → pak |
| Cli | [`ResolveAsset`](src/Polaris.Cli/Program.md#resolveassetcontent-string-asset-string-string) | ✅ | 注解路径 → 磁盘路径 |
'| Cli | [`Check`](src/Polaris.Cli/Program.md#checkopts-options-int) | ✅ | 全函数往返覆盖率 |
'| Cli | [`Decompile`](src/Polaris.Cli/Program.md#decompileopts-options-int) | ✅ | 可读伪代码 |
'| Cli | [`Diff`](src/Polaris.Cli/Program.md#diffopts-options-int) | ✅ | CDO/函数/字节差异 |
'| Cli | [`Ubergraph`](src/Polaris.Cli/Program.md#ubergraphopts-options-int) | ✅ | 入口/段偏移诊断 |
| Cli | [`Offsets`](src/Polaris.Cli/Program.md#offsetsopts-options-int) | ✅ | 语句偏移/跳转落点 |'''
'| Compiler | [`Format`](src/Polaris.Compiler/KismetText.md#formatnodes-ireadonlylistknode-asset-uasset-string) | ✅ | 节点树 → 伪代码 |
| Compiler | [`Ensure`](src/Polaris.Compiler/LocalVariables.md#ensurefunction-functionexport-asset-uasset-name-string-propertytype-string-elementsize-int-fproperty) | ✅ | 补局部变量属性 |
| Cli | [`Diff`](src/Polaris.Cli/Program.md#diffbefore-string-after-string-diffreport) | 🚧 | 变更报告 |

## 3. 未实现清单（按影响面排序）

| # | 项 | 卡在哪 | 对应里程碑 |
|---|---|---|---|
| 1 | 成员/函数/存根注入 | 只有 CDO 改值落地；`ClassPatcher` 全是设计 | M6 / M7 |
| 2 | ubergraph 重排 + 回填 | 现有 `solstice-kismet` 只重烘、不回填 | M4（闸 G2） |
| 3 | C# → KNode 前端 | `Transpiler` 只有设计 | M3（闸 G1） |
| 4 | BP → C# 基座 | 两个上游仓库都没有 emitter | M5（闸 G3） |
| 5 | 类型系统（C# → FProperty） | 读侧类型渲染已有（SdkEmitter.Names）；写侧 `ToProperty/CreateDefault` 仍是设计；CDO 只支持 int/bool/float | M1 / M7 |
| 6 | bmap 扫描器 | `AssetIndex` 只有设计；当前测试直接读资产 | M0 |
| 7 | 蓝图类基座（`<Class>_Base`） | 依赖 bmap；当前 SDK 只有原生类/结构/枚举 | M1 后续 |
| 8 | 新函数运行时可见性 | 完全未验证（`FindFunctionByName` 认不认） | M6（闸 G4） |
| 9 | 新成员布局 | 完全未验证（UStruct 属性链） | M7（闸 G5） |