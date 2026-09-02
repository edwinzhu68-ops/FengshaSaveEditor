using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace FengshaSaveEditor;

internal sealed record HeroSlotChoice(
    string Name,
    string Path,
    int FileCount,
    DateTime LastActivity)
{
    public override string ToString() => $"{Name}    ·    {FileCount}/4 个核心文件";
}

internal sealed record PendingUnitEdit(
    string UnitType,
    string Attribute,
    int Value,
    bool SingleInstance,
    int InstanceIndex);

internal sealed record MultiplierChoice(decimal Value, string Label)
{
    public override string ToString() => Label;
}

internal sealed record PendingPlayerEdit(string Attribute, int Value);

internal sealed record PendingAdvancedEdit(
    string Key,
    string Kind,
    string Attribute,
    string Unit,
    int Value);

internal sealed class HeroEditorForm : Form
{
    private static readonly Color Background = Color.White;
    private static readonly Color Surface = Color.FromArgb(247, 247, 247);
    private static readonly Color SurfaceRaised = Color.White;
    private static readonly Color Border = Color.FromArgb(190, 190, 190);
    private static readonly Color TextPrimary = Color.FromArgb(20, 20, 20);
    private static readonly Color TextSecondary = Color.FromArgb(65, 65, 65);
    private static readonly Color TextMuted = Color.FromArgb(115, 115, 115);
    private static readonly Color Gold = Color.FromArgb(235, 235, 235);
    private static readonly Color GoldLight = Color.FromArgb(220, 220, 220);
    private static readonly Color Green = Color.FromArgb(37, 116, 61);
    private static readonly Color Red = Color.FromArgb(164, 45, 45);
    private static readonly Color Warning = Color.FromArgb(145, 95, 20);
    private static readonly JsonSerializerOptions CliJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _defaultSaveRoot;
    private string _saveRoot;
    private string? _slotPath;
    private bool _refreshingSlots;
    private bool _busy;
    private bool _dirty;
    private bool _suppressSelection;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _unitInputCts;

    private TableLayoutPanel _shell = null!;
    private ToolStrip _toolbar = null!;
    private ToolStripButton _saveButton = null!;
    private ToolStripLabel _multiplierLabel = null!;
    private ToolStripComboBox _multiplierPicker = null!;
    private ComboBox _slotPicker = null!;
    private Label _folderLabel = null!;
    private Label _fileStatusLabel = null!;
    private Label _gameStatusLabel = null!;
    private TabControl _tabs = null!;
    private ToolStripStatusLabel _statusLabel = null!;

    private DataGridView? _unitGrid;
    private TextBox? _unitSearch;
    private string _selectedUnitType = "MinFu";
    private Label? _unitSelectedLabel;
    private readonly Dictionary<string, NumericUpDown> _unitAttributeInputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _unitInitialValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unitChangedAttributes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PendingUnitEdit> _pendingUnitEdits = new(StringComparer.OrdinalIgnoreCase);
    private bool _loadingUnitInputs;
    private NumericUpDown? _unitInstance;
    private RadioButton? _unitAllRadio;
    private RadioButton? _unitSingleRadio;
    private Label? _unitCurrentLabel;

    private bool _pendingAllResources;
    private decimal _pendingAllResourceMultiplier = 1m;
    private decimal _selectedMultiplier = 1m;
    private bool _loadingMultiplier;

    private bool _pendingBuildingStorage;
    private decimal _pendingBuildingMultiplier = 1m;
    private NumericUpDown? _resourceBatchMultiplier;
    private NumericUpDown? _buildingBatchMultiplier;
    private Label? _buildingReadbackLabel;
    private ToolTip? _buildingReadbackTip;
    private ResourceListResponse? _resourceReadback;
    private BuildingStorageListResponse? _buildingReadback;
    private bool _loadingBatchInputs;

    private DataGridView? _playerGrid;
    private TextBox? _playerSearch;
    private string _selectedPlayerAttribute = "AT_CartCapacity";
    private Label? _playerSelectedLabel;
    private Dictionary<string, string> _playerStates = new(StringComparer.OrdinalIgnoreCase);
    private NumericUpDown? _playerValue;
    private readonly Dictionary<string, PendingPlayerEdit> _pendingPlayerEdits = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, NumericUpDown> _advancedInputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Kind, string Attribute, string Unit)> _advancedOperations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PendingAdvancedEdit> _pendingAdvancedEdits = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _advancedInitialValues = new(StringComparer.OrdinalIgnoreCase);
    private bool _loadingAdvancedInputs;

    public HeroEditorForm()
    {
        _defaultSaveRoot = GetDefaultSaveRoot();
        _saveRoot = _defaultSaveRoot;
        Text = "烽沙 · 存档编辑器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1120, 640);
        ClientSize = new Size(1280, 760);
        BackColor = Background;
        ForeColor = TextPrimary;
        Font = new Font("Microsoft YaHei UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BuildShell();
        FormClosing += HandleFormClosing;
        RefreshSlots();
        Shown += (_, _) => PromptForFolderIfDefaultEmpty();
        UpdateGlobalStatus();
    }

    private void BuildShell()
    {
        _shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Background,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0)
        };
        _shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        _shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        _shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        _toolbar = BuildToolbar();
        _shell.Controls.Add(_toolbar, 0, 0);
        _shell.Controls.Add(BuildSaveBar(), 0, 1);
        _tabs = BuildTabs();
        _shell.Controls.Add(_tabs, 0, 2);
        _shell.Controls.Add(BuildStatusBar(), 0, 3);
        Controls.Add(_shell);
    }

    private ToolStrip BuildToolbar()
    {
        var toolbar = new ToolStrip
        {
            Dock = DockStyle.Fill,
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = Background,
            ForeColor = TextPrimary,
            Padding = new Padding(8, 3, 8, 3),
            Renderer = new LightToolStripRenderer()
        };

        var brand = new ToolStripLabel("烽沙 · 存档编辑器")
        {
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            ForeColor = TextPrimary,
            Margin = new Padding(4, 0, 16, 0)
        };
        toolbar.Items.Add(brand);
        toolbar.Items.Add(ToolButton("打开存档", (_, _) => BrowseFolder()));
        toolbar.Items.Add(ToolButton("重新读取", async (_, _) => await ReloadCurrentTabAsync()));
        _multiplierLabel = new ToolStripLabel("修改倍数")
        {
            ForeColor = TextPrimary,
            Margin = new Padding(12, 0, 4, 0)
        };
        toolbar.Items.Add(_multiplierLabel);
        _multiplierPicker = new ToolStripComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 82,
            BackColor = SurfaceRaised,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            IntegralHeight = false
        };
        foreach (var multiplier in SupportedMultipliers())
        {
            _multiplierPicker.Items.Add(new MultiplierChoice(multiplier, $"{multiplier:0.##} 倍"));
        }
        _multiplierPicker.SelectedIndexChanged += (_, _) => MultiplierChanged();
        _multiplierPicker.SelectedIndex = 0;
        toolbar.Items.Add(_multiplierPicker);
        _saveButton = ToolButton("保存修改", async (_, _) => await ApplyCurrentTabAsync());
        toolbar.Items.Add(_saveButton);
        toolbar.Items.Add(new ToolStripLabel("请先备份存档，避免损坏")
        {
            ForeColor = Warning,
            Margin = new Padding(8, 0, 0, 0)
        });
        return toolbar;
    }

    private Control BuildSaveBar()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Background,
            Padding = new Padding(14, 8, 14, 6)
        };
        panel.Paint += DrawBottomBorder;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));

        grid.Controls.Add(TopLabel("存档目录"), 0, 0);
        _folderLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Text = "尚未选择",
            ForeColor = TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 8, 0)
        };
        var folderTip = new ToolTip();
        folderTip.SetToolTip(_folderLabel, "一个存档由多个 .sav 文件组成，请选择文件夹，不要只选择单个文件。");
        grid.Controls.Add(_folderLabel, 1, 0);
        grid.Controls.Add(TopLabel("槽位"), 2, 0);
        _slotPicker = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = SurfaceRaised,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            IntegralHeight = false
        };
        _slotPicker.SelectedIndexChanged += (_, _) => SlotChanged();
        grid.Controls.Add(_slotPicker, 3, 0);
        _fileStatusLabel = SecondaryLabel("文件 0/4");
        grid.Controls.Add(_fileStatusLabel, 4, 0);
        _gameStatusLabel = SecondaryLabel("检查中");
        _gameStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        grid.Controls.Add(_gameStatusLabel, 5, 0);
        var open = SmallButton("选择文件夹", (_, _) => BrowseFolder());
        grid.Controls.Add(open, 6, 0);
        panel.Controls.Add(grid);
        return panel;
    }

    private TabControl BuildTabs()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            BackColor = Background,
            ForeColor = TextPrimary,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            ItemSize = new Size(116, 34),
            SizeMode = TabSizeMode.Fixed,
            Padding = new Point(16, 4)
        };
        tabs.DrawItem += DrawTab;
        tabs.SelectedIndexChanged += async (_, _) =>
        {
            var page = tabs.SelectedTab?.Name;
            UpdateMultiplierToolbarVisibility(page);
            if (page is not null) await LoadPageAsync(page);
            UpdateGlobalStatus();
        };

        tabs.TabPages.Add(MakeTab("units", "单位属性", BuildUnitsPage()));
        tabs.TabPages.Add(MakeTab("buildings", "建筑", BuildBuildingsPage()));
        tabs.TabPages.Add(MakeTab("player", "玩家属性", BuildPlayerPage()));
        tabs.TabPages.Add(MakeTab("advanced", "高级功能", BuildAdvancedPage()));
        tabs.SelectedIndex = 0;
        UpdateMultiplierToolbarVisibility(tabs.SelectedTab?.Name);
        return tabs;
    }

    private static TabPage MakeTab(string name, string text, Control content)
    {
        var tab = new TabPage(text)
        {
            Name = name,
            BackColor = Background,
            ForeColor = TextPrimary,
            Padding = new Padding(12)
        };
        content.Dock = DockStyle.Fill;
        tab.Controls.Add(content);
        return tab;
    }

    private Control BuildUnitsPage()
    {
        var split = NewEditorSplit();
        var left = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(10) };
        left.Paint += DrawBorder;
        _unitSearch = SearchBox("搜索单位");
        _unitSearch.TextChanged += (_, _) => FilterGrid(_unitGrid, _unitSearch.Text);
        _unitGrid = CreateGrid(("单位", 300), ("数量", 120));
        _unitGrid.Dock = DockStyle.Fill;
        _unitGrid.Margin = new Padding(0, 8, 0, 0);
        left.Controls.Add(_unitGrid);
        left.Controls.Add(_unitSearch);
        split.Panel1.Controls.Add(left);

        var right = EditorPanel();
        right.Controls.Add(PageHeading("单位属性", ""));
        _unitSelectedLabel = ValueLabel(UnitScanner.GetUnitLabel(_selectedUnitType));
        right.Controls.Add(FormLine("当前单位", _unitSelectedLabel, ""));

        var scope = new GroupBox
        {
            Text = "范围",
            ForeColor = TextPrimary,
            BackColor = Background,
            Dock = DockStyle.Top,
            Height = 64,
            Padding = new Padding(12, 8, 12, 6),
            Margin = new Padding(0, 8, 0, 8)
        };
        _unitAllRadio = new RadioButton { Text = "全部区域", Checked = true, AutoSize = true, ForeColor = TextPrimary, BackColor = Background, Location = new Point(14, 25) };
        _unitSingleRadio = new RadioButton { Text = "单个区域", AutoSize = true, ForeColor = TextPrimary, BackColor = Background, Location = new Point(110, 25) };
        _unitInstance = NumberBox(0, 0, 1_000_000);
        _unitInstance.Width = 90;
        _unitInstance.Enabled = false;
        _unitInstance.ValueChanged += (_, _) => UpdateUnitSummary();
        _unitAllRadio.CheckedChanged += (_, _) => { _unitInstance.Enabled = !_unitAllRadio.Checked; UpdateUnitSummary(); };
        _unitSingleRadio.CheckedChanged += (_, _) => { _unitInstance.Enabled = _unitSingleRadio.Checked; UpdateUnitSummary(); };
        scope.Controls.Add(_unitAllRadio);
        scope.Controls.Add(_unitSingleRadio);
        _unitInstance.Location = new Point(190, 20);
        scope.Controls.Add(_unitInstance);
        right.Controls.Add(scope);

        _unitCurrentLabel = SecondaryLabel("全部匹配单位区域");
        _unitCurrentLabel.Dock = DockStyle.Top;
        _unitCurrentLabel.Height = 30;
        right.Controls.Add(_unitCurrentLabel);
        right.Controls.Add(InlineSecondaryLabel("在窗口顶部选择修改倍数，会按当前读取值批量设置下面的属性；1 倍表示保持当前值。"));
        var properties = CreateNumericPropertyRows(
            UnitScanner.SupportedAttributes,
            _unitAttributeInputs,
            DefaultUnitAttributeValue);
        right.Controls.Add(properties);
        var actions = ActionBar();
        actions.Controls.Add(SmallButton("读取当前值", async (_, _) => await RunUnitReadAsync()));
        actions.Controls.Add(SmallButton("预览", async (_, _) => await PreviewUnitAsync()));
        right.Controls.Add(actions);
        _unitGrid.SelectionChanged += (_, _) =>
        {
            if (_suppressSelection) return;
            if (_unitGrid.CurrentRow?.Tag is string type)
            {
                _selectedUnitType = type;
                UpdateUnitSummary();
                _ = ReloadSelectedUnitInputsAsync(type);
            }
        };
        PopulateUnitGrid(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        split.Panel2.Controls.Add(right);
        return split;
    }

    private Control BuildBuildingsPage()
    {
        var page = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Background,
            Padding = new Padding(28, 28, 28, 28)
        };

        var rows = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.Transparent,
            Padding = new Padding(0)
        };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        _buildingReadbackLabel = SecondaryLabel("当前存档：尚未读取");
        _buildingReadbackLabel.Dock = DockStyle.Top;
        _buildingReadbackLabel.Height = 34;
        _buildingReadbackTip = new ToolTip
        {
            AutoPopDelay = 12_000,
            InitialDelay = 350,
            ReshowDelay = 100,
            ShowAlways = true
        };

        _resourceBatchMultiplier = BatchMultiplierBox();
        _resourceBatchMultiplier.ValueChanged += (_, _) =>
        {
            if (!_loadingBatchInputs) StageResourceBatchMultiplier(_resourceBatchMultiplier.Value);
        };
        _resourceBatchMultiplier.TextChanged += (_, _) =>
        {
            if (!_loadingBatchInputs) UpdateGlobalStatus();
        };
        _resourceBatchMultiplier.Validated += (_, _) =>
        {
            if (!_loadingBatchInputs) StageResourceBatchMultiplier(_resourceBatchMultiplier.Value);
        };
        _buildingBatchMultiplier = BatchMultiplierBox();
        _buildingBatchMultiplier.ValueChanged += (_, _) =>
        {
            if (!_loadingBatchInputs) StageBuildingBatchMultiplier(_buildingBatchMultiplier.Value);
        };
        _buildingBatchMultiplier.TextChanged += (_, _) =>
        {
            if (!_loadingBatchInputs) UpdateGlobalStatus();
        };
        _buildingBatchMultiplier.Validated += (_, _) =>
        {
            if (!_loadingBatchInputs) StageBuildingBatchMultiplier(_buildingBatchMultiplier.Value);
        };

        rows.Controls.Add(BatchMultiplierLabel("矿产（几倍）"), 0, 0);
        rows.Controls.Add(_resourceBatchMultiplier, 1, 0);
        rows.Controls.Add(BatchMultiplierLabel("仓库（几倍）"), 0, 1);
        rows.Controls.Add(_buildingBatchMultiplier, 1, 1);
        page.Controls.Add(rows);
        page.Controls.Add(_buildingReadbackLabel);
        UpdateBuildingReadbackDisplay();
        return page;
    }

    private Control BuildPlayerPage()
    {
        var split = NewEditorSplit();
        var left = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(10) };
        left.Paint += DrawBorder;
        _playerSearch = SearchBox("搜索玩家属性");
        _playerSearch.TextChanged += (_, _) => FilterGrid(_playerGrid, _playerSearch.Text);
        _playerGrid = CreateGrid(("属性", 250), ("当前值", 340));
        _playerGrid.Dock = DockStyle.Fill;
        left.Controls.Add(_playerGrid);
        left.Controls.Add(_playerSearch);
        split.Panel1.Controls.Add(left);

        var right = EditorPanel();
        right.Controls.Add(PageHeading("玩家属性", ""));
        right.Controls.Add(InlineSecondaryLabel("在窗口顶部选择修改倍数，会按当前读取值设置选中的玩家属性；1 倍表示保持当前值。"));
        _playerSelectedLabel = ValueLabel(GetEditorLabel(_selectedPlayerAttribute));
        right.Controls.Add(FormLine("当前属性", _playerSelectedLabel, ""));
        _playerValue = NumberBox(FromStorageValue(_selectedPlayerAttribute, DefaultPlayerAttributeValue(_selectedPlayerAttribute)));
        _playerValue.ValueChanged += (_, _) => { if (!_suppressSelection) StagePlayerInput(); };
        right.Controls.Add(FormLine("目标值", _playerValue, ""));
        var actions = ActionBar();
        actions.Controls.Add(SmallButton("读取属性", async (_, _) => await RunReadOnlyAsync("--list-player-attributes")));
        actions.Controls.Add(SmallButton("预览", async (_, _) => await PreviewPlayerAsync()));
        right.Controls.Add(actions);
        _playerGrid.SelectionChanged += (_, _) =>
        {
            if (_playerGrid.CurrentRow?.Tag is string attr) SelectPlayerAttribute(attr);
        };
        PopulatePlayerGrid(Array.Empty<string>(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        split.Panel2.Controls.Add(right);
        return split;
    }

    private Control BuildAdvancedPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Background, AutoScroll = true, Padding = new Padding(12) };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0)
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(PageHeading("高级功能", ""), 0, 0);
        var note = SecondaryLabel("当前值自动从存档读取；修改会先暂存在本工具中，点击顶部“保存修改”后才写入。选择顶部修改倍数后，会按当前读取值设置下面的高级属性；1 倍表示保持当前值。所有民夫只改当前存档民夫，全部自有兵种包含民夫、常规兵种和攻城器械，不包含野兽、建筑、城防设施。");
        grid.Controls.Add(note, 0, 1);

        _advancedInputs.Clear();
        _advancedOperations.Clear();

        var properties = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(0, 4, 0, 0)
        };
        AddAdvancedInput(properties, "所有民夫移速", "MinFuMoveSpeed", "unit", "AT_MoveSpeed", "MinFu", 200);
        AddAdvancedInput(properties, "全部自有兵种移速", "AllUnitMoveSpeed", "unit", "AT_MoveSpeed", UnitScanner.PlayerOwnedUnitSelectionKey, 200);
        AddAdvancedInput(properties, "每次搬运数量", "CartCapacity", "player", "AT_CartCapacity", "", 10);
        AddAdvancedInput(properties, "搬运效率", "CarryEfficiency", "player", "AT_CarryEfficiency", "", 1);
        AddAdvancedInput(properties, "采集效率", "CollectEfficiency", "player", "AT_CollectEfficiency", "", 1);
        AddAdvancedInput(properties, "制作效率", "CraftEfficiency", "player", "AT_CraftEfficiency", "", 1);
        AddAdvancedInput(properties, "政策移速", "PolicyMoveSpeed", "player", "AT_PolicyMoveSpeed", "", 0);
        AddAdvancedInput(properties, "道路税", "RoadToll", "player", "AT_RoadToll", "", 0);
        grid.Controls.Add(properties, 0, 2);
        page.Controls.Add(grid);
        return page;
    }

    private Control BuildStatusBar()
    {
        var status = new StatusStrip
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            ForeColor = TextSecondary,
            SizingGrip = false,
            Renderer = new LightStatusRenderer()
        };
        _statusLabel = new ToolStripStatusLabel("就绪");
        status.Items.Add(_statusLabel);
        return status;
    }

    private async Task LoadPageAsync(string page)
    {
        _loadCts?.Cancel();
        var cancellation = new CancellationTokenSource();
        _loadCts = cancellation;
        try
        {
            switch (page)
            {
                case "units":
                    await LoadUnitChoicesAsync(cancellation.Token);
                    break;
                case "buildings":
                    await LoadBuildingReadbackAsync(cancellation.Token);
                    break;
                case "player":
                    await LoadPlayerChoicesAsync(cancellation.Token);
                    break;
                case "advanced":
                    await LoadAdvancedChoicesAsync(cancellation.Token);
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // 快速切换槽位或标签页时，放弃旧的读取结果。
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cancellation)) _loadCts = null;
            cancellation.Dispose();
        }
    }

    private async Task ReloadCurrentTabAsync()
    {
        if (_tabs.SelectedTab?.Name is string page) await LoadPageAsync(page);
        AppendLog("已重新读取当前页面。");
    }

    private async Task LoadBuildingReadbackAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_slotPath) || _busy) return;

        ResourceListResponse? resources = null;
        BuildingStorageListResponse? warehouses = null;
        var errors = new List<string>();

        try
        {
            var result = await RunCliAsync(
                BuildCliArgs("--list-resources", "--json"),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            resources = ParseCliJson<ResourceListResponse>(result, "矿产数据");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add("矿产读取失败：" + ex.Message);
        }

        try
        {
            var result = await RunCliAsync(
                BuildCliArgs("--list-building-storage", "--json"),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            warehouses = ParseCliJson<BuildingStorageListResponse>(result, "仓库数据");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add("仓库读取失败：" + ex.Message);
        }

        _resourceReadback = resources;
        _buildingReadback = warehouses;
        UpdateBuildingReadbackDisplay(errors);
    }

    private void UpdateBuildingReadbackDisplay(IReadOnlyList<string>? errors = null)
    {
        if (_buildingReadbackLabel is null) return;

        var summary = new List<string>();
        if (_resourceReadback is not null)
        {
            var resourceCount = _resourceReadback.Nodes.Sum(item => item.NodeCount);
            summary.Add($"矿产 {resourceCount:N0} 处");
        }

        if (_buildingReadback is not null)
        {
            foreach (var group in _buildingReadback.Buildings
                         .GroupBy(item => item.Label, StringComparer.Ordinal)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                summary.Add($"{group.Key} {group.Count():N0} 座");
            }
        }

        if (summary.Count == 0)
        {
            _buildingReadbackLabel.Text = errors is { Count: > 0 }
                ? "当前存档：读取失败，请点击“重新读取”"
                : "当前存档：尚未读取";
        }
        else
        {
            _buildingReadbackLabel.Text = "当前存档：" + string.Join("  ·  ", summary);
        }

        if (_buildingReadbackTip is null) return;
        if (_resourceBatchMultiplier is not null)
        {
            _buildingReadbackTip.SetToolTip(
                _resourceBatchMultiplier,
                _resourceReadback is null
                    ? "填写本次矿产上限的倍数：1 倍=不修改，2 倍=当前存档上限×2。"
                    : BuildResourceReadbackTip(_resourceReadback));
        }

        if (_buildingBatchMultiplier is not null)
        {
            _buildingReadbackTip.SetToolTip(
                _buildingBatchMultiplier,
                _buildingReadback is null
                    ? "填写本次仓库上限的倍数：1 倍=不修改，2 倍=当前存档上限×2。"
                    : BuildBuildingReadbackTip(_buildingReadback));
        }

        var readbackTip = new List<string>();
        if (_resourceReadback is not null) readbackTip.Add(BuildResourceReadbackTip(_resourceReadback));
        if (_buildingReadback is not null) readbackTip.Add(BuildBuildingReadbackTip(_buildingReadback));
        _buildingReadbackTip.SetToolTip(
            _buildingReadbackLabel,
            readbackTip.Count == 0 ? "进入本页后会自动读取当前存档。" : string.Join("\r\n\r\n", readbackTip));
    }

    private static string BuildResourceReadbackTip(ResourceListResponse response)
    {
        var rows = response.Nodes
            .OrderBy(item => item.Label, StringComparer.Ordinal)
            .ThenBy(item => item.SizeLabel, StringComparer.Ordinal)
            .Select(item =>
                $"{item.Label}（{item.SizeLabel}）：上限 {FormatSavedResourceCapacity(item.Capacity)} × {item.NodeCount:N0} 处")
            .ToList();
        return "当前存档已读取：\r\n"
            + (rows.Count == 0 ? "没有识别到矿产。" : string.Join("\r\n", rows))
            + "\r\n\r\n输入框填写本次修改倍数：1 倍=不修改。";
    }

    private static string BuildBuildingReadbackTip(BuildingStorageListResponse response)
    {
        var rows = response.Buildings
            .OrderBy(item => item.Label, StringComparer.Ordinal)
            .Select(item => $"{item.Label}：{item.Current}")
            .ToList();
        return "当前存档已读取：\r\n"
            + (rows.Count == 0 ? "没有识别到仓库。" : string.Join("\r\n", rows))
            + "\r\n\r\n输入框填写本次修改倍数：1 倍=不修改。";
    }

    private static string FormatSavedResourceCapacity(int rawValue)
    {
        return rawValue >= 0 && rawValue % 256 == 0
            ? (rawValue / 256).ToString("N0", CultureInfo.InvariantCulture)
            : rawValue.ToString("N0", CultureInfo.InvariantCulture);
    }

    private async Task ApplyCurrentTabAsync() => await SaveAllPendingAsync();

    private async Task LoadUnitChoicesAsync(CancellationToken cancellationToken)
    {
        if (_unitGrid is null || string.IsNullOrWhiteSpace(_slotPath) || _busy) return;
        try
        {
            var result = await RunCliAsync(BuildCliArgs("--list-units", "--json"), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var payload = ParseCliJson<UnitListResponse>(result, "单位目录");
            foreach (var item in payload.Units)
            {
                if (item.Count > 0) counts[item.Key] = item.Count;
            }
            var selected = _selectedUnitType;
            _suppressSelection = true;
            cancellationToken.ThrowIfCancellationRequested();
            PopulateUnitGrid(counts);
            _selectedUnitType = UnitScanner.IsKnownSelection(selected) ? selected : "MinFu";
            SelectUnitRow(_selectedUnitType);
            _suppressSelection = false;
            UpdateUnitSummary();
            await LoadUnitAttributeInputsAsync(cancellationToken, _selectedUnitType);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _suppressSelection = false;
            AppendLog("单位目录读取失败：" + ex.Message);
        }
    }

    private async Task ReloadSelectedUnitInputsAsync(string unitType)
    {
        _unitInputCts?.Cancel();
        _unitInputCts?.Dispose();
        var cancellation = new CancellationTokenSource();
        _unitInputCts = cancellation;
        try
        {
            await LoadUnitAttributeInputsAsync(cancellation.Token, unitType);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // 选择了另一个单位，放弃旧单位的属性结果。
        }
        finally
        {
            if (ReferenceEquals(_unitInputCts, cancellation))
            {
                _unitInputCts = null;
                cancellation.Dispose();
            }
        }
    }

    private async Task LoadUnitAttributeInputsAsync(CancellationToken cancellationToken, string? requestedUnit = null)
    {
        if (_unitGrid is null || string.IsNullOrWhiteSpace(_slotPath) || _busy) return;
        var unitType = requestedUnit ?? _selectedUnitType;
        try
        {
            var result = await RunCliAsync(
                BuildCliArgs("--list-attributes", "--unit", unitType, "--json"),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!unitType.Equals(_selectedUnitType, StringComparison.OrdinalIgnoreCase)) return;
            var payload = ParseCliJson<AttributeListResponse>(result, "单位属性");
            _loadingUnitInputs = true;
            _unitChangedAttributes.Clear();
            _unitInitialValues.Clear();
            foreach (var pair in UnitScanner.SupportedAttributes)
            {
                if (!_unitAttributeInputs.TryGetValue(pair.Key, out var input)) continue;
                var item = payload.Attributes.FirstOrDefault(attribute => attribute.Key.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
                var rawValue = item is null ? DefaultUnitAttributeValue(pair.Key) : ParseFirstInteger(item.Current) ?? DefaultUnitAttributeValue(pair.Key);
                _unitInitialValues[pair.Key] = rawValue;
                if (TryGetPendingUnitEdit(unitType, pair.Key, out var pending))
                {
                    SetInputFromStorage(input, pair.Key, pending.Value);
                    _unitChangedAttributes.Add(pair.Key);
                }
                else
                {
                    SetInputFromStorage(input, pair.Key, rawValue);
                }
            }
            _loadingUnitInputs = false;
            if (_selectedMultiplier != 1m) ApplyUnitMultiplier();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _loadingUnitInputs = false;
            AppendLog("单位属性读取失败：" + ex.Message);
        }
    }

    private async Task LoadPlayerChoicesAsync(CancellationToken cancellationToken)
    {
        if (_playerGrid is null || string.IsNullOrWhiteSpace(_slotPath) || _busy) return;
        try
        {
            var result = await RunCliAsync(BuildCliArgs("--list-player-attributes", "--json"), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var payload = ParseCliJson<PlayerListResponse>(result, "玩家属性目录");
            var names = payload.Attributes
                .Select(item => item.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var states = payload.Attributes
                .ToDictionary(item => item.Key, item => item.Current, StringComparer.OrdinalIgnoreCase);
            _playerStates = states;
            cancellationToken.ThrowIfCancellationRequested();
            PopulatePlayerGrid(names, states);
            var selected = names.Contains(_selectedPlayerAttribute, StringComparer.OrdinalIgnoreCase)
                ? _selectedPlayerAttribute
                : names.OrderBy(GetEditorLabel, StringComparer.Ordinal).FirstOrDefault() ?? "AT_CartCapacity";
            SelectPlayerRow(selected);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppendLog("玩家属性读取失败：" + ex.Message);
        }
    }

    private async Task LoadAdvancedChoicesAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_slotPath) || _busy) return;
        try
        {
            var unitResult = await RunCliAsync(
                BuildCliArgs("--list-attributes", "--unit", UnitScanner.PlayerOwnedUnitSelectionKey, "--json"),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var unitPayload = ParseCliJson<AttributeListResponse>(unitResult, "高级单位属性");

            var playerResult = await RunCliAsync(
                BuildCliArgs("--list-player-attributes", "--json"),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var playerPayload = ParseCliJson<PlayerListResponse>(playerResult, "高级玩家属性");

            _loadingAdvancedInputs = true;
            foreach (var pair in _advancedOperations)
            {
                var current = pair.Value.Kind.Equals("unit", StringComparison.OrdinalIgnoreCase)
                    ? unitPayload.Attributes.FirstOrDefault(attribute => attribute.Key.Equals(pair.Value.Attribute, StringComparison.OrdinalIgnoreCase))
                        ?.Current
                    : playerPayload.Attributes.FirstOrDefault(attribute => attribute.Key.Equals(pair.Value.Attribute, StringComparison.OrdinalIgnoreCase))
                        ?.Current;
                var rawValue = ParseFirstInteger(current) ?? DefaultAdvancedAttributeValue(pair.Value.Attribute);
                _advancedInitialValues[pair.Key] = rawValue;
                if (_pendingAdvancedEdits.TryGetValue(pair.Key, out var pending))
                {
                    if (_advancedInputs.TryGetValue(pair.Key, out var pendingInput)) SetInputFromStorage(pendingInput, pair.Value.Attribute, pending.Value);
                }
                else if (_advancedInputs.TryGetValue(pair.Key, out var input))
                {
                    SetInputFromStorage(input, pair.Value.Attribute, rawValue);
                }
            }
            _loadingAdvancedInputs = false;
            if (_selectedMultiplier != 1m) ApplyAdvancedMultiplier();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _loadingAdvancedInputs = false;
            AppendLog("高级功能读取失败：" + ex.Message);
        }
    }

    private async Task RunUnitReadAsync()
    {
        if (!EnsureSlot()) return;
        var unit = _selectedUnitType;
        await RunReadOnlyAsync("--list-attributes", "--unit", unit);
    }

    private async Task PreviewUnitAsync()
    {
        if (!EnsureSlot()) return;
        var changes = GetUnitChanges();
        if (changes.Count == 0)
        {
            MessageBox.Show("请先在右侧修改至少一个数值。", "没有待预览修改", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        foreach (var change in changes)
        {
            var args = BuildUnitWriteArgs(change.Attribute, change.Value, true);
            await RunReadOnlyAsync(args.ToArray());
        }
        SetDirtySummary($"{UnitScanner.GetUnitLabel(_selectedUnitType)} · {changes.Count} 项");
    }

    private List<(string Attribute, int Value)> GetUnitChanges()
    {
        var single = _unitSingleRadio?.Checked == true;
        var instance = (int)(_unitInstance?.Value ?? 0);
        return _pendingUnitEdits.Values
            .Where(edit => edit.UnitType.Equals(_selectedUnitType, StringComparison.OrdinalIgnoreCase))
            .Where(edit => edit.SingleInstance == single && (!single || edit.InstanceIndex == instance))
            .OrderBy(edit => UnitScanner.GetAttributeLabel(edit.Attribute), StringComparer.Ordinal)
            .Select(edit => (edit.Attribute, edit.Value))
            .ToList();
    }

    private List<string> BuildUnitWriteArgs(string attribute, int value, bool preview)
    {
        return BuildUnitWriteArgs(
            new PendingUnitEdit(
                _selectedUnitType,
                attribute,
                value,
                _unitSingleRadio?.Checked == true,
                (int)(_unitInstance?.Value ?? 0)),
            preview);
    }

    private static List<string> BuildUnitWriteArgs(PendingUnitEdit edit, bool preview)
    {
        var args = new List<string>
        {
            "--unit", edit.UnitType,
            "--attribute", edit.Attribute,
            "--value", edit.Value.ToString(CultureInfo.InvariantCulture)
        };
        if (edit.SingleInstance)
        {
            args.Add("--unit-instance");
            args.Add(edit.InstanceIndex.ToString(CultureInfo.InvariantCulture));
        }
        if (preview) args.Add("--dry-run");
        else args.Add("--yes");
        return args;
    }

    private static List<string> BuildResourceArgs(string category, int? configId, decimal multiplier, bool preview)
    {
        var args = new List<string>
        {
            "--resource", category,
            "--resource-multiplier", multiplier.ToString("0.##", CultureInfo.InvariantCulture)
        };
        if (configId.HasValue) { args.Add("--resource-config"); args.Add(configId.Value.ToString()); }
        if (preview) args.Add("--dry-run");
        return args;
    }

    private async Task PreviewPlayerAsync()
    {
        if (!EnsureSlot()) return;
        var attr = SelectedPlayerAttribute();
        var value = ToStorageValue(attr, _playerValue?.Value ?? 0);
        await RunReadOnlyAsync("--player-attribute", attr, "--player-value", value.ToString(CultureInfo.InvariantCulture), "--dry-run");
        SetDirtySummary($"{GetEditorLabel(attr)} → {FormatDisplayValue(attr, value)}");
    }

    private async Task PreviewAdvancedEntryAsync(string key)
    {
        if (!EnsureSlot() || !_advancedOperations.TryGetValue(key, out var operation) || !_advancedInputs.TryGetValue(key, out var input)) return;
        var rawValue = ToStorageValue(operation.Attribute, input.Value);
        var args = BuildAdvancedArgs(operation, input.Value, true);
        await RunReadOnlyAsync(args.ToArray());
        SetDirtySummary($"{GetAdvancedLabel(key)} → {FormatDisplayValue(operation.Attribute, rawValue)}");
    }

    private async Task SaveAllPendingAsync()
    {
        if (!EnsureSlot()) return;
        if (IsGameRunning())
        {
            MessageBox.Show(
                "请先完全退出《烽沙》，再点击保存修改。游戏运行时会用内存中的建筑数据覆盖仓库容量。",
                "请先退出游戏",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var operations = BuildPendingWriteArgs();
        if (operations.Count == 0)
        {
            MessageBox.Show("请先修改数值。修改会先暂存在这里，点击顶部“保存修改”后才写入存档。", "没有待保存修改", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var summary = BuildPendingSummary();
        var warning = LiveWriteWarning();
        var message = $"本次将写入 {operations.Count:N0} 项修改：\r\n{summary}\r\n\r\n请先备份存档，避免损坏。{warning}\r\n确定保存吗？";
        if (MessageBox.Show(message, "保存修改", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        if (!await RunWriteBatchAsync(operations)) return;

        // 写回成功后先清空暂存状态，再从磁盘重新读取当前页面。
        // 最后一遍清理是为了防止控件在回读期间触发 ValueChanged，
        // 把已经写入存档的值错误地重新标记成“待保存”。
        ClearPendingEdits();
        ResetMultiplierSelection();
        await ReloadCurrentTabAsync();
        ClearPendingEdits();
        _dirty = false;
        UpdateGlobalStatus();
        _statusLabel.Text = "修改已保存，已重新读取存档";
    }

    private List<IReadOnlyList<string>> BuildPendingWriteArgs()
    {
        // 保存时重新读取批量输入框，不依赖输入事件的触发时序。
        SyncBatchInputs();
        var operations = new List<IReadOnlyList<string>>();
        foreach (var edit in _pendingUnitEdits.Values
                     .OrderBy(edit => edit.UnitType, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(edit => edit.Attribute, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(edit => edit.SingleInstance)
                     .ThenBy(edit => edit.InstanceIndex))
        {
            operations.Add(BuildUnitWriteArgs(edit, false));
        }

        if (_pendingAllResources)
        {
            operations.Add(BuildResourceArgs("*", null, _pendingAllResourceMultiplier, false).Append("--yes").ToArray());
        }

        if (_pendingBuildingStorage)
        {
            operations.Add(new[]
            {
                "--building-multiplier",
                _pendingBuildingMultiplier.ToString("0.##", CultureInfo.InvariantCulture),
                "--yes"
            });
        }

        foreach (var edit in _pendingPlayerEdits.Values.OrderBy(edit => edit.Attribute, StringComparer.OrdinalIgnoreCase))
        {
            operations.Add(new[]
            {
                "--player-attribute", edit.Attribute,
                "--player-value", edit.Value.ToString(CultureInfo.InvariantCulture),
                "--yes"
            });
        }

        foreach (var edit in _pendingAdvancedEdits.Values.OrderBy(edit => edit.Key, StringComparer.OrdinalIgnoreCase))
        {
            operations.Add(BuildAdvancedArgs(
                (edit.Kind, edit.Attribute, edit.Unit),
                edit.Value,
                false));
        }

        return operations;
    }

    private string BuildPendingSummary()
    {
        var parts = new List<string>();
        if (_pendingUnitEdits.Count > 0) parts.Add($"单位属性 {_pendingUnitEdits.Count:N0} 项");
        if (_pendingAllResources) parts.Add($"全部矿产 × {FormatMultiplier(_pendingAllResourceMultiplier)}");
        if (_pendingBuildingStorage) parts.Add($"全部仓库 × {FormatMultiplier(_pendingBuildingMultiplier)}");
        if (_pendingPlayerEdits.Count > 0) parts.Add($"玩家属性 {_pendingPlayerEdits.Count:N0} 项");
        if (_pendingAdvancedEdits.Count > 0) parts.Add($"高级功能 {_pendingAdvancedEdits.Count:N0} 项");
        return parts.Count == 0 ? "无" : string.Join("、", parts);
    }

    private void ClearPendingEdits()
    {
        _pendingUnitEdits.Clear();
        _pendingPlayerEdits.Clear();
        _pendingAdvancedEdits.Clear();
        _pendingAllResources = false;
        _pendingAllResourceMultiplier = 1m;
        _pendingBuildingStorage = false;
        _pendingBuildingMultiplier = 1m;
        _loadingBatchInputs = true;
        try
        {
            if (_resourceBatchMultiplier is not null) _resourceBatchMultiplier.Value = 1m;
            if (_buildingBatchMultiplier is not null) _buildingBatchMultiplier.Value = 1m;
        }
        finally
        {
            _loadingBatchInputs = false;
        }
        _unitChangedAttributes.Clear();
        _dirty = false;
    }

    private static List<string> BuildAdvancedArgs((string Kind, string Attribute, string Unit) operation, decimal value, bool preview)
    {
        var rawValue = ToStorageValue(operation.Attribute, value);
        var args = operation.Kind.Equals("unit", StringComparison.OrdinalIgnoreCase)
            ? new List<string> { "--unit", operation.Unit, "--attribute", operation.Attribute, "--value", rawValue.ToString(CultureInfo.InvariantCulture) }
            : new List<string> { "--player-attribute", operation.Attribute, "--player-value", rawValue.ToString(CultureInfo.InvariantCulture) };
        args.Add(preview ? "--dry-run" : "--yes");
        return args;
    }

    private string GetAdvancedLabel(string key)
    {
        return key switch
        {
            "MinFuMoveSpeed" => "所有民夫移速",
            "AllUnitMoveSpeed" => "全部自有兵种移速",
            "CartCapacity" => "每次搬运数量",
            "CarryEfficiency" => "搬运效率（倍）",
            "CollectEfficiency" => "采集效率（倍）",
            "CraftEfficiency" => "制作效率（倍）",
            "PolicyMoveSpeed" => "政策移速",
            "RoadToll" => "道路税",
            _ => key
        };
    }

    private string SelectedPlayerAttribute()
    {
        return PlayerScanner.NormalizeAttribute(_selectedPlayerAttribute);
    }

    private async Task RunReadOnlyAsync(params string[] operationArgs)
    {
        if (!EnsureSlot()) return;
        await RunCliAndLogAsync(operationArgs, false);
    }

    private async Task<bool> RunWriteBatchAsync(IReadOnlyList<IReadOnlyList<string>> operations)
    {
        if (!EnsureSlot() || operations.Count == 0) return false;
        foreach (var operation in operations)
        {
            var result = await RunCliAndLogAsync(operation, true);
            if (result.ExitCode != 0)
            {
                UpdateGlobalStatus();
                return false;
            }
        }

        return true;
    }

    private async Task<CliRunResult> RunCliAndLogAsync(IEnumerable<string> operationArgs, bool write)
    {
        if (_busy) return new CliRunResult(1, "已有操作正在执行，请稍候。");
        _busy = true;
        UpdateGlobalStatus();
        var args = BuildCliArgs(operationArgs.ToArray());
        AppendLog($"开始：{string.Join(" ", args.Select(QuoteForLog))}");
        try
        {
            var result = await RunCliAsync(args);
            AppendLog(result.Output);
            _statusLabel.Text = result.ExitCode == 0
                ? write ? "修改完成" : "读取完成"
                : "操作失败";
            return result;
        }
        catch (Exception ex)
        {
            AppendLog("[界面错误] " + ex.Message);
            _statusLabel.Text = "操作失败";
            return new CliRunResult(1, ex.Message);
        }
        finally
        {
            _busy = false;
            UpdateGlobalStatus();
        }
    }

    private async Task<CliRunResult> RunCliAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath)) throw new InvalidOperationException("无法定位当前程序。");
        var info = new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (Path.GetFileName(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            var entry = Path.Combine(AppContext.BaseDirectory, "FengshaSaveEditor.dll");
            if (File.Exists(entry)) info.ArgumentList.Add(entry);
        }
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new InvalidOperationException("无法启动内部命令。");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // 子进程已退出或系统拒绝终止时，继续等待其输出任务收尾。
            }

            await Task.WhenAll(outputTask, errorTask);
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;
        if (!string.IsNullOrWhiteSpace(error)) output += Environment.NewLine + error;
        return new CliRunResult(process.ExitCode, output.Trim());
    }

    private static T ParseCliJson<T>(CliRunResult result, string operation)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidDataException($"{operation}读取命令失败（退出码 {result.ExitCode}）。");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(result.Output, CliJsonOptions)
                ?? throw new InvalidDataException($"{operation}返回了空数据。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{operation}返回的数据格式异常：{ex.Message}", ex);
        }
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_busy)
        {
            MessageBox.Show("当前仍有存档操作在执行，请等待操作完成后再退出。", "正在处理", MessageBoxButtons.OK, MessageBoxIcon.Information);
            e.Cancel = true;
            return;
        }

        if (HasPendingEdits())
        {
            var answer = MessageBox.Show(
                "当前有尚未写入存档的修改，退出后这些修改会丢失。确定退出吗？",
                "有待保存修改",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        _loadCts?.Cancel();
        _unitInputCts?.Cancel();
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 SaveGames 目录，或直接选择一个存档槽文件夹。不要选择单个 .sav 文件。",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_saveRoot) ? _saveRoot : GetDefaultSaveRoot()
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var chosen = Path.GetFullPath(dialog.SelectedPath);
        if (HasSlotFiles(chosen))
        {
            _saveRoot = Directory.GetParent(chosen)?.FullName ?? chosen;
            RefreshSlots(Path.GetFileName(chosen));
        }
        else
        {
            _saveRoot = chosen;
            _slotPath = null;
            RefreshSlots();
        }
        AppendLog($"已选择目录：{chosen}");
    }

    private void RefreshSlots(string? preferred = null)
    {
        if (_refreshingSlots) return;
        _refreshingSlots = true;
        try
        {
            var slots = EnumerateSlots(_saveRoot);
            _slotPicker.Items.Clear();
            foreach (var slot in slots) _slotPicker.Items.Add(slot);
            var index = slots.FindIndex(slot => string.Equals(slot.Path, preferred, StringComparison.OrdinalIgnoreCase) || string.Equals(slot.Name, preferred, StringComparison.OrdinalIgnoreCase));
            _slotPicker.SelectedIndex = index >= 0 ? index : slots.Count > 0 ? 0 : -1;
            _slotPath = _slotPicker.SelectedItem is HeroSlotChoice choice ? choice.Path : null;
            _resourceReadback = null;
            _buildingReadback = null;
            UpdateBuildingReadbackDisplay();
            UpdateGlobalStatus();
            _ = LoadPageAsync(_tabs.SelectedTab?.Name ?? "units");
        }
        finally
        {
            _refreshingSlots = false;
        }
    }

    private void SlotChanged()
    {
        if (_refreshingSlots) return;
        _slotPath = (_slotPicker.SelectedItem as HeroSlotChoice)?.Path;
        _resourceReadback = null;
        _buildingReadback = null;
        UpdateBuildingReadbackDisplay();
        ClearPendingEdits();
        ResetMultiplierSelection();
        _dirty = false;
        UpdateGlobalStatus();
        _ = LoadPageAsync(_tabs.SelectedTab?.Name ?? "units");
    }

    private void UpdateGlobalStatus()
    {
        if (_folderLabel is null) return;
        SyncBatchInputs();
        var pending = HasPendingEditsCore();
        var running = IsGameRunning();
        _folderLabel.Text = _slotPath is null ? _saveRoot : _slotPath;
        var files = _slotPath is null ? 0 : CountRequiredFiles(_slotPath);
        _fileStatusLabel.Text = $"文件 {files}/4";
        _fileStatusLabel.ForeColor = files == 4 ? Green : files >= 2 ? Warning : TextMuted;
        _gameStatusLabel.Text = running ? "游戏运行中 · 请退出后保存" : "游戏未运行";
        _gameStatusLabel.ForeColor = running ? Red : Green;
        _dirty = pending;
        _saveButton.Enabled = !_busy && !running && _slotPath is not null && pending;
        _statusLabel.Text = _busy ? "正在处理…" : pending ? running ? "游戏运行中，保存已禁用" : "有待保存的修改" : running ? "游戏运行中，请退出后保存" : "就绪";
    }

    private void SetDirtySummary(string text)
    {
        _dirty = HasPendingEdits();
        _statusLabel.Text = _dirty ? "待保存：" + text : "预览：" + text;
        AppendLog("待保存修改：" + text);
    }

    private void MarkDirty()
    {
        if (!_suppressSelection) _dirty = HasPendingEdits();
        UpdateGlobalStatus();
    }

    private static IReadOnlyList<decimal> SupportedMultipliers() =>
        [1m, 2m, 5m, 10m, 20m, 50m, 100m];

    private static string FormatMultiplier(decimal multiplier) =>
        multiplier.ToString("0.##", CultureInfo.InvariantCulture) + "倍";

    private void ResetMultiplierSelection()
    {
        _loadingMultiplier = true;
        try
        {
            _selectedMultiplier = 1m;
            if (_multiplierPicker.Items.Count > 0) _multiplierPicker.SelectedIndex = 0;
        }
        finally
        {
            _loadingMultiplier = false;
        }
    }

    private void MultiplierChanged()
    {
        if (_loadingMultiplier || _multiplierPicker.SelectedItem is not MultiplierChoice choice)
        {
            return;
        }

        _selectedMultiplier = choice.Value;
        ApplyMultiplierToCurrentPage();
    }

    private void UpdateMultiplierToolbarVisibility(string? page)
    {
        var visible = !string.Equals(page, "buildings", StringComparison.OrdinalIgnoreCase);
        _multiplierLabel.Visible = visible;
        _multiplierPicker.Visible = visible;
    }

    private void ApplyMultiplierToCurrentPage()
    {
        if (_busy || _tabs is null) return;

        switch (_tabs.SelectedTab?.Name)
        {
            case "units":
                ApplyUnitMultiplier();
                break;
            case "buildings":
                // 建筑页由“矿产（倍数）”和“仓库（倍数）”两个输入框直接暂存。
                break;
            case "player":
                ApplyPlayerMultiplier();
                break;
            case "advanced":
                ApplyAdvancedMultiplier();
                break;
        }
    }

    private void StageResourceBatchMultiplier(decimal multiplier)
    {
        if (multiplier == 1m)
        {
            _pendingAllResources = false;
            _pendingAllResourceMultiplier = 1m;
        }
        else
        {
            _pendingAllResources = true;
            _pendingAllResourceMultiplier = multiplier;
        }

        MarkDirty();
    }

    private void StageBuildingBatchMultiplier(decimal multiplier)
    {
        _pendingBuildingStorage = multiplier != 1m;
        _pendingBuildingMultiplier = multiplier;
        MarkDirty();
    }

    private void SyncBatchInputs()
    {
        if (_loadingBatchInputs) return;

        if (_resourceBatchMultiplier is not null)
        {
            var multiplier = _resourceBatchMultiplier.Value;
            _pendingAllResources = multiplier != 1m;
            _pendingAllResourceMultiplier = multiplier;
        }

        if (_buildingBatchMultiplier is not null)
        {
            var multiplier = _buildingBatchMultiplier.Value;
            _pendingBuildingStorage = multiplier != 1m;
            _pendingBuildingMultiplier = multiplier;
        }
    }

    private void ApplyUnitMultiplier()
    {
        if (_unitAttributeInputs.Count == 0 || _unitInitialValues.Count == 0) return;

        _loadingUnitInputs = true;
        try
        {
            foreach (var pair in _unitAttributeInputs)
            {
                if (_unitInitialValues.TryGetValue(pair.Key, out var initial))
                {
                    SetInputFromStorage(pair.Value, pair.Key, ScaleRawValue(pair.Key, initial, _selectedMultiplier));
                }
            }
        }
        finally
        {
            _loadingUnitInputs = false;
        }

        foreach (var pair in _unitAttributeInputs)
        {
            StageUnitInput(pair.Key, pair.Value);
        }
    }

    private void ApplyPlayerMultiplier()
    {
        if (_playerValue is null) return;

        var attribute = SelectedPlayerAttribute();
        var initial = _playerStates.TryGetValue(attribute, out var state)
            ? ParseFirstInteger(state)
            : null;
        var baseValue = initial ?? ToStorageValue(attribute, _playerValue.Value);
        _suppressSelection = true;
        try
        {
            SetInputFromStorage(_playerValue, attribute, ScaleRawValue(attribute, baseValue, _selectedMultiplier));
        }
        finally
        {
            _suppressSelection = false;
        }

        StagePlayerInput();
    }

    private void ApplyAdvancedMultiplier()
    {
        if (_advancedInputs.Count == 0 || _advancedInitialValues.Count == 0) return;

        _loadingAdvancedInputs = true;
        try
        {
            foreach (var pair in _advancedInputs)
            {
                if (_advancedInitialValues.TryGetValue(pair.Key, out var initial)
                    && _advancedOperations.TryGetValue(pair.Key, out var operation))
                {
                    SetInputFromStorage(pair.Value, operation.Attribute, ScaleRawValue(operation.Attribute, initial, _selectedMultiplier));
                }
            }
        }
        finally
        {
            _loadingAdvancedInputs = false;
        }

        foreach (var pair in _advancedInputs)
        {
            StageAdvancedInput(pair.Key, pair.Value);
        }
    }

    private static int ScaleRawValue(string attribute, int rawValue, decimal multiplier)
    {
        if (IsToggleAttribute(attribute))
        {
            return rawValue;
        }

        var scaled = decimal.Round(rawValue * multiplier, 0, MidpointRounding.AwayFromZero);
        return checked((int)Math.Clamp(scaled, -1_000_000_000m, 1_000_000_000m));
    }

    private void StageUnitInput(string attribute, NumericUpDown input)
    {
        var single = _unitSingleRadio?.Checked == true;
        var instance = (int)(_unitInstance?.Value ?? 0);
        var value = ToStorageValue(attribute, input.Value);
        var key = GetUnitEditKey(_selectedUnitType, attribute, single, instance);
        if (_unitInitialValues.TryGetValue(attribute, out var initial) && value == initial)
        {
            _pendingUnitEdits.Remove(key);
            _unitChangedAttributes.Remove(attribute);
        }
        else
        {
            _pendingUnitEdits[key] = new PendingUnitEdit(_selectedUnitType, attribute, value, single, instance);
            _unitChangedAttributes.Add(attribute);
            RemoveConflictingAdvancedUnitEdits(_selectedUnitType, attribute);
        }

        MarkDirty();
    }

    private void StagePlayerInput()
    {
        var attribute = SelectedPlayerAttribute();
        var value = ToStorageValue(attribute, _playerValue?.Value ?? 0);
        var initial = _playerStates.TryGetValue(attribute, out var state) ? ParseFirstInteger(state) : null;
        if (initial.HasValue && initial.Value == value)
        {
            _pendingPlayerEdits.Remove(attribute);
        }
        else
        {
            _pendingPlayerEdits[attribute] = new PendingPlayerEdit(attribute, value);
            RemoveConflictingAdvancedPlayerEdit(attribute);
        }

        MarkDirty();
    }

    private void StageAdvancedInput(string key, NumericUpDown input)
    {
        if (!_advancedOperations.TryGetValue(key, out var operation)) return;
        var value = ToStorageValue(operation.Attribute, input.Value);
        if (_advancedInitialValues.TryGetValue(key, out var initial) && value == initial)
        {
            _pendingAdvancedEdits.Remove(key);
        }
        else
        {
            _pendingAdvancedEdits[key] = new PendingAdvancedEdit(key, operation.Kind, operation.Attribute, operation.Unit, value);
            if (operation.Kind.Equals("unit", StringComparison.OrdinalIgnoreCase))
            {
                RemoveConflictingUnitEdits(operation.Unit, operation.Attribute);
            }
            else
            {
                _pendingPlayerEdits.Remove(operation.Attribute);
            }
        }

        MarkDirty();
    }

    private bool TryGetPendingUnitEdit(string unitType, string attribute, out PendingUnitEdit edit)
    {
        var single = _unitSingleRadio?.Checked == true;
        var instance = (int)(_unitInstance?.Value ?? 0);
        return _pendingUnitEdits.TryGetValue(GetUnitEditKey(unitType, attribute, single, instance), out edit!);
    }

    private static string GetUnitEditKey(string unitType, string attribute, bool single, int instance)
    {
        return $"{unitType}\u001F{attribute}\u001F{(single ? instance.ToString(CultureInfo.InvariantCulture) : "all")}";
    }

    private void RemoveConflictingAdvancedUnitEdits(string unitType, string attribute)
    {
        foreach (var key in _pendingAdvancedEdits.Values
                     .Where(edit => edit.Kind.Equals("unit", StringComparison.OrdinalIgnoreCase)
                         && edit.Unit.Equals(unitType, StringComparison.OrdinalIgnoreCase)
                         && edit.Attribute.Equals(attribute, StringComparison.OrdinalIgnoreCase))
                     .Select(edit => edit.Key)
                     .ToList())
        {
            _pendingAdvancedEdits.Remove(key);
        }
    }

    private void RemoveConflictingUnitEdits(string unitType, string attribute)
    {
        foreach (var key in _pendingUnitEdits.Values
                     .Where(edit => edit.UnitType.Equals(unitType, StringComparison.OrdinalIgnoreCase)
                         && edit.Attribute.Equals(attribute, StringComparison.OrdinalIgnoreCase))
                     .Select(edit => GetUnitEditKey(edit.UnitType, edit.Attribute, edit.SingleInstance, edit.InstanceIndex))
                     .ToList())
        {
            _pendingUnitEdits.Remove(key);
        }
    }

    private void RemoveConflictingAdvancedPlayerEdit(string attribute)
    {
        foreach (var key in _pendingAdvancedEdits.Values
                     .Where(edit => !edit.Kind.Equals("unit", StringComparison.OrdinalIgnoreCase)
                         && edit.Attribute.Equals(attribute, StringComparison.OrdinalIgnoreCase))
                     .Select(edit => edit.Key)
                     .ToList())
        {
            _pendingAdvancedEdits.Remove(key);
        }
    }

    private bool HasPendingEdits()
    {
        SyncBatchInputs();
        return HasPendingEditsCore();
    }

    private bool HasPendingEditsCore()
    {
        return _pendingUnitEdits.Count > 0
            || _pendingPlayerEdits.Count > 0
            || _pendingAdvancedEdits.Count > 0
            || _pendingAllResources
            || _pendingBuildingStorage;
    }

    private void UpdateUnitSummary()
    {
        if (_unitCurrentLabel is null) return;
        var range = _unitSingleRadio?.Checked == true
            ? $"第 {(int)(_unitInstance?.Value ?? 0) + 1} 个匹配单位区域（该区域内全部字段）"
            : "全部匹配单位区域";
        _unitCurrentLabel.Text = $"对象：{UnitScanner.GetUnitLabel(_selectedUnitType)}    |    范围：{range}";
    }

    private void PopulateUnitGrid(IReadOnlyDictionary<string, int> counts)
    {
        if (_unitGrid is null) return;
        _unitGrid.Rows.Clear();
        var ownTotal = counts
            .Where(pair => UnitScanner.PlayerOwnedUnitTypes.Contains(pair.Key))
            .Sum(pair => pair.Value);
        var ownRow = _unitGrid.Rows[_unitGrid.Rows.Add("全部自有兵种", ownTotal > 0 ? ownTotal.ToString("N0") : "—")];
        ownRow.Tag = UnitScanner.PlayerOwnedUnitSelectionKey;
        foreach (var type in UnitScanner.KnownUnitTypes)
        {
            var current = counts.TryGetValue(type, out var count) ? count.ToString("N0") : "—";
            var row = _unitGrid.Rows[_unitGrid.Rows.Add(UnitScanner.GetUnitLabel(type), current)];
            row.Tag = type;
        }
        SelectUnitRow(_selectedUnitType);
        FilterGrid(_unitGrid, _unitSearch?.Text ?? string.Empty);
    }

    private void PopulatePlayerGrid(IReadOnlyCollection<string> names, IReadOnlyDictionary<string, string> states)
    {
        if (_playerGrid is null) return;
        _playerGrid.Rows.Clear();
        var source = names.Count > 0 ? names : new[] { "AT_CartCapacity", "AT_CarryEfficiency", "AT_CollectEfficiency", "AT_CraftEfficiency", "AT_PolicyMoveSpeed", "AT_RoadToll" };
        foreach (var name in source.OrderBy(GetEditorLabel, StringComparer.Ordinal))
        {
            var row = _playerGrid.Rows[_playerGrid.Rows.Add(GetEditorLabel(name), states.TryGetValue(name, out var state) ? FormatFriendlyDistribution(name, state) : "尚未读取")];
            row.Tag = name;
        }
        FilterGrid(_playerGrid, _playerSearch?.Text ?? string.Empty);
    }

    private void SelectUnitRow(string unitType)
    {
        if (_unitGrid is null) return;
        foreach (DataGridViewRow row in _unitGrid.Rows)
        {
            if (row.Tag is string tag && tag.Equals(unitType, StringComparison.OrdinalIgnoreCase))
            {
                row.Selected = true;
                if (row.Cells.Count > 0) _unitGrid.CurrentCell = row.Cells[0];
                return;
            }
        }
    }

    private void SelectPlayerRow(string attribute)
    {
        if (_playerGrid is null) return;
        foreach (DataGridViewRow row in _playerGrid.Rows)
        {
            if (row.Tag is string tag && tag.Equals(attribute, StringComparison.OrdinalIgnoreCase))
            {
                row.Selected = true;
                if (row.Cells.Count > 0) _playerGrid.CurrentCell = row.Cells[0];
                SelectPlayerAttribute(attribute);
                return;
            }
        }
        SelectPlayerAttribute(attribute);
    }

    private void SelectPlayerAttribute(string attribute)
    {
        _selectedPlayerAttribute = PlayerScanner.NormalizeAttribute(attribute);
        if (_playerSelectedLabel is not null) _playerSelectedLabel.Text = GetEditorLabel(_selectedPlayerAttribute);
        if (_playerValue is null) return;
        var rawValue = _pendingPlayerEdits.TryGetValue(_selectedPlayerAttribute, out var pending)
            ? pending.Value
            : _playerStates.TryGetValue(_selectedPlayerAttribute, out var state)
                ? ParseFirstInteger(state)
                : null;
        _suppressSelection = true;
        SetInputFromStorage(_playerValue, _selectedPlayerAttribute, rawValue ?? DefaultPlayerAttributeValue(_selectedPlayerAttribute));
        _suppressSelection = false;
        if (_selectedMultiplier != 1m) ApplyPlayerMultiplier();
    }

    private static void FilterGrid(DataGridView? grid, string? query)
    {
        if (grid is null) return;
        var text = query?.Trim() ?? string.Empty;
        foreach (DataGridViewRow row in grid.Rows)
        {
            row.Visible = string.IsNullOrWhiteSpace(text) || row.Cells.Cast<DataGridViewCell>().Any(cell => cell.Value?.ToString()?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);
        }
    }

    private IReadOnlyList<string> BuildCliArgs(params string[] operationArgs)
    {
        var args = new List<string> { "--save-root", _saveRoot };
        if (!string.IsNullOrWhiteSpace(_slotPath)) { args.Add("--slot"); args.Add(Path.GetFileName(_slotPath)); }
        args.AddRange(operationArgs);
        return args;
    }

    private static List<HeroSlotChoice> EnumerateSlots(string root)
    {
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateDirectories(root)
            .Select(path => new HeroSlotChoice(Path.GetFileName(path), path, CountRequiredFiles(path), GetLastActivity(path)))
            .Where(slot => File.Exists(Path.Combine(slot.Path, "Mass.sav")) && File.Exists(Path.Combine(slot.Path, "Level.sav")))
            .OrderByDescending(slot => slot.LastActivity)
            .ToList();
    }

    private static int CountRequiredFiles(string path)
    {
        return new[] { "Mass.sav", "Level.sav", "Player.sav", "Slot.sav" }.Count(file => File.Exists(Path.Combine(path, file)));
    }

    private static bool HasSlotFiles(string path) => File.Exists(Path.Combine(path, "Mass.sav")) && File.Exists(Path.Combine(path, "Level.sav"));

    private static DateTime GetLastActivity(string path)
    {
        try { return Directory.EnumerateFiles(path, "*.sav", SearchOption.TopDirectoryOnly).Select(File.GetLastWriteTime).DefaultIfEmpty(DateTime.MinValue).Max(); }
        catch { return DateTime.MinValue; }
    }

    private static string GetDefaultSaveRoot()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
        {
            return Path.Combine(local, "MOProject", "Saved", "SaveGames");
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, "AppData", "Local", "MOProject", "Saved", "SaveGames");
    }

    private static bool IsGameRunning()
    {
        var processes = Process.GetProcessesByName("MOProject-Win64-Shipping");
        var running = processes.Length > 0;
        foreach (var process in processes) process.Dispose();
        return running;
    }

    private bool EnsureSlot()
    {
        if (!string.IsNullOrWhiteSpace(_slotPath)) return true;
        MessageBox.Show("请先选择存档文件夹和槽位。一个存档由多个 .sav 文件组成。", "尚未选择存档", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private string LiveWriteWarning()
    {
        return IsGameRunning()
            ? "\r\n当前游戏正在运行；若游戏同时保存，可能覆盖本次修改。"
            : string.Empty;
    }

    private void PromptForFolderIfDefaultEmpty()
    {
        if (!string.Equals(
                Path.GetFullPath(_saveRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(_defaultSaveRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase)) return;
        if (EnumerateSlots(_defaultSaveRoot).Count > 0 || IsDisposed) return;
        BrowseFolder();
    }

    private static string QuoteForLog(string value) => value.Contains(' ') || value.Contains('\\') || value.Contains('：') ? $"\"{value}\"" : value;

    private void AppendLog(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendLog(text)); return; }
        if (string.IsNullOrWhiteSpace(text) || _statusLabel is null) return;
        _statusLabel.Text = GetStatusSummary(text);
    }

    private static string GetStatusSummary(string text)
    {
        var line = text
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        if (string.IsNullOrWhiteSpace(line)) return "操作完成";
        return line.Length <= 150 ? line : line[..147] + "…";
    }

    private static SplitContainer NewEditorSplit()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.None,
            SplitterWidth = 6,
            BackColor = Background,
            BorderStyle = BorderStyle.None
        };
        split.SizeChanged += (_, _) => ConfigureEditorSplit(split);
        split.HandleCreated += (_, _) =>
        {
            try
            {
                split.BeginInvoke((Action)(() => ConfigureEditorSplit(split)));
            }
            catch (ObjectDisposedException)
            {
                // 窗口关闭过程中不再调整分栏。
            }
            catch (InvalidOperationException)
            {
                // 控件尚未进入消息循环时，SizeChanged 会在布局完成后再次触发。
            }
        };
        return split;
    }

    private static void ConfigureEditorSplit(SplitContainer split)
    {
        if (split.IsDisposed || split.Width <= split.SplitterWidth) return;

        var available = split.Width - split.SplitterWidth;
        var panel1Min = Math.Min(360, available / 2);
        var panel2Min = Math.Min(560, Math.Max(0, available - panel1Min));
        var maxDistance = Math.Max(panel1Min, split.Width - panel2Min);
        var target = Math.Clamp(460, panel1Min, maxDistance);

        try
        {
            // 先清空旧约束，再以当前真实宽度设置目标值和最小宽度，避免启动阶段校验顺序触发异常。
            split.Panel1MinSize = 0;
            split.Panel2MinSize = 0;
            split.SplitterDistance = Math.Clamp(target, 0, split.Width);
            split.Panel1MinSize = panel1Min;
            split.Panel2MinSize = panel2Min;
        }
        catch (ArgumentException)
        {
            // 极窄窗口或系统布局竞争时保留可用的无约束分栏，不能让编辑器启动崩溃。
            try
            {
                split.Panel1MinSize = 0;
                split.Panel2MinSize = 0;
                split.SplitterDistance = Math.Clamp(split.Width / 2, 0, split.Width);
            }
            catch (ArgumentException)
            {
                // 控件正在销毁时忽略最后一次布局调整。
            }
        }
    }

    private static void ConfigureHorizontalSplit(SplitContainer split)
    {
        if (split.IsDisposed || split.Height <= split.SplitterWidth) return;

        var available = split.Height - split.SplitterWidth;
        var panel1Min = Math.Min(260, available / 2);
        var panel2Min = Math.Min(260, Math.Max(0, available - panel1Min));
        var maxDistance = Math.Max(panel1Min, split.Height - panel2Min);
        var target = Math.Clamp(split.Height / 2, panel1Min, maxDistance);

        try
        {
            split.Panel1MinSize = 0;
            split.Panel2MinSize = 0;
            split.SplitterDistance = Math.Clamp(target, 0, split.Height);
            split.Panel1MinSize = panel1Min;
            split.Panel2MinSize = panel2Min;
        }
        catch (ArgumentException)
        {
            try
            {
                split.Panel1MinSize = 0;
                split.Panel2MinSize = 0;
                split.SplitterDistance = Math.Clamp(split.Height / 2, 0, split.Height);
            }
            catch (ArgumentException)
            {
                // 控件正在创建或销毁时忽略最后一次布局调整。
            }
        }
    }

    private static Panel EditorPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(18), AutoScroll = true };
        panel.Paint += DrawBorder;
        return panel;
    }

    private static Panel ActionBar()
    {
        return new Panel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(0, 8, 0, 0), BackColor = Color.Transparent };
    }

    private static Label ValueLabel(string text) => new()
    {
        Text = text,
        Width = 440,
        Height = 34,
        BackColor = SurfaceRaised,
        ForeColor = TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 8, 0),
        AutoEllipsis = true
    };

    private TableLayoutPanel CreateNumericPropertyRows(
        IReadOnlyDictionary<string, string> labels,
        IDictionary<string, NumericUpDown> inputs,
        Func<string, int> defaultValue)
    {
        inputs.Clear();
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(0, 4, 0, 0),
            BackColor = Color.Transparent
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        foreach (var pair in labels.OrderBy(pair => pair.Value, StringComparer.Ordinal))
        {
            var attribute = pair.Key;
            var label = new Label
            {
                Text = GetEditorLabel(attribute),
                Dock = DockStyle.Fill,
                ForeColor = TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 0, 8, 0)
            };
        var isMultiplier = IsMultiplierAttribute(attribute);
        var input = NumberBox(
            FromStorageValue(attribute, defaultValue(attribute)),
            decimalPlaces: isMultiplier ? 2 : 0,
            increment: isMultiplier ? 0.1m : 1m);
            input.Width = 240;
            input.Margin = new Padding(0, 4, 0, 4);
            input.ValueChanged += (_, _) =>
            {
                if (_loadingUnitInputs) return;
                StageUnitInput(attribute, input);
            };
            inputs[attribute] = input;
            var row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            table.Controls.Add(label, 0, row);
            table.Controls.Add(input, 1, row);
        }
        return table;
    }

    private void AddAdvancedInput(
        TableLayoutPanel table,
        string label,
        string key,
        string kind,
        string attribute,
        string unit,
        decimal defaultValue)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 46,
            Width = 650,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 2)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        var title = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            ForeColor = TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var isMultiplier = IsMultiplierAttribute(attribute);
        var input = NumberBox(
            defaultValue,
            decimalPlaces: isMultiplier ? 2 : 0,
            increment: isMultiplier ? 0.1m : 1m);
        input.Width = 240;
        input.Margin = new Padding(0, 4, 0, 4);
        input.ValueChanged += (_, _) =>
        {
            if (!_loadingAdvancedInputs) StageAdvancedInput(key, input);
        };
        var preview = SmallButton("预览", async (_, _) => await PreviewAdvancedEntryAsync(key));
        row.Controls.Add(title, 0, 0);
        row.Controls.Add(input, 1, 0);
        row.Controls.Add(preview, 2, 0);
        table.Controls.Add(row);
        _advancedInputs[key] = input;
        _advancedOperations[key] = (kind, attribute, unit);
    }

    private static int DefaultUnitAttributeValue(string attribute)
    {
        return attribute switch
        {
            "AT_MoveSpeed" => 200,
            "AT_MaxHP" or "AT_HP" => 100,
            "AT_Atk" => 5,
            "AT_Morale" => 50,
            _ when IsMultiplierAttribute(attribute) => 10000,
            _ => 0
        };
    }

    private static int DefaultAdvancedAttributeValue(string attribute)
    {
        return attribute switch
        {
            "AT_MoveSpeed" => 200,
            "AT_CartCapacity" => 10,
            _ when IsMultiplierAttribute(attribute) => 10000,
            _ => 0
        };
    }

    private static int DefaultPlayerAttributeValue(string attribute)
    {
        return attribute switch
        {
            "AT_CartCapacity" => 10,
            _ when IsMultiplierAttribute(attribute) => 10000,
            _ => 0
        };
    }

    private static bool IsMultiplierAttribute(string attribute)
    {
        return attribute switch
        {
            "AT_ArmyCreateNumRate" or
            "AT_CarryEfficiency" or
            "AT_CollectEfficiency" or
            "AT_CraftEfficiency" or
            "AT_DamageToEnemyBuildingRate" or
            "AT_DefaultRangedSkillLengthRate" or
            "AT_GlobalFertilityCostAttenuationCoef" or
            "AT_ModelScale" or
            "AT_ResIncreaseRate" or
            "AT_SkillDamageK1" or
            "AT_SkillLengthModulus" or
            "AT_StartNumRate" or
            "AT_WallBuildEfficiency" => true,
            _ => false
        };
    }

    private static bool IsToggleAttribute(string attribute)
    {
        return attribute is "AT_FakePhysicsAtkSwitch"
            or "AT_FakePhysicsBuffSwitch"
            or "AT_GloablExtraResourceSwitch"
            or "AT_GlobalFakePhysicsSwitch";
    }

    private static string GetEditorLabel(string attribute)
    {
        var label = UnitScanner.SupportedAttributes.ContainsKey(attribute)
            ? UnitScanner.GetAttributeLabel(attribute)
            : PlayerScanner.GetLabel(attribute);
        if (attribute == "AT_ModelScale") return "模型大小（倍）";
        if (attribute == "AT_SkillDamageK1") return "技能伤害（倍）";
        if (attribute == "AT_SkillLengthModulus") return "技能距离（倍）";
        if (IsMultiplierAttribute(attribute) && !label.Contains("倍率", StringComparison.Ordinal) && !label.Contains("倍", StringComparison.Ordinal))
        {
            return label + "（倍）";
        }

        return label;
    }

    private static decimal FromStorageValue(string attribute, int rawValue)
    {
        return IsMultiplierAttribute(attribute) ? rawValue / 10_000m : rawValue;
    }

    private static int ToStorageValue(string attribute, decimal displayValue)
    {
        var rawValue = IsMultiplierAttribute(attribute)
            ? decimal.Round(displayValue * 10_000m, 0, MidpointRounding.AwayFromZero)
            : decimal.Round(displayValue, 0, MidpointRounding.AwayFromZero);
        return checked((int)rawValue);
    }

    private static void SetInputFromStorage(NumericUpDown input, string attribute, int rawValue)
    {
        var displayValue = FromStorageValue(attribute, rawValue);
        var isMultiplier = IsMultiplierAttribute(attribute);
        input.DecimalPlaces = isMultiplier ? 2 : 0;
        input.Increment = isMultiplier ? 0.1m : 1m;
        input.ThousandsSeparator = !isMultiplier;
        input.Value = Math.Clamp(displayValue, input.Minimum, input.Maximum);
    }

    private static string FormatDisplayValue(string attribute, int rawValue)
    {
        if (IsToggleAttribute(attribute)) return rawValue == 0 ? "关闭" : "开启";
        if (IsMultiplierAttribute(attribute)) return $"{FromStorageValue(attribute, rawValue):0.##}倍";
        return rawValue.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string FormatFriendlyDistribution(string attribute, string? current)
    {
        if (string.IsNullOrWhiteSpace(current)) return "尚未读取";
        var parts = new List<string>();
        var total = 0;
        foreach (var segment in current.Split(['，', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var rawValue = ParseFirstInteger(segment);
            if (!rawValue.HasValue) continue;
            var marker = segment.IndexOf('×');
            var count = marker >= 0 ? ParseFirstInteger(segment[(marker + 1)..]) ?? 1 : 1;
            total += Math.Max(count, 1);
            parts.Add($"{FormatDisplayValue(attribute, rawValue.Value)}（{count:N0}项）");
        }

        if (parts.Count == 0) return "尚未读取";
        if (parts.Count <= 3) return string.Join("、", parts);
        return $"{string.Join("、", parts.Take(3))} 等（共{total:N0}项）";
    }

    private static int? ParseFirstInteger(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var builder = new StringBuilder();
        var started = false;
        foreach (var character in text)
        {
            if (!started && character is '-')
            {
                builder.Append(character);
                started = true;
                continue;
            }

            if (char.IsDigit(character))
            {
                builder.Append(character);
                started = true;
                continue;
            }

            if (started) break;
        }

        return int.TryParse(builder.ToString(), out var value) ? value : null;
    }

    private static Panel FormLine(string label, Control control, string hint)
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(0, 2, 0, 2), BackColor = Color.Transparent };
        var title = new Label { Text = label, Width = 88, Height = 34, ForeColor = TextPrimary, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(0, 4) };
        control.Location = new Point(94, 4);
        control.Height = 34;
        panel.Controls.Add(title);
        panel.Controls.Add(control);
        return panel;
    }

    private static Label PageHeading(string title, string subtitle)
    {
        var label = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Text = title,
            ForeColor = TextPrimary,
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
            Padding = new Padding(0, 0, 0, 4)
        };
        return label;
    }

    private static Label TopLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        ForeColor = TextSecondary,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Label SecondaryLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        ForeColor = TextSecondary,
        Font = new Font("Microsoft YaHei UI", 9F),
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Label InlineSecondaryLabel(string text, int height = 34)
    {
        var label = SecondaryLabel(text);
        label.Dock = DockStyle.Top;
        label.Height = height;
        return label;
    }

    private static TextBox SearchBox(string placeholder)
    {
        return new TextBox
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = SurfaceRaised,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = placeholder
        };
    }

    private static NumericUpDown NumberBox(int value, int min = -1_000_000_000, int max = 1_000_000_000)
    {
        return NumberBox((decimal)value, min, max);
    }

    private static NumericUpDown NumberBox(decimal value, decimal min = -1_000_000_000m, decimal max = 1_000_000_000m, int decimalPlaces = 0, decimal increment = 1m)
    {
        return new NumericUpDown
        {
            Width = 240,
            Minimum = min,
            Maximum = max,
            DecimalPlaces = decimalPlaces,
            Increment = increment,
            Value = Math.Clamp(value, min, max),
            ThousandsSeparator = decimalPlaces == 0,
            BackColor = SurfaceRaised,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 10F)
        };
    }

    private static NumericUpDown BatchMultiplierBox()
    {
        var input = NumberBox(1m, 1m, 1_000m, decimalPlaces: 2, increment: 0.5m);
        input.Width = 180;
        input.Height = 34;
        input.ThousandsSeparator = false;
        input.TextAlign = HorizontalAlignment.Left;
        input.Margin = new Padding(0, 6, 0, 6);
        return input;
    }

    private static Label BatchMultiplierLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        ForeColor = TextPrimary,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(0, 0, 12, 0)
    };

    private static DataGridView CreateGrid(params (string Header, int Weight)[] columns)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Top,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            MultiSelect = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = SurfaceRaised,
            GridColor = Border,
            BorderStyle = BorderStyle.FixedSingle,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = 34,
            RowTemplate = { Height = 32 },
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = SurfaceRaised,
                ForeColor = TextPrimary,
                SelectionBackColor = Color.FromArgb(220, 232, 244),
                SelectionForeColor = TextPrimary,
                Font = new Font("Microsoft YaHei UI", 9F),
                Padding = new Padding(7, 0, 7, 0)
            },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Surface,
                ForeColor = TextSecondary,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                Padding = new Padding(7, 0, 7, 0)
            }
        };
        foreach (var column in columns)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = column.Header,
                Name = column.Header,
                FillWeight = column.Weight,
                SortMode = DataGridViewColumnSortMode.Automatic
            });
        }
        return grid;
    }

    private static Button SmallButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 34,
            MinimumSize = new Size(110, 34),
            BackColor = SurfaceRaised,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Padding = new Padding(12, 0, 12, 0),
            Margin = new Padding(0, 0, 10, 0),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 232, 232);
        button.Click += handler;
        return button;
    }

    private static Button PrimaryButton(string text, EventHandler handler)
    {
        var button = SmallButton(text, handler);
        button.BackColor = Gold;
        button.FlatAppearance.BorderColor = Gold;
        button.FlatAppearance.MouseOverBackColor = GoldLight;
        return button;
    }

    private static ToolStripButton ToolButton(string text, EventHandler handler)
    {
        var button = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ForeColor = TextPrimary,
            AutoSize = true,
            Padding = new Padding(9, 4, 9, 4),
            Margin = new Padding(0, 0, 3, 0)
        };
        button.Click += handler;
        return button;
    }

    private static void DrawBorder(object? sender, PaintEventArgs e)
    {
        if (sender is not Control control) return;
        using var pen = new Pen(Border);
        e.Graphics.DrawRectangle(pen, 0, 0, control.Width - 1, control.Height - 1);
    }

    private static void DrawBottomBorder(object? sender, PaintEventArgs e)
    {
        if (sender is not Control control) return;
        using var pen = new Pen(Border);
        e.Graphics.DrawLine(pen, 0, control.Height - 1, control.Width, control.Height - 1);
    }

    private void DrawTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs || e.Index < 0) return;
        var selected = e.Index == tabs.SelectedIndex;
        var rect = e.Bounds;
        using var brush = new SolidBrush(selected ? Color.FromArgb(232, 232, 232) : Background);
        e.Graphics.FillRectangle(brush, rect);
        using var pen = new Pen(selected ? TextPrimary : Border, selected ? 2 : 1);
        e.Graphics.DrawLine(pen, rect.Left + 2, rect.Bottom - 1, rect.Right - 2, rect.Bottom - 1);
        using var textBrush = new SolidBrush(selected ? TextPrimary : TextSecondary);
        using var font = new Font("Microsoft YaHei UI", 9F, selected ? FontStyle.Bold : FontStyle.Regular);
        var text = tabs.TabPages[e.Index].Text;
        var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        e.Graphics.DrawString(text, font, textBrush, rect, format);
    }

    private sealed class LightToolStripRenderer : ToolStripProfessionalRenderer
    {
        public LightToolStripRenderer() : base(new LightColorTable()) { }
    }

    private sealed class LightStatusRenderer : ToolStripProfessionalRenderer
    {
        public LightStatusRenderer() : base(new LightColorTable()) { }
    }

    private sealed class LightColorTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => Surface;
        public override Color ToolStripGradientMiddle => Surface;
        public override Color ToolStripGradientEnd => Surface;
        public override Color MenuItemSelected => Color.FromArgb(232, 232, 232);
        public override Color MenuBorder => Border;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
        public override Color StatusStripGradientBegin => Surface;
        public override Color StatusStripGradientEnd => Surface;
        public override Color ButtonSelectedGradientBegin => Color.FromArgb(232, 232, 232);
        public override Color ButtonSelectedGradientMiddle => Color.FromArgb(232, 232, 232);
        public override Color ButtonSelectedGradientEnd => Color.FromArgb(232, 232, 232);
    }
}
