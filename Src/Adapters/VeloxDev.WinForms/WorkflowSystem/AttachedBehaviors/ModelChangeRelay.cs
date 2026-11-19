using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

// 把一个或多个模型对象的属性变更编组到控件自己的线程上再回调。
// 三个视图控件（slot / link / node）此前各写过一遍同样的「订阅、退订、InvokeRequired 编排」，
// 而那段编排是容易写错的一类代码，所以只留一份。
internal sealed class ModelChangeRelay
{
    private readonly Control _host;
    private readonly Action<PropertyChangedEventArgs> _onChanged;
    private readonly List<INotifyPropertyChanged> _models = [];

    internal ModelChangeRelay(Control host, Action<PropertyChangedEventArgs> onChanged)
    {
        _host = host;
        _onChanged = onChanged;
    }

    // 换掉当前订阅的对象；null 表示退订。视图换绑定时用这个。
    internal void Set(INotifyPropertyChanged? model)
    {
        Clear();
        Add(model);
    }

    // 追加一个订阅（连线要同时跟着两端走）。重复的忽略。
    internal void Add(INotifyPropertyChanged? model)
    {
        if (model is null || _models.Contains(model)) return;

        _models.Add(model);
        model.PropertyChanged += Handle;
    }

    internal void Clear()
    {
        foreach (var model in _models)
        {
            model.PropertyChanged -= Handle;
        }

        _models.Clear();
    }

    private void Handle(object? sender, PropertyChangedEventArgs e)
    {
        // 模型可能从别的线程改（节点的锚点由编译期/后台写入）。控件还没建句柄时 InvokeRequired 是 false，
        // 直接回调 —— 与搬过来的原逻辑一致。
        if (_host.InvokeRequired)
        {
            _host.BeginInvoke(new PropertyChangedEventHandler(Handle), sender, e);
            return;
        }

        _onChanged(e);
    }
}
