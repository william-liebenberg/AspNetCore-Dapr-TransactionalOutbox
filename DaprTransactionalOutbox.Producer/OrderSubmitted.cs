namespace DaprTransactionalOutbox.Producer;

internal record OrderSubmitted(Guid Id, DateTime Submitted, string Description, decimal Price);