using CCRSwitch.Core;

namespace CCRSwitch;

/// <summary>左键弹出面板：服务状态条 + [CCR / Claude 直连] 两个页签（对应 mac 版 Popover）。</summary>
internal sealed class PanelForm : Form
{
    private readonly AppState _state;

    private readonly Label _statusLabel = new();
    private readonly Button _btnRestart = new() { Text = "重启", AutoSize = true };
    private readonly Button _btnStop = new() { Text = "停止", AutoSize = true };
    private readonly Button _btnWeb = new() { Text = "Web UI", AutoSize = true };

    private readonly ListView _ccrList = new()
    {
        View = View.Details,
        FullRowSelect = true,
        HideSelection = true,
        MultiSelect = false,
    };
    private readonly CheckBox _applyAll = new() { Text = "切换时应用到全部槽位", Checked = true, AutoSize = true };
    private readonly ComboBox[] _slotCombos = new ComboBox[4];
    private readonly Label _ccrHint = new();

    private readonly ListView _directList = new()
    {
        View = View.Details,
        FullRowSelect = true,
        HideSelection = true,
        MultiSelect = false,
    };
    private readonly Label _directHint = new();

    private bool _updating;


    public PanelForm(AppState state)
    {
        _state = state;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(460, 640);
        Font = new Font("Segoe UI", 9F);
        Deactivate += (_, _) => Hide();

        BuildUi();
    }

    private void BuildUi()
    {
        // 标题栏
        var header = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = SystemColors.ControlDark };
        var title = new Label
        {
            Text = "  ⚡ CCR Switch",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var close = new Button
        {
            Text = "✕",
            Dock = DockStyle.Right,
            Width = 32,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0),
        };
        close.Click += (_, _) => Hide();
        header.Controls.Add(title);
        header.Controls.Add(close);

        // 服务状态条
        var status = new Panel { Dock = DockStyle.Top, Height = 36 };
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.Padding = new Padding(8, 0, 0, 0);
        _btnRestart.Dock = DockStyle.Right;
        _btnStop.Dock = DockStyle.Right;
        _btnWeb.Dock = DockStyle.Right;
        _btnRestart.Click += async (_, _) => await Busy(_state.RestartServiceAsync());
        _btnStop.Click += async (_, _) => await Busy(_state.StopServiceAsync());
        _btnWeb.Click += (_, _) => _state.OpenWebUi();
        status.Controls.Add(_statusLabel);
        status.Controls.Add(_btnWeb);
        status.Controls.Add(_btnStop);
        status.Controls.Add(_btnRestart);

        // 页签
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildCcrPage());
        tabs.TabPages.Add(BuildDirectPage());

        // 底栏
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 36 };
        var btnOpenCcr = new Button { Text = "打开 config.json", AutoSize = true, Dock = DockStyle.Left };
        var btnOpenSettings = new Button { Text = "打开 settings.json", AutoSize = true, Dock = DockStyle.Left };
        var btnQuit = new Button { Text = "退出", AutoSize = true, Dock = DockStyle.Right };
        btnOpenCcr.Click += (_, _) => AppState.RevealFile(Paths.CcrConfigPath);
        btnOpenSettings.Click += (_, _) => AppState.RevealFile(Paths.ClaudeSettingsPath);
        btnQuit.Click += (_, _) => Application.Exit();
        footer.Controls.Add(btnOpenCcr);
        footer.Controls.Add(btnOpenSettings);
        footer.Controls.Add(btnQuit);

        // Dock 顺序：Fill 最先加入
        Controls.Add(tabs);
        Controls.Add(footer);
        Controls.Add(status);
        Controls.Add(header);
    }

    private TabPage BuildCcrPage()
    {
        var page = new TabPage("CCR") { Padding = new Padding(8) };

        // 底部：Router 槽位
        var slots = new GroupBox { Dock = DockStyle.Bottom, Height = 168, Text = "Router 槽位" };
        var slotsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10, 4, 10, 4) };
        slotsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        slotsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var slotList = new[] { RouterSlot.Default, RouterSlot.Think, RouterSlot.LongContext, RouterSlot.Background };
        for (int i = 0; i < slotList.Length; i++)
        {
            var slot = slotList[i];
            slotsLayout.Controls.Add(new Label
            {
                Text = slot.Label(),
                Anchor = AnchorStyles.Left,
                AutoSize = true,
            }, 0, i);
            var combo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Consolas", 9F),
                Tag = slot,
            };
            combo.SelectedIndexChanged += OnSlotChanged;
            _slotCombos[i] = combo;
            slotsLayout.Controls.Add(combo, 1, i);
        }
        var slotFooter = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 30, WrapContents = false };
        slotFooter.Controls.Add(_applyAll);
        slots.Controls.Add(slotsLayout);
        slots.Controls.Add(slotFooter);

        // 中部：供应商列表操作条
        var actions = new Panel { Dock = DockStyle.Bottom, Height = 36 };
        var btnAdd = new Button { Text = "添加供应商", AutoSize = true, Dock = DockStyle.Left };
        var btnEdit = new Button { Text = "编辑", AutoSize = true, Dock = DockStyle.Left };
        var btnDelete = new Button { Text = "删除", AutoSize = true, Dock = DockStyle.Left };
        btnAdd.Click += (_, _) => EditProvider(null);
        btnEdit.Click += (_, _) => EditSelectedProvider();
        btnDelete.Click += async (_, _) => await DeleteSelectedProvider();
        actions.Controls.Add(btnAdd);
        actions.Controls.Add(btnEdit);
        actions.Controls.Add(btnDelete);

        _ccrList.Dock = DockStyle.Fill;
        _ccrList.Columns.Add("当前", 42);
        _ccrList.Columns.Add("名称", 110);
        _ccrList.Columns.Add("default 模型", 120);
        _ccrList.Columns.Add("Base URL", 170);
        _ccrList.DoubleClick += async (_, _) => await SwitchSelectedProvider();

        _ccrHint.Text = "双击行切换 default · 右键更多操作 · 改动自动备份并重启 ccr";
        _ccrHint.Dock = DockStyle.Top;
        _ccrHint.Height = 22;
        _ccrHint.ForeColor = SystemColors.GrayText;

        page.Controls.Add(_ccrList);
        page.Controls.Add(actions);
        page.Controls.Add(slots);
        page.Controls.Add(_ccrHint);
        return page;
    }

    private TabPage BuildDirectPage()
    {
        var page = new TabPage("Claude 直连") { Padding = new Padding(8) };

        var actions = new Panel { Dock = DockStyle.Bottom, Height = 36 };
        var btnAdd = new Button { Text = "添加供应商", AutoSize = true, Dock = DockStyle.Left };
        var btnEdit = new Button { Text = "编辑", AutoSize = true, Dock = DockStyle.Left };
        var btnDuplicate = new Button { Text = "复制", AutoSize = true, Dock = DockStyle.Left };
        var btnDelete = new Button { Text = "删除", AutoSize = true, Dock = DockStyle.Left };
        btnAdd.Click += (_, _) => EditDirect(new DirectProvider { BaseUrl = "https://" });
        btnEdit.Click += (_, _) => EditSelectedDirect();
        btnDuplicate.Click += (_, _) =>
        {
            if (SelectedDirect() is { } p) { _state.DuplicateDirectProvider(p); RefreshAll(); }
        };
        btnDelete.Click += (_, _) =>
        {
            if (SelectedDirect() is { } p)
            {
                _state.DeleteDirectProvider(p.Id);
                RefreshAll();
            }
        };
        actions.Controls.Add(btnAdd);
        actions.Controls.Add(btnEdit);
        actions.Controls.Add(btnDuplicate);
        actions.Controls.Add(btnDelete);

        _directList.Dock = DockStyle.Fill;
        _directList.Columns.Add("当前", 46);
        _directList.Columns.Add("名称", 130);
        _directList.Columns.Add("Base URL", 180);
        _directList.Columns.Add("模型", 110);
        _directList.DoubleClick += (_, _) =>
        {
            if (SelectedDirect() is { } p)
            {
                var err = _state.ApplyDirect(p);
                if (err is not null) MessageBox.Show(err, "CCR Switch", MessageBoxButtons.OK, MessageBoxIcon.Error);
                RefreshAll();
            }
        };

        _directHint.Text = "双击行应用 · 写入 ~/.claude/settings.json 的 env · Claude Code 热切换，新会话生效";
        _directHint.Dock = DockStyle.Top;
        _directHint.Height = 22;
        _directHint.ForeColor = SystemColors.GrayText;

        page.Controls.Add(_directList);
        page.Controls.Add(actions);
        page.Controls.Add(_directHint);
        return page;
    }

    // MARK: - 显示与刷新

    public void ShowNearCursor()
    {
        _state.ReloadConfig();
        _ = _state.PollServiceAsync();
        RefreshAll();

        var area = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
        var x = Cursor.Position.X - Width / 2;
        var y = Cursor.Position.Y - Height - 12;
        if (x < area.Left) x = area.Left;
        if (x + Width > area.Right) x = area.Right - Width;
        if (y < area.Top) y = Cursor.Position.Y + 12;
        Location = new Point(x, y);

        Show();
        Activate();
    }

    public void RefreshAll()
    {
        _updating = true;
        try
        {
            RefreshStatus();
            RefreshCcr();
            RefreshDirect();
        }
        finally { _updating = false; }
    }

    private void RefreshStatus()
    {
        if (_state.ConfigError is { } err)
        {
            _statusLabel.Text = "  " + err;
            return;
        }
        var host = _state.Config?.Host ?? "127.0.0.1";
        var port = _state.Config?.Port ?? 3456;
        var pid = _state.CcrPid is { } p ? $"PID {p} · " : "";
        _statusLabel.Text = _state.ServiceRunning
            ? $"  ● ccr 运行中 · {pid}{host}:{port}"
            : $"  ○ ccr 未运行 · {host}:{port}";
        _btnStop.Enabled = _state.ServiceRunning;
    }

    private void RefreshCcr()
    {
        _ccrList.BeginUpdate();
        _ccrList.Items.Clear();
        if (_state.Config is { } config)
        {
            var defaultProvider = config.ProviderNameInSlot(RouterSlot.Default);
            var defaultModel = config.ModelNameInSlot(RouterSlot.Default);
            foreach (var p in config.Providers)
            {
                var isDefault = p.Name == defaultProvider;
                var item = new ListViewItem(isDefault ? "◉" : "○") { Tag = p.Name };
                item.SubItems.Add(p.Name);
                item.SubItems.Add(isDefault ? defaultModel ?? "" : "");
                item.SubItems.Add(HostOf(p.ApiBaseUrl));
                _ccrList.Items.Add(item);
            }
        }
        _ccrList.EndUpdate();

        foreach (var combo in _slotCombos)
        {
            if (combo.Tag is not RouterSlot slot) continue;
            combo.Items.Clear();
            if (_state.Config is { } cfg)
            {
                foreach (var p in cfg.Providers)
                    foreach (var m in p.Models)
                        combo.Items.Add($"{p.Name},{m}");
                var current = cfg.Router.GetValueOrDefault(slot);
                if (current is not null) combo.SelectedItem = current;
            }
        }
    }

    private void RefreshDirect()
    {
        _directList.BeginUpdate();
        _directList.Items.Clear();
        foreach (var p in _state.Store.DirectProviders)
        {
            var isCurrent = _state.Store.CurrentDirectProviderId == p.Id;
            var item = new ListViewItem(isCurrent ? "◉" : "○") { Tag = p.Id };
            item.SubItems.Add(p.Name + (isCurrent ? "（当前）" : ""));
            item.SubItems.Add(HostOf(p.BaseUrl));
            item.SubItems.Add(p.Model.Length == 0 ? "默认模型" : p.Model);
            _directList.Items.Add(item);
        }
        _directList.EndUpdate();
    }

    private static string HostOf(string url)
    {
        try { return new Uri(url).Host; }
        catch { return url; }
    }

    // MARK: - 交互

    private CcrProvider? SelectedProvider()
    {
        if (_ccrList.SelectedItems.Count == 0) return null;
        var name = _ccrList.SelectedItems[0].Tag as string;
        return _state.Config?.Providers.FirstOrDefault(p => p.Name == name);
    }

    private DirectProvider? SelectedDirect()
    {
        if (_directList.SelectedItems.Count == 0) return null;
        var id = _directList.SelectedItems[0].Tag as Guid?;
        return _state.Store.DirectProviders.FirstOrDefault(p => p.Id == id);
    }

    private async Task SwitchSelectedProvider()
    {
        if (SelectedProvider() is not { } p) return;
        var model = _state.Store.CcrModelChoice.GetValueOrDefault(p.Name);
        if (!p.Models.Contains(model)) model = p.Models.FirstOrDefault() ?? "";
        var err = await Busy(_state.SwitchDefaultAsync(p, model, _applyAll.Checked));
        if (err is not null) MessageBox.Show(err, "CCR Switch", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private async void OnSlotChanged(object? sender, EventArgs e)
    {
        if (_updating) return;
        if (sender is not ComboBox combo || combo.Tag is not RouterSlot slot) return;
        if (combo.SelectedItem is not string route) return;
        var parts = route.Split(',', 2);
        var provider = _state.Config?.Providers.FirstOrDefault(p => p.Name == parts[0]);
        if (provider is null || parts.Length < 2) return;
        var err = await Busy(_state.SetSlotAsync(slot, provider, parts[1]));
        if (err is not null) MessageBox.Show(err, "CCR Switch", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void EditProvider(CcrProvider? existing)
    {
        string? original = existing?.Name;
        bool NameExists(string name) =>
            _state.Config?.Providers.Any(p => p.Name == name && p.Name != original) == true;

        using var editor = new CcrProviderEditorForm(existing, NameExists);
        if (editor.ShowDialog(this) == DialogResult.OK && editor.Result is { } provider)
        {
            _ = Busy(_state.SaveCcrProviderAsync(provider, original)); // Busy 内部处理错误与刷新
        }
    }

    private void EditSelectedProvider()
    {
        if (SelectedProvider() is { } p) EditProvider(p);
    }

    private async Task DeleteSelectedProvider()
    {
        if (SelectedProvider() is not { } p) return;
        if (MessageBox.Show(this,
                $"删除供应商 {p.Name}？\nRouter 槽位引用会一并清理，并自动重启服务。",
                "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        var err = await Busy(_state.DeleteCcrProviderAsync(p.Name));
        if (err is not null) MessageBox.Show(err, "CCR Switch", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void EditDirect(DirectProvider provider)
    {
        using var editor = new DirectProviderEditorForm(_state.BackfilledDirect(provider));
        if (editor.ShowDialog(this) == DialogResult.OK && editor.Result is { } p)
        {
            var err = _state.SaveDirectProvider(p);
            if (err is not null) MessageBox.Show(err, "CCR Switch", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshAll();
        }
    }

    private void EditSelectedDirect()
    {
        if (SelectedDirect() is { } p) EditDirect(p);
    }

    /// <summary>执行耗时动作：面板内禁用按钮显示进度，完成后刷新。</summary>
    private async Task<string?> Busy(Task<string?> op)
    {
        var old = _statusLabel.Text;
        Enabled = false;
        _statusLabel.Text = "  ⏳ 处理中…";
        try
        {
            var err = await op;
            await _state.PollServiceAsync();
            RefreshAll();
            if (err is not null) MessageBox.Show(err, "CCR Switch", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return err;
        }
        finally
        {
            Enabled = true;
            _statusLabel.Text = old;
        }
    }
}
