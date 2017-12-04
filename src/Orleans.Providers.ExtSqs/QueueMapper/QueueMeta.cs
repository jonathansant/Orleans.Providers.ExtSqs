using System.Linq;

namespace Orleans.Providers.ExtSqs.QueueMapper
{
	internal class QueueMeta
	{
		public string Namespace { get; }
		public uint QueueIndex { get; }
		public uint GeneratedId { get; }
		public string QueueName { get; }

		public QueueMeta(string queueName)
		{
			var parts = queueName.Split('_');

			Namespace = parts.First();
			QueueIndex = uint.Parse(parts.Last());
			QueueName = queueName;
			GeneratedId = JenkinsHash.ComputeHash(QueueName);
		}
	}
}