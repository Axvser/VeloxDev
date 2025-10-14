using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using System;
using Tmds.DBus.Protocol;
using VeloxDev.Avalonia.PlatformAdapters;
using VeloxDev.Core.DynamicTheme;
using VeloxDev.Core.TransitionSystem;

namespace Demo.Views;

/* ���ǽ�������������صĲ�������������һ���ֲ��У������������������߼�ʱ�������ܵ��޹ش���Ĵ��� */
/* ע�⣺����ʹ��Riderʱ����ô�����ܻ�����޷�ʶ���������ݵ����⣬��Ӱ����룬���ǿ���ֻ������Rider���ָܻ�ʶ�� */

//------------------------------------------------------------------------------------------------------------------
// User Part ��

[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["#ffffff"])]
[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Foreground), ["#ffffff"], ["#1e1e1e"])]
public partial class MainWindow : Window
{
    private readonly WindowNotificationManager _message;

    public MainWindow()
    {
        InitializeComponent();
        _message = new WindowNotificationManager(this) { MaxItems = 3 };
        LoadTheme();
    }

    private void ChangeTheme(object sender, RoutedEventArgs e)
    {
        ReverseThemeWithAnimation();
    }
}

[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["#ffffff"])]
[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Foreground), ["#ffffff"], ["#1e1e1e"])]
public partial class MainWindow
{
    private void LoadTheme()
    {
        InitializeTheme(); // ��仰�������,�ұ�������InitializeComponent()

        // [ ȫ����Ч ]
        // �������ʹ�ô�����Ч���������л�����ô���Բ����ò�ֵ����������仰�Ǳ�����õ�
        ThemeManager.SetPlatformInterpolator(new Interpolator());

        // [ ȫ����Ч ]
        // �����ⷢ���仯����ϣ����������ʼ״̬�Ǵӻ����ȡ�أ����Ƿ����ȡ��ǰ״̬��Ϊ��ʼ�أ�
        ThemeManager.StartModel = StartModel.Cache;
    }

    /// <summary>
    /// �����л��߱��ص�
    /// </summary>
    /// <param name="oldValue">�л�ǰ��ֵ</param>
    /// <param name="newValue">�л����ֵ</param>
    partial void OnThemeChanged(Type? oldValue, Type? newValue)
    {
        _message.Show(new Notification("Message", $"Theme changed from {oldValue?.Name} to {newValue?.Name}"));
    }

    /// <summary>
    /// ���������л�����ؽ��䶯��
    /// </summary>
    private static void ReverseThemeWithAnimation()
    {
        var condition = ThemeManager.Current == typeof(Dark);
        if (condition)
        {
            ThemeManager.Transition<Light>(TransitionEffects.Theme);
        }
        else
        {
            ThemeManager.Transition<Dark>(TransitionEffects.Theme);
        }
    }

    /// <summary>
    /// ���������л�û�н��䶯��
    /// </summary>
    private static void ReverseThemeWithOutAnimation()
    {
        var condition = ThemeManager.Current == typeof(Dark);
        if (condition)
        {
            ThemeManager.Jump<Light>();
        }
        else
        {
            ThemeManager.Jump<Dark>();
        }
    }

    /// <summary>
    /// �ṩһ���ȡ���༭������Դ������չ����Щ�������Զ����ɵģ�����˴����Ƕ���MainWindow�ķ���
    /// </summary>
    private void ThemeValueEx()
    {
        // ��̬�༭������Դֵ
        EditThemeValue<Light>(nameof(Background), new object?[] { "#ffffff" });
        // ���Իָ�Ϊ��ʼ״̬
        RestoreThemeValue<Light>(nameof(Foreground));

        // ��ȡ��̬��Դ
        var staticCache = GetStaticCache();
        // ��ȡ��̬��Դ
        var dynamicCache = GetActiveCache();

        /* �˴��ġ���Դ����һ���Զ����ɵĸ��ӽṹ
           ֻ�б��޸Ĺ������ԲŻ�洢�ڶ�̬��Դ�У�������Դ�ڲ���洢�������л�����ʱ����̬���ݽ����Ǿ�̬����
           Dictionary<string,Dictionary<PropertyInfo,Dictionary<Type,object?>>>

           ��������
           string       -> name of property
           PropertyInfo -> target to use theme change
           Type         -> theme
           object?      -> value of property at the theme

           ���ṩ����ȫ����������Դ������
         */
    }
}