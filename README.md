# Orleans.Providers.ExtSqs

## Overview
The SQS Stream Provider is a new implementation of a `PersistentStreamProvider` for Microsoft Orleans.
It builds on the foundations of the Microsoft's `ExtSqsStreamProvider` (for more information see https://github.com/dotnet/orleans/tree/master/src/OrleansAWSUtils/Streams). However, it differs in two main ways:

1. Instead of creating its own queues it operates on ones already created. It does not created its own queues.
2. It can accept messages from external producers.

## Installation
To start working with the `ExtSqsStreamProvider` make sure you do the following steps:

* `Install-Package Orleans.Streams.ExtSqs`

Example for `ExtSqsStreamProvider` configuration:
```xml
<Provider Type="Orleans.Providers.ExtSqs"
		Name="ExtSqsProvider"
		QueuePrefix="player" <!-- To subscribe to all player queues -->
		Version="v1"
		DataConnectionString="Service={region};AccessKey={accessKey};SecretKey={secreteKey};"
		FifoQueues="true"
		ExternalMessageIdentifier="External"/>
```
In code:
```csharp
	var streamConfigBuilder = new ExtSqsConfigurationBuilder()
		.ConnectWith("Acce$$Key", "J007B", "eu-west-1")
		.WithQueuePrefix("player")
		.WithVersion("v1")
		.IdentifyExternallyProducedMessagesWith("External");

	config.AddSqsStreamProvider("ExtSqsProvider", streamConfigBuilder);
```

## Implementation
The `ExtSqsStreamProvider` is implemented using the Orleans Guidelines to implement a new `PersistentStreamProvider` over the PersistentStreamProvider class (shown in this page: http://dotnet.github.io/orleans/Orleans-Streams/Streams-Extensibility).

*Note*: that it is built upon the foundation of Microsoft's `SQSStreamProvider` found here: https://github.com/dotnet/orleans/tree/master/src/OrleansAWSUtils/Streams

## Queue Setup
In order for the provider to work as expected there are some constraints on the queue naming:
- Always start with a namespace - The provider will list all queues that begin with the supplied `QueuePrefix` in the configuration
- Following the namespace the queue index must be appended - This will be used for load balancing purposes
- Finally `.fifo` must be used for FIFO queues

### Queue Template

```
{ namespace }_{ queueIndex }[ .fifo ]
```
Example: `player-updates_0.fifo`

## Usage

#### Producing Messages

##### External Messages
- `ExtSqs` will take the **`MessageGroupId`** as the `StreamId`
- The `QueueNamespace` is the queue name without the `QueueIndex` and `.fifo` postfix
- Messages should also contain an attribute that identifies that the message is produced externally. This is configurable via the config builder's `IdentifyExternallyProducesMessagesWith()` with the default being `"External"`

##### Internal Messages
```csharp
var streamProvider = client.GetStreamProvider("ExtSqsProvider");
var stream = streamProvider.GetStream<TestModel>(testGrain.GetPrimaryKeyString(), "player-updates");

string line;
while ((line = Console.ReadLine()) != string.Empty)
{
	stream.OnNextAsync(new TestModel
	{
		Greeting = line
	}).Wait();
}
```

#### Consuming Messages
```csharp
public override async Task OnActivateAsync()
{
	var streamProvider = GetStreamProvider("ExtSqsProvider");
	var stream = streamProvider.GetStream<TestModel>(this.GetPrimaryKeyString(), "player-balances");

	await streamBalances.SubscribeAsync((message, sequence) =>
	{
		Console.WriteLine($"bal {message.Greeting}");
		return Task.CompletedTask;
	});
}
```
## Configurable Values
These are the configurable values that the `SQSStreamProvider` offers to its users, the first two are required while the others have default values:

- **QueuePrefix** - The provider will be using the queues that are named started with the prefix supplied
- **DataConnectionString** - The connection string that will be used to connect toi AWS SQS
- **FifoQueues** - Determines whether the queues used are FIFO or not
- **ExternalMessageIdentifier** - This SQS Message attribute tells the provider that the message is produced by an external producer and hence it needs to apply the proper de-serialization 

Additionally you have the default configuration options offered by Orleans to any `PersistentStreamProvider` which can be found here (under `StreamProvider` configuration): https://dotnet.github.io/orleans/Documentation/Orleans-Streams/Streams-Extensibility.html.
