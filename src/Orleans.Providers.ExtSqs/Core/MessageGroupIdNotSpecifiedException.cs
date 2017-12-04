using System;

namespace Orleans.Providers.ExtSqs.Core
{
	public class MessageGroupIdNotSpecifiedException : Exception
	{
		public MessageGroupIdNotSpecifiedException(string message = null)
			: base(message ?? "Message contains no MessageGroupId!")
		{
		}
	}
}