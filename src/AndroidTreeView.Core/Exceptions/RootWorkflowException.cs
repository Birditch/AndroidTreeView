using AndroidTreeView.Models.Rooting;

namespace AndroidTreeView.Core.Exceptions;

/// <summary>A root workflow failure carrying a stable code and optional sanitized diagnostic detail.</summary>
public sealed class RootWorkflowException : Exception
{
    public RootWorkflowException(RootErrorCode errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public RootWorkflowException(RootErrorCode errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public RootErrorCode ErrorCode { get; }

    /// <summary>Sanitized process or parser detail suitable for diagnostics, not direct UI display.</summary>
    public string? DiagnosticSummary { get; init; }
}
