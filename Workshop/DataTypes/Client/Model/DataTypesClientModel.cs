/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;
using Opc.Ua.Samples.Client;

namespace Quickstarts.DataTypes.Model
{
    /// <summary>
    /// The client model of the DataTypes client: loads the structured data types the
    /// server defines, so that their values arrive decoded into their fields.
    /// </summary>
    /// <remarks>
    /// This sample exists to show a client making sense of structures it has no compiled
    /// knowledge of. The complex type system reads the data type dictionaries and the
    /// type definitions of the server when the session is attached and builds the types
    /// at run time; a value read after that is an object with named fields instead of the
    /// raw body of an extension object. There are no subscriptions: the window browses
    /// through the shared browse control and reads through <see cref="ReadValueAsync"/>.
    /// </remarks>
    public sealed class DataTypesClientModel : SampleClientModel
    {
        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public DataTypesClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        /// <summary>
        /// True once the structured types of the server were loaded on the attached
        /// session.
        /// </summary>
        public bool TypeSystemLoaded { get; private set; }

        /// <summary>
        /// Reads the value of a variable. A structure arrives decoded into its fields
        /// once the type system was loaded.
        /// </summary>
        /// <param name="nodeId">The variable.</param>
        /// <param name="ct">The cancellation token.</param>
        public Task<DataValue> ReadValueAsync(NodeId nodeId, CancellationToken ct = default)
        {
            return RequireSession().ReadValueAsync(nodeId, ct);
        }

        /// <inheritdoc/>
        protected override async Task OnAttachedAsync(CancellationToken ct)
        {
            ISession session = RequireSession();

            // the type system is loaded for the whole server, including the types no
            // node references yet, so that a value of any structure the server defines
            // can be decoded.
            var typeSystem = ComplexTypeSystemClientExtensions.Create(session, Telemetry);
            await typeSystem.LoadAsync(true, true, ct).ConfigureAwait(false);

            TypeSystemLoaded = true;
        }

        /// <inheritdoc/>
        protected override Task OnDetachingAsync()
        {
            // the types were built on the session, so they go with it.
            TypeSystemLoaded = false;

            return Task.CompletedTask;
        }
    }
}
