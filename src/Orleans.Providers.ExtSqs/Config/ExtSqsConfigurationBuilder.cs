using System.Collections.Generic;
using Orleans.Providers.ExtSqs.Core;

namespace Orleans.Providers.ExtSqs.Config
{
	public class ExtSqsConfigurationBuilder
	{
		private readonly IDictionary<string, string> _properties;

		public ExtSqsConfigurationBuilder() => _properties = new Dictionary<string, string>();

		public ExtSqsConfigurationBuilder ConnectWith(
			string accessKey,
			string secreteKey,
			string region
		)
		{
			_properties.Add(
				ExtSqsAdapterFactory.DataConnectionStringPropertyName,
				$"Service={region};AccessKey={accessKey};SecretKey={secreteKey};");

			return this;
		}

		public ExtSqsConfigurationBuilder WithVersion(string version)
		{
			_properties.Add(ExtSqsAdapterFactory.VersionPropertyName, version);
			return this;
		}

		public ExtSqsConfigurationBuilder WithQueuePrefix(string prefix)
		{
			_properties.Add(ExtSqsAdapterFactory.QueuePrefixPropertyName, prefix);
			return this;
		}

		public ExtSqsConfigurationBuilder WithQueueNames(IEnumerable<string> queueNames)
		{
			_properties.Add(ExtSqsAdapterFactory.QueueNamesPropertyName, string.Join(";", queueNames));
			return this;
		}

		public ExtSqsConfigurationBuilder IdentifyExternallyProducedMessagesWith(string attributeName)
		{
			_properties.Add(ExtSqsAdapterFactory.ExternalMessageIdentifierPropertyName, attributeName);
			return this;
		}

		public ExtSqsConfigurationBuilder UseStandardQueues()
		{
			_properties.Add(ExtSqsAdapterFactory.FifoQueuesPropertyName, false.ToString());
			return this;
		}

		public IDictionary<string, string> Build() => _properties;
	}
}