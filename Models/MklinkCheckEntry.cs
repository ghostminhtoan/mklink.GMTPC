using System.Windows.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MKLink.Models
{
    public class MklinkCheckEntry : INotifyPropertyChanged
    {
        private bool _isSelected;

        public MklinkCheckEntry(int sourceIndex, string normalizedInput, string outputPath, bool exists, bool isLink, string status)
        {
            SourceIndex = sourceIndex;
            NormalizedInput = normalizedInput;
            OutputPath = outputPath;
            Exists = exists;
            IsLink = isLink;
            Status = status;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public int SourceIndex { get; private set; }

        public string NormalizedInput { get; private set; }

        public string OutputPath { get; private set; }

        public bool Exists { get; private set; }

        public bool IsLink { get; private set; }

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
                if (!Exists)
                {
                    return Brushes.LightGray;
                }

                return IsLink ? Brushes.IndianRed : Brushes.White;
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
