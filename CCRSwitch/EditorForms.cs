using CCRSwitch.Core;
using System.Text.Json.Nodes;

namespace CCRSwitch;

/// <summary>CCR 供应商编辑器（添加/编辑）。</summary>
internal sealed class CcrProviderEditorForm : Form
{
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly TextBox _baseUrl = new() { Dock = DockStyle.Fill };
    private readonly TextBox _apiKey = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _models = new() { Dock = DockStyle.Fill, Multiline = true, Height = 96, Font = new Font("Consolas", 9F), ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _transformer = new() { Dock = DockStyle.Fill, Multiline = true, Height = 104, Font = new Font("Consolas", 9F), ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox _showKey = new() { Text = "显示", AutoSize = true };
    private readonly Func<string, bool> _nameExists;

    public CcrProvider? Result { get; private set; }

    public CcrProviderEditorForm(CcrProvider? existing, Func<string, bool> nameExists)
    {
        _nameExists = nameExists;
        Text = existing is null ? "添加 CCR 供应商" : "编辑 CCR 供应商";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(480, 540);
        Font = new Font("Segoe UI", 9F);

        if (existing is { } e)
        {
            _name.Text = e.Name;
            _baseUrl.Text = e.ApiBaseUrl;
            _apiKey.Text = e.ApiKey;
            _models.Text = string.Join("\r\n", e.Models);
            _transformer.Text = e.TransformerJson ?? "";
        }

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 2,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        void Field(string label, Control input, Control? extra = null, int height = 0)
        {
            table.Controls.Add(new Label { Text = label, Anchor = AnchorStyles.Left, AutoSize = true }, 0, row);
            if (extra is null)
            {
                input.Dock = DockStyle.Fill;
                input.Margin = new Padding(4);
                table.Controls.Add(input, 1, row);
            }
            else
            {
                var box = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
                input.Dock = DockStyle.Fill;
                extra.Dock = DockStyle.Right;
                box.Controls.Add(input);
                box.Controls.Add(extra);
                table.Controls.Add(box, 1, row);
            }
            table.RowStyles.Add(height > 0
                ? new RowStyle(SizeType.Absolute, height)
                : new RowStyle(SizeType.AutoSize));
            row++;
        }

        Field("名称", _name);
        Field("API Base URL", _baseUrl);
        _showKey.CheckedChanged += (_, _) => _apiKey.UseSystemPasswordChar = !_showKey.Checked;
        Field("API Key", _apiKey, _showKey);
        Field("模型（每行一个）", _models, height: 100);
        Field("transformer (JSON)", _transformer, height: 110);

        table.Controls.Add(new Label
        {
            Text = "例：{ \"use\": [[\"maxtoken\", {\"max_tokens\": 8192}]] }\r\n留空表示不使用 transformer",
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
        }, 1, row);
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row++;

        table.Controls.Add(new Control(), 0, row); // 占位填满
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var ok = new Button { Text = "保存", DialogResult = DialogResult.None, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += OnOk;
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        table.Controls.Add(buttons, 1, row + 1);
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Controls.Add(table);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void OnOk(object? sender, EventArgs e)
    {
        var name = _name.Text.Trim();
        var url = _baseUrl.Text.Trim();
        var models = _models.Lines.Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        var transformer = _transformer.Text.Trim();

        if (name.Length == 0) { Warn("名称不能为空"); return; }
        if (_nameExists(name)) { Warn($"已存在同名供应商：{name}"); return; }
        if (url.Length == 0) { Warn("API Base URL 不能为空"); return; }
        if (models.Count == 0) { Warn("至少需要一个模型"); return; }
        if (transformer.Length > 0 && JsonUtil.ParseTolerant(transformer) is not JsonObject)
        {
            Warn("transformer 不是合法 JSON 对象");
            return;
        }

        Result = new CcrProvider(name, url, _apiKey.Text, models, transformer.Length == 0 ? null : transformer);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Warn(string msg) =>
        MessageBox.Show(this, msg, "CCR Switch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}

/// <summary>直连供应商编辑器。</summary>
internal sealed class DirectProviderEditorForm : Form
{
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly TextBox _baseUrl = new() { Dock = DockStyle.Fill };
    private readonly TextBox _authToken = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _model = new() { Dock = DockStyle.Fill };
    private readonly TextBox _extraEnv = new() { Dock = DockStyle.Fill, Multiline = true, Height = 92, Font = new Font("Consolas", 9F), ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox _showKey = new() { Text = "显示", AutoSize = true };

    public DirectProvider? Result { get; private set; }

    public DirectProviderEditorForm(DirectProvider existing)
    {
        Text = existing.Name.Length == 0 ? "添加直连供应商" : "编辑直连供应商";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(480, 460);
        Font = new Font("Segoe UI", 9F);

        _name.Text = existing.Name;
        _baseUrl.Text = existing.BaseUrl;
        _authToken.Text = existing.AuthToken;
        _model.Text = existing.Model;
        _extraEnv.Text = string.Join("\r\n",
            existing.ExtraEnv.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
        Tag = existing.Id;

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 2,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        void Field(string label, Control input, Control? extra = null, int height = 0)
        {
            table.Controls.Add(new Label { Text = label, Anchor = AnchorStyles.Left, AutoSize = true }, 0, row);
            if (extra is null)
            {
                input.Dock = DockStyle.Fill;
                input.Margin = new Padding(4);
                table.Controls.Add(input, 1, row);
            }
            else
            {
                var box = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
                input.Dock = DockStyle.Fill;
                extra.Dock = DockStyle.Right;
                box.Controls.Add(input);
                box.Controls.Add(extra);
                table.Controls.Add(box, 1, row);
            }
            table.RowStyles.Add(height > 0
                ? new RowStyle(SizeType.Absolute, height)
                : new RowStyle(SizeType.AutoSize));
            row++;
        }

        Field("名称", _name);
        Field("Base URL", _baseUrl);
        _showKey.CheckedChanged += (_, _) => _authToken.UseSystemPasswordChar = !_showKey.Checked;
        Field("Auth Token", _authToken, _showKey);
        Field("模型", _model);
        Field("额外 env", _extraEnv, height: 96);

        table.Controls.Add(new Label
        {
            Text = "每行 KEY=VALUE，切换时一并写入；切到不含该键的供应商时自动清除。",
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
        }, 1, row);
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row++;

        table.Controls.Add(new Control(), 0, row);
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var ok = new Button { Text = "保存", DialogResult = DialogResult.None, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += OnOk;
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        table.Controls.Add(buttons, 1, row + 1);
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Controls.Add(table);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void OnOk(object? sender, EventArgs e)
    {
        var name = _name.Text.Trim();
        if (name.Length == 0) { Warn("名称不能为空"); return; }
        if (_baseUrl.Text.Trim().Length == 0) { Warn("Base URL 不能为空"); return; }

        var extra = new Dictionary<string, string>();
        foreach (var line in _extraEnv.Lines)
        {
            var t = line.Trim();
            if (t.Length == 0) continue;
            var eq = t.IndexOf('=');
            if (eq <= 0) continue;
            extra[t[..eq].Trim()] = t[(eq + 1)..];
        }

        Result = new DirectProvider
        {
            Id = (Guid)Tag!,
            Name = name,
            BaseUrl = _baseUrl.Text.Trim(),
            AuthToken = _authToken.Text,
            Model = _model.Text.Trim(),
            ExtraEnv = extra,
        };
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Warn(string msg) =>
        MessageBox.Show(this, msg, "CCR Switch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
