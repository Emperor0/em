using D7SystemIntelligence.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace D7SystemIntelligence;

public sealed class StartupManagerWindow : Window
{
    private readonly StartupManagerService _service = new();
    private readonly DataGrid _grid = new();
    private readonly TextBlock _status = new();

    public StartupManagerWindow()
    {
        Title="D7 — Startup Manager";
        Width=1080; Height=700; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        FlowDirection=FlowDirection.RightToLeft;
        Background=Brush("Bg",Brushes.Black); Foreground=Brush("Text",Brushes.White);

        var root=new Grid{Margin=new Thickness(18)};
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

        var header=new StackPanel();
        header.Children.Add(new TextBlock{Text="Startup Manager",FontSize=28,FontWeight=FontWeights.SemiBold});
        header.Children.Add(new TextBlock{Text="تعطيل قابل للرجوع: Registry Run يُحفظ ثم تُحذف القيمة؛ Startup files تُنقل لمجلد D7. العناصر الحساسة تظهر بتحذير ولا يتم تعطيلها تلقائيًا.",TextWrapping=TextWrapping.Wrap,Foreground=Brush("Muted",Brushes.Gray),Margin=new Thickness(0,6,0,8)});
        var refresh=new Button{Content="إعادة الفحص",HorizontalAlignment=HorizontalAlignment.Right}; refresh.Click+=(_,_)=>Refresh(); header.Children.Add(refresh);
        Grid.SetRow(header,0); root.Children.Add(header);

        _grid.AutoGenerateColumns=false; _grid.IsReadOnly=true; _grid.SelectionMode=DataGridSelectionMode.Single; _grid.Margin=new Thickness(0,12,0,12);
        _grid.Columns.Add(new DataGridCheckBoxColumn{Header="On",Binding=new System.Windows.Data.Binding(nameof(StartupEntry.Enabled)),Width=new DataGridLength(.4,DataGridLengthUnitType.Star)});
        _grid.Columns.Add(new DataGridTextColumn{Header="البرنامج",Binding=new System.Windows.Data.Binding(nameof(StartupEntry.Name)),Width=new DataGridLength(1.4,DataGridLengthUnitType.Star)});
        _grid.Columns.Add(new DataGridTextColumn{Header="النطاق",Binding=new System.Windows.Data.Binding(nameof(StartupEntry.Scope)),Width=new DataGridLength(.6,DataGridLengthUnitType.Star)});
        _grid.Columns.Add(new DataGridTextColumn{Header="المصدر",Binding=new System.Windows.Data.Binding(nameof(StartupEntry.Source)),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        _grid.Columns.Add(new DataGridTextColumn{Header="التقييم",Binding=new System.Windows.Data.Binding(nameof(StartupEntry.ImpactHint)),Width=new DataGridLength(1.1,DataGridLengthUnitType.Star)});
        _grid.Columns.Add(new DataGridTextColumn{Header="الأمر/الملف",Binding=new System.Windows.Data.Binding(nameof(StartupEntry.Command)),Width=new DataGridLength(3,DataGridLengthUnitType.Star)});
        Grid.SetRow(_grid,1); root.Children.Add(_grid);

        var footer=new StackPanel();
        var row=new StackPanel{Orientation=Orientation.Horizontal};
        var disable=new Button{Content="تعطيل المحدد"};
        disable.Click+=(_,_)=>
        {
            if(_grid.SelectedItem is not StartupEntry e){_status.Text="اختر عنصرًا.";return;}
            if(e.ImpactHint.Contains("حساس",StringComparison.OrdinalIgnoreCase) && MessageBox.Show($"D7 صنف {e.Name} كعنصر حساس/قد يكون ضروريًا. تعطيله؟","Startup Warning",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
            try{_status.Text=_service.Disable(e.Id);}catch(Exception ex){_status.Text=ex.Message;} Refresh();
        };
        var restore=new Button{Content="استعادة المحدد"};
        restore.Click+=(_,_)=>
        {
            if(_grid.SelectedItem is not StartupEntry e){_status.Text="اختر عنصرًا.";return;}
            try{_status.Text=_service.Restore(e.Id);}catch(Exception ex){_status.Text=ex.Message;} Refresh();
        };
        row.Children.Add(disable);row.Children.Add(restore);footer.Children.Add(row);
        _status.TextWrapping=TextWrapping.Wrap;_status.Margin=new Thickness(0,8,0,0);footer.Children.Add(_status);
        Grid.SetRow(footer,2);root.Children.Add(footer);
        Content=root; Loaded+=(_,_)=>Refresh();
    }

    private void Refresh()
    {
        try{var list=_service.Scan();_grid.ItemsSource=list;_status.Text=$"Startup entries: {list.Count} • Active: {list.Count(x=>x.Enabled)} • Disabled by D7: {list.Count(x=>!x.Enabled)}";}
        catch(Exception ex){_status.Text=ex.Message;}
    }

    private static Brush Brush(string key,Brush fallback)=>Application.Current.TryFindResource(key) as Brush??fallback;
}
