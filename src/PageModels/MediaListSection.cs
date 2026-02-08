using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AniSprinkles.PageModels
{
    public class MediaListSection : ObservableCollection<MediaListEntry>
    {
        private readonly List<MediaListEntry> _allItems = [];
        private bool _isExpanded;
        private bool _suppressNotifications;

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

        public void AddItems(IEnumerable<MediaListEntry> entries)
        {
            var list = entries as IList<MediaListEntry> ?? entries.ToList();
            if (list.Count == 0)
                return;

            _allItems.AddRange(list);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(TotalCount)));

            if (IsExpanded)
            {
                ReplaceItems(_allItems);
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
                ReplaceItems(_allItems);
            }
            else
            {
                Clear();
            }
        }

        private void ReplaceItems(IEnumerable<MediaListEntry> items)
        {
            _suppressNotifications = true;
            ClearItems();
            foreach (var entry in items)
            {
                Add(entry);
            }
            _suppressNotifications = false;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (_suppressNotifications)
                return;

            base.OnCollectionChanged(e);
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (_suppressNotifications)
                return;

            base.OnPropertyChanged(e);
        }
    }
}
