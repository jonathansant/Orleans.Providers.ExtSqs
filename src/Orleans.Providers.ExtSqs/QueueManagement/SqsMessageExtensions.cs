using Amazon.SQS.Model;

namespace Orleans.Providers.ExtSqs.QueueManagement
{
	internal static class SqsMessageExtensions
	{
		public static string GetMessageGroupId(this Message message)
		{
			message.Attributes.TryGetValue(SqsQueueManager.MessageGroupIdAttributeName, out var messageGroupId);
			return messageGroupId;
		}

		public static bool GetIsExternal(this Message message)
		{
			if (!message.MessageAttributes.TryGetValue(SqsQueueManager.ExternalQueueAttributeName, out var isExternalAttribute))
			{
				return false;
			}

			bool.TryParse(isExternalAttribute.StringValue, out var isExternal);
			return isExternal;
		}
	}
}