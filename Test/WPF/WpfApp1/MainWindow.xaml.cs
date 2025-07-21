﻿using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using VeloxDev.WPF.WorkflowSystem.ViewModels;

namespace WpfApp1
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            var node1 = new ShowerNodeViewModel();
            var node2 = new ShowerNodeViewModel();
            node1.Anchor = new(50, 100, 2);
            node2.Anchor = new(100, 50, 1);
            var tree = new FactoryViewModel()
            {
                Children = [node1, node2],
            };
            container.DataContext = tree;
        }
    }
}