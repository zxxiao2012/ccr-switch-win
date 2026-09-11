using CCRSwitch.Core;
using System.Text.Json.Nodes;

// --selftest / --selftest-live：透传给 Core 的真实环境自检
if (args.Contains("--selftest")) { SelfTest.Run(live: false); return 0; }
if (args.Contains("--selftest-live")) { SelfTest.Run(live: true); return 0; }

int passed = 0, failed = 0;
foreach (var (name, test) in Suite.All())
{
    var failures = new List<string>();
    var ctx = new TestCtx(name, failures);
    TestCtx.Current = ctx;
    try { test(); }
    catch (Exception e) { failures.Add($"抛出异常: {e.Message}"); }
    if (failures.Count == 0) { passed++; Console.WriteLine($"✓ {name}"); }
    else
    {
        failed++;
        foreach (var f in failures) Console.WriteLine($"  ✗ [{name}] {f}");
    }
}
Console.WriteLine($"\n结果: {passed} 通过, {failed} 失败");
return failed == 0 ? 0 : 1;

// MARK: - 轻量测试工具

sealed class TestCtx
{
    public static TestCtx Current { get; set; } = new("", new List<string>());
    public TestCtx(string name, List<string> failures) => (Name, Failures) = (name, failures);
    public string Name { get; }
    public List<string> Failures { get; }
}

static class Assert
{
    public static void Equal<T>(T? a, T? b, string msg = "")
    {
        if (!Equals(a, b))
            TestCtx.Current.Failures.Add($"不相等: {a} != {b} {msg}");
    }
    public static void True(bool cond, string msg = "")
    {
        if (!cond) TestCtx.Current.Failures.Add($"期望为真: {msg}");
    }
    public static void Null(object? v, string msg = "")
    {
        if (v is not null) TestCtx.Current.Failures.Add($"期望 null，实际 {v} {msg}");
    }
    public static void Throws(Action body, string msg = "")
    {
        try { body(); TestCtx.Current.Failures.Add($"期望抛出错误但没有: {msg}"); }
        catch { }
    }
}

static class Fixture
{
    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccrswitch-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string CcrConfig(string dir)
    {
        var path = Path.Combine(dir, "config.json");
        File.WriteAllText(path, """
        // ccr 支持带注释的 JSON5
        {
          LOG: false,
          APIKEY: 'secret',
          HOST: "127.0.0.1",
          PORT: 3456,
          transformers: [{ path: "~/plugins/foo.js" }],
          Providers: [
            {
              name: "zhipu",
              api_base_url: "https://open.bigmodel.cn/api/paas/v4/chat/completions",
              api_key: "sk-test",
              models: ["glm-5.3", "glm-5.2", "glm-4.7"],
              transformer: { use: ["deepseek"] }
            }
          ],
          Router: {
            default: "zhipu,glm-5.2",
            think: "zhipu,glm-5.2",
            longContext: "zhipu,glm-5.3",
            background: "zhipu,glm-4.7",
            longContextThreshold: 60000
          }
        }
        """);
        return path;
    }

    public static string ClaudeSettings(string dir)
    {
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, """
        {
          "model": "opus",
          "permissions": { "allow": ["Bash(ls:*)"] },
          "env": {
            "ANTHROPIC_BASE_URL": "https://old.example.com/api/anthropic",
            "ANTHROPIC_AUTH_TOKEN": "old-token",
            "ANTHROPIC_MODEL": "old-model",
            "CLAUDE_CODE_MAX_CONTEXT_TOKENS": "1000000"
          }
        }
        """);
        return path;
    }
}

// MARK: - 测试套件（与 mac 版 Swift 套件逐条对应）

static class Suite
{
    public static (string Name, Action Test)[] All() => new (string, Action)[]
    {
        ("CcrConfig.LoadParsesJson5", LoadParsesJson5),
        ("CcrConfig.SetSlotPreservesUnknownKeysAndThreshold", SetSlotPreservesUnknownKeysAndThreshold),
        ("CcrConfig.UpsertRenameRewiresRouter", UpsertRenameRewiresRouter),
        ("CcrConfig.UpsertDuplicateNameThrows", UpsertDuplicateNameThrows),
        ("CcrConfig.RemoveProviderCleansSlotsAndFallsBackDefault", RemoveProviderCleansSlotsAndFallsBackDefault),
        ("CcrConfig.BackupCreatesAndPrunes", BackupCreatesAndPrunes),
        ("DirectApplier.ApplyReplacesManagedKeysAndPreservesOthers", ApplyReplacesManagedKeysAndPreservesOthers),
        ("DirectApplier.PreviousExtraKeysRemovedOnSwitchAway", PreviousExtraKeysRemovedOnSwitchAway),
        ("DirectApplier.EmptyModelRemovesModelKeys", EmptyModelRemovesModelKeys),
        ("DirectApplier.ReadCurrentRoundTrip", ReadCurrentRoundTrip),
    };

    static void LoadParsesJson5()
    {
        var dir = Fixture.TempDir();
        var c = CcrConfig.Load(Fixture.CcrConfig(dir));
        Assert.Equal(1, c.Providers.Count);
        Assert.Equal("zhipu", c.Providers[0].Name);
        Assert.Equal("glm-5.3,glm-5.2,glm-4.7", string.Join(",", c.Providers[0].Models));
        Assert.True(c.Providers[0].TransformerJson?.Contains("deepseek") == true);
        Assert.Equal("zhipu,glm-5.2", c.Router.GetValueOrDefault(RouterSlot.Default));
        Assert.Equal("127.0.0.1", c.Host);
        Assert.Equal(3456, c.Port);
    }

    static void SetSlotPreservesUnknownKeysAndThreshold()
    {
        var dir = Fixture.TempDir();
        var path = Fixture.CcrConfig(dir);
        var c = CcrConfig.Load(path);
        c.SetSlot(RouterSlot.Default, "zhipu,glm-5.3");
        c.SetSlot(RouterSlot.Think, null);
        c.Write(path);

        var raw = JsonUtil.ReadFile(path)!;
        Assert.Equal(false, JsonUtil.Bool(JsonUtil.At(raw, "LOG")));
        Assert.Equal("secret", JsonUtil.Str(JsonUtil.At(raw, "APIKEY")));
        Assert.True((JsonUtil.At(raw, "transformers") as JsonArray)?.Count > 0);
        Assert.Equal("zhipu,glm-5.3", JsonUtil.Str(JsonUtil.At(JsonUtil.At(raw, "Router"), "default")));
        Assert.Null(JsonUtil.Str(JsonUtil.At(JsonUtil.At(raw, "Router"), "think")));
        Assert.Equal(60000, JsonUtil.Int(JsonUtil.At(JsonUtil.At(raw, "Router"), "longContextThreshold")));
        Assert.Equal("zhipu,glm-4.7", JsonUtil.Str(JsonUtil.At(JsonUtil.At(raw, "Router"), "background")));
    }

    static void UpsertRenameRewiresRouter()
    {
        var dir = Fixture.TempDir();
        var path = Fixture.CcrConfig(dir);
        var c = CcrConfig.Load(path);

        c.Upsert(new CcrProvider("zhipu-official", "https://x", "k",
            new List<string> { "glm-5.3", "glm-5.2" }, null), replacing: "zhipu");
        Assert.Equal("zhipu-official,glm-5.2", c.Router.GetValueOrDefault(RouterSlot.Default));
        Assert.Equal("zhipu-official,glm-5.3", c.Router.GetValueOrDefault(RouterSlot.LongContext));

        c.Upsert(new CcrProvider("deepseek", "https://y", "k2",
            new List<string> { "deepseek-chat", "deepseek-reasoner" }, null));
        Assert.Equal("zhipu-official,deepseek", string.Join(",", c.Providers.Select(p => p.Name)));

        c.Write(path);
        var c2 = CcrConfig.Load(path);
        Assert.Equal(2, c2.Providers.Count);
        Assert.Equal("zhipu-official,glm-4.7", c2.Router.GetValueOrDefault(RouterSlot.Background));
    }

    static void UpsertDuplicateNameThrows()
    {
        var dir = Fixture.TempDir();
        var c = CcrConfig.Load(Fixture.CcrConfig(dir));
        c.Upsert(new CcrProvider("deepseek", "https://y", "k",
            new List<string> { "deepseek-chat" }, null));
        Assert.Throws(() => c.Upsert(new CcrProvider("deepseek", "https://x", "k",
            new List<string> { "m" }, null), replacing: "zhipu"));
    }

    static void RemoveProviderCleansSlotsAndFallsBackDefault()
    {
        var dir = Fixture.TempDir();
        var path = Fixture.CcrConfig(dir);
        var c = CcrConfig.Load(path);
        c.Upsert(new CcrProvider("deepseek", "https://y", "k",
            new List<string> { "deepseek-chat" }, null));
        c.RemoveProvider("zhipu");

        Assert.Null(c.Router.GetValueOrDefault(RouterSlot.Think));
        Assert.Null(c.Router.GetValueOrDefault(RouterSlot.LongContext));
        Assert.Equal("deepseek,deepseek-chat", c.Router.GetValueOrDefault(RouterSlot.Default));
        c.Write(path);
        var c2 = CcrConfig.Load(path);
        Assert.Equal("deepseek", string.Join(",", c2.Providers.Select(p => p.Name)));
    }

    static void BackupCreatesAndPrunes()
    {
        var dir = Fixture.TempDir();
        var path = Fixture.CcrConfig(dir);
        for (int i = 0; i < 12; i++)
            File.WriteAllText(Path.Combine(dir, $"config.json.bak-app-20250101-0000{i:d2}.000"), "old");
        var c = CcrConfig.Load(path);
        c.SetSlot(RouterSlot.Default, "zhipu,glm-5.3");
        c.Write(path);

        var baks = Directory.EnumerateFiles(dir)
            .Select(Path.GetFileName!)
            .Where(n => n.StartsWith(CcrConfig.BackupPrefix, StringComparison.Ordinal))
            .ToList();
        Assert.Equal(10, baks.Count);
        Assert.True(baks.Any(n => !n.StartsWith("config.json.bak-app-2025")), "本次写入的新备份必须保留");
    }

    static void ApplyReplacesManagedKeysAndPreservesOthers()
    {
        var dir = Fixture.TempDir();
        var path = Fixture.ClaudeSettings(dir);
        DirectApplier.Apply(new DirectProvider
        {
            Name = "zhipu",
            BaseUrl = "https://open.bigmodel.cn/api/anthropic",
            AuthToken = "new-token",
            Model = "glm-5.3",
        }, Array.Empty<string>(), path);

        var raw = JsonUtil.ReadFile(path)!;
        Assert.Equal("opus", JsonUtil.Str(JsonUtil.At(raw, "model")));
        Assert.Equal(1, (JsonUtil.At(JsonUtil.At(raw, "permissions"), "allow") as JsonArray)?.Count);
        var env = JsonUtil.At(raw, "env")!;
        Assert.Equal("https://open.bigmodel.cn/api/anthropic", JsonUtil.Str(env["ANTHROPIC_BASE_URL"]));
        Assert.Equal("new-token", JsonUtil.Str(env["ANTHROPIC_AUTH_TOKEN"]));
        Assert.Equal("glm-5.3", JsonUtil.Str(env["ANTHROPIC_MODEL"]));
        Assert.Equal("glm-5.3", JsonUtil.Str(env["ANTHROPIC_DEFAULT_SONNET_MODEL"]));
        Assert.Equal("glm-5.3", JsonUtil.Str(env["ANTHROPIC_DEFAULT_OPUS_MODEL"]));
        Assert.Equal("glm-5.3", JsonUtil.Str(env["ANTHROPIC_DEFAULT_HAIKU_MODEL"]));
        Assert.Equal("1000000", JsonUtil.Str(env["CLAUDE_CODE_MAX_CONTEXT_TOKENS"]));

        var baks = Directory.EnumerateFiles(dir).Select(Path.GetFileName!)
            .Where(n => n.StartsWith(DirectApplier.BackupPrefix, StringComparison.Ordinal)).ToList();
        Assert.Equal(1, baks.Count);
    }

    static void PreviousExtraKeysRemovedOnSwitchAway()
    {
        var dir = Fixture.TempDir();
        var path = Fixture.ClaudeSettings(dir);
        DirectApplier.Apply(new DirectProvider
        {
            Name = "a", BaseUrl = "https://a.example.com", AuthToken = "ta", Model = "m1",
            ExtraEnv = new() { ["X_CUSTOM_FLAG"] = "1", ["ANTHROPIC_CUSTOM"] = "x" },
        }, Array.Empty<string>(), path);
        Assert.Equal("1", JsonUtil.Str(JsonUtil.At(JsonUtil.ReadFile(path)!, "env")!["X_CUSTOM_FLAG"]));

        DirectApplier.Apply(new DirectProvider
        {
            Name = "b", BaseUrl = "https://b.example.com", AuthToken = "tb", Model = "m2",
        }, new[] { "X_CUSTOM_FLAG", "ANTHROPIC_CUSTOM" }, path);

        var env = JsonUtil.At(JsonUtil.ReadFile(path)!, "env")!;
        Assert.Null(env["X_CUSTOM_FLAG"]);
        Assert.Null(env["ANTHROPIC_CUSTOM"]);
        Assert.Equal("https://b.example.com", JsonUtil.Str(env["ANTHROPIC_BASE_URL"]));
        Assert.Equal("1000000", JsonUtil.Str(env["CLAUDE_CODE_MAX_CONTEXT_TOKENS"]));
    }

    static void EmptyModelRemovesModelKeys()
    {
        var dir = Fixture.TempDir();
        var path = Fixture.ClaudeSettings(dir);
        DirectApplier.Apply(new DirectProvider
        {
            Name = "x", BaseUrl = "https://x.example.com", AuthToken = "t",
        }, Array.Empty<string>(), path);
        var env = JsonUtil.At(JsonUtil.ReadFile(path)!, "env")!;
        Assert.Null(env["ANTHROPIC_MODEL"]);
        Assert.Null(env["ANTHROPIC_DEFAULT_SONNET_MODEL"]);
    }

    static void ReadCurrentRoundTrip()
    {
        var dir = Fixture.TempDir();
        var path = Fixture.ClaudeSettings(dir);
        DirectApplier.Apply(new DirectProvider
        {
            Name = "zhipu", BaseUrl = "https://open.bigmodel.cn/api/anthropic",
            AuthToken = "tok", Model = "glm-5.3",
            ExtraEnv = new() { ["CLAUDE_CODE_MAX_CONTEXT_TOKENS"] = "1000000" },
        }, Array.Empty<string>(), path);

        var current = DirectApplier.ReadCurrent(path);
        Assert.Equal("https://open.bigmodel.cn/api/anthropic", current?.BaseUrl);
        Assert.Equal("tok", current?.AuthToken);
        Assert.Equal("glm-5.3", current?.Model);
        Assert.Equal("1000000", current?.ExtraEnv.GetValueOrDefault("CLAUDE_CODE_MAX_CONTEXT_TOKENS"));
    }
}
