using System.Collections.Specialized;
using System.ComponentModel;

namespace Demo;

public abstract class ObservableViewModelBase : INotifyPropertyChanging, INotifyPropertyChanged
{
    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;

    public virtual void OnPropertyChanging(string propertyName)
    {
        PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
    }

    public virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected virtual void OnCollectionChanged<T>(string propertyName, NotifyCollectionChangedEventArgs e, IEnumerable<T>? oldItems, IEnumerable<T>? newItems)
    {
    }
}
