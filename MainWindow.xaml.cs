// MainWindow.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using System;
using System.Collections.Generic;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.UI;

namespace Clipora
{
    public sealed partial class MainWindow : Window
    {
        private readonly ApplicationDataContainer localSettings;
        private const string SNIPPETS_KEY = "SavedSnippets";

        private bool _didPlaceWindow;

        public MainWindow()
        {
            InitializeComponent();

            SetWindowIcon();

            localSettings = ApplicationData.Current.LocalSettings;

            Activated += OnActivatedPlaceWindow;

            LoadSnippets();
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
                PlaceWindowOnVisibleArea(width: 400, height: 600, margin: 12);
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

            // Clamp size to working area.
            int w = Math.Min(width, Math.Max(200, wa.Width - margin * 2));
            int h = Math.Min(height, Math.Max(200, wa.Height - margin * 2));

            appWindow.Resize(new SizeInt32(w, h));

            int x = wa.X + wa.Width - w - margin;
            int y = wa.Y + margin;

            // Clamp position.
            x = Clamp(x, wa.X + margin, wa.X + wa.Width - w - margin);
            y = Clamp(y, wa.Y + margin, wa.Y + wa.Height - h - margin);

            appWindow.Move(new PointInt32(x, y));
        }

        private static int Clamp(int v, int min, int max)
            => (v < min) ? min : (v > max) ? max : v;

        private void LoadSnippets()
        {
            var snippetsData = localSettings.Values[SNIPPETS_KEY] as string;

            if (!string.IsNullOrEmpty(snippetsData))
            {
                var snippets = snippetsData.Split(new[] { "|||" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var snippet in snippets)
                {
                    AddSnippetToUI(snippet);
                }
            }
            else
            {
                AddSnippetToUI("Welcome to Clipboard Widget!");
                AddSnippetToUI("Click the + button to add new snippets");
                SaveSnippets();
            }
        }

        private void SaveSnippets()
        {
            var snippets = new List<string>();

            foreach (var child in SnippetsPanel.Children)
            {
                if (child is Border border && border.Tag is string text)
                {
                    snippets.Add(text);
                }
            }

            localSettings.Values[SNIPPETS_KEY] = string.Join("|||", snippets);
        }

        private void AddSnippetToUI(string text)
        {
            var snippetBorder = new Border
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 45, 45, 48)),
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 63, 63, 70)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Tag = text
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // copy
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // edit
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // delete

            var textBlock = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(textBlock, 0);

            var copyButton = new Button
            {
                Content = "📋",
                FontSize = 16,
                Width = 36,
                Height = 36,
                Margin = new Thickness(4, 0, 4, 0),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 14, 116, 144)),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6)
            };
            // IMPORTANT: copy the current value from Tag, not the original captured "text"
            copyButton.Click += (s, e) =>
                CopyToClipboard(snippetBorder.Tag as string ?? "", copyButton);
            Grid.SetColumn(copyButton, 1);

            var editButton = new Button
            {
                Content = "✏️",
                FontSize = 16,
                Width = 36,
                Height = 36,
                Margin = new Thickness(4, 0, 4, 0),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 100, 100, 100)),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6)
            };
            editButton.Click += (s, e) => EditSnippet(snippetBorder, textBlock);
            Grid.SetColumn(editButton, 2);

            var deleteButton = new Button
            {
                Content = "🗑️",
                FontSize = 16,
                Width = 36,
                Height = 36,
                Margin = new Thickness(4, 0, 4, 0),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 185, 28, 28)),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6)
            };
            deleteButton.Click += (s, e) => DeleteSnippet(snippetBorder);
            Grid.SetColumn(deleteButton, 3);

            grid.Children.Add(textBlock);
            grid.Children.Add(copyButton);
            grid.Children.Add(editButton);
            grid.Children.Add(deleteButton);

            snippetBorder.Child = grid;
            SnippetsPanel.Children.Add(snippetBorder);
        }
        private async void CopyToClipboard(string text, Button button)
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(text);
            Clipboard.SetContent(dataPackage);

            var originalContent = button.Content;
            button.Content = "✓";
            button.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 21, 128, 61));

            await System.Threading.Tasks.Task.Delay(1000);

            button.Content = originalContent;
            button.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 14, 116, 144));
        }

        private void DeleteSnippet(Border snippet)
        {
            SnippetsPanel.Children.Remove(snippet);
            SaveSnippets();
        }

        private async void EditSnippet(Border snippetBorder, TextBlock textBlock)
        {
            var currentText = snippetBorder.Tag as string ?? "";

            var dialog = new ContentDialog
            {
                Title = "Edit Snippet",
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var textBox = new TextBox
            {
                Text = currentText,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 120,
                Margin = new Thickness(0, 8, 0, 0)
            };

            dialog.Content = textBox;

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                // Update UI + stored value
                textBlock.Text = textBox.Text;
                snippetBorder.Tag = textBox.Text;

                SaveSnippets();
            }
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Add New Snippet",
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var textBox = new TextBox
            {
                PlaceholderText = "Enter text to save...",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 120,
                Margin = new Thickness(0, 8, 0, 0)
            };

            dialog.Content = textBox;

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                AddSnippetToUI(textBox.Text);
                SaveSnippets();
            }
        }
    }
}