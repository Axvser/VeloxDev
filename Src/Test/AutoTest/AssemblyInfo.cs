// The UI suites each drive a real application with one interactive desktop and one foreground window between them,
// so they must not run side by side. This is why the project deliberately does not copy the Core test project's
// MSTestSettings.cs, which turns on method-level parallelisation: the attribute is assembly-scoped, so leaving it
// out here changes nothing for the other test projects.
[assembly: DoNotParallelize]
