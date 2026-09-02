using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace FengshaSaveEditor;

internal sealed record HeroEditorResourceRow(
    string Label,
    string Category,
    int? ConfigId,
    string SizeLabel,
    int NodeCount,
    int Capacity,
    int? CurrentAmount);

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

internal sealed record PendingResourceEdit(
    string Category,
    int? ConfigId,
    decimal Multiplier);

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

    private DataGridView? _resourceGrid;
    private TextBox? _resourceSearch;
    private string _selectedResourceCategory = "IronOre";
    private int? _selectedResourceConfigId;
    private string _selectedResourceSizeLabel = "未分档";
    private Label? _resourceSelectedLabel;
    private Label? _resourceCurrentStateLabel;
    private CheckBox? _resourceAll;
    private readonly Dictionary<string, PendingResourceEdit> _pendingResourceEdits = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _resourceInitialCapacities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int?> _resourceInitialAmounts = new(StringComparer.OrdinalIgnoreCase);
    private bool _pendingAllResources;
    private decimal _pendingAllResourceMultiplier = 1m;
    private bool _loadingResourceInputs;
    private decimal _selectedMultiplier = 1m;
    private bool _loadingMultiplier;

    private DataGridView? _buildingGrid;
    private TextBox? _buildingSearch;
    private string? _selectedBuildingKey;
    private Label? _buildingSelectedLabel;
    private Label? _buildingCurrentStateLabel;
    private CheckBox? _buildingAll;
    private BuildingStorageListResponse? _buildingScan;
    private bool _pendingBuildingStorage;
    private decimal _pendingBuildingMultiplier = 1m;

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
        toolbar.Items.Add(new ToolStripLabel("修改倍数")
        {
            ForeColor = TextPrimary,
            Margin = new Padding(12, 0, 4, 0)
        });
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
            if (page is not null) await LoadPageAsync(page);
            UpdateGlobalStatus();
        };

        tabs.TabPages.Add(MakeTab("resources", "资源", BuildResourcesPage()));
        tabs.TabPages.Add(MakeTab("buildings", "建筑", BuildBuildingsPage()));
        tabs.SelectedIndex = 0;
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

    private Control BuildResourcesPage()
    {
        var split = NewEditorSplit();
        var left = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(10) };
        left.Paint += DrawBorder;
        _resourceSearch = SearchBox("搜索铁矿、枣子林、狩猎区域");
        _resourceSearch.TextChanged += (_, _) => FilterGrid(_resourceGrid, _resourceSearch.Text);
        _resourceGrid = CreateGrid(("资源", 260), ("规模", 150), ("资源点数量", 150));
        _resourceGrid.Dock = DockStyle.Fill;
        _resourceGrid.Margin = new Padding(0, 8, 0, 0);
        left.Controls.Add(_resourceGrid);
        left.Controls.Add(_resourceSearch);
        split.Panel1.Controls.Add(left);

        var right = EditorPanel();
        right.Controls.Add(PageHeading("资源", ""));
        _resourceSelectedLabel = ValueLabel("未选择资源");
        right.Controls.Add(FormLine("当前资源", _resourceSelectedLabel, ""));
        _resourceCurrentStateLabel = SecondaryLabel("当前数量：尚未读取");
        _resourceCurrentStateLabel.Dock = DockStyle.Top;
        _resourceCurrentStateLabel.Height = 42;
        right.Controls.Add(_resourceCurrentStateLabel);
        right.Controls.Add(InlineSecondaryLabel("在窗口顶部选择修改倍数：1 倍=当前存档上限，2 倍=扩大到两倍，10 倍=扩大到十倍。"));
        var resourceTip = new ToolTip();
        _resourceAll = new CheckBox { Text = "全部资源使用当前倍数", AutoSize = true, ForeColor = TextPrimary, Checked = false };
        resourceTip.SetToolTip(_resourceAll, "勾选后，保存时会把所有已识别资源点的最大上限和当前数量一起按顶部倍数修改。未勾选时只处理左侧选中的资源规模。");
        _resourceAll.CheckedChanged += (_, _) =>
        {
            if (!_loadingResourceInputs)
            {
                _pendingAllResources = _resourceAll.Checked;
                if (_resourceAll.Checked) _pendingAllResourceMultiplier = _selectedMultiplier;
            }
            if (_resourceAll?.Checked == true)
            {
                if (_resourceGrid is not null) _resourceGrid.Enabled = false;
            }
            else
            {
                if (_resourceGrid is not null) _resourceGrid.Enabled = true;
                if (_resourceGrid?.CurrentRow?.Tag is HeroEditorResourceRow item) SelectResourceRow(item);
            }
            if (!_loadingResourceInputs) MarkDirty();
        };
        right.Controls.Add(FormLine("批量设置", _resourceAll, ""));
        right.Controls.Add(InlineSecondaryLabel("未勾选时，只修改左侧选中的资源和规模；勾选后所有资源都使用当前倍数。修改只会暂存，点击顶部“保存修改”才写入。", 44));
        var actions = ActionBar();
        actions.Controls.Add(SmallButton("读取资源", async (_, _) => await RunReadOnlyAsync("--list-resources")));
        actions.Controls.Add(SmallButton("预览", async (_, _) => await PreviewResourceAsync()));
        right.Controls.Add(actions);
        _resourceGrid.SelectionChanged += (_, _) =>
        {
            if (_resourceGrid.CurrentRow?.Tag is HeroEditorResourceRow item)
            {
                SelectResourceRow(item);
            }
        };
        PopulateResourceGrid(Array.Empty<HeroEditorResourceRow>());
        split.Panel2.Controls.Add(right);
        return split;
    }

    private Control BuildBuildingsPage()
    {
        var split = NewEditorSplit();
        var left = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(10) };
        left.Paint += DrawBorder;
        _buildingSearch = SearchBox("搜索辎重库、粮仓、军械库");
        _buildingSearch.TextChanged += (_, _) => FilterGrid(_buildingGrid, _buildingSearch.Text);
        _buildingGrid = CreateGrid(("建筑", 190), ("储存类别", 120), ("当前上限", 240));
        _buildingGrid.Dock = DockStyle.Fill;
        _buildingGrid.Margin = new Padding(0, 8, 0, 0);
        left.Controls.Add(_buildingGrid);
        left.Controls.Add(_buildingSearch);
        split.Panel1.Controls.Add(left);

        var right = EditorPanel();
        right.Controls.Add(PageHeading("建筑", "只修改真正仓库的储存上限"));
        _buildingSelectedLabel = ValueLabel("未选择建筑");
        right.Controls.Add(FormLine("当前建筑", _buildingSelectedLabel, ""));
        _buildingCurrentStateLabel = SecondaryLabel("当前上限：尚未读取");
        _buildingCurrentStateLabel.Dock = DockStyle.Top;
        _buildingCurrentStateLabel.Height = 46;
        right.Controls.Add(_buildingCurrentStateLabel);
        right.Controls.Add(InlineSecondaryLabel("顶部选择 2 倍、5 倍或 10 倍后，会按每座建筑自己的当前上限计算；只会修改已经存在的储存类别。"));

        var buildingTip = new ToolTip();
        _buildingAll = new CheckBox
        {
            Text = "全部仓库使用当前倍数",
            AutoSize = true,
            ForeColor = TextPrimary,
            BackColor = Background,
            Checked = true
        };
        buildingTip.SetToolTip(
            _buildingAll,
            "保存时同时处理所有辎重库、粮仓和军械库；每座建筑按自己的当前上限计算，不会给没有的储存类别补数据。");
        _buildingAll.CheckedChanged += (_, _) =>
        {
            _pendingBuildingStorage = _buildingAll.Checked && _selectedMultiplier != 1m;
            _pendingBuildingMultiplier = _selectedMultiplier;
            MarkDirty();
        };
        right.Controls.Add(FormLine("修改范围", _buildingAll, ""));
        right.Controls.Add(InlineSecondaryLabel("仓库包括：辎重库、粮仓、军械库。保存修改前会先弹出确认；本页修改只暂存，不会立即写入。", 44));

        var actions = ActionBar();
        actions.Controls.Add(SmallButton("读取建筑", async (_, _) => await RunReadOnlyAsync("--list-building-storage")));
        actions.Controls.Add(SmallButton("预览", async (_, _) => await PreviewBuildingAsync()));
        right.Controls.Add(actions);
        _buildingGrid.SelectionChanged += (_, _) =>
        {
            if (_buildingGrid.CurrentRow?.Tag is BuildingStorageListItem item)
            {
                SelectBuildingRow(item);
            }
        };
        PopulateBuildingGrid(Array.Empty<BuildingStorageListItem>());
        split.Panel2.Controls.Add(right);
        return split;
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

    private Control BuildDonationPage()
    {
        var page = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Background,
            AutoScroll = true,
            Padding = new Padding(18)
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Background,
            Padding = new Padding(0)
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 470));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        content.Controls.Add(PageHeading("捐赠", ""), 0, 0);
        content.Controls.Add(SecondaryLabel("如果这个工具对你有帮助，欢迎自愿支持。捐赠不会影响任何功能，感谢你的鼓励。"), 0, 1);

        var codes = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Background,
            Padding = new Padding(0, 8, 0, 8)
        };
        codes.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
        codes.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
        codes.Controls.Add(DonationCodeCard("微信支付", "donation-wechat.png", "二维码待添加"), 0, 0);
        codes.Controls.Add(DonationCodeCard("支付宝", "donation-alipay.jpg", "二维码待添加"), 1, 0);
        content.Controls.Add(codes, 0, 2);

        var contact = new Label
        {
            Dock = DockStyle.Fill,
            Text = "联系 QQ：35611294",
            ForeColor = TextPrimary,
            Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0)
        };
        content.Controls.Add(contact, 0, 3);
        page.Controls.Add(content);
        return page;
    }

    private static Control DonationCodeCard(string title, string imageFileName, string placeholder)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Surface,
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(10)
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Paint += DrawBorder;

        var heading = new Label
        {
            Dock = DockStyle.Fill,
            Text = title,
            ForeColor = TextPrimary,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };
        card.Controls.Add(heading, 0, 0);

        var image = LoadDonationImage(imageFileName);
        if (image is not null)
        {
            var code = new PictureBox
            {
                Dock = DockStyle.Fill,
                Image = image,
                BackColor = Background,
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom,
                Cursor = Cursors.Hand
            };
            var tip = new ToolTip();
            tip.SetToolTip(code, "双击查看大图");
            code.DoubleClick += (_, _) => OpenDonationImage(imageFileName);
            card.Controls.Add(code, 0, 1);
        }
        else
        {
            var code = new Label
            {
                Dock = DockStyle.Fill,
                Text = placeholder,
                ForeColor = TextMuted,
                BackColor = Background,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = ContentAlignment.MiddleCenter
            };
            card.Controls.Add(code, 0, 1);
        }
        return card;
    }

    private static Image? LoadDonationImage(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            using var source = Image.FromStream(stream);
            return new Bitmap(source);
        }
        catch
        {
            return null;
        }
    }

    private static void OpenDonationImage(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        if (!File.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
            // 打开大图失败不影响捐赠页和修改功能。
        }
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
                case "resources":
                    await LoadResourceChoicesAsync(cancellation.Token);
                    break;
                case "buildings":
                    await LoadBuildingChoicesAsync(cancellation.Token);
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

    private async Task LoadResourceChoicesAsync(CancellationToken cancellationToken)
    {
        if (_resourceGrid is null || string.IsNullOrWhiteSpace(_slotPath) || _busy) return;
        try
        {
            var result = await RunCliAsync(BuildCliArgs("--list-resources", "--json"), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var payload = ParseCliJson<ResourceListResponse>(result, "资源目录");
            _resourceInitialCapacities.Clear();
            _resourceInitialAmounts.Clear();
            var rows = payload.Nodes
                .Select(item => new HeroEditorResourceRow(
                    item.Label,
                    item.Category,
                    item.ConfigId,
                    item.SizeLabel,
                    item.NodeCount,
                    item.Capacity,
                    item.CurrentAmount))
                .ToList();
            foreach (var item in rows)
            {
                _resourceInitialCapacities[GetResourceEditKey(item.Category, item.ConfigId)] = item.Capacity;
                _resourceInitialAmounts[GetResourceEditKey(item.Category, item.ConfigId)] = item.CurrentAmount;
            }
            cancellationToken.ThrowIfCancellationRequested();
            PopulateResourceGrid(rows);
            if (_selectedMultiplier != 1m) ApplyMultiplierToCurrentPage();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppendLog("资源目录读取失败：" + ex.Message);
        }
    }

    private async Task LoadBuildingChoicesAsync(CancellationToken cancellationToken)
    {
        if (_buildingGrid is null || string.IsNullOrWhiteSpace(_slotPath) || _busy) return;
        try
        {
            var result = await RunCliAsync(BuildCliArgs("--list-building-storage", "--json"), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _buildingScan = ParseCliJson<BuildingStorageListResponse>(result, "建筑目录");
            PopulateBuildingGrid(_buildingScan.Buildings);
            if (_selectedMultiplier != 1m) ApplyMultiplierToCurrentPage();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppendLog("建筑目录读取失败：" + ex.Message);
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

    private async Task PreviewResourceAsync()
    {
        if (!EnsureSlot() || !EnsureResourceSelection()) return;
        var allResources = _resourceAll?.Checked == true;
        var category = allResources ? "*" : _selectedResourceCategory;
        var configId = allResources ? null : _selectedResourceConfigId;
        var args = BuildResourceArgs(category, configId, _selectedMultiplier, true);
        await RunReadOnlyAsync(args.ToArray());
        var scope = allResources
            ? "全部资源、所有规模"
            : $"{ResourceScanner.GetCategoryLabel(category)} · {_selectedResourceSizeLabel}";
        SetDirtySummary($"{scope} · 上限 × {FormatMultiplier(_selectedMultiplier)}");
    }

    private async Task PreviewBuildingAsync()
    {
        if (!EnsureSlot()) return;
        if (_buildingAll?.Checked != true)
        {
            MessageBox.Show("请勾选“全部仓库使用当前倍数”。", "尚未选择修改范围", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var args = new[]
        {
            "--building-multiplier",
            _selectedMultiplier.ToString("0.##", CultureInfo.InvariantCulture),
            "--dry-run"
        };
        await RunReadOnlyAsync(args);
        SetDirtySummary($"全部仓库 · 上限 × {FormatMultiplier(_selectedMultiplier)}");
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

    private bool EnsureResourceSelection()
    {
        if (_resourceAll?.Checked == true) return true;
        if (_selectedResourceConfigId.HasValue) return true;
        MessageBox.Show("请先读取资源并在左侧选择一种资源规模，或勾选“全部资源使用当前倍数”。", "尚未选择资源规模", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
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
        ClearPendingEdits();
        ResetMultiplierSelection();
        _dirty = false;
        await ReloadCurrentTabAsync();
        _statusLabel.Text = "修改已保存，回读校验通过";
    }

    private List<IReadOnlyList<string>> BuildPendingWriteArgs()
    {
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
        else
        {
            foreach (var edit in _pendingResourceEdits.Values
                         .OrderBy(edit => edit.Category, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(edit => edit.ConfigId))
            {
                operations.Add(BuildResourceArgs(edit.Category, edit.ConfigId, edit.Multiplier, false).Append("--yes").ToArray());
            }
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
        if (_pendingAllResources) parts.Add($"全部资源上限 × {FormatMultiplier(_pendingAllResourceMultiplier)}");
        else if (_pendingResourceEdits.Count > 0) parts.Add($"资源 {_pendingResourceEdits.Count:N0} 项（按上限倍数）");
        if (_pendingBuildingStorage) parts.Add($"全部真正仓库上限 × {FormatMultiplier(_pendingBuildingMultiplier)}");
        if (_pendingPlayerEdits.Count > 0) parts.Add($"玩家属性 {_pendingPlayerEdits.Count:N0} 项");
        if (_pendingAdvancedEdits.Count > 0) parts.Add($"高级功能 {_pendingAdvancedEdits.Count:N0} 项");
        return parts.Count == 0 ? "无" : string.Join("、", parts);
    }

    private void ClearPendingEdits()
    {
        _pendingUnitEdits.Clear();
        _pendingResourceEdits.Clear();
        _pendingPlayerEdits.Clear();
        _pendingAdvancedEdits.Clear();
        _pendingAllResources = false;
        _pendingAllResourceMultiplier = 1m;
        _pendingBuildingStorage = false;
        _pendingBuildingMultiplier = 1m;
        _buildingScan = null;
        _selectedBuildingKey = null;
        _unitChangedAttributes.Clear();
        var previousLoading = _loadingResourceInputs;
        _loadingResourceInputs = true;
        if (_resourceAll is not null) _resourceAll.Checked = false;
        _loadingResourceInputs = previousLoading;
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

        if (_dirty)
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
            UpdateGlobalStatus();
            _ = LoadPageAsync(_tabs.SelectedTab?.Name ?? "resources");
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
        ClearPendingEdits();
        ResetMultiplierSelection();
        _dirty = false;
        UpdateGlobalStatus();
        _ = LoadPageAsync(_tabs.SelectedTab?.Name ?? "resources");
    }

    private void UpdateGlobalStatus()
    {
        if (_folderLabel is null) return;
        var running = IsGameRunning();
        _folderLabel.Text = _slotPath is null ? _saveRoot : _slotPath;
        var files = _slotPath is null ? 0 : CountRequiredFiles(_slotPath);
        _fileStatusLabel.Text = $"文件 {files}/4";
        _fileStatusLabel.ForeColor = files == 4 ? Green : files >= 2 ? Warning : TextMuted;
        _gameStatusLabel.Text = running ? "游戏运行中 · 可写" : "游戏未运行";
        _gameStatusLabel.ForeColor = running ? Red : Green;
        _saveButton.Enabled = !_busy && _slotPath is not null && HasPendingEdits();
        _statusLabel.Text = _busy ? "正在处理…" : HasPendingEdits() ? "有待保存的修改" : running ? "游戏运行中，可直接保存" : "就绪";
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

    private static string FormatResourceValue(int rawValue)
    {
        return rawValue > 0 && rawValue % 256 == 0
            ? (rawValue / 256).ToString("N0", CultureInfo.InvariantCulture)
            : rawValue.ToString("N0", CultureInfo.InvariantCulture);
    }

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

    private void ApplyMultiplierToCurrentPage()
    {
        if (_busy || _tabs is null) return;

        switch (_tabs.SelectedTab?.Name)
        {
            case "units":
                ApplyUnitMultiplier();
                break;
            case "resources":
                if (_resourceAll?.Checked == true)
                {
                    _pendingAllResources = true;
                    _pendingAllResourceMultiplier = _selectedMultiplier;
                    MarkDirty();
                }
                else
                {
                    StageResourceMultiplier();
                }
                break;
            case "buildings":
                if (_buildingAll?.Checked == true)
                {
                    _pendingBuildingStorage = _selectedMultiplier != 1m;
                    _pendingBuildingMultiplier = _selectedMultiplier;
                    MarkDirty();
                }
                break;
            case "player":
                ApplyPlayerMultiplier();
                break;
            case "advanced":
                ApplyAdvancedMultiplier();
                break;
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

    private void StageResourceMultiplier()
    {
        if (_resourceAll?.Checked == true) return;
        var key = GetResourceEditKey(_selectedResourceCategory, _selectedResourceConfigId);
        var hasCapacity = _resourceInitialCapacities.TryGetValue(key, out var capacity);
        var hasCurrent = _resourceInitialAmounts.TryGetValue(key, out var currentAmount);
        var hasOriginal = hasCapacity && hasCurrent;
        var unchanged = _selectedMultiplier == 1m
            && hasOriginal
            && currentAmount.HasValue
            && currentAmount.Value == capacity;
        if (unchanged)
        {
            _pendingResourceEdits.Remove(key);
        }
        else
        {
            _pendingResourceEdits[key] = new PendingResourceEdit(
                _selectedResourceCategory,
                _selectedResourceConfigId,
                _selectedMultiplier);
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

    private static string GetResourceEditKey(string category, int? configId)
    {
        return $"{category}\u001F{(configId?.ToString(CultureInfo.InvariantCulture) ?? "all")}";
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
        return _pendingUnitEdits.Count > 0
            || _pendingResourceEdits.Count > 0
            || _pendingPlayerEdits.Count > 0
            || _pendingAdvancedEdits.Count > 0
            || _pendingAllResources;
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

    private void PopulateResourceGrid(IReadOnlyList<HeroEditorResourceRow> rows)
    {
        if (_resourceGrid is null) return;
        _resourceGrid.Rows.Clear();
        var source = rows.Count > 0
            ? rows
            : ResourceScanner.KnownCategoryLabels
                .Select(pair => new HeroEditorResourceRow(pair.Value, pair.Key, null, "未读取", 0, 0, null))
                .ToList();
        foreach (var item in source)
        {
            var count = item.NodeCount > 0 ? $"{item.NodeCount:N0} 处" : "未读取";
            var row = _resourceGrid.Rows[_resourceGrid.Rows.Add(item.Label, item.SizeLabel, count)];
            row.Tag = item;
        }
        if (source.Count > 0)
        {
            var selected = source.FirstOrDefault(item =>
                item.Category.Equals(_selectedResourceCategory, StringComparison.OrdinalIgnoreCase)
                && item.ConfigId == _selectedResourceConfigId) ?? source[0];
            SelectResourceRow(selected);
        }
        FilterGrid(_resourceGrid, _resourceSearch?.Text ?? string.Empty);
    }

    private void PopulateBuildingGrid(IReadOnlyList<BuildingStorageListItem> rows)
    {
        if (_buildingGrid is null) return;
        _buildingGrid.Rows.Clear();
        foreach (var item in rows)
        {
            var row = _buildingGrid.Rows[_buildingGrid.Rows.Add(
                item.Label,
                $"{item.ItemCount:N0} 项",
                item.Current)];
            row.Tag = item;
        }

        if (rows.Count > 0)
        {
            var selected = rows.FirstOrDefault(item =>
                item.Key.Equals(_selectedBuildingKey, StringComparison.OrdinalIgnoreCase)) ?? rows[0];
            SelectBuildingRow(selected);
        }
        else
        {
            if (_buildingSelectedLabel is not null) _buildingSelectedLabel.Text = "未读取建筑";
            if (_buildingCurrentStateLabel is not null) _buildingCurrentStateLabel.Text = "当前上限：尚未读取";
        }

        FilterGrid(_buildingGrid, _buildingSearch?.Text ?? string.Empty);
    }

    private void SelectBuildingRow(BuildingStorageListItem item)
    {
        _selectedBuildingKey = item.Key;
        if (_buildingSelectedLabel is not null) _buildingSelectedLabel.Text = item.Label;
        if (_buildingCurrentStateLabel is not null)
        {
            _buildingCurrentStateLabel.Text = $"当前上限：{item.Current}（{item.ItemCount:N0} 个现有储存类别）";
        }
    }

    private void SelectResourceRow(HeroEditorResourceRow item)
    {
        _selectedResourceCategory = item.Category;
        _selectedResourceConfigId = item.ConfigId;
        _selectedResourceSizeLabel = item.SizeLabel;
        if (_resourceSelectedLabel is not null) _resourceSelectedLabel.Text = $"{item.Label} · {item.SizeLabel}";
        if (_resourceCurrentStateLabel is not null) _resourceCurrentStateLabel.Text = DescribeResourceCurrent(item);
    }

    private static string DescribeResourceCurrent(HeroEditorResourceRow item)
    {
        if (item.NodeCount <= 0) return "当前数量：尚未读取";
        var count = $"{item.NodeCount:N0} 处";
        var capacity = FormatResourceValue(item.Capacity);
        if (!item.CurrentAmount.HasValue) return $"当前数量：各处不同；最大上限：{capacity}（{count}）";
        var state = item.Capacity > 0 && item.CurrentAmount.Value >= item.Capacity ? "，已满" : string.Empty;
        return $"当前数量：{FormatResourceValue(item.CurrentAmount.Value)}；最大上限：{capacity}（{count}{state}）";
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
