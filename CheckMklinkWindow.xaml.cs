using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MKLink.Models;

namespace MKLink
{
    public partial class CheckMklinkWindow : Window, INotifyPropertyChanged
    {
        private readonly Func<IEnumerable<MklinkCheckEntry>> _entriesProvider;
        private readonly Action<List<int>> _deleteRowsBySourceIndices;
        private readonly Action<List<MklinkCheckEntry>> _makeMklinkCopyAction;
        private readonly Action<List<MklinkCheckEntry>> _makeMklinkNoCopyAction;
        private readonly Action<List<MklinkCheckEntry>> _makeReverseAction;
        private readonly Action<List<MklinkCheckEntry>> _moveToDestinationAction;
        private StatusFilterKind _statusFilter = StatusFilterKind.All;
        private DestinationFilterKind _destinationFilter = DestinationFilterKind.All;
        private List<MklinkCheckEntry> _contextMenuCopySnapshot = new List<MklinkCheckEntry>();

        public CheckMklinkWindow(
            Func<IEnumerable<MklinkCheckEntry>> entriesProvider,
            Action<List<int>> deleteRowsBySourceIndices,
            Action<List<MklinkCheckEntry>> makeMklinkCopyAction,
            Action<List<MklinkCheckEntry>> makeMklinkNoCopyAction,
            Action<List<MklinkCheckEntry>> makeReverseAction,
            Action<List<MklinkCheckEntry>> moveToDestinationAction)
        {
            InitializeComponent();
            _entriesProvider = entriesProvider;
            _deleteRowsBySourceIndices = deleteRowsBySourceIndices;
            _makeMklinkCopyAction = makeMklinkCopyAction;
            _makeMklinkNoCopyAction = makeMklinkNoCopyAction;
            _makeReverseAction = makeReverseAction;
            _moveToDestinationAction = moveToDestinationAction;
            Entries = new ObservableCollection<MklinkCheckEntry>();
            EntriesView = CollectionViewSource.GetDefaultView(Entries);
            EntriesView.Filter = FilterEntry;
            DataContext = this;
            RefreshFromSource();
        }

        public ObservableCollection<MklinkCheckEntry> Entries { get; private set; }
        public ICollectionView EntriesView { get; private set; }
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

            if (EntriesView != null)
            {
                EntriesView.Refresh();
            }

            OnPropertyChanged("SummaryText");
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshFromSource();
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

        private List<MklinkCheckEntry> CollectSelectedEntries()
        {
            var selectedEntries = new List<MklinkCheckEntry>();
            for (int i = 0; i < Entries.Count; i++)
            {
                MklinkCheckEntry entry = Entries[i];
                if (entry != null && entry.IsSelected)
                {
                    selectedEntries.Add(entry);
                }
            }

            return selectedEntries;
        }

        private List<MklinkCheckEntry> CollectEntriesForContextCopy()
        {
            if (_contextMenuCopySnapshot != null && _contextMenuCopySnapshot.Count > 0)
            {
                return new List<MklinkCheckEntry>(_contextMenuCopySnapshot);
            }

            var selectedEntries = new List<MklinkCheckEntry>();
            if (CheckGrid != null && CheckGrid.SelectedItems.Count > 0)
            {
                for (int i = 0; i < CheckGrid.SelectedItems.Count; i++)
                {
                    MklinkCheckEntry entry = CheckGrid.SelectedItems[i] as MklinkCheckEntry;
                    if (entry != null && !selectedEntries.Contains(entry))
                    {
                        selectedEntries.Add(entry);
                    }
                }
            }

            if (selectedEntries.Count > 0)
            {
                return selectedEntries;
            }

            return CollectSelectedEntries();
        }

        private List<MklinkCheckEntry> CollectGridSelectedEntries(DataGrid grid)
        {
            var selectedEntries = new List<MklinkCheckEntry>();
            if (grid == null)
            {
                return selectedEntries;
            }

            for (int i = 0; i < grid.SelectedItems.Count; i++)
            {
                MklinkCheckEntry entry = grid.SelectedItems[i] as MklinkCheckEntry;
                if (entry != null && !selectedEntries.Contains(entry))
                {
                    selectedEntries.Add(entry);
                }
            }

            return selectedEntries;
        }

        private void SelectMissingButton_Click(object sender, RoutedEventArgs e)
        {
            SelectByPredicate(x => x != null && !x.Exists);
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

        private void CheckSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            List<MklinkCheckEntry> selectedEntries = CollectSelectedEntries();
            if (selectedEntries.Count == 0)
            {
                return;
            }

            for (int i = 0; i < selectedEntries.Count; i++)
            {
                selectedEntries[i].IsSelected = true;
            }
        }

        private void UncheckSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            List<MklinkCheckEntry> selectedEntries = CollectSelectedEntries();
            if (selectedEntries.Count == 0)
            {
                return;
            }

            for (int i = 0; i < selectedEntries.Count; i++)
            {
                selectedEntries[i].IsSelected = false;
            }
        }

        private void UncheckAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (Entries.Count == 0)
            {
                return;
            }

            for (int i = 0; i < Entries.Count; i++)
            {
                Entries[i].IsSelected = false;
            }
        }

        private void DeleteSelectedLinesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_deleteRowsBySourceIndices == null)
            {
                return;
            }

            List<int> indices = CollectSelectedSourceIndices();
            if (indices.Count == 0)
            {
                return;
            }

            _deleteRowsBySourceIndices(indices);
            RefreshFromSource();
        }

        private void DeleteAllLinesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_deleteRowsBySourceIndices == null)
            {
                return;
            }

            var indices = new List<int>();
            for (int i = 0; i < Entries.Count; i++)
            {
                int sourceIndex = Entries[i].SourceIndex;
                if (sourceIndex >= 0 && !indices.Contains(sourceIndex))
                {
                    indices.Add(sourceIndex);
                }
            }

            if (indices.Count == 0)
            {
                return;
            }

            _deleteRowsBySourceIndices(indices);
            RefreshFromSource();
        }

        private void MakeMklinkCopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_makeMklinkCopyAction == null)
            {
                return;
            }

            List<MklinkCheckEntry> selectedEntries = CollectSelectedEntries();
            if (selectedEntries.Count == 0)
            {
                return;
            }

            _makeMklinkCopyAction(selectedEntries);
        }

        private void MakeMklinkNoCopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_makeMklinkNoCopyAction == null)
            {
                return;
            }

            List<MklinkCheckEntry> selectedEntries = CollectSelectedEntries();
            if (selectedEntries.Count == 0)
            {
                return;
            }

            _makeMklinkNoCopyAction(selectedEntries);
        }

        private void MakeReverseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_makeReverseAction == null)
            {
                return;
            }

            List<MklinkCheckEntry> selectedEntries = CollectSelectedEntries();
            if (selectedEntries.Count == 0)
            {
                return;
            }

            _makeReverseAction(selectedEntries);
        }

        private void MoveToDestinationButton_Click(object sender, RoutedEventArgs e)
        {
            if (_moveToDestinationAction == null)
            {
                return;
            }

            List<MklinkCheckEntry> selectedEntries = CollectSelectedEntries();
            if (selectedEntries.Count == 0)
            {
                return;
            }

            _moveToDestinationAction(selectedEntries);
        }

        private void CopySelectedNormalizeInput_Click(object sender, RoutedEventArgs e)
        {
            CopySelectedField(entry => entry != null ? entry.NormalizedInput : string.Empty);
        }

        private void CopySelectedOutput_Click(object sender, RoutedEventArgs e)
        {
            CopySelectedField(entry => entry != null ? entry.OutputPath : string.Empty);
        }

        private void CopySelectedAlreadyMklinkTo_Click(object sender, RoutedEventArgs e)
        {
            CopySelectedField(entry => entry != null ? entry.AlreadyMklinkTo : string.Empty);
        }

        private void CopySelectedField(Func<MklinkCheckEntry, string> selector)
        {
            if (selector == null)
            {
                return;
            }

            List<MklinkCheckEntry> selectedEntries = CollectEntriesForContextCopy();
            if (selectedEntries.Count == 0)
            {
                return;
            }

            var lines = new List<string>();
            for (int i = 0; i < selectedEntries.Count; i++)
            {
                string value = selector(selectedEntries[i]);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    lines.Add(value);
                }
            }

            if (lines.Count == 0)
            {
                _contextMenuCopySnapshot = new List<MklinkCheckEntry>();
                return;
            }

            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            _contextMenuCopySnapshot = new List<MklinkCheckEntry>();
        }

        private void CheckGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var grid = sender as DataGrid;
            if (grid == null)
            {
                return;
            }

            DependencyObject source = e.OriginalSource as DependencyObject;
            DataGridRow row = FindParent<DataGridRow>(source);
            if (row == null)
            {
                _contextMenuCopySnapshot = CollectSelectedEntries();
                return;
            }

            if (grid.SelectedItems.Count > 0)
            {
                _contextMenuCopySnapshot = new List<MklinkCheckEntry>();
                foreach (object candidate in grid.SelectedItems)
                {
                    MklinkCheckEntry entry = candidate as MklinkCheckEntry;
                    if (entry != null && !_contextMenuCopySnapshot.Contains(entry))
                    {
                        _contextMenuCopySnapshot.Add(entry);
                    }
                }
            }
            else
            {
                _contextMenuCopySnapshot = new List<MklinkCheckEntry>();
                var rowEntry = row.Item as MklinkCheckEntry;
                if (rowEntry != null)
                {
                    _contextMenuCopySnapshot.Add(rowEntry);
                }
            }

            grid.Focus();
        }

        private void CheckGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space)
            {
                return;
            }

            var grid = sender as DataGrid;
            if (grid == null)
            {
                return;
            }

            if (IsTextEditingContext(e.OriginalSource))
            {
                return;
            }

            List<MklinkCheckEntry> selectedEntries = CollectGridSelectedEntries(grid);
            if (selectedEntries.Count == 0)
            {
                var focusedEntry = grid.SelectedItem as MklinkCheckEntry;
                if (focusedEntry != null)
                {
                    selectedEntries.Add(focusedEntry);
                }
            }

            if (selectedEntries.Count == 0)
            {
                return;
            }

            bool anyUnchecked = false;
            for (int i = 0; i < selectedEntries.Count; i++)
            {
                if (!selectedEntries[i].IsSelected)
                {
                    anyUnchecked = true;
                    break;
                }
            }

            bool newValue = anyUnchecked;
            for (int i = 0; i < selectedEntries.Count; i++)
            {
                selectedEntries[i].IsSelected = newValue;
            }

            e.Handled = true;
        }

        private static bool IsTextEditingContext(object originalSource)
        {
            DependencyObject current = originalSource as DependencyObject;
            while (current != null)
            {
                if (current is TextBox)
                {
                    return true;
                }

                DependencyObject parent = VisualTreeHelper.GetParent(current);
                if (parent == null)
                {
                    parent = LogicalTreeHelper.GetParent(current);
                }

                current = parent;
            }

            return false;
        }

        private void DeleteCopyRowsBySourceIndices(List<int> sourceIndices)
        {
            if (_deleteRowsBySourceIndices == null || sourceIndices == null || sourceIndices.Count == 0)
            {
                return;
            }

            _deleteRowsBySourceIndices(sourceIndices);
            RefreshFromSource();
        }

        private void StatusHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as FrameworkElement;
            if (button == null)
            {
                return;
            }

            ContextMenu menu = BuildStatusFilterMenu();
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void DestinationHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as FrameworkElement;
            if (button == null)
            {
                return;
            }

            ContextMenu menu = BuildDestinationFilterMenu();
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private ContextMenu BuildStatusFilterMenu()
        {
            var menu = BuildFilterMenuBase();
            string groupName = "StatusFilterGroup";
            menu.Items.Add(CreateFilterRadioItem("All", _statusFilter == StatusFilterKind.All, groupName, delegate
            {
                SetStatusFilter(StatusFilterKind.All);
            }, menu));
            menu.Items.Add(CreateFilterRadioItem("MKLINK", _statusFilter == StatusFilterKind.Mklink, groupName, delegate
            {
                SetStatusFilter(StatusFilterKind.Mklink);
            }, menu));
            menu.Items.Add(CreateFilterRadioItem("Real folder", _statusFilter == StatusFilterKind.RealFolder, groupName, delegate
            {
                SetStatusFilter(StatusFilterKind.RealFolder);
            }, menu));
            menu.Items.Add(CreateFilterRadioItem("Missing", _statusFilter == StatusFilterKind.Missing, groupName, delegate
            {
                SetStatusFilter(StatusFilterKind.Missing);
            }, menu));
            return menu;
        }

        private ContextMenu BuildDestinationFilterMenu()
        {
            var menu = BuildFilterMenuBase();
            string groupName = "DestinationFilterGroup";
            menu.Items.Add(CreateFilterRadioItem("All", _destinationFilter == DestinationFilterKind.All, groupName, delegate
            {
                SetDestinationFilter(DestinationFilterKind.All);
            }, menu));
            menu.Items.Add(CreateFilterRadioItem("Checked", _destinationFilter == DestinationFilterKind.Checked, groupName, delegate
            {
                SetDestinationFilter(DestinationFilterKind.Checked);
            }, menu));
            menu.Items.Add(CreateFilterRadioItem("Unchecked", _destinationFilter == DestinationFilterKind.Unchecked, groupName, delegate
            {
                SetDestinationFilter(DestinationFilterKind.Unchecked);
            }, menu));
            return menu;
        }

        private ContextMenu BuildFilterMenuBase()
        {
            var menu = new ContextMenu();
            menu.Background = (System.Windows.Media.Brush)FindResource("PanelBrush");
            menu.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
            menu.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrushDark");
            menu.BorderThickness = new Thickness(1);
            menu.Padding = new Thickness(4);
            menu.StaysOpen = false;
            return menu;
        }

        private MenuItem CreateFilterRadioItem(string header, bool isChecked, string groupName, Action onSelected, ContextMenu ownerMenu)
        {
            var radio = new RadioButton
            {
                Content = header,
                GroupName = groupName,
                IsChecked = isChecked,
                MinWidth = 180,
                Padding = new Thickness(10, 6, 10, 6),
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = (System.Windows.Media.Brush)FindResource("TextBrush")
            };

            radio.Style = BuildFilterRadioStyle();
            radio.Checked += delegate
            {
                onSelected();
                if (ownerMenu != null)
                {
                    ownerMenu.IsOpen = false;
                }
            };

            radio.Click += delegate
            {
                onSelected();
                if (ownerMenu != null)
                {
                    ownerMenu.IsOpen = false;
                }
            };

            return new MenuItem
            {
                Header = radio,
                Padding = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent
            };
        }

        private Style BuildFilterRadioStyle()
        {
            var style = new Style(typeof(RadioButton));
            style.Setters.Add(new Setter(Control.BackgroundProperty, System.Windows.Media.Brushes.Transparent));
            style.Setters.Add(new Setter(Control.ForegroundProperty, FindResource("TextBrush")));
            style.Setters.Add(new Setter(Control.SnapsToDevicePixelsProperty, true));

            var trigger = new Trigger
            {
                Property = UIElement.IsMouseOverProperty,
                Value = true
            };
            trigger.Setters.Add(new Setter(Control.BackgroundProperty, FindResource("AccentBrush")));
            trigger.Setters.Add(new Setter(Control.ForegroundProperty, System.Windows.Media.Brushes.White));
            style.Triggers.Add(trigger);

            var checkedTrigger = new Trigger
            {
                Property = RadioButton.IsCheckedProperty,
                Value = true
            };
            checkedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, FindResource("AccentBrush")));
            checkedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, System.Windows.Media.Brushes.White));
            style.Triggers.Add(checkedTrigger);

            return style;
        }

        private void SetStatusFilter(StatusFilterKind filter)
        {
            _statusFilter = filter;
            if (EntriesView != null)
            {
                EntriesView.Refresh();
            }
        }

        private void SetDestinationFilter(DestinationFilterKind filter)
        {
            _destinationFilter = filter;
            if (EntriesView != null)
            {
                EntriesView.Refresh();
            }
        }

        private bool FilterEntry(object value)
        {
            var entry = value as MklinkCheckEntry;
            if (entry == null)
            {
                return false;
            }

            return MatchesStatusFilter(entry) && MatchesDestinationFilter(entry);
        }

        private bool MatchesStatusFilter(MklinkCheckEntry entry)
        {
            if (_statusFilter == StatusFilterKind.All)
            {
                return true;
            }

            if (_statusFilter == StatusFilterKind.Mklink)
            {
                return entry.IsLink;
            }

            if (_statusFilter == StatusFilterKind.RealFolder)
            {
                return entry.Exists && !entry.IsLink;
            }

            return !entry.Exists;
        }

        private bool MatchesDestinationFilter(MklinkCheckEntry entry)
        {
            if (_destinationFilter == DestinationFilterKind.All)
            {
                return true;
            }

            if (_destinationFilter == DestinationFilterKind.Checked)
            {
                return entry.DestinationCheck;
            }

            return !entry.DestinationCheck;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject current = child;
            while (current != null)
            {
                if (current is T)
                {
                    return (T)current;
                }

                DependencyObject parent = VisualTreeHelper.GetParent(current);
                if (parent == null)
                {
                    parent = LogicalTreeHelper.GetParent(current);
                }

                current = parent;
            }

            return null;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private enum StatusFilterKind
        {
            All,
            Mklink,
            RealFolder,
            Missing
        }

        private enum DestinationFilterKind
        {
            All,
            Checked,
            Unchecked
        }
    }
}
