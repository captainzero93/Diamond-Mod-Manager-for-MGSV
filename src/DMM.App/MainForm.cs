// Main window: fields, navigation and page layout
using DMM.Core.Engine;
using DMM.Core.Game;
using DMM.Core.Logging;
using DMM.Core.Mgsv;
using DMM.Core.State;

namespace DMM.App;

internal sealed partial class MainForm : Form
{
    private const string PageOrder = "order", PageLibrary = "library", PageActivity = "activity", PageSettings = "settings";

    private readonly AppConfig config;
    private readonly DmmContext context;
    private DmmManager? manager;
    private GameStatus? status;
    private string? gameProblem, problemDir; // why the game folder could not be opened or read, shown in the bar with a retry
    private ConflictReport? conflicts;
    // switched-off mods included: what the Show Conflicts filter lists
    private ConflictReport? potentialConflicts;
    private List<ModSource> available = [];
    private List<ModSourceError> unreadable = [];
    private bool busy;
    private string page = PageOrder;

    // frame
    private readonly NavRail nav = new() { Dock = DockStyle.Left };
    private readonly Panel main = new() { Dock = DockStyle.Fill };
    private readonly BusyBar busyBar = new() { Dock = DockStyle.Top };
    private readonly StackPanel top = new(scroll: false) { Dock = DockStyle.Top, FitHeight = true };
    private readonly ActionRow header = new("", "", Theme.PageTitle);
    private readonly InfoBar infoBar = new() { Visible = false };
    // keeps its width while empty, so Apply changes does not move when the note comes and goes
    private readonly Label pendingNote = new()
    {
        Font = Theme.Small, ForeColor = Theme.Warning, TextAlign = ContentAlignment.MiddleRight,
        MinimumSize = TextRenderer.MeasureText(PendingText, Theme.Small) + new Size(4, 0),
    };
    private const string PendingText = "Changes not applied";
    private readonly UiButton applyButton = new("Apply changes", ButtonKind.Primary, Theme.IconCheck, "Write the load order to the game files");
    private readonly UiButton addAllButton = new("Add all", ButtonKind.Secondary, Theme.IconAdd, "Add every mod here that isn't in the load order yet");
    private readonly UiButton addFileButton = new("Add file", ButtonKind.Secondary, Theme.IconAdd, "Add a .mgsv or .zip from anywhere (Ctrl+O). It's copied to your Mods folder.");
    private readonly UiButton changeFolderButton = new("Change folder", ButtonKind.Secondary, Theme.IconFolder);
    private readonly UiButton openLogButton = new("Open log file", ButtonKind.Secondary, Theme.IconOpen);
    private readonly Card searchCard = new();
    private readonly TextBox searchBox = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Body, PlaceholderText = "Search mods (Ctrl+F)" };
    private readonly Label statusLine = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Font = Theme.Small, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Panel pages = new() { Dock = DockStyle.Fill };

    // load order and mods folder share the details pane
    private readonly SplitContainer workspace = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
    private readonly Card orderCard = new() { Dock = DockStyle.Fill };
    private readonly ModList orderList = new() { Dock = DockStyle.Fill, AllowReorder = true, MultiSelect = true };
    private readonly InfoText orderCount = new() { Dock = DockStyle.Fill, AlignRight = true };
    private readonly UiButton upButton = new("", ButtonKind.Icon, Theme.IconUp, "Move up (Ctrl+Up)");
    private readonly UiButton downButton = new("", ButtonKind.Icon, Theme.IconDown, "Move down (Ctrl+Down)");
    private readonly UiButton removeButton = new("", ButtonKind.Icon, Theme.IconDelete, "Remove from the load order (Delete)");
    private readonly UiButton copyOrderButton = new("", ButtonKind.Icon, Theme.IconCopy, "Copy the load order as text");
    private static readonly string[] OrderSorts = ["order", "name", "date"], LibrarySorts = ["name", "order", "date"];
    private readonly Segmented orderSort = new("Load Order", "Name", "Date") { ShowDirection = true };
    private readonly CheckToggle showConflicts = new("Show Conflicts");
    // shows all mods, or only the ones switched on or off; for this session, like Show Conflicts
    private readonly Segmented onOff = new("All", "On", "Off");
    private readonly UiButton allOn = new("Disable all", ButtonKind.Subtle) { Dock = DockStyle.Left };
    private readonly Panel orderTop = new() { Dock = DockStyle.Top };
    private readonly Panel libraryTop = new() { Dock = DockStyle.Top, Visible = false };
    private readonly ToolTip orderCountTip = new();
    private string orderCountLong = "", orderCountShort = "";
    private readonly Segmented librarySort = new("Name", "Load Order", "Date") { ShowDirection = true };
    private readonly WrapLabel orderEmpty = new("", Theme.Body, Theme.Muted) { Dock = DockStyle.Fill, Centered = true, Visible = false };

    private readonly Card libraryCard = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly ModList libraryList = new() { Dock = DockStyle.Fill };
    private readonly InfoText libraryCount = new() { Dock = DockStyle.Fill };
    private readonly UiButton openFolderButton = new("", ButtonKind.Icon, Theme.IconOpen, "Open the mods folder");
    private readonly WrapLabel libraryEmpty = new("", Theme.Body, Theme.Muted) { Dock = DockStyle.Fill, Centered = true, Visible = false };
    private readonly FileSystemWatcher watcher = new() { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName };
    private readonly System.Windows.Forms.Timer rescanTimer = new() { Interval = 700 };
    private readonly System.Windows.Forms.Timer gameTimer = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer statusTimer = new() { Interval = 40 };
    private readonly NavRail.Entry launchEntry;
    private bool gameRunning, closingGame;

    // details
    private readonly Card detailsCard = new() { Dock = DockStyle.Fill };
    private readonly Panel detailsBody = new() { Dock = DockStyle.Fill };
    private readonly WrapLabel detailsEmpty = new("Select a mod to see what it installs, its description and conflicts.", Theme.Body, Theme.Muted) { Dock = DockStyle.Fill, Centered = true };
    private readonly StackPanel detailsHead = new(scroll: false) { Dock = DockStyle.Top, FitHeight = true };
    private readonly WrapLabel modTitle = new("", Theme.Heading) { Margin = new Padding(0, 0, 0, 2) };
    private readonly WrapLabel modSubtitle = new("", Theme.Body, Theme.Muted) { Margin = new Padding(0, 0, 0, 10) };
    private readonly ChipRow modChips = new() { Margin = new Padding(0, 0, 0, 12) };
    private readonly FlowLayoutPanel modActions = new() { WrapContents = true, Margin = new Padding(0, 0, 0, 14) };
    private readonly UiButton detailAdd = new("Add to load order", ButtonKind.Primary, Theme.IconAdd);
    private readonly UiButton detailToggle = new("Disable", ButtonKind.Secondary);
    private readonly UiButton detailRemove = new("Remove", ButtonKind.Secondary, Theme.IconDelete);
    private readonly UiButton detailReapply = new("Reapply", ButtonKind.Secondary);
    private readonly UiButton detailShow = new("Show file", ButtonKind.Subtle, Theme.IconFolder);
    private readonly Pivot pivot = new("Overview", "Files", "Conflicts") { Dock = DockStyle.Top };
    private readonly Panel pivotBody = new() { Dock = DockStyle.Fill };
    // overview: one bordered box that scrolls as a whole: conflict note, facts, then the description at its full height
    private readonly Panel overview = new() { Dock = DockStyle.Fill };
    private readonly Card overviewBox = new() { Dock = DockStyle.Fill };
    private readonly StackPanel overviewScroll = new() { Dock = DockStyle.Fill };
    private readonly InfoBar conflictCallout = new() { Visible = false };
    private readonly FactsGrid facts = new() { Margin = Padding.Empty };
    private readonly WrapLabel descriptionHeading = new("DESCRIPTION", Theme.Label, Theme.Muted);
    private readonly AutoHeightRichText description = new();
    private readonly Panel filesPanel = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly WrapLabel filesNote = new("", Theme.Small, Theme.Muted) { Dock = DockStyle.Top, AutoHeight = true };
    private readonly StyledList files = new() { Dock = DockStyle.Fill };
    private readonly Panel conflictsPanel = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly WrapLabel conflictsNote = new("", Theme.Small, Theme.Muted) { Dock = DockStyle.Top, AutoHeight = true };
    private readonly StyledList conflictList = new() { Dock = DockStyle.Fill };

    // other pages
    private readonly Card activityCard = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly WrapLabel activityEmpty = new("Nothing yet this session. Adding, applying and building show up here.", Theme.Body, Theme.Muted)
    {
        Dock = DockStyle.Fill, Centered = true,
    };
    private readonly LogView logView = new() { Dock = DockStyle.Fill };
    private int unseenWarnings; // shown on the Activity entry until the page is opened
    private readonly StackPanel settings = new() { Dock = DockStyle.Fill, Visible = false };

    private static readonly Icon AppIcon = new(typeof(MainForm).Assembly.GetManifestResourceStream("icon.ico")!);

    public MainForm(AppConfig config, DmmContext context, string? startPage)
    {
        this.config = config;
        this.context = context;
        launchEntry = new NavRail.Entry("launch", Theme.IconPlay, "Launch MGSV") { Bottom = true, Run = LaunchGame };
        Text = $"DMM {Log.Version}";
        Icon = AppIcon;
        Font = Theme.Body;
        BackColor = Theme.Content;
        ForeColor = Theme.Text;
        KeyPreview = true;
        // sizes in device pixels, so the window fits its scaled controls at 150% or 200%
        MinimumSize = new Size(Theme.Scale(this, 1120), Theme.Scale(this, 620));
        Size = new Size(Theme.Scale(this, 1360), Theme.Scale(this, 840));
        StartPosition = FormStartPosition.CenterScreen;
        if (config.WindowBounds is { Length: 4 } b && SystemInformation.VirtualScreen.IntersectsWith(new Rectangle(b[0], b[1], b[2], b[3])))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(b[0], b[1], b[2], b[3]);
        }
        if (config.WindowMaximized)
            WindowState = FormWindowState.Maximized;
        BuildFrame();
        BuildWorkspace();
        BuildDetails();
        BuildOtherPages();
        BuildSettings();
        BuildPresets();
        BuildBuilder();
        EnableFileDrop(this);
        Navigate(startPage ?? PageOrder);

        Log.Line += OnLogLine;
        Shown += (_, _) =>
        {
            // create every page's window handles now, so the first visit to a page does not build them on screen
            foreach (Control hidden in new Control[] { libraryCard, presetsCard, builderPage, activityCard, settings })
                CreateHandles(hidden);
            // start with the keyboard in the list, not on the rail or a toolbar control
            if (page == PageOrder)
                ActiveControl = orderList;
            StartModsFolder();
            OpenGame(config.GameDir ?? SteamLocator.FindGame());
        };
        FormClosing += OnClosing;
        FormClosed += OnClosed;
        Disposed += (_, _) =>
        {
            orderCountTip.Dispose();
            // header buttons of pages not shown are out of the control tree, so the window's Dispose misses them
            foreach (Control c in new Control[] { addFileButton, addAllButton, changeFolderButton, savePresetButton, buildButton, openLogButton })
                c.Dispose();
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.StyleTitleBar(this);
    }

    private void BuildFrame()
    {
        nav.Width = Theme.Scale(this, 236);
        nav.Add(new NavRail.Entry(PageOrder, Theme.IconList, "Load Order"));
        nav.Add(new NavRail.Entry(PageLibrary, Theme.IconFolder, "Mods Folder"));
        nav.Add(new NavRail.Entry(PagePresets, Theme.IconPresets, "Presets"));
        nav.Add(new NavRail.Entry(PageBuild, Theme.IconBuild, "Build Mod"));
        nav.Add(new NavRail.Entry(PageActivity, Theme.IconActivity, "Activity") { CountTone = Tone.Warning });
        nav.Add(launchEntry);
        nav.Add(new NavRail.Entry(PageSettings, Theme.IconSettings, "Settings") { Bottom = true });
        nav.Navigate += Navigate;
        gameTimer.Tick += (_, _) => RefreshLaunch();
        gameTimer.Start();
        statusTimer.Tick += (_, _) => OnStatusTick();

        int side = Theme.Scale(this, 28);
        top.Padding = new Padding(side, Theme.Scale(this, 22), side, Theme.Scale(this, 4));
        header.Margin = new Padding(0, 0, 0, Theme.Scale(this, 14));
        infoBar.Margin = new Padding(0, 0, 0, Theme.Scale(this, 14));
        top.Add(header, infoBar);
        applyButton.Click += (_, _) => RunApply(force: false);
        addFileButton.Click += (_, _) => AddFileDialog();
        addAllButton.Click += (_, _) => AddAll();
        changeFolderButton.Click += (_, _) => ChooseModsFolder();
        openLogButton.Click += (_, _) => OpenPath(Log.FilePath);

        busyBar.Height = Theme.Scale(this, 3);
        statusLine.Padding = new Padding(Theme.Scale(this, 14), 0, side, 0);
        pages.Padding = new Padding(side, 0, side, 0);

        // search at the bottom left: filters both mod lists as you type, Escape clears it
        int searchHeight = Theme.Body.Height + Theme.Scale(this, 14);
        // children kept clear of the rounded border on every side, so they do not paint over its corners
        int inset = Math.Max(Theme.Scale(this, 3), (searchHeight - searchBox.PreferredHeight) / 2);
        searchCard.Dock = DockStyle.Left;
        searchCard.Width = Theme.Scale(this, 260);
        searchCard.Padding = new Padding(Theme.Scale(this, 6), inset, Theme.Scale(this, 10), inset);
        searchCard.Controls.Add(searchBox);
        searchCard.Controls.Add(new Label { Dock = DockStyle.Left, Width = Theme.Scale(this, 24), Text = Theme.IconSearch, Font = Theme.IconsSmall, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleCenter });
        var footer = new Panel { Dock = DockStyle.Bottom, Height = searchHeight + Theme.Scale(this, 14), Padding = new Padding(side, Theme.Scale(this, 6), 0, Theme.Scale(this, 8)) };
        footer.Controls.Add(statusLine);
        footer.Controls.Add(searchCard);
        searchBox.TextChanged += (_, _) =>
        {
            string? before = (page == PageLibrary ? libraryList : orderList).SelectedRow?.Key;
            orderList.AllowReorder = CanReorder;
            RefreshLoadOrder();
            RefreshAvailable();
            upButton.Enabled = downButton.Enabled = CanEdit && page == PageOrder && orderList.SelectedRow != null; // in a filtered view they say why not
            // the details pane (description, file list) is rebuilt only when filtering changed the selection
            if ((page == PageLibrary ? libraryList : orderList).SelectedRow?.Key != before)
                ShowSelection();
        };
        searchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                searchBox.Clear();
                e.SuppressKeyPress = true;
            }
        };

        main.Controls.Add(pages);
        main.Controls.Add(footer);
        main.Controls.Add(top);
        main.Controls.Add(busyBar);
        Controls.Add(main);
        Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Border });
        Controls.Add(nav);
    }

    private void BuildWorkspace()
    {
        workspace.SplitterWidth = Theme.Scale(this, 16);
        // SplitContainer draws a dotted focus frame on its splitter once dragged; hand focus back to the list instead
        workspace.TabStop = false;
        // visible splitter grip in the scrollbar track colour, so the drag handle can be found
        workspace.Paint += (_, e) =>
        {
            Rectangle s = workspace.SplitterRectangle;
            int width = Theme.Scale(this, 4), inset = Theme.Scale(this, 8);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Theme.FillRounded(e.Graphics, Theme.Dark ? Theme.Rail : Theme.Border,
                new RectangleF(s.X + (s.Width - width) / 2f, s.Y + inset, width, Math.Max(0, s.Height - inset * 2)), width / 2f);
        };
        // SplitContainer drags with a hatched XOR bar; when the splitter cannot move (window at minimum width) the bar is
        // never erased, so repaint once the drag ends either way
        workspace.MouseUp += (_, _) => workspace.Invalidate();
        workspace.MouseCaptureChanged += (_, _) => { if (!workspace.Capture) workspace.Invalidate(); };
        workspace.SplitterMoved += (_, _) =>
        {
            workspace.Invalidate();
            if (workspace.Focused)
                ActiveControl = page == PageLibrary ? libraryList : orderList;
        };

        // load order
        orderCard.Padding = new Padding(Theme.Scale(this, 6));
        orderEmpty.Text = "No mods in the load order yet.\n\nDrop .mgsv or .zip files on this window, or add them from the Mods folder page.";
        orderList.SelectedIndexChanged += (_, _) => { if (!orderList.Refreshing) ShowSelection(); };
        orderList.ToggleClicked += row =>
        {
            if (ModifierKeys == Keys.None)
                SetEnabledWithSelection(row.Key, row.Enabled != true);
            else
                Change(() => manager!.SetEnabled([row.Key], row.Enabled != true)); // Ctrl/Shift+click on a switch: just that mod
        };
        orderList.Reordered += order => Change(() => manager!.SetOrder(order));
        orderList.FilesDropped += DropFiles;
        orderList.KeyDown += OnOrderKeyDown;
        upButton.Click += (_, _) => MoveSelected(-1);
        downButton.Click += (_, _) => MoveSelected(1);
        removeButton.Click += (_, _) => RemoveSelected();
        copyOrderButton.Click += (_, _) => CopyText(LoadOrderText, "Load order copied");
        orderCard.Controls.Add(orderList);
        orderCard.Controls.Add(orderEmpty);
        orderSort.SelectedIndex = Math.Max(0, Array.IndexOf(OrderSorts, config.OrderSort));
        orderSort.Descending = config.OrderSortDescending;
        orderList.AllowReorder = CanReorder;
        orderSort.SelectedChanged += index =>
        {
            config.OrderSort = OrderSorts[index];
            config.OrderSortDescending = orderSort.Descending;
            orderList.AllowReorder = CanReorder;
            SaveConfig();
            RefreshLoadOrder();
            ShowSelection();
        };
        // a filter for this session: starts off, so the full load order is what opens
        showConflicts.CheckedChanged += (_, _) =>
        {
            RefreshLoadOrder();
            ShowSelection();
        };
        Panel orderBar = Toolbar(null, orderSort, upButton, downButton, removeButton, copyOrderButton);
        orderCount.Resize += (_, _) => FitOrderCount();
        orderCard.Controls.Add(orderBar);

        // mods folder
        libraryCard.Padding = new Padding(Theme.Scale(this, 6));
        libraryList.SelectedIndexChanged += (_, _) => { if (!libraryList.Refreshing) ShowSelection(); };
        libraryList.ActionClicked += row => AddSources(row.Tag is ModSource s ? [s] : []);
        libraryList.RowActivated += row => AddSources(row.Action != null && row.Tag is ModSource s ? [s] : []);
        libraryList.FilesDropped += DropFiles;
        openFolderButton.Click += (_, _) => OpenPath(config.ResolvedModsFolder);
        libraryCard.Controls.Add(libraryList);
        libraryCard.Controls.Add(libraryEmpty);
        librarySort.SelectedIndex = Math.Max(0, Array.IndexOf(LibrarySorts, config.LibrarySort));
        librarySort.Descending = config.LibrarySortDescending;
        librarySort.SelectedChanged += index =>
        {
            config.LibrarySort = LibrarySorts[index];
            config.LibrarySortDescending = librarySort.Descending;
            SaveConfig();
            RefreshAvailable();
        };
        libraryCard.Controls.Add(Toolbar(null, librarySort, openFolderButton));

        // Show Conflicts sits above the load order card, so the toolbar keeps its room in a narrow list
        showConflicts.Dock = DockStyle.Left;
        orderTop.Height = Theme.Scale(this, 38);
        orderTop.Padding = new Padding(Theme.Scale(this, 2), 0, Theme.Scale(this, 10), Theme.Scale(this, 6));
        // the mod count sits on this line, right-aligned, so it never shares the toolbar with the sort buttons
        orderTop.Controls.Add(orderCount);
        orderTop.Controls.Add(allOn);
        orderTop.Controls.Add(new Panel { Dock = DockStyle.Left, Width = Theme.Scale(this, 18) });
        onOff.Dock = DockStyle.Left;
        orderTop.Controls.Add(onOff);
        orderTop.Controls.Add(new Panel { Dock = DockStyle.Left, Width = Theme.Scale(this, 14) });
        orderTop.Controls.Add(showConflicts);
        onOff.SelectedChanged += _ =>
        {
            orderList.AllowReorder = CanReorder;
            RefreshLoadOrder();
            ShowSelection();
        };
        // reads "Disable all" when every mod is on, otherwise "Enable all"
        allOn.Click += (_, _) => Change(() => manager!.SetAllEnabled(allOn.Text == "Enable all"));
        orderCount.BringToFront();
        // the mods folder count gets the same line above its list
        libraryTop.Height = orderTop.Height;
        libraryTop.Padding = new Padding(Theme.Scale(this, 4), 0, Theme.Scale(this, 10), Theme.Scale(this, 6));
        libraryTop.Controls.Add(libraryCount);
        workspace.Panel1.Controls.Add(orderCard);
        workspace.Panel1.Controls.Add(libraryCard);
        workspace.Panel1.Controls.Add(orderTop);
        workspace.Panel1.Controls.Add(libraryTop);
        orderCard.BringToFront();
        libraryCard.BringToFront();
        // details card starts level with the splitter grip, beside the list's top line
        workspace.Panel2.Padding = new Padding(0, Theme.Scale(this, 8), 0, 0);
        workspace.Panel2.Controls.Add(detailsCard);
        pages.Controls.Add(workspace);

        Load += (_, _) =>
        {
            SetSplitterLimits(config.ListWidth > 0 ? config.ListWidth : workspace.Width * 45 / 100);
            // the list keeps its width when the window shrinks, so the details pane is given back its minimum here
            workspace.SizeChanged += (_, _) => SetSplitterLimits(workspace.SplitterDistance);
        };
    }

    // SplitContainer throws when its minimums do not fit its width, so they shrink to fit a window smaller than intended
    private void SetSplitterLimits(int listWidth)
    {
        int list = Theme.Scale(this, 420), details = Theme.Scale(this, 380); // list: room for the sort control (three segments) and four buttons
        int room = workspace.Width - workspace.SplitterWidth;
        if (room <= 0)
            return;
        if (room < list + details)
        {
            list = Math.Max(0, room * list / (list + details));
            details = Math.Max(0, room - list);
        }
        workspace.Panel1MinSize = 0;
        workspace.Panel2MinSize = 0;
        workspace.SplitterDistance = Math.Clamp(listWidth, list, Math.Max(list, room - details));
        workspace.Panel1MinSize = list;
        workspace.Panel2MinSize = details;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        MinimumSize = new Size(Theme.Scale(this, 1120), Theme.Scale(this, 620));
        if (IsHandleCreated)
            BeginInvoke(() => SetSplitterLimits(workspace.SplitterDistance));
    }

    private void FitOrderCount()
    {
        int room = orderCount.ClientSize.Width;
        string text = TextRenderer.MeasureText(orderCountLong, orderCount.Font).Width <= room ? orderCountLong
            : TextRenderer.MeasureText(orderCountShort, orderCount.Font).Width <= room ? orderCountShort : "";
        if (orderCount.Text != text)
            orderCount.Text = text;
        orderCountTip.SetToolTip(orderCount, text == orderCountLong ? null : orderCountLong);
    }

    private Panel Toolbar(Label? text, params Control[] buttons)
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = Theme.Scale(this, 40), Padding = new Padding(Theme.Scale(this, text == null ? 4 : 10), 0, 0, Theme.Scale(this, 4)) };
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, Theme.Scale(this, 2), 0, 0) };
        // the sort control sits at the left edge, over the rows it sorts; the buttons stay at the right
        // sized by hand: AutoSize left the Mods Folder sort cut off, since that card is built hidden
        var left = new FlowLayoutPanel { Dock = DockStyle.Left, WrapContents = false, Padding = right.Padding };
        void FitLeft() => left.Width = left.Padding.Horizontal + left.Controls.Cast<Control>().Sum(c => c.Width + c.Margin.Horizontal);
        foreach (Control button in buttons)
        {
            button.Margin = new Padding(button is Segmented ? 0 : Theme.Scale(this, 2), 0, 0, 0);
            (button is Segmented ? left : right).Controls.Add(button);
            if (button is Segmented)
                button.SizeChanged += (_, _) => FitLeft();
        }
        FitLeft();
        if (text != null)
            bar.Controls.Add(text);
        bar.Controls.Add(right);
        if (left.Controls.Count > 0)
            bar.Controls.Add(left);
        return bar;
    }

    private void BuildDetails()
    {
        int pad = Theme.Scale(this, 22);
        detailsCard.Padding = new Padding(pad, Theme.Scale(this, 20), Theme.Scale(this, 6), Theme.Scale(this, 6));
        detailsHead.Padding = new Padding(0, 0, pad - Theme.Scale(this, 6), 0);
        detailsHead.Add(modTitle, modSubtitle, modChips, modActions);
        foreach (UiButton button in new[] { detailAdd, detailToggle, detailReapply, detailRemove, detailShow })
        {
            button.Margin = new Padding(0, 0, Theme.Scale(this, 8), 0);
            modActions.Controls.Add(button);
        }
        detailAdd.Click += (_, _) => { if (CurrentSource() is { } s) AddSources([s]); };
        detailToggle.Click += (_, _) => { if (CanEdit && CurrentModId() is { } id) SetEnabledWithSelection(id, manager!.State!.Mods.First(m => m.Id == id).Enabled is false); };
        detailRemove.Click += (_, _) => RemoveSelected();
        detailReapply.Click += (_, _) =>
        {
            if (!CanEdit || CurrentModId() is not { } id)
                return;
            string name = manager!.State!.Mods.First(m => m.Id == id).Name;
            if (!manager.State.GameDirFiles.Values.Any(f => f.Owner == id))
                MessageForm.Info(this, "Reapply mod", $"{name} doesn't put any files in the game folder, only into the game's .dat files. " +
                    "DMM rebuilds those every time you apply, so there's nothing to put back.");
            else if (MessageForm.Ask(this, "Reapply mod",
                    $"Put {name}'s game folder files back as they are in the mod?\n\nAny changes you made to those files will be lost.", "Reapply", tone: Tone.Warning))
                RunApply(force: false, reapply: id);
        };
        detailShow.Click += (_, _) => ShowSourceFile();

        pivot.SelectedChanged += index =>
        {
            overview.Visible = index == 0;
            filesPanel.Visible = index == 1;
            conflictsPanel.Visible = index == 2;
        };

        overview.Padding = new Padding(0, Theme.Scale(this, 14), pad - Theme.Scale(this, 6), Theme.Scale(this, 6));
        overviewScroll.BackColor = Theme.Surface;
        overviewScroll.Padding = new Padding(Theme.Scale(this, 16), Theme.Scale(this, 14), Theme.Scale(this, 12), Theme.Scale(this, 14));
        conflictCallout.Margin = new Padding(0, 0, 0, Theme.Scale(this, 12));
        var divider = new Divider { Margin = new Padding(0, Theme.Scale(this, 16), 0, 0) };
        descriptionHeading.Margin = new Padding(0, Theme.Scale(this, 16), 0, Theme.Scale(this, 8));
        description.Margin = Padding.Empty;
        overviewScroll.Add(conflictCallout, facts, divider, descriptionHeading, description);
        overviewBox.Controls.Add(overviewScroll);
        overview.Controls.Add(overviewBox);
        facts.LinkClicked += OpenUrl;
        description.LinkClicked += (_, e) => OpenUrl(e.LinkText);
        description.BackColor = Theme.Surface;
        description.ForeColor = Theme.Text;

        filesPanel.Padding = new Padding(0, Theme.Scale(this, 12), Theme.Scale(this, 4), 0);
        files.AddColumns(("Goes to", 150), ("Path", 360), ("Hash", 140), ("ContentHash", 240), ("Note", 200));
        filesPanel.Controls.Add(files);
        filesPanel.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.Scale(this, 10) });
        filesPanel.Controls.Add(filesNote);

        conflictsPanel.Padding = filesPanel.Padding;
        conflictList.AddColumns(("File", 300), ("In", 130), ("Mods, in load order", 260), ("Used", 160));
        conflictsPanel.Controls.Add(conflictList);
        conflictsPanel.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.Scale(this, 10) });
        conflictsPanel.Controls.Add(conflictsNote);

        pivotBody.Controls.AddRange([overview, filesPanel, conflictsPanel]);
        detailsBody.Controls.Add(pivotBody);
        detailsBody.Controls.Add(pivot);
        detailsBody.Controls.Add(detailsHead);
        detailsCard.Controls.Add(detailsBody);
        detailsCard.Controls.Add(detailsEmpty);
    }

    private void BuildOtherPages()
    {
        activityCard.Padding = new Padding(Theme.Scale(this, 14), Theme.Scale(this, 10), Theme.Scale(this, 4), Theme.Scale(this, 10));
        // a label rather than PlaceholderText, which disappears as soon as the box is clicked
        activityCard.Controls.Add(logView);
        activityCard.Controls.Add(activityEmpty);
        activityEmpty.BringToFront();

        pages.Controls.Add(activityCard);
        pages.Controls.Add(settings);
    }

    private void Navigate(string key)
    {
        // the rail raises Navigate again with the same key
        if (nav.Selected != key)
        {
            nav.Select(key);
            return;
        }
        page = key;
        if (key == PageActivity && unseenWarnings > 0)
        {
            unseenWarnings = 0;
            nav[PageActivity].Count = null;
            nav.Invalidate();
        }
        // search filters the two mod lists; the other pages have nothing to search
        searchCard.Visible = key is PageOrder or PageLibrary;
        statusLine.Padding = new Padding(searchCard.Visible ? Theme.Scale(this, 14) : 0, 0, statusLine.Padding.Right, 0);
        // one repaint for the whole switch instead of one per control shown or hidden
        using var pause = new RedrawPause(main);
        bool modsPage = key is PageOrder or PageLibrary;
        workspace.Visible = modsPage;
        orderCard.Visible = orderTop.Visible = key == PageOrder;
        libraryCard.Visible = libraryTop.Visible = key == PageLibrary;
        activityCard.Visible = key == PageActivity;
        presetsCard.Visible = key == PagePresets;
        builderPage.Visible = key == PageBuild;
        settings.Visible = key == PageSettings;

        (string title, string hint, Control[] actions) = key switch
        {
            PageOrder => ("Load Order", "Drag to reorder. Lower mods win when files clash.", new Control[] { addFileButton }),
            PageLibrary => ("Mods Folder", "The .mgsv and .zip files in your Mods folder.", new Control[] { changeFolderButton, addAllButton, addFileButton }),
            PagePresets => ("Presets", "Saved load orders you can switch between.",
                new Control[] { savePresetButton }),
            PageBuild => ("Build Mod", "Pack a mod folder into a .mgsv, the same way MakeBite does.",
                new Control[] { buildButton }),
            PageActivity => ("Activity", "What DMM did this session.", new Control[] { openLogButton }),
            _ => ("Settings", "Game folder, mods folder and appearance.", []),
        };
        header.Title = title;
        header.Hint = hint;
        header.SetActions([.. actions, pendingNote, applyButton]);
        ShowApplyButton();
        if (modsPage)
            ShowSelection();
        if (key == PagePresets)
            RefreshPresets();
    }

    private static void CreateHandles(Control control)
    {
        _ = control.Handle;
        foreach (Control child in control.Controls)
            CreateHandles(child);
    }

    // drops are accepted anywhere in the window; the mod lists handle their own drops
    private void EnableFileDrop(Control control)
    {
        if (control is ModList)
            return;
        control.AllowDrop = true;
        control.DragEnter += (_, e) => e.Effect = e.Data?.GetData(DataFormats.FileDrop) is string[] f && f.Any(ModSources.IsModFile) ? DragDropEffects.Copy : DragDropEffects.None;
        control.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] dropped)
                DropFiles(dropped.Where(ModSources.IsModFile).ToArray());
        };
        foreach (Control child in control.Controls)
            EnableFileDrop(child);
    }

    // back from another program (Steam): not on closing one of DMM's own dialogs, which would cut into the flow that opened it
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x001C && m.WParam != 0) // WM_ACTIVATEAPP, activated
            BeginInvoke(FinishRestoreWhenRetail);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Control && e.KeyCode == Keys.O)
        {
            AddFileDialog();
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.F)
        {
            searchBox.Focus();
            searchBox.SelectAll();
            e.Handled = true;
        }
        // Ctrl+1 to Ctrl+6: the pages in rail order
        else if (e.Control && e.KeyCode is >= Keys.D1 and <= Keys.D6)
        {
            string[] pagesInOrder = [PageOrder, PageLibrary, PagePresets, PageBuild, PageActivity, PageSettings];
            nav.Select(pagesInOrder[e.KeyCode - Keys.D1]);
            e.Handled = true;
        }
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (busy || building)
        {
            // Windows shutting down: refuse without a dialog; Windows shows the reason given to ShutdownBlock
            e.Cancel = true;
            if (e.CloseReason != CloseReason.WindowsShutDown)
                MessageForm.Info(this, "Still working", busy
                    ? "DMM is still changing the game files. Wait for it to finish before closing."
                    : "DMM is still building the mod. Wait for it to finish before closing.", Tone.Warning);
            return;
        }
        // a theme switch reopens the window with the same load order, so nothing is lost
        if (e.CloseReason != CloseReason.WindowsShutDown && !context.Rebuilding && !closingForSteam && manager is { HasPendingChanges: true }
            && !MessageForm.Ask(this, "Changes not applied", "You've changed the load order but haven't applied it yet. Quit without applying?", "Quit", "Stay", Tone.Warning))
        {
            e.Cancel = true;
            return;
        }
        if (busy || building)
        {
            e.Cancel = true; // something started while the question was open
            return;
        }
        if (context.Rebuilding)
            context.BuilderFields = [buildFolder ?? "", .. BuilderTexts.Select(t => t.Text)];
        Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        config.WindowBounds = [bounds.X, bounds.Y, bounds.Width, bounds.Height];
        config.WindowMaximized = WindowState == FormWindowState.Maximized;
        config.ListWidth = workspace.SplitterDistance;
        SaveConfig();
    }

    // after FormClosing, which a cancelled Windows shutdown undoes; FormClosed comes only when the window really closes
    private void OnClosed(object? sender, FormClosedEventArgs e)
    {
        Log.Line -= OnLogLine;
        rescanTimer.Dispose();
        gameTimer.Dispose();
        statusTimer.Dispose();
        watcher.Dispose();
        manager?.Dispose();
    }

    // the last action, at the bottom; it fades out after 10 s unless kept in Settings, and never while work runs
    private string Status
    {
        get => statusLine.Text;
        set
        {
            statusLine.Text = value;
            statusLine.ForeColor = Theme.Muted;
            statusShownAt = Environment.TickCount64;
            statusTimer.Enabled = !config.KeepStatusText;
        }
    }

    private long statusShownAt;

    private void OnStatusTick()
    {
        const int hold = 10_000, fade = 600;
        long age = Environment.TickCount64 - statusShownAt;
        if (busy || building)
            statusShownAt = Environment.TickCount64; // counts from when the work ends
        else if (age >= hold + fade)
        {
            statusTimer.Stop();
            statusLine.Text = "";
            statusLine.ForeColor = Theme.Muted;
        }
        else if (age >= hold)
            statusLine.ForeColor = Theme.Mix(Theme.Muted, statusLine.Parent?.BackColor ?? Theme.Content, Math.Min(1f, (age - hold) / (float)fade));
    }

    private void SaveConfig()
    {
        try { config.Save(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Warn("Couldn't save your settings (" + e.Message + ")"); }
    }

    private void OnLogLine(LogLevel level, string message)
    {
        if (level == LogLevel.Debug || IsDisposed || !IsHandleCreated)
            return;
        LogEntry entry = LogEntry.From(level, message);
        try
        {
            BeginInvoke(() =>
            {
                activityEmpty.Visible = false;
                logView.Add(entry);
                // warnings no longer flash past in the status line; the Activity entry counts them until it is opened
                if (level != LogLevel.Info && page != PageActivity)
                {
                    unseenWarnings++;
                    nav[PageActivity].Count = unseenWarnings.ToString();
                    nav.Invalidate();
                }
            });
        }
        catch (InvalidOperationException)
        {
            // window closing
        }
    }
}
