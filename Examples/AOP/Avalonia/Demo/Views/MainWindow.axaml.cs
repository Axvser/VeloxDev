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

    // 不必改动 ViewModel 源码：Aop() 会自动缓存并返回 AOP 代理
    private void ConfigureAOP(TeamViewModel data)
    {
        var p = data.Aop();

        // 前置钩子：读 Name 之前
        p.SetProxy(ProxyMembers.Getter,
            nameof(TeamViewModel.Name),
            (_, _) => { _manager.Show(new Notification("Message", $"a read operation happened at [{DateTime.Now}]")); return null; },
            null,
            null);

        // 后置钩子：改 Name 之后
        p.SetProxy(ProxyMembers.Setter,
            nameof(TeamViewModel.Name),
            null,
            null,
            (p, _) => { _manager.Show(new Notification("Message", $"the name of team has been changed to {p?[0]}")); return null; });

        // 覆盖原逻辑：调用 Reset() 时
        p.SetProxy(ProxyMembers.Method,
            nameof(TeamViewModel.Reset),
            null,
            (_, _) => { _manager.Show(new Notification("Message", $"the default Reset() has been cancelled")); return null; },
            null);

        // 扩展：Members 新增成员时
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

        // 扩展：Members 移除成员时
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