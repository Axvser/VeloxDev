using Microsoft.VisualStudio.TestTools.UnitTesting;

// HeadlessUnitTestSession owns one dispatcher thread; running test classes in parallel would stack sessions.
[assembly: DoNotParallelize]
