using System.Collections.ObjectModel;
using System.Collections.Specialized;
using VeloxDev.Core.AspectOriented;
using VeloxDev.Core.MVVM;

namespace Demo.ViewModels;

public partial class TeamViewModel
{
    public TeamViewModel()
    {
        _members.CollectionChanged += OnMemberAdded;
        _members.CollectionChanged += OnMemberRemoved;
    }

    [VeloxProperty][AspectOriented] private string _name = string.Empty;
    [VeloxProperty][AspectOriented] private ObservableCollection<MemberViewModel> _members = [];

    [AspectOriented]
    public void Reset()
    {
        Name = string.Empty;
        Members.Clear();
    }

    partial void OnMembersChanged(ObservableCollection<MemberViewModel> oldValue,
        ObservableCollection<MemberViewModel> newValue)
    {
        oldValue.CollectionChanged -= OnMemberAdded;
        oldValue.CollectionChanged -= OnMemberRemoved;
        newValue.CollectionChanged += OnMemberAdded;
        newValue.CollectionChanged += OnMemberRemoved;
    }

    private void OnMemberAdded(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Proxy.AOP_OnMemberAdded(sender, e);
    }
    [AspectOriented]
    public void AOP_OnMemberAdded(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
    }

    private void OnMemberRemoved(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Proxy.AOP_OnMemberRemoved(sender, e);
    }
    [AspectOriented]
    public void AOP_OnMemberRemoved(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Remove) return;
    }
}