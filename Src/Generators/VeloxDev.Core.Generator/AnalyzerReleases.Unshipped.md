; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
VELOX_MVVM_CMD001 | VeloxDev.MVVM | Error | Unsupported [VeloxCommand] signature
VELOX_MVVM_PROP001 | VeloxDev.MVVM | Error | Conflicting [VeloxProperty] declaration
VELOX_MVVM_PROP002 | VeloxDev.MVVM | Warning | Unusable [VeloxProperty] name
VELOX_MVVM_PROP003 | VeloxDev.MVVM | Warning | Uncompletable [VeloxProperty] property
VELOX_AI_TREE001 | VeloxDev.AI | Warning | Ambiguous method overload in the agent context tree
VELOX_JSON_ARCH001 | VeloxDev.Serialization | Error | Unsupported [Archivable] root
VELOX_JSON_HOOK001 | VeloxDev.Serialization | Warning | Ambiguous serialization callback
VELOX_JSON_HOOK002 | VeloxDev.Serialization | Error | Unreachable serialization callback
VELOX_JSON_MEMBER001 | VeloxDev.Serialization | Error | Unusable member declaration
VELOX_JSON_MEMBER002 | VeloxDev.Serialization | Warning | Conflicting [Archive(KeepField)]
VELOX_JSON_GENERIC001 | VeloxDev.Serialization | Warning | Type parameter with no resolvable constraint
VELOX_LANGVERSION001 | VeloxDev.Generators | Warning | LangVersion is below what the generated code needs
VELOX_JSON_INCLUDE001 | VeloxDev.Serialization | Info | Type reached without being named by a declaration
