using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using System.Windows;
using System.Windows.Threading;
using System.Linq;
using MKLink.Models;

namespace MKLink.ViewModels
{
    public class MainViewModel : NotifyObject
    {
        private bool _isWordWrapEnabled = true;
        private readonly Stack<MarkdownDocumentState> _undoStack = new Stack<MarkdownDocumentState>();
        private readonly Stack<MarkdownDocumentState> _redoStack = new Stack<MarkdownDocumentState>();
        private bool _isRestoringHistory;

        public MainViewModel()
        {
            var copyItems = new ObservableCollection<PathItem>();

            CopyTab = new PathTabViewModel(this, "COPY", ScriptMode.Copy, copyItems, false, null);
            DeleteTab = new PathTabViewModel(this, "DELETE", ScriptMode.Delete, copyItems, true, CopyTab);
            MklinkTab = new PathTabViewModel(this, "MKLINK D", ScriptMode.MklinkD, copyItems, true, CopyTab);
            CopyReverseTab = new PathTabViewModel(this, "COPY REVERSE", ScriptMode.ReverseCopy, copyItems, true, CopyTab);
            DeleteReverseTab = new PathTabViewModel(this, "DELETE REVERSE", ScriptMode.ReverseDelete, copyItems, true, CopyTab);

            CopyTab.DataChanged += CopyTab_DataChanged;
            DeleteTab.RefreshScript();
            MklinkTab.RefreshScript();
            CopyReverseTab.RefreshScript();
            DeleteReverseTab.RefreshScript();
            NotifyCombinedScriptsChanged();
        }

        public PathTabViewModel CopyTab { get; private set; }

        public PathTabViewModel DeleteTab { get; private set; }

        public PathTabViewModel MklinkTab { get; private set; }

        public PathTabViewModel CopyReverseTab { get; private set; }

        public PathTabViewModel DeleteReverseTab { get; private set; }

        public string CombinedCopyDeleteMklinkScript
        {
            get
            {
                var builder = new StringBuilder();
                if (CopyTab != null && !string.IsNullOrWhiteSpace(CopyTab.ScriptResult))
                {
                    builder.AppendLine("echo ===== [COPY] =====");
                    builder.AppendLine(CopyTab.ScriptResult);
                }
                if (DeleteTab != null && !string.IsNullOrWhiteSpace(DeleteTab.ScriptResult))
                {
                    if (builder.Length > 0) builder.AppendLine();
                    builder.AppendLine("echo ===== [DELETE] =====");
                    builder.AppendLine(DeleteTab.ScriptResult);
                }
                if (MklinkTab != null && !string.IsNullOrWhiteSpace(MklinkTab.ScriptResult))
                {
                    if (builder.Length > 0) builder.AppendLine();
                    builder.AppendLine("echo ===== [MKLINK D] =====");
                    builder.AppendLine(MklinkTab.ScriptResult);
                }
                return builder.ToString();
            }
        }

        public string CombinedDeleteAndCopyReverseScript
        {
            get
            {
                var builder = new StringBuilder();
                if (DeleteReverseTab != null && !string.IsNullOrWhiteSpace(DeleteReverseTab.ScriptResult))
                {
                    builder.AppendLine("echo ===== [DELETE REVERSE] =====");
                    builder.AppendLine(DeleteReverseTab.ScriptResult);
                }
                if (CopyReverseTab != null && !string.IsNullOrWhiteSpace(CopyReverseTab.ScriptResult))
                {
                    if (builder.Length > 0) builder.AppendLine();
                    builder.AppendLine("echo ===== [COPY REVERSE] =====");
                    builder.AppendLine(CopyReverseTab.ScriptResult);
                }
                if (DeleteReverseTab != null && !string.IsNullOrWhiteSpace(DeleteReverseTab.ScriptResult))
                {
                    if (builder.Length > 0) builder.AppendLine();
                    builder.AppendLine("echo ===== [DELETE REVERSE] =====");
                    builder.AppendLine(DeleteReverseTab.ScriptResult);
                }
                return builder.ToString();
            }
        }

        private void NotifyCombinedScriptsChanged()
        {
            OnPropertyChanged("CombinedCopyDeleteMklinkScript");
            OnPropertyChanged("CombinedDeleteAndCopyReverseScript");
        }

        public bool CanUndo
        {
            get { return _undoStack.Count > 0; }
        }

        public bool CanRedo
        {
            get { return _redoStack.Count > 0; }
        }

        public bool IsWordWrapEnabled
        {
            get { return _isWordWrapEnabled; }
            set
            {
                if (_isWordWrapEnabled != value)
                {
                    _isWordWrapEnabled = value;
                    OnPropertyChanged();
                    CopyTab.NotifyDisplayOptionsChanged();
                    DeleteTab.NotifyDisplayOptionsChanged();
                    MklinkTab.NotifyDisplayOptionsChanged();
                    CopyReverseTab.NotifyDisplayOptionsChanged();
                    DeleteReverseTab.NotifyDisplayOptionsChanged();
                }
            }
        }

        private void CopyTab_DataChanged(object sender, EventArgs e)
        {
            DeleteTab.NotifyMirrorStateChanged();
            MklinkTab.NotifyMirrorStateChanged();
            CopyReverseTab.NotifyMirrorStateChanged();
            DeleteReverseTab.NotifyMirrorStateChanged();
            DeleteTab.RefreshScript();
            MklinkTab.RefreshScript();
            CopyReverseTab.RefreshScript();
            DeleteReverseTab.RefreshScript();
            NotifyCombinedScriptsChanged();
        }

        public string ExportMarkdown()
        {
            return MarkdownStateSerializer.Serialize(this);
        }

        public void ImportMarkdown(string markdown)
        {
            MarkdownDocumentState state = MarkdownStateSerializer.Deserialize(markdown);
            CopyTab.LoadDocumentState(state);
            IsWordWrapEnabled = state.IsWordWrapEnabled;
            DeleteTab.NotifyMirrorStateChanged();
            MklinkTab.NotifyMirrorStateChanged();
            CopyReverseTab.NotifyMirrorStateChanged();
            DeleteReverseTab.NotifyMirrorStateChanged();
            DeleteTab.RefreshScript();
            MklinkTab.RefreshScript();
            CopyReverseTab.RefreshScript();
            DeleteReverseTab.RefreshScript();
            NotifyCombinedScriptsChanged();
        }

        public void CaptureUndoSnapshot()
        {
            if (_isRestoringHistory)
            {
                return;
            }

            _undoStack.Push(CaptureCurrentState());
            _redoStack.Clear();
            OnPropertyChanged("CanUndo");
            OnPropertyChanged("CanRedo");
        }

        public void Undo()
        {
            if (_undoStack.Count == 0)
            {
                return;
            }

            _redoStack.Push(CaptureCurrentState());
            RestoreState(_undoStack.Pop());
        }

        public void Redo()
        {
            if (_redoStack.Count == 0)
            {
                return;
            }

            _undoStack.Push(CaptureCurrentState());
            RestoreState(_redoStack.Pop());
        }

        private MarkdownDocumentState CaptureCurrentState()
        {
            MarkdownDocumentState state = CopyTab.CaptureDocumentState();
            state.IsWordWrapEnabled = _isWordWrapEnabled;
            return state;
        }

        private void RestoreState(MarkdownDocumentState state)
        {
            if (state == null)
            {
                return;
            }

            _isRestoringHistory = true;
            try
            {
                CopyTab.LoadDocumentState(state);
                IsWordWrapEnabled = state.IsWordWrapEnabled;
            }
            finally
            {
                _isRestoringHistory = false;
            }

            DeleteTab.NotifyMirrorStateChanged();
            MklinkTab.NotifyMirrorStateChanged();
            CopyReverseTab.NotifyMirrorStateChanged();
            DeleteReverseTab.NotifyMirrorStateChanged();
            DeleteTab.RefreshScript();
            MklinkTab.RefreshScript();
            CopyReverseTab.RefreshScript();
            DeleteReverseTab.RefreshScript();
            NotifyCombinedScriptsChanged();
            OnPropertyChanged("CanUndo");
            OnPropertyChanged("CanRedo");
        }

        internal bool IsRestoringHistory
        {
            get { return _isRestoringHistory; }
        }
    }

    public class PathTabViewModel : NotifyObject
    {
        private const string DefaultRootFolder = @"D:\";
        private const string DefaultSubfolderName = "USERS";

        private readonly MainViewModel _owner;
        private readonly ScriptMode _scriptMode;
        private readonly PathTabViewModel _masterTab;
        private string _selectedRootFolder;
        private string _subfolderName;
        private string _rootTargetPath;
        private string _scriptResult;
        private EditOutputFilterKind _editOutputFilterKind = EditOutputFilterKind.All;
        private bool _isRefreshing;
        private bool _placeholderRefreshPending;

        public PathTabViewModel(
            MainViewModel owner,
            string title,
            ScriptMode scriptMode,
            ObservableCollection<PathItem> sourceItems,
            bool isMirrorTab,
            PathTabViewModel masterTab)
        {
            _owner = owner;
            Title = title;
            _scriptMode = scriptMode;
            _masterTab = masterTab;
            IsMirrorTab = isMirrorTab;
            _selectedRootFolder = DefaultRootFolder;
            _subfolderName = DefaultSubfolderName;
            _rootTargetPath = Path.Combine(_selectedRootFolder, _subfolderName);
            SourceItems = sourceItems;
            SourceItems.CollectionChanged += SourceItems_CollectionChanged;

            ApplyTargetCommand = new RelayCommand(ApplyTarget, CanEditSource);
            CopyScriptCommand = new RelayCommand(CopyScript);
            UndoCommand = new RelayCommand(Undo, CanUndo);
            RedoCommand = new RelayCommand(Redo, CanRedo);

            for (int i = 0; i < SourceItems.Count; i++)
            {
                SourceItems[i].PropertyChanged += PathItem_PropertyChanged;
            }

            if (!IsMirrorTab)
            {
                EnsureInputPlaceholder();
                RefreshAll();
            }

            RefreshScript();
        }

        public event EventHandler DataChanged;

        public string Title { get; private set; }

        public ScriptMode ScriptMode
        {
            get { return _scriptMode; }
        }

        public bool IsMirrorTab { get; private set; }

        public bool CanEditRows
        {
            get { return !IsMirrorTab; }
        }

        public ObservableCollection<PathItem> SourceItems { get; private set; }

        public string SelectedRootFolder
        {
            get { return _selectedRootFolder; }
        }

        public string SubfolderName
        {
            get
            {
                if (IsMirrorTab && _masterTab != null)
                {
                    return _masterTab.SubfolderName;
                }

                return _subfolderName;
            }
            set
            {
                if (_subfolderName != value)
                {
                    _subfolderName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string RootTargetPath
        {
            get
            {
                if (IsMirrorTab && _masterTab != null)
                {
                    return _masterTab.RootTargetPath;
                }

                return _rootTargetPath;
            }
            private set
            {
                if (_rootTargetPath != value)
                {
                    _rootTargetPath = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ScriptResult
        {
            get { return _scriptResult; }
            private set
            {
                if (_scriptResult != value)
                {
                    _scriptResult = value;
                    OnPropertyChanged();
                }
            }
        }

        public EditOutputFilterKind EditOutputFilterKind
        {
            get { return _editOutputFilterKind; }
            set
            {
                if (_editOutputFilterKind != value)
                {
                    _editOutputFilterKind = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand ApplyTargetCommand { get; private set; }

        public ICommand CopyScriptCommand { get; private set; }

        public ICommand UndoCommand { get; private set; }

        public ICommand RedoCommand { get; private set; }

        public void NotifyMirrorStateChanged()
        {
            if (!IsMirrorTab)
            {
                return;
            }

            OnPropertyChanged("SubfolderName");
            OnPropertyChanged("RootTargetPath");
        }

        public void NotifyDisplayOptionsChanged()
        {
            OnPropertyChanged("IsWordWrapEnabled");
        }

        public void PasteSourceText(string text)
        {
            if (IsMirrorTab && _masterTab != null)
            {
                _masterTab.PasteSourceText(text);
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            RecordHistory();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = CleanInput(lines[i]);
                if (line.Length > 0)
                {
                    AddSourceLine(line);
                }
            }

            RefreshAll();
            RaiseDataChanged();
        }

        public void LoadDocumentState(MarkdownDocumentState state)
        {
            if (IsMirrorTab)
            {
                if (_masterTab != null)
                {
                    _masterTab.LoadDocumentState(state);
                }

                return;
            }

            if (state == null)
            {
                return;
            }

            if (_owner != null && !_owner.IsRestoringHistory)
            {
                _owner.CaptureUndoSnapshot();
            }

            _isRefreshing = true;
            try
            {
                _selectedRootFolder = string.IsNullOrWhiteSpace(state.SelectedRootFolder) ? DefaultRootFolder : state.SelectedRootFolder;
                _subfolderName = string.IsNullOrWhiteSpace(state.SubfolderName) ? DefaultSubfolderName : state.SubfolderName;
                RootTargetPath = string.IsNullOrWhiteSpace(state.RootTargetPath)
                    ? Path.Combine(_selectedRootFolder, _subfolderName)
                    : state.RootTargetPath;

                SourceItems.Clear();
                if (state.PathRows != null && state.PathRows.Count > 0)
                {
                    for (int i = 0; i < state.PathRows.Count; i++)
                    {
                        MarkdownPathRowState row = state.PathRows[i];
                        if (row == null)
                        {
                            continue;
                        }

                        string originalInput = CleanInput(row.OriginalInput);
                        if (string.IsNullOrWhiteSpace(originalInput))
                        {
                            continue;
                        }

                        var item = new PathItem
                        {
                            OriginalInput = originalInput
                        };

                        SourceItems.Add(item);
                        RefreshItem(item);

                        if (!string.IsNullOrWhiteSpace(row.NormalizedInput))
                        {
                            item.NormalizedInput = CleanInput(row.NormalizedInput);
                        }

                        if (!string.IsNullOrWhiteSpace(row.OutputPath))
                        {
                            item.MappedTarget = CleanInput(row.OutputPath);
                        }

                        item.EditOutputPath = CleanInput(row.EditOutputPath);
                    }
                }
                else if (state.InputLines != null)
                {
                    for (int i = 0; i < state.InputLines.Count; i++)
                    {
                        string input = CleanInput(state.InputLines[i]);
                        if (input.Length > 0)
                        {
                            SourceItems.Add(new PathItem { OriginalInput = input });
                        }
                    }
                }
            }
            finally
            {
                _isRefreshing = false;
            }

            EnsureInputPlaceholder();
            RefreshAll();
            if (state.PathRows != null && state.PathRows.Count > 0)
            {
                _isRefreshing = true;
                try
                {
                    int limit = state.PathRows.Count < SourceItems.Count ? state.PathRows.Count : SourceItems.Count;
                    for (int i = 0; i < limit; i++)
                    {
                        MarkdownPathRowState row = state.PathRows[i];
                        PathItem item = SourceItems[i];
                        if (row == null || item == null || item.IsPlaceholder)
                        {
                            continue;
                        }

                        item.NormalizedInput = CleanInput(row.NormalizedInput);
                        item.MappedTarget = CleanInput(row.OutputPath);
                        item.EditOutputPath = CleanInput(row.EditOutputPath);
                    }
                }
                finally
                {
                    _isRefreshing = false;
                }

                RefreshScript();
            }

            OnPropertyChanged("SelectedRootFolder");
            OnPropertyChanged("SubfolderName");
            OnPropertyChanged("RootTargetPath");
            RaiseDataChanged();
        }

        public void RefreshScript()
        {
            var builder = new StringBuilder();

            for (int i = 0; i < SourceItems.Count; i++)
            {
                PathItem item = SourceItems[i];
                if (!CanGenerateCommand(item))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(BuildCommand(item));
            }

            ScriptResult = builder.ToString();
        }

        private bool CanEditSource()
        {
            return !IsMirrorTab;
        }

        public void SetSelectedRootFolder(string selectedRootFolder)
        {
            if (IsMirrorTab || string.IsNullOrWhiteSpace(selectedRootFolder))
            {
                return;
            }

            RecordHistory();
            _selectedRootFolder = selectedRootFolder;
            OnPropertyChanged("SelectedRootFolder");
        }

        private void ApplyTarget()
        {
            if (IsMirrorTab)
            {
                return;
            }

            RecordHistory();
            string root = CleanInput(_selectedRootFolder);
            string subfolder = CleanInput(SubfolderName);

            if (root.Length == 0)
            {
                root = DefaultRootFolder;
            }

            RootTargetPath = subfolder.Length > 0 ? Path.Combine(root, subfolder) : root;
            if (!string.IsNullOrWhiteSpace(RootTargetPath))
            {
                Directory.CreateDirectory(RootTargetPath);
            }
            RefreshAll();
            RaiseDataChanged();
        }

        private void CopyScript()
        {
            System.Windows.Clipboard.SetText(ScriptResult ?? string.Empty);
        }

        public MarkdownDocumentState CaptureDocumentState()
        {
            var state = new MarkdownDocumentState
            {
                SelectedRootFolder = _selectedRootFolder,
                SubfolderName = _subfolderName,
                RootTargetPath = _rootTargetPath,
                InputLines = new List<string>(),
                PathRows = new List<MarkdownPathRowState>()
            };

            for (int i = 0; i < SourceItems.Count; i++)
            {
                PathItem item = SourceItems[i];
                if (item != null && !item.IsPlaceholder && !string.IsNullOrWhiteSpace(item.OriginalInput))
                {
                    state.InputLines.Add(item.OriginalInput);
                    state.PathRows.Add(new MarkdownPathRowState
                    {
                        OriginalInput = item.OriginalInput,
                        NormalizedInput = item.NormalizedInput,
                        OutputPath = item.MappedTarget,
                        EditOutputPath = item.EditOutputPath
                    });
                }
            }

            return state;
        }

        public void RemoveSourceItem(PathItem item)
        {
            if (IsMirrorTab || item == null)
            {
                return;
            }

            RemoveSourceItems(new[] { item });
        }

        public void RemoveSourceItems(IEnumerable<PathItem> items)
        {
            if (IsMirrorTab || items == null)
            {
                return;
            }

            var deleteList = new List<PathItem>();
            foreach (PathItem item in items)
            {
                if (item != null && !item.IsPlaceholder && !item.IsEmpty && SourceItems.Contains(item))
                {
                    deleteList.Add(item);
                }
            }

            if (deleteList.Count == 0)
            {
                return;
            }

            RecordHistory();
            for (int i = 0; i < deleteList.Count; i++)
            {
                SourceItems.Remove(deleteList[i]);
            }
        }

        public void MoveSourceItems(IEnumerable<PathItem> items, PathItem targetItem)
        {
            if (IsMirrorTab || items == null)
            {
                return;
            }

            var moveList = new List<PathItem>();
            foreach (PathItem item in items)
            {
                if (item != null && !item.IsPlaceholder && SourceItems.Contains(item) && !moveList.Contains(item))
                {
                    moveList.Add(item);
                }
            }

            if (moveList.Count == 0)
            {
                return;
            }

            if (targetItem != null && moveList.Contains(targetItem))
            {
                return;
            }

            moveList.Sort(delegate (PathItem left, PathItem right)
            {
                return SourceItems.IndexOf(left).CompareTo(SourceItems.IndexOf(right));
            });

            RecordHistory();
            _isRefreshing = true;
            try
            {
                var originalIndices = new List<int>();
                for (int i = 0; i < moveList.Count; i++)
                {
                    originalIndices.Add(SourceItems.IndexOf(moveList[i]));
                }

                originalIndices.Sort();

                int targetIndex = targetItem != null ? SourceItems.IndexOf(targetItem) : GetPlaceholderIndex();
                if (targetIndex < 0)
                {
                    targetIndex = SourceItems.Count;
                }

                for (int i = originalIndices.Count - 1; i >= 0; i--)
                {
                    SourceItems.RemoveAt(originalIndices[i]);
                }

                int removedBeforeTarget = 0;
                for (int i = 0; i < originalIndices.Count; i++)
                {
                    if (originalIndices[i] < targetIndex)
                    {
                        removedBeforeTarget++;
                    }
                }

                targetIndex -= removedBeforeTarget;
                int placeholderIndex = GetPlaceholderIndex();
                if (placeholderIndex >= 0 && targetIndex > placeholderIndex)
                {
                    targetIndex = placeholderIndex;
                }

                if (targetIndex < 0)
                {
                    targetIndex = 0;
                }

                for (int i = 0; i < moveList.Count; i++)
                {
                    SourceItems.Insert(targetIndex + i, moveList[i]);
                }
            }
            finally
            {
                _isRefreshing = false;
            }

            RefreshAll();
            RaiseDataChanged();
        }

        public void ClearAllSourceItems()
        {
            if (IsMirrorTab)
            {
                return;
            }

            if (SourceItems.Count == 0)
            {
                return;
            }

            RecordHistory();
            SourceItems.Clear();
        }

        private void AddSourceLine(string line)
        {
            PathItem placeholder = FindPlaceholderItem();
            if (placeholder != null)
            {
                placeholder.IsPlaceholder = false;
                placeholder.OriginalInput = line;
                return;
            }

            SourceItems.Add(new PathItem { OriginalInput = line });
        }

        private PathItem FindPlaceholderItem()
        {
            for (int i = 0; i < SourceItems.Count; i++)
            {
                if (SourceItems[i].IsPlaceholder && SourceItems[i].IsEmpty)
                {
                    return SourceItems[i];
                }
            }

            return null;
        }

        private int GetPlaceholderIndex()
        {
            for (int i = 0; i < SourceItems.Count; i++)
            {
                if (SourceItems[i].IsPlaceholder)
                {
                    return i;
                }
            }

            return -1;
        }

        private void SourceItems_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                for (int i = 0; i < e.NewItems.Count; i++)
                {
                    var item = e.NewItems[i] as PathItem;
                    if (item != null)
                    {
                        item.PropertyChanged += PathItem_PropertyChanged;
                        if (!IsMirrorTab)
                        {
                            RefreshItem(item);
                        }
                    }
                }
            }

            if (e.OldItems != null)
            {
                for (int i = 0; i < e.OldItems.Count; i++)
                {
                    var item = e.OldItems[i] as PathItem;
                    if (item != null)
                    {
                        item.PropertyChanged -= PathItem_PropertyChanged;
                    }
                }
            }

            if (_isRefreshing)
            {
                return;
            }

            if (!IsMirrorTab)
            {
                QueueInputPlaceholderRefresh();
                RefreshScript();
                RaiseDataChanged();
            }
            else
            {
                RefreshScript();
            }
        }

        private void PathItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_isRefreshing)
            {
                return;
            }

            if (!IsMirrorTab && e.PropertyName == "OriginalInput")
            {
                var item = sender as PathItem;
                if (item != null)
                {
                    item.IsPlaceholder = false;
                    RefreshItem(item);
                    QueueInputPlaceholderRefresh();
                    RefreshScript();
                    RaiseDataChanged();
                }

                return;
            }

            if (e.PropertyName == "NormalizedInput" ||
                e.PropertyName == "MappedTarget" ||
                e.PropertyName == "EditOutputPath" ||
                e.PropertyName == "IsValid" ||
                e.PropertyName == "IsPlaceholder")
            {
                RefreshScript();
                if (e.PropertyName == "EditOutputPath")
                {
                    RaiseDataChanged();
                }
            }
        }

        private void RefreshAll()
        {
            if (IsMirrorTab && _masterTab != null)
            {
                _masterTab.RefreshAll();
                return;
            }

            _isRefreshing = true;
            try
            {
                for (int i = 0; i < SourceItems.Count; i++)
                {
                    RefreshItem(SourceItems[i]);
                }
            }
            finally
            {
                _isRefreshing = false;
            }

            EnsureInputPlaceholder();
            RefreshScript();
        }

        private void EnsureInputPlaceholder()
        {
            EnsureInputPlaceholderCore();
        }

        private void QueueInputPlaceholderRefresh()
        {
            if (IsMirrorTab)
            {
                return;
            }

            if (_placeholderRefreshPending)
            {
                return;
            }

            _placeholderRefreshPending = true;

            var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
            if (dispatcher == null)
            {
                _placeholderRefreshPending = false;
                EnsureInputPlaceholderCore();
                RefreshScript();
                RaiseDataChanged();
                return;
            }

            dispatcher.BeginInvoke(new Action(delegate
            {
                _placeholderRefreshPending = false;
                EnsureInputPlaceholderCore();
                RefreshScript();
                RaiseDataChanged();
            }), DispatcherPriority.Background);
        }

        private void EnsureInputPlaceholderCore()
        {
            if (IsMirrorTab)
            {
                return;
            }

            int nonEmptyCount = 0;
            for (int i = 0; i < SourceItems.Count; i++)
            {
                if (!SourceItems[i].IsEmpty)
                {
                    nonEmptyCount++;
                }
            }

            if (nonEmptyCount >= 1)
            {
                for (int i = SourceItems.Count - 1; i >= 0; i--)
                {
                    if (SourceItems[i].IsEmpty)
                    {
                        SourceItems.RemoveAt(i);
                    }
                }
            }
            else
            {
                PathItem lastEmpty = null;
                for (int i = 0; i < SourceItems.Count; i++)
                {
                    if (SourceItems[i].IsEmpty)
                    {
                        lastEmpty = SourceItems[i];
                    }
                }

                for (int i = SourceItems.Count - 1; i >= 0; i--)
                {
                    if (SourceItems[i].IsEmpty && !ReferenceEquals(SourceItems[i], lastEmpty))
                    {
                        SourceItems.RemoveAt(i);
                    }
                }

                if (lastEmpty == null)
                {
                    SourceItems.Add(new PathItem { IsPlaceholder = true });
                }
                else
                {
                    lastEmpty.IsPlaceholder = true;
                    RefreshItem(lastEmpty);
                }
            }
        }

        private void RefreshItem(PathItem item)
        {
            string original = CleanInput(item.OriginalInput);
            if (original.Length == 0)
            {
                item.NormalizedInput = string.Empty;
                item.MappedTarget = string.Empty;
                item.IsValid = false;
                item.ErrorMessage = "Path trong.";
                return;
            }

            try
            {
                MappingInfo info = BuildMappingInfo(original);
                item.NormalizedInput = info.NormalizedSource;
                item.MappedTarget = info.MappedTarget;
                item.IsValid = true;
                item.ErrorMessage = string.Empty;
            }
            catch (Exception ex)
            {
                item.NormalizedInput = original;
                item.MappedTarget = string.Empty;
                item.IsValid = false;
                item.ErrorMessage = "Path loi format: " + ex.Message;
            }
        }

        private MappingInfo BuildMappingInfo(string original)
        {
            string normalized = NormalizeSource(original);
            string expanded = Environment.ExpandEnvironmentVariables(normalized);

            // Dung Path.GetFullPath de bat format loi nhung khong yeu cau path ton tai.
            string fullPath = Path.GetFullPath(expanded);
            string relative = GetRelativeForTarget(normalized, expanded, fullPath);
            string mappedTarget = CombineTarget(RootTargetPath, relative);

            return new MappingInfo(normalized, mappedTarget);
        }

        private string NormalizeSource(string original)
        {
            string path = CleanInput(original);
            if (path.Length == 0)
            {
                return path;
            }

            string programFilesX86 = @"C:\Program Files (x86)\";
            string programFiles = @"C:\Program Files\";
            string programData = @"C:\ProgramData\";
            string systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
            if (string.IsNullOrWhiteSpace(systemDrive))
            {
                systemDrive = "C:";
            }

            string remainder;
            if (TryNormalizeUserProfile(path, out remainder))
            {
                return remainder;
            }

            if (TryNormalizeWithPrefix(path, programFilesX86, "%programfiles(x86)%", out remainder))
            {
                return remainder;
            }

            if (TryNormalizeWithPrefix(path, programFiles, "%programw6432%", out remainder))
            {
                return remainder;
            }

            if (TryNormalizeWithPrefix(path, programData, "%programdata%", out remainder))
            {
                return remainder;
            }

            if (path.Length >= 3 &&
                path[1] == ':' &&
                (path[2] == '\\' || path[2] == '/') &&
                string.Equals(path.Substring(0, 2), systemDrive, StringComparison.OrdinalIgnoreCase))
            {
                remainder = path.Substring(3);
                if (remainder.Length == 0)
                {
                    return "%systemdrive%\\";
                }

                return "%systemdrive%\\" + remainder;
            }

            return path;
        }

        private static bool TryNormalizeUserProfile(string path, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string prefix = @"C:\Users\";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int nextSeparator = path.IndexOf('\\', prefix.Length);
            if (nextSeparator < 0)
            {
                normalized = "%userprofile%";
                return true;
            }

            string remainder = path.Substring(nextSeparator + 1);
            if (remainder.Length == 0)
            {
                normalized = "%userprofile%";
                return true;
            }

            normalized = "%userprofile%\\" + remainder;
            return true;
        }

        private string GetRelativeForTarget(string normalized, string expanded, string fullPath)
        {
            if (TryStripNormalizedPrefix(normalized, "%userprofile%", out var userRelative))
            {
                return userRelative;
            }

            if (TryStripNormalizedPrefix(normalized, "%programfiles(x86)%", out var pf86Relative))
            {
                return Path.Combine("Program Files (x86)", pf86Relative);
            }

            if (TryStripNormalizedPrefix(normalized, "%programw6432%", out var pfRelative))
            {
                return Path.Combine("Program Files", pfRelative);
            }

            if (TryStripNormalizedPrefix(normalized, "%programdata%", out var programDataRelative))
            {
                return Path.Combine("ProgramData", programDataRelative);
            }

            if (TryStripNormalizedPrefix(normalized, "%systemdrive%", out var systemRelative))
            {
                return systemRelative;
            }

            if (!Path.IsPathRooted(expanded))
            {
                return normalized;
            }

            string root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root) && fullPath.Length > root.Length)
            {
                return fullPath.Substring(root.Length);
            }

            return normalized;
        }

        private string CombineTarget(string root, string relative)
        {
            string cleanRelative = CleanInput(relative).TrimStart('\\', '/');
            if (cleanRelative.Length == 0)
            {
                return root;
            }

            return Path.Combine(root, cleanRelative);
        }

        private static bool TryNormalizeWithPrefix(string path, string prefix, string normalizedPrefix, out string normalized)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string remainder = path.Substring(prefix.Length);
                normalized = string.IsNullOrEmpty(remainder)
                    ? normalizedPrefix + "\\"
                    : normalizedPrefix + "\\" + remainder;
                return true;
            }

            normalized = null;
            return false;
        }

        private static bool TryStripNormalizedPrefix(string path, string normalizedPrefix, out string remainder)
        {
            string prefixWithSlash = normalizedPrefix.EndsWith("\\", StringComparison.Ordinal)
                ? normalizedPrefix
                : normalizedPrefix + "\\";

            if (path.StartsWith(prefixWithSlash, StringComparison.OrdinalIgnoreCase))
            {
                remainder = path.Substring(prefixWithSlash.Length);
                return true;
            }

            if (string.Equals(path, normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                remainder = string.Empty;
                return true;
            }

            remainder = null;
            return false;
        }

        private bool CanGenerateCommand(PathItem item)
        {
            if (item == null || item.IsPlaceholder || !item.IsValid)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(item.NormalizedInput))
            {
                return false;
            }

            if (_scriptMode != ScriptMode.Delete &&
                _scriptMode != ScriptMode.ReverseDelete &&
                string.IsNullOrWhiteSpace(item.EffectiveOutputPath))
            {
                return false;
            }

            return true;
        }

        private string BuildCommand(PathItem item)
        {
            if (_scriptMode == ScriptMode.Copy)
            {
                string outputPath = item.EffectiveOutputPath;
                string command = "if not exist " + Quote(outputPath) + " mkdir " + Quote(outputPath) + Environment.NewLine +
                                 "xcopy " + Quote(item.NormalizedInput) + " " + Quote(outputPath) + " /E /I /Y";
                return command;
            }

            if (_scriptMode == ScriptMode.ReverseCopy)
            {
                return "xcopy " + Quote(item.EffectiveOutputPath) + " " + Quote(item.NormalizedInput) + " /E /I /Y";
            }

            if (_scriptMode == ScriptMode.Delete)
            {
                // DELETE lien ket voi Copy!A nhu file Excel.
                return "rmdir /s /q " + Quote(item.NormalizedInput);
            }

            if (_scriptMode == ScriptMode.ReverseDelete)
            {
                // DELETE REVERSE van xoa input, chi co ReverseCopy la dao chieu.
                return "rmdir /s /q " + Quote(item.NormalizedInput);
            }

            // MKLINK D lien ket voi Copy!A va Copy!B.
            return "mklink /D " + Quote(item.NormalizedInput) + " " + Quote(item.EffectiveOutputPath);
        }

        private string Quote(string value)
        {
            return "\"" + (value ?? string.Empty) + "\"";
        }

        private void RaiseDataChanged()
        {
            var handler = DataChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private static string CleanInput(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return value.Trim().Trim('"');
        }

        private void RecordHistory()
        {
            if (_owner != null && !_owner.IsRestoringHistory)
            {
                _owner.CaptureUndoSnapshot();
            }
        }

        private bool CanUndo()
        {
            return _owner != null && _owner.CanUndo;
        }

        private bool CanRedo()
        {
            return _owner != null && _owner.CanRedo;
        }

        private void Undo()
        {
            if (_owner != null)
            {
                _owner.Undo();
            }
        }

        private void Redo()
        {
            if (_owner != null)
            {
                _owner.Redo();
            }
        }
    }

    public enum ScriptMode
    {
        Copy,
        Delete,
        MklinkD,
        ReverseCopy,
        ReverseDelete
    }

    public enum EditOutputFilterKind
    {
        All,
        Default,
        Edited
    }

    public class MappingInfo
    {
        public MappingInfo(string normalizedSource, string mappedTarget)
        {
            NormalizedSource = normalizedSource;
            MappedTarget = mappedTarget;
        }

        public string NormalizedSource { get; private set; }

        public string MappedTarget { get; private set; }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute)
            : this(execute, null)
        {
        }

        public RelayCommand(Action execute, Func<bool> canExecute)
        {
            if (execute == null)
            {
                throw new ArgumentNullException("execute");
            }

            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute();
        }

        public void Execute(object parameter)
        {
            _execute();
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }

    public class NotifyObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    public class MarkdownDocumentState
    {
        public string SelectedRootFolder { get; set; }

        public string SubfolderName { get; set; }

        public string RootTargetPath { get; set; }

        public bool IsWordWrapEnabled { get; set; }

        public List<string> InputLines { get; set; }

        public List<MarkdownPathRowState> PathRows { get; set; }
    }

    public class MarkdownPathRowState
    {
        public string OriginalInput { get; set; }

        public string NormalizedInput { get; set; }

        public string OutputPath { get; set; }

        public string EditOutputPath { get; set; }
    }

    public static class MarkdownStateSerializer
    {
        private const string Heading = "# MKLink Path Mapper";

        private static string EscapePipe(string text)
        {
            if (text == null)
            {
                return string.Empty;
            }
            return text.Replace("|", "\\|");
        }

        private static string FormatPathForTable(string path)
        {
            if (path == null)
            {
                return string.Empty;
            }
            // In Markdown, a backslash before '<' escapes it. To prevent this, we write double backslashes (\\<wbr>).
            string result = path.Replace("\\", "\\\\<wbr>").Replace("/", "/<wbr>");
            return EscapePipe(result);
        }

        private static string CleanTablePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }
            string cleaned = path
                .Replace("<wbr>", "")
                .Replace("<wbr />", "")
                .Replace("<WBR>", "")
                .Replace("<WBR />", "")
                .Replace("&#8203;", "");

            // Clean up double backslashes (preserving UNC paths starting with \\)
            if (cleaned.StartsWith("\\\\"))
            {
                return "\\\\" + cleaned.Substring(2).Replace("\\\\", "\\");
            }
            return cleaned.Replace("\\\\", "\\");
        }

        public static string Serialize(MainViewModel viewModel)
        {
            if (viewModel == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            builder.AppendLine(Heading);
            builder.AppendLine();
            builder.AppendLine("<style>");
            builder.AppendLine("  table {");
            builder.AppendLine("    width: 100%;");
            builder.AppendLine("    table-layout: fixed;");
            builder.AppendLine("  }");
            builder.AppendLine("  td, th {");
            builder.AppendLine("    word-break: break-all;");
            builder.AppendLine("    word-wrap: break-word;");
            builder.AppendLine("  }");
            builder.AppendLine("</style>");
            builder.AppendLine();
            builder.AppendLine("- SelectedRootFolder: " + viewModel.CopyTab.SelectedRootFolder);
            builder.AppendLine("- SubfolderName: " + viewModel.CopyTab.SubfolderName);
            builder.AppendLine("- RootTargetPath: " + viewModel.CopyTab.RootTargetPath);
            builder.AppendLine("- IsWordWrapEnabled: " + viewModel.IsWordWrapEnabled);
            builder.AppendLine();

            var validItems = new List<PathItem>();
            for (int i = 0; i < viewModel.CopyTab.SourceItems.Count; i++)
            {
                PathItem item = viewModel.CopyTab.SourceItems[i];
                if (item != null && !item.IsPlaceholder && !string.IsNullOrWhiteSpace(item.OriginalInput))
                {
                    validItems.Add(item);
                }
            }

            var sortedItems = validItems.OrderByDescending(item => !string.IsNullOrWhiteSpace(item.EditOutputPath)).ToList();

            builder.AppendLine("## INPUT");
            builder.AppendLine();
            builder.AppendLine("| Input | Normalize Input |");
            builder.AppendLine("| --- | --- |");
            for (int i = 0; i < sortedItems.Count; i++)
            {
                PathItem item = sortedItems[i];
                builder.AppendLine(string.Format("| {0} | {1} |",
                    FormatPathForTable(item.OriginalInput),
                    FormatPathForTable(item.NormalizedInput)));
            }
            builder.AppendLine();

            builder.AppendLine("## OUTPUT");
            builder.AppendLine();
            builder.AppendLine("| Output | Edit Output |");
            builder.AppendLine("| --- | --- |");
            for (int i = 0; i < sortedItems.Count; i++)
            {
                PathItem item = sortedItems[i];
                builder.AppendLine(string.Format("| {0} | {1} |",
                    FormatPathForTable(item.MappedTarget),
                    FormatPathForTable(item.EditOutputPath)));
            }

            return builder.ToString();
        }

        public static MarkdownDocumentState Deserialize(string markdown)
        {
            var state = new MarkdownDocumentState
            {
                InputLines = new List<string>(),
                PathRows = new List<MarkdownPathRowState>()
            };

            if (string.IsNullOrWhiteSpace(markdown))
            {
                return state;
            }

            var inputRows = new List<Tuple<string, string>>();
            var outputRows = new List<Tuple<string, string>>();

            using (var reader = new StringReader(markdown))
            {
                string line;
                bool inCodeBlock = false;
                bool inRowBlock = false;
                bool inInputBlock = false;
                bool inOutputBlock = false;

                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("- SelectedRootFolder:", StringComparison.OrdinalIgnoreCase))
                    {
                        state.SelectedRootFolder = UnwrapValue(trimmed.Substring("- SelectedRootFolder:".Length));
                        continue;
                    }

                    if (trimmed.StartsWith("- SubfolderName:", StringComparison.OrdinalIgnoreCase))
                    {
                        state.SubfolderName = UnwrapValue(trimmed.Substring("- SubfolderName:".Length));
                        continue;
                    }

                    if (trimmed.StartsWith("- RootTargetPath:", StringComparison.OrdinalIgnoreCase))
                    {
                        state.RootTargetPath = UnwrapValue(trimmed.Substring("- RootTargetPath:".Length));
                        continue;
                    }

                    if (trimmed.StartsWith("- IsWordWrapEnabled:", StringComparison.OrdinalIgnoreCase))
                    {
                        state.IsWordWrapEnabled = ParseBool(UnwrapValue(trimmed.Substring("- IsWordWrapEnabled:".Length)));
                        continue;
                    }

                    if (trimmed == "## COPY ROWS")
                    {
                        inRowBlock = true;
                        inInputBlock = false;
                        inOutputBlock = false;
                        continue;
                    }

                    if (trimmed == "## INPUT")
                    {
                        inRowBlock = false;
                        inInputBlock = true;
                        inOutputBlock = false;
                        continue;
                    }

                    if (trimmed == "## OUTPUT")
                    {
                        inRowBlock = false;
                        inInputBlock = false;
                        inOutputBlock = true;
                        continue;
                    }

                    if (trimmed == "```paths")
                    {
                        inCodeBlock = true;
                        continue;
                    }

                    if (trimmed == "```")
                    {
                        if (inCodeBlock)
                        {
                            inCodeBlock = false;
                        }
                        continue;
                    }

                    if (inCodeBlock && trimmed.Length > 0)
                    {
                        if (inRowBlock)
                        {
                            string[] parts = line.Split(new[] { '\t' }, 4);
                            state.PathRows.Add(new MarkdownPathRowState
                            {
                                OriginalInput = parts.Length > 0 ? parts[0] : string.Empty,
                                NormalizedInput = parts.Length > 1 ? parts[1] : string.Empty,
                                OutputPath = parts.Length > 2 ? parts[2] : string.Empty,
                                EditOutputPath = parts.Length > 3 ? parts[3] : string.Empty
                            });
                        }
                        else
                        {
                            state.InputLines.Add(line);
                        }
                        continue;
                    }

                    if (trimmed.StartsWith("|") && trimmed.EndsWith("|"))
                    {
                        if (trimmed.IndexOf("Input", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            trimmed.IndexOf("Output", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            trimmed.IndexOf("Original Input", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            trimmed.IndexOf("---") >= 0)
                        {
                            continue;
                        }

                        string temp = trimmed.Replace("\\|", "\x01");
                        string[] rawParts = temp.Split('|');

                        if (inRowBlock)
                        {
                            if (rawParts.Length >= 5)
                            {
                                var parts = new List<string>();
                                for (int i = 1; i < rawParts.Length - 1; i++)
                                {
                                    string val = rawParts[i].Trim().Replace("\x01", "|");
                                    parts.Add(CleanTablePath(val));
                                }

                                state.PathRows.Add(new MarkdownPathRowState
                                {
                                    OriginalInput = parts.Count > 0 ? parts[0] : string.Empty,
                                    NormalizedInput = parts.Count > 1 ? parts[1] : string.Empty,
                                    OutputPath = parts.Count > 2 ? parts[2] : string.Empty,
                                    EditOutputPath = parts.Count > 3 ? parts[3] : string.Empty
                                });
                            }
                        }
                        else if (inInputBlock)
                        {
                            if (rawParts.Length >= 3)
                            {
                                string originalInput = CleanTablePath(rawParts[1].Trim().Replace("\x01", "|"));
                                string normalizedInput = CleanTablePath(rawParts[2].Trim().Replace("\x01", "|"));
                                inputRows.Add(Tuple.Create(originalInput, normalizedInput));
                            }
                        }
                        else if (inOutputBlock)
                        {
                            if (rawParts.Length >= 3)
                            {
                                string outputPath = CleanTablePath(rawParts[1].Trim().Replace("\x01", "|"));
                                string editOutputPath = CleanTablePath(rawParts[2].Trim().Replace("\x01", "|"));
                                outputRows.Add(Tuple.Create(outputPath, editOutputPath));
                            }
                        }
                    }
                }
            }

            if (inputRows.Count > 0 || outputRows.Count > 0)
            {
                int count = Math.Max(inputRows.Count, outputRows.Count);
                for (int i = 0; i < count; i++)
                {
                    var inputRow = i < inputRows.Count ? inputRows[i] : Tuple.Create(string.Empty, string.Empty);
                    var outputRow = i < outputRows.Count ? outputRows[i] : Tuple.Create(string.Empty, string.Empty);

                    state.PathRows.Add(new MarkdownPathRowState
                    {
                        OriginalInput = inputRow.Item1,
                        NormalizedInput = inputRow.Item2,
                        OutputPath = outputRow.Item1,
                        EditOutputPath = outputRow.Item2
                    });
                }
            }

            if (state.PathRows == null)
            {
                state.PathRows = new List<MarkdownPathRowState>();
            }

            return state;
        }

        private static string UnwrapValue(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            string result = value.Trim();
            if (result.StartsWith("`", StringComparison.Ordinal) && result.EndsWith("`", StringComparison.Ordinal) && result.Length >= 2)
            {
                result = result.Substring(1, result.Length - 2);
            }

            return result;
        }

        private static bool ParseBool(string value)
        {
            bool parsed;
            if (bool.TryParse(value, out parsed))
            {
                return parsed;
            }

            return true;
        }
    }
}
