// 只是为了触发编译 + 验证生成物可用：引用生成的两个类型。
namespace SdkGenConsumer;

public static class Probe
{
    public static string Describe()
    {
        // BaseCardObject 是 kards usmap 里的结构，SDK 生成时应该出现
        return typeof(global::Polaris.Sdk.Kards.BaseCardObject).FullName ?? "";
    }
}
