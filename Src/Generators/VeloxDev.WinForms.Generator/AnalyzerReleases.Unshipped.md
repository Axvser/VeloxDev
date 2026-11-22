; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
VELOX_XAML001 | VeloxDev.Xaml | Error | Unknown XAML type
VELOX_XAML002 | VeloxDev.Xaml | Error | Unknown XAML member
VELOX_XAML003 | VeloxDev.Xaml | Warning | Unsupported XAML value
VELOX_XAML004 | VeloxDev.Xaml | Warning | Unsupported XAML construct
VELOX_XAML005 | VeloxDev.Xaml | Error | Missing x:Class
VELOX_XAML006 | VeloxDev.Xaml | Error | x:Class type not found
VELOX_XAML007 | VeloxDev.Xaml | Warning | Duplicate x:Name
VELOX_XAML008 | VeloxDev.Xaml | Warning | Missing event handler
VELOX_XAML009 | VeloxDev.Xaml | Error | x:Class type is not partial
VELOX_XAML010 | VeloxDev.Xaml | Warning | Unsupported resource
VELOX_XAML011 | VeloxDev.Xaml | Warning | Unsupported root
VELOX_XAML012 | VeloxDev.Xaml | Error | XAML parse error
VELOX_XAML013 | VeloxDev.Xaml | Warning | Unknown attached property
VELOX_XAML014 | VeloxDev.Xaml | Error | Invalid style TargetType
VELOX_XAML015 | VeloxDev.Xaml | Error | InitializeComponent already declared
VELOX_XAML016 | VeloxDev.Xaml | Warning | Ambiguous event handler
VELOX_XAML017 | VeloxDev.Xaml | Warning | Invalid event handler signature
VELOX_XAML018 | VeloxDev.Xaml | Error | Ambiguous XAML type
VELOX_XAML019 | VeloxDev.Xaml | Error | Unprefixed type is not a VeloxDev type
VELOX_XAMLP001 | VeloxDev.Xaml | Error | XamlProperty must be partial
VELOX_XAMLP002 | VeloxDev.Xaml | Error | XamlProperty owner unsupported
VELOX_XAMLP003 | VeloxDev.Xaml | Error | Invalid attached property accessors
VELOX_XAMLP004 | VeloxDev.Xaml | Error | Dependency property name conflict
VELOX_XAMLP005 | VeloxDev.Xaml | Error | Dependency property default value required
