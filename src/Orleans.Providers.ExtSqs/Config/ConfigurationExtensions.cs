using System.Collections.Generic;
using Orleans.Providers.ExtSqs;
using Orleans.Providers.ExtSqs.Config;
using Orleans.Runtime.Configuration;

// ReSharper disable once CheckNamespace
namespace Orleans.Hosting
{
	public static class ConfigurationExtensions
	{
		public static void AddSqsStreamProvider(
			this ClientConfiguration config,
			string providerName,
			IDictionary<string, string> properties
		) => config.RegisterStreamProvider<ExtSqsStreamProvider>(providerName, properties);

		public static void AddSqsStreamProvider(
			this ClusterConfiguration config,
			string providerName,
			IDictionary<string, string> properties
		) => config.Globals.RegisterStreamProvider<ExtSqsStreamProvider>(providerName, properties);

		public static void AddSqsStreamProvider(
			this ClientConfiguration config,
			string providerName,
			ExtSqsConfigurationBuilder builder
		) => AddSqsStreamProvider(config, providerName, builder.Build());

		public static void AddSqsStreamProvider(
			this ClusterConfiguration config,
			string providerName,
			ExtSqsConfigurationBuilder builder
		) => AddSqsStreamProvider(config, providerName, builder.Build());
	}
}