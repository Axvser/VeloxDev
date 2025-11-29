using System.Windows;
using System.Windows.Input;
using VeloxDev.Core.TimeLine;

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

                InitializeMonoBehaviour();  // 每个实例都需要执行一次
                // CloseMonoBehavior();
                MonoBehaviourManager.Start();  // 全局一次即可
                // MonoBehaviourManager.Stop();
            };
        }
        
        partial void Update(FrameEventArgs e)
        {
            // 更新性能参数显示
            UpdatePerformanceDisplay(e);

            // 检测 Ctrl + H 组合键
            if (_pressedKeys.Contains(Key.H) && _pressedKeys.Contains(Key.LeftCtrl))
            {
                MessageBox.Show($"[ Ctrl + H ] has been invoked");
                _pressedKeys.Remove(Key.H);
                _pressedKeys.Remove(Key.LeftCtrl);
            }

            // Update执行频率受负载影响
            // FixedUpdate执行频率不受负载影响
            Thread.Sleep(random.Next(0,30));
        }

        private void UpdatePerformanceDisplay(FrameEventArgs e)
        {
            // 使用Dispatcher确保UI线程安全更新
            Dispatcher.Invoke(() =>
            {
                txtDeltaTime.Text = $"DeltaTime: {e.DeltaTime}ms";
                txtTotalTime.Text = $"TotalTime: {e.TotalTime}ms";
                txtCurrentFPS.Text = $"Current FPS: {e.CurrentFPS}";
                txtTargetFPS.Text = $"Target FPS: {e.TargetFPS}";
                txtTotalFrames.Text = $"Total Frames: {MonoBehaviourManager.TotalFrames}";
                txtHandled.Text = $"Handled: {e.Handled}";
            });
        }

        private readonly HashSet<Key> _pressedKeys = [];
        private readonly Random random = new();

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