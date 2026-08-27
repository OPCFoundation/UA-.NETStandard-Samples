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
using Quickstarts.MethodsServer;

namespace Opc.Ua.Samples.Tests
{
    /// <summary>
    /// What the methods sample does: a method which validates its arguments and starts a
    /// process which ramps a value towards a target.
    /// </summary>
    /// <remarks>
    /// The node manager builds its address space by hand, so the browse names and the
    /// shape of the arguments are part of what the sample promises. The interesting
    /// behaviour is in OnStart: it rejects incomplete and mistyped arguments, echoes the
    /// accepted ones back as revised values, and replaces a process which is still running.
    /// </remarks>
    [TestFixture]
    [Category("NodeManager")]
    [NonParallelizable]
    public class MethodsNodeManagerTests : NodeManagerFixtureBase
    {
        /// <inheritdoc/>
        protected override string SampleName => "Methods";

        /// <summary>
        /// The namespace the sample serves its nodes in.
        /// </summary>
        /// <remarks>
        /// Spelled out rather than imported, because Opc.Ua defines a Namespaces class too
        /// and the tests live in a namespace below Opc.Ua.
        /// </remarks>
        private const string MethodsNamespace = Quickstarts.MethodsServer.Namespaces.Methods;

        private QualifiedName Process => Name(MethodsNamespace, "My Process");
        private QualifiedName State => Name(MethodsNamespace, "State");
        private QualifiedName Start => Name(MethodsNamespace, "Start");

        /// <summary>
        /// The process object, its state property and the start method are where the sample
        /// says they are, and the method declares two unsigned integers in and two out.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task StartMethodResolvesWithArgumentMetadata(CancellationToken ct)
        {
            NodeId processId = await ResolveAsync(ct, Process).ConfigureAwait(false);
            NodeId stateId = await ResolveAsync(ct, Process, State).ConfigureAwait(false);
            NodeId startId = await ResolveAsync(ct, Process, Start).ConfigureAwait(false);

            ushort ns = NamespaceIndex(MethodsNamespace);

            Assert.Multiple(() => {
                Assert.That(processId, Is.EqualTo(new NodeId(1u, ns)), "The process object moved.");
                Assert.That(stateId, Is.EqualTo(new NodeId(2u, ns)), "The state property moved.");
                Assert.That(startId, Is.EqualTo(new NodeId(3u, ns)), "The start method moved.");
            });

            Argument[] inputs = await ReadArgumentsAsync(startId, BrowseNames.InputArguments, ct)
                .ConfigureAwait(false);
            Argument[] outputs = await ReadArgumentsAsync(startId, BrowseNames.OutputArguments, ct)
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    inputs.Select(argument => argument.Name),
                    Is.EqualTo(new[] { "Initial State", "Final State" }),
                    "The input arguments of Start changed.");

                Assert.That(
                    outputs.Select(argument => argument.Name),
                    Is.EqualTo(new[] { "Revised Initial State", "Revised Final State" }),
                    "The output arguments of Start changed.");

                Assert.That(
                    inputs.Concat(outputs).Select(argument => argument.DataType),
                    Is.All.EqualTo(DataTypeIds.UInt32),
                    "The arguments of Start are all unsigned integers.");

                Assert.That(
                    inputs.Concat(outputs).Select(argument => argument.ValueRank),
                    Is.All.EqualTo(ValueRanks.Scalar),
                    "The arguments of Start are all scalars.");
            });
        }

        /// <summary>
        /// The sample refuses to start a process without both arguments.
        /// </summary>
        /// <remarks>
        /// OnStart answers BadArgumentsMissing itself, but the server checks the argument
        /// count against the declaration before it ever reaches the handler. Which of the
        /// two answers arrives is what this records: the point of the test is that the call
        /// is refused and that a migrated node manager refuses it the same way.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task CallWithMissingArgumentsFails(CancellationToken ct)
        {
            NodeId processId = await ResolveAsync(ct, Process).ConfigureAwait(false);
            NodeId startId = await ResolveAsync(ct, Process, Start).ConfigureAwait(false);

            CallMethodResult noArguments = await SessionOps
                .CallAsync(Session, processId, startId, ct)
                .ConfigureAwait(false);

            CallMethodResult oneArgument = await SessionOps
                .CallAsync(Session, processId, startId, ct, Variant.From((uint)1))
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync($"Start() -> {noArguments.StatusCode}, Start(1) -> {oneArgument.StatusCode}")
                .ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(
                    noArguments.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadArgumentsMissing),
                    "Calling Start without arguments has to be refused as missing arguments.");

                Assert.That(
                    oneArgument.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadArgumentsMissing),
                    "Calling Start with one argument has to be refused as missing arguments.");
            });
        }

        /// <summary>
        /// The sample refuses arguments of the wrong type.
        /// </summary>
        /// <remarks>
        /// Both arguments are declared as unsigned integers, and a signed one is not
        /// convertible to them, so the call is rejected. The server reports the offending
        /// argument in InputArgumentResults, and the overall status code says that one of
        /// the arguments was the problem.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task CallWithWrongArgumentTypeFails(CancellationToken ct)
        {
            NodeId processId = await ResolveAsync(ct, Process).ConfigureAwait(false);
            NodeId startId = await ResolveAsync(ct, Process, Start).ConfigureAwait(false);

            CallMethodResult result = await SessionOps
                .CallAsync(
                    Session,
                    processId,
                    startId,
                    ct,
                    Variant.From("not a number"),
                    Variant.From("not a number either"))
                .ConfigureAwait(false);

            await TestContext.Out
                .WriteLineAsync(
                    $"Start(\"...\", \"...\") -> {result.StatusCode}, " +
                    $"per argument: {string.Join(", ", result.InputArgumentResults.ToArray().Select(code => code.ToString()))}")
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsBad(result.StatusCode),
                Is.True,
                "Calling Start with strings where unsigned integers are declared has to fail.");

            Assert.That(
                result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument),
                "A call whose arguments do not match the declaration is reported as an invalid argument.");

            Assert.That(
                result.InputArgumentResults.ToArray(),
                Is.All.EqualTo((StatusCode)StatusCodes.BadTypeMismatch),
                "Both arguments have the wrong type, so both are reported as a type mismatch.");
        }

        /// <summary>
        /// Starting the process ramps the state up to the final value and then stops.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task StartRampsStateUpToFinalValue(CancellationToken ct)
        {
            NodeId processId = await ResolveAsync(ct, Process).ConfigureAwait(false);
            NodeId stateId = await ResolveAsync(ct, Process, State).ConfigureAwait(false);
            NodeId startId = await ResolveAsync(ct, Process, Start).ConfigureAwait(false);

            const uint initial = 3;
            const uint final = 6;

            CallMethodResult result = await SessionOps
                .CallAsync(Session, processId, startId, ct, Variant.From(initial), Variant.From(final))
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(result.StatusCode),
                Is.True,
                $"Starting the process failed: {result.StatusCode}");

            Assert.That(
                result.OutputArguments.ToArray().Select(ToUInt32),
                Is.EqualTo(new[] { initial, final }),
                "The revised values have to echo the accepted arguments.");

            // the process moves one step per second, so the state passes through every
            // value between the initial and the final one
            uint reached = await Poll.UntilAsync(
                token => ReadStateAsync(stateId, token),
                value => value == final,
                $"the process to ramp from {initial} to {final}",
                timeout: TimeSpan.FromSeconds(20),
                ct: ct).ConfigureAwait(false);

            Assert.That(reached, Is.EqualTo(final));

            // once the target is reached the process stops, so the value stays put
            await Task.Delay(TimeSpan.FromSeconds(2.5), ct).ConfigureAwait(false);

            uint afterwards = await ReadStateAsync(stateId, ct).ConfigureAwait(false);

            Assert.That(
                afterwards,
                Is.EqualTo(final),
                "The process has to stop once it reached its final state.");
        }

        /// <summary>
        /// Starting the process again while it runs replaces it, and it also ramps down.
        /// </summary>
        /// <remarks>
        /// The two halves of this belong together: the second call is made while the first
        /// process is still moving, which is the branch in OnStart that disposes the
        /// running timer before it starts a new one.
        /// </remarks>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task StartWhileRunningReplacesTheProcessAndRampsDown(CancellationToken ct)
        {
            NodeId processId = await ResolveAsync(ct, Process).ConfigureAwait(false);
            NodeId stateId = await ResolveAsync(ct, Process, State).ConfigureAwait(false);
            NodeId startId = await ResolveAsync(ct, Process, Start).ConfigureAwait(false);

            // a long ramp, so that the process is certainly still running below
            CallMethodResult running = await SessionOps
                .CallAsync(Session, processId, startId, ct, Variant.From(0u), Variant.From(60u))
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(running.StatusCode), Is.True, $"{running.StatusCode}");

            // replace it with one which ramps down to a target it can reach quickly
            CallMethodResult replacing = await SessionOps
                .CallAsync(Session, processId, startId, ct, Variant.From(9u), Variant.From(6u))
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(replacing.StatusCode), Is.True, $"{replacing.StatusCode}");

            Assert.That(
                replacing.OutputArguments.ToArray().Select(ToUInt32),
                Is.EqualTo(new[] { 9u, 6u }),
                "The revised values have to echo the arguments of the second call.");

            uint reached = await Poll.UntilAsync(
                token => ReadStateAsync(stateId, token),
                value => value == 6u,
                "the replacing process to ramp down from 9 to 6",
                timeout: TimeSpan.FromSeconds(20),
                ct: ct).ConfigureAwait(false);

            Assert.That(reached, Is.EqualTo(6u));

            // the first process would have kept counting towards 60, so a value which stays
            // at 6 is the proof that starting the second one replaced it
            await Task.Delay(TimeSpan.FromSeconds(2.5), ct).ConfigureAwait(false);

            uint afterwards = await ReadStateAsync(stateId, ct).ConfigureAwait(false);

            Assert.That(
                afterwards,
                Is.EqualTo(6u),
                "The process which was replaced must not keep updating the state.");
        }

        /// <summary>
        /// A client which subscribes sees every step of the ramp, not just the result.
        /// </summary>
        [Test]
        [CancelAfter(kTimeout)]
        public async Task SubscribersSeeEveryStepOfTheRamp(CancellationToken ct)
        {
            NodeId processId = await ResolveAsync(ct, Process).ConfigureAwait(false);
            NodeId stateId = await ResolveAsync(ct, Process, State).ConfigureAwait(false);
            NodeId startId = await ResolveAsync(ct, Process, Start).ConfigureAwait(false);

            await using DataChangeCapture capture = await DataChangeCapture
                .CreateAsync(Session, stateId, ct)
                .ConfigureAwait(false);

            CallMethodResult result = await SessionOps
                .CallAsync(Session, processId, startId, ct, Variant.From(20u), Variant.From(24u))
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True, $"{result.StatusCode}");

            IReadOnlyList<DataValue> values = await capture
                .CollectDistinctUntilAsync(
                    value => ToUInt32(value.WrappedValue) == 24u,
                    TimeSpan.FromSeconds(30),
                    ct)
                .ConfigureAwait(false);

            uint[] reported = values
                .Select(value => ToUInt32(value.WrappedValue))
                .ToArray();

            await ReportAsync("State reported", reported.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                .ConfigureAwait(false);

            // the subscription reports the value the process happened to be sitting on
            // before the call, so the ramp is the tail which starts at the initial state
            uint[] ramp = reported.SkipWhile(value => value != 20u).ToArray();

            Assert.That(
                ramp,
                Is.EqualTo(new[] { 20u, 21u, 22u, 23u, 24u }),
                "The process reports every step it takes towards its final state.");
        }

        private async Task<uint> ReadStateAsync(NodeId stateId, CancellationToken ct)
        {
            DataValue value = await SessionOps
                .ReadValueAsync(Session, stateId, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(value.StatusCode),
                Is.True,
                $"Reading the process state failed: {value.StatusCode}");

            return ToUInt32(value.WrappedValue);
        }

        private async Task<Argument[]> ReadArgumentsAsync(
            NodeId methodId,
            string browseName,
            CancellationToken ct)
        {
            NodeId argumentsId = await ResolveFromAsync(methodId, ct, new QualifiedName(browseName))
                .ConfigureAwait(false);

            DataValue value = await SessionOps
                .ReadValueAsync(Session, argumentsId, ct)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(value.StatusCode),
                Is.True,
                $"Reading {browseName} failed: {value.StatusCode}");

            var arguments = new List<Argument>();

            foreach (ExtensionObject encoded in AsExtensionObjects(value.WrappedValue))
            {
                Assert.That(
                    encoded.TryGetValue(out Argument argument, Session.MessageContext),
                    Is.True,
                    $"An entry of {browseName} did not decode as an Argument.");

                arguments.Add(argument);
            }

            return arguments.ToArray();
        }

        /// <summary>
        /// The entries of a variant which carries an array of structures.
        /// </summary>
        private static IEnumerable<ExtensionObject> AsExtensionObjects(Variant value)
        {
            object boxed = value.AsBoxedObject();

            return boxed switch {
                ExtensionObject[] array => array,
                ArrayOf<ExtensionObject> arrayOf => arrayOf.ToArray(),
                _ => throw new InvalidOperationException(
                    $"The value is not an array of structures but a {boxed?.GetType().Name ?? "null"}."),
            };
        }

        private static uint ToUInt32(Variant value)
        {
            return Convert.ToUInt32(value.AsBoxedObject(), System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
