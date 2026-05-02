using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;

namespace Clipora
{
    public sealed class Snippet : INotifyPropertyChanged
    {
        private string _text = "";
        private bool _isPinned;
        private string _category = "";

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Text
        {
            get => _text;
            set { if (_text == value) return; _text = value; OnChanged(); }
        }

        public bool IsPinned
        {
            get => _isPinned;
            set
            {
                if (_isPinned == value) return;
                _isPinned = value;
                OnChanged();
                OnChanged(nameof(PinIcon));
                OnChanged(nameof(PinBackground));
            }
        }

        public string Category
        {
            get => _category;
            set
            {
                if (_category == value) return;
                _category = value;
                OnChanged();
                OnChanged(nameof(HasCategory));
                OnChanged(nameof(CategoryVisibility));
            }
        }

        public bool IsAutoCaptured { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public int Order { get; set; }

        [JsonIgnore]
        public string PinIcon => _isPinned ? "📌" : "📍";

        [JsonIgnore]
        public string PinBackground => _isPinned ? "#CA8A04" : "#3F3F46";

        [JsonIgnore]
        public bool HasCategory => !string.IsNullOrEmpty(_category);

        [JsonIgnore]
        public Visibility CategoryVisibility => HasCategory ? Visibility.Visible : Visibility.Collapsed;

        [field: JsonIgnore]
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnChanged([CallerMemberName] string? prop = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
