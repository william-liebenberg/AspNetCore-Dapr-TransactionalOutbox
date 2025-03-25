using Dapr.Client;
using DaprTransactionalOutbox.Producer;
using Microsoft.EntityFrameworkCore;

public class OutboxProcessor
{
    private const string PubsubName = "pubsub";

    private readonly ILogger<OutboxProcessor> _logger;
    private readonly OrderDbContext _context;
    private readonly DaprClient _daprClient;

    public OutboxProcessor(
        ILogger<OutboxProcessor> logger,
        OrderDbContext context,
        DaprClient daprClient)
    {
        _logger = logger;
        _context = context;
        _daprClient = daprClient;
    }

    public async Task ProcessOutboxMessagesAsync(CancellationToken cancellationToken)
    {   
        List<OutboxEvent> messages = await _context.OutboxEvents
            .Where(m => !m.Processed)
            .ToListAsync(cancellationToken: cancellationToken);
        
        _logger.LogInformation("{count} outbox messages to process", messages.Count);
        
        foreach (OutboxEvent message in messages)
        {
            if (message.Payload != null)
            {
                await _daprClient.PublishEventAsync(PubsubName, message.Topic, message.Payload, cancellationToken);
            }

            // Mark message as processed
            message.Processed = true;
            message.ProcessedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}

public class OutboxProcessorBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<OutboxProcessorBackgroundService> _logger;

    public OutboxProcessorBackgroundService(IServiceScopeFactory serviceScopeFactory,
        ILogger<OutboxProcessorBackgroundService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            IServiceScope scope = _serviceScopeFactory.CreateScope();
            var outboxProcessor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
            await outboxProcessor.ProcessOutboxMessagesAsync(cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }
}