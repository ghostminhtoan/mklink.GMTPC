using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MKLink.Models
{
    public class PathItem : INotifyPropertyChanged
    {
        private string _originalInput;
        private string _normalizedInput;
        private string _mappedTarget;
        private string _editOutputPath;
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
                    if (!string.IsNullOrWhiteSpace(_editOutputPath) &&
                        string.Equals(_editOutputPath, _mappedTarget, System.StringComparison.OrdinalIgnoreCase))
                    {
                        _editOutputPath = string.Empty;
                    }
                    OnPropertyChanged();
                    OnPropertyChanged("EditOutputPath");
                    OnPropertyChanged("IsEdited");
                    OnPropertyChanged("EditOutputStateText");
                    OnPropertyChanged("EffectiveOutputPath");
                }
            }
        }

        public string EditOutputPath
        {
            get { return _editOutputPath; }
            set
            {
                string normalized = NormalizeEditOutputPath(value);
                if (_editOutputPath != normalized)
                {
                    _editOutputPath = normalized;
                    OnPropertyChanged();
                    OnPropertyChanged("IsEdited");
                    OnPropertyChanged("EditOutputStateText");
                    OnPropertyChanged("EffectiveOutputPath");
                }
            }
        }

        public string EffectiveOutputPath
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_editOutputPath))
                {
                    return _editOutputPath;
                }

                return _mappedTarget;
            }
        }

        public bool IsEdited
        {
            get { return !string.IsNullOrWhiteSpace(_editOutputPath); }
        }

        public string EditOutputStateText
        {
            get { return IsEdited ? "Edited" : "Default"; }
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

        private string NormalizeEditOutputPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string cleaned = value.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(_mappedTarget) &&
                string.Equals(cleaned, _mappedTarget, System.StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return cleaned;
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
