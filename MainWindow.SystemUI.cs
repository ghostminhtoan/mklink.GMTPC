using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using MKLink.Models;
using MKLink.ViewModels;

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
            _viewModel.CopyTab.DataChanged += CopyTab_DataChanged_ForCheckWindow;
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

        private void SaveMarkdownButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            using (var dialog = new Forms.SaveFileDialog())
            {
                dialog.Filter = "Markdown files (*.md)|*.md|All files (*.*)|*.*";
                dialog.DefaultExt = "md";
                dialog.AddExtension = true;
                dialog.FileName = "mklink.md";

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
                }
                catch (IOException ex)
                {
                    MessageBox.Show(ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
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

                if (dialog.ShowDialog() != Forms.DialogResult.OK)
                {
                    return;
                }

                try
                {
                    string markdown = File.ReadAllText(dialog.FileName);
                    _viewModel.ImportMarkdown(markdown);
                }
                catch (IOException ex)
                {
                    MessageBox.Show(ex.Message, "Load failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CheckMklinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            if (_checkMklinkWindow != null)
            {
                _checkMklinkWindow.RefreshFromSource();
                _checkMklinkWindow.Activate();
                return;
            }

            _checkMklinkWindow = new CheckMklinkWindow(BuildMklinkCheckEntries, DeleteCopyRowsBySourceIndices)
            {
                Owner = this
            };
            _checkMklinkWindow.Closed += CheckMklinkWindow_Closed;
            _checkMklinkWindow.Show();
        }

        private void CopyAndMklinkButton_Click(object sender, RoutedEventArgs e)
        {
            RunElevatedCommandSequence(
                _viewModel != null ? _viewModel.CopyTab.ScriptResult : string.Empty,
                _viewModel != null ? _viewModel.DeleteTab.ScriptResult : string.Empty,
                _viewModel != null ? _viewModel.MklinkTab.ScriptResult : string.Empty);
        }

        private void DeleteAndMklinkButton_Click(object sender, RoutedEventArgs e)
        {
            RunElevatedCommandSequence(
                _viewModel != null ? _viewModel.DeleteTab.ScriptResult : string.Empty,
                _viewModel != null ? _viewModel.MklinkTab.ScriptResult : string.Empty);
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

                AddCheckEntry(entries, i, item.NormalizedInput, item.MappedTarget);
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
                }
            }
            catch
            {
                status = "Invalid path";
            }

            entries.Add(new MklinkCheckEntry(sourceIndex, normalizedInput, outputPath, exists, isLink, status));
        }

        private void CopyTab_DataChanged_ForCheckWindow(object sender, EventArgs e)
        {
            if (_checkMklinkWindow != null)
            {
                _checkMklinkWindow.RefreshFromSource();
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

        private void CheckMklinkWindow_Closed(object sender, EventArgs e)
        {
            if (_checkMklinkWindow != null)
            {
                _checkMklinkWindow.Closed -= CheckMklinkWindow_Closed;
                _checkMklinkWindow = null;
            }
        }

        private static string CleanPathForCheck(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Trim().Trim('"');
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
            if (_viewModel == null)
            {
                return;
            }

            string batchContent = BuildBatchContent(scripts);
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
                RegisterLaunchedProcess(process);
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

        private static string BuildBatchContent(params string[] scripts)
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
                if (i == 0)
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

        private void RegisterLaunchedProcess(Process process)
        {
            if (process == null)
            {
                return;
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
