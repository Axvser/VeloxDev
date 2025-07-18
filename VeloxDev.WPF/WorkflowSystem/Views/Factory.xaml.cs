using System.Windows;
using System.Windows.Controls;
using VeloxDev.Core.Interfaces.WorkflowSystem.View;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.Views
{
    public partial class Factory : Canvas, IViewTree
    {
        public Factory()
        {
            InitializeComponent();
        }

        public bool MoveNode(object node, Anchor anchor)
        {
            if (node is UIElement element && Children.Contains(element))
            {
                SetLeft(element, anchor.Left);
                SetTop(element, anchor.Top);
                SetZIndex(element, anchor.Layer);
                return true;
            }
            return false;
        }
        public bool InstallConnector(object connector)
        {
            if (connector is UIElement element && !Children.Contains(element))
            {
                Children.Add(element);
                return true;
            }
            return false;
        }
        public bool InstallNode(object node, Anchor anchor)
        {
            if (node is UIElement element && !Children.Contains(element))
            {
                SetLeft(element, anchor.Left);
                SetTop(element, anchor.Top);
                SetZIndex(element, anchor.Layer);
                Children.Add(element);
                return true;
            }
            return false;
        }
        public bool UninstallConnector(object connector)
        {
            if (connector is UIElement element && Children.Contains(element))
            {
                Children.Remove(element);
                return true;
            }
            return false;
        }
        public bool UninstallNode(object node)
        {
            if (node is UIElement element && Children.Contains(element))
            {
                Children.Remove(element);
                return true;
            }
            return false;
        }
    }
}
