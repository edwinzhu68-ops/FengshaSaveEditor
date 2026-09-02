namespace FengshaSaveEditor;

internal sealed record CliRunResult(int ExitCode, string Output);

internal sealed record UnitListResponse(
    int RegionCount,
    List<UnitListItem> Units);

internal sealed record UnitListItem(
    string Key,
    string Label,
    int Count);

internal sealed record AttributeListResponse(
    string Unit,
    int RegionCount,
    List<AttributeListItem> Attributes);

internal sealed record AttributeListItem(
    string Key,
    string Label,
    int FieldCount,
    string Current);

internal sealed record ResourceListResponse(
    int ResourceSaveIdFieldCount,
    int CandidateRecordCount,
    int SkippedRecordCount,
    List<ResourceListItem> Nodes);

internal sealed record ResourceListItem(
    string Label,
    string Category,
    int ConfigId,
    string SizeLabel,
    int NodeCount,
    int Capacity,
    int? CurrentAmount,
    string Summary);

internal sealed record PlayerListResponse(
    int FieldCount,
    int AttributeCount,
    List<PlayerListItem> Attributes);

internal sealed record PlayerListItem(
    string Key,
    string Label,
    int FieldCount,
    string Current);
