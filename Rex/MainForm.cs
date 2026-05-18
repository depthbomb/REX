using Microsoft.CSharp;
using System.Resources;
using System.Collections;
using System.Resources.Tools;
using System.CodeDom.Compiler;
using System.ComponentModel.Design;
using System.Diagnostics;

namespace Rex;

public partial class MainForm : Form
{
    private readonly ResourceListPanel    _list        = new();
    private readonly StatusStrip          _statusBar   = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolStripStatusLabel _countLabel  = new();
    private readonly List<ResourceEntry>  _resources   = [];

    private string? _currentFile;
    private string  _lastNamespace = "MyApplication.Properties";

    private bool IsDirty
    {
        get;
        set
        {
            field = value;
            UpdateTitle();
        }
    }

    private void UpdateTitle()
    {
        var file = _currentFile ?? "Untitled";

        Text = $"{(IsDirty ? "● " : "")}{file} - REX";
    }

    private void UpdateStatus()
    {
        _statusLabel.Text = _currentFile ?? "No file open";
        _countLabel.Text  = $"{_resources.Count} resource{(_resources.Count != 1 ? "s" : "")}";
    }

    public MainForm()
    {
        Width     = 1280;
        Height    = 720;
        AllowDrop = true;

        InitializeComponent();

        SuspendLayout();

        InitializeList();
        InitializeStatusBar();
        InitializeMenu();

        Controls.Add(_list);
        Controls.Add(_statusBar);
        Controls.Add(MainMenuStrip!);

        ResumeLayout(true);

        UpdateTitle();
        UpdateStatus();

        DragEnter += OnDragEnter;
        DragDrop  += OnDragDrop;

        Icon = Resources.Icons.icon;
    }

    private void InitializeStatusBar()
    {
        _statusLabel.Spring     = true;
        _statusLabel.TextAlign  = ContentAlignment.MiddleLeft;
        _countLabel.BorderSides = ToolStripStatusLabelBorderSides.Left;
        _countLabel.AutoSize    = true;

        _statusBar.Dock = DockStyle.Bottom;

        _statusBar.Items.Add(_statusLabel);
        _statusBar.Items.Add(_countLabel);
    }

    private void InitializeMenu()
    {
        var menu = new MenuStrip
        {
            Dock = DockStyle.Top
        };

        var fileMenu = new ToolStripMenuItem("File");
        fileMenu.DropDownItems.AddRange(
            Item("New", Keys.Control | Keys.N, (_, _) => NewFile()),
            Item("Open…", Keys.Control | Keys.O, (_, _) => OpenResx()),
            new ToolStripSeparator(),
            Item("Save", Keys.Control | Keys.S, (_, _) => SaveResx()),
            Item("Save As…", Keys.Control | Keys.Shift | Keys.S, (_, _) => SaveResxAs()),
            new ToolStripSeparator(),
            Item("Close resource", Keys.None, (_, _) => CloseResource()),
            new ToolStripSeparator(),
            Item("Generate Designer…", Keys.None, (_, _) => GenerateDesigner())
        );

        var editMenu = new ToolStripMenuItem("Edit");
        editMenu.DropDownItems.AddRange(
            Item("Add String…", Keys.Control | Keys.T, (_, _) => AddString()),
            Item("Add File…",   Keys.Control | Keys.F, (_, _) => AddFile()),
            new ToolStripSeparator(),
            Item("Rename…",         Keys.F2,     (_, _) => RenameSelected()),
            Item("Delete Selected", Keys.Delete, (_, _) => DeleteSelected())
        );

        menu.Items.Add(fileMenu);
        menu.Items.Add(editMenu);

        MainMenuStrip = menu;

        static ToolStripMenuItem Item(string text, Keys keys, EventHandler handler)
        {
            var item = new ToolStripMenuItem(text);

            item.Click += handler;

            if (keys != Keys.None)
                item.ShortcutKeys = keys;

            return item;
        }
    }

    private void InitializeList()
    {
        _list.Dock      = DockStyle.Fill;
        _list.AllowDrop = true;

        _list.RowDoubleClick  += ListOnRowDoubleClick;
        _list.DeleteRequested += (_, _) => DeleteSelected();
        _list.RenameRequested += (_, _) => RenameSelected();
        _list.DragEnter       += OnDragEnter;
        _list.DragDrop        += OnDragDrop;
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files)
            return;

        if (files is [var single] && single.EndsWith(".resx", StringComparison.OrdinalIgnoreCase) && _currentFile is null)
        {
            LoadResx(single);
            return;
        }

        foreach (string file in files)
            AddFileFromPath(file);
    }

    private void NewFile()
    {
        if (!ConfirmUnsavedChanges())
            return;

        CloseCurrentResourceState();
    }

    private void OpenResx()
    {
        if (!ConfirmUnsavedChanges())
            return;

        using var dialog = new OpenFileDialog();
        dialog.Filter = "RESX files (*.resx)|*.resx";

        if (dialog.ShowDialog() == DialogResult.OK)
            LoadResx(dialog.FileName);
    }

    private void LoadResx(string path)
    {
        _resources.Clear();
        _list.Clear();
        _currentFile = path;

        var originalDir = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = Path.GetDirectoryName(path)!;

            using var reader = new ResXResourceReader(path);
            reader.UseResXDataNodes = true;

            foreach (DictionaryEntry entry in reader)
            {
                if (entry.Value is not ResXDataNode node) continue;

                object? value = null;
                try { value = node.GetValue((ITypeResolutionService?)null); }
                catch { /* ignored */ }

                var resource = new ResourceEntry
                {
                    Name    = node.Name,
                    Value   = value,
                    Comment = node.Comment,
                    FileRef = node.FileRef
                };

                _resources.Add(resource);
                _list.AddItem(BuildRowItem(resource));
            }
        }
        finally
        {
            Environment.CurrentDirectory = originalDir;
        }

        _lastNamespace = ReadNamespaceFromDesigner(path) ?? _lastNamespace;

        IsDirty = false;
        UpdateStatus();
    }

    private void SaveResx()
    {
        if (_currentFile is null)
        {
            SaveResxAs();
            return;
        }

        WriteResx(_currentFile);
    }

    private void SaveResxAs()
    {
        using var dialog = new SaveFileDialog();
        dialog.Filter = "RESX files (*.resx)|*.resx";
        dialog.FileName = _currentFile is null
            ? "Resources.resx"
            : Path.GetFileName(_currentFile);

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        _currentFile = dialog.FileName;

        WriteResx(_currentFile);
    }

    private void CloseResource()
    {
        if (!ConfirmUnsavedChanges())
            return;

        CloseCurrentResourceState();
    }

    private void CloseCurrentResourceState()
    {
        _resources.Clear();
        _list.Clear();

        _currentFile = null;

        IsDirty = false;

        UpdateStatus();
    }

    private void WriteResx(string path)
    {
        using var writer = new ResXResourceWriter(path);

        foreach (var resource in _resources)
        {
            var node = resource.FileRef is not null
                ? new ResXDataNode(resource.Name, resource.FileRef)
                : new ResXDataNode(resource.Name, resource.Value);

            node.Comment = resource.Comment;
            writer.AddResource(node);
        }

        writer.Generate();
        IsDirty = false;
        UpdateStatus();
    }

    private void AddString()
    {
        using var dialog = new EditStringDialog();

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        if (string.IsNullOrWhiteSpace(dialog.ResourceName))
        {
            MessageBox.Show("Name cannot be empty.", "Invalid Name",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var resource = new ResourceEntry
        {
            Name    = dialog.ResourceName,
            Value   = dialog.ResourceValue,
            Comment = dialog.ResourceComment
        };

        _resources.Add(resource);
        _list.AddItem(BuildRowItem(resource));
        IsDirty = true;
        UpdateStatus();
    }

    private void AddFile()
    {
        using var dialog = new OpenFileDialog();
        if (dialog.ShowDialog() == DialogResult.OK)
            AddFileFromPath(dialog.FileName);
    }

    private void AddFileFromPath(string filePath)
    {
        var name      = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        object? value = null;

        switch (extension)
        {
            case ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif":
                try { value = Image.FromFile(filePath); } catch { /*Ignored*/ }
                break;
            case ".ico":
                try { value = new Icon(filePath); } catch { /*Ignored*/ }
                break;
        }

        string typeQName = value switch
        {
            Icon  _ => typeof(Icon).AssemblyQualifiedName!,
            Image _ => typeof(Bitmap).AssemblyQualifiedName!,
            _       => typeof(byte[]).AssemblyQualifiedName!
        };

        var resource = new ResourceEntry
        {
            Name    = name,
            Value   = value,
            FileRef = new ResXFileRef(filePath, typeQName)
        };

        _resources.Add(resource);
        _list.AddItem(BuildRowItem(resource));
        IsDirty = true;
        UpdateStatus();
    }

    private void DeleteSelected()
    {
        if (_list.SelectedIndices.Count == 0)
            return;

        if (_list.SelectedIndices.Count > 1)
        {
            var ok = MessageBox.Show(
                $"Delete {_list.SelectedIndices.Count} resources?",
                "Confirm Delete",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (ok != DialogResult.Yes)
                return;
        }

        foreach (int i in _list.SelectedIndices.OrderByDescending(x => x))
        {
            _resources.RemoveAt(i);
            _list.RemoveAt(i);
        }

        IsDirty = true;
        UpdateStatus();
    }

    private void RenameSelected()
    {
        if (_list.SelectedIndex < 0)
            return;

        int index    = _list.SelectedIndex;
        var resource = _resources[index];

        var newName = Prompt("Rename resource", resource.Name);
        if (newName is null || newName == resource.Name)
            return;

        if (string.IsNullOrWhiteSpace(newName))
        {
            MessageBox.Show("Name cannot be empty.", "Invalid Name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        resource.Name = newName;
        _list.UpdateItem(index, BuildRowItem(resource));
        IsDirty = true;
    }

    private void ListOnRowDoubleClick(object? sender, ResourceListPanel.RowActionEventArgs e)
    {
        var resource = _resources[e.Index];

        switch (resource.Value)
        {
            case string s:
            {
                using var dialog = new EditStringDialog(resource.Name, s, resource.Comment);
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                resource.Value   = dialog.ResourceValue;
                resource.Comment = dialog.ResourceComment;

                _list.UpdateItem(e.Index, BuildRowItem(resource));
                IsDirty = true;
                break;
            }

            case Image when e.IsPreviewZone:
            {
                using var dialog = new OpenFileDialog();
                dialog.Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico";

                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                resource.Value   = Image.FromFile(dialog.FileName);
                resource.FileRef = new ResXFileRef(
                    dialog.FileName,
                    typeof(Bitmap).AssemblyQualifiedName!);

                _list.UpdateItem(e.Index, BuildRowItem(resource));
                IsDirty = true;
                break;
            }
        }
    }

    private static ResourceListPanel.RowItem BuildRowItem(ResourceEntry resource)
    {
        Image?  preview;
        string  typeName;
        string  displayValue;

        switch (resource.Value)
        {
            case null:
                typeName     = "null";
                displayValue = "";
                preview      = null;
                break;
            case string s:
                typeName     = "String";
                displayValue = s;
                preview      = null;
                break;
            case Bitmap bmp:
                typeName     = "Bitmap";
                displayValue = $"{bmp.Width}×{bmp.Height}";
                preview      = CreateThumbnail(bmp);
                break;
            case Icon icon:
                typeName     = "Icon";
                displayValue = $"{icon.Width}×{icon.Height}";
                preview      = CreateThumbnail(icon.ToBitmap());
                break;
            case byte[] bytes:
                typeName     = "Byte[]";
                displayValue = $"{bytes.Length:N0} bytes";
                preview      = null;
                break;
            default:
                typeName     = resource.Value.GetType().Name;
                displayValue = resource.Value.ToString() ?? "";
                preview      = null;
                break;
        }

        return new ResourceListPanel.RowItem
        {
            Name         = resource.Name,
            TypeLabel    = typeName,
            DisplayValue = displayValue,
            Comment      = resource.Comment,
            Preview      = preview
        };
    }

    private static Image CreateThumbnail(Image source)
    {
        const int maxSize = 64;

        var scale = Math.Min((float)maxSize / source.Width, (float)maxSize / source.Height);
        var w     = Math.Max(1, (int)(source.Width  * scale));
        var h     = Math.Max(1, (int)(source.Height * scale));
        var bmp   = new Bitmap(w, h);

        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode   = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.SmoothingMode     = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        g.DrawImage(source, 0, 0, w, h);

        return bmp;
    }

    private void GenerateDesigner()
    {
        if (_currentFile is null)
            return;

        try
        {
            var className = Path.GetFileNameWithoutExtension(_currentFile);
            var namespaceName = Prompt("Namespace", _lastNamespace);
            if (namespaceName is null)
                return;

            _lastNamespace = namespaceName;

            var    provider    = new CSharpCodeProvider();
            string originalDir = Environment.CurrentDirectory;

            try
            {
                Environment.CurrentDirectory = Path.GetDirectoryName(_currentFile)!;

                var ccu = StronglyTypedResourceBuilder.Create(
                    _currentFile,
                    className,
                    namespaceName,
                    null,
                    provider,
                    false,
                    out string[]? unmatchable);

                var outputPath = Path.Combine(Path.GetDirectoryName(_currentFile)!, $"{className}.Designer.cs");

                using var sw = new StreamWriter(outputPath);
                provider.GenerateCodeFromCompileUnit(ccu, sw, new CodeGeneratorOptions());

                MessageBox.Show(
                    unmatchable is { Length: > 0 }
                        ? $"Generated with warnings:\n\n{string.Join("\n", unmatchable)}"
                        : $"Generated:\n{outputPath}",
                    unmatchable is { Length: > 0 } ? "Warnings" : "Success",
                    MessageBoxButtons.OK,
                    unmatchable is { Length: > 0 } ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            finally
            {
                Environment.CurrentDirectory = originalDir;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Generation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string? ReadNamespaceFromDesigner(string resxPath)
    {
        var designerPath = Path.ChangeExtension(resxPath, null) + ".Designer.cs";

        if (!File.Exists(designerPath))
            return null;

        foreach (string line in File.ReadLines(designerPath))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("namespace ", StringComparison.Ordinal))
                return trimmed["namespace ".Length..].Trim('{', ' ');
        }

        return null;
    }

    private bool ConfirmUnsavedChanges()
    {
        if (!IsDirty)
            return true;

        return MessageBox.Show(
            "You have unsaved changes. Discard them?",
            "Unsaved Changes",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private static string? Prompt(string label, string initial = "")
    {
        using var form = new Form();
        form.Text            = label;
        form.Width           = 500;
        form.Height          = 155;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.StartPosition   = FormStartPosition.CenterParent;
        form.MaximizeBox     = false;
        form.MinimizeBox     = false;

        var box    = new TextBox { Left = 12, Top = 12, Width = 460, Text = initial };
        var ok     = new Button { Text  = "OK", Left = 308, Top = 46, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text  = "Cancel", Left = 396, Top = 46, Width = 80, DialogResult = DialogResult.Cancel };

        form.Controls.AddRange(box, ok, cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        box.SelectAll();

        return form.ShowDialog() == DialogResult.OK ? box.Text : null;
    }

    private sealed class ResourceEntry
    {
        public required string       Name    { get; set; }
        public          object?      Value   { get; set; }
        public          string?      Comment { get; set; }
        public          ResXFileRef? FileRef { get; set; }
    }

    private sealed class EditStringDialog : Form
    {
        private readonly TextBox _nameBox;
        private readonly TextBox _valueBox;
        private readonly TextBox _commentBox;

        public string  ResourceName    => _nameBox.Text;
        public string  ResourceValue   => _valueBox.Text;
        public string? ResourceComment => string.IsNullOrWhiteSpace(_commentBox.Text) ? null : _commentBox.Text;

        public EditStringDialog(string  name    = "",
                                string  value   = "",
                                string? comment = null)
        {
            var isNew = string.IsNullOrEmpty(name);

            Text            = isNew ? "Add String" : "Edit String";
            Width           = 520;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            StartPosition   = FormStartPosition.CenterParent;

            int y = 12;

            Add(Label("Name:", y)); y += 20;

            _nameBox = new TextBox
            {
                Left      = 12, Top = y, Width = 472, Text = name,
                ReadOnly  = !isNew,
                BackColor = isNew ? SystemColors.Window : SystemColors.Control
            };

            Add(_nameBox);
            y += 30;

            Add(Label("Value:", y));
            y += 20;

            _valueBox = new TextBox
            {
                Left = 12, Top = y, Width = 472, Height = 72,
                Multiline = true, ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true, Text = value
            };

            Add(_valueBox);
            y += 82;

            Add(Label("Comment (optional):", y));
            y += 20;

            _commentBox = new TextBox { Left = 12, Top = y, Width = 472, Text = comment ?? "" };

            Add(_commentBox);
            y += 38;

            var ok     = new Button { Text = "OK",     Left = 316, Width = 80, Top = y, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 404, Width = 80, Top = y, DialogResult = DialogResult.Cancel };

            Add(ok);
            Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
            ClientSize   = new Size(504, y + 40);

            if (isNew)
                _nameBox.Focus();
            else
            {
                _valueBox.Focus();
                _valueBox.SelectAll();
            }

            return;

            void Add(Control c) => Controls.Add(c);

            static Label Label(string t, int top) => new() { Text = t, Left = 12, Top = top, AutoSize = true };
        }
    }
}
