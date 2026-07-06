namespace JackOfAll.Lib.Messaging;

public interface IToolMessage;

public sealed record TokenDetectedMessage(string Token, string SourceTool) : IToolMessage;

public sealed record JsonDetectedMessage(string Json, string SourceTool) : IToolMessage;

public sealed record OpenToolMessage(string ToolId, string? Payload = null) : IToolMessage;
