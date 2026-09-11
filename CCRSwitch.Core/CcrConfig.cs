using System.Text.Json;
using System.Text.Json.Nodes;

namespace CCRSwitch.Core;

public enum RouterSlot { Default, Think, LongContext, Background }

public static class RouterSlotExtensions
{
    public static string Key(this RouterSlot slot) => slot switch
    {
        RouterSlot.Default => "default",
        RouterSlot.Think => "think",
        RouterSlot.LongContext => "longContext",
        RouterSlot.Background => "background",
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    public static string Label(this RouterSlot slot) => slot switch
    {
        RouterSlot.Default => "默认",
        RouterSlot.Think => "思考",
        RouterSlot.LongContext => "长上下文",
        RouterSlot.Background => "后台",
        _ => slot.Key(),
    };
}

public sealed record CcrProvider(
    string Name,
    string ApiBaseUrl,
    string ApiKey,
    List<string> Models,
    string? TransformerJson);

/// <summary>
/// ccr (claude-code-router v1.0.73) 的 config.json 操作层。
/// Router 槽位值为 "provider,model"；顶层/Router 上的其余键（LOG、APIKEY、longContextThreshold、webSearch…）保真保留。
/// </summary>
public sealed class CcrConfig
{
    public const string BackupPrefix = "config.json.bak-app-";

    public JsonNode Raw { get; private set; } = new JsonObject();
    public List<CcrProvider> Providers { get; private set; } = new();
    public Dictionary<RouterSlot, string> Router { get; private set; } = new();
    public string Host { get; private set; } = "127.0.0.1";
    public int Port { get; private set; } = 3456;

    public static CcrConfig Load(string path)
    {
        var config = new CcrConfig
        {
            Raw = JsonUtil.ReadFile(path) ?? new JsonObject(),
        };
        config.Reparse();
        return config;
    }

    // MARK: - 查询

    public string? ProviderNameInSlot(RouterSlot slot)
        => Router.TryGetValue(slot, out var v) ? v.Split(',')[0] : null;

    public string? ModelNameInSlot(RouterSlot slot)
    {
        if (!Router.TryGetValue(slot, out var v)) return null;
        var parts = v.Split(',', 2);
        return parts.Length > 1 ? parts[1] : null;
    }

    // MARK: - 修改

    /// <summary>新增或更新供应商；replacing 指定原名称（重命名场景），会同步改写 Router 引用。</summary>
    public void Upsert(CcrProvider provider, string? replacing = null)
    {
        var original = replacing ?? provider.Name;
        if (Providers.Any(p => p.Name == provider.Name && p.Name != original))
            throw new AppError($"已存在同名供应商：{provider.Name}");

        var attached = JsonUtil.At(Raw, "Providers") is JsonArray existing;
        var arr = (JsonUtil.At(Raw, "Providers") as JsonArray) ?? new JsonArray();
        int idx = -1;
        for (int i = 0; i < arr.Count; i++)
            if (JsonUtil.Str(JsonUtil.At(arr[i], "name")) == original) { idx = i; break; }

        var node = BuildProviderNode(provider, idx >= 0 ? arr[idx] : null);
        if (idx >= 0)
        {
            arr[idx] = node;
            if (original != provider.Name) RewriteRouterReferences(original, provider.Name);
        }
        else
        {
            arr.Add(node);
        }
        if (!attached && Raw is JsonObject root) root["Providers"] = arr;

        ApplyRouterToRaw();
        Reparse();
    }

    /// <summary>删除供应商；引用它的槽位被清空，default 若被清则回退到第一个可用 provider。</summary>
    public void RemoveProvider(string name)
    {
        if (JsonUtil.At(Raw, "Providers") is JsonArray arr)
        {
            for (int i = arr.Count - 1; i >= 0; i--)
                if (JsonUtil.Str(JsonUtil.At(arr[i], "name")) == name) arr.RemoveAt(i);
        }
        Reparse();

        Router = Router
            .Where(kv => kv.Value.Split(',')[0] != name)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        if (!Router.ContainsKey(RouterSlot.Default) && Providers.Count > 0)
        {
            var first = Providers[0];
            if (first.Models.Count > 0) Router[RouterSlot.Default] = $"{first.Name},{first.Models[0]}";
        }
        ApplyRouterToRaw();
        Reparse();
    }

    /// <summary>设置（或传 null 清除）某个槽位的路由。</summary>
    public void SetSlot(RouterSlot slot, string? route)
    {
        if (route is not null) Router[slot] = route;
        else Router.Remove(slot);
        ApplyRouterToRaw();
        Reparse();
    }

    /// <summary>写回磁盘：先备份（保留 10 份）再原子写入。</summary>
    public void Write(string path)
    {
        JsonUtil.Backup(path, BackupPrefix);
        JsonUtil.WriteFileAtomic(path, Raw);
    }

    // MARK: - 内部实现

    private void Reparse()
    {
        Providers.Clear();
        if (JsonUtil.At(Raw, "Providers") is JsonArray arr)
        {
            foreach (var item in arr)
            {
                var name = JsonUtil.Str(JsonUtil.At(item, "name"));
                if (string.IsNullOrEmpty(name)) continue;
                var models = new List<string>();
                if (JsonUtil.At(item, "models") is JsonArray ms)
                    models.AddRange(ms.Where(m => m is not null).Select(m => JsonUtil.Str(m)!).Where(s => s.Length > 0));
                string? transformer = null;
                if (JsonUtil.At(item, "transformer") is JsonObject t)
                    transformer = t.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                Providers.Add(new CcrProvider(
                    name,
                    JsonUtil.Str(JsonUtil.At(item, "api_base_url")) ?? "",
                    JsonUtil.Str(JsonUtil.At(item, "api_key")) ?? "",
                    models,
                    transformer));
            }
        }

        Router.Clear();
        if (JsonUtil.At(Raw, "Router") is JsonObject r)
        {
            foreach (RouterSlot slot in Enum.GetValues<RouterSlot>())
            {
                var v = JsonUtil.Str(JsonUtil.At(r, slot.Key()));
                if (v is not null) Router[slot] = v;
            }
        }

        Host = JsonUtil.Str(JsonUtil.At(Raw, "HOST")) ?? "127.0.0.1";
        Port = JsonUtil.Int(JsonUtil.At(Raw, "PORT")) ?? 3456;
    }

    private void ApplyRouterToRaw()
    {
        if (Raw is not JsonObject root) return;
        var attached = root["Router"] is JsonObject;
        var r = (root["Router"] as JsonObject) ?? new JsonObject();
        foreach (RouterSlot slot in Enum.GetValues<RouterSlot>())
        {
            if (Router.TryGetValue(slot, out var v)) r[slot.Key()] = v;
            else r.Remove(slot.Key());
        }
        if (!attached) root["Router"] = r;
    }

    private void RewriteRouterReferences(string oldName, string newName)
    {
        Router = Router.ToDictionary(kv => kv.Key, kv =>
        {
            var parts = kv.Value.Split(',', 2);
            return parts[0] == oldName
                ? (parts.Length > 1 ? $"{newName},{parts[1]}" : newName)
                : kv.Value;
        });
    }

    /// <summary>构造 provider 节点；与旧对象合并以保留未知键。</summary>
    private static JsonObject BuildProviderNode(CcrProvider p, JsonNode? old)
    {
        var o = new JsonObject();
        if (old is JsonObject oldObj)
        {
            foreach (var kv in oldObj) o[kv.Key] = kv.Value?.DeepClone();
        }
        o["name"] = p.Name;
        o["api_base_url"] = p.ApiBaseUrl;
        o["api_key"] = p.ApiKey;
        var models = new JsonArray();
        foreach (var m in p.Models) models.Add(m);
        o["models"] = models;

        var t = p.TransformerJson?.Trim();
        if (!string.IsNullOrEmpty(t) && JsonUtil.ParseTolerant(t) is JsonObject parsed)
            o["transformer"] = parsed;
        else
            o.Remove("transformer");
        return o;
    }
}
