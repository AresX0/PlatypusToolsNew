using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PlatypusTools.Core.Models
{
    /// <summary>
    /// Base class for models that need property change notification.
    /// Provides standard INotifyPropertyChanged implementation.
    /// </summary>
    public abstract class BindableModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
                return false;
            
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
