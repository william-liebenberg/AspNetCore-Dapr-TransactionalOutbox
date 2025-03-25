# AspNetCore-Dapr-TransactionalOutbox

This repository demonstrates a robust implementation of the **Transactional Outbox Pattern** using **ASP.NET Core** and **Dapr**. The project showcases how to ensure reliable event-driven communication between microservices while maintaining data consistency.

## Features

- **Transactional Outbox Pattern**: Ensures atomicity between database operations and event publishing.
- **Dapr Integration**: Leverages Dapr's building blocks for pub/sub, state management, and sidecar communication.
- **Redis Pub/Sub**: Implements Redis as the message broker for pub/sub communication.
- **SQL Server State Store**: Uses SQL Server for state persistence and outbox storage.
- **Background Processing**: Processes outbox events asynchronously using a background service.


# Getting Started

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download)
- [Dapr CLI](https://docs.dapr.io/getting-started/install-dapr-cli/)
- [Docker](https://www.docker.com/)

### Setup

1. Clone the repository:

```pwsh
git clone https://github.com/william-liebenberg/AspNetCore-Dapr-TransactionalOutbox.git
cd AspNetCore-Dapr-TransactionalOutbox
```

2. Start the required services (SQL Server and Redis) using Docker Compose:

```pwsh
docker-compose up -d
```

3. Initialize Dapr components:

```pwsh
dapr init
```

4. Run the producer and consumer services via Aspire AppHost by opening the `DaprTransactionalOutbox.sln` in **Visual Studio** or **Rider** and start debugging with the AppHost project.

## Testing
Use the `stress-test.js` script to simulate load using [k6](https://k6.io):

```pwsh
k6 run stress-test.js
```

## Key Endpoints

### Producer
Submit Order: `POST /order`
Submit Order via Outbox: `POST /order-via-outbox`
Submit Order via Transactional Outbox: `POST /order-via-tx-outbox`

### Consumer
Order Submitted Event Handler: `POST /orderSubmitted`

## Dapr Components

Pub/Sub: Configured in [`pubsub.yaml`](/components/pubsub.yaml)
Pub/Sub Outbox: Configured in [`pubsub-outbox.yaml`](/components/pubsub-outbox.yaml)
State Store: Configured in [`statestore.yaml`](/components/statestore.yaml)

## License
This project is licensed under the MIT License.

## Contributing
Contributions are welcome! Feel free to open issues or submit pull requests to improve this project.

## Links

[Dapr](https://dapr.io)