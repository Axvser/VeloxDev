namespace VeloxDev.AI.Workflow;

public static class WorkflowProtocolErrorCodes
{
    public const string UnhandledError = "UnhandledError";
    public const string InvalidPatchRequest = "InvalidPatchRequest";
    public const string RevisionConflict = "RevisionConflict";
    public const string TargetNotFound = "TargetNotFound";
    public const string IndexOutOfRange = "IndexOutOfRange";
    public const string JsonSerializationFailed = "JsonSerializationFailed";
    public const string InvalidConnection = "InvalidConnection";
    public const string PropertyPathInvalid = "PropertyPathInvalid";
    public const string CommandNotFound = "CommandNotFound";
    public const string CommandNotExecutable = "CommandNotExecutable";
    public const string MethodNotFound = "MethodNotFound";
    public const string MethodAmbiguous = "MethodAmbiguous";
    public const string InvalidOperation = "InvalidOperation";
}
