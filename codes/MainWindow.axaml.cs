using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
namespace NoWrite;

public partial class MainWindow : Window
{
    string? _iso; long _isoLen; bool _busy;
    CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();
        OpenIsoBtn.Click += async (_, _) => await PickIso();
        RefreshBtn.Click += (_, _) => Refresh();
        DriveBox.SelectionChanged += (_, _) => UpdateEnabled();
        FlashBtn.Click += async (_, _) => await Flash();
        Opened += (_, _) => Refresh();
    }

    void SetStatus(string s, string color, string msg = "")
    {
        Status.Text = s; Status.Foreground = Brush.Parse(color); StatusMsg.Text = msg;
    }

    async Task PickIso()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select ISO", AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("ISO image") { Patterns = new[] { "*.iso" } } }
        });
        if (files.Count == 0) return;
        var path = files[0].Path.LocalPath;
        _iso = null; _isoLen = new FileInfo(path).Length;
        IsoName.Text = Path.GetFileName(path); IsoSize.Text = Fmt.Bytes(_isoLen);
        if (!FlashService.IsHybridIso(path))
            SetStatus("ERROR", "#E5534B",
                "This ISO isn't a hybrid image (typical for Windows ISOs), so it can't be written directly yet.");
        else { _iso = path; SetStatus("READY", "#4CC38A"); }
        UpdateEnabled();
    }

    void Refresh()
    {
        try { DriveBox.ItemsSource = UsbDriveService.List(); DriveBox.SelectedIndex = DriveBox.ItemCount > 0 ? 0 : -1; }
        catch (Exception e) { SetStatus("ERROR", "#E5534B", "Could not scan drives: " + e.Message); }
        UpdateEnabled();
    }

    void UpdateEnabled()
    {
        var d = DriveBox.SelectedItem as UsbDrive;
        bool fits = d != null && _iso != null && (ulong)_isoLen <= d.Size;
        if (d != null && _iso != null && !fits)
            SetStatus("ERROR", "#E5534B", "The selected USB drive is smaller than the ISO.");
        FlashBtn.IsEnabled = !_busy && fits;
        OpenIsoBtn.IsEnabled = RefreshBtn.IsEnabled = DriveBox.IsEnabled = !_busy;
    }

    async Task<bool> Confirm(UsbDrive d)
    {
        var dlg = new Window
        {
            Title = "Confirm erase", Width = 440, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush.Parse("#1D2026")
        };
        var yes = new Button { Content = "Erase & Flash", Background = Brush.Parse("#C93C37"), Foreground = Brushes.White };
        var no = new Button { Content = "Cancel" };
        yes.Click += (_, _) => dlg.Close(true); no.Click += (_, _) => dlg.Close(false);
        dlg.Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 14,
            Children =
            {
                new TextBlock { Text = "ALL DATA on this drive will be erased:", FontWeight = FontWeight.Bold },
                new TextBlock { Text = $"{d.Model}\n{d.Letters}  ·  {Fmt.Bytes(d.Size)}\nPhysicalDrive{d.Index}", FontSize = 16 },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { no, yes } }
            }
        };
        return await dlg.ShowDialog<bool>(this);
    }

    async Task Flash()
    {
        var drive = (UsbDrive)DriveBox.SelectedItem!;
        if (!await Confirm(drive)) return;
        _busy = true; UpdateEnabled(); _cts = new();
        SetStatus("FLASHING...", "#E0A93B");
        var prog = new Progress<FlashProgress>(p =>
        {
            Bar.Value = p.Percent; Pct.Text = $"{p.Percent:0}%"; Op.Text = p.Operation;
            Speed.Text = p.SpeedBps is { } s ? $"{Fmt.Bytes(s)}/s" + (p.Eta is { } e ? $"  ·  {e:mm\\:ss} remaining" : "") : "";
            if (p.Phase == "verify") SetStatus("VERIFYING...", "#5AA9FF");
        });
        try
        {
            await FlashService.FlashAsync(_iso!, drive, prog, _cts.Token);
            SetStatus("SUCCESS", "#4CC38A", "The USB drive is ready to boot.");
        }
        catch (Exception e) { SetStatus("ERROR", "#E5534B", e.Message); }
        finally { _busy = false; UpdateEnabled(); Speed.Text = ""; }
    }
}
