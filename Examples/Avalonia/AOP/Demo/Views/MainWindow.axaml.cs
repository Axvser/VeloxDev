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
    private readonly TeamViewModel _teamData;      // ԭʼ���ݷ���
    private readonly TeamViewModel_Demo_ViewModels_Aop _team; // AOP�������

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

    /* ���ǲ���Ҫ�޸� ViewModel ���κ�Դ�룬���ڣ�����һЩ��Ա�Ѿ�֧�� AOP */
    private TeamViewModel_Demo_ViewModels_Aop ConfigureAOP(TeamViewModel data)
    {
        /* ǰ�ù��ӣ� Team �� Name ����ȡ[ǰ] */
        data.Proxy.SetProxy(ProxyMembers.Getter,
            nameof(TeamViewModel.Name),
            (p, r) =>
            {
                _manager.Show(new Notification("Message", $"a read operation happened at [{DateTime.Now}]"));
                return null;
            },
            null,
            null);

        /* ���ù��ӣ� Team �� Name ������[��] */
        data.Proxy.SetProxy(ProxyMembers.Setter,
            nameof(TeamViewModel.Name),
            null,
            null,
            (p, r) =>
            {
                _manager.Show(new Notification("Message", $"the name of team has been changed to {p?[0]}"));
                return null;
            });

        /* ��дԭ�߼��� Team �� Reset ����������[ʱ] */
        data.Proxy.SetProxy(ProxyMembers.Method,
            nameof(TeamViewModel.Reset),
            null,
            (p, r) =>
            {
                _manager.Show(new Notification("Message", $"the default Reset() has been cancle"));
                return null;
            },
            null);

        /* ��չ�� Team �� Members���� �г�Ա�����ʱ */
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

        /* ��չ�� Team �� Members���� �г�Ա���Ƴ�ʱ */
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

        /* ���ͣ�SetProxy�ĺ����������ֱ��ǡ�ǰ�ù��ӡ�����д���ӡ������ù��ӡ�*/
        /* ���ͣ�
         ���ӵ�һ��������ʾ�˷���������ʱ���յ��Ĳ���
         ���ӵڶ���������ʾ��һ�����ӻ���ԭʼ�߼���ִ�з���ֵ
         */
    }
}