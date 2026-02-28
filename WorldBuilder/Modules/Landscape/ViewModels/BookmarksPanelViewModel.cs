using System.Collections.ObjectModel;
using System.Numerics;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogHostAvalonia;

using WorldBuilder.Lib.Settings;
using WorldBuilder.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Landscape.ViewModels {
    public partial class BookmarksPanelViewModel : ViewModelBase {
        private readonly LandscapeViewModel _landScapeViewModel;
        private readonly WorldBuilderSettings _settings;

        [ObservableProperty]
        private ObservableCollection<CameraBookmarkItem> _bookmarks = new();

        [ObservableProperty]
        private CameraBookmarkItem? _selectedBookmark;

        public BookmarksPanelViewModel(LandscapeViewModel landScapeViewModel, WorldBuilderSettings settings) {
            _landScapeViewModel = landScapeViewModel ?? throw new ArgumentNullException(nameof(landScapeViewModel));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            LoadFromSettings();
        }

        private void LoadFromSettings() {
            Bookmarks.Clear();
            foreach (var bm in _settings.Landscape.Bookmarks) {
                Bookmarks.Add(new CameraBookmarkItem(bm));
            }
        }

        private void SaveToSettings() {
            _settings.Landscape.Bookmarks.Clear();
            foreach (var item in Bookmarks) {
                _settings.Landscape.Bookmarks.Add(item.Model);
            }
            _settings.Save();
        }

        [RelayCommand]
        public void AddBookmark() {
            var scene = _landScapeViewModel.GameScene;
            var persp = scene.Camera3D;

            var lbX = (int)Math.Max(0, persp.Position.X / 192f);
            var lbY = (int)Math.Max(0, persp.Position.Y / 192f);
            lbX = Math.Clamp(lbX, 0, 253);
            lbY = Math.Clamp(lbY, 0, 253);

            var bookmark = new CameraBookmark {
                Name = $"LB {(lbX << 8 | lbY):X4}",
                PositionX = persp.Position.X,
                PositionY = persp.Position.Y,
                PositionZ = persp.Position.Z,
                Yaw = persp.Yaw,
                Pitch = persp.Pitch,
                FieldOfView = scene.Camera2D.FieldOfView,
                IsPerspective = true
            };

            var item = new CameraBookmarkItem(bookmark);
            Bookmarks.Add(item);
            SelectedBookmark = item;
            SaveToSettings();
        }

        [RelayCommand]
        public void GoToBookmark(CameraBookmarkItem? item) {
            if (item == null) return;
            var bm = item.Model;
            var scene = _landScapeViewModel.GameScene;

            var pos = new Vector3(bm.PositionX, bm.PositionY, bm.PositionZ);

            scene.Camera.Position = pos;
            scene.Camera3D.Yaw = bm.Yaw;
            scene.Camera3D.Pitch = bm.Pitch;

            scene.Camera2D.LookAt(pos);
            if (!float.IsNaN(bm.FieldOfView) && bm.FieldOfView > 0) {
                scene.Camera2D.FieldOfView = bm.FieldOfView;
            }
        }

        [RelayCommand]
        public void UpdateBookmark(CameraBookmarkItem? item) {
            if (item == null) return;
            var scene = _landScapeViewModel.GameScene;
            var persp = scene.Camera3D;
            var bm = item.Model;

            bm.PositionX = persp.Position.X;
            bm.PositionY = persp.Position.Y;
            bm.PositionZ = persp.Position.Z;
            bm.Yaw = persp.Yaw;
            bm.Pitch = persp.Pitch;
            bm.FieldOfView = scene.Camera2D.FieldOfView;
            bm.IsPerspective = true;

            item.RefreshDisplay();
            SaveToSettings();
        }

        [RelayCommand]
        public async Task RenameBookmark(CameraBookmarkItem? item) {
            if (item == null) return;

            var newName = await ShowRenameDialog(item.Name);
            if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

            item.Model.Name = newName;
            item.RefreshDisplay();
            SaveToSettings();
        }

        [RelayCommand]
        public void DeleteBookmark(CameraBookmarkItem? item) {
            if (item == null) return;
            Bookmarks.Remove(item);
            if (SelectedBookmark == item) SelectedBookmark = null;
            SaveToSettings();
        }

        [RelayCommand]
        public void MoveUp(CameraBookmarkItem? item) {
            if (item == null) return;
            var idx = Bookmarks.IndexOf(item);
            if (idx > 0) {
                Bookmarks.Move(idx, idx - 1);
                SaveToSettings();
            }
        }

        [RelayCommand]
        public void MoveDown(CameraBookmarkItem? item) {
            if (item == null) return;
            var idx = Bookmarks.IndexOf(item);
            if (idx >= 0 && idx < Bookmarks.Count - 1) {
                Bookmarks.Move(idx, idx + 1);
                SaveToSettings();
            }
        }

        private async Task<string?> ShowRenameDialog(string currentName) {
            string? result = null;
            var textBox = new Avalonia.Controls.TextBox {
                Text = currentName,
                Width = 300,
                Watermark = "Enter bookmark name"
            };

            // Add KeyBindings to the TextBox for Enter and Escape
            textBox.KeyBindings.AddRange(new[] {
                new Avalonia.Input.KeyBinding {
                    Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.Enter),
                    Command = new RelayCommand(() => {
                        result = textBox.Text;
                        DialogHost.Close("MainDialogHost");
                    })
                },
                new Avalonia.Input.KeyBinding {
                    Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.Escape),
                    Command = new RelayCommand(() => DialogHost.Close("MainDialogHost"))
                }
            });

            await DialogHost.Show(new Avalonia.Controls.StackPanel {
                Margin = new Avalonia.Thickness(20),
                Spacing = 15,
                Children = {
                    new Avalonia.Controls.TextBlock {
                        Text = "Rename Bookmark",
                        FontSize = 16,
                        FontWeight = Avalonia.Media.FontWeight.Bold
                    },
                    textBox,
                    new Avalonia.Controls.StackPanel {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = {
                            new Avalonia.Controls.Button {
                                Content = "Cancel",
                                Command = new RelayCommand(() => DialogHost.Close("MainDialogHost"))
                            },
                            new Avalonia.Controls.Button {
                                Content = "Rename",
                                Command = new RelayCommand(() => {
                                    result = textBox.Text;
                                    DialogHost.Close("MainDialogHost");
                                })
                            }
                        }
                    }
                }
            }, "MainDialogHost");

            return result;
        }
    }

    public partial class CameraBookmarkItem : ObservableObject {
        public CameraBookmark Model { get; }

        [ObservableProperty]
        private string _name;

        [ObservableProperty]
        private string _detail;

        public CameraBookmarkItem(CameraBookmark model) {
            Model = model;
            _name = model.Name;
            _detail = FormatDetail(model);
        }

        public void RefreshDisplay() {
            Name = Model.Name;
            Detail = FormatDetail(Model);
        }

        private static string FormatDetail(CameraBookmark bm) {
            var lbX = (int)Math.Max(0, bm.PositionX / 192f);
            var lbY = (int)Math.Max(0, bm.PositionY / 192f);
            lbX = Math.Clamp(lbX, 0, 253);
            lbY = Math.Clamp(lbY, 0, 253);
            return $"({lbX},{lbY})";
        }
    }
}
