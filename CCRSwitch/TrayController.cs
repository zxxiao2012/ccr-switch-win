using CCRSwitch.Core;

namespace CCRSwitch;

/// <summary>托盘常驻：左键开面板，右键菜单快速切换。</summary>
internal sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _tray;
    private readonly AppState _state = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 10_000 };
    private PanelForm? _panel;

    public TrayController()
    {
        _tray = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "CCR Switch",
            Visible = true,
        };
        _tray.MouseClick += OnTrayClick;
        _timer.Tick += async (_, _) => { await _state.PollServiceAsync(); RebuildMenu(); };
    }

    public void Run()
    {
        _state.Init();
        RebuildMenu();
        _timer.Start();
        Application.Run();
        _tray.Visible = false;
    }

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) ShowPanel();
    }

    public void ShowPanel()
    {
        _panel ??= new PanelForm(_state);
        _panel.ShowNearCursor();
    }

    private static Icon LoadIcon()
    {
        using var stream = typeof(TrayController).Assembly.GetManifestResourceStream("CCRSwitch.app.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream!);
    }

    /// <summary>根据当前状态重建右键菜单。</summary>
    public void RebuildMenu()
    {
        var menu = new ContextMenuStrip();

        var port = _state.Config?.Port ?? 3456;
        menu.Items.Add(new ToolStripMenuItem(_state.ServiceRunning
            ? $"● ccr 运行中  127.0.0.1:{port}" : "○ ccr 未运行")
        { Enabled = false });

        // 切换 default 子菜单（供应商 → 模型）
        var switchMenu = new ToolStripMenuItem("切换 default");
        if (_state.Config is { } config)
        {
            foreach (var p in config.Providers)
            {
                var providerMenu = new ToolStripMenuItem(p.Name);
                foreach (var m in p.Models)
                {
                    var item = new ToolStripMenuItem(m)
                    {
                        Checked = config.Router.GetValueOrDefault(RouterSlot.Default) == $"{p.Name},{m}",
                    };
                    var provider = p; var model = m;
                    item.Click += async (_, _) => await Run(_state.SwitchDefaultAsync(provider, model, true));
                    providerMenu.DropDownItems.Add(item);
                }
                if (providerMenu.DropDownItems.Count == 0) providerMenu.Enabled = false;
                switchMenu.DropDownItems.Add(providerMenu);
            }
        }
        if (switchMenu.DropDownItems.Count == 0) switchMenu.Enabled = false;
        menu.Items.Add(switchMenu);

        // Claude 直连子菜单
        var directMenu = new ToolStripMenuItem("Claude 直连");
        foreach (var p in _state.Store.DirectProviders)
        {
            var provider = p;
            var item = new ToolStripMenuItem(p.Name)
            {
                Checked = _state.Store.CurrentDirectProviderId == p.Id,
            };
            item.Click += (_, _) =>
            {
                var err = _state.ApplyDirect(provider);
                if (err is not null) MessageBox.Show(err, "CCR Switch", MessageBoxButtons.OK, MessageBoxIcon.Error);
                RebuildMenu();
            };
            directMenu.DropDownItems.Add(item);
        }
        if (directMenu.DropDownItems.Count == 0) directMenu.Enabled = false;
        menu.Items.Add(directMenu);

        menu.Items.Add(new ToolStripSeparator());
        var restartItem = new ToolStripMenuItem("重启服务");
        restartItem.Click += async (_, _) => await Run(_state.RestartServiceAsync());
        var stopItem = new ToolStripMenuItem("停止服务") { Enabled = _state.ServiceRunning };
        stopItem.Click += async (_, _) => await Run(_state.StopServiceAsync());
        var webItem = new ToolStripMenuItem("打开 ccr Web UI");
        webItem.Click += (_, _) => _state.OpenWebUi();
        var panelItem = new ToolStripMenuItem("管理面板…");
        panelItem.Click += (_, _) => ShowPanel();
        menu.Items.AddRange(new ToolStripItem[] { restartItem, stopItem, webItem, panelItem });

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) =>
        {
            _tray.Visible = false;
            Application.Exit();
        });

        var old = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = menu;
        old?.Dispose();
    }

    /// <summary>执行一个动作（写配置/服务控制），完成后刷新菜单；出错弹窗。</summary>
    private async Task Run(Task<string?> op)
    {
        var err = await op;
        await _state.PollServiceAsync();
        RebuildMenu();
        if (err is not null) MessageBox.Show(err, "CCR Switch", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    public void Dispose()
    {
        _timer.Dispose();
        _tray.Dispose();
    }
}
