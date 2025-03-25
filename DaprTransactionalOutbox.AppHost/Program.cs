using CommunityToolkit.Aspire.Hosting.Dapr;

var builder = DistributedApplication.CreateBuilder(args);

//var cache = builder.AddRedis("cache");
var dbuilder = builder.AddDapr(o =>
{
    o.EnableTelemetry = true;
});

//var sqlsrv = dbuilder.AddConnectionString("sqldb", "ConnectionStrings:sqldb");

var sqlConnStr = builder.AddParameter("SqlDbConnectionString", secret: true);

// var sqlserver = dbuilder.AddSqlServer("sqlserver", sqlpassword)
//     .WithDataVolume()
//     .WithLifetime(ContainerLifetime.Persistent)
//     .WithDbGate();
//
// var sqldb = sqlserver.AddDatabase("database");

var pubsub = dbuilder.AddDaprPubSub("pubsub", new DaprComponentOptions()
{
    LocalPath = "../components/pubsub.yaml"
});

var pubsubOutbox = dbuilder.AddDaprComponent("pubsubOutbox", DaprConstants.BuildingBlocks.PubSub, new DaprComponentOptions()
{
    LocalPath = "../components/pubsub-outbox.yaml"
});

var stateStore = dbuilder.AddDaprStateStore("statestore", new DaprComponentOptions()
{
    LocalPath = "../components/statestore.yaml"
});

// --------------------------------------------------------------------------------
DaprSidecarOptions producerSidecarOptions = new()
{
    AppId = "producer-api",
    //AppProtocol = "https",
    LogLevel = "debug",
    EnableApiLogging = true,
    
    // DaprGrpcPort = 50001,
    // DaprHttpPort = 3500,
    // MetricsPort = 9090
};

DaprSidecarOptions consumerSidecarOptions = new()
{
    AppId = "consumer-api",
    //AppProtocol = "https",
    LogLevel = "debug",
    EnableApiLogging = true,
    // DaprGrpcPort = 50001,
    // DaprHttpPort = 3500,
    // MetricsPort = 9090
};

// --------------------------------------------------------------------------------

var producerService = dbuilder.AddProject<Projects.DaprTransactionalOutbox_Producer>("producer")
    .WithEnvironment("SqlDbConnectionString", sqlConnStr)
    .WithReference(pubsub)
    .WithReference(pubsubOutbox)
    .WithReference(stateStore)
    .WithDaprSidecar(producerSidecarOptions);
//    .WithReference(sqldb)
//    .WaitFor(sqldb);

// --------------------------------------------------------------------------------

var consumerService = dbuilder.AddProject<Projects.DaprTransactionalOutbox_Consumer>("consumer")
    .WithReference(pubsub)
    //.WithReference(pubsubOutbox)
    .WithReference(stateStore)
    .WithDaprSidecar(consumerSidecarOptions)
    //.WithExplicitStart()
    ;

// --------------------------------------------------------------------------------
// DaprSidecarOptions sidecarOptions = new()
// {
//     AppId = "webfrontend-app",
//     LogLevel = "debug"
// };
//
// builder.AddProject<Projects.DaprTransactionalOutbox_Web>("webfrontend")
//     .WithExternalHttpEndpoints()
//     .WithReference(cache)
//     .WaitFor(cache)
//     .WithReference(producerService)
//     .WaitFor(producerService)
//     .WithReference(consumerService)
//     .WaitFor(consumerService)
//     .WithDaprSidecar(sidecarOptions);

dbuilder.Build().Run();

// --------------------------------------------------------------------------------

public static class DaprConstants
{
    public static class BuildingBlocks
    {
        public const string PubSub = "pubsub";

        public const string StateStore = "state";
    }
}
