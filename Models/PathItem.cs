using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MKLink.Models
{
    public class PathItem : INotifyPropertyChanged
    {
        private string _originalInput;
        private string _normalizedInput;
        private string _mappedTarget;
        private bool _isValid;
        private bool _isPlaceholder;
        private string _errorMessage;

        public string OriginalInput
        {
            get { return _originalInput; }
            set
            {
                if (_originalInput != value)
                {
                    _originalInput = value;
                    OnPropertyChanged();
                    OnPropertyChanged("IsEmpty");
                }
            }
        }

        public string NormalizedInput
        {
            get { return _normalizedInput; }
            set
            {
                if (_normalizedInput != value)
                {
                    _normalizedInput = value;
                    OnPropertyChanged();
                }
            }
        }

        public string MappedTarget
        {
            get { return _mappedTarget; }
            set
            {
                if (_mappedTarget != value)
                {
                    _mappedTarget = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsValid
        {
            get { return _isValid; }
            set
            {
                if (_isValid != value)
                {
                    _isValid = value;
                    OnPropertyChanged();
                    OnPropertyChanged("ShowValidationError");
                }
            }
        }

        public bool IsPlaceholder
        {
            get { return _isPlaceholder; }
            set
            {
                if (_isPlaceholder != value)
                {
                    _isPlaceholder = value;
                    OnPropertyChanged();
                    OnPropertyChanged("ShowValidationError");
                }
            }
        }

        public bool IsEmpty
        {
            get { return string.IsNullOrWhiteSpace(OriginalInput); }
        }

        public bool ShowValidationError
        {
            get { return !IsPlaceholder && !IsValid; }
        }

        public string ErrorMessage
        {
            get { return _errorMessage; }
            set
            {
                if (_errorMessage != value)
                {
                    _errorMessage = value;
                    OnPropertyChanged();
                }
            }
        }

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
}
