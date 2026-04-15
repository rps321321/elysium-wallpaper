using System.Collections.ObjectModel;

namespace ElysiumWallpaper.Tests;

public class PagedViewTests
{
    private static ObservableCollection<int> Range(int n) =>
        new(Enumerable.Range(1, n));

    [Fact]
    public void Empty_source_yields_page_1_of_1_with_no_items()
    {
        var source = new ObservableCollection<int>();
        var view = new PagedView<int>(source, pageSize: 5);

        Assert.Empty(view.Items);
        Assert.Equal(1, view.Page);
        Assert.Equal(1, view.TotalPages);
        Assert.False(view.CanGoPrev);
        Assert.False(view.CanGoNext);
    }

    [Fact]
    public void Page_size_splits_source_into_expected_pages()
    {
        var view = new PagedView<int>(Range(12), pageSize: 5);

        Assert.Equal(3, view.TotalPages);      // ceil(12/5)
        Assert.Equal([1, 2, 3, 4, 5], view.Items);

        view.NextPage();
        Assert.Equal([6, 7, 8, 9, 10], view.Items);
        Assert.True(view.CanGoPrev);
        Assert.True(view.CanGoNext);

        view.NextPage();
        Assert.Equal([11, 12], view.Items);
        Assert.True(view.CanGoPrev);
        Assert.False(view.CanGoNext);
    }

    [Fact]
    public void Page_is_clamped_to_valid_range()
    {
        var view = new PagedView<int>(Range(10), pageSize: 4) { Page = 99 };
        Assert.Equal(3, view.Page);  // clamped to TotalPages

        view.Page = -5;
        Assert.Equal(1, view.Page);  // clamped to 1
    }

    [Fact]
    public void Source_change_updates_items_automatically()
    {
        var source = Range(3);
        var view = new PagedView<int>(source, pageSize: 5);
        Assert.Equal([1, 2, 3], view.Items);

        source.Add(4);
        source.Add(5);
        Assert.Equal([1, 2, 3, 4, 5], view.Items);

        source.Add(6);
        Assert.Equal(2, view.TotalPages);
    }

    [Fact]
    public void Removing_items_reclamps_page()
    {
        var source = Range(20);
        var view = new PagedView<int>(source, pageSize: 5) { Page = 4 };
        Assert.Equal([16, 17, 18, 19, 20], view.Items);

        // Remove enough items that the current page disappears.
        while (source.Count > 8) source.RemoveAt(source.Count - 1);

        Assert.True(view.Page <= view.TotalPages);
    }

    [Fact]
    public void PageSize_setter_recomputes_pages()
    {
        var view = new PagedView<int>(Range(10), pageSize: 5);
        Assert.Equal(2, view.TotalPages);

        view.PageSize = 3;
        Assert.Equal(4, view.TotalPages);  // ceil(10/3)
    }

    [Fact]
    public void PageSize_zero_or_negative_is_rejected_silently()
    {
        var view = new PagedView<int>(Range(5), pageSize: 5);
        view.PageSize = 0;       // ignored
        view.PageSize = -3;      // ignored
        Assert.Equal(5, view.PageSize);
    }

    [Fact]
    public void PageLabel_is_human_readable()
    {
        var view = new PagedView<int>(Range(10), pageSize: 4);
        Assert.Contains("Page 1 of 3", view.PageLabel);
        Assert.Contains("10 items", view.PageLabel);
    }
}
