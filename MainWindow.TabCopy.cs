using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MKLink.Models;
using MKLink.ViewModels;

namespace MKLink
{
    public partial class MainWindow
    {
        private const string DragItemsFormat = "MKLink.PathItems";
        private Point _copyDragStartPoint;
        private PathItem _copyDragSourceItem;
        private bool _copyDragActive;

        // Tab Copy la nguon du lieu chinh cho Delete va MKLINK D.
        private void InitializeTabCopy()
        {
            DataObject.AddPastingHandler(CopyInputGrid, CopyInputGrid_OnPasting);
            CommandManager.AddPreviewExecutedHandler(CopyInputGrid, CopyInputGrid_PreviewExecuted);
            AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(MainWindow_PreviewKeyDown), true);
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled)
            {
                return;
            }

            var tab = CopyInputGrid != null ? CopyInputGrid.DataContext as PathTabViewModel : null;
            if (tab == null)
            {
                return;
            }

            bool ctrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (!ctrlPressed || e.OriginalSource is TextBox)
            {
                return;
            }

            if (e.Key == Key.V)
            {
                PasteClipboardIntoCopyTab();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Z && tab.UndoCommand != null && tab.UndoCommand.CanExecute(null))
            {
                tab.UndoCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Y && tab.RedoCommand != null && tab.RedoCommand.CanExecute(null))
            {
                tab.RedoCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void CopyInputGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                if (IsTextEditingContext(e.OriginalSource))
                {
                    return;
                }

                var deleteGrid = sender as DataGrid;
                if (deleteGrid != null)
                {
                    DeleteSelectedRows(deleteGrid);
                    e.Handled = true;
                    return;
                }
            }

            bool ctrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (!ctrlPressed)
            {
                return;
            }

            if (e.Key == Key.C)
            {
                if (IsTextEditingContext(e.OriginalSource))
                {
                    return;
                }

                var copyGrid = sender as DataGrid;
                if (copyGrid == null)
                {
                    return;
                }

                CopyNormalizedSelectionToClipboard();
                e.Handled = true;
                return;
            }

            if (e.Key != Key.V)
            {
                return;
            }

            PasteClipboardIntoCopyTab();
            e.Handled = true;
        }

        private void CopyInputGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsTextEditingContext(e.OriginalSource))
            {
                _copyDragSourceItem = null;
                _copyDragActive = false;
                return;
            }

            var grid = sender as DataGrid;
            if (grid == null)
            {
                return;
            }

            var row = FindParent<DataGridRow>(e.OriginalSource as DependencyObject);
            _copyDragSourceItem = row != null ? row.Item as PathItem : null;
            _copyDragStartPoint = e.GetPosition(null);
            _copyDragActive = false;
        }

        private void CopyInputGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _copyDragSourceItem == null || _copyDragActive)
            {
                return;
            }

            Point currentPosition = e.GetPosition(null);
            double horizontalChange = Math.Abs(currentPosition.X - _copyDragStartPoint.X);
            double verticalChange = Math.Abs(currentPosition.Y - _copyDragStartPoint.Y);
            if (horizontalChange < SystemParameters.MinimumHorizontalDragDistance &&
                verticalChange < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            var grid = sender as DataGrid;
            var tab = grid != null ? grid.DataContext as PathTabViewModel : null;
            if (grid == null || tab == null || tab.IsMirrorTab)
            {
                return;
            }

            List<PathItem> dragItems = CollectSelectedDragItems(grid);
            if (dragItems.Count == 0)
            {
                return;
            }

            _copyDragActive = true;
            try
            {
                var data = new DataObject();
                data.SetData(DragItemsFormat, dragItems.ToArray());
                DragDrop.DoDragDrop(grid, data, DragDropEffects.Move);
            }
            finally
            {
                _copyDragActive = false;
                _copyDragSourceItem = null;
            }
        }

        private void CopyInputGrid_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DragItemsFormat))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                return;
            }

            if (HasDroppedPaths(e.Data))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void CopyInputGrid_Drop(object sender, DragEventArgs e)
        {
            var grid = sender as DataGrid;
            var tab = grid != null ? grid.DataContext as PathTabViewModel : null;
            if (grid == null || tab == null)
            {
                return;
            }

            if (e.Data.GetDataPresent(DragItemsFormat))
            {
                var movedItems = e.Data.GetData(DragItemsFormat) as PathItem[];
                if (movedItems == null || movedItems.Length == 0)
                {
                    e.Handled = true;
                    return;
                }

                PathItem targetItem = GetDropTargetItem(grid, e.GetPosition(grid));
                tab.MoveSourceItems(movedItems, targetItem);
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                return;
            }

            string droppedText = GetDroppedPathsAsText(e.Data);
            if (string.IsNullOrWhiteSpace(droppedText))
            {
                return;
            }

            tab.PasteSourceText(droppedText);
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void MainWindow_DragOver(object sender, DragEventArgs e)
        {
            if (HasDroppedPaths(e.Data))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.None;
        }

        private void MainWindow_Drop(object sender, DragEventArgs e)
        {
            if (_viewModel == null || !HasDroppedPaths(e.Data))
            {
                return;
            }

            string droppedText = GetDroppedPathsAsText(e.Data);
            if (string.IsNullOrWhiteSpace(droppedText))
            {
                return;
            }

            _viewModel.CopyTab.PasteSourceText(droppedText);
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void CopyInputGrid_PreviewExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Command == ApplicationCommands.Copy)
            {
                if (IsTextEditingContext(e.OriginalSource))
                {
                    return;
                }

                var copyGrid = sender as DataGrid;
                if (copyGrid == null)
                {
                    e.Handled = true;
                    return;
                }

                CopyNormalizedSelectionToClipboard();
                e.Handled = true;
                return;
            }

            if (e.Command != ApplicationCommands.Delete)
            {
                return;
            }

            if (IsTextEditingContext(e.OriginalSource))
            {
                return;
            }

            var grid = sender as DataGrid;
            if (grid == null)
            {
                e.Handled = true;
                return;
            }

            e.Handled = true;
            DeleteSelectedRows(grid);
        }

        private void DeleteSelectedLinesButton_Click(object sender, RoutedEventArgs e)
        {
            var tab = CopyInputGrid.DataContext as PathTabViewModel;
            if (tab == null)
            {
                return;
            }

            var itemsToDelete = new System.Collections.Generic.List<PathItem>();
            foreach (object candidate in CopyInputGrid.SelectedItems)
            {
                var item = candidate as PathItem;
                if (item != null && !item.IsPlaceholder && !item.IsEmpty)
                {
                    itemsToDelete.Add(item);
                }
            }

            if (itemsToDelete.Count == 0)
            {
                return;
            }

            tab.RemoveSourceItems(itemsToDelete);
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            var tab = CopyInputGrid.DataContext as PathTabViewModel;
            if (tab == null)
            {
                return;
            }

            tab.ClearAllSourceItems();
        }

        private void CopyInputGrid_OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (_viewModel == null || _viewModel.CopyTab == null)
            {
                return;
            }

            string pasteText = GetClipboardAsPlainText();
            if (string.IsNullOrWhiteSpace(pasteText))
            {
                return;
            }

            _viewModel.CopyTab.PasteSourceText(pasteText);
            e.CancelCommand();
        }

        private void PasteClipboardIntoCopyTab()
        {
            if (_viewModel == null || _viewModel.CopyTab == null)
            {
                return;
            }

            string pasteText = GetClipboardAsPlainText();
            if (string.IsNullOrWhiteSpace(pasteText))
            {
                return;
            }

            _viewModel.CopyTab.PasteSourceText(pasteText);
        }

        private bool IsTextEditingContext(object originalSource)
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

        private List<PathItem> CollectSelectedDragItems(DataGrid grid)
        {
            var dragItems = new List<PathItem>();
            if (grid == null)
            {
                return dragItems;
            }

            foreach (object candidate in grid.SelectedItems)
            {
                var item = candidate as PathItem;
                if (item != null && !item.IsPlaceholder && !item.IsEmpty && !dragItems.Contains(item))
                {
                    dragItems.Add(item);
                }
            }

            if (dragItems.Count == 0)
            {
                var selectedItem = grid.SelectedItem as PathItem;
                if (selectedItem != null && !selectedItem.IsPlaceholder && !selectedItem.IsEmpty)
                {
                    dragItems.Add(selectedItem);
                }
            }

            return dragItems;
        }

        private void DeleteSelectedRows(DataGrid grid)
        {
            var tab = grid != null ? grid.DataContext as PathTabViewModel : null;
            if (tab == null)
            {
                return;
            }

            var itemsToDelete = new List<PathItem>();
            foreach (object candidate in grid.SelectedItems)
            {
                var item = candidate as PathItem;
                if (item != null && !item.IsPlaceholder && !item.IsEmpty)
                {
                    itemsToDelete.Add(item);
                }
            }

            if (itemsToDelete.Count == 0)
            {
                var selectedItem = grid.SelectedItem as PathItem;
                if (selectedItem != null && !selectedItem.IsPlaceholder && !selectedItem.IsEmpty)
                {
                    itemsToDelete.Add(selectedItem);
                }
            }

            if (itemsToDelete.Count == 0)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(delegate
            {
                tab.RemoveSourceItems(itemsToDelete);
            }), DispatcherPriority.Background);
        }

        private bool HasDroppedPaths(IDataObject data)
        {
            if (data == null)
            {
                return false;
            }

            return data.GetDataPresent(DataFormats.FileDrop) ||
                   data.GetDataPresent(DataFormats.UnicodeText) ||
                   data.GetDataPresent(DataFormats.StringFormat);
        }

        private string GetDroppedPathsAsText(IDataObject data)
        {
            if (data == null)
            {
                return string.Empty;
            }

            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    return string.Join(Environment.NewLine, files);
                }
            }

            if (data.GetDataPresent(DataFormats.UnicodeText))
            {
                return data.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
            }

            if (data.GetDataPresent(DataFormats.StringFormat))
            {
                return data.GetData(DataFormats.StringFormat) as string ?? string.Empty;
            }

            return string.Empty;
        }

        private PathItem GetDropTargetItem(DataGrid grid, Point point)
        {
            DependencyObject current = grid.InputHitTest(point) as DependencyObject;
            while (current != null && !(current is DataGridRow))
            {
                current = VisualTreeHelper.GetParent(current);
            }

            var row = current as DataGridRow;
            return row != null ? row.Item as PathItem : null;
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

        private string GetClipboardAsPlainText()
        {
            if (Clipboard.ContainsText())
            {
                return Clipboard.GetText();
            }

            if (Clipboard.ContainsFileDropList())
            {
                StringCollection files = Clipboard.GetFileDropList();
                if (files == null || files.Count == 0)
                {
                    return string.Empty;
                }

                string[] paths = new string[files.Count];
                files.CopyTo(paths, 0);
                return string.Join(Environment.NewLine, paths);
            }

            var data = Clipboard.GetDataObject();
            if (data != null && data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    return string.Join(Environment.NewLine, files);
                }
            }

            return string.Empty;
        }
    }
}
