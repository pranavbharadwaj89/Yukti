; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
YUKTI002 | Auditing | Error | NoUnauditedCommandHandlerAnalyzer, [FR-AUDIT-01](../../docs/specification/product/INIT-YUKTI-BACKEND-001.md)
YUKTI003 | AsyncStandards | Error | AsyncMethodsRequireCancellationTokenAnalyzer, [FR-STD-02](../../docs/specification/product/INIT-YUKTI-BACKEND-001.md)
YUKTI004 | AsyncStandards | Error | NoSyncOverAsyncAnalyzer, [FR-STD-03](../../docs/specification/product/INIT-YUKTI-BACKEND-001.md)
