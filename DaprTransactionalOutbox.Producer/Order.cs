using System.ComponentModel.DataAnnotations;

namespace DaprTransactionalOutbox.Producer;

public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(255)]
    public string? Status { get; set; }
    [MaxLength(255)]
    public string? Description { get; set; }
    public DateTime Created { get; set; }
    public decimal Price { get; set; }
}