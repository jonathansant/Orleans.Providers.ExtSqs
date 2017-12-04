using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using Orleans.Providers.ExtSqs.Config;
using Orleans.Runtime;
using Orleans.Runtime.Configuration;
using TestGrains;

namespace TestClient
{
	class Program
	{
		static void Main(string[] args)
		{
			var clientTask = StartClientWithRetries();
			clientTask.Wait();

			var clusterClient = clientTask.Result;

			var testGrain = clusterClient.GetGrain<ITestGrain>("PLAYER-5a98c80e-26b8-4d1c-a5da-cb64237f2392");

			var result = testGrain.GetThePhrase();
			result.Wait();

			Console.WriteLine(result.Result);

			var streamId = testGrain.GetPrimaryKeyString();

			var streamProvider = clientTask.Result.GetStreamProvider("ExtSqsProvider");
			var stream = streamProvider.GetStream<TestModel>(streamId, "player-balances");

			Console.Title = "Client";

			string line;
			while ((line = Console.ReadLine()) != string.Empty)
			{
				stream.OnNextAsync(new TestModel
				{
					Greeting = line
				}).Wait();
			}
			Console.ReadKey();
		}

		private static async Task<IClusterClient> StartClientWithRetries(int initializeAttemptsBeforeFailing = 7)
		{
			var attempt = 0;
			IClusterClient client;
			while (true)
			{
				try
				{
					var config = ClientConfiguration.LocalhostSilo();

					var streamConfigBuilder = new ExtSqsConfigurationBuilder()
						.ConnectWith("AKIAJR7KSJG6KRVIOZYA", "e69xdTv6/3BFaF8mhxrZ2+v6s0J41k16kJVbblho", "eu-west-1")
						.WithQueuePrefix("player");

					config.AddSqsStreamProvider("ExtSqsProvider", streamConfigBuilder);

					client = new ClientBuilder()
						.UseConfiguration(config)
						.AddApplicationPartsFromReferences(Assembly.Load("TestGrains"))
						.ConfigureLogging(logging => logging.AddConsole())
						.Build();

					await client.Connect();

					Console.WriteLine("Client successfully connect to silo host");
					break;
				}
				catch (SiloUnavailableException)
				{
					attempt++;
					Console.WriteLine(
						$"Attempt {attempt} of {initializeAttemptsBeforeFailing} failed to initialize the Orleans client.");
					if (attempt > initializeAttemptsBeforeFailing)
					{
						throw;
					}
					Thread.Sleep(TimeSpan.FromSeconds(3));
				}
			}

			return client;
		}
	}
}