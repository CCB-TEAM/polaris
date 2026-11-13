# `Polaris.Assets/AssetDiff`

**职责**：两个字节数组的差异摘要，给「改值只准 1 字节」「补条目允许长度变化」这两类断言用。
**对外入口**：`Compare`。
**内部结构**：单趟扫描公共前缀 + 长度差。
**依赖**：无。
**取舍**：不做二进制补丁/回滚——回滚靠「不写盘」和保留输入，而不是反向打补丁。

### `Compare(before: byte[], after: byte[]): ByteDiff`

**状态：✅ 已实现**（被 M2 的两个测试断言使用：改值 1 字节、补条目 +3/+17）

- **签名与契约**：返回 `(BeforeSize, AfterSize, ChangedBytes, FirstOffsets)`；纯函数，无副作用。
- **输入输出**：`ChangedBytes` = 公共前缀里不同位置数 + 长度差的绝对值；`FirstOffsets` 最多 8 个位置；`SizeDelta = AfterSize - BeforeSize`。
- **关键分支（伪代码）**：
  ```
  n = min(len(a), len(b))
  for i in 0..n: if a[i] != b[i]: changed++; 记前 8 个偏移
  changed += abs(len(a) - len(b))
  ```
- **边界与失败**：插入/删除会让插入点之后**全部**计入不同（整体平移），所以 `ChangedBytes` 对变长改动没有「就改了一处」的含义——这正是「改值 vs 补条目」要分两档断言的原因。
- **调用者与被调用者**：`CdoEditor.Apply`、CLI、测试调用；不调用别人。
