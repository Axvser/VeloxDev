using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Demo.ViewModels;
using System;
using System.Collections.Specialized;
using VeloxDev.Core.AopInterfaces;
using VeloxDev.Core.AspectOriented;

namespace Demo.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        _manager = new WindowNotificationManager(this) { MaxItems = 3 };
        _teamData = new TeamViewModel();
        _team = ConfigureAOP(_teamData);
    }

    private readonly WindowNotificationManager _manager;
    private readonly TeamViewModel _teamData;      // 原始数据访问
    private readonly TeamViewModel_Demo_ViewModels_Aop _team; // AOP代理访问

    private void Click0(object sender, RoutedEventArgs e)
    {
        if (_team.Members.Count > 0) _team.Members.RemoveAt(0);
    }

    private void Click1(object sender, RoutedEventArgs e)
    {
        _team.Members.Add(new MemberViewModel() { Name = "Jack" });
    }

    private void Click2(object sender, RoutedEventArgs e)
    {
        _ = _team.Name;
    }

    private void Click3(object sender, RoutedEventArgs e)
    {
        _team.Name = "New Team Name";
    }

    private void Click4(object sender, RoutedEventArgs e)
    {
        _team.Reset();
    }

    /* 我们不需要修改 ViewModel 的任何源码，现在，它的一些成员已经支持 AOP */
    private TeamViewModel_Demo_ViewModels_Aop ConfigureAOP(TeamViewModel data)
    {
        /* 前置钩子： Team 的 Name 被读取[前] */
        data.Proxy.SetProxy(ProxyMembers.Getter,
            nameof(TeamViewModel.Name),
            (p, r) =>
            {
                _manager.Show(new Notification("Message", $"a read operation happened at [{DateTime.Now}]"));
                return null;
            },
            null,
            null);

        /* 后置钩子： Team 的 Name 被更改[后] */
        data.Proxy.SetProxy(ProxyMembers.Setter,
            nameof(TeamViewModel.Name),
            null,
            null,
            (p, r) =>
            {
                _manager.Show(new Notification("Message", $"the name of team has been changed to {p?[0]}"));
                return null;
            });

        /* 覆写原逻辑： Team 的 Reset 方法被调用[时] */
        data.Proxy.SetProxy(ProxyMembers.Method,
            nameof(TeamViewModel.Reset),
            null,
            (p, r) =>
            {
                _manager.Show(new Notification("Message", $"the default Reset() has been cancle"));
                return null;
            },
            null);

        /* 扩展： Team 的 Members集合 有成员被添加时 */
        data.Proxy.SetProxy(ProxyMembers.Method,
            nameof(TeamViewModel.AOP_OnMemberAdded),
            null,
            null,
            (p, r) =>
            {
                if (p?[1] is not NotifyCollectionChangedEventArgs e || e.NewItems is null) return null;
                foreach (MemberViewModel member in e.NewItems)
                {
                    _manager.Show(new Notification("Message", $"a member named [{member.Name}] has been added"));
                }
                return null;
            });

        /* 扩展： Team 的 Members集合 有成员被移除时 */
        data.Proxy.SetProxy(ProxyMembers.Method,
            nameof(TeamViewModel.AOP_OnMemberRemoved),
            null,
            null,
            (p, r) =>
            {
                if (p?[1] is not NotifyCollectionChangedEventArgs e || e.OldItems is null) return null;
                foreach (MemberViewModel member in e.OldItems)
                {
                    _manager.Show(new Notification("Message", $"a member named [{member.Name}] has been removed"));
                }
                return null;
            });

        return data.Proxy;

        /* 解释：SetProxy的后三个参数分别是【前置钩子】【覆写钩子】【后置钩子】*/
        /* 解释：
         钩子第一个参数表示此方法被调用时接收到的参数
         钩子第二个参数表示上一个钩子或者原始逻辑的执行返回值
         */
    }
}