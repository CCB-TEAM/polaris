using System.Security.Cryptography;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.Kismet.Bytecode;

namespace Polaris.Assets;

/// <summary>
/// 把每个 UFunction 的字节码重新序列化后取 SHA-256。
/// 用来验证「改 CDO/资产数据」没有动到任何函数的字节码——不依赖任何外部字节码库。
/// </summary>
public static class FunctionBytes
{
    public static IReadOnlyDictionary<string, string> Hashes(UAsset asset)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fn in asset.Exports.OfType<FunctionExport>())
        {
            if (fn.ScriptBytecode is not { Length: > 0 })
            {
                continue;
            }

            using var ms = new MemoryStream();
            using (var writer = new AssetBinaryWriter(ms, asset))
            {
                foreach (var expr in fn.ScriptBytecode)
                {
                    ExpressionSerializer.WriteExpression(expr, writer);
                }
            }

            map[fn.ObjectName.ToString()] = Convert.ToHexString(SHA256.HashData(ms.ToArray()));
        }

        return map;
    }
}
