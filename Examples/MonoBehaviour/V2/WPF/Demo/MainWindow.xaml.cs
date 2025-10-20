using System.Windows;
using System.Windows.Input;
using VeloxDev.Core.Mono;

namespace Demo
{
    [MonoBehaviour]
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                KeyDown += User_KeyDown;
                KeyUp += User_KeyUp;
                CanMonoBehaviour = true;
            };
        }

        private readonly HashSet<Key> _pressedKeys = [];

        partial void Update()
        {
            if (_pressedKeys.Contains(Key.H) && _pressedKeys.Contains(Key.LeftCtrl))
            {
                MessageBox.Show($"[ Ctrl + H ] has been invoked");
                _pressedKeys.Remove(Key.H);
                _pressedKeys.Remove(Key.LeftCtrl);
            }
        }

        private void User_KeyDown(object? sender, KeyEventArgs e)
        {
            _pressedKeys.Add(e.Key);
        }

        private void User_KeyUp(object? sender, KeyEventArgs e)
        {
            _pressedKeys.Remove(e.Key);
        }
    }
}