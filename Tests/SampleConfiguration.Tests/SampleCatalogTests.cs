/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// Tier 0: keeps the catalog honest. Every path it names has to exist, so that a
    /// renamed or removed sample fails here instead of silently dropping out of the
    /// higher tiers.
    /// </summary>
    [TestFixture]
    [Category("Configuration")]
    public class SampleCatalogTests
    {
        public static IEnumerable<SampleDefinition> Samples => SampleCatalog.All;

        [Test]
        [TestCaseSource(nameof(Samples))]
        public void CatalogPathsExist(SampleDefinition sample)
        {
            IEnumerable<string> paths = new[]
            {
                sample.ServerProject,
                sample.ServerConfig,
                sample.ClientProject,
                sample.ClientConfig,
                sample.ClientEndpointSource,
            }.Where(path => path != null);

            Assert.Multiple(() => {
                foreach (string path in paths)
                {
                    Assert.That(RepositoryLayout.Exists(path), Is.True, $"{sample.Name}: '{path}' does not exist.");
                }
            });
        }

        [Test]
        [TestCaseSource(nameof(Samples))]
        public void CatalogEntryIsUsable(SampleDefinition sample)
        {
            Assert.Multiple(() => {
                Assert.That(sample.Name, Is.Not.Null.And.Not.Empty);
                Assert.That(
                    sample.ServerProject ?? sample.ClientProject,
                    Is.Not.Null,
                    $"{sample.Name}: an entry without any project does nothing.");
                Assert.That(
                    sample.ClientEndpointSource == null || sample.ClientProject != null,
                    Is.True,
                    $"{sample.Name}: a client endpoint source without a client project.");
            });
        }

        [Test]
        public void SampleNamesAreUnique()
        {
            IEnumerable<string> duplicates = SampleCatalog.All
                .GroupBy(sample => sample.Name, System.StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key);

            Assert.That(duplicates, Is.Empty, "Sample names are used as test case names and have to be unique.");
        }
    }
}
