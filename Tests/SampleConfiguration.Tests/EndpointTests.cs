/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Tier 0: the endpoint a sample client connects to has to be the endpoint its sample
    /// server listens on, and two samples that are meant to run side by side must not fight
    /// over the same port.
    /// </summary>
    [TestFixture]
    [Category("Configuration")]
    public partial class EndpointTests
    {
        /// <summary>
        /// Matches the endpoint urls the sample clients hard code in their sources.
        /// </summary>
        [GeneratedRegex("\"(opc\\.tcp://[^\"]+)\"", RegexOptions.CultureInvariant)]
        private static partial Regex EndpointUrlRegex();

        /// <summary>
        /// Ports which more than one sample uses on purpose, or knowingly.
        /// </summary>
        /// <remarks>
        /// Every other collision is a defect: the two samples cannot run at the same time.
        /// </remarks>
        private static readonly IReadOnlyList<string[]> s_acceptedPortSharing = new[]
        {
            // the WinForms and the console global discovery server are two hosts for the
            // same server, they are never started together
            new[] { "Gds", "GdsConsole" },

            // KNOWN DEFECT, see docs/TESTING.md: the WinForms aggregation server listens on
            // 62541 while aggregating a downstream reference server on the very same port,
            // so it cannot run as configured. The console variant uses 62530 instead.
            new[] { "Aggregation", "Reference" },
        };

        public static IEnumerable<SampleDefinition> ServersWithEndpoint
            => SampleCatalog.Servers.Where(sample => sample.ServerUrl != null);

        public static IEnumerable<SampleDefinition> ClientsWithHardCodedUrl
            => SampleCatalog.ClientsWithHardCodedUrl;

        [Test]
        [TestCaseSource(nameof(ServersWithEndpoint))]
        public async Task ServerListensOnTheCatalogedEndpoint(SampleDefinition sample)
        {
            using var pki = new TemporaryPki(sample.Name);

            ApplicationConfiguration configuration =
                await SampleConfigurationLoader.LoadAsync(sample.ServerConfig, pki).ConfigureAwait(false);

            IEnumerable<string> baseAddresses = SampleConfigurationLoader
                .GetBaseAddresses(configuration)
                .Select(Normalize);

            Assert.That(
                baseAddresses,
                Contains.Item(Normalize(sample.ServerUrl)),
                $"{sample.Name}: the base addresses in {sample.ServerConfig} do not contain the endpoint listed in SampleCatalog.");
        }

        [Test]
        [TestCaseSource(nameof(ClientsWithHardCodedUrl))]
        public void ClientConnectsToItsOwnServer(SampleDefinition sample)
        {
            string source = File.ReadAllText(RepositoryLayout.PathOf(sample.ClientEndpointSource));

            Match match = EndpointUrlRegex().Match(source);

            Assert.That(
                match.Success,
                Is.True,
                $"{sample.Name}: no hard coded opc.tcp url found in {sample.ClientEndpointSource}. " +
                "Either the client changed, or its catalog entry has to drop ClientEndpointSource.");

            Assert.That(
                Normalize(match.Groups[1].Value),
                Is.EqualTo(Normalize(sample.ServerUrl)),
                $"{sample.Name}: the client connects to an endpoint its own server does not offer.");
        }

        [Test]
        public void ServerPortsDoNotCollide()
        {
            var collisions = new List<string>();

            IEnumerable<IGrouping<int, SampleDefinition>> byPort = ServersWithEndpoint
                .GroupBy(sample => new Uri(sample.ServerUrl).Port);

            foreach (IGrouping<int, SampleDefinition> group in byPort)
            {
                string[] names = group.Select(sample => sample.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();

                if (names.Length < 2 || IsAccepted(names))
                {
                    continue;
                }

                collisions.Add($"port {group.Key}: {string.Join(", ", names)}");
            }

            Assert.That(
                collisions,
                Is.Empty,
                "Samples which share a port cannot run at the same time. Add the pair to " +
                "s_acceptedPortSharing only if that is intended and documented.");
        }

        private static bool IsAccepted(string[] names)
        {
            return s_acceptedPortSharing.Any(accepted =>
                accepted.Length == names.Length && !accepted.OrderBy(name => name, StringComparer.Ordinal).Except(names).Any());
        }

        /// <summary>
        /// Endpoint urls are compared case insensitively and without a trailing slash,
        /// which is how the stack treats them.
        /// </summary>
        private static string Normalize(string url)
        {
            return url?.TrimEnd('/').ToUpperInvariant();
        }
    }
}
