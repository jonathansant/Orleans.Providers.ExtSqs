using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Orleans.Providers.ExtSqs.Core;
using Orleans.Providers.ExtSqs.Utils;
using SQSMessage = Amazon.SQS.Model.Message;

namespace Orleans.Providers.ExtSqs.QueueManagement
{
	/// <summary>
	/// Wrapper/Helper class around AWS SQS queue service
	/// </summary>
	internal class SqsQueueManager
	{
		/// <summary>
		/// Maximum number of messages allowed by SQS to peak per request
		/// </summary>
		public const int MaxNumberOfMessageToPeak = 10;

		/// <summary>
		/// Refers to the MessageGroupId attribute that is sent with SQS messages.
		/// </summary>
		public const string MessageGroupIdAttributeName = "MessageGroupId";

		/// <summary>
		/// An identifier of messages that are produced by an external source. 
		/// </summary>
		public static string ExternalQueueAttributeName = "External";

		private const string AccessKeyPropertyName = "AccessKey";
		private const string SecretKeyPropertyName = "SecretKey";
		private const string ServicePropertyName = "Service";

		private readonly ILogger _logger;

		private string _queueUrl;
		private readonly AmazonSQSClient _sqsClient;
		private readonly string _queueName;

		/// <summary>
		/// Default Ctor
		/// </summary>
		/// <param name="loggerFactory">logger factory to use</param>
		/// <param name="queueName">The name of the queue</param>
		/// <param name="connectionString">The connection string</param>
		/// <param name="isFifo">The managed queue is a FIFO queue</param>
		public SqsQueueManager(
			ILoggerFactory loggerFactory,
			string queueName,
			string connectionString,
			bool isFifo
		)
		{
			_queueName = queueName;

			if (isFifo)
			{
				_queueName += ".fifo";
			}

			var credentials = ParseDataConnectionString(connectionString);

			_logger = loggerFactory.CreateLogger<SqsQueueManager>();

			_sqsClient = CreateClient(credentials.ServiceUrl, credentials.AccessKey, credentials.SecretKey);
		}

		#region Queue Management Operations

		public static async Task<IEnumerable<SqsQueueProperties>> GetAllQueuesAsync(
			string connectionString,
			string queuePrefix,
			ILogger logger
		)
		{
			try
			{
				var credentials = ParseDataConnectionString(connectionString);
				var client = CreateClient(credentials.ServiceUrl, credentials.AccessKey, credentials.SecretKey);

				var response = await client.ListQueuesAsync(queuePrefix);
				var queues = response.QueueUrls.Select(url => GetQueueProperties(client, url));

				return await Task.WhenAll(queues);
			}
			catch (Exception ex)
			{
				ReportErrorAndRethrow(
					logger,
					string.Empty,
					ex,
					"GetAllQueuesAsync",
					ErrorCode.AzureQueueBase);
			}

			return null;
		}

		private static async Task<SqsQueueProperties> GetQueueProperties(AmazonSQSClient client, string queueUrl)
		{
			var queueProps = await client.GetQueueAttributesAsync(queueUrl, new List<string>
			{
				"All"
			});

			var arn = queueProps.QueueARN.Split(':');

			return new SqsQueueProperties
			{
				Url = queueUrl,
				Name = arn.Last().Replace(".fifo", string.Empty)
			};
		}

		private static AmazonCredentials ParseDataConnectionString(string dataConnectionString)
		{
			var parameters = dataConnectionString.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
			var serviceConfig = parameters.FirstOrDefault(p => p.Contains(ServicePropertyName));
			var credentials = new AmazonCredentials();

			if (!string.IsNullOrWhiteSpace(serviceConfig))
			{
				var value = serviceConfig.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries);
				if (value.Length == 2 && !string.IsNullOrWhiteSpace(value[1]))
				{
					credentials.ServiceUrl = value[1];
				}
			}

			var secretKeyConfig = parameters.FirstOrDefault(p => p.Contains(SecretKeyPropertyName));
			if (!string.IsNullOrWhiteSpace(secretKeyConfig))
			{
				var value = secretKeyConfig.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries);
				if (value.Length == 2 && !string.IsNullOrWhiteSpace(value[1]))
				{
					credentials.SecretKey = value[1];
				}
			}

			var accessKeyConfig = parameters.FirstOrDefault(p => p.Contains(AccessKeyPropertyName));
			if (!string.IsNullOrWhiteSpace(accessKeyConfig))
			{
				var value = accessKeyConfig.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries);
				if (value.Length == 2 && !string.IsNullOrWhiteSpace(value[1]))
				{
					credentials.AccessKey = value[1];
				}
			}

			return credentials;
		}

		private static AmazonSQSClient CreateClient(string serviceUrl, string accessKey, string secreteKey)
		{
			if (serviceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
				serviceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			{
				// Local SQS instance (for testing)
				return new AmazonSQSClient(
					new BasicAWSCredentials("dummy", "dummyKey"),
					new AmazonSQSConfig
					{
						ServiceURL = serviceUrl
					});
			}

			if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(accessKey))
			{
				// AWS SQS instance (implicit auth - EC2 IAM Roles etc)
				return new AmazonSQSClient(new AmazonSQSConfig
				{
					RegionEndpoint = AwsUtils.GetRegionEndpoint(serviceUrl)
				});
			}

			// AWS SQS instance (auth via explicit credentials)
			return new AmazonSQSClient(
				new BasicAWSCredentials(accessKey, secreteKey),
				new AmazonSQSConfig
				{
					RegionEndpoint = AwsUtils.GetRegionEndpoint(serviceUrl)
				});
		}

		private async Task<string> GetQueueUrl()
		{
			try
			{
				var response = await _sqsClient.GetQueueUrlAsync(_queueName);
				if (!string.IsNullOrWhiteSpace(response.QueueUrl))
				{
					_queueUrl = response.QueueUrl;
				}

				return _queueUrl;
			}
			catch (QueueDoesNotExistException)
			{
				return null;
			}
		}

		/// <summary>
		/// Initialize SQSStorage by creating or connecting to an existent queue
		/// </summary>
		/// <returns></returns>
		public async Task InitQueueAsync()
		{
			try
			{
				if (string.IsNullOrWhiteSpace(await GetQueueUrl()))
				{
					var response = await _sqsClient.CreateQueueAsync(_queueName);
					_queueUrl = response.QueueUrl;
				}
			}
			catch (Exception exc)
			{
				ReportErrorAndRethrow(exc, "InitQueueAsync", ErrorCode.StreamProviderManagerBase);
			}
		}

		/// <summary>
		/// Delete the queue
		/// </summary>
		/// <returns></returns>
		public async Task DeleteQueue()
		{
			try
			{
				if (string.IsNullOrWhiteSpace(_queueUrl))
					throw new InvalidOperationException("Queue not initialized");
				await _sqsClient.DeleteQueueAsync(_queueUrl);
			}
			catch (Exception exc)
			{
				ReportErrorAndRethrow(exc, "DeleteQueue", ErrorCode.StreamProviderManagerBase);
			}
		}

		#endregion

		#region Messaging

		/// <summary>
		/// Add a message to the SQS queue
		/// </summary>
		/// <param name="message">Message request</param>
		/// <returns></returns>
		public async Task AddMessage(SendMessageRequest message)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(_queueUrl))
				{
					throw new InvalidOperationException("Queue not initialized");
				}

				message.QueueUrl = _queueUrl;
				message.MessageDeduplicationId = Guid.NewGuid().ToString(); // todo: implement this appropriately
				await _sqsClient.SendMessageAsync(message);
			}
			catch (Exception exc)
			{
				ReportErrorAndRethrow(exc, "AddMessage", ErrorCode.StreamProviderManagerBase);
			}
		}

		/// <summary>
		/// Get Messages from SQS Queue.
		/// </summary>
		/// <param name="count">The number of messages to peak. Min 1 and max 10</param>
		/// <returns>Collection with messages from the queue</returns>
		public async Task<IEnumerable<SQSMessage>> GetMessages(int count = 1)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(_queueUrl))
				{
					throw new InvalidOperationException("Queue not initialized");
				}

				if (count < 1)
				{
					throw new ArgumentOutOfRangeException(nameof(count));
				}

				var request = new ReceiveMessageRequest
				{
					QueueUrl = _queueUrl,
					MaxNumberOfMessages = count <= MaxNumberOfMessageToPeak ? count : MaxNumberOfMessageToPeak,
					AttributeNames = new List<string>
					{
						MessageGroupIdAttributeName
					},
					MessageAttributeNames = new List<string>
					{
						ExternalQueueAttributeName
					}
				};

				var response = await _sqsClient.ReceiveMessageAsync(request);
				return response.Messages;
			}
			catch (Exception exc)
			{
				ReportErrorAndRethrow(exc, "GetMessages", ErrorCode.StreamProviderManagerBase);
			}
			return null;
		}

		/// <summary>
		/// Delete a message from SQS queue
		/// </summary>
		/// <param name="message">The message to be deleted</param>
		/// <returns></returns>
		public async Task DeleteMessage(SQSMessage message)
		{
			try
			{
				if (message == null)
					throw new ArgumentNullException(nameof(message));

				if (string.IsNullOrWhiteSpace(message.ReceiptHandle))
					throw new ArgumentNullException(nameof(message.ReceiptHandle));

				if (string.IsNullOrWhiteSpace(_queueUrl))
					throw new InvalidOperationException("Queue not initialized");

				await _sqsClient.DeleteMessageAsync(
					new DeleteMessageRequest
					{
						QueueUrl = _queueUrl,
						ReceiptHandle = message.ReceiptHandle
					});
			}
			catch (Exception exc)
			{
				ReportErrorAndRethrow(exc, "GetMessages", ErrorCode.StreamProviderManagerBase);
			}
		}

		#endregion

		private void ReportErrorAndRethrow(Exception exc, string operation, ErrorCode errorCode)
		{
			ReportErrorAndRethrow(_logger, _queueName, exc, operation, errorCode);
		}

		private static void ReportErrorAndRethrow(
			ILogger logger, string queueName,
			Exception exc,
			string operation,
			ErrorCode errorCode)
		{
			var errMsg = string.Format(
				"Error doing {0} for SQS queue {1} " + Environment.NewLine
				+ "Exception = {2}", operation, queueName, exc);

			logger.LogError((int)errorCode, errMsg, exc);

			throw new AggregateException(errMsg, exc);
		}

		private struct AmazonCredentials
		{
			public string ServiceUrl { get; set; }

			public string AccessKey { get; set; }

			public string SecretKey { get; set; }
		}
	}
}