/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Opc.Ua.Gds.Server.DB;

namespace Opc.Ua.Gds.Server.Database.Sql
{
    public class SqlApplicationsDatabase : ApplicationsDatabaseBase, ICertificateRequest
    {
        #region IApplicationsDatabase Members
        public override void Initialize()
        {
            using (gdsdbEntities entities = new gdsdbEntities())
            {
                Assembly assembly = typeof(SqlApplicationsDatabase).GetTypeInfo().Assembly;
                #pragma warning disable CA2000 // Justification: WinForms/sample ownership or lifetime is managed outside the local scope.
                StreamReader istrm = new StreamReader(assembly.GetManifestResourceStream("Opc.Ua.Gds.Server.DB.gdsdb.edmx.sql"));
                #pragma warning restore CA2000
                string tables = istrm.ReadToEnd();
                entities.Database.EnsureCreated();
                var parts = tables.Split(new string[] { "GO" }, System.StringSplitOptions.None);
                foreach (var part in parts)
                {
                    if (!string.IsNullOrWhiteSpace(part)) { entities.Database.ExecuteSqlRaw(part); }
                }
                entities.SaveChanges();
            }
        }

        public override NodeId RegisterApplication(
            ApplicationRecordDataType application
            )
        {
            NodeId appNodeId = base.RegisterApplication(application);
            if ((appNodeId).IsNull)
            {
                appNodeId = new NodeId(Guid.NewGuid(), NamespaceIndex);
            }
            Guid applicationId = GetNodeIdGuid(appNodeId);
            string capabilities = base.ServerCapabilities(application);

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                Application record = null;

                if (applicationId != Guid.Empty)
                {
                    var results = from ii in entities.Applications
                                  where ii.ApplicationId == applicationId
                                  select ii;

                    record = results.SingleOrDefault();

                    if (record != null)
                    {
                        var endpoints = from ii in entities.ServerEndpoints
                                        where ii.ApplicationId == record.ID
                                        select ii;

                        foreach (var endpoint in endpoints)
                        {
                            entities.ServerEndpoints.Remove(endpoint);
                        }

                        var names = from ii in entities.ApplicationNames
                                    where ii.ApplicationId == record.ID
                                    select ii;

                        foreach (var name in names)
                        {
                            entities.ApplicationNames.Remove(name);
                        }

                        entities.SaveChanges();
                    }
                }

                bool isNew = false;

                if (record == null)
                {
                    applicationId = Guid.NewGuid();
                    record = new Application() { ApplicationId = applicationId };
                    isNew = true;
                }

                record.ApplicationUri = application.ApplicationUri;
                record.ApplicationName = application.ApplicationNames[0].Text;
                record.ApplicationType = (int)application.ApplicationType;
                record.ProductUri = application.ProductUri;
                record.ServerCapabilities = capabilities;

                if (isNew)
                {
                    entities.Applications.Add(record);
                }

                entities.SaveChanges();

                if (application.DiscoveryUrls != null)
                {
                    foreach (var discoveryUrl in application.DiscoveryUrls)
                    {
                        entities.ServerEndpoints.Add(new ServerEndpoint() { ApplicationId = record.ID, DiscoveryUrl = discoveryUrl });
                    }
                }

                if (application.ApplicationNames != null && application.ApplicationNames.Count >= 1)
                {
                    foreach (var applicationName in application.ApplicationNames)
                    {
                        entities.ApplicationNames.Add(new ApplicationName() { ApplicationId = record.ID, Locale = applicationName.Locale, Text = applicationName.Text });
                    }
                }

                entities.SaveChanges();
                m_lastCounterResetTime = DateTime.UtcNow;
                return new NodeId(applicationId, NamespaceIndex); ;
            }
        }

        public override void UnregisterApplication(NodeId applicationId)
        {
            Guid id = GetNodeIdGuid(applicationId);

            List<byte[]> certificates = new List<byte[]>();

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var result = (from ii in entities.Applications
                              where ii.ApplicationId == id
                              select ii).SingleOrDefault();

                if (result == null)
                {
                    throw new ArgumentException("A record with the specified application id does not exist.", nameof(applicationId));
                }

                foreach (var entry in new List<CertificateRequest>(result.CertificateRequests))
                {
                    entities.CertificateRequests.Remove(entry);
                }

                foreach (var entry in new List<CertificateStore>(result.CertificateStores))
                {
                    entities.CertificateStores.Remove(entry);
                }

                foreach (var entry in new List<ApplicationName>(result.ApplicationNames))
                {
                    entities.ApplicationNames.Remove(entry);
                }

                foreach (var entry in new List<ServerEndpoint>(result.ServerEndpoints))
                {
                    entities.ServerEndpoints.Remove(entry);
                }

                entities.Applications.Remove(result);
                entities.SaveChanges();
                m_lastCounterResetTime = DateTime.UtcNow;
            }
        }

        public override ApplicationRecordDataType GetApplication(
            NodeId applicationId
            )
        {
            Guid id = GetNodeIdGuid(applicationId);
            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var results = from x in entities.Applications
                              where x.ApplicationId == id
                              select x;

                var result = results.SingleOrDefault();

                if (result == null)
                {
                    return null;
                }

                List<LocalizedText> names = new List<LocalizedText>();
                if (result.ApplicationNames != null)
                {
                    foreach (var entry in new List<ApplicationName>(result.ApplicationNames))
                    {
                        names.Add(new LocalizedText(entry.Locale, entry.Text));
                    }
                }
                else
                {
                    names.Add(new LocalizedText(result.ApplicationName));
                }

                List<string> discoveryUrls = null;

                if (result.ServerEndpoints != null)
                {
                    discoveryUrls = new List<string>();

                    foreach (var endpoint in result.ServerEndpoints)
                    {
                        discoveryUrls.Add(endpoint.DiscoveryUrl);
                    }
                }

                string[] capabilities = null;

                if (result.ServerCapabilities != null &&
                    result.ServerCapabilities.Length > 0)
                {
                    capabilities = result.ServerCapabilities.Split(',');
                }

                return new ApplicationRecordDataType() {
                    ApplicationId = new NodeId(result.ApplicationId, NamespaceIndex),
                    ApplicationUri = result.ApplicationUri,
                    ApplicationType = (ApplicationType)result.ApplicationType,
                    ApplicationNames = names,
                    ProductUri = result.ProductUri,
                    DiscoveryUrls = discoveryUrls,
                    ServerCapabilities = capabilities
                };
            }
        }

        public override ApplicationRecordDataType[] FindApplications(
            string applicationUri
            )
        {
            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var results = from x in entities.Applications
                              where x.ApplicationUri == applicationUri
                              select x;

                List<ApplicationRecordDataType> records = new List<ApplicationRecordDataType>();

                foreach (var result in results)
                {
                    var names = new List<LocalizedText>();

                    IEnumerable<ApplicationName> applicationNames =
                    from ii in entities.ApplicationNames
                    where ii.ApplicationId == result.ID
                    select ii;

                    foreach (ApplicationName applicationName in applicationNames)
                    {
                        names.Add(new LocalizedText(applicationName.Locale, applicationName.Text));
                    }

                    if (names.Count == 0 && result.ApplicationName != null)
                    {
                        names = [new LocalizedText(result.ApplicationName)];
                    }


                    List<string> discoveryUrls = null;

                    if (result.ServerEndpoints != null)
                    {
                        discoveryUrls = new List<string>();

                        foreach (var endpoint in result.ServerEndpoints)
                        {
                            discoveryUrls.Add(endpoint.DiscoveryUrl);
                        }
                    }

                    string[] capabilities = null;

                    if (result.ServerCapabilities != null)
                    {
                        capabilities = result.ServerCapabilities.Split(',');
                    }

                    records.Add(new ApplicationRecordDataType() {
                        ApplicationId = new NodeId(result.ApplicationId, NamespaceIndex),
                        ApplicationUri = result.ApplicationUri,
                        ApplicationType = (ApplicationType)result.ApplicationType,
                        ApplicationNames = new List<LocalizedText>(names),
                        ProductUri = result.ProductUri,
                        DiscoveryUrls = discoveryUrls,
                        ServerCapabilities = capabilities
                    });
                }

                return records.ToArray();
            }
        }

        public override ApplicationDescription[] QueryApplications(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            uint applicationType,
            string productUri,
            ArrayOf<string> serverCapabilities,
            out DateTimeUtc lastCounterResetTime,
            out uint nextRecordId)
        {
            lastCounterResetTime = DateTime.MinValue;
            nextRecordId = 0;
            var records = new List<ApplicationDescription>();

            lastCounterResetTime = m_lastCounterResetTime;

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var results = from x in entities.Applications
                              where ((int)startingRecordId == 0 || (int)startingRecordId <= x.ID)
                              orderby x.ID
                              select x;

                int lastID = 0;

                foreach (var result in results)
                {

                    if (!String.IsNullOrEmpty(applicationName))
                    {
                        if (!Match(result.ApplicationName, applicationName))
                        {
                            continue;
                        }
                    }

                    if (!String.IsNullOrEmpty(applicationUri))
                    {
                        if (!Match(result.ApplicationUri, applicationUri))
                        {
                            continue;
                        }
                    }

                    if (!String.IsNullOrEmpty(productUri))
                    {
                        if (!Match(result.ProductUri, productUri))
                        {
                            continue;
                        }
                    }

                    string[] capabilities = null;
                    if (!String.IsNullOrEmpty(result.ServerCapabilities))
                    {
                        capabilities = result.ServerCapabilities.Split(',');
                    }

                    if (!serverCapabilities.IsNull && serverCapabilities.Count > 0)
                    {
                        bool match = true;

                        for (int ii = 0; ii < serverCapabilities.Count; ii++)
                        {
                            if (capabilities == null || !capabilities.Contains(serverCapabilities[ii]))
                            {
                                match = false;
                                break;
                            }
                        }

                        if (!match)
                        {
                            continue;
                        }
                    }

                    // type filter, 0 and 3 returns all
                    // filter for servers
                    if (applicationType == 1 &&
                        result.ApplicationType == (int)ApplicationType.Client)
                    {
                        continue;
                    }
                    else // filter for clients
                    if (applicationType == 2 &&
                        result.ApplicationType != (int)ApplicationType.Client &&
                        result.ApplicationType != (int)ApplicationType.ClientAndServer)
                    {
                        continue;
                    }

                    var discoveryUrls = new List<string>();
                    if (result.ServerEndpoints != null)
                    {
                        discoveryUrls = new List<string>();

                        foreach (var endpoint in result.ServerEndpoints)
                        {
                            discoveryUrls.Add(endpoint.DiscoveryUrl);
                        }
                    }

                    if (lastID == 0)
                    {
                        lastID = result.ID;
                    }
                    else
                    {
                        if (maxRecordsToReturn != 0 &&
                            records.Count >= maxRecordsToReturn)
                        {
                            break;
                        }

                        lastID = result.ID;
                    }

                    var names = new List<LocalizedText>();

                    IEnumerable<ApplicationName> applicationNames =
                    from ii in entities.ApplicationNames
                    where ii.ApplicationId == result.ID
                    select ii;

                    foreach (ApplicationName appName in applicationNames)
                    {
                        names.Add(new LocalizedText(appName.Locale, appName.Text));
                    }

                    records.Add(new ApplicationDescription() {
                        ApplicationUri = result.ApplicationUri,
                        ProductUri = result.ProductUri,
                        ApplicationName = names.FirstOrDefault().IsNull ? new LocalizedText(result.ApplicationName) : names.FirstOrDefault(),
                        ApplicationType = (ApplicationType)result.ApplicationType,
                        GatewayServerUri = null,
                        DiscoveryProfileUri = null,
                        DiscoveryUrls = discoveryUrls
                    });
                    nextRecordId = (uint)lastID + 1;

                }
                return records.ToArray();
            }
        }

        public override ServerOnNetwork[] QueryServers(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            ArrayOf<string> serverCapabilities,
            out DateTimeUtc lastCounterResetTime)
        {
            lastCounterResetTime = m_lastCounterResetTime;

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var results = from x in entities.ServerEndpoints
                              join y in entities.Applications on x.ApplicationId equals y.ID
                              where ((int)startingRecordId == 0 || (int)startingRecordId <= x.ID)
                              orderby x.ID
                              select new {
                                  x.ID,
                                  y.ApplicationName,
                                  y.ApplicationUri,
                                  y.ProductUri,
                                  x.DiscoveryUrl,
                                  y.ServerCapabilities
                              };

                List<ServerOnNetwork> records = new List<ServerOnNetwork>();
                int lastID = 0;

                foreach (var result in results)
                {

                    if (!String.IsNullOrEmpty(applicationName))
                    {
                        if (!Match(result.ApplicationName, applicationName))
                        {
                            continue;
                        }
                    }

                    if (!String.IsNullOrEmpty(applicationUri))
                    {
                        if (!Match(result.ApplicationUri, applicationUri))
                        {
                            continue;
                        }
                    }

                    if (!String.IsNullOrEmpty(productUri))
                    {
                        if (!Match(result.ProductUri, productUri))
                        {
                            continue;
                        }
                    }

                    string[] capabilities = null;
                    if (!String.IsNullOrEmpty(result.ServerCapabilities))
                    {
                        capabilities = result.ServerCapabilities.Split(',');
                    }

                    if (!serverCapabilities.IsNull && serverCapabilities.Count > 0)
                    {
                        bool match = true;

                        for (int ii = 0; ii < serverCapabilities.Count; ii++)
                        {
                            if (capabilities == null || !capabilities.Contains(serverCapabilities[ii]))
                            {
                                match = false;
                                break;
                            }
                        }

                        if (!match)
                        {
                            continue;
                        }
                    }

                    if (lastID == 0)
                    {
                        lastID = result.ID;
                    }
                    else
                    {
                        if (maxRecordsToReturn != 0 &&
                            lastID != result.ID &&
                            records.Count >= maxRecordsToReturn)
                        {
                            break;
                        }

                        lastID = result.ID;
                    }

                    records.Add(new ServerOnNetwork() {
                        RecordId = (uint)result.ID,
                        ServerName = result.ApplicationName,
                        DiscoveryUrl = result.DiscoveryUrl,
                        ServerCapabilities = capabilities
                    });


                }

                return records.ToArray();
            }
        }

        public override bool SetApplicationCertificate(
            NodeId applicationId,
            string certificateTypeId,
            ByteString certificate)
        {
            Guid id = GetNodeIdGuid(applicationId);

            if (certificateTypeId.Equals(nameof(Ua.ObjectTypeIds.UserCertificateType), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var results = from x in entities.Applications
                              where x.ApplicationId == id
                              select x;

                var result = results.SingleOrDefault();

                if (result == null)
                {
                    return false;
                }

                if (certificateTypeId.Equals(nameof(Ua.ObjectTypeIds.HttpsCertificateType), StringComparison.OrdinalIgnoreCase))
                {
                    result.HttpsCertificate = certificate.ToArray();
                }
                else
                {
                    result.Certificate = certificate.ToArray();
                }

                entities.SaveChanges();
            }

            return true;
        }

        public override bool GetApplicationCertificate(
            NodeId applicationId,
            string certificateTypeId,
            out ByteString certificate)
        {
            certificate = default;

            Guid id = GetNodeIdGuid(applicationId);

            List<byte[]> certificates = new List<byte[]>();

            if (certificateTypeId.Equals(nameof(Ua.ObjectTypeIds.UserCertificateType), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var result = (from ii in entities.Applications
                              where ii.ApplicationId == id
                              select ii).SingleOrDefault();

                if (result == null)
                {
                    throw new ArgumentException("A record with the specified application id does not exist.", nameof(applicationId));
                }

                if (certificateTypeId.Equals(nameof(Ua.ObjectTypeIds.HttpsCertificateType), StringComparison.OrdinalIgnoreCase))
                {
                    certificate = ByteString.From(result.HttpsCertificate);
                }
                else
                {
                    certificate = ByteString.From(result.Certificate);
                }
            }

            return !certificate.IsNull;
        }

        public override bool SetApplicationTrustLists(NodeId applicationId, string certificateTypeId, string trustListId)
        {
            Guid id = GetNodeIdGuid(applicationId);
            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var result = (from x in entities.Applications where x.ApplicationId == id select x).SingleOrDefault();

                if (result == null || certificateTypeId == null)
                {
                    return false;
                }

                var result2 = (from x in entities.CertificateStores where x.CertificateType == certificateTypeId && x.ApplicationId == result.ID select x).SingleOrDefault();

                if (result2 != null)
                {
                    result2.Path = trustListId;
                }
                else
                {
                    entities.CertificateStores.Add(
                        new CertificateStore() {
                            Application = result,
                            ApplicationId = result.ID,
                            CertificateType = certificateTypeId,
                            Path = trustListId
                        });
                }
                entities.SaveChanges();
            }

            return true;
        }

        public override bool GetApplicationTrustLists(NodeId applicationId, string certificateTypeId, out string trustListId)
        {
            Guid id = GetNodeIdGuid(applicationId);
            trustListId = null;
           
            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var result = (from x in entities.Applications where x.ApplicationId == id select x).SingleOrDefault();

                if (result == null || certificateTypeId == null)
                {
                    return false;
                }
                var result2 = (from x in entities.CertificateStores where x.CertificateType == certificateTypeId && x.ApplicationId == result.ID select x).SingleOrDefault();


                if (result2 == null)
                {
                    return false;
                }
                trustListId = result2.Path;

                return true;
            }

        }


        #endregion

        #region ICertificateRequest
        public NodeId StartSigningRequest(
                NodeId applicationId,
                string certificateGroupId,
                string certificateTypeId,
                ByteString certificateRequest,
                string authorityId)
        {
            Guid id = GetNodeIdGuid(applicationId);

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var application = (from x in entities.Applications where x.ApplicationId == id select x).SingleOrDefault();

                if (application == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }

                var request = (from x in application.CertificateRequests where x.AuthorityId == authorityId select x).SingleOrDefault();

                bool isNew = false;

                if (request == null)
                {
                    request = new CertificateRequest() { RequestId = Guid.NewGuid(), AuthorityId = authorityId };
                    isNew = true;
                }

                request.State = (int)CertificateRequestState.New;
                request.CertificateGroupId = certificateGroupId;
                request.CertificateTypeId = certificateTypeId;
                request.SubjectName = null;
                request.DomainNames = null;
                request.PrivateKeyFormat = null;
                request.PrivateKeyPassword = null;
                request.CertificateSigningRequest = certificateRequest.ToArray();

                if (isNew)
                {
                    application.CertificateRequests.Add(request);
                }

                entities.SaveChanges();

                return new NodeId(request.RequestId, NamespaceIndex);
            }
        }

        public NodeId StartNewKeyPairRequest(
            NodeId applicationId,
            string certificateGroupId,
            string certificateTypeId,
            string subjectName,
            ArrayOf<string> domainNames,
            string privateKeyFormat,
            ReadOnlySpan<char> privateKeyPassword,
            string authorityId)
        {
            Guid id = GetNodeIdGuid(applicationId);

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var application = (from x in entities.Applications where x.ApplicationId == id select x).SingleOrDefault();

                if (application == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }

                var request = (from x in application.CertificateRequests where x.AuthorityId == authorityId select x).SingleOrDefault();

                bool isNew = false;

                if (request == null)
                {
                    request = new CertificateRequest() { RequestId = Guid.NewGuid(), AuthorityId = authorityId };
                    isNew = true;
                }

                request.State = (int)CertificateRequestState.New;
                request.CertificateGroupId = certificateGroupId;
                request.CertificateTypeId = certificateTypeId;
                request.SubjectName = subjectName;
                request.DomainNames = JsonSerializer.Serialize(domainNames.IsNull ? null : domainNames.ToArray());
                request.PrivateKeyFormat = privateKeyFormat;
                request.PrivateKeyPassword = privateKeyPassword.ToString();
                request.CertificateSigningRequest = null;

                if (isNew)
                {
                    application.CertificateRequests.Add(request);
                }

                entities.SaveChanges();

                return new NodeId(request.RequestId, NamespaceIndex);
            }
        }

        public void ApproveRequest(
            NodeId requestId,
            bool isRejected
            )
        {
            Guid id = GetNodeIdGuid(requestId);
            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var request = (from x in entities.CertificateRequests where x.RequestId == id select x).SingleOrDefault();

                if (request == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }

                if (isRejected)
                {
                    request.State = (int)CertificateRequestState.Rejected;
                    // erase information which is ot required anymore
                    request.CertificateSigningRequest = null;
                    request.PrivateKeyPassword = null;
                }
                else
                {
                    request.State = (int)CertificateRequestState.Approved;
                }

                entities.SaveChanges();
            }
        }

        public void AcceptRequest(
            NodeId requestId,
            ByteString certificate)
        {
            Guid id = GetNodeIdGuid(requestId);
            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var request = (from x in entities.CertificateRequests where x.RequestId == id select x).SingleOrDefault();

                if (request == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }

                request.State = (int)CertificateRequestState.Accepted;

                // erase information which is ot required anymore
                request.CertificateSigningRequest = null;
                request.PrivateKeyPassword = null;

                entities.SaveChanges();
            }
        }


        public CertificateRequestState FinishRequest(
            NodeId applicationId,
            NodeId requestId,
            out string certificateGroupId,
            out string certificateTypeId,
            out ByteString signedCertificate,
            out ByteString privateKey)
        {
            certificateGroupId = null;
            certificateTypeId = null;
            signedCertificate = default;
            privateKey = default;
            Guid reqId = GetNodeIdGuid(requestId);
            Guid appId = GetNodeIdGuid(applicationId);

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var request = (from x in entities.CertificateRequests where x.RequestId == reqId select x).SingleOrDefault();

                if (request == null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }

                switch (request.State)
                {
                    case (int)CertificateRequestState.New:
                        return CertificateRequestState.New;
                    case (int)CertificateRequestState.Rejected:
                        return CertificateRequestState.Rejected;
                    case (int)CertificateRequestState.Accepted:
                        return CertificateRequestState.Accepted;
                    case (int)CertificateRequestState.Approved:
                        break;
                    default:
                        throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }

                certificateGroupId = request.CertificateGroupId;
                certificateTypeId = request.CertificateTypeId;

                entities.SaveChanges();
                return CertificateRequestState.Approved;
            }
        }

        public CertificateRequestState ReadRequest(
            NodeId applicationId,
            NodeId requestId,
            out string certificateGroupId,
            out string certificateTypeId,
            out ByteString certificateRequest,
            out string subjectName,
            out string[] domainNames,
            out string privateKeyFormat,
            out ReadOnlySpan<char> privateKeyPassword)
        {
            certificateGroupId = null;
            certificateTypeId = null;
            certificateRequest = default;
            subjectName = null;
            domainNames = null;
            privateKeyFormat = null;
            privateKeyPassword = null;
            Guid reqId = GetNodeIdGuid(requestId);
            Guid appId = GetNodeIdGuid(applicationId);

            using (gdsdbEntities entities = new gdsdbEntities())
            {
                var request = (from x in entities.CertificateRequests where x.RequestId == reqId select x).SingleOrDefault();

                if (request == null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }

                switch (request.State)
                {
                    case (int)CertificateRequestState.New:
                        return CertificateRequestState.New;
                    case (int)CertificateRequestState.Rejected:
                        return CertificateRequestState.Rejected;
                    case (int)CertificateRequestState.Accepted:
                        return CertificateRequestState.Accepted;
                    case (int)CertificateRequestState.Approved:
                        break;
                    default:
                        throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }

                certificateGroupId = request.CertificateGroupId;
                certificateTypeId = request.CertificateTypeId;
                certificateRequest = ByteString.From(request.CertificateSigningRequest);
                subjectName = request.SubjectName;
                domainNames = request.DomainNames != null ? JsonSerializer.Deserialize<string[]>(request.DomainNames) : null;
                privateKeyFormat = request.PrivateKeyFormat;
                privateKeyPassword = request.PrivateKeyPassword.AsSpan();

                entities.SaveChanges();
                return CertificateRequestState.Approved;
            }
        }
        #endregion

        #region Private Fields
        private DateTime m_lastCounterResetTime = DateTime.MinValue;
        #endregion
    }
}
