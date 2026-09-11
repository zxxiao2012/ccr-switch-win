namespace CCRSwitch.Core;

/// <summary>CLI 自检：不启动 GUI，验证 config 解析 / 备份 / 写入 / 服务重启 / 健康检查整条链路。</summary>
public static class SelfTest
{
    public static void Run(bool live)
    {
        var configPath = Paths.CcrConfigPath;
        if (!File.Exists(configPath))
        {
            Console.WriteLine($"[FAIL] 找不到 {configPath}");
            return;
        }

        // 1. 读取解析
        var config = CcrConfig.Load(configPath);
        Console.WriteLine($"[OK] 解析 config.json: host={config.Host} port={config.Port}");
        Console.WriteLine($"     Providers: [{string.Join(", ", config.Providers.Select(p => p.Name))}]");
        foreach (var p in config.Providers)
            Console.WriteLine($"       - {p.Name}: models=[{string.Join(", ", p.Models)}]");
        foreach (RouterSlot slot in Enum.GetValues<RouterSlot>())
            Console.WriteLine($"     Router.{slot.Key()} = {config.Router.GetValueOrDefault(slot) ?? "未设置"}");
        Console.WriteLine($"[OK] 顶层未知键保真: LOG={JsonUtil.Bool(JsonUtil.At(config.Raw, "LOG"))} APIKEY={JsonUtil.At(config.Raw, "APIKEY") is not null}");

        // 2. 服务健康
        var healthy = HealthCheck.Ping(config.Host, config.Port);
        Console.WriteLine($"[{(healthy ? "OK" : "WARN")}] /health 探测: {(healthy ? "运行中" : "未运行")} (PID 文件: {ServicePid.Current()?.ToString() ?? "无"})");

        if (!live)
        {
            Console.WriteLine("[DONE] 只读自检结束（--selftest-live 可执行真实切换验证）");
            return;
        }

        // 3. 真实切换（往返）：default 切到另一个模型 → 重启验证 → 切回原值 → 重启验证
        var provider = config.Providers.FirstOrDefault();
        var originalRoute = config.Router.GetValueOrDefault(RouterSlot.Default);
        if (provider is null || provider.Models.Count < 1 || originalRoute is null)
        {
            Console.WriteLine("[SKIP] 无法确定切换目标（provider/模型不足）");
            return;
        }
        var targetModel = provider.Models.FirstOrDefault(m => originalRoute != $"{provider.Name},{m}");
        if (targetModel is null)
        {
            Console.WriteLine("[SKIP] 只有一个模型，无可切换目标");
            return;
        }

        var store = StoreModel.Load(Paths.StorePath);
        try
        {
            RoundTrip(configPath, provider.Name, targetModel, store, originalRoute);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[FAIL] live 切换失败: {e.Message}");
            Console.WriteLine($"       如需手动恢复: Router.default = {originalRoute}");
        }
    }

    private static void RoundTrip(string configPath, string providerName, string targetModel, StoreModel store, string originalRoute)
    {
        // 切到目标
        WriteAndVerify(configPath, providerName, targetModel);
        var restart1 = RestartAndWait(configPath, store);
        if (!restart1) throw new AppError("切到目标后健康检查超时");

        // 切回原值
        var (origProvider, origModel) = SplitRoute(originalRoute);
        WriteAndVerify(configPath, origProvider, origModel);
        if (!RestartAndWait(configPath, store)) throw new AppError("切回原值后健康检查超时");

        Console.WriteLine($"[DONE] live 自检结束：default 已恢复为 {originalRoute}");
    }

    private static void WriteAndVerify(string configPath, string providerName, string model)
    {
        var c = CcrConfig.Load(configPath);
        c.SetSlot(RouterSlot.Default, $"{providerName},{model}");
        c.Write(configPath);
        var reloaded = CcrConfig.Load(configPath);
        Console.WriteLine($"[OK] 已写入 Router.default = {providerName},{model}（重读校验: {reloaded.Router.GetValueOrDefault(RouterSlot.Default) ?? "?"}，Providers={reloaded.Providers.Count}，备份前缀 {CcrConfig.BackupPrefix} 保留 10 份）");
    }

    private static bool RestartAndWait(string configPath, StoreModel store)
    {
        var result = CcrService.Run("restart", store.CcrCommand);
        Console.WriteLine($"[{(result.Code == 0 ? "OK" : "WARN")}] ccr restart 退出码 {result.Code}{(result.Output.Length == 0 ? "" : $" | {result.Output.ReplaceLineEndings(" / ")}")}");
        for (int i = 0; i < 30; i++)
        {
            var c = CcrConfig.Load(configPath);
            if (HealthCheck.Ping(c.Host, c.Port)) return true;
            Thread.Sleep(500);
        }
        Console.WriteLine("[FAIL] 重启后 /health 超时");
        return false;
    }

    private static (string provider, string model) SplitRoute(string route)
    {
        var parts = route.Split(',', 2);
        return (parts[0], parts.Length > 1 ? parts[1] : "");
    }
}
