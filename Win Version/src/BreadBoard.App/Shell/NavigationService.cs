using System.Windows;
using BreadBoard.Core.Config;
using BreadBoard.Core.Logging;

namespace BreadBoard.App.Shell;

public enum PageId { Soundboard, Mixer, Shortcuts, Settings }

/// <summary>
/// Creates each page the first time it is shown and keeps it (no work for pages never visited).
/// Remembers the last page across restarts.
/// </summary>
public sealed class NavigationService(ConfigService config)
{
    private readonly Dictionary<PageId, FrameworkElement> _pages = [];
    private Func<PageId, FrameworkElement>? _factory;

    public PageId Current { get; private set; } = Enum.TryParse<PageId>(config.Current.Ui.LastPage, out var p) ? p : PageId.Soundboard;
    public event Action<PageId, FrameworkElement>? Navigated;

    public void SetFactory(Func<PageId, FrameworkElement> factory) => _factory = factory;

    public void Navigate(PageId page)
    {
        if (_factory is null) throw new InvalidOperationException("Page factory not set");
        if (!_pages.TryGetValue(page, out var view))
        {
            view = _factory(page);
            _pages[page] = view;
            Log.Debug($"Page created: {page}");
        }
        Current = page;
        if (config.Current.Ui.LastPage != page.ToString()) config.Update(c => c.Ui.LastPage = page.ToString());
        Navigated?.Invoke(page, view);
    }
}
