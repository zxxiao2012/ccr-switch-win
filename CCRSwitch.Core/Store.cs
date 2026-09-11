using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CCRSwitch.Core;

/// <summary>Claude Code 直连供应商（写入 ~/.claude/settings.json 的 env）。</summary>
public sealed class DirectProvider
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public string Model { get; set; } = "";
    /// <summary>额外 env 键值（切换时一并写入；切走时清掉上次写过的额外键）</summary>
    public Dictionary<string, string> ExtraEnv { get; set; } = new();

    public DirectProvider Clone() => new()
    {
        Id = Id,
        Name = Name,
        BaseUrl = BaseUrl,
        AuthToken = AuthToken,
        Model = Model,
        ExtraEnv = new Dictionary<string, string>(ExtraEnv),
    };
}

/// <summary>应用自有状态（store.json）。</summary>
public sealed class StoreModel
{
    public int Version { get; set; } = 1;
    public List<DirectProvider> DirectProviders { get; set; } = new();
    public Guid? CurrentDirectProviderId { get; set; }
    public List<string> AppliedExtraEnvKeys { get; set; } = new();
    public Dictionary<string, string> CcrModelChoice { get; set; } = new();
    /// <summary>ccr 调用命令（Windows 下通常解析为 %APPDATA%\npm\ccr.cmd）</summary>
    public string CcrCommand { get; set; } = "ccr";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static StoreModel Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new StoreModel();
            return JsonSerializer.Deserialize<StoreModel>(File.ReadAllText(path), JsonOpts) ?? new StoreModel();
        }
        catch { return new StoreModel(); }
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, JsonOpts);
        var tmp = Path.Combine(dir, ".tmp-" + Guid.NewGuid().ToString("N")[..8]);
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>各平台路径约定（Windows 用 %USERPROFILE%，其余用 $HOME）。</summary>
public static class Paths
{
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string CcrConfigPath => Path.Combine(Home, ".claude-code-router", "config.json");
    public static string CcrPidPath => Path.Combine(Home, ".claude-code-router", ".claude-code-router.pid");
    public static string CcrLogDir => Path.Combine(Home, ".claude-code-router", "logs");
    public static string ClaudeSettingsPath => Path.Combine(Home, ".claude", "settings.json");
    public static string StorePath => Path.Combine(Home, ".ccr-switch", "store.json");
}
