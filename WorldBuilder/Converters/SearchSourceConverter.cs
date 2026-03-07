using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Data.Converters;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Converters
{
    public class SearchSourceConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count >= 2)
            {
                var searchText = values[0] as string;
                var bookmarks = values[1] as HierarchicalTreeDataGridSource<BookmarkNode>;
                var searchResults = values.Count > 2 ? values[2] as HierarchicalTreeDataGridSource<BookmarkNode> : null;

                if (string.IsNullOrWhiteSpace(searchText) && bookmarks != null)
                {
                    return bookmarks;
                }
                else if (!string.IsNullOrWhiteSpace(searchText) && searchResults != null)
                {
                    return searchResults;
                }
            }
            
            return values.Count > 1 ? values[1] : null;
        }

        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}