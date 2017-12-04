using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.SQS.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Orleans.Providers.ExtSqs.QueueManagement;
using Orleans.Providers.ExtSqs.QueueMapper;
using Orleans.Providers.ExtSqs.Utils;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;
using SQSMessage = Amazon.SQS.Model.Message;

namespace Orleans.Providers.ExtSqs.Core
{
	[Serializable]
	internal class ExtSqsBatchContainer : IBatchContainer
	{
		[NonSerialized]
		// Need to store reference to the original SQS Message to be able to delete it later on.
		// Don't need to serialize it, since we are never interested in sending it to stream consumers.
		internal SQSMessage Message;

		public Guid StreamGuid { get; }

		public string StreamNamespace { get; }

		public StreamSequenceToken SequenceToken => sequenceToken;

		// ReSharper disable once InconsistentNaming
		[JsonProperty] private EventSequenceTokenV2 sequenceToken;

		// ReSharper disable once InconsistentNaming
		[JsonProperty] private readonly List<object> events;

		// ReSharper disable once InconsistentNaming
		[JsonProperty] private readonly Dictionary<string, object> requestContext;

		private readonly bool _isExternalBatch;

		[JsonConstructor]
		private ExtSqsBatchContainer(
			Guid streamGuid,
			string streamNamespace,
			List<object> events,
			Dictionary<string, object> requestContext,
			EventSequenceTokenV2 sequenceToken
		) : this(streamGuid, streamNamespace, events, requestContext, sequenceToken, false)
		{
			
		}

		private ExtSqsBatchContainer(
			Guid streamGuid,
			string streamNamespace,
			List<object> events,
			Dictionary<string, object> requestContext,
			EventSequenceTokenV2 sequenceToken = null,
			bool isExternalBatch = false
		)
		{
			StreamGuid = streamGuid;
			StreamNamespace = streamNamespace;
			this.events = events ?? throw new ArgumentNullException("events", "Message contains no events");
			this.requestContext = requestContext;
			this.sequenceToken = sequenceToken;
			_isExternalBatch = isExternalBatch;
		}

		public IEnumerable<Tuple<T, StreamSequenceToken>> GetEvents<T>()
		{
			if (!_isExternalBatch)
			{
				return this.events
					.OfType<T>()
					.Select((@event, iteration) =>
						Tuple.Create<T, StreamSequenceToken>(@event, this.sequenceToken.CreateSequenceTokenForEvent(iteration)));
			}

			return this.events
				.Select((@event, iteration) =>
				{
					try
					{
						T message;

						if (typeof(T).IsPrimitive || typeof(T) == typeof(string) || typeof(T) == typeof(decimal))
						{
							message = (T)Convert.ChangeType(@event, typeof(T));
						}
						else
						{
							message = JsonConvert.DeserializeObject<T>((string)@event); // todo: support for multiple serializer
						}

						return Tuple.Create<T, StreamSequenceToken>(message, this.sequenceToken.CreateSequenceTokenForEvent(iteration));
					}
					catch (Exception)
					{
						return null;
					}
				})
				.Where(@event => @event != null);
		}

		public bool ShouldDeliver(IStreamIdentity stream, object filterData, StreamFilterPredicate shouldReceiveFunc)
		{
			// If there is something in this batch that the consumer is interested in, we should send it
			// else the consumer is not interested in any of these events, so don't send.
			return this.events.Any(item => shouldReceiveFunc(stream, filterData, item));
		}

		internal static SendMessageRequest ToSqsMessage<T>(
			SerializationManager serializationManager,
			Guid streamGuid,
			string streamNamespace,
			IEnumerable<T> events,
			Dictionary<string, object> requestContext
		)
		{
			var sqsBatchMessage = new ExtSqsBatchContainer(
				streamGuid,
				streamNamespace,
				events.Cast<object>().ToList(),
				requestContext);

			var rawBytes = serializationManager.SerializeToByteArray(sqsBatchMessage);

			var payload = new JObject
			{
				{"payload", JToken.FromObject(rawBytes)}
			};

			return new SendMessageRequest
			{
				MessageBody = payload.ToString()
			};
		}

		internal static ExtSqsBatchContainer FromSqsMessage(
			SerializationManager serializationManager,
			SQSMessage msg,
			long sequenceId
		)
		{
			var json = JObject.Parse(msg.Body);
			var sqsBatch = serializationManager.DeserializeFromByteArray<ExtSqsBatchContainer>(json["payload"].ToObject<byte[]>());

			sqsBatch.Message = msg;
			sqsBatch.sequenceToken = new EventSequenceTokenV2(sequenceId);

			return sqsBatch;
		}

		internal static ExtSqsBatchContainer FromExternalSqsMessage(
			SerializationManager serializationManager,
			SQSMessage sqsMessage,
			QueueMeta queueMeta,
			long sequenceId
		)
		{
			var messageGroupId = sqsMessage.GetMessageGroupId();
			if (string.IsNullOrEmpty(messageGroupId))
			{
				throw new MessageGroupIdNotSpecifiedException();
			}

			return new ExtSqsBatchContainer(
				StreamProviderUtils.GenerateStreamGuid(messageGroupId),
				queueMeta.Namespace,
				new List<object>
				{
					sqsMessage.Body
				},
				null,
				new EventSequenceTokenV2(sequenceId),
				isExternalBatch: true)
			{
				Message = sqsMessage
			};
		}

		public bool ImportRequestContext()
		{
			if (this.requestContext == null)
			{
				return false;
			}

			foreach (var contextProperties in this.requestContext)
			{
				RequestContext.Set(contextProperties.Key, contextProperties.Value);
			}

			return true;
		}

		public override string ToString()
			=> string.Format("[SQSBatchContainer:Stream={0},#Items={1}]", StreamGuid, this.events.Count);
	}
}