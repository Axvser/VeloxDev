using Microsoft.VisualStudio.TestTools.UnitTesting;

// 可视树、dispatcher 与 ViewPool 的批次都按线程/帧推进；并行跑会把它们的时序搅在一起。
[assembly: DoNotParallelize]
