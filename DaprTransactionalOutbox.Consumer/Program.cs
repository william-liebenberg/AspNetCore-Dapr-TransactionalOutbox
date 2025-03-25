using System.Text;
using System.Text.Json;
using Dapr;
using DaprTransactionalOutbox.Consumer;
using Google.Rpc.Context;
using Microsoft.AspNetCore.Mvc;

const string PUBSUB_NAME = "pubsub";
const string OUTBOX_PUBSUB_NAME = "outboxPubsub";
const string NEW_ORDER_TOPIC = "orders";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprClient();

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseMyCloudEvents();
app.MapSubscribeHandler();
app.UseExceptionHandler();
app.MapOpenApi();
//app.UseHttpsRedirection();

// app.MapPost("/orderSubmitted", 
//     [Topic(PUBSUB_NAME, NEW_ORDER_TOPIC)]
//     async (HttpRequest request, CancellationToken ct) =>
// {
//     //var contentType = request.ContentType;
//     string? rawContent = await request.ReadFromJsonAsync<string>(cancellationToken: ct);
//     if (rawContent is null)
//     {
//         return Results.BadRequest();
//     }
//     
//     //Console.WriteLine("-------------------------------------------------------------------------------------------------------------------");
//     //Console.WriteLine($"Received order: {rawContent}");
//     //Console.WriteLine($"Content type: {contentType}");
//     
//     var options = new JsonSerializerOptions
//     {
//         PropertyNameCaseInsensitive = true
//     };
//
//     var orderSubmitted = JsonSerializer.Deserialize<OrderSubmitted>(rawContent, options);
//     Console.WriteLine($"Order submitted: {orderSubmitted}");
//     //Console.WriteLine("-------------------------------------------------------------------------------------------------------------------");
//     //Console.WriteLine();
//     
//     return Results.Ok();
// });

app.MapPost("/orderSubmitted",  
        [Topic(PUBSUB_NAME, NEW_ORDER_TOPIC)] 
        ([FromBody] OrderSubmitted @event,
            [FromServices]ILogger<Program> logger) =>
    {
        logger.LogInformation("Received order: {id}, {desc}, {price}, {submitted}", @event.Id, @event.Description, @event.Price, @event.Submitted);
        return Results.Ok();
    })
    //.Accepts<OrderSubmitted>("application/json")
    .WithName("OrderSubmitted");

app.MapDefaultEndpoints();

app.Run();

public class OrderSubmitted
{
    public string? Id { get; set; }
    public DateTime? Submitted { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
}




