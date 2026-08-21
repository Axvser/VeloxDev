using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Demo.ViewModels;
using System;
using System.Collections.Specialized;
using VeloxDev.AspectOriented;

namespace Demo.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        _manager = new WindowNotificationManager(this) { MaxItems = 3 };
        _teamData = new TeamViewModel();
        ConfigureAOP(_teamData);
    }

    private readonly WindowNotificationManager _manager;
    private readonly TeamViewModel _teamData;

    private void Click0(object sender, RoutedEventArgs e)
    {
        var team = _teamData.Aop();
        if (team.Members.Count > 0) team.Members.RemoveAt(0);
    }

    private void Click1(object sender, RoutedEventArgs e)
    {
        _teamData.Aop().Members.Add(new MemberViewModel() { Name = "Jack" });
    }

    private void Click2(object sender, RoutedEventArgs e)
    {
        _ = _teamData.Aop().Name;
    }

    private void Click3(object sender, RoutedEventArgs e)
    {
        _teamData.Aop().Name = "New Team Name";
    }

    private void Click4(object sender, RoutedEventArgs e)
    {
        _teamData.Aop().Reset();
    }

    /* No need to modify the ViewModel source — Aop() automatically caches and returns an AOP proxy */
    private void ConfigureAOP(TeamViewModel data)
    {
        var p = data.Aop();

        /* Before hook: before Name is read */
        p.SetProxy(ProxyMembers.Getter,
            nameof(TeamViewModel.Name),
            (_, _) => { _manager.Show(new Notification("Message", $"a read operation happened at [{DateTime.Now}]")); return null; },
            null,
            null);

        /* After hook: after Name is changed */
        p.SetProxy(ProxyMembers.Setter,
            nameof(TeamViewModel.Name),
            null,
            null,
            (p, _) => { _manager.Show(new Notification("Message", $"the name of team has been changed to {p?[0]}")); return null; });

        /* Override original logic: when Reset() is called */
        p.SetProxy(ProxyMembers.Method,
            nameof(TeamViewModel.Reset),
            null,
            (_, _) => { _manager.Show(new Notification("Message", $"the default Reset() has been cancelled")); return null; },
            null);

        /* Extension: when a member is added to Members */
        p.SetProxy(ProxyMembers.Method,
            nameof(TeamViewModel.AOP_OnMemberAdded),
            null,
            null,
            (p, _) =>
            {
                if (p?[1] is not NotifyCollectionChangedEventArgs e || e.NewItems is null) return null;
                foreach (MemberViewModel member in e.NewItems)
                    _manager.Show(new Notification("Message", $"a member named [{member.Name}] has been added"));
                return null;
            });

        /* Extension: when a member is removed from Members */
        p.SetProxy(ProxyMembers.Method,
            nameof(TeamViewModel.AOP_OnMemberRemoved),
            null,
            null,
            (p, _) =>
            {
                if (p?[1] is not NotifyCollectionChangedEventArgs e || e.OldItems is null) return null;
                foreach (MemberViewModel member in e.OldItems)
                    _manager.Show(new Notification("Message", $"a member named [{member.Name}] has been removed"));
                return null;
            });
    }
}