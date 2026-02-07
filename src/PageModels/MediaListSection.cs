using System.Collections.ObjectModel;
using System.ComponentModel;
using AniSprinkles.Models;
using CommunityToolkit.Mvvm.Input;

namespace AniSprinkles.PageModels
{
    public class MediaListSection : ObservableCollection<MediaListEntry>
    {
        private readonly List<MediaListEntry> _allItems = [];
        private bool _isExpanded;

        public MediaListSection(string title, bool isExpanded)
        {
            Title = title;
            _isExpanded = isExpanded;
            ToggleCommand = new RelayCommand(ToggleExpanded);
        }

        public string Title { get; }

        public int TotalCount => _allItems.Count;

        public IRelayCommand ToggleCommand { get; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                    return;

                _isExpanded = value;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsExpanded)));
                UpdateItems();
            }
        }

        public void AddItem(MediaListEntry entry)
        {
            _allItems.Add(entry);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(TotalCount)));

            if (IsExpanded)
            {
                Add(entry);
            }
        }

        private void ToggleExpanded()
        {
            IsExpanded = !IsExpanded;
        }

        private void UpdateItems()
        {
            if (IsExpanded)
            {
                foreach (var entry in _allItems)
                {
                    Add(entry);
                }
            }
            else
            {
                Clear();
            }
        }
    }
}
