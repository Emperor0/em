using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class StorageCenterWindow : Window
{
    private readonly StorageIntelligenceService _storage = new();
    private readonly DataGrid _drives = new();
    private readonly DataGrid _volumes = new();
    private readonly TextBlock _status = new();

    public StorageCenterWindow()
    {
        Title = "D7 — Storage Center";
        Width = 1120;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("Bg", Brushes.Black);
        Foreground = Brush("Text", Brushes.White);

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "Storage Center", FontSize = 28, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "SMART/Reliability عبر Windows Storage API، حرارة/ساعات تشغيل/أخطاء عندما يوفرها القرص، ومساحة الأقسام. ReTrim يستخدم Optimize-Volume الرسمي فقط.", TextWrapping = TextWrapping.Wrap, Foreground = Brush("Muted", Brushes.Gray), Margin = new Thickness(0,6,0,8) });
        var scan = new Button { Content = "فحص التخزين", HorizontalAlignment = HorizontalAlignment.Right };
        scan.Click += async (_,_) => await RefreshAsync();
        header.Children.Add(scan);
        Grid.SetRow(header,0); root.Children.Add(header);

        ConfigureDrives();
        Grid.SetRow(_drives,1); root.Children.Add(_drives);
        ConfigureVolumes();
        Grid.SetRow(_volumes,2); root.Children.Add(_volumes);

        var footer = new StackPanel();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var analyze = new Button { Content = "Analyze Volume" };
        analyze.Click += async (_,_) => await WithVolumeAsync(v => _storage.AnalyzeVolumeAsync(v.DriveLetter));
        var retrim = new Button { Content = "ReTrim المحدد" };
        retrim.Click += async (_,_) =>
        {
            if (_volumes.SelectedItem is not VolumeRecord v) { _status.Text="اختر Volume أولًا."; return; }
            if (MessageBox.Show($"تشغيل Windows ReTrim على {v.DriveLetter}؟ هذا مناسب عادة لـSSD/NVMe التي تدعم TRIM، وWindows سيرفض إذا غير مدعوم.","D7 ReTrim",MessageBoxButton.YesNo,MessageBoxImage.Information)!=MessageBoxResult.Yes) return;
            await WithVolumeAsync(x => _storage.RetrimVolumeAsync(x.DriveLetter));
        };
        row.Children.Add(analyze); row.Children.Add(retrim); footer.Children.Add(row);
        _status.TextWrapping=TextWrapping.Wrap; _status.Margin=new Thickness(0,8,0,0); footer.Children.Add(_status);
        Grid.SetRow(footer,3); root.Children.Add(footer);

        Content=root;
        Loaded += async (_,_) => await RefreshAsync();
    }

    private void ConfigureDrives()
    {
        _drives.AutoGenerateColumns=false; _drives.IsReadOnly=true; _drives.Margin=new Thickness(0,12,0,8);
        _drives.Columns.Add(new DataGridTextColumn{Header="Disk",Binding=new System.Windows.Data.Binding(nameof(PhysicalDriveRecord.FriendlyName)),Width=new DataGridLength(2,DataGridLengthUnitType.Star)});
        _drives.Columns.Add(new DataGridTextColumn{Header="Type",Binding=new System.Windows.Data.Binding(nameof(PhysicalDriveRecord.MediaType)),Width=new DataGridLength(.8,DataGridLengthUnitType.Star)});
        _drives.Columns.Add(new DataGridTextColumn{Header="Health",Binding=new System.Windows.Data.Binding(nameof(PhysicalDriveRecord.HealthStatus)),Width=new DataGridLength(.8,DataGridLengthUnitType.Star)});
        _drives.Columns.Add(new DataGridTextColumn{Header="GB",Binding=new System.Windows.Data.Binding(nameof(PhysicalDriveRecord.SizeGb)){StringFormat="{0:0.0}"},Width=new DataGridLength(.7,DataGridLengthUnitType.Star)});
        _drives.Columns.Add(new DataGridTextColumn{Header="Temp °C",Binding=new System.Windows.Data.Binding(nameof(PhysicalDriveRecord.TemperatureC)){StringFormat="{0:0}"},Width=new DataGridLength(.7,DataGridLengthUnitType.Star)});
        _drives.Columns.Add(new DataGridTextColumn{Header="Hours",Binding=new System.Windows.Data.Binding(nameof(PhysicalDriveRecord.PowerOnHours)),Width=new DataGridLength(.7,DataGridLengthUnitType.Star)});
        _drives.Columns.Add(new DataGridTextColumn{Header="Wear",Binding=new System.Windows.Data.Binding(nameof(PhysicalDriveRecord.Wear)),Width=new DataGridLength(.6,DataGridLengthUnitType.Star)});
        _drives.Columns.Add(new DataGridTextColumn{Header="Serial",Binding=new System.Windows.Data.Binding(nameof(PhysicalDriveRecord.SerialNumber)),Width=new DataGridLength(1.2,DataGridLengthUnitType.Star)});
    }

    private void ConfigureVolumes()
    {
        _volumes.AutoGenerateColumns=false; _volumes.IsReadOnly=true; _volumes.SelectionMode=DataGridSelectionMode.Single; _volumes.Margin=new Thickness(0,8,0,8);
        _volumes.Columns.Add(new DataGridTextColumn{Header="Volume",Binding=new System.Windows.Data.Binding(nameof(VolumeRecord.DriveLetter)),Width=new DataGridLength(.6,DataGridLengthUnitType.Star)});
        _volumes.Columns.Add(new DataGridTextColumn{Header="FS",Binding=new System.Windows.Data.Binding(nameof(VolumeRecord.FileSystem)),Width=new DataGridLength(.7,DataGridLengthUnitType.Star)});
        _volumes.Columns.Add(new DataGridTextColumn{Header="Health",Binding=new System.Windows.Data.Binding(nameof(VolumeRecord.HealthStatus)),Width=new DataGridLength(.8,DataGridLengthUnitType.Star)});
        _volumes.Columns.Add(new DataGridTextColumn{Header="Size GB",Binding=new System.Windows.Data.Binding(nameof(VolumeRecord.SizeGb)){StringFormat="{0:0.0}"},Width=new DataGridLength(.8,DataGridLengthUnitType.Star)});
        _volumes.Columns.Add(new DataGridTextColumn{Header="Free GB",Binding=new System.Windows.Data.Binding(nameof(VolumeRecord.FreeGb)){StringFormat="{0:0.0}"},Width=new DataGridLength(.8,DataGridLengthUnitType.Star)});
        _volumes.Columns.Add(new DataGridTextColumn{Header="Free %",Binding=new System.Windows.Data.Binding(nameof(VolumeRecord.FreePercent)){StringFormat="{0:0.0}%"},Width=new DataGridLength(.8,DataGridLengthUnitType.Star)});
    }

    private async Task RefreshAsync()
    {
        _status.Text="جاري قراءة Storage Reliability…";
        var s=await _storage.ScanAsync();
        _drives.ItemsSource=s.Drives; _volumes.ItemsSource=s.Volumes; _status.Text=s.Summary;
    }

    private async Task WithVolumeAsync(Func<VolumeRecord,Task<string>> action)
    {
        if(_volumes.SelectedItem is not VolumeRecord v){_status.Text="اختر Volume أولًا.";return;}
        try{_status.Text=await action(v);}catch(Exception ex){_status.Text=ex.Message;}
    }

    private static Brush Brush(string key, Brush fallback)=>Application.Current.TryFindResource(key) as Brush??fallback;
}
