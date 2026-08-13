/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Sample configuration for the LDS-to-GDS scan direction, i.e. the list of
    /// Local Discovery Servers the GDS periodically reads servers from.
    /// </summary>
    /// <remarks>
    /// The <see cref="GlobalDiscoveryServerConfiguration"/> shipped with the
    /// stack is a source-generated type that only models the certificate/database
    /// settings, so the scan direction is not represented there. This sample type
    /// is stored as its own <c>&lt;Extensions&gt;</c> element in
    /// <c>Opc.Ua.GlobalDiscoveryServer.Config.xml</c> and read back with
    /// <see cref="Parse"/>. Add or remove <c>&lt;Url&gt;</c> entries to point the
    /// GDS at one or more LDS discovery endpoints.
    /// </remarks>
    public sealed class LdsScannerConfiguration
    {
        /// <summary>
        /// The XML local name of the extension element parsed by <see cref="Parse"/>.
        /// </summary>
        public const string ElementName = "LdsScannerConfiguration";

        /// <summary>
        /// The canonical LDS discovery endpoint per OPC 10000-12 is
        /// <c>opc.tcp://{host}:4840</c>.
        /// </summary>
        public const string DefaultLdsDiscoveryUrl = "opc.tcp://localhost:4840";

        /// <summary>
        /// The LDS discovery URLs the GDS scans for servers on the network.
        /// </summary>
        public IList<string> LdsDiscoveryUrls { get; } = new List<string>();

        /// <summary>
        /// Reads the sample <see cref="LdsScannerConfiguration"/> from the
        /// application configuration <c>Extensions</c>.
        /// </summary>
        /// <param name="configuration">The loaded application configuration.</param>
        /// <returns>
        /// The parsed configuration. When no extension element is present a
        /// configuration seeded with <see cref="DefaultLdsDiscoveryUrl"/> is
        /// returned so the sample works out of the box against a local LDS.
        /// </returns>
        public static LdsScannerConfiguration Parse(ApplicationConfiguration configuration)
        {
            var result = new LdsScannerConfiguration();

            if (configuration?.Extensions != null)
            {
                foreach (var extension in configuration.Extensions)
                {
                    if (extension.IsNull || extension.IsEmpty)
                    {
                        continue;
                    }

                    XElement element = extension.ToXElement();
                    if (element == null || element.Name.LocalName != ElementName)
                    {
                        continue;
                    }

                    XElement urls = element.Elements()
                        .FirstOrDefault(e => e.Name.LocalName == "LdsDiscoveryUrls");
                    if (urls != null)
                    {
                        foreach (XElement url in urls.Elements())
                        {
                            string value = url.Value?.Trim();
                            if (!string.IsNullOrEmpty(value))
                            {
                                result.LdsDiscoveryUrls.Add(value);
                            }
                        }
                    }

                    break;
                }
            }

            if (result.LdsDiscoveryUrls.Count == 0)
            {
                result.LdsDiscoveryUrls.Add(DefaultLdsDiscoveryUrl);
            }

            return result;
        }
    }
}
