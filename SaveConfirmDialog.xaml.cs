using System.Windows;

namespace MKLink
{
    public partial class SaveConfirmDialog : Window
    {
        public SaveAction Action { get; private set; } = SaveAction.Cancel;

        public SaveConfirmDialog(string currentFilePath)
        {
            InitializeComponent();
            FilePathText.Text = currentFilePath;
            Loaded += (s, e) => OverwriteButton.Focus();
        }

        private void OverwriteButton_Click(object sender, RoutedEventArgs e)
        {
            Action = SaveAction.Overwrite;
            DialogResult = true;
            Close();
        }

        private void SaveAsButton_Click(object sender, RoutedEventArgs e)
        {
            Action = SaveAction.SaveAs;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Action = SaveAction.Cancel;
            DialogResult = false;
            Close();
        }
    }

    public enum SaveAction
    {
        Cancel,
        Overwrite,
        SaveAs
    }
}
