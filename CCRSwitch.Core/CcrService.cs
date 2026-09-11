using System.Diagnostics;
using System.Text;

namespace CCRSwitch.Core;

public sealed class AppError : Exception
{
    public AppError(string message) : base(message) { }
}

public readonly record struct CcrRunResult(int Code, string Output);

/// <summary>执行 ccr CLI 子命令。Windows 下 npm 全局安装的 ccr 是 ccr.cmd，需经 cmd.exe 调用。</summary>
public static class CcrService
{
    public static CcrRunResult Run(string subcommand, string? ccrCommand = null)
    {
        try
        {
            var psi = BuildStartInfo(subcommand, ccrCommand);
            using var p = Process.Start(psi);
            if (p is null) return new CcrRunResult(127, "Process.Start 返回 null");
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            p.WaitForExit();
            var output = new StringBuilder();
            if (stdout.Result.Length > 0) output.AppendLine(stdout.Result);
            if (stderr.Result.Length > 0) output.AppendLine(stderr.Result);
            return new CcrRunResult(p.ExitCode, output.ToString().Trim());
        }
        catch (Exception e)
        {
            return new CcrRunResult(127, $"启动 ccr 失败：{e.Message}");
        }
    }

    private static ProcessStartInfo BuildStartInfo(string subcommand, string? ccrCommand)
    {
        var command = string.IsNullOrWhiteSpace(ccrCommand) ? "ccr" : ccrCommand.Trim();
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (OperatingSystem.IsWindows())
        {
            var resolved = ResolveWindowsCommand(command);
            psi.FileName = "cmd.exe";
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(resolved);
            psi.ArgumentList.Add(subcommand);
        }
        else
        {
            psi.FileName = "/bin/zsh";
            psi.ArgumentList.Add("-lc");
            psi.ArgumentList.Add($"{command} {subcommand}");
        }

        // GUI 进程 PATH 很短：补 npm / homebrew 目录
        var extra = OperatingSystem.IsWindows()
            ? new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm") }
            : new[] { "/opt/homebrew/bin", "/usr/local/bin" };
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var merged = extra.Concat(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            .Where(s => s.Length > 0)
            .Distinct();
        psi.EnvironmentVariables["PATH"] = string.Join(Path.PathSeparator, merged);
        return psi;
    }

    private static string ResolveWindowsCommand(string command)
    {
        // 显式路径直接用；裸命令优先 %APPDATA%\npm\ccr.cmd，再交给 cmd 的 PATH
        if (command.Contains('\\') || command.Contains('/')) return command;
        var npmDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
        var candidate = Path.Combine(npmDir, command + ".cmd");
        return File.Exists(candidate) ? candidate : command;
    }
}

/// <summary>探测 ccr 服务的 /health（免认证端点）。</summary>
public static class HealthCheck
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    public static async Task<bool> PingAsync(string host, int port)
    {
        try
        {
            using var resp = await Http.GetAsync($"http://{host}:{port}/health");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public static bool Ping(string host, int port) => PingAsync(host, port).GetAwaiter().GetResult();
}

/// <summary>读取 ccr PID 文件并检查进程存活。</summary>
public static class ServicePid
{
    public static int? Current()
    {
        try
        {
            if (!File.Exists(Paths.CcrPidPath)) return null;
            var text = File.ReadAllText(Paths.CcrPidPath).Trim();
            if (!int.TryParse(text, out var pid) || pid <= 0) return null;
            _ = Process.GetProcessById(pid); // 不存在会抛 ArgumentException
            return pid;
        }
        catch { return null; }
    }
}
