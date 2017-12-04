using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Providers.ExtSqs.QueueManagement;
using Orleans.Providers.ExtSqs.QueueMapper;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;
using SQSMessage = Amazon.SQS.Model.Message;

namespace Orleans.Providers.ExtSqs.Core
{
	/// <summary>
	/// Receives batches of messages from a single partition of a message queue.  
	/// </summary>
	internal class ExtSqsAdapterReceiver : IQueueAdapterReceiver
	{
		private SqsQueueManager _queue;
		private long _lastReadMessage;
		private Task _outstandingTask;
		private readonly ILogger _logger;
		private readonly SerializationManager _serializationManager;
		private readonly QueueId _queueId;

		public QueueId Id { get; }

		public static IQueueAdapterReceiver Create(
			SerializationManager serializationManager,
			ILoggerFactory loggerFactory,
			QueueId queueId,
			string dataConnectionString,
			bool fifoQueues)
		{
			if (queueId == null)
				throw new ArgumentNullException("queueId");

			if (string.IsNullOrEmpty(dataConnectionString))
				throw new ArgumentNullException("dataConnectionString");

			var queue = new SqsQueueManager(loggerFactory, queueId.GetStringNamePrefix(), dataConnectionString, fifoQueues);
			return new ExtSqsAdapterReceiver(serializationManager, loggerFactory, queueId, queue);
		}

		private ExtSqsAdapterReceiver(
			SerializationManager serializationManager,
			ILoggerFactory loggerFactory,
			QueueId queueId,
			SqsQueueManager queue)
		{
			Id = queueId ?? throw new ArgumentNullException("queueId");

			_queue = queue ?? throw new ArgumentNullException("queue");
			_logger = loggerFactory.CreateLogger<ExtSqsAdapterReceiver>();
			_serializationManager = serializationManager;
			_queueId = queueId;
		}

		public Task Initialize(TimeSpan timeout)
		{
			return _queue?.InitQueueAsync() ?? Task.CompletedTask;
		}

		public async Task Shutdown(TimeSpan timeout)
		{
			try
			{
				// await the last storage operation, so after we shutdown and stop this receiver we don't get async operation completions from pending storage operations.
				if (_outstandingTask != null)
				{
					await _outstandingTask;
				}
			}
			finally
			{
				// remember that we shut down so we never try to read from the queue again.
				_queue = null;
			}
		}

		public async Task<IList<IBatchContainer>> GetQueueMessagesAsync(int maxCount)
		{
			try
			{
				var queueRef = _queue; // store direct ref, in case we are somehow asked to shutdown while we are receiving.    
				if (queueRef == null)
				{
					return new List<IBatchContainer>();
				}

				var count = maxCount < 0 || maxCount == QueueAdapterConstants.UNLIMITED_GET_QUEUE_MSG
					? SqsQueueManager.MaxNumberOfMessageToPeak
					: Math.Min(maxCount, SqsQueueManager.MaxNumberOfMessageToPeak);

				var messagesTask = queueRef.GetMessages(count);

				_outstandingTask = messagesTask;

				var messages = await messagesTask;

				return messages.Select(ToBatchContainer)
					.ToList();
			}
			finally
			{
				_outstandingTask = null;
			}
		}

		public async Task MessagesDeliveredAsync(IList<IBatchContainer> messages)
		{
			try
			{
				var queueRef = _queue; // store direct ref, in case we are somehow asked to shutdown while we are receiving.  

				if (messages.Count == 0 || queueRef == null)
				{
					return;
				}

				var cloudQueueMessages = messages.Cast<ExtSqsBatchContainer>()
					.Select(batchContainer => batchContainer.Message)
					.ToList();

				_outstandingTask = Task.WhenAll(cloudQueueMessages.Select(queueRef.DeleteMessage));

				try
				{
					await _outstandingTask;
				}
				catch (Exception exc)
				{
					_logger.Warn(
						(int)ErrorCode.AzureQueue_15,
						$"Exception upon DeleteMessage on queue {Id}. Ignoring.", exc);
				}
			}
			finally
			{
				_outstandingTask = null;
			}
		}

		private IBatchContainer ToBatchContainer(SQSMessage message)
		{
			_logger.LogInformation("Creating batch container with body: {message}", message.Body);

			return message.GetIsExternal()
				? ExtSqsBatchContainer.FromExternalSqsMessage(
					_serializationManager,
					message,
					new QueueMeta(_queueId.GetStringNamePrefix()),
					DateTime.Now.Ticks// todo: [Experiment] this should be caclulated appropriately
					//_lastReadMessage++,
				)
				: ExtSqsBatchContainer.FromSqsMessage(_serializationManager, message, _lastReadMessage++);
		}
	}
}