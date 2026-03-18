using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using WorldBuilder.Controls;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Converters
{
    public class TreeListSearchSourceConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count >= 2)
            {
                var searchText = values[0] as string;
                var bookmarks = values[1] as TreeList<BookmarkNode>;
                var searchResults = values.Count > 2 ? values[2] as TreeList<BookmarkNode> : null;

                if (string.IsNullOrWhiteSpace(searchText) && bookmarks != null)
                {
                    return bookmarks.VisibleRows;
                }
                else if (!string.IsNullOrWhiteSpace(searchText) && searchResults != null)
                {
                    return searchResults.VisibleRows;
                }
            }
            
            // Fallback: return the visible rows of the bookmarks if available
            if (values.Count > 1 && values[1] is TreeList<BookmarkNode> bookmarkList)
            {
                return bookmarkList.VisibleRows;
            }
            
            return null;
        }

        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
