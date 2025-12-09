using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VeloxDev.Core.TimeLine;

namespace Demo
{
    [MonoBehaviour]
    public partial class MainWindow : Window
    {
        [MonoBehaviour]
        private partial class PhysicsComponent
        {
            private int _updateCount = 0;
            private int _fixedUpdateCount = 0;
            private double _velocity = 0;
            private const double Gravity = 9.8;

            public PhysicsComponent() => InitializeMonoBehaviour();

            partial void Update(FrameEventArgs e)
            {
                _updateCount++;
                _velocity += Gravity * (e.DeltaTime / 1000.0);
            }

            partial void FixedUpdate(FrameEventArgs e)
            {
                _fixedUpdateCount++;
            }

            public string GetStats() => $"Physics - Updates: {_updateCount}, Fixed: {_fixedUpdateCount}, Velocity: {_velocity:F2}";
        }

        [MonoBehaviour]
        private partial class InputComponent
        {
            private int _updateCount = 0;
            private int _keyPressCount = 0;
            private readonly HashSet<Key> _currentKeys = new();

            public InputComponent() => InitializeMonoBehaviour();

            partial void Update(FrameEventArgs e)
            {
                _updateCount++;
                if (_currentKeys.Count > 0)
                {
                    _keyPressCount += _currentKeys.Count;
                }
            }

            public void RecordKeyPress(Key key) => _currentKeys.Add(key);
            public void RecordKeyRelease(Key key) => _currentKeys.Remove(key);

            public string GetStats() => $"Input - Updates: {_updateCount}, KeyEvents: {_keyPressCount}";
        }

        [MonoBehaviour]
        private partial class RenderComponent
        {
            private int _updateCount = 0;
            private int _fixedUpdateCount = 0;
            private int _frameCounter = 0;

            public RenderComponent() => InitializeMonoBehaviour();

            partial void Update(FrameEventArgs e)
            {
                _updateCount++;
                _frameCounter++;
                Thread.Sleep(new Random().Next(10, 50));
            }

            partial void FixedUpdate(FrameEventArgs e)
            {
                _fixedUpdateCount++;
            }

            public string GetStats() => $"Render - Updates: {_updateCount}, Fixed: {_fixedUpdateCount}, Frames: {_frameCounter}";
        }

        // Component instances
        private PhysicsComponent _physics;
        private InputComponent _input;
        private RenderComponent _render;

        // Window statistics
        private int _windowUpdateCount = 0;
        private int _windowFixedUpdateCount = 0;
        private readonly HashSet<Key> _pressedKeys = [];
        private readonly Random _random = new();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                _physics = new PhysicsComponent();
                _input = new InputComponent();
                _render = new RenderComponent();

                KeyDown += OnKeyDown;
                KeyUp += OnKeyUp;

                InitializeMonoBehaviour();
                MonoBehaviourManager.Start();
            };

            Closing += async (s, e) =>
            {
                await MonoBehaviourManager.StopAsync();
            };
        }

        partial void Update(FrameEventArgs e)
        {
            _windowUpdateCount++;

            UpdatePerformanceDisplay(e);
            UpdateComponentStatistics();

            if (_pressedKeys.Contains(Key.H) && _pressedKeys.Contains(Key.LeftCtrl))
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"[ Ctrl + H ] invoked - System Stats:\n" +
                                   $"Physics Updates: {_physics?.GetStats() ?? "N/A"}\n" +
                                   $"Input Updates: {_input?.GetStats() ?? "N/A"}\n" +
                                   $"Render Updates: {_render?.GetStats() ?? "N/A"}");
                });
                _pressedKeys.Remove(Key.H);
                _pressedKeys.Remove(Key.LeftCtrl);
            }
        }

        partial void FixedUpdate(FrameEventArgs e)
        {
            _windowFixedUpdateCount++;
        }

        private void UpdatePerformanceDisplay(FrameEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var txtDeltaTime = FindName("txtDeltaTime") as TextBlock;
                var txtTotalTime = FindName("txtTotalTime") as TextBlock;
                var txtCurrentFPS = FindName("txtCurrentFPS") as TextBlock;
                var txtTargetFPS = FindName("txtTargetFPS") as TextBlock;
                var txtTotalFrames = FindName("txtTotalFrames") as TextBlock;
                var txtHandled = FindName("txtHandled") as TextBlock;

                if (txtDeltaTime != null) txtDeltaTime.Text = $"Delta Time: {e.DeltaTime} ms";
                if (txtTotalTime != null) txtTotalTime.Text = $"Total Time: {e.TotalTime / 1000.0:F1} s";
                if (txtCurrentFPS != null) txtCurrentFPS.Text = $"Current FPS: {e.CurrentFPS}";
                if (txtTargetFPS != null) txtTargetFPS.Text = $"Target FPS: {e.TargetFPS}";
                if (txtTotalFrames != null) txtTotalFrames.Text = $"Total Frames: {MonoBehaviourManager.TotalFrames}";
                if (txtHandled != null) txtHandled.Text = $"Handled: {(e.Handled ? "Yes" : "No")}";
            });
        }

        private void UpdateComponentStatistics()
        {
            Dispatcher.Invoke(() =>
            {
                var txtWindowStats = FindName("txtWindowStats") as TextBlock;
                var txtPhysicsStats = FindName("txtPhysicsStats") as TextBlock;
                var txtInputStats = FindName("txtInputStats") as TextBlock;
                var txtRenderStats = FindName("txtRenderStats") as TextBlock;
                var txtActiveComponents = FindName("txtActiveComponents") as TextBlock;
                var txtTotalUpdates = FindName("txtTotalUpdates") as TextBlock;
                var txtThreadStatus = FindName("txtThreadStatus") as TextBlock;
                var txtSystemStatus = FindName("txtSystemStatus") as TextBlock;

                if (txtWindowStats != null)
                    txtWindowStats.Text = $"Window - Updates: {_windowUpdateCount}, Fixed: {_windowFixedUpdateCount}";
                if (txtPhysicsStats != null) txtPhysicsStats.Text = _physics?.GetStats() ?? "Physics: Not loaded";
                if (txtInputStats != null) txtInputStats.Text = _input?.GetStats() ?? "Input: Not loaded";
                if (txtRenderStats != null) txtRenderStats.Text = _render?.GetStats() ?? "Render: Not loaded";
                if (txtActiveComponents != null)
                    txtActiveComponents.Text = $"Active Components: {MonoBehaviourManager.ActiveBehaviorCount}";
                if (txtTotalUpdates != null)
                    txtTotalUpdates.Text = $"Total Updates: {MonoBehaviourManager.TotalFrames}";
                if (txtThreadStatus != null)
                    txtThreadStatus.Text = $"Update Thread: {(MonoBehaviourManager.IsUpdateThreadAlive ? "Running" : "Stopped")} | Physics Thread: {(MonoBehaviourManager.IsFixedUpdateThreadAlive ? "Running" : "Stopped")}";
                if (txtSystemStatus != null)
                    txtSystemStatus.Text = MonoBehaviourManager.IsRunning ? (MonoBehaviourManager.IsPaused ? "🟡 System Paused" : "🟢 System Running") : "🔴 System Stopped";
            });
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            _pressedKeys.Add(e.Key);
            _input?.RecordKeyPress(e.Key);
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            _pressedKeys.Remove(e.Key);
            _input?.RecordKeyRelease(e.Key);
        }

        // Test control methods
        private void BtnAddComponent_Click(object sender, RoutedEventArgs e)
        {
            var newComponent = new PhysicsComponent();
        }

        private void BtnResetStats_Click(object sender, RoutedEventArgs e)
        {
            _windowUpdateCount = 0;
            _windowFixedUpdateCount = 0;
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            MonoBehaviourManager.Start();
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            MonoBehaviourManager.Pause();
        }

        private void BtnResume_Click(object sender, RoutedEventArgs e)
        {
            MonoBehaviourManager.Resume();
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            await MonoBehaviourManager.StopAsync();
        }
    }
}