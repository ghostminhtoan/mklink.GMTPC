using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using MKLink.Models;

namespace MKLink
{
    public partial class CheckMklinkWindow : Window, INotifyPropertyChanged
    {
        private readonly Func<IEnumerable<MklinkCheckEntry>> _entriesProvider;
        private readonly Action<List<int>> _deleteRowsBySourceIndices;

        public CheckMklinkWindow(
            Func<IEnumerable<MklinkCheckEntry>> entriesProvider,
            Action<List<int>> deleteRowsBySourceIndices)
        {
            InitializeComponent();
            _entriesProvider = entriesProvider;
            _deleteRowsBySourceIndices = deleteRowsBySourceIndices;
            Entries = new ObservableCollection<MklinkCheckEntry>();
            DataContext = this;
            RefreshFromSource();
        }

        public ObservableCollection<MklinkCheckEntry> Entries { get; private set; }
        public event PropertyChangedEventHandler PropertyChanged;

        public string SummaryText
        {
            get
            {
                int linkCount = 0;
                int missingCount = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    if (Entries[i].IsLink)
                    {
                        linkCount++;
                    }
                    else if (!Entries[i].Exists)
                    {
                        missingCount++;
                    }
                }

                return string.Format("Tổng: {0} | Folder thật: {1} | Shortcut mklink: {2} | Không tồn tại: {3}",
                    Entries.Count,
                    Entries.Count - linkCount - missingCount,
                    linkCount,
                    missingCount);
            }
        }

        public void RefreshFromSource()
        {
            IEnumerable<MklinkCheckEntry> entries = _entriesProvider != null
                ? _entriesProvider()
                : Enumerable.Empty<MklinkCheckEntry>();

            Entries.Clear();
            foreach (MklinkCheckEntry entry in entries)
            {
                Entries.Add(entry);
            }

            OnPropertyChanged("SummaryText");
        }

        private void SelectByPredicate(Func<MklinkCheckEntry, bool> predicate)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                Entries[i].IsSelected = predicate(Entries[i]);
            }
        }

        private List<int> CollectSelectedSourceIndices()
        {
            var indices = new List<int>();
            for (int i = 0; i < Entries.Count; i++)
            {
                MklinkCheckEntry entry = Entries[i];
                if (entry.IsSelected && entry.SourceIndex >= 0 && !indices.Contains(entry.SourceIndex))
                {
                    indices.Add(entry.SourceIndex);
                }
            }

            return indices;
        }

        private void SelectMklinkButton_Click(object sender, RoutedEventArgs e)
        {
            SelectByPredicate(x => x.IsLink);
        }

        private void SelectRealFolderButton_Click(object sender, RoutedEventArgs e)
        {
            SelectByPredicate(x => x.Exists && !x.IsLink);
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            SelectByPredicate(x => true);
        }

        private void SelectNoneButton_Click(object sender, RoutedEventArgs e)
        {
            SelectByPredicate(x => false);
        }

        private void InvertSelectButton_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                Entries[i].IsSelected = !Entries[i].IsSelected;
            }
        }

        private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            List<int> indices = CollectSelectedSourceIndices();
            if (indices.Count == 0 || _deleteRowsBySourceIndices == null)
            {
                return;
            }

            _deleteRowsBySourceIndices(indices);
            RefreshFromSource();
        }

        private void DeleteAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_deleteRowsBySourceIndices == null || Entries.Count == 0)
            {
                return;
            }

            var allIndices = new List<int>();
            for (int i = 0; i < Entries.Count; i++)
            {
                int sourceIndex = Entries[i].SourceIndex;
                if (sourceIndex >= 0 && !allIndices.Contains(sourceIndex))
                {
                    allIndices.Add(sourceIndex);
                }
            }

            if (allIndices.Count == 0)
            {
                return;
            }

            _deleteRowsBySourceIndices(allIndices);
            RefreshFromSource();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
