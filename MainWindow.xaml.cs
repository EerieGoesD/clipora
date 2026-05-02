using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Clipora
{
    public sealed partial class MainWindow : Window
    {
        private readonly List<Snippet> _allSnippets = new();
        private readonly ObservableCollection<Snippet> _displayedSnippets = new();
        private AppSettings _settings = new();
        private string _searchText = "";
        private string _categoryFilter = "All";
        private string? _lastCapturedText;
        private bool _didPlaceWindow;

        public MainWindow()
        {
            InitializeComponent();

            SetWindowIcon();

            Activated += OnActivatedPlaceWindow;

            SnippetsList.ItemsSource = _displayedSnippets;

            Load();

            try
            {
                Clipboard.ContentChanged += OnClipboardChanged;
            }
            catch
            {
            }
        }

        private void SetWindowIcon()
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.SetIcon("Assets\\icon.ico");
        }

        private void OnActivatedPlaceWindow(object sender, WindowActivatedEventArgs args)
        {
            if (_didPlaceWindow) return;
            _didPlaceWindow = true;

            try
            {
                PlaceWindowOnVisibleArea(width: 420, height: 640, margin: 12);
            }
            catch
            {
            }
        }

        private void PlaceWindowOnVisibleArea(int width, int height, int margin)
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            RectInt32 wa = displayArea.WorkArea;

            int w = Math.Min(width, Math.Max(200, wa.Width - margin * 2));
            int h = Math.Min(height, Math.Max(200, wa.Height - margin * 2));

            appWindow.Resize(new SizeInt32(w, h));

            int x = wa.X + wa.Width - w - margin;
            int y = wa.Y + margin;

            x = Clamp(x, wa.X + margin, wa.X + wa.Width - w - margin);
            y = Clamp(y, wa.Y + margin, wa.Y + wa.Height - h - margin);

            appWindow.Move(new PointInt32(x, y));
        }

        private static int Clamp(int v, int min, int max)
            => (v < min) ? min : (v > max) ? max : v;

        private void Load()
        {
            _settings = SnippetStorage.LoadSettings();
            AutoCaptureToggle.IsChecked = _settings.AutoCapture;

            var snippets = SnippetStorage.LoadSnippets();

            if (snippets.Count == 0)
            {
                snippets.Add(new Snippet { Text = "Welcome to Clipora!", Order = 0 });
                snippets.Add(new Snippet { Text = "Press Ctrl+Shift+V anywhere to open this window.", Category = "Tip", Order = 1 });
                snippets.Add(new Snippet { Text = "Click + to add a snippet, or just copy text and it shows up here.", Category = "Tip", Order = 2 });
                SnippetStorage.SaveSnippets(snippets);
            }

            _allSnippets.Clear();
            _allSnippets.AddRange(snippets);

            RebuildCategoryFilter();
            ApplyFilter();
        }

        private void Save() => SnippetStorage.SaveSnippets(_allSnippets);

        private void ApplyFilter()
        {
            _displayedSnippets.Clear();

            IEnumerable<Snippet> q = _allSnippets;

            if (_categoryFilter == "History")
                q = q.Where(s => s.IsAutoCaptured || s.IsDeleted);
            else if (_categoryFilter == "Pinned")
                q = q.Where(s => s.IsPinned && !s.IsDeleted);
            else if (_categoryFilter == "All")
                q = q.Where(s => !s.IsDeleted);
            else
                q = q.Where(s => s.Category == _categoryFilter && !s.IsDeleted);

            if (!string.IsNullOrEmpty(_searchText))
                q = q.Where(s => s.Text.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

            q = q.OrderByDescending(s => s.IsPinned).ThenBy(s => s.Order);

            foreach (var s in q) _displayedSnippets.Add(s);
        }

        private void RebuildCategoryFilter()
        {
            CategoryFilterPanel.Children.Clear();

            var filters = new List<string> { "All", "Pinned", "History" };
            foreach (var cat in _allSnippets
                .Select(s => s.Category)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .OrderBy(c => c))
            {
                filters.Add(cat);
            }

            foreach (var f in filters)
            {
                var btn = new Button
                {
                    Content = f,
                    Tag = f,
                    Padding = new Thickness(10, 4, 10, 4),
                    FontSize = 12,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(12),
                    MinWidth = 0,
                    MinHeight = 0
                };

                btn.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    f == _categoryFilter
                        ? Windows.UI.Color.FromArgb(255, 99, 102, 241)
                        : Windows.UI.Color.FromArgb(255, 24, 24, 28));

                btn.Click += (s, e) =>
                {
                    var b = (Button)s;
                    _categoryFilter = (string)b.Tag;
                    RebuildCategoryFilter();
                    ApplyFilter();
                };

                CategoryFilterPanel.Children.Add(btn);
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = SearchBox.Text ?? "";
            ApplyFilter();
        }

        private async void OnClipboardChanged(object? sender, object e)
        {
            if (!_settings.AutoCapture) return;

            string? text = null;
            try
            {
                var content = Clipboard.GetContent();
                if (!content.Contains(StandardDataFormats.Text)) return;
                text = await content.GetTextAsync();
            }
            catch
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(text)) return;
            if (text == _lastCapturedText) return;

            DispatcherQueue.TryEnqueue(() => AddCapturedSnippet(text));
        }

        private void AddCapturedSnippet(string text)
        {
            _lastCapturedText = text;

            var existing = _allSnippets.FirstOrDefault(s => s.Text == text);
            if (existing != null)
            {
                existing.Order = NextTopOrder();
                Save();
                ApplyFilter();
                return;
            }

            var snippet = new Snippet
            {
                Text = text,
                IsAutoCaptured = true,
                Order = NextTopOrder()
            };
            _allSnippets.Add(snippet);
            EnforceCap();
            Save();
            RebuildCategoryFilter();
            ApplyFilter();
        }

        private int NextTopOrder()
        {
            var min = _allSnippets.Count == 0 ? 0 : _allSnippets.Min(s => s.Order);
            return min - 1;
        }

        private void EnforceCap()
        {
            if (_settings.HistoryCap <= 0) return;
            var captured = _allSnippets
                .Where(s => s.IsAutoCaptured && !s.IsPinned)
                .OrderByDescending(s => s.CreatedAt)
                .ToList();

            for (int i = _settings.HistoryCap; i < captured.Count; i++)
            {
                _allSnippets.Remove(captured[i]);
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not Snippet s) return;

            var data = new DataPackage();
            data.SetText(s.Text);
            Clipboard.SetContent(data);
            _lastCapturedText = s.Text;

            var original = btn.Content;
            var originalBg = btn.Background;
            btn.Content = "✓";
            btn.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 21, 128, 61));

            _ = System.Threading.Tasks.Task.Delay(900).ContinueWith(_ =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    btn.Content = original;
                    btn.Background = originalBg;
                });
            });
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not Snippet s) return;
            s.IsPinned = !s.IsPinned;
            if (s.IsPinned) s.Order = NextTopOrder();
            Save();
            ApplyFilter();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not Snippet s) return;

            if (s.IsDeleted || s.IsAutoCaptured || _categoryFilter == "History")
            {
                _allSnippets.Remove(s);
            }
            else
            {
                s.IsDeleted = true;
            }

            Save();
            RebuildCategoryFilter();
            ApplyFilter();
        }

        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not Snippet s) return;
            await ShowSnippetDialog(s);
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            await ShowSnippetDialog(null);
        }

        private async System.Threading.Tasks.Task ShowSnippetDialog(Snippet? existing)
        {
            var textBox = new TextBox
            {
                Text = existing?.Text ?? "",
                PlaceholderText = "Enter text to save…",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 220,
                MaxHeight = 420,
                Width = 420,
                Margin = new Thickness(0, 8, 0, 8)
            };
            ScrollViewer.SetVerticalScrollBarVisibility(textBox, ScrollBarVisibility.Auto);

            var categoryBox = new TextBox
            {
                Text = existing?.Category ?? "",
                PlaceholderText = "Category (optional, e.g. Work, Code)",
                Width = 420,
                Margin = new Thickness(0, 0, 0, 0)
            };

            var stack = new StackPanel { Spacing = 8, Width = 420 };
            stack.Children.Add(textBox);
            stack.Children.Add(categoryBox);

            var dialog = new ContentDialog
            {
                Title = existing == null ? "Add Snippet" : "Edit Snippet",
                PrimaryButtonText = existing == null ? "Add" : "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot,
                Content = stack
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;
            if (string.IsNullOrWhiteSpace(textBox.Text)) return;

            if (existing != null)
            {
                existing.Text = textBox.Text;
                existing.Category = categoryBox.Text?.Trim() ?? "";
            }
            else
            {
                _allSnippets.Add(new Snippet
                {
                    Text = textBox.Text,
                    Category = categoryBox.Text?.Trim() ?? "",
                    Order = NextTopOrder()
                });
            }

            Save();
            RebuildCategoryFilter();
            ApplyFilter();
        }

        private void SnippetsList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            for (int i = 0; i < _displayedSnippets.Count; i++)
            {
                _displayedSnippets[i].Order = i;
            }

            var visibleIds = _displayedSnippets.Select(s => s.Id).ToHashSet();
            int next = _displayedSnippets.Count;
            foreach (var s in _allSnippets)
            {
                if (visibleIds.Contains(s.Id)) continue;
                s.Order = next++;
            }

            Save();
        }

        private void AutoCaptureToggle_Click(object sender, RoutedEventArgs e)
        {
            _settings.AutoCapture = AutoCaptureToggle.IsChecked;
            SnippetStorage.SaveSettings(_settings);
        }

        private void HistoryCap50_Click(object sender, RoutedEventArgs e) => SetCap(50);
        private void HistoryCap100_Click(object sender, RoutedEventArgs e) => SetCap(100);
        private void HistoryCapUnlimited_Click(object sender, RoutedEventArgs e) => SetCap(0);

        private void SetCap(int cap)
        {
            _settings.HistoryCap = cap;
            SnippetStorage.SaveSettings(_settings);
            EnforceCap();
            Save();
            ApplyFilter();
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileSavePicker();
                picker.SuggestedFileName = "clipora-export";
                picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSaveFileAsync();
                if (file == null) return;

                await FileIO.WriteTextAsync(file, SnippetStorage.ExportJson(_allSnippets));
            }
            catch (Exception ex)
            {
                await ShowError("Export failed", ex.Message);
            }
        }

        private async void Import_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".json");

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSingleFileAsync();
                if (file == null) return;

                var json = await FileIO.ReadTextAsync(file);
                var imported = SnippetStorage.ImportJson(json);
                if (imported == null)
                {
                    await ShowError("Import failed", "Could not parse the selected file.");
                    return;
                }

                var existingIds = _allSnippets.Select(s => s.Id).ToHashSet();
                var existingTexts = _allSnippets.Select(s => s.Text).ToHashSet();

                int top = NextTopOrder();
                foreach (var s in imported)
                {
                    if (existingIds.Contains(s.Id)) continue;
                    if (existingTexts.Contains(s.Text)) continue;
                    s.Order = --top;
                    _allSnippets.Add(s);
                }

                Save();
                RebuildCategoryFilter();
                ApplyFilter();
            }
            catch (Exception ex)
            {
                await ShowError("Import failed", ex.Message);
            }
        }

        private async void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Clear history?",
                Content = "This removes all auto-captured snippets. Pinned and manually added snippets are kept.",
                PrimaryButtonText = "Clear",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            var toRemove = _allSnippets.Where(s => s.IsAutoCaptured && !s.IsPinned).ToList();
            foreach (var s in toRemove) _allSnippets.Remove(s);

            Save();
            RebuildCategoryFilter();
            ApplyFilter();
        }

        private async System.Threading.Tasks.Task ShowError(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
