using System.Windows;
using System.Windows.Controls;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

public partial class MainWindow
{
    private void Directory_Loaded(object sender, RoutedEventArgs e) => SizeDirectoryNameColumn();
    private void Directory_SizeChanged(object sender, SizeChangedEventArgs e) => SizeDirectoryNameColumn();

    private void SizeDirectoryNameColumn()
    {
        if (MediaGrid?.View is not GridView columns || DirectoryNameColumn is null || MediaGrid.ActualWidth <= 0) return;
        var fixedWidth = columns.Columns.Where(column => column != DirectoryNameColumn)
            .Sum(column => double.IsNaN(column.Width) ? column.ActualWidth : column.Width);
        // Preserve a readable name at narrow widths; the ListView supplies horizontal scrolling.
        DirectoryNameColumn.Width = Math.Max(220, MediaGrid.ActualWidth - fixedWidth - SystemParameters.VerticalScrollBarWidth - 6);
    }
}

public sealed class DirectoryTemplateSelector : DataTemplateSelector
{
    public DataTemplate? FolderTemplate { get; set; }
    public DataTemplate? FileTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item switch
    {
        FolderEntry => FolderTemplate,
        MediaItem => FileTemplate,
        _ => base.SelectTemplate(item, container)
    };
}
