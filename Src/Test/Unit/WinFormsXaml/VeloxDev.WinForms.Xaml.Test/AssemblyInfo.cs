using Microsoft.VisualStudio.TestTools.UnitTesting;

// 控件句柄、消息泵和 UIThreadInspector 的线程归属都是进程级/线程级共享状态；并行类会互相踩。
[assembly: DoNotParallelize]
