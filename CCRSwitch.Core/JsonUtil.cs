using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CCRSwitch.Core;

public static partial class JsonUtil
{
    /// <summary>宽容解析：先标准 JSON，失败则按 JSON5 规则清洗（注释/尾逗号/裸键名/单引号字符串）后重试。</summary>
    public static JsonNode? ParseTolerant(string text)
    {
        try { return JsonNode.Parse(text); }
        catch (JsonException)
        {
            return JsonNode.Parse(Json5Strip(text));
        }
    }

    public static JsonNode? ReadFile(string path) => ParseTolerant(File.ReadAllText(path));

    /// <summary>2 空格缩进序列化并原子写入（temp + 覆盖移动）。注意：JSON5 注释会丢失，与 ccr 自身写回行为一致。</summary>
    public static void WriteFileAtomic(string path, JsonNode node)
    {
        var json = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var dir = Path.GetDirectoryName(path) ?? ".";
        var tmp = Path.Combine(dir, ".tmp-" + Guid.NewGuid().ToString("N")[..8]);
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    public static string Timestamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");

    /// <summary>写前备份：复制为同目录 <paramref name="backupNamePrefix"/> + 时间戳，按文件名倒序只保留 keep 份。</summary>
    public static void Backup(string path, string backupNamePrefix, int keep = 10)
    {
        if (!File.Exists(path)) return;
        var dir = Path.GetDirectoryName(path)!;
        var bak = Path.Combine(dir, backupNamePrefix + Timestamp());
        if (!File.Exists(bak))
        {
            try { File.Copy(path, bak); } catch { /* 备份失败不阻断主流程 */ }
        }
        var names = Directory.EnumerateFiles(dir)
            .Select(Path.GetFileName)
            .Where(n => n is not null && n.StartsWith(backupNamePrefix, StringComparison.Ordinal))
            .Select(n => n!)
            .OrderByDescending(n => n, StringComparer.Ordinal)
            .ToList();
        foreach (var name in names.Skip(keep))
        {
            try { File.Delete(Path.Combine(dir, name)); } catch { }
        }
    }

    /// <summary>安全取对象属性（非对象节点/不存在都返回 null）。</summary>
    public static JsonNode? At(JsonNode? node, string key)
        => node is JsonObject o && o.TryGetPropertyValue(key, out var v) ? v : null;

    public static string? Str(JsonNode? node)
        => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static int? Int(JsonNode? node)
        => node is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;

    public static bool? Bool(JsonNode? node)
        => node is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    // MARK: - JSON5 清洗（尽力而为，仅在标准解析失败后启用）

    [GeneratedRegex(@",(\s*[}\]])")]
    private static partial Regex TrailingCommaRegex();

    public static string Json5Strip(string text)
    {
        var sb = new StringBuilder(text.Length);
        int i = 0, n = text.Length;
        bool inString = false;
        char quote = '"';

        while (i < n)
        {
            char c = text[i];

            if (inString)
            {
                if (c == '\\' && i + 1 < n) { sb.Append(c).Append(text[i + 1]); i += 2; continue; }
                if (c == quote) inString = false;
                sb.Append(c); i++; continue;
            }

            // 行注释
            if (c == '/' && i + 1 < n && text[i + 1] == '/')
            {
                while (i < n && text[i] != '\n') i++;
                continue;
            }
            // 块注释
            if (c == '/' && i + 1 < n && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < n && !(text[i] == '*' && text[i + 1] == '/')) i++;
                i = Math.Min(i + 2, n);
                continue;
            }
            // 双引号字符串
            if (c == '"') { inString = true; quote = '"'; sb.Append(c); i++; continue; }
            // 单引号字符串 → 转成双引号并转义内部 " 与 \
            if (c == '\'')
            {
                sb.Append('"');
                i++;
                while (i < n && text[i] != '\'')
                {
                    if (text[i] == '\\' && i + 1 < n)
                    {
                        char e = text[i + 1];
                        if (e == '\'') sb.Append('\'');        // \' → '（双引号内无需转义）
                        else if (e == '"') sb.Append("\\\"");  // \" 保持转义
                        else { sb.Append('\\').Append(e); }    // 其余转义原样保留
                        i += 2;
                        continue;
                    }
                    if (text[i] == '"') sb.Append("\\\"");
                    else sb.Append(text[i]);
                    i++;
                }
                sb.Append('"');
                i++; // 跳过闭合 '
                continue;
            }
            // 裸键名（{ 或 , 后面的 identifier: ）→ 加引号
            if ((c == '{' || c == ','))
            {
                sb.Append(c); i++;
                int j = i;
                while (j < n && char.IsWhiteSpace(text[j])) j++;
                if (j < n && (char.IsLetter(text[j]) || text[j] == '_' || text[j] == '$'))
                {
                    int k = j;
                    while (k < n && (char.IsLetterOrDigit(text[k]) || text[k] == '_' || text[k] == '$' || text[k] == '-')) k++;
                    int m = k;
                    while (m < n && char.IsWhiteSpace(text[m])) m++;
                    if (m < n && text[m] == ':')
                    {
                        sb.Append('"').Append(text[j..k]).Append('"');
                        i = k;
                        continue;
                    }
                }
                continue;
            }

            sb.Append(c); i++;
        }

        // 尾逗号（此正则仅在字符串外出现概率极低，且该函数本身就是兜底路径）
        return TrailingCommaRegex().Replace(sb.ToString(), "$1");
    }
}
