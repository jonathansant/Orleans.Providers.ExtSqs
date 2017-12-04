using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.Runtime.Internal.Util;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Providers.ExtSqs.QueueManagement;
using Orleans.Providers.ExtSqs.QueueMapper;
using Orleans.Providers.Streams.Common;
using Orleans.Serialization;
using Orleans.Streams;

namespace Orleans.Providers.ExtSqs.Core
{
	/// <summary> Factory class for SQS Queue based stream provider.</summary>
	public class ExtSqsAdapterFactory : IQueueAdapterFactory
	{
		private string _dataConnectionString;
		private string _providerName;
		private int _cacheSize;
		private bool _fifoQueues;
		private IStreamQueueMapper _streamQueueMapper;
		private IQueueAdapterCache _adapterCache;
		private SerializationManager _serializationManager;
		private ILoggerFactory _loggerFactory;

		private const bool FifoQueuesDefaultValue = true;
		private const int CacheSizeDefaultValue = 4096;
		private const string ExternalMessageIdentifierDefaultValue = "External";

		public const string FifoQueuesPropertyName = "FifoQueues";
		public const string VersionPropertyName = "Version";
		public const string QueuePrefixPropertyName = "QueuePrefix";
		public const string QueueNamesPropertyName = "QueueNames";
		public const string DataConnectionStringPropertyName = "DataConnectionString";
		public const string ExternalMessageIdentifierPropertyName = "ExternalMessageIdentifier";

		/// <summary>
		/// Application level failure handler override.
		/// </summary>
		protected Func<QueueId, Task<IStreamFailureHandler>> StreamFailureHandlerFactory { private get; set; }

		public void Init(IProviderConfiguration config, string providerName, IServiceProvider serviceProvider)
		{
			if (config == null)
				throw new ArgumentNullException("config");

			if (!config.Properties.TryGetValue(DataConnectionStringPropertyName, out _dataConnectionString))
				throw new ArgumentException($"{DataConnectionStringPropertyName} property not set");

			SqsQueueManager.ExternalQueueAttributeName = config.GetProperty(
				ExternalMessageIdentifierPropertyName,
				ExternalMessageIdentifierDefaultValue
			);

			_cacheSize = SimpleQueueAdapterCache.ParseSize(config, CacheSizeDefaultValue);
			_loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
			_providerName = providerName;
			_serializationManager = serviceProvider.GetRequiredService<SerializationManager>();
			_fifoQueues = config.GetBoolProperty(FifoQueuesPropertyName, FifoQueuesDefaultValue);

			var queues = GetQueues(config);

			_streamQueueMapper = new ExtSqsQueueMapper(queues, _loggerFactory.CreateLogger<ExtSqsQueueMapper>());
			_adapterCache = new SimpleQueueAdapterCache(_cacheSize, _providerName, _loggerFactory);

			if (StreamFailureHandlerFactory == null)
			{
				StreamFailureHandlerFactory =
					qid => Task.FromResult<IStreamFailureHandler>(new NoOpStreamDeliveryFailureHandler());
			}
		}

		/// <summary>Creates the SQS Queue based adapter.</summary>
		public virtual Task<IQueueAdapter> CreateAdapter()
		{
			var adapter = new ExtSqsAdapter(
				_serializationManager,
				_streamQueueMapper,
				_loggerFactory,
				_dataConnectionString,
				_providerName,
				_fifoQueues
			);

			return Task.FromResult<IQueueAdapter>(adapter);
		}

		/// <summary>Creates the adapter cache.</summary>
		public virtual IQueueAdapterCache GetQueueAdapterCache() => _adapterCache;

		/// <summary>Creates the factory stream queue mapper.</summary>
		public IStreamQueueMapper GetStreamQueueMapper() => _streamQueueMapper;


		/// <summary>
		/// Creates a delivery failure handler for the specified queue.
		/// </summary>
		/// <param name="queueId"></param>
		/// <returns></returns>
		public Task<IStreamFailureHandler> GetDeliveryFailureHandler(QueueId queueId) => StreamFailureHandlerFactory(queueId);

		private static string CreateQueuePrefix(string prefix, string version) => string.IsNullOrEmpty(version)
				? prefix
				: $"{prefix}-{version}";

		private IEnumerable<SqsQueueProperties> GetQueues(IProviderConfiguration config)
		{
			var logger = _loggerFactory.CreateLogger<SqsQueueManager>();

			var queueNames = config.GetProperty(QueueNamesPropertyName, null);
			if (string.IsNullOrEmpty(queueNames))
			{
				if (!config.Properties.TryGetValue(QueuePrefixPropertyName, out var queuePrefix))
					throw new ArgumentException($"{QueuePrefixPropertyName} property not set");

				var version = config.GetProperty(VersionPropertyName, string.Empty);

				return AsyncHelpers.RunSync(() => SqsQueueManager.GetAllQueuesAsync(
					_dataConnectionString,
					CreateQueuePrefix(queuePrefix, version),
					logger
				));
			}

			var queueNameList = queueNames.Split(';');
			var queuePropsCollection = new List<SqsQueueProperties>();
			Parallel.ForEach(queueNameList, queueName =>
			{
				var queuesForPrefix = AsyncHelpers.RunSync(() => SqsQueueManager.GetAllQueuesAsync(
					_dataConnectionString,
					queueName,
					logger
				));

				queuePropsCollection.AddRange(queuesForPrefix);
			});

			return queuePropsCollection;
		}
	}
}