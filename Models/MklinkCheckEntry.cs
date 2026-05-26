using System;
using System.IO;
using System.Windows.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MKLink.Models
{
    public class MklinkCheckEntry : INotifyPropertyChanged
    {
        private bool _isSelected;

        public MklinkCheckEntry(int sourceIndex, string normalizedInput, string outputPath, bool exists, bool isLink, bool destinationCheck, string alreadyMklinkTo, string status)
        {
            SourceIndex = sourceIndex;
            NormalizedInput = normalizedInput;
            OutputPath = outputPath;
            Exists = exists;
            IsLink = isLink;
            DestinationCheck = destinationCheck;
            AlreadyMklinkTo = alreadyMklinkTo;
            Status = status;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public int SourceIndex { get; private set; }

        public string NormalizedInput { get; private set; }

        public string OutputPath { get; private set; }

        public bool Exists { get; private set; }

        public bool IsLink { get; private set; }

        public bool DestinationCheck { get; private set; }

        public string AlreadyMklinkTo { get; private set; }

        public string Status { get; private set; }

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public Brush StatusBrush
        {
            get
            {
                if (string.Equals(Status, "MKLINK", System.StringComparison.OrdinalIgnoreCase))
                {
                    return Brushes.Cyan;
                }

                if (string.Equals(Status, "Real folder", System.StringComparison.OrdinalIgnoreCase))
                {
                    return Brushes.Gold;
                }

                return Brushes.White;
            }
        }

        public bool IsMatched
        {
            get
            {
                if (string.IsNullOrEmpty(OutputPath) || string.IsNullOrEmpty(AlreadyMklinkTo))
                {
                    return false;
                }

                string p1 = OutputPath;
                try
                {
                    p1 = Environment.ExpandEnvironmentVariables(p1);
                    p1 = Path.GetFullPath(p1);
                }
                catch { }

                string p2 = AlreadyMklinkTo;
                try
                {
                    p2 = Environment.ExpandEnvironmentVariables(p2);
                    p2 = Path.GetFullPath(p2);
                }
                catch { }

                p1 = p1.Replace('/', '\\').TrimEnd('\\');
                p2 = p2.Replace('/', '\\').TrimEnd('\\');

                return string.Equals(p1, p2, StringComparison.OrdinalIgnoreCase);
            }
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
