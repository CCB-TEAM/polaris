# `Polaris.Cli/Program`

**职责**：命令行入口。当前只实现 `cards list` 与 `cards set/reset`（M2）；`scan/sdk/apply/diff` 在后续里程碑。
**对外入口**：进程入口（`polaris <verb> [options]`）。
**内部结构**：`Main` → `Parse` → `List` / `Edit`；所有资产逻辑在 `Polaris.Assets`，CLI 只做编排与打印。
**依赖**：`Polaris.Assets`。
**取舍**：CLI 不实现任何资产逻辑——同一份逻辑被测试复用，避免 cskismet 那种「探针与主路径两套闸」。

### `Main(args: string[]): int`

**状态：✅ 已实现**（`cards list/set/reset` 实测；`scan/sdk/apply/diff` 未实现）

- **签名与契约**：解析 verb 与参数并分发；返回退出码 0=成功、1=失败、2=参数错误。所有异常转成可读诊断。
- **输入输出**：`polaris cards list --asset X --usmap U`；`polaris cards set --asset X --usmap U --set k=v [--set ...] --out D [--work W]`；`polaris cards reset ... --reset k`。`--work` 默认 `<cwd>/.polaris-work`。
- **关键分支（伪代码）**：
  ```
  if 无参数: 打印 usage, 返回 2
  switch args[0]:
    cards: Parse(剩余) → list | set | reset
    _    : 返回 2
  catch VerificationFailedException: 打印 CDO/函数失败项，返回 1
  ```
- **边界与失败**：`--set` 写法缺 `=`、缺 `--asset/--usmap/--out` 都返回 2；`VerificationFailedException` 保留输出文件供诊断，但退出码 1。
- **调用者与被调用者**：`run.ps1` 与用户调用；调用 `CardAsset.Open`、`CdoEditor.Apply`。

### `List(opts: Options): int`

**状态：✅ 已实现**

- **签名与契约**：打印类名、原生父类、CDO 属性表（名字/类型/值/声明类）；只读。
- **输入输出**：`--asset`/`--usmap` 必需；无 `--out`。
- **关键分支（伪代码）**：`card = Open(...); foreach CDO 属性: 打印 + ResolveMember(name).DeclaringClass`。
- **边界与失败**：属性在类链上找不到时打印 `<类链上没有>`，不中断。
- **调用者与被调用者**：`Main` 调用；调用 `CardAsset.Open/ResolveMember`。

### `Edit(opts: Options): int`

**状态：✅ 已实现**（`set`/`reset` 同一入口）

- **签名与契约**：把 `--set/--reset` 转成 `PatchSpec` 列表 → `CdoEditor.Apply` → 打印 patch、字节差异、校验结果；返回 0/1。
- **输入输出**：必须给 `--out`；`--set` 值只支持 int/bool/float（与 CdoEditor 一致）。
- **关键分支（伪代码）**：`if patches.Count == 0: 返回 2` → `report = Apply(...)` → 打印 → `report.Verify.Ok ? 0 : 1`。
- **边界与失败**：写后校验失败时异常在 `Main` 捕获并打印报告；输出目录不覆盖输入（`CardAsset.Save` 拦截）。
- **调用者与被调用者**：`Main` 调用；调用 `CdoEditor.Apply`。

### `Sdk(opts: Options): int`

**状态：✅ 已实现**（真实数据：485 文件 / 4357 类 / 5349 结构 / 1875 枚举 / 12617 方法 / 19275 成员）

- **签名与契约**：读 usmap + functions.json → `SdkEmitter.Emit` → 按 `Polaris.Sdk/<Ns>/<Ns>.<Kind>.g.cs` 写盘；返回 0/1。
- **输入输出**：`--usmap`/`--catalog`/`--out` 必需；`--filter kards,Engine` 只生成指定模块（按短名）。
- **关键分支（伪代码）**：`UsmapReader.Read(bytes)` + `JmapReader.Read(text)` → `Emit(..., new SdkOptions(filter))` → 写文件 → 打印统计。
- **边界与失败**：输出目录自动创建；解析失败抛 `FormatException`（`POLARIS_DEBUG=1` 打印堆栈）。
- **调用者与被调用者**：`Main` 调用；调用 `UsmapReader`、`JmapReader`、`SdkEmitter`。

### `Pak(opts: Options): int`

**状态：✅ 已实现**（`pak --file ... --pak ...` 实测）

- **签名与契约**：把若干绝对路径文件按 `--content-root` 相对化后打包；返回 0/1。
- **输入输出**：至少一个 `--file`；`--pak` 必需；`--mount` 默认 `../../../kards/Content/`。
- **关键分支（伪代码）**：`files.Select(f => (RelativeContentPath(f, root), ReadAllBytes(f)))` → `ModPak.Build` → 打印 mount/条目。
- **边界与失败**：没有 `--file` 返回 2；路径不在 content root 下时 `PakPathException` → 1。
- **调用者与被调用者**：`Main` 调用；调用 `ModPak.Build`。

### `Diff(before: string, after: string): DiffReport`

**状态：🚧 未实现**

- **签名与契约**：两个资产/两份 bmap 的函数级与字段级差异报告；只读。
- **输入输出**：用于回归：同一输入两次生成必须无 diff；`apply` 后只有声明的函数/字段有 diff。
- **关键分支（伪代码）**：`逐函数字节 hash 比较 → 只对变化项做结构 diff`。
- **边界与失败**：hash 相同但结构不同不可能；结构相同但字节不同必须报出来。
- **调用者与被调用者**：未来的 `apply` 回归用；调用 `KismetWorkspace.VerifyAll`。

### `Apply(opts: Options): int`

**状态：✅ 已实现**（`samples/BytecodeMod/Mod.cs` → `card_unit_t_70eot.OnPlayedFromHand`，3 条表达式，pak 7650 字节）

- **签名与契约**：`polaris apply --mod <Mod.cs> --content <ContentRoot> --usmap <usmap> --out <dir> [--sdk <DLL>] [--pak <mod.pak>] [--dry-run]`；同一资产可同时写函数（`[PolarisReplace]`）与 CDO 字段（`[PolarisPatch]`）；多个资产时 `--pak` 视为**目录**，每个资产输出 `<资产名>.pak`；写回前校验：mod 方法参数名必须在目标函数 `CPF_Parm` 里、返回值有无必须与 `CPF_ReturnParm` 一致、`CPF_OutParm` 必须在 mod 方法里声明同名参数；`--dry-run` 只编译+校验不写盘；写回后重开并校验：其余函数哈希不变、被替换函数往返一致、CDO 字段按 patch 生效；成功后输出目录写 `mod.json` 清单（资产/函数/patch/pak 列表）。
- **输入输出**：读取 mod 源码；`[PolarisAsset]` 支持相对路径、`/Game/...`、绝对路径；按资产分组写回，非目标函数必须字节不变；`--pak` 给出则出包。
- **关键分支（伪代码）**：
  ```
  compile(mod, refs=TPA+Sdk+--sdk)
  targets = ModPlanBuilder.FindTargets(compilation)
  foreach asset group:
      card = CardAsset.Open; before = FunctionBytes.Hashes
      foreach target: compile → BytecodeCheck → FunctionWriter.Replace
      save; reopen; 其余函数哈希不变? else 失败
      if --pak: ModPak.Build
  ```
- **边界与失败**：无标注/资产或函数不存在 → 退出 1；不覆盖输入资产；生成 SDK 可传 DLL（`--sdk`）或直接传源码目录（`--sdk-source`，把生成器输出一起编进 mod 编译单元）。
- **调用者与被调用者**：`polaris apply`；调用 `ModPlanBuilder`、`Translator`、`BytecodeCheck`、`FunctionWriter`、`CardAsset`、`ModPak`、`ResolveAsset`。

### `ResolveAsset(content: string, asset: string): string`

**状态：✅ 已实现**

- **签名与契约**：纯函数；`/Game/` 前缀去前缀拼 content；绝对路径原样；相对路径拼 content。
- **输入输出**：不做存在性检查（由 `Apply` 报错）。
- **关键分支（伪代码）**：`startsWith("/Game/") ? content + rest : IsPathRooted ? asset : content + asset`。
- **边界与失败**：路径分隔符统一为平台分隔符。
- **调用者与被调用者**：`Apply` 调用。

### `Check(opts: Options): int`

**状态：✅ 已实现**（`card_event_aans` 2/2；全量 Cards 语料 6492/6492 函数（2039 资产，跳过 14））

- **签名与契约**：`polaris check --asset <uasset>` 或 `--dir <Blueprints 目录> [--limit N]`（递归 `*.uasset`）+ `--usmap <usmap> [--json <file>]`；对每个有字节码的函数跑 `RoundTrip.Verify`；`--json` 落基线（含 ok/total/assets/skipped/failures）；全部一致返回 0，否则 1。
- **输入输出**：只读资产；打印 `roundtrip ok/total（百分比） across N assets，skipped M`，失败时打印前 10 条（函数名 + 差异偏移/token）；没有 CDO 的非卡资产计入 skipped。
- **关键分支（伪代码）**：
  ```
  foreach FunctionExport with ScriptBytecode:
      total++; if RoundTrip.Verify(fn).Ok then ok++ else 记录失败
  print ok/total; return ok == total ? 0 : 1
  ```
- **边界与失败**：资产不存在由 `CardAsset.Open` 抛 `FileNotFoundException`；没有函数时按 100% 处理。
- **调用者与被调用者**：`polaris check`；调用 `CardAsset`、`RoundTrip`。

### `Decompile(opts: Options): int`

**状态：✅ 已实现**（`card_event_aans.OnPlayedFromHand` 输出 `persist[...]` / `ExecuteUbergraph(10)` / `return;`）

- **签名与契约**：`polaris decompile --asset <uasset> | --dir <目录> --usmap <usmap> [--function <name>] [--strict] [--out <file|dir>]`；输出每个函数的可读伪代码并带 `roundtrip=ok/FAIL` 与 `controlflow=decoded/raw` 标记；`--dir` 批量写 `<资产>.txt`；`--strict` 时任一函数往返失败即返回 1；指定函数不存在返回 1。
- **输入输出**：只读资产；函数名解析走 `StackNode`（导出/导入表），变量名取 `FFieldPath`。
- **关键分支（伪代码）**：
  ```
  nodes = KismetDecompiler.Decompile(fn, asset)
  print "// " + fn.Name; print KismetText.Format(nodes, asset)
  ```
- **边界与失败**：跳转/未知指令以注释输出，不还原控制流。
- **调用者与被调用者**：`polaris decompile`；调用 `CardAsset`、`KismetDecompiler`、`KismetText`。

### `Diff(opts: Options): int`

**状态：✅ 已实现**（`card_unit_t_70eot` 验证包 vs 原卡：`kredits 2 -> 0`、`OnPlayedFromHand` 变化、uexp 39 字节变化）

- **签名与契约**：`polaris diff --before <uasset> --after <uasset> --usmap <usmap>`；输出 CDO 字段、函数字节、资产字节差异；总是返回 0（只读诊断）。
- **输入输出**：CDO 用 `Snapshot()` 对比；函数用 `FunctionBytes.Hashes` 对比；字节用 `AssetDiff.Compare` 汇总。
- **关键分支（伪代码）**：
  ```
  before/after = CardAsset.Open
  遍历 CDO 键并集 → +/-/~
  遍历函数哈希并集 → +/-/~
  打印 uasset/uexp 的 ChangedBytes 与长度
  ```
- **边界与失败**：两个资产必须都是卡资产（无 CDO 会抛）；只看内容，不看 pak。
- **调用者与被调用者**：`polaris diff`；调用 `CardAsset`、`FunctionBytes`、`AssetDiff`。

### `Ubergraph(opts: Options): int`

**状态：✅ 已实现**（`card_event_aans`：ubergraph 9 条 / 367 字节，1 个 stub，`entry=10` 不落在指令起点；`BP_CardFunctions`：59 条 / 2035 字节，10 个 stub）

- **签名与契约**：`polaris ubergraph --asset <uasset> --usmap <usmap> [--json <file>]`；打印 ubergraph 规模、`EX_ComputedJump` 操作数类型，以及每个事件存根的入口值与落点；`--json` 输出可归档的诊断基线。
- **输入输出**：只读；每个 stub 输出 `entry`、是否落在解析出的指令起点、最近起点与差值。
- **关键分支（伪代码）**：
  ```
  找 ExecuteUbergraph_*
  序列化 + 顺序回读 → starts
  找 stub 里调用 ubergraph 的 EX_LocalFinalFunction → 取 EX_IntConst 参数
  打印 entry/landed/next/delta
  ```
- **边界与失败**：没有 ubergraph 返回 1；入口表语义仍未解（见 PLAN §9）。
- **调用者与被调用者**：`polaris ubergraph`；调用 `CardAsset`、`ExpressionSerializer`。

### `Offsets(opts: Options): int`

**状态：✅ 已实现**（`card_unit_t_70eot` 原卡 ubergraph：多条 `EX_Jump` 目标 `525/10/202` 均 `landed=False`）

- **签名与契约**：`polaris offsets --asset <uasset> --function <name> --usmap <usmap>`；逐条打印语句偏移、跳转目标、是否落在指令起点。
- **输入输出**：只读；用于定位「跳转目标不落在语句起点」的函数（M4/反编译问题）。
- **关键分支（伪代码）**：序列化 → 顺序回读得 starts → 逐表达式打印 token/offset → Jump/JumpIfNot 打印 `CodeOffset` 与 landed。
- **边界与失败**：函数不存在或无字节码返回 1。
- **调用者与被调用者**：`polaris offsets`；调用 `CardAsset`、`ExpressionSerializer`。
