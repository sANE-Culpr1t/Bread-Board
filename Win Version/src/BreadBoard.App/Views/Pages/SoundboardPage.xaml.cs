using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BreadBoard.App.ViewModels;

namespace BreadBoard.App.Views.Pages;

public partial class SoundboardPage : UserControl
{
    private static readonly string[] FixedFilters = ["All", "Favourites"];
    private readonly ICollectionView _view;
    private string _query = "";
    private string _filter = "All";

    public SoundboardPage()
    {
        InitializeComponent();
        Pads.ItemsSource = SamplePad.All;
        _view = CollectionViewSource.GetDefaultView(SamplePad.All);
        _view.Filter = o => Matches((SamplePad)o);

        var filters = FixedFilters.Concat(SamplePad.All.Select(p => p.Category).Distinct());
        foreach (var f in filters)
        {
            var chip = new RadioButton { Content = f, GroupName = "sbfilter", Style = (Style)FindResource("Chip"), IsChecked = f == "All" };
            chip.Checked += (_, _) => { _filter = f; Refresh(); };
            Filters.Children.Add(chip);
        }
    }

    public void SetQuery(string query)
    {
        _query = query.Trim();
        Heading.Text = _query.Length == 0 ? "Quick Access" : $"Results for “{_query}”";
        Refresh();
    }

    private bool Matches(SamplePad p)
    {
        if (_filter == "Favourites" && !p.Favorite) return false;
        if (_filter is not ("All" or "Favourites") && p.Category != _filter) return false;
        return _query.Length == 0
            || p.Name.Contains(_query, StringComparison.OrdinalIgnoreCase)
            || p.Category.Contains(_query, StringComparison.OrdinalIgnoreCase)
            || p.Shortcut.Equals(_query, StringComparison.OrdinalIgnoreCase);
    }

    private void Refresh()
    {
        _view.Refresh();
        var any = !_view.IsEmpty;
        Empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Text = _query.Length > 0 ? "No sounds match your search." : "No sounds in this view.";
    }
}

