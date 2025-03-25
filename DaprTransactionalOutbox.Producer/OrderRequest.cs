namespace DaprTransactionalOutbox.Producer;

internal record OrderRequest(string Description, decimal Price);