using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace ElysiumWallpaper.ViewModels;

/// <summary>
/// Read-only sliding window over a source collection with fixed page size.
/// Mirrors source changes into <see cref="Items"/> by re-slicing whenever the
/// underlying list changes or the caller flips pages.
/// </summary>
public sealed class PagedView<T> : ObservableObject
{
    private readonly ObservableCollection<T> _source;
    private int _page = 1;
    private int _pageSize;

    public PagedView(ObservableCollection<T> source, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));

        _source = source;
        _pageSize = pageSize;
        Items = [];
        _source.CollectionChanged += OnSourceChanged;
        Refresh();
    }

    public ObservableCollection<T> Items { get; }

    public int Page
    {
        get => _page;
        set
        {
            int clamped = Math.Max(1, Math.Min(value, TotalPages));
            if (SetProperty(ref _page, clamped))
            {
                Refresh();
                NotifyPaging();
            }
        }
    }

    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (value <= 0) return;
            if (SetProperty(ref _pageSize, value))
            {
                Refresh();
                NotifyPaging();
            }
        }
    }

    public int TotalItems => _source.Count;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(_source.Count / (double)_pageSize));
    public bool CanGoPrev => _page > 1;
    public bool CanGoNext => _page < TotalPages;
    public string PageLabel => TotalItems == 0 ? "0 items" : $"Page {_page} of {TotalPages}  ({TotalItems} items)";

    public void NextPage() => Page = _page + 1;
    public void PrevPage() => Page = _page - 1;

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_page > TotalPages) _page = TotalPages;
        if (_page < 1) _page = 1;
        Refresh();
        NotifyPaging();
    }

    private void Refresh()
    {
        Items.Clear();
        int start = (_page - 1) * _pageSize;
        int end = Math.Min(start + _pageSize, _source.Count);
        for (int i = start; i < end; i++)
        {
            Items.Add(_source[i]);
        }
    }

    private void NotifyPaging()
    {
        OnPropertyChanged(nameof(TotalItems));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(PageLabel));
    }
}
