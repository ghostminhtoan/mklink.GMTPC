using System.Windows;
using MKLink.ViewModels;

namespace MKLink
{
    public partial class MainWindow
    {
        // Tab Reverse mirror du lieu tu Tab Copy nhung dung luong nguoc.
        private void InitializeTabReverse()
        {
        }

        private void ReversePrimaryActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            var element = sender as FrameworkElement;
            var tab = element != null ? element.DataContext as PathTabViewModel : null;
            if (tab == null)
            {
                return;
            }

            if (tab.ScriptMode == ScriptMode.ReverseCopy)
            {
                RunElevatedCommandSequence(_viewModel.CopyReverseTab.ScriptResult);
                return;
            }

            if (tab.ScriptMode == ScriptMode.ReverseDelete)
            {
                RunElevatedCommandSequence(_viewModel.DeleteReverseTab.ScriptResult);
            }
        }

        private void ReverseSecondaryActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            var element = sender as FrameworkElement;
            var tab = element != null ? element.DataContext as PathTabViewModel : null;
            if (tab == null || tab.ScriptMode != ScriptMode.ReverseCopy)
            {
                return;
            }

            RunElevatedCommandSequence(
                _viewModel.DeleteReverseTab.ScriptResult,
                _viewModel.CopyReverseTab.ScriptResult,
                _viewModel.DeleteReverseTab.ScriptResult);
        }
    }
}
