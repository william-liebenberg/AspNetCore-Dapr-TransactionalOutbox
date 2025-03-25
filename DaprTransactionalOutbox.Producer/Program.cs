using System.Text;
using System.Text.Json;
using Dapr;
using Dapr.Client;
using DaprTransactionalOutbox.Producer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

const string STORE_NAME = "statestore";
const string PUBSUB_NAME = "pubsub";
const string NEW_ORDER_TOPIC = "orders";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprClient();
//builder.AddSqlServerDbContext<OrderDbContext>(connectionName: "database");

builder.Services.AddDbContext<OrderDbContext>(options =>
{
    string connStr = builder.Configuration["SqlDbConnectionString"] ?? throw new InvalidOperationException("Connection string 'database' not found.");
    options.UseSqlServer(connStr);
});

builder.Services.AddSingleton<IEventBus, DaprEventBus>();

// https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-9.0&tabs=visual-studio#consuming-a-scoped-service-in-a-background-task
// https://learn.microsoft.com/en-us/dotnet/core/extensions/scoped-service
builder.Services.AddScoped<OutboxProcessor>();
builder.Services.AddHostedService<OutboxProcessorBackgroundService>();

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseCloudEvents();
app.MapSubscribeHandler();

app.UseExceptionHandler();
app.MapOpenApi();
// app.UseHttpsRedirection();
app.MapDefaultEndpoints();


app.MapGet("/", async ([FromServices] OrderDbContext db) =>
{
    List<Order> orders = await db.Orders.AsNoTracking().ToListAsync();
    return Results.Ok(orders);
});
    
app.MapPost("/order", async (OrderRequest orderRequest, 
    [FromServices] OrderDbContext db,
    [FromServices] DaprClient daprClient,
    CancellationToken cancellationToken) =>
{
    var order = new Order()
    {
        Created = DateTime.UtcNow, 
        Price = orderRequest.Price,
        Status = "Pending",
        Description = orderRequest.Description
    };
    
    // Add the order to the database
    await db.Orders.AddAsync(order, cancellationToken);
    
    // Save the changes to the database to get the order ID
    // This is required to publish the order submitted event
    // SaveChangesAsync creates a transaction that is committed when the method completes
    await db.SaveChangesAsync(cancellationToken);
    
    // Define the order submitted event
    var orderSubmitted = new OrderSubmitted(order.Id, DateTime.UtcNow, order.Description, order.Price);
    
    // Publish the order submitted event to the event bus with the order ID as the key
    await daprClient.PublishEventAsync(PUBSUB_NAME, NEW_ORDER_TOPIC, JsonSerializer.Serialize(orderSubmitted), cancellationToken);
    
    return Results.Ok($"Order {order.Id} submitted");
});

app.MapPost("/order-via-outbox", async (OrderRequest orderRequest, 
    [FromServices] OrderDbContext db,
    [FromServices] IEventBus eventBus,
    CancellationToken cancellationToken) =>
{
    var order = new Order()
    {
        Created = DateTime.UtcNow, 
        Price = orderRequest.Price,
        Status = "Pending",
        Description = orderRequest.Description
    };
    
    // Add the order to the database
    await db.Orders.AddAsync(order, cancellationToken);
    
    // Define the order submitted event
    var orderSubmitted = new OrderSubmitted(order.Id, DateTime.UtcNow, order.Description, order.Price);

    // Define the outbox event
    var @event = new OutboxEvent
    {
        Topic = "orders",
        EventType = typeof(OrderSubmitted).FullName,
        Payload = JsonSerializer.Serialize(orderSubmitted),
        Processed = false
    };

    await db.OutboxEvents.AddAsync(@event, cancellationToken);
    
    // Save the changes to the database to get the order ID
    // This is required to publish the order submitted event
    // SaveChangesAsync creates a transaction that is committed when the method completes
    await db.SaveChangesAsync(cancellationToken);
    
    return Results.Ok($"Order {order.Id} submitted");
});

app.MapPost("/order-via-tx-outbox", async (OrderRequest orderRequest, 
    [FromServices] OrderDbContext db,
    [FromServices] IEventBus eventBus,
    CancellationToken cancellationToken) =>
{
    await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);

    try
    {
        var order = new Order()
        {
            Created = DateTime.UtcNow, 
            Price = orderRequest.Price,
            Status = "Pending",
            Description = orderRequest.Description
        };
    
        // Add the order to the database
        await db.Orders.AddAsync(order, cancellationToken);
    
        // Save the changes to the database to get the order ID
        // This is required to publish the order submitted event
        // SaveChangesAsync creates a transaction that is committed when the method completes
        await db.SaveChangesAsync(cancellationToken);
        
        // Commit transaction
        await transaction.CommitAsync(cancellationToken);
        
        // Define the order submitted event
        var orderSubmitted = new OrderSubmitted(order.Id, DateTime.UtcNow, order.Description, order.Price);
    
        // Publish the order submitted event to the event bus with the order ID as the key
        await eventBus.Publish(order.Id.ToString(), orderSubmitted, cancellationToken);

        return Results.Ok($"Order {order.Id} submitted");
    }
    catch (Exception)
    {
        await transaction.RollbackAsync(cancellationToken);
        throw;
    }
});

// for testing only - don't do this in real production apps! 
using (IServiceScope scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    //context.Database.EnsureCreated();
    context.Database.Migrate();

    if(!context.Orders.Any())
    {
        context.Orders.Add(new Order
        {
            Status = "Pending",
            Created = DateTime.UtcNow,
            Price = 100.00m
        });
        
        context.SaveChanges();
    }
}

app.Run();