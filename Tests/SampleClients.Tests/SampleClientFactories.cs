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
using System.Linq;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// One sample client to drive: the catalog entry of the sample, the composition
    /// root its own <c>Program.Main</c> uses, and the main form that root creates.
    /// </summary>
    public sealed class SampleClientUnderTest
    {
        /// <summary>
        /// The catalog entry of the sample.
        /// </summary>
        public SampleDefinition Sample { get; init; }

        /// <summary>
        /// Registers what the main form of the client is built on - its client model -
        /// through the same <c>Add&lt;Sample&gt;Client()</c> its entry point calls.
        /// </summary>
        public Action<IServiceCollection> ConfigureServices { get; init; }

        /// <summary>
        /// The main form of the client, which the container creates.
        /// </summary>
        public Type MainFormType { get; init; }

        /// <summary>
        /// The designer name of a control the sample enables once it is connected, or null
        /// if the sample has none. It proves that the post connect logic of the sample ran
        /// and not just the shared connect control.
        /// </summary>
        public string EnabledAfterConnect { get; init; }

        /// <summary>
        /// Creates the main form of the client the way <c>SampleWinFormsHost</c> does:
        /// out of a container holding the configuration, the telemetry and whatever the
        /// sample registered on top of them.
        /// </summary>
        /// <remarks>
        /// The provider is disposed with the form, so the client model the container
        /// created is released even when a test never connected. The form disposes the
        /// model too; the second dispose finds nothing left to do.
        /// </remarks>
        /// <param name="configuration">The application configuration the test loaded.</param>
        /// <param name="telemetry">The telemetry context of the test.</param>
        public Form CreateMainForm(ApplicationConfiguration configuration, ITelemetryContext telemetry)
        {
            var services = new ServiceCollection();

            services.AddSingleton(configuration);
            services.AddSingleton(telemetry);

            ConfigureServices?.Invoke(services);

            ServiceProvider provider = services.BuildServiceProvider();

            try
            {
                var form = (Form)ActivatorUtilities.CreateInstance(provider, MainFormType);

                form.Disposed += (_, _) => provider.Dispose();

                return form;
            }
            catch
            {
                provider.Dispose();
                throw;
            }
        }

        /// <inheritdoc/>
        public override string ToString() => Sample.Name;
    }

    /// <summary>
    /// Maps the samples in the catalog to their client main forms.
    /// </summary>
    /// <remarks>
    /// Written out rather than resolved by reflection, for the same reason as the server
    /// factories: a renamed or removed sample client breaks the build of the tests. Each
    /// row names the composition root of the sample rather than calling its constructor,
    /// so a client which starts taking something else from the container is covered
    /// without a change here.
    /// </remarks>
    public static class SampleClientFactories
    {
        private static readonly Dictionary<string, SampleClientUnderTest> s_clients = Build();

        /// <summary>
        /// The sample clients tier 2 covers.
        /// </summary>
        public static IEnumerable<SampleClientUnderTest> All => s_clients.Values;

        /// <summary>
        /// The names of the samples which have a factory.
        /// </summary>
        public static IEnumerable<string> CoveredSamples => s_clients.Keys;

        private static Dictionary<string, SampleClientUnderTest> Build()
        {
            var clients = new List<SampleClientUnderTest> {
                Client("AlarmCondition", services => services.AddAlarmConditionClient(), typeof(Quickstarts.AlarmConditionClient.MainForm)),
                Client("AliasNames", services => services.AddAliasNamesClient(), typeof(Quickstarts.AliasNames.Client.MainForm), "FindBTN"),
                Client("Boiler", services => services.AddBoilerClient(), typeof(Quickstarts.Boiler.Client.MainForm), "BoilerCB"),
                Client("DataAccess", services => services.AddDataAccessClient(), typeof(Quickstarts.DataAccessClient.MainForm), "BrowseNodesTV"),
                Client("DataTypes", services => services.AddDataTypesClient(), typeof(Quickstarts.DataTypes.MainForm)),
                Client("Empty", services => services.AddEmptyClient(), typeof(Quickstarts.EmptyClient.MainForm)),
                Client("FileTransfer", services => services.AddFileTransferClient(), typeof(Quickstarts.FileTransferClient.MainForm), "UploadBTN"),
                Client("HistoricalAccess", services => services.AddHistoricalAccessClient(), typeof(Quickstarts.HistoricalAccess.Client.MainForm)),
                Client("HistoricalEvents", services => services.AddHistoricalEventsClient(), typeof(Quickstarts.HistoricalEvents.Client.MainForm)),
                Client("Methods", services => services.AddMethodsClient(), typeof(Quickstarts.MethodsClient.MainForm), "StartBTN"),
                Client("NodeManagement", services => services.AddNodeManagementClient(), typeof(Quickstarts.NodeManagement.Client.MainForm), "RefreshBTN"),
                Client("PerfTest", services => services.AddPerfTestClient(), typeof(Quickstarts.PerfTestClient.MainForm)),
                Client("RoleManagement", services => services.AddRoleManagementClient(), typeof(Quickstarts.RoleManagement.Client.MainForm), "RefreshBTN"),
                Client("RuntimeNodeSets", services => services.AddRuntimeNodeSetsClient(), typeof(Quickstarts.RuntimeNodeSets.Client.MainForm), "LoadBTN"),
                Client("SimpleEvents", services => services.AddSimpleEventsClient(), typeof(Quickstarts.SimpleEvents.Client.MainForm)),
                Client("StateMachines", services => services.AddStateMachinesClient(), typeof(Quickstarts.StateMachines.Client.MainForm), "PowerOnBTN"),
                Client("UserAuthentication", services => services.AddUserAuthenticationClient(), typeof(Quickstarts.UserAuthenticationClient.MainForm)),
                Client("Views", services => services.AddViewsClient(), typeof(Quickstarts.ViewsClient.MainForm)),

                // the reference client keeps its logic in the shared controls rather than in
                // a client model, so it registers nothing of its own
                Client("Reference", null, typeof(Quickstarts.ReferenceClient.MainForm)),
            };

            return clients.ToDictionary(client => client.Sample.Name, StringComparer.Ordinal);
        }

        private static SampleClientUnderTest Client(
            string name,
            Action<IServiceCollection> configureServices,
            Type mainFormType,
            string enabledAfterConnect = null)
        {
            SampleDefinition sample = SampleCatalog.All.Single(entry => entry.Name == name);

            return new SampleClientUnderTest {
                Sample = sample,
                ConfigureServices = configureServices,
                MainFormType = mainFormType,
                EnabledAfterConnect = enabledAfterConnect,
            };
        }
    }
}
