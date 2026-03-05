using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

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
        private DropPosition _dropPosition = DropPosition.None;
        private BookmarkFolder? _parent;

        /// <summary>
        /// The name of the Bookmark or BookmarkFolder
        /// </summary>
        public string Name {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>
        /// Current drop position for drag-drop feedback
        /// </summary>
        [JsonIgnore]
        public DropPosition DropPosition {
            get => _dropPosition;
            set => SetProperty(ref _dropPosition, value);
        }

        /// <summary>
        /// The parent folder containing this node, or null if at root level
        /// </summary>
        [JsonIgnore]
        public BookmarkFolder? Parent {
            get => _parent;
            set => SetProperty(ref _parent, value);
        }

        /// <summary>
        /// Reference to the original BookmarkNode in BookmarksManager.
        /// This is used for Edit Bookmark cloning to maintain references
        /// between cloned objects and their original counterparts.
        /// </summary>
        [JsonIgnore]
        public BookmarkNode? Ref { get; set; }

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
        /// Creates a deep copy of this Bookmark
        /// </summary>
        public override Bookmark Clone() {
            return new Bookmark {
                Name = this.Name,
                Location = this.Location,
                Parent = this.Parent
            };
        }
    }

    public partial class BookmarkFolder : BookmarkNode
    {
        private System.Collections.ObjectModel.ObservableCollection<BookmarkNode> _items = new();
        private bool _isExpanded = false;

        /// <summary>
        /// The collection of bookmarks and subfolders within this folder
        /// </summary>
        public System.Collections.ObjectModel.ObservableCollection<BookmarkNode> Items {
            get => _items;
            set => SetProperty(ref _items, value);
        }

        /// <summary>
        /// Whether the folder is expanded in the TreeView
        /// </summary>
        [JsonIgnore]
        public bool IsExpanded {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        /// <summary>
        /// Creates a deep copy of this BookmarkFolder
        /// </summary>
        public override BookmarkFolder Clone() {
            var clone = new BookmarkFolder {
                Name = this.Name,
                IsExpanded = this.IsExpanded,
                Parent = this.Parent
            };
            foreach (var item in this.Items) {
                if (item is Bookmark bookmark) {
                    clone.Items.Add(bookmark.Clone());
                } else if (item is BookmarkFolder bookmarkFolder) {
                    clone.Items.Add(bookmarkFolder.Clone());
                }
            }
            return clone;
        }
    }
}
