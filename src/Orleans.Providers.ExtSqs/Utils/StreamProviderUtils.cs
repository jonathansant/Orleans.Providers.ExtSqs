using System;

namespace Orleans.Providers.ExtSqs.Utils
{
	/// <summary>
	/// SQS utility functions
	/// </summary>
	public class StreamProviderUtils
	{
		private static readonly Guid ExtSqsNamespace = Guid.Parse("56e72527-fe42-4a7c-9587-edadde42f2ee");

		/// <summary>
		/// Generates a deterministic GUID for a string. 
		/// Useful so that there is no restriction on GUID only stream Ids.
		/// </summary>
		/// <returns>The GUID representing the StreamId.</returns>
		public static Guid GenerateStreamGuid(string streamId) => GuidUtility.Create(ExtSqsNamespace, streamId);
	}
}