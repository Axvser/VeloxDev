using Microsoft.VisualStudio.TestTools.UnitTesting;

// Jalium 的 Dispatcher/ThemeManager 是进程级共享状态；测试类串行，避免互相重置。
[assembly: DoNotParallelize]
