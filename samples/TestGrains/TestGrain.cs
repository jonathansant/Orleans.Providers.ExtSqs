using System;
using System.Threading.Tasks;
using Orleans;
using Orleans.Providers;
using Orleans.Streams;

namespace TestGrains
{
	[StorageProvider(ProviderName = "MemoryStore")]
	public class TestGrain : Grain<TestModel>, ITestGrain
	{
		public async Task<string> GetThePhrase()
		{
			const string phrase = "Hello World";
			return phrase;
		}

		public override async Task OnActivateAsync()
		{
			var sqsProvider = GetStreamProvider("ExtSqsProvider");

			var streamId = this.GetPrimaryKeyString();

			var testStream = sqsProvider.GetStream<TestModel>(streamId, "player-balances");
			var streamReality = sqsProvider.GetStream<string>(streamId, "game-state");

			// To resume stream in case of stream deactivation
			var subscriptionHandles = await testStream.GetAllSubscriptionHandles();

			if (subscriptionHandles.Count > 0)
			{
				foreach (var subscriptionHandle in subscriptionHandles)
				{
					await subscriptionHandle.ResumeAsync(OnNextTestMessage);
				}
			}
			else
			{
				await testStream.SubscribeAsync(OnNextTestMessage);
			}

			await streamReality.SubscribeAsync((message, sequence) =>
			{
				Console.WriteLine(message);
				return Task.CompletedTask;
			});
		}

		private Task OnNextTestMessage(TestModel message, StreamSequenceToken sequenceToken)
		{
			Console.WriteLine(message.Greeting);
			return Task.CompletedTask;
		}
	}
}