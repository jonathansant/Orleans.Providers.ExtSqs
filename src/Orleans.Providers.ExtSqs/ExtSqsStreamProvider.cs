using Orleans.Providers.ExtSqs.Core;
using Orleans.Providers.Streams.Common;

namespace Orleans.Providers.ExtSqs
{
	/// <summary>
	/// Persistent stream provider that uses SQS queue for persistence
	/// </summary>
	public class ExtSqsStreamProvider : PersistentStreamProvider<ExtSqsAdapterFactory>
	{
	}
}