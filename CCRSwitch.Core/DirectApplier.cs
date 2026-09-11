using System.Text.Json.Nodes;

namespace CCRSwitch.Core;

/// <summary>把直连供应商写入 ~/.claude/settings.json：只动 env 里我们管理的键，其余内容保真保留。</summary>
public static class DirectApplier
{
    public static readonly string[] ManagedEnvKeys =
    {
        "ANTHROPIC_BASE_URL",
        "ANTHROPIC_AUTH_TOKEN",
        "ANTHROPIC_MODEL",
        "ANTHROPIC_DEFAULT_SONNET_MODEL",
        "ANTHROPIC_DEFAULT_OPUS_MODEL",
        "ANTHROPIC_DEFAULT_HAIKU_MODEL",
    };

    public const string BackupPrefix = "settings.json.bak-ccrswitch-";

    /// <summary>应用直连供应商。previousExtraKeys 是上一次应用写入的额外键，本次没再提供的会被移除。</summary>
    public static void Apply(DirectProvider provider, IReadOnlyList<string> previousExtraKeys, string settingsPath)
    {
        if (File.Exists(settingsPath)) JsonUtil.Backup(settingsPath, BackupPrefix);

        var root = File.Exists(settingsPath)
            ? JsonUtil.ReadFile(settingsPath)
            : new JsonObject();
        if (root is not JsonObject obj)
            throw new AppError("settings.json 顶层不是 JSON 对象，无法写入");

        var hadEnv = obj["env"] is JsonObject;
        var env = (obj["env"] as JsonObject) ?? new JsonObject();

        var values = new Dictionary<string, string>();
        if (provider.BaseUrl.Length > 0) values["ANTHROPIC_BASE_URL"] = provider.BaseUrl;
        if (provider.AuthToken.Length > 0) values["ANTHROPIC_AUTH_TOKEN"] = provider.AuthToken;
        if (provider.Model.Length > 0)
        {
            values["ANTHROPIC_MODEL"] = provider.Model;
            values["ANTHROPIC_DEFAULT_SONNET_MODEL"] = provider.Model;
            values["ANTHROPIC_DEFAULT_OPUS_MODEL"] = provider.Model;
            values["ANTHROPIC_DEFAULT_HAIKU_MODEL"] = provider.Model;
        }
        foreach (var kv in provider.ExtraEnv)
            if (kv.Key.Length > 0) values[kv.Key] = kv.Value;

        var targetKeys = new HashSet<string>(ManagedEnvKeys);
        targetKeys.UnionWith(provider.ExtraEnv.Keys);
        targetKeys.UnionWith(previousExtraKeys);
        foreach (var key in targetKeys)
        {
            if (values.TryGetValue(key, out var v)) env[key] = v;
            else env.Remove(key);
        }

        if (!hadEnv) obj["env"] = env;
        JsonUtil.WriteFileAtomic(settingsPath, root);
    }

    /// <summary>从活的 settings.json 读出当前直连配置（首次导入 / 编辑回填用）。非 managed 的 env 键收进 ExtraEnv。</summary>
    public static DirectProvider? ReadCurrent(string settingsPath)
    {
        var root = File.Exists(settingsPath) ? JsonUtil.ReadFile(settingsPath) : null;
        if (root is not JsonObject obj) return null;
        if (JsonUtil.At(root, "env") is not JsonObject env) return null;
        var baseUrl = JsonUtil.Str(env["ANTHROPIC_BASE_URL"]);
        if (string.IsNullOrEmpty(baseUrl)) return null;

        var extra = new Dictionary<string, string>();
        foreach (var kv in env)
        {
            if (ManagedEnvKeys.Contains(kv.Key)) continue;
            var s = JsonUtil.Str(kv.Value);
            if (s is not null) extra[kv.Key] = s;
        }
        var model = JsonUtil.Str(env["ANTHROPIC_MODEL"])
                    ?? JsonUtil.Str(env["ANTHROPIC_DEFAULT_SONNET_MODEL"])
                    ?? "";
        return new DirectProvider
        {
            Name = "",
            BaseUrl = baseUrl,
            AuthToken = JsonUtil.Str(env["ANTHROPIC_AUTH_TOKEN"]) ?? "",
            Model = model,
            ExtraEnv = extra,
        };
    }
}
