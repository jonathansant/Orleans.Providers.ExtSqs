using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Providers.ExtSqs.QueueManagement;
using Orleans.Serialization;
using Orleans.Streams;

namespace Orleans.Providers.ExtSqs.Core
{
	internal class ExtSqsAdapter : IQueueAdapter
	{
		private readonly SerializationManager _serializationManager;
		private readonly IStreamQueueMapper _streamQueueMapper;
		private readonly ILoggerFactory _loggerFactory;
		private readonly bool _fifoQueues;

		protected readonly string DataConnectionString;

		protected readonly ConcurrentDictionary<QueueId, SqsQueueManager> Queues =
			new ConcurrentDictionary<QueueId, SqsQueueManager>();

		public string Name { get; }

		public bool IsRewindable => false;

		public StreamProviderDirection Direction => StreamProviderDirection.ReadWrite;

		public ExtSqsAdapter(
			SerializationManager serializationManager,
			IStreamQueueMapper streamQueueMapper,
			ILoggerFactory loggerFactory,
			string dataConnectionString,
			string providerName,
			bool fifoQueues)
		{
			if (string.IsNullOrEmpty(dataConnectionString))
				throw new ArgumentNullException("dataConnectionString");

			_loggerFactory = loggerFactory;
			_serializationManager = serializationManager;
			_streamQueueMapper = streamQueueMapper;

			DataConnectionString = dataConnectionString;
			_fifoQueues = fifoQueues;

			Name = providerName;
		}

		public IQueueAdapterReceiver CreateReceiver(QueueId queueId) => ExtSqsAdapterReceiver.Create(
				_serializationManager,
				_loggerFactory,
				queueId,
				DataConnectionString,
				_fifoQueues
		);


		public async Task QueueMessageBatchAsync<T>(
			Guid streamGuid,
			string streamNamespace,
			IEnumerable<T> events,
			StreamSequenceToken token,
			Dictionary<string, object> requestContext)
		{
			if (token != null)
				throw new ArgumentException("SQSStream stream provider currently does not support non-null StreamSequenceToken.",
					"token");

			var queueId = _streamQueueMapper.GetQueueForStream(streamGuid, streamNamespace);

			if (!Queues.TryGetValue(queueId, out var queue))
			{
				var tmpQueue = new SqsQueueManager(
					_loggerFactory,
					queueId.GetStringNamePrefix(),
					DataConnectionString,
					_fifoQueues
				);

				await tmpQueue.InitQueueAsync();
				queue = Queues.GetOrAdd(queueId, tmpQueue);
			}

			var msg = ExtSqsBatchContainer.ToSqsMessage(
				_serializationManager, 
				streamGuid, 
				streamNamespace, 
				events, 
				requestContext
			);

			msg.MessageGroupId = streamGuid.ToString();

			await queue.AddMessage(msg);
		}
	}
}