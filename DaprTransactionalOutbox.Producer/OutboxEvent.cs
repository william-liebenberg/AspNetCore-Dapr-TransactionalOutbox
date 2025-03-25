using System.ComponentModel.DataAnnotations;

namespace DaprTransactionalOutbox.Producer;

public class OutboxEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [MaxLength(255)]
    public string? EventType { get; set; }
    
    [MaxLength(255)]
    public required string Topic { get; init; }
    
    [MaxLength(2555555)]
    
    public string? Payload { get; set; }
    
    public DateTimeOffset ProcessedAt { get; set; }
    
    public bool Processed { get; set; }
}