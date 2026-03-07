using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;
using Avalonia.Styling;
using Material.Icons;

namespace WorldBuilder.ViewModels
{
    public enum DropPosition {
        None,
        Above,
        Below,
        Inside
    }

    public abstract partial class BookmarkNode : ObservableObject
    {
        private string _name = string.Empty;

        /// <summary>
        /// The name of the Bookmark or BookmarkFolder
        /// </summary>
        public string Name {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private DropPosition _dropPosition = DropPosition.None;

        /// <summary>
        /// Current drop position for drag-drop feedback
        /// </summary>
        [JsonIgnore]
        public DropPosition DropPosition {
            get => _dropPosition;
            set => SetProperty(ref _dropPosition, value);
        }

        private BookmarkFolder? _parent;

        /// <summary>
        /// The parent folder containing this node, or null if at root level
        /// </summary>
        [JsonIgnore]
        public BookmarkFolder? Parent {
            get => _parent;
            set => SetProperty(ref _parent, value);
        }

        private bool _isExpanded = false;

        /// <summary>
        /// Whether the node is expanded in the TreeView. Only applies to folders, but is defined in the base class
        /// to enable direct two-way binding with TreeDataGrid's HierarchicalExpanderColumn.
        /// </summary>
        [JsonIgnore]
        public bool IsExpanded {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        /// <summary>
        /// Reference to the original BookmarkNode in BookmarksManager.
        /// This is used for Edit Bookmark cloning to maintain references
        /// between cloned objects and their original counterparts.
        /// </summary>
        [JsonIgnore]
        public BookmarkNode? Ref { get; set; }

        /// <summary>
        /// The icon kind to display for this node
        /// </summary>
        [JsonIgnore]
        public abstract MaterialIconKind IconKind { get; }

        private SolidColorBrush? _iconColor;

        /// <summary>
        /// The icon color for this node (theme-aware and reactive)
        /// </summary>
        [JsonIgnore]
        public SolidColorBrush IconColor {
            get {
                // Always create fresh color to ensure theme awareness
                return CreateThemeAwareColor();
            }
        }

        /// <summary>
        /// The icon opacity for this node
        /// </summary>
        [JsonIgnore]
        public abstract double IconOpacity { get; }

        /// <summary>
        /// Creates theme-aware color based on current theme
        /// </summary>
        protected abstract SolidColorBrush CreateThemeAwareColor();

        /// <summary>
        /// Updates icon color when theme changes
        /// </summary>
        public void UpdateThemeColor() {
            var newColor = CreateThemeAwareColor();
            if (_iconColor?.Color != newColor.Color) {
                _iconColor = newColor;
                OnPropertyChanged(nameof(IconColor));
            }
        }

        public abstract BookmarkNode Clone();
    }

    public partial class Bookmark : BookmarkNode
    {
        private string _location = string.Empty;

        /// <summary>
        /// The AC /loc string format 0xXXYYCCCC [X Y Z] w x y z
        /// </summary>
        public string Location {
            get => _location;
            set => SetProperty(ref _location, value);
        }

        /// <summary>
        /// Icon kind for bookmark items
        /// </summary>
        public override MaterialIconKind IconKind => MaterialIconKind.MapMarker;

        /// <summary>
        /// Icon color for bookmark items (theme-aware)
        /// </summary>
        protected override SolidColorBrush CreateThemeAwareColor() {
            // Try to detect theme and use appropriate color
            var app = Avalonia.Application.Current;
            if (app?.RequestedThemeVariant == ThemeVariant.Dark) {
                return new SolidColorBrush(Color.FromRgb(220, 220, 220)); // #DCDCDC - off-white for dark mode
            }
            
            // Light mode or fallback - use darker color
            return new SolidColorBrush(Color.FromRgb(51, 51, 51)); // #333333 - dark gray for light mode
        }

        /// <summary>
        /// Icon opacity for bookmark items
        /// </summary>
        public override double IconOpacity => 0.96;

        public Bookmark() {
        }

        /// <summary>
        /// Creates a deep copy of this Bookmark
        /// </summary>
        public override Bookmark Clone() {
            return new Bookmark {
                Name = this.Name,
                Location = this.Location,
                Parent = this.Parent,
                IsExpanded = this.IsExpanded
            };
        }
    }

    public partial class BookmarkFolder : BookmarkNode
    {
        private ObservableCollection<BookmarkNode> _items = new();

        public BookmarkFolder() {
        }

        /// <summary>
        /// The collection of bookmarks and subfolders within this folder
        /// </summary>
        public ObservableCollection<BookmarkNode> Items {
            get => _items;
            set => SetProperty(ref _items, value);
        }

        /// <summary>
        /// Icon kind for folder items
        /// </summary>
        public override MaterialIconKind IconKind => MaterialIconKind.Folder;

        /// <summary>
        /// Icon color for folder items (always golden)
        /// </summary>
        protected override SolidColorBrush CreateThemeAwareColor() => new SolidColorBrush(Color.FromRgb(217, 184, 96)); // #D9B860

        /// <summary>
        /// Icon opacity for folder items
        /// </summary>
        public override double IconOpacity => 1.0;

        /// <summary>
        /// Creates a deep copy of this BookmarkFolder
        /// </summary>
        public override BookmarkFolder Clone() {
            var clone = new BookmarkFolder {
                Name = this.Name,
                Parent = this.Parent,
                IsExpanded = this.IsExpanded
            };
            foreach (var item in this.Items) {
                if (item is Bookmark bookmark) {
                    var clonedBookmark = bookmark.Clone();
                    clonedBookmark.Ref = bookmark;
                    clone.Items.Add(clonedBookmark);
                } else if (item is BookmarkFolder bookmarkFolder) {
                    var clonedFolder = bookmarkFolder.Clone();
                    clonedFolder.Ref = bookmarkFolder;
                    clone.Items.Add(clonedFolder);
                }
            }
            return clone;
        }
    }
}
