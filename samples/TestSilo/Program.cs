using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Hosting;
using Orleans.Providers.ExtSqs.Config;
using Orleans.Runtime.Configuration;

namespace TestSilo
{
	class Program
	{
		static void Main(string[] args)
		{
			var config = ClusterConfiguration.LocalhostPrimarySilo();
			config.AddMemoryStorageProvider();
			config.AddMemoryStorageProvider("PubSubStore");

			var streamConfigBuilder = new ExtSqsConfigurationBuilder()
				.ConnectWith("AKIAJR7KSJG6KRVIOZYA", "e69xdTv6/3BFaF8mhxrZ2+v6s0J41k16kJVbblho", "eu-west-1")
				// You can choose either of the below but not both
				//.WithQueuePrefix("player"); 
				.WithQueueNames(new List<string>
				{
					"player-balances",
					"game-state",
				});

			config.AddSqsStreamProvider("ExtSqsProvider", streamConfigBuilder);

			var builder = new SiloHostBuilder()
				.UseConfiguration(config)
				.AddApplicationPartsFromReferences(Assembly.Load("TestGrains"))
				.ConfigureServices((context, serviceCollection) =>
				{
					serviceCollection.AddLogging();
				})
				.ConfigureLogging(logging => logging.AddConsole());

			var host = builder.Build();
			host.StartAsync().Wait();

			Console.Title = "Silo 1";
			Console.ReadKey();

			host.StopAsync().Wait();
		}
	}
}