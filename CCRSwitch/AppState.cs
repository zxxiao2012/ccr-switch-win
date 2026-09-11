using System.Diagnostics;
using CCRSwitch.Core;

namespace CCRSwitch;

/// <summary>UI 状态机：CCR 配置、直连注册表、服务状态、切换动作（对应 mac 版 AppModel）。</summary>
internal sealed class AppState
{
    public CcrConfig? Config;
    public string? ConfigError;
    public StoreModel Store = new();
    public bool ServiceRunning;
    public int? CcrPid;

    public void Init()
    {
        Store = StoreModel.Load(Paths.StorePath);
        if (Store.DirectProviders.Count == 0) ImportCurrentDirect();
        if (OperatingSystem.IsWindows()) Store.CcrCommand = ResolveCcrCommand(Store.CcrCommand);
        Store.Save(Paths.StorePath);
        ReloadConfig();
        _ = PollServiceAsync();
    }

    public void ReloadConfig()
    {
        try { Config = CcrConfig.Load(Paths.CcrConfigPath); ConfigError = null; }
        catch (Exception e) { Config = null; ConfigError = $"读取 config.json 失败：{e.Message}"; }
    }

    public async Task PollServiceAsync()
    {
        var host = Config?.Host ?? "127.0.0.1";
        var port = Config?.Port ?? 3456;
        ServiceRunning = await HealthCheck.PingAsync(host, port);
        CcrPid = ServiceRunning ? ServicePid.Current() : null;
    }

    private void ImportCurrentDirect()
    {
        var imported = DirectApplier.ReadCurrent(Paths.ClaudeSettingsPath);
        if (imported is null) return;
        imported.Name = $"当前直连 ({HostOf(imported.BaseUrl)})";
        Store.DirectProviders = new List<DirectProvider> { imported };
        Store.CurrentDirectProviderId = imported.Id;
        Store.AppliedExtraEnvKeys = imported.ExtraEnv.Keys.ToList();
    }

    private static string HostOf(string url)
    {
        try { return new Uri(url).Host; }
        catch { return url; }
    }

    /// <summary>把裸 ccr 命令解析成 %APPDATA%\npm\ccr.cmd 存入 store，GUI 调用更稳。</summary>
    private static string ResolveCcrCommand(string current)
    {
        if (current.Contains('\\') || current.Contains('/')) return current;
        var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "ccr.cmd");
        return File.Exists(npm) ? npm : current;
    }

    // MARK: - CCR 切换

    /// <summary>切 default 槽位；allSlots 时其余槽位一并指向该 provider（已指向的保留原模型）。</summary>
    public async Task<string?> SwitchDefaultAsync(CcrProvider provider, string model, bool allSlots)
    {
        if (model.Length == 0) return "该供应商没有可用模型";
        try
        {
            var c = CcrConfig.Load(Paths.CcrConfigPath);
            c.SetSlot(RouterSlot.Default, $"{provider.Name},{model}");
            if (allSlots)
            {
                foreach (var slot in new[] { RouterSlot.Think, RouterSlot.LongContext, RouterSlot.Background })
                    c.SetSlot(slot, $"{provider.Name},{PickModel(provider, slot, model)}");
            }
            c.Write(Paths.CcrConfigPath);
            Store.CcrModelChoice[provider.Name] = model;
            Store.Save(Paths.StorePath);
            ReloadConfig();
            await RestartAfterChangeAsync();
            return null;
        }
        catch (Exception e) { return $"切换失败：{e.Message}"; }
    }

    private string PickModel(CcrProvider provider, RouterSlot slot, string fallback)
    {
        if (Config is { } c && c.ProviderNameInSlot(slot) == provider.Name
            && c.ModelNameInSlot(slot) is { } m && provider.Models.Contains(m))
            return m;
        if (slot == RouterSlot.Background)
        {
            foreach (var kw in new[] { "turbo", "flash", "mini", "haiku" })
            {
                var hit = provider.Models.FirstOrDefault(x => x.ToLowerInvariant().Contains(kw));
                if (hit is not null) return hit;
            }
        }
        return fallback;
    }

    public async Task<string?> SetSlotAsync(RouterSlot slot, CcrProvider provider, string model)
    {
        try
        {
            var c = CcrConfig.Load(Paths.CcrConfigPath);
            c.SetSlot(slot, $"{provider.Name},{model}");
            c.Write(Paths.CcrConfigPath);
            ReloadConfig();
            await RestartAfterChangeAsync();
            return null;
        }
        catch (Exception e) { return $"更新槽位失败：{e.Message}"; }
    }

    public async Task<string?> SaveCcrProviderAsync(CcrProvider provider, string? originalName)
    {
        try
        {
            var c = CcrConfig.Load(Paths.CcrConfigPath);
            c.Upsert(provider, originalName);
            c.Write(Paths.CcrConfigPath);
            if (originalName is { } old && old != provider.Name)
            {
                if (Store.CcrModelChoice.TryGetValue(old, out var choice))
                {
                    Store.CcrModelChoice[provider.Name] = choice;
                    Store.CcrModelChoice.Remove(old);
                }
            }
            Store.Save(Paths.StorePath);
            ReloadConfig();
            await RestartAfterChangeAsync();
            return null;
        }
        catch (Exception e) { return $"保存失败：{e.Message}"; }
    }

    public async Task<string?> DeleteCcrProviderAsync(string name)
    {
        try
        {
            var c = CcrConfig.Load(Paths.CcrConfigPath);
            c.RemoveProvider(name);
            c.Write(Paths.CcrConfigPath);
            Store.CcrModelChoice.Remove(name);
            Store.Save(Paths.StorePath);
            ReloadConfig();
            await RestartAfterChangeAsync();
            return null;
        }
        catch (Exception e) { return $"删除失败：{e.Message}"; }
    }

    // MARK: - 服务控制

    public async Task<string?> RestartServiceAsync()
    {
        var result = await Task.Run(() => CcrService.Run("restart", Store.CcrCommand));
        var ok = await WaitForHealthAsync(15);
        ServiceRunning = ok;
        CcrPid = ServicePid.Current();
        return ok ? null : $"ccr 重启后健康检查失败\n{result.Output}";
    }

    public async Task<string?> StopServiceAsync()
    {
        var result = await Task.Run(() => CcrService.Run("stop", Store.CcrCommand));
        await Task.Delay(800);
        await PollServiceAsync();
        return ServiceRunning ? $"停止后服务仍在响应\n{result.Output}" : null;
    }

    /// <summary>配置变更后：若服务在跑则重启使其生效（ccr README：改配置必须重启）。</summary>
    private async Task RestartAfterChangeAsync()
    {
        if (!ServiceRunning && ServicePid.Current() is null) return;
        await Task.Run(() => CcrService.Run("restart", Store.CcrCommand));
        ServiceRunning = await WaitForHealthAsync(15);
        CcrPid = ServicePid.Current();
    }

    private async Task<bool> WaitForHealthAsync(int timeoutSeconds)
    {
        var host = Config?.Host ?? "127.0.0.1";
        var port = Config?.Port ?? 3456;
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await HealthCheck.PingAsync(host, port)) return true;
            await Task.Delay(400);
        }
        return false;
    }

    // MARK: - 直连

    public string? ApplyDirect(DirectProvider provider)
    {
        try
        {
            DirectApplier.Apply(provider, Store.AppliedExtraEnvKeys, Paths.ClaudeSettingsPath);
            Store.CurrentDirectProviderId = provider.Id;
            Store.AppliedExtraEnvKeys = provider.ExtraEnv.Keys.ToList();
            Store.Save(Paths.StorePath);
            return null;
        }
        catch (Exception e) { return $"写入 settings.json 失败：{e.Message}"; }
    }

    /// <summary>编辑当前生效的 provider 时，从活 settings.json 回填（dual-way sync）。</summary>
    public DirectProvider BackfilledDirect(DirectProvider p)
    {
        if (p.Id != Store.CurrentDirectProviderId) return p;
        var live = DirectApplier.ReadCurrent(Paths.ClaudeSettingsPath);
        if (live is null) return p;
        p.BaseUrl = live.BaseUrl;
        p.AuthToken = live.AuthToken;
        p.Model = live.Model;
        p.ExtraEnv = live.ExtraEnv;
        return p;
    }

    public string? SaveDirectProvider(DirectProvider provider)
    {
        if (provider.Name.Trim().Length == 0) return "名称不能为空";
        provider.Name = provider.Name.Trim();
        var existing = Store.DirectProviders.FindIndex(x => x.Id == provider.Id);
        if (existing >= 0) Store.DirectProviders[existing] = provider;
        else Store.DirectProviders.Add(provider);
        Store.Save(Paths.StorePath);
        return null;
    }

    public void DeleteDirectProvider(Guid id)
    {
        Store.DirectProviders.RemoveAll(x => x.Id == id);
        if (Store.CurrentDirectProviderId == id) Store.CurrentDirectProviderId = null;
        Store.Save(Paths.StorePath);
    }

    public void DuplicateDirectProvider(DirectProvider p)
    {
        var copy = p.Clone();
        copy.Id = Guid.NewGuid();
        copy.Name = p.Name + " 副本";
        Store.DirectProviders.Add(copy);
        Store.Save(Paths.StorePath);
    }

    // MARK: - 系统交互

    public void OpenWebUi()
    {
        var host = Config?.Host ?? "127.0.0.1";
        var port = Config?.Port ?? 3456;
        OpenUrl($"http://{host}:{port}/ui/");
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch { }
    }

    public static void RevealFile(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            else
                Process.Start("open", $"-R \"{path}\"");
        }
        catch { }
    }
}
