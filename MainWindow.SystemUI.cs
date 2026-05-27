using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Linq;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using MKLink.Models;
using MKLink.ViewModels;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace MKLink
{
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;
        private bool _startupPromptShown;
        private CheckMklinkWindow _checkMklinkWindow;
        private readonly object _launchedProcessLock = new object();
        private readonly List<Process> _launchedProcesses = new List<Process>();
        private bool _isClosing;
        private bool _isSyncingLinkedSelection;
        private string _currentMarkdownFilePath;

        public MainWindow()
        {
            InitializeComponent();
            InitializeSystemUI();
            InitializeTabCopy();
            InitializeTabDelete();
            InitializeTabMKLink();
            InitializeTabReverse();
            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;
            Loaded += MainWindow_Loaded;
        }

        // Quan ly binding UI chinh cua window.
        private void InitializeSystemUI()
        {
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            Title = "MKLink Path Mapper | Build: " + GetBuildTimeText();

            _checkMklinkWindow = new CheckMklinkWindow(
                BuildMklinkCheckEntries,
                DeleteCopyRowsBySourceIndices,
                RunCopyDeleteMklinkFromCheckWindow,
                RunDeleteMklinkFromCheckWindow,
                MakeReverseFromCheckWindow,
                MoveToDestinationFromCheckWindow);
            CheckMklinkTab.Content = _checkMklinkWindow;

            _checkMklinkWindow.FilterCleared += CheckMklinkWindow_FilterCleared;

            _viewModel.CopyTab.DataChanged += CopyTab_DataChanged_ForCheckWindow;

            if (SearchBox != null)
            {
                _checkMklinkWindow.UpdateSearchFilter(SearchBox.Text);
            }
        }

        private static string GetBuildTimeText()
        {
            try
            {
                string location = System.Reflection.Assembly.GetExecutingAssembly().Location;
                DateTime buildTime = File.GetLastWriteTime(location);
                return buildTime.ToString("yyyy-MM-dd hh.mm.ss tt dddd", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return "unknown";
            }
        }

        private void BrowseRootFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            var tab = element != null ? element.DataContext as PathTabViewModel : null;
            if (tab == null || tab.IsMirrorTab)
            {
                return;
            }

            string pickedPath;
            if (PickFolderVista("Chọn thư mục cần symlink", tab.SelectedRootFolder, out pickedPath))
            {
                tab.SetSelectedRootFolder(pickedPath);
                tab.ApplyTargetCommand.Execute(null);
            }
        }

        private void HandleEditOutput(PathItem item, DependencyObject visualElement)
        {
            if (item == null)
            {
                return;
            }

            DataGrid grid = null;
            if (visualElement is MenuItem menuItem)
            {
                var contextMenu = menuItem.Parent as ContextMenu;
                var row = contextMenu != null ? contextMenu.PlacementTarget as DataGridRow : null;
                grid = row != null ? FindParent<DataGrid>(row) : null;
            }
            else
            {
                grid = FindParent<DataGrid>(visualElement);
            }

            string initialPath = item.EditOutputPath;
            if (string.IsNullOrWhiteSpace(initialPath))
            {
                initialPath = item.MappedTarget;
            }

            if (!PickFolderVista("Chọn thư mục output đã chỉnh", initialPath, out var pickedPath))
            {
                return;
            }

            if (_viewModel != null)
            {
                _viewModel.CaptureUndoSnapshot();
            }

            item.EditOutputPath = pickedPath;
            if (grid != null)
            {
                RefreshEditOutputGridFilter(grid);
            }
        }

        private void HandleRestoreDefault(PathItem item, DependencyObject visualElement)
        {
            if (item == null)
            {
                return;
            }

            DataGrid grid = null;
            if (visualElement is MenuItem menuItem)
            {
                var contextMenu = menuItem.Parent as ContextMenu;
                var row = contextMenu != null ? contextMenu.PlacementTarget as DataGridRow : null;
                grid = row != null ? FindParent<DataGrid>(row) : null;
            }
            else
            {
                grid = FindParent<DataGrid>(visualElement);
            }

            if (_viewModel != null)
            {
                _viewModel.CaptureUndoSnapshot();
            }

            item.EditOutputPath = string.Empty;
            if (grid != null)
            {
                RefreshEditOutputGridFilter(grid);
            }
        }

        private void RowContextMenuEdit_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var item = menuItem != null ? menuItem.DataContext as PathItem : null;
            HandleEditOutput(item, menuItem);
        }

        private void RowContextMenuRestore_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var item = menuItem != null ? menuItem.DataContext as PathItem : null;
            HandleRestoreDefault(item, menuItem);
        }

        private void OutputGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = sender as DataGridRow;
            var item = row != null ? row.DataContext as PathItem : null;
            if (item == null || item.IsPlaceholder)
            {
                return;
            }
            HandleEditOutput(item, row);
        }

        private void OutputGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true; // Prevent default sorting

            if (e.Column.Header != null && e.Column.Header.ToString() == "Edit Output")
            {
                var grid = sender as DataGrid;
                var tab = grid?.DataContext as PathTabViewModel;
                if (tab == null || tab.SourceItems == null)
                {
                    return;
                }

                var direction = e.Column.SortDirection == ListSortDirection.Descending
                    ? ListSortDirection.Ascending
                    : ListSortDirection.Descending;

                e.Column.SortDirection = direction;

                SortSourceItemsByEdited(tab.SourceItems, direction == ListSortDirection.Descending);
            }
        }

        private void SortSourceItemsByEdited(ObservableCollection<PathItem> collection, bool descending)
        {
            if (collection == null || collection.Count <= 1)
            {
                return;
            }

            var itemsWithIndex = collection
                .Select((item, index) => new { Item = item, OriginalIndex = index })
                .ToList();

            var placeholders = itemsWithIndex.Where(x => x.Item.IsPlaceholder).ToList();
            var normalItems = itemsWithIndex.Where(x => !x.Item.IsPlaceholder).ToList();

            normalItems.Sort((a, b) =>
            {
                bool aEdited = a.Item.IsEdited;
                bool bEdited = b.Item.IsEdited;

                if (aEdited != bEdited)
                {
                    if (descending)
                    {
                        return aEdited ? -1 : 1;
                    }
                    else
                    {
                        return aEdited ? 1 : -1;
                    }
                }

                return a.OriginalIndex.CompareTo(b.OriginalIndex);
            });

            var sortedList = normalItems.Concat(placeholders).Select(x => x.Item).ToList();

            for (int i = 0; i < sortedList.Count; i++)
            {
                var targetItem = sortedList[i];
                int currentIndex = collection.IndexOf(targetItem);
                if (currentIndex != i && currentIndex >= 0)
                {
                    collection.Move(currentIndex, i);
                }
            }
        }

        private static string GetLastSavedMdFolder()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string configDir = Path.Combine(appData, "MKLink");
                string configPath = Path.Combine(configDir, "last_md_folder.txt");
                if (File.Exists(configPath))
                {
                    string path = File.ReadAllText(configPath).Trim();
                    if (Directory.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        private static void SaveLastSavedMdFolder(string fileOrFolderPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileOrFolderPath))
                {
                    return;
                }
                string folder = Directory.Exists(fileOrFolderPath)
                    ? fileOrFolderPath
                    : Path.GetDirectoryName(fileOrFolderPath);

                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    return;
                }

                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string configDir = Path.Combine(appData, "MKLink");
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }
                string configPath = Path.Combine(configDir, "last_md_folder.txt");
                File.WriteAllText(configPath, folder);
            }
            catch { }
        }

        private void SaveMarkdownButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            bool showSaveDialog = false;

            if (!string.IsNullOrWhiteSpace(_currentMarkdownFilePath))
            {
                var confirmDialog = new SaveConfirmDialog(_currentMarkdownFilePath)
                {
                    Owner = this
                };

                if (confirmDialog.ShowDialog() == true)
                {
                    if (confirmDialog.Action == SaveAction.Overwrite)
                    {
                        try
                        {
                            File.WriteAllText(_currentMarkdownFilePath, _viewModel.ExportMarkdown());
                            SaveLastSavedMdFolder(_currentMarkdownFilePath);
                            return;
                        }
                        catch (IOException ex)
                        {
                            MessageBox.Show(ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                    else if (confirmDialog.Action == SaveAction.SaveAs)
                    {
                        showSaveDialog = true;
                    }
                }
                else
                {
                    return;
                }
            }
            else
            {
                showSaveDialog = true;
            }

            if (showSaveDialog)
            {
                using (var dialog = new Forms.SaveFileDialog())
                {
                    dialog.Filter = "Markdown files (*.md)|*.md|All files (*.*)|*.*";
                    dialog.DefaultExt = "md";
                    dialog.AddExtension = true;
                    dialog.FileName = string.IsNullOrWhiteSpace(_currentMarkdownFilePath) 
                        ? "mklink.md" 
                        : Path.GetFileName(_currentMarkdownFilePath);

                    string initialDir = GetLastSavedMdFolder();
                    if (!string.IsNullOrWhiteSpace(initialDir))
                    {
                        dialog.InitialDirectory = initialDir;
                    }

                    if (dialog.ShowDialog() != Forms.DialogResult.OK)
                    {
                        return;
                    }

                    string path = dialog.FileName;
                    if (Path.GetExtension(path).Length == 0)
                    {
                        path += ".md";
                    }

                    try
                    {
                        File.WriteAllText(path, _viewModel.ExportMarkdown());
                        _currentMarkdownFilePath = path;
                        SaveLastSavedMdFolder(path);
                    }
                    catch (IOException ex)
                    {
                        MessageBox.Show(ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void LoadMarkdownButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            using (var dialog = new Forms.OpenFileDialog())
            {
                dialog.Filter = "Markdown files (*.md)|*.md|All files (*.*)|*.*";
                dialog.DefaultExt = "md";
                dialog.CheckFileExists = true;

                string initialDir = GetLastSavedMdFolder();
                if (!string.IsNullOrWhiteSpace(initialDir))
                {
                    dialog.InitialDirectory = initialDir;
                }

                if (dialog.ShowDialog() != Forms.DialogResult.OK)
                {
                    return;
                }

                try
                {
                    string markdown = File.ReadAllText(dialog.FileName);
                    _viewModel.ImportMarkdown(markdown);
                    _currentMarkdownFilePath = dialog.FileName;
                    SaveLastSavedMdFolder(dialog.FileName);
                }
                catch (IOException ex)
                {
                    MessageBox.Show(ex.Message, "Load failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }


        private void DonateAuthorButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://tinyurl.com/gmtpcdonate",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Open link failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyAndMklinkButton_Click(object sender, RoutedEventArgs e)
        {
            RunElevatedCommandSequence(
                _viewModel != null ? _viewModel.CopyTab.ScriptResult : string.Empty,
                _viewModel != null ? _viewModel.DeleteTab.ScriptResult : string.Empty,
                _viewModel != null ? _viewModel.MklinkTab.ScriptResult : string.Empty);
        }

        private void RunCopyDeleteMklinkFromCheckWindow(List<MklinkCheckEntry> selectedEntries)
        {
            RunCopyDeleteMklinkForEntries(selectedEntries, RefreshCheckWindowStatus);
        }

        private void DeleteAndMklinkButton_Click(object sender, RoutedEventArgs e)
        {
            RunElevatedCommandSequence(
                _viewModel != null ? _viewModel.DeleteTab.ScriptResult : string.Empty,
                _viewModel != null ? _viewModel.MklinkTab.ScriptResult : string.Empty);
        }

        private void RunDeleteMklinkFromCheckWindow(List<MklinkCheckEntry> selectedEntries)
        {
            RunDeleteMklinkForEntries(selectedEntries, RefreshCheckWindowStatus);
        }

        private void MakeReverseFromCheckWindow(List<MklinkCheckEntry> selectedEntries)
        {
            RunReverseForEntries(selectedEntries, RefreshCheckWindowStatus);
        }

        private void MoveToDestinationFromCheckWindow(List<MklinkCheckEntry> selectedEntries)
        {
            RunMoveToDestinationForEntries(selectedEntries, RefreshCheckWindowStatus);
        }

        private void CopyResultButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            var button = sender as FrameworkElement;
            var tab = button != null ? button.DataContext as PathTabViewModel : null;
            if (tab == null)
            {
                return;
            }

            if (tab == _viewModel.CopyTab)
            {
                CopyNormalizedSelectionToClipboard();
                return;
            }

            Clipboard.SetText(tab.ScriptResult ?? string.Empty);
        }

        private void CopyCombinedDirectScriptButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }
            Clipboard.SetText(_viewModel.CombinedCopyDeleteMklinkScript ?? string.Empty);
        }

        private void CopyCombinedReverseScriptButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }
            Clipboard.SetText(_viewModel.CombinedDeleteAndCopyReverseScript ?? string.Empty);
        }

        private void CombinedReverseRunButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }
            RunElevatedCommandSequence(
                _viewModel.DeleteReverseTab.ScriptResult,
                _viewModel.CopyReverseTab.ScriptResult,
                _viewModel.DeleteReverseTab.ScriptResult);
        }

        private void CopyNormalizedSelectionToClipboard()
        {
            if (CopyInputGrid == null)
            {
                return;
            }

            var lines = new System.Collections.Generic.List<string>();
            foreach (object candidate in CopyInputGrid.SelectedItems)
            {
                var item = candidate as PathItem;
                if (item != null && !item.IsPlaceholder && !item.IsEmpty && !string.IsNullOrWhiteSpace(item.NormalizedInput))
                {
                    lines.Add(item.NormalizedInput);
                }
            }

            if (lines.Count == 0)
            {
                for (int i = 0; i < CopyInputGrid.Items.Count; i++)
                {
                    var item = CopyInputGrid.Items[i] as PathItem;
                    if (item != null && !item.IsPlaceholder && !item.IsEmpty && !string.IsNullOrWhiteSpace(item.NormalizedInput))
                    {
                        lines.Add(item.NormalizedInput);
                    }
                }
            }

            Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }

        private void InputGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingLinkedSelection)
            {
                return;
            }

            var sourceGrid = sender as DataGrid;
            if (sourceGrid == null)
            {
                return;
            }

            var selectedItem = sourceGrid.SelectedItem as PathItem;
            if (selectedItem == null || selectedItem.IsPlaceholder)
            {
                return;
            }

            SyncLinkedGridsSelection(sourceGrid, selectedItem);
        }

        private void EditOutputGrid_Loaded(object sender, RoutedEventArgs e)
        {
            var grid = sender as DataGrid;
            if (grid == null)
            {
                return;
            }

            var tab = grid.DataContext as PathTabViewModel;
            if (tab == null || tab.SourceItems == null)
            {
                return;
            }

            if (grid.ItemsSource is ListCollectionView existingView &&
                ReferenceEquals(existingView.SourceCollection, tab.SourceItems))
            {
                existingView.Filter = item => FilterEditOutputRow(tab, item as PathItem);
                existingView.Refresh();
                return;
            }

            var view = new ListCollectionView(tab.SourceItems);
            view.Filter = item => FilterEditOutputRow(tab, item as PathItem);
            grid.ItemsSource = view;
        }

        private void EditOutputFilterHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as FrameworkElement;
            var grid = FindParent<DataGrid>(button);
            if (grid == null)
            {
                return;
            }

            var tab = grid.DataContext as PathTabViewModel;
            if (tab == null)
            {
                return;
            }

            ContextMenu menu = BuildEditOutputFilterMenu(grid, tab);
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private ContextMenu BuildEditOutputFilterMenu(DataGrid grid, PathTabViewModel tab)
        {
            var menu = new ContextMenu();
            var menuStyle = TryFindResource("DarkContextMenuStyle") as Style;
            if (menuStyle != null)
            {
                menu.Style = menuStyle;
            }

            menu.Items.Add(CreateEditOutputFilterMenuItem("All", EditOutputFilterKind.All, grid, tab));
            menu.Items.Add(CreateEditOutputFilterMenuItem("Default", EditOutputFilterKind.Default, grid, tab));
            menu.Items.Add(CreateEditOutputFilterMenuItem("Edited", EditOutputFilterKind.Edited, grid, tab));
            return menu;
        }

        private MenuItem CreateEditOutputFilterMenuItem(
            string header,
            EditOutputFilterKind filterKind,
            DataGrid grid,
            PathTabViewModel tab)
        {
            var menuItem = new MenuItem { Header = header };
            var menuItemStyle = TryFindResource("DarkContextMenuItemStyle") as Style;
            if (menuItemStyle != null)
            {
                menuItem.Style = menuItemStyle;
            }

            menuItem.Click += delegate
            {
                if (tab != null)
                {
                    tab.EditOutputFilterKind = filterKind;
                }

                RefreshEditOutputGridFilter(grid);
            };
            return menuItem;
        }

        private void RefreshEditOutputGridFilter(DataGrid grid)
        {
            if (grid == null || grid.ItemsSource == null)
            {
                return;
            }

            var view = grid.ItemsSource as ICollectionView;
            if (view != null)
            {
                view.Refresh();
            }
        }

        private void SyncLinkedGridsSelection(DataGrid sourceGrid, PathItem selectedItem)
        {
            if (sourceGrid == null || selectedItem == null)
            {
                return;
            }

            List<DataGrid> linkedGrids = FindLinkedDataGrids(sourceGrid);
            if (linkedGrids.Count == 0)
            {
                return;
            }

            _isSyncingLinkedSelection = true;
            try
            {
                for (int i = 0; i < linkedGrids.Count; i++)
                {
                    DataGrid targetGrid = linkedGrids[i];
                    if (targetGrid == null || ReferenceEquals(targetGrid, sourceGrid))
                    {
                        continue;
                    }

                    targetGrid.ScrollIntoView(selectedItem);
                    targetGrid.SelectedItem = selectedItem;
                }
            }
            finally
            {
                _isSyncingLinkedSelection = false;
            }
        }

        private List<DataGrid> FindLinkedDataGrids(DataGrid inputGrid)
        {
            var result = new List<DataGrid>();
            if (inputGrid == null)
            {
                return result;
            }

            object inputDataContext = inputGrid.DataContext;
            object inputItemsSource = GetLinkedSourceCollection(inputGrid.ItemsSource);
            IEnumerable<DataGrid> dataGrids = FindVisualChildren<DataGrid>(this);
            foreach (DataGrid grid in dataGrids)
            {
                if (grid == null)
                {
                    continue;
                }

                if (!ReferenceEquals(grid.DataContext, inputDataContext))
                {
                    continue;
                }

                object gridItemsSource = GetLinkedSourceCollection(grid.ItemsSource);
                if (ReferenceEquals(gridItemsSource, inputItemsSource))
                {
                    result.Add(grid);
                }
            }

            return result;
        }

        private static object GetLinkedSourceCollection(object itemsSource)
        {
            var collectionView = itemsSource as CollectionView;
            if (collectionView != null)
            {
                return collectionView.SourceCollection;
            }

            return itemsSource;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_viewModel == null || _viewModel.CopyTab == null)
            {
                return;
            }

            string searchText = SearchBox.Text;
            ICollectionView view = CollectionViewSource.GetDefaultView(_viewModel.CopyTab.SourceItems);
            if (view != null)
            {
                if (string.IsNullOrWhiteSpace(searchText))
                {
                    view.Filter = null;
                }
                else
                {
                    string keyword = searchText.Trim();
                    view.Filter = item =>
                    {
                        var pathItem = item as PathItem;
                        if (pathItem == null || pathItem.IsPlaceholder)
                        {
                            return false;
                        }
                        return (pathItem.OriginalInput != null && pathItem.OriginalInput.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                            || (pathItem.NormalizedInput != null && pathItem.NormalizedInput.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                            || (pathItem.EffectiveOutputPath != null && pathItem.EffectiveOutputPath.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                    };
                }
            }

            RefreshEditOutputViews();

            if (_checkMklinkWindow != null)
            {
                _checkMklinkWindow.UpdateSearchFilter(searchText);
            }
        }

        private bool FilterEditOutputRow(PathTabViewModel tab, PathItem item)
        {
            if (tab == null || item == null || item.IsPlaceholder || item.IsEmpty)
            {
                return false;
            }

            if (SearchBox != null && !string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                string keyword = SearchBox.Text.Trim();
                bool matches = (item.OriginalInput != null && item.OriginalInput.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                            || (item.NormalizedInput != null && item.NormalizedInput.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                            || (item.EffectiveOutputPath != null && item.EffectiveOutputPath.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!matches)
                {
                    return false;
                }
            }

            switch (tab.EditOutputFilterKind)
            {
                case EditOutputFilterKind.Default:
                    return !item.IsEdited;
                case EditOutputFilterKind.Edited:
                    return item.IsEdited;
                default:
                    return true;
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                yield break;
            }

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T)
                {
                    yield return (T)child;
                }

                foreach (T descendant in FindVisualChildren<T>(child))
                {
                    yield return descendant;
                }
            }
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }
            foreach (T child in FindVisualChildren<T>(parent))
            {
                return child;
            }
            return null;
        }

        private bool _isSyncingScroll = false;

        private void DataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isSyncingScroll)
            {
                return;
            }

            var sourceGrid = sender as DataGrid;
            if (sourceGrid == null || e.VerticalChange == 0)
            {
                return;
            }

            var scrollViewer = e.OriginalSource as ScrollViewer;
            if (scrollViewer == null)
            {
                return;
            }

            List<DataGrid> linkedGrids = FindLinkedDataGrids(sourceGrid);
            if (linkedGrids.Count == 0)
            {
                return;
            }

            _isSyncingScroll = true;
            try
            {
                for (int i = 0; i < linkedGrids.Count; i++)
                {
                    DataGrid targetGrid = linkedGrids[i];
                    if (targetGrid == null || ReferenceEquals(targetGrid, sourceGrid))
                    {
                        continue;
                    }

                    ScrollViewer targetViewer = FindVisualChild<ScrollViewer>(targetGrid);
                    if (targetViewer != null)
                    {
                        targetViewer.ScrollToVerticalOffset(e.VerticalOffset);
                    }
                }
            }
            finally
            {
                _isSyncingScroll = false;
            }
        }

        private IEnumerable<MklinkCheckEntry> BuildMklinkCheckEntries()
        {
            var entries = new List<MklinkCheckEntry>();
            if (_viewModel == null || _viewModel.CopyTab == null || _viewModel.CopyTab.SourceItems == null)
            {
                return entries;
            }

            for (int i = 0; i < _viewModel.CopyTab.SourceItems.Count; i++)
            {
                PathItem item = _viewModel.CopyTab.SourceItems[i];
                if (item == null || item.IsPlaceholder || item.IsEmpty)
                {
                    continue;
                }

                AddCheckEntry(entries, i, item.NormalizedInput, item.EffectiveOutputPath);
            }

            return entries;
        }

        private static void AddCheckEntry(List<MklinkCheckEntry> entries, int sourceIndex, string normalizedInputRaw, string outputRaw)
        {
            string normalizedInput = CleanPathForCheck(normalizedInputRaw);
            if (string.IsNullOrWhiteSpace(normalizedInput))
            {
                return;
            }

            string outputPath = CleanPathForCheck(outputRaw);
            bool exists = false;
            bool isLink = false;
            bool destinationCheck = false;
            string alreadyMklinkTo = string.Empty;
            string status = "Missing";

            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(normalizedInput);
                if (!string.IsNullOrWhiteSpace(expanded))
                {
                    expanded = expanded.Trim();
                }

                exists = Directory.Exists(expanded) || File.Exists(expanded);
                if (exists)
                {
                    FileAttributes attributes = File.GetAttributes(expanded);
                    isLink = (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
                    status = isLink ? "MKLINK" : "Real folder";
                    if (isLink)
                    {
                        TryResolveLinkTarget(expanded, out alreadyMklinkTo);
                    }
                }

                string expandedOutput = Environment.ExpandEnvironmentVariables(outputPath);
                if (!string.IsNullOrWhiteSpace(expandedOutput))
                {
                    expandedOutput = expandedOutput.Trim();
                }

                destinationCheck = !string.IsNullOrWhiteSpace(expandedOutput) && Directory.Exists(expandedOutput);
            }
            catch
            {
                status = "Invalid path";
            }

            entries.Add(new MklinkCheckEntry(sourceIndex, normalizedInput, outputPath, exists, isLink, destinationCheck, alreadyMklinkTo, status));
        }

        private void CopyTab_DataChanged_ForCheckWindow(object sender, EventArgs e)
        {
            RefreshEditOutputViews();
            if (_checkMklinkWindow != null)
            {
                _checkMklinkWindow.RefreshFromSource();
            }
        }

        private void CheckMklinkWindow_FilterCleared(object sender, EventArgs e)
        {
            if (SearchBox != null)
            {
                SearchBox.Text = string.Empty;
            }
        }

        private void RefreshEditOutputViews()
        {
            IEnumerable<DataGrid> dataGrids = FindVisualChildren<DataGrid>(this);
            foreach (DataGrid grid in dataGrids)
            {
                if (grid == null || grid.ItemsSource == null)
                {
                    continue;
                }

                ICollectionView view = grid.ItemsSource as ICollectionView;
                if (view != null)
                {
                    view.Refresh();
                }
            }
        }

        private void DeleteCopyRowsBySourceIndices(List<int> sourceIndices)
        {
            if (_viewModel == null || _viewModel.CopyTab == null || sourceIndices == null || sourceIndices.Count == 0)
            {
                return;
            }

            var itemsToDelete = new List<PathItem>();
            var uniqueSorted = sourceIndices
                .Where(x => x >= 0 && x < _viewModel.CopyTab.SourceItems.Count)
                .Distinct()
                .OrderByDescending(x => x)
                .ToList();

            for (int i = 0; i < uniqueSorted.Count; i++)
            {
                PathItem item = _viewModel.CopyTab.SourceItems[uniqueSorted[i]];
                if (item != null && !item.IsPlaceholder && !item.IsEmpty)
                {
                    itemsToDelete.Add(item);
                }
            }

            if (itemsToDelete.Count == 0)
            {
                return;
            }

            _viewModel.CopyTab.RemoveSourceItems(itemsToDelete);
        }


        private static string CleanPathForCheck(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Trim().Trim('"');
        }

        private static bool TryResolveLinkTarget(string path, out string resolvedPath)
        {
            resolvedPath = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                using (SafeFileHandle handle = CreateFile(
                    path,
                    0,
                    FileShare.ReadWrite | FileShare.Delete,
                    IntPtr.Zero,
                    FileMode.Open,
                    FileFlagsBackupsSemantics,
                    IntPtr.Zero))
                {
                    if (handle == null || handle.IsInvalid)
                    {
                        return false;
                    }

                    var buffer = new StringBuilder(512);
                    int result = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, 0);
                    if (result <= 0)
                    {
                        return false;
                    }

                    if (result >= buffer.Capacity)
                    {
                        buffer = new StringBuilder(result + 1);
                        result = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, 0);
                        if (result <= 0)
                        {
                            return false;
                        }
                    }

                    resolvedPath = NormalizeDevicePath(buffer.ToString());
                    return resolvedPath.Length > 0;
                }
            }
            catch
            {
                resolvedPath = string.Empty;
                return false;
            }
        }

        private static string NormalizeDevicePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string result = path.Trim();
            if (result.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                return @"\\" + result.Substring(8);
            }

            if (result.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            {
                return result.Substring(4);
            }

            return result;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            int dwDesiredAccess,
            FileShare dwShareMode,
            IntPtr lpSecurityAttributes,
            FileMode dwCreationDisposition,
            int dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetFinalPathNameByHandle(
            SafeFileHandle hFile,
            StringBuilder lpszFilePath,
            int cchFilePath,
            int dwFlags);

        private const int FileFlagsBackupsSemantics = 0x02000000;

        private void RunCopyDeleteMklinkForEntries(List<MklinkCheckEntry> entries, Action onProcessExited)
        {
            string copyScript = BuildCopyScript(entries);
            string deleteScript = BuildDeleteInputScript(entries);
            string mklinkScript = BuildMklinkScript(entries);
            RunElevatedCommandSequence(
                new[] { "COPY", "DELETE", "MKLINK D" },
                onProcessExited,
                copyScript,
                deleteScript,
                mklinkScript);
        }

        private void RunDeleteMklinkForEntries(List<MklinkCheckEntry> entries, Action onProcessExited)
        {
            string deleteScript = BuildDeleteInputScript(entries);
            string mklinkScript = BuildMklinkScript(entries);
            RunElevatedCommandSequence(
                new[] { "DELETE", "MKLINK D" },
                onProcessExited,
                deleteScript,
                mklinkScript);
        }

        private void RunReverseForEntries(List<MklinkCheckEntry> entries, Action onProcessExited)
        {
            string deleteReverseScript = BuildDeleteInputScript(entries);
            string copyReverseScript = BuildReverseCopyScript(entries);
            RunElevatedCommandSequence(
                new[] { "DELETE REVERSE", "COPY REVERSE" },
                onProcessExited,
                deleteReverseScript,
                copyReverseScript);
        }

        private void RunMoveToDestinationForEntries(List<MklinkCheckEntry> entries, Action onProcessExited)
        {
            string moveScript = BuildMoveToDestinationScript(entries);
            RunElevatedCommandSequence(
                new[] { "MOVE TO DESTINATION" },
                onProcessExited,
                moveScript);
        }

        private static string BuildCopyScript(IEnumerable<MklinkCheckEntry> entries)
        {
            var builder = new StringBuilder();
            if (entries == null)
            {
                return string.Empty;
            }

            foreach (MklinkCheckEntry entry in entries)
            {
                if (!IsUsableEntry(entry) || string.IsNullOrWhiteSpace(entry.OutputPath))
                {
                    continue;
                }

                AppendCommandBlock(builder,
                    "if not exist " + QuoteCommand(entry.OutputPath) + " mkdir " + QuoteCommand(entry.OutputPath),
                    "xcopy " + QuoteCommand(entry.NormalizedInput) + " " + QuoteCommand(entry.OutputPath) + " /E /I /Y");
            }

            return builder.ToString();
        }

        private static string BuildDeleteInputScript(IEnumerable<MklinkCheckEntry> entries)
        {
            var builder = new StringBuilder();
            if (entries == null)
            {
                return string.Empty;
            }

            foreach (MklinkCheckEntry entry in entries)
            {
                if (!IsUsableEntry(entry))
                {
                    continue;
                }

                AppendCommand(builder, "rmdir /s /q " + QuoteCommand(entry.NormalizedInput));
            }

            return builder.ToString();
        }

        private static string BuildMklinkScript(IEnumerable<MklinkCheckEntry> entries)
        {
            var builder = new StringBuilder();
            if (entries == null)
            {
                return string.Empty;
            }

            foreach (MklinkCheckEntry entry in entries)
            {
                if (!IsUsableEntry(entry) || string.IsNullOrWhiteSpace(entry.OutputPath))
                {
                    continue;
                }

                AppendCommand(builder, "mklink /D " + QuoteCommand(entry.NormalizedInput) + " " + QuoteCommand(entry.OutputPath));
            }

            return builder.ToString();
        }

        private static string BuildReverseCopyScript(IEnumerable<MklinkCheckEntry> entries)
        {
            var builder = new StringBuilder();
            if (entries == null)
            {
                return string.Empty;
            }

            foreach (MklinkCheckEntry entry in entries)
            {
                if (!IsUsableEntry(entry) || string.IsNullOrWhiteSpace(entry.OutputPath))
                {
                    continue;
                }

                AppendCommand(builder, "xcopy " + QuoteCommand(entry.OutputPath) + " " + QuoteCommand(entry.NormalizedInput) + " /E /I /Y");
            }

            return builder.ToString();
        }

        private static string BuildMoveToDestinationScript(IEnumerable<MklinkCheckEntry> entries)
        {
            var builder = new StringBuilder();
            if (entries == null)
            {
                return string.Empty;
            }

            foreach (MklinkCheckEntry entry in entries)
            {
                if (!CanMoveToDestination(entry))
                {
                    continue;
                }

                AppendCommandBlock(builder,
                    "if not exist " + QuoteCommand(entry.OutputPath) + " mkdir " + QuoteCommand(entry.OutputPath),
                    "robocopy " + QuoteCommand(entry.AlreadyMklinkTo) + " " + QuoteCommand(entry.OutputPath) + " /E /MOVE /R:1 /W:1");
                AppendCommand(builder, "if exist " + QuoteCommand(entry.AlreadyMklinkTo) + " rmdir /s /q " + QuoteCommand(entry.AlreadyMklinkTo));
                AppendCommand(builder, "if exist " + QuoteCommand(entry.NormalizedInput) + " rmdir /s /q " + QuoteCommand(entry.NormalizedInput));
                AppendCommand(builder, "mklink /D " + QuoteCommand(entry.NormalizedInput) + " " + QuoteCommand(entry.OutputPath));
            }

            return builder.ToString();
        }

        private static bool IsUsableEntry(MklinkCheckEntry entry)
        {
            return entry != null &&
                   !string.IsNullOrWhiteSpace(entry.NormalizedInput) &&
                   entry.Exists;
        }

        private static bool CanMoveToDestination(MklinkCheckEntry entry)
        {
            if (entry == null || !entry.IsLink)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(entry.OutputPath) || string.IsNullOrWhiteSpace(entry.AlreadyMklinkTo))
            {
                return false;
            }

            return !PathsMatch(entry.OutputPath, entry.AlreadyMklinkTo);
        }

        private static bool PathsMatch(string left, string right)
        {
            string normalizedLeft = NormalizeComparisonPath(left);
            string normalizedRight = NormalizeComparisonPath(right);
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeComparisonPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string result = CleanPathForCheck(value);
            result = Environment.ExpandEnvironmentVariables(result);
            result = result.Trim();

            try
            {
                result = Path.GetFullPath(result);
            }
            catch
            {
            }

            return result.TrimEnd('\\', '/');
        }

        private static void AppendCommand(StringBuilder builder, string command)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine(command);
        }

        private static void AppendCommandBlock(StringBuilder builder, string firstLine, string secondLine)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine(firstLine);
            builder.AppendLine(secondLine);
        }

        private static string QuoteCommand(string value)
        {
            return "\"" + (value ?? string.Empty) + "\"";
        }

        private void RefreshCheckWindowStatus()
        {
            if (_checkMklinkWindow == null)
            {
                return;
            }

            _checkMklinkWindow.RefreshFromSource();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_startupPromptShown)
            {
                return;
            }

            _startupPromptShown = true;
            // De dialog sau khi window da render xong, tranh man hinh trang luc khoi dong.
            Dispatcher.BeginInvoke(new Action(ShowStartupPromptSafely), DispatcherPriority.ContextIdle);
        }

        private void ShowStartupPromptSafely()
        {
            try
            {
                PromptStartupSymlinkFolder();
            }
            catch (Exception)
            {
                if (_viewModel != null)
                {
                    _viewModel.CopyTab.SetSelectedRootFolder(@"D:\");
                    _viewModel.CopyTab.ApplyTargetCommand.Execute(null);
                }
            }
        }

        private void PromptStartupSymlinkFolder()
        {
            if (_viewModel == null)
            {
                return;
            }

            string pickedPath;
            if (PickFolderVista("Chọn thư mục cần symlink", _viewModel.CopyTab.SelectedRootFolder, out pickedPath))
            {
                _viewModel.CopyTab.SetSelectedRootFolder(pickedPath);
                _viewModel.CopyTab.ApplyTargetCommand.Execute(null);
                return;
            }

            _viewModel.CopyTab.SetSelectedRootFolder(@"D:\");
            _viewModel.CopyTab.ApplyTargetCommand.Execute(null);
        }

        private bool PickFolderVista(string title, string initialPath, out string selectedPath)
        {
            selectedPath = null;

            IFileOpenDialog dialog = null;
            try
            {
                dialog = (IFileOpenDialog)new FileOpenDialog();
                uint options;
                dialog.GetOptions(out options);
                options |= (uint)(FOS.FOS_PICKFOLDERS | FOS.FOS_FORCEFILESYSTEM | FOS.FOS_PATHMUSTEXIST | FOS.FOS_DONTADDTORECENT);
                dialog.SetOptions(options);
                dialog.SetTitle(title);

                if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
                {
                    IShellItem folderItem;
                    Guid shellItemGuid = ShellGuids.IID_IShellItem;
                    int hr = ShellNativeMethods.SHCreateItemFromParsingName(initialPath, IntPtr.Zero, ref shellItemGuid, out folderItem);
                    if (hr == 0 && folderItem != null)
                    {
                        dialog.SetFolder(folderItem);
                    }
                }

                int result = dialog.Show(Handle);
                if (result != 0)
                {
                    return false;
                }

                IShellItem item;
                dialog.GetResult(out item);
                if (item == null)
                {
                    return false;
                }

                IntPtr pszString;
                item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out pszString);
                try
                {
                    selectedPath = Marshal.PtrToStringUni(pszString);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pszString);
                }

                return !string.IsNullOrWhiteSpace(selectedPath);
            }
            catch (COMException)
            {
                return false;
            }
            finally
            {
                if (dialog != null)
                {
                    Marshal.ReleaseComObject(dialog);
                }
            }
        }

        private IntPtr Handle
        {
            get { return new WindowInteropHelper(this).Handle; }
        }

        private void RunElevatedCommandSequence(params string[] scripts)
        {
            RunElevatedCommandSequence(null, scripts);
        }

        private void RunElevatedCommandSequence(Action onProcessExited, params string[] scripts)
        {
            RunElevatedCommandSequence(null, onProcessExited, scripts);
        }

        private void RunElevatedCommandSequence(string[] stageNames, Action onProcessExited, params string[] scripts)
        {
            if (_viewModel == null)
            {
                return;
            }

            string batchContent = BuildBatchContent(stageNames, scripts);
            if (string.IsNullOrWhiteSpace(batchContent))
            {
                MessageBox.Show("Khong co lenh nao de chay.", "MKLink", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string batchPath = Path.Combine(Path.GetTempPath(), "MKLink_Run_" + Guid.NewGuid().ToString("N") + ".cmd");
            try
            {
                File.WriteAllText(batchPath, batchContent, Encoding.ASCII);

                string system32Path = Environment.SystemDirectory;
                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(system32Path, "cmd.exe"),
                    Arguments = "/d /k call \"" + batchPath + "\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = system32Path
                };

                Process process = Process.Start(startInfo);
                RegisterLaunchedProcess(process, onProcessExited);
            }
            catch (Win32Exception ex)
            {
                MessageBox.Show(ex.Message, "Run failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (IOException ex)
            {
                MessageBox.Show(ex.Message, "Run failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string BuildBatchContent(string[] stageNames, params string[] scripts)
        {
            var builder = new StringBuilder();
            builder.AppendLine("@echo off");
            builder.AppendLine("setlocal");
            builder.AppendLine("echo Current directory: %CD%");

            var stageScripts = new System.Collections.Generic.List<KeyValuePair<string, string>>();
            for (int i = 0; i < scripts.Length; i++)
            {
                string script = scripts[i];
                if (string.IsNullOrWhiteSpace(script))
                {
                    continue;
                }

                string stageName;
                if (stageNames != null && i < stageNames.Length && !string.IsNullOrWhiteSpace(stageNames[i]))
                {
                    stageName = stageNames[i];
                }
                else if (i == 0)
                {
                    stageName = "COPY";
                }
                else if (i == 1)
                {
                    stageName = scripts.Length == 2 ? "MKLINK D" : "DELETE";
                }
                else
                {
                    stageName = "MKLINK D";
                }

                stageScripts.Add(new KeyValuePair<string, string>(stageName, script));
            }

            if (stageScripts.Count == 0)
            {
                return string.Empty;
            }

            for (int i = 0; i < stageScripts.Count; i++)
            {
                string stageName = stageScripts[i].Key;
                string script = stageScripts[i].Value;

                builder.AppendLine("echo ===== [" + stageName + "] =====");
                string[] lines = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                for (int j = 0; j < lines.Length; j++)
                {
                    string line = lines[j].Trim();
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    builder.AppendLine("echo [" + stageName + "] " + line.Replace("%", "%%"));
                    builder.AppendLine(line);
                }

                if (i < stageScripts.Count - 1)
                {
                    builder.AppendLine("timeout /t 5 /nobreak >nul");
                }
            }

            builder.AppendLine("echo.");
            builder.AppendLine("echo Done. Press any key to close this window.");
            builder.AppendLine("pause >nul");
            builder.AppendLine("exit /b 0");
            return builder.ToString();
        }

        private void RegisterLaunchedProcess(Process process, Action onProcessExited)
        {
            if (process == null)
            {
                return;
            }

            if (onProcessExited != null)
            {
                process.EnableRaisingEvents = true;
                process.Exited += delegate
                {
                    try
                    {
                        Dispatcher.BeginInvoke(new Action(onProcessExited), DispatcherPriority.Background);
                    }
                    catch
                    {
                    }
                };
            }

            lock (_launchedProcessLock)
            {
                _launchedProcesses.Add(process);
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_isClosing)
            {
                return;
            }

            _isClosing = true;
            CleanupLaunchedProcesses();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            CleanupLaunchedProcesses();

            try
            {
                Application.Current.Shutdown();
            }
            catch
            {
            }

            Environment.Exit(0);
        }

        private void CleanupLaunchedProcesses()
        {
            List<Process> processes;
            lock (_launchedProcessLock)
            {
                processes = new List<Process>(_launchedProcesses);
                _launchedProcesses.Clear();
            }

            for (int i = 0; i < processes.Count; i++)
            {
                Process process = processes[i];
                if (process == null)
                {
                    continue;
                }

                try
                {
                    if (!process.HasExited)
                    {
                        process.CloseMainWindow();
                        if (!process.WaitForExit(500))
                        {
                            process.Kill();
                            process.WaitForExit(1000);
                        }
                    }
                }
                catch
                {
                }
                finally
                {
                    try
                    {
                        process.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    [ClassInterface(ClassInterfaceType.None)]
    internal class FileOpenDialog
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
    internal interface IFileDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);

        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName(out IntPtr pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
    internal interface IFileOpenDialog : IFileDialog
    {
        void GetResults(out IntPtr ppenum);
        void GetSelectedItems(out IntPtr ppsai);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    internal interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [Flags]
    internal enum FOS : uint
    {
        FOS_OVERWRITEPROMPT = 0x2,
        FOS_STRICTFILETYPES = 0x4,
        FOS_NOCHANGEDIR = 0x8,
        FOS_PICKFOLDERS = 0x20,
        FOS_FORCEFILESYSTEM = 0x40,
        FOS_ALLNONSTORAGEITEMS = 0x80,
        FOS_NOVALIDATE = 0x100,
        FOS_ALLOWMULTISELECT = 0x200,
        FOS_PATHMUSTEXIST = 0x800,
        FOS_FILEMUSTEXIST = 0x1000,
        FOS_CREATEPROMPT = 0x2000,
        FOS_SHAREAWARE = 0x4000,
        FOS_NOREADONLYRETURN = 0x8000,
        FOS_NOTESTFILECREATE = 0x10000,
        FOS_HIDEMRUPLACES = 0x20000,
        FOS_HIDEPINNEDPLACES = 0x40000,
        FOS_NODEREFERENCELINKS = 0x100000,
        FOS_DONTADDTORECENT = 0x2000000,
        FOS_FORCESHOWHIDDEN = 0x10000000,
    }

    internal enum SIGDN : uint
    {
        SIGDN_FILESYSPATH = 0x80058000
    }

    internal static class ShellNativeMethods
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        internal static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);
    }

    internal static class ShellGuids
    {
        internal static Guid IID_IShellItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    }
}
