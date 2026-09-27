using CadSpace.Controls;
using CadSpace.Dxf;
using CadSpace.Engine;
using CadSpace.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace CadSpace.App;

public sealed partial class App : Application
{
    public static Window? MainWindow { get; private set; }
    private CadWorkspace? _workspace;
    private readonly List<OpenDrawing> _documents = new();
    private OpenDrawing? _active;
    private bool _fileOperation;
    public App() { InitializeComponent(); RequestedTheme = ApplicationTheme.Dark; }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _workspace = new CadWorkspace();
        MainWindow = new Window { Title = "CadSpace — Drafting & Modeling", Content = _workspace };
        _workspace.FileRequested += ExecuteFile;
        _workspace.DocumentTabs.ActivateRequested += key => Activate((OpenDrawing)key);
        _workspace.DocumentTabs.CloseRequested += async key => await Close((OpenDrawing)key);
        Open(SampleDrawings.StudioPlan()); MainWindow.Activate();
        _workspace.Loaded += (_, _) => _workspace.CommandLine.FocusInput();
    }
    private void Open(Drawing drawing, DxfSource? source = null, bool model = false)
    {
        var document = new OpenDrawing(drawing, source); _documents.Add(document); document.Session.Document.Changed += RefreshTabs; Activate(document);
        if (model) _workspace!.Viewport.Set3D(true);
    }
    private void Activate(OpenDrawing document)
    {
        _active = document; _workspace!.Bind(document.Session, document.Commands); RefreshTabs(); _workspace.CommandLine.FocusInput();
    }
    private void RefreshTabs()
    {
        if (_active == null || _workspace == null) return;
        _workspace.DocumentTabs.SetDocuments(_documents.Select(d => ((object)d, d.Session.Document.Drawing.Name, d.Session.Document.IsDirty)), _active);
        if (MainWindow != null) MainWindow.Title = _active.Session.Document.Drawing.Name + (_active.Session.Document.IsDirty ? " *" : "") + " — CadSpace";
    }
    private async void ExecuteFile(string action)
    {
        if (_fileOperation || _workspace == null) return; _fileOperation = true;
        try
        {
            switch (action)
            {
                case "NEW": Open(Drawing.Empty with { Name = $"Drawing{_documents.Count + 1}.dxf" }); break;
                case "STUDIO": Open(SampleDrawings.StudioPlan()); break;
                case "MODEL": Open(SampleDrawings.ModelStudy(), model: true); break;
                case "OPEN": await OpenFile(); break;
                case "SAVE": await SaveFile(); break;
                case "ABOUT":
                    await Dialog("CadSpace 0.1", "Independent CAD software built with Uno Platform 6.7, Skia and OpenGL/WebGL.\n\nThe workspace supports editable 2D geometry, layers, blocks, dimensions, hatching and triangle-mesh modeling. Type HELP for available commands.\n\nThis release is not a complete AutoCAD replacement. ACIS/B-rep solids, Boolean modeling, DWG, dynamic blocks, constraints and paper-space plotting are not implemented. DXF import/export supports a documented subset and reports lossy conversions. Keep backups of original files.\n\nMIT licensed • github.com/wieslawsoltes/CadSpace"); break;
            }
        }
        catch (Exception error) { _workspace.CommandLine.AddMessage(error.Message); await Dialog("Operation could not be completed", error.Message); }
        finally { _fileOperation = false; }
    }
    private async Task OpenFile()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, ViewMode = PickerViewMode.List };
        picker.FileTypeFilter.Add(".dxf");
        var file = await picker.PickSingleFileAsync(); if (file == null) return;
        var properties = await file.GetBasicPropertiesAsync();
        if (properties.Size > DxfCodec.MaximumCharacters) throw new InvalidOperationException("This version limits DXF imports to 64 MiB.");
        var text = await FileIO.ReadTextAsync(file); var read = DxfCodec.Read(text, file.Name);
        Open(read.Drawing, read.Source);
        if (!read.Warnings.IsEmpty) await Dialog("DXF import report", string.Join("\n\n", read.Warnings));
        _workspace!.CommandLine.AddMessage($"Opened {file.Name}: {read.Drawing.Entities.Length} model-space records.");
    }
    private async Task SaveFile()
    {
        if (_active == null) return;
        var document = _active; var drawing = document.Session.Document.Drawing;
        var result = DxfCodec.Write(drawing, document.Source);
        if (!result.Warnings.IsEmpty)
        {
            var dialog = new ContentDialog { XamlRoot = _workspace!.XamlRoot, Title = "Review DXF export", Content = new ScrollViewer { MaxHeight = 360, Content = new TextBlock { Text = string.Join("\n\n", result.Warnings) + "\n\nExport a copy and keep your original file. This is not a lossless native project save.", TextWrapping = TextWrapping.Wrap } }, PrimaryButtonText = "Export copy", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = Path.GetFileNameWithoutExtension(drawing.Name) + (result.Warnings.IsEmpty ? "" : "-export") };
        picker.FileTypeChoices.Add("ASCII DXF drawing", new List<string> { ".dxf" });
        var file = await picker.PickSaveFileAsync(); if (file == null) return;
        await FileIO.WriteTextAsync(file, result.Text);
        if (result.Warnings.IsEmpty && document.Session.Document.Drawing == drawing) document.Session.Document.MarkSaved();
        _workspace!.CommandLine.AddMessage($"Exported {file.Name}." + (result.Warnings.IsEmpty ? "" : " Editable native state remains in this session; export was lossy."));
    }
    private async Task Close(OpenDrawing document)
    {
        if (_fileOperation) return;
        _fileOperation = true;
        try
        {
            if (document.Session.Document.IsDirty)
            {
                var dialog = new ContentDialog { XamlRoot = _workspace!.XamlRoot, Title = "Discard unsaved changes?", Content = $"Changes to {document.Session.Document.Drawing.Name} have not been saved losslessly. Closing the tab will discard this editing state.", PrimaryButtonText = "Discard", CloseButtonText = "Keep open", DefaultButton = ContentDialogButton.Close };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            }
            document.Session.Document.Changed -= RefreshTabs; _documents.Remove(document);
            if (_documents.Count == 0) Open(Drawing.Empty);
            else if (_active == document) Activate(_documents[^1]); else RefreshTabs();
        }
        finally { _fileOperation = false; }
    }
    private async Task Dialog(string title, string message)
    {
        if (_workspace?.XamlRoot == null) return;
        await new ContentDialog { XamlRoot = _workspace.XamlRoot, Title = title, Content = new ScrollViewer { MaxHeight = 420, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap } }, CloseButtonText = "Close" }.ShowAsync();
    }
    private sealed class OpenDrawing
    {
        public CadSession Session { get; }
        public CommandEngine Commands { get; }
        public DxfSource? Source { get; }
        public OpenDrawing(Drawing drawing, DxfSource? source) { Session = new(new CadDocument(drawing)); Commands = new(Session); Source = source; }
    }
}
