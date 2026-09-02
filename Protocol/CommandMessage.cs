using System;
using System.Text.Json;

namespace ClassRoom_Control.Protocol;

public class CommandMessage
{
    public CommandType Type { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public string? Payload { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static CommandMessage Create(CommandType type, string senderId, string senderName, string? payload = null, string? targetId = null)
    {
        return new CommandMessage
        {
            Type = type,
            SenderId = senderId,
            SenderName = senderName,
            TargetId = targetId,
            Payload = payload,
            Timestamp = DateTime.UtcNow
        };
    }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static CommandMessage? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<CommandMessage>(json);
        }
        catch
        {
            return null;
        }
    }
}