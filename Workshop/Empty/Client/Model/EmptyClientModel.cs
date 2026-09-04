/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using Opc.Ua;
using Opc.Ua.Samples.Client;

namespace Quickstarts.EmptyClient.Model
{
    /// <summary>
    /// The client model of the Empty client: the half of the sample which talks OPC UA.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This sample is the template a new client is copied from, so its model is the
    /// template of a model. It has no members of its own; what it inherits is the contract
    /// every model follows: the window hands it the session with
    /// <see cref="SampleClientModel.AttachAsync"/>, it sets itself up in
    /// <see cref="SampleClientModel.OnAttachedAsync"/>, releases everything in
    /// <see cref="SampleClientModel.OnDetachingAsync"/>, and reports what it learns through
    /// events, which arrive on the thread the model was created on.
    /// </para>
    /// <para>
    /// A model never references Windows Forms. That is what lets the model tier of the
    /// tests drive it without a window - and what keeps the OPC UA logic of a sample
    /// readable on its own.
    /// </para>
    /// </remarks>
    public sealed class EmptyClientModel : SampleClientModel
    {
        /// <summary>
        /// Creates the model.
        /// </summary>
        /// <param name="telemetry">The telemetry context of the client.</param>
        public EmptyClientModel(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }
    }
}
