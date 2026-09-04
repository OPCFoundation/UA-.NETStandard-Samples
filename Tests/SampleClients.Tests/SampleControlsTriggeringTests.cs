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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Configuration;
using Opc.Ua.Sample.Controls;
using Opc.Ua.Samples.Client;

namespace Opc.Ua.Samples.Tests
{
    // the V2 subscription engine reuses names the classic engine has in Opc.Ua.Client, so
    // the V2 types are pinned explicitly.
    using IMonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem;
    using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

    /// <summary>
    /// Tier 2: the triggering the subscription dialogs of the sample controls offer under
    /// <c>Set Triggering...</c>, driven against the Reference server.
    /// </summary>
    /// <remarks>
    /// Both halves of the triggering API are used by the dialog and both are exercised
    /// here: the imperative <c>SetTriggeringAsync</c> for items which already exist on the
    /// server, which is the only one that reports a status per link, and the declarative
    /// <c>TriggeredByNames</c> for items the wizard staged but the engine has not created
    /// yet. The dialog itself is modal and is left to the user; what is driven is the work
    /// its OK button leads to.
    /// </remarks>
    [TestFixture]
    [Category("ClientSmoke")]
    [Category("RequiresDesktop")]
    [NonParallelizable]
    public class SampleControlsTriggeringTests
    {
        private const int kTimeout = 90_000;
        private const string kSample = "Reference";

        [Test]
        [CancelAfter(kTimeout)]
        public async Task TriggeringLinksAndUnlinksItemsOfASubscription(CancellationToken ct)
        {
            SampleDefinition sample = SampleCatalog.All.Single(entry => entry.Name == kSample);
            SampleServerUnderTest server = SampleServerFactories.All.Single(entry => entry.Sample.Name == kSample);

            await using SampleServerHost host = await SampleServerHost
                .StartAsync(kSample, server.Sample.ServerConfig, server.ConfigureServices, ct)
                .ConfigureAwait(false);

            await WinFormsHarness.RunAsync(
                async _ => await DriveTriggeringAsync(sample, host.EndpointUrl, ct).ConfigureAwait(true),
                TimeSpan.FromMilliseconds(kTimeout) - TimeSpan.FromSeconds(15))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Runs on the STA thread of the harness, with a message loop pumping.
        /// </summary>
        private static async Task DriveTriggeringAsync(SampleDefinition sample, string endpointUrl, CancellationToken ct)
        {
            using var pki = new TemporaryPki("client-sample-triggering");

            ApplicationConfiguration configuration = await SampleConfigurationLoader
                .LoadAsync(sample.ClientConfig, pki, ct)
                .ConfigureAwait(true);

            await using var application = new ApplicationInstance(configuration, NullTelemetry.Instance);

            Assert.That(
                await application.CheckApplicationInstanceCertificatesAsync(true, null, ct).ConfigureAwait(true),
                Is.True,
                "The client certificate of the sample could not be created.");

            EndpointDescription endpointDescription = await CoreClientUtils
                .SelectEndpointAsync(configuration, endpointUrl, false, 15_000, NullTelemetry.Instance, ct)
                .ConfigureAwait(true);

            var endpoint = new ConfiguredEndpoint(null, endpointDescription, EndpointConfiguration.Create(configuration));

            ISession session = await new ManagedSessionFactory(NullTelemetry.Instance)
                .CreateAsync(configuration, endpoint, false, false, "TriggeringTest", 60_000, null, (string[])null, ct)
                .ConfigureAwait(true);

            SubscriptionHandle subscription = null;

            try
            {
                subscription = new SubscriptionHandle(session, "Triggering Subscription", ClientUtils.DefaultSubscriptionOptions);

                Assert.That(subscription.Create(), Is.Not.Null, "The subscription was not registered with the V2 engine.");

                // the triggering item reports at its own rate, the triggered ones only
                // sample - which is what triggering exists to work around.
                MonitoredItemHandle trigger = subscription.AddItem(
                    "Trigger",
                    NodeClass.Variable,
                    Reporting(VariableIds.Server_ServerStatus_CurrentTime));

                MonitoredItemHandle sampled = subscription.AddItem(
                    "Sampled",
                    NodeClass.Variable,
                    Sampling(VariableIds.Server_ServerStatus_State));

                MonitoredItemHandle second = subscription.AddItem(
                    "SecondSampled",
                    NodeClass.Variable,
                    Sampling(VariableIds.Server_ServerStatus_CurrentTime));

                await subscription.WaitForPendingChangesAsync(TimeSpan.FromSeconds(20), ct).ConfigureAwait(true);

                Assert.Multiple(() => {
                    Assert.That(trigger.Created, Is.True, $"The trigger item was refused: {trigger.Item?.Error}");
                    Assert.That(sampled.Created, Is.True, $"The sampled item was refused: {sampled.Item?.Error}");
                    Assert.That(second.Created, Is.True, $"The second sampled item was refused: {second.Item?.Error}");
                    Assert.That(SetTriggeringDlg.IsTriggeredBy(sampled, trigger), Is.False, "Items are linked before anything asked for it.");
                });

                // the imperative half, which is what the menu item runs for items which
                // already exist on the server.
                SetTriggeringResult result = await subscription.Subscription
                    .SetTriggeringAsync(trigger.Item, [sampled.Item, second.Item], null, ct)
                    .ConfigureAwait(true);

                Assert.Multiple(() => {
                    Assert.That(StatusCode.IsGood(result.ServiceResult), Is.True, $"SetTriggering failed: {result.ServiceResult}");
                    Assert.That(
                        result.AddResults.Select(entry => entry.Item2),
                        Has.All.Matches<StatusCode>(StatusCode.IsGood),
                        "The server refused a triggering link: " +
                        string.Join(", ", result.AddResults.Select(entry => $"{entry.Item1?.Name}={entry.Item2}")));
                    Assert.That(result.AddResults, Has.Count.EqualTo(2), "The per link results do not pair up with the request.");
                });

                // N:M, seen from both ends - which is what the grid column and the check
                // list of the dialog read.
                Assert.Multiple(() => {
                    Assert.That(SetTriggeringDlg.IsTriggeredBy(sampled, trigger), Is.True, "The link is not visible on the triggered item.");
                    Assert.That(SetTriggeringDlg.IsTriggeredBy(second, trigger), Is.True, "The second link is not visible on the triggered item.");
                    Assert.That(
                        trigger.Item.TriggeredItems.Select(item => item.Name),
                        Is.EquivalentTo(new[] { sampled.Name, second.Name }),
                        "The triggering item does not report the items it triggers.");
                    Assert.That(
                        SetTriggeringDlg.GetTriggeredByDisplayText(subscription, sampled),
                        Is.EqualTo(trigger.DisplayName),
                        "The 'Triggered By' column of the item grid shows the wrong name.");
                    Assert.That(
                        SetTriggeringDlg.GetTriggeredByDisplayText(subscription, trigger),
                        Is.Empty,
                        "The triggering item is shown as being triggered by something.");
                });

                // and unlinking one of them leaves the other alone
                SetTriggeringResult removed = await subscription.Subscription
                    .SetTriggeringAsync(trigger.Item, null, [sampled.Item], ct)
                    .ConfigureAwait(true);

                Assert.Multiple(() => {
                    Assert.That(StatusCode.IsGood(removed.ServiceResult), Is.True, $"SetTriggering failed on remove: {removed.ServiceResult}");
                    Assert.That(
                        removed.RemoveResults.Select(entry => entry.Item2),
                        Has.All.Matches<StatusCode>(StatusCode.IsGood),
                        "The server refused to remove a triggering link.");
                    Assert.That(SetTriggeringDlg.IsTriggeredBy(sampled, trigger), Is.False, "The link was not removed.");
                    Assert.That(SetTriggeringDlg.IsTriggeredBy(second, trigger), Is.True, "Removing one link removed the other one too.");
                });

                // the declarative half: an item the wizard staged carries its intent in the
                // options it will be created with, and the engine issues the SetTriggering
                // itself once the item exists.
                MonitoredItemHandle staged = subscription.StageItem(
                    "Staged",
                    NodeClass.Variable,
                    Sampling(VariableIds.Server_ServerStatus_State) with {
                        TriggeredByNames = new List<string> { trigger.Name },
                    });

                Assert.That(
                    SetTriggeringDlg.IsTriggeredBy(staged, trigger),
                    Is.True,
                    "The intent of a staged item is not visible before the engine creates it.");

                subscription.ApplyChanges();

                await subscription.WaitForPendingChangesAsync(TimeSpan.FromSeconds(20), ct).ConfigureAwait(true);

                Assert.That(staged.Created, Is.True, $"The staged item was refused: {staged.Item?.Error}");

                // the engine replays the desired set once both ends exist, so the link is
                // there without anyone having called SetTriggering for it.
                await WaitUntilAsync(
                    () => SetTriggeringDlg.IsTriggeredBy(staged, trigger),
                    "the declared triggering link never reached the server",
                    ct).ConfigureAwait(true);

                Assert.That(
                    trigger.Item.TriggeredItems.Select(item => item.Name),
                    Has.Member(staged.Name),
                    "The declaratively linked item is not reported by the triggering item.");
            }
            finally
            {
                // the subscription goes first: closing a session which still carries one
                // waits for the publish pipeline to drain. The close itself is bounded and
                // not tied to the token of the test, which may already have fired.
                if (subscription != null)
                {
                    await subscription.DeleteAsync().ConfigureAwait(true);
                }

                await ClientUtils.CloseAndDisposeAsync(session, CancellationToken.None).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// An item which reports on its own, the role of a triggering item.
        /// </summary>
        private static MonitoredItemOptions Reporting(NodeId nodeId)
        {
            return new MonitoredItemOptions {
                StartNodeId = nodeId,
                AttributeId = Attributes.Value,
                MonitoringMode = MonitoringMode.Reporting,
                SamplingInterval = TimeSpan.FromMilliseconds(500),
                QueueSize = 1,
            };
        }

        /// <summary>
        /// An item which samples without reporting, the role of a triggered item.
        /// </summary>
        private static MonitoredItemOptions Sampling(NodeId nodeId)
        {
            return Reporting(nodeId) with { MonitoringMode = MonitoringMode.Sampling };
        }

        /// <summary>
        /// Waits for a condition the engine reaches on its own worker.
        /// </summary>
        private static async Task WaitUntilAsync(Func<bool> condition, string message, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);

            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(100, ct).ConfigureAwait(true);
            }

            Assert.Fail(message);
        }
    }
}
