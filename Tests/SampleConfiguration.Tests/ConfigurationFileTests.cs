/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Tier 0: every application configuration shipped with a sample has to load and
    /// validate. A sample whose configuration is broken cannot start, so this is the
    /// cheapest possible check that the samples are still usable.
    /// </summary>
    [TestFixture]
    [Category("Configuration")]
    public class ConfigurationFileTests
    {
        /// <summary>
        /// All configuration files found in the repository.
        /// </summary>
        public static IEnumerable<string> ConfigurationFiles => RepositoryLayout.EnumerateConfigurationFiles();

        [Test]
        [TestCaseSource(nameof(ConfigurationFiles))]
        public async Task ConfigurationLoadsAndValidates(string relativePath)
        {
            using var pki = new TemporaryPki(relativePath);

            ApplicationConfiguration configuration =
                await SampleConfigurationLoader.LoadAsync(relativePath, pki).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(configuration.ApplicationName, Is.Not.Null.And.Not.Empty, "ApplicationName is missing.");
                Assert.That(configuration.ApplicationUri, Is.Not.Null.And.Not.Empty, "ApplicationUri is missing.");
                Assert.That(
                    configuration.ApplicationType,
                    Is.AnyOf(ApplicationType.Client, ApplicationType.Server, ApplicationType.ClientAndServer, ApplicationType.DiscoveryServer),
                    "ApplicationType is not set.");
                Assert.That(configuration.SecurityConfiguration, Is.Not.Null, "SecurityConfiguration is missing.");
            });
        }

        [Test]
        [TestCaseSource(nameof(ConfigurationFiles))]
        public async Task ServerConfigurationHasBaseAddresses(string relativePath)
        {
            using var pki = new TemporaryPki(relativePath);

            ApplicationConfiguration configuration =
                await SampleConfigurationLoader.LoadAsync(relativePath, pki).ConfigureAwait(false);

            if (configuration.ApplicationType == ApplicationType.Client)
            {
                Assert.Pass("Not a server configuration.");
            }

            Assert.That(
                SampleConfigurationLoader.GetBaseAddresses(configuration),
                Is.Not.Empty,
                "A server has to declare at least one base address.");
        }

        /// <summary>
        /// Guards the guard: a configuration which is not valid has to fail, otherwise the
        /// test above would pass for every sample no matter what its configuration says.
        /// </summary>
        [Test]
        public void BrokenConfigurationIsRejected()
        {
            string path = Path.Combine(Path.GetTempPath(), $"Broken-{System.Guid.NewGuid():N}.Config.xml");

            try
            {
                // a well formed xml document, but not an application configuration
                File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?><NotAConfiguration />");

                Assert.That(
                    async () => await SampleConfigurationLoader.LoadAsync(path).ConfigureAwait(false),
                    Throws.Exception,
                    "The loader accepted a file which is not an application configuration.");
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// A configuration file which no catalog entry points at belongs to a sample that
        /// no tier of the test suite covers.
        /// </summary>
        [Test]
        public void EveryConfigurationFileBelongsToACatalogEntry()
        {
            var cataloged = new HashSet<string>(
                SampleCatalog.All
                    .SelectMany(sample => new[] { sample.ServerConfig, sample.ClientConfig })
                    .Where(path => path != null),
                System.StringComparer.OrdinalIgnoreCase);

            IEnumerable<string> orphans = ConfigurationFiles.Where(path => !cataloged.Contains(path));

            Assert.That(
                orphans,
                Is.Empty,
                "These configuration files are not referenced by SampleCatalog, so the sample they belong to is untested.");
        }
    }
}
