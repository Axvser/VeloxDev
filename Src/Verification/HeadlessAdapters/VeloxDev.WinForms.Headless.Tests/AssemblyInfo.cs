using Microsoft.VisualStudio.TestTools.UnitTesting;

// Control handles and the application message loop are per-thread; parallel classes would share both.
[assembly: DoNotParallelize]
