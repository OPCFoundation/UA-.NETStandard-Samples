/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

namespace Quickstarts.RuntimeNodeSets.Site
{
    /// <summary>
    /// The identifiers the overlay documents in <c>NodeSets/Site.*.NodeSet2.xml</c>
    /// give the nodes they add to the site model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generator emits <see cref="Objects"/>, <see cref="Variables"/> and
    /// <see cref="Methods"/> for everything <c>ModelDesign.xml</c> declares. It cannot
    /// emit anything for a document it never sees, so the identifiers a runtime overlay
    /// brings with it are spelled out here instead. A server which downloads its overlay
    /// from a device has no such class at all and resolves the nodes by browse path.
    /// </para>
    /// <para>
    /// The documents and this class must agree; nothing checks that at compile time,
    /// which is the price of a model that is only known at run time. The site node
    /// manager resolves every one of them in <c>Configure</c>, so a mismatch fails at
    /// start up rather than at the first request.
    /// </para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "The names mirror the generated Objects/Variables/Methods classes, where the underscore separates a node from its parent.")]
    public static class SiteOverlayIds
    {
        /// <summary>
        /// The station the overlay puts in the first slot, replacing the generated
        /// <c>Site/Station1</c> placeholder at <see cref="Objects.Site_Station1"/>.
        /// </summary>
        public const uint Station1 = 5001;

        /// <summary>
        /// The <c>Throughput</c> the overlay gives <see cref="Station1"/>.
        /// </summary>
        public const uint Station1_Throughput = 5002;

        /// <summary>
        /// The <c>Status</c> the overlay gives <see cref="Station1"/>.
        /// </summary>
        public const uint Station1_Status = 5003;

        /// <summary>
        /// A station the model does not declare, added below <c>Site</c>.
        /// </summary>
        public const uint Station3 = 5010;

        /// <summary>
        /// The <c>Throughput</c> of <see cref="Station3"/>.
        /// </summary>
        public const uint Station3_Throughput = 5011;

        /// <summary>
        /// The <c>Reset</c> Method of <see cref="Station3"/>, from the second document.
        /// Its <c>MethodDeclarationId</c> is <see cref="Methods.StationType_Reset"/>, so
        /// the importer materializes it as the generated <c>ResetMethodState</c>.
        /// </summary>
        public const uint Station3_Reset = 5020;

        /// <summary>
        /// The <c>InputArguments</c> Property of <see cref="Station3_Reset"/>. An
        /// imported Method has exactly the children its document declares, so a Method
        /// whose document leaves these out cannot be called with arguments.
        /// </summary>
        public const uint Station3_Reset_InputArguments = 5021;

        /// <summary>
        /// The <c>OutputArguments</c> Property of <see cref="Station3_Reset"/>.
        /// </summary>
        public const uint Station3_Reset_OutputArguments = 5022;

        /// <summary>
        /// A variable of <see cref="Station3"/> which no station type declares, from the
        /// second document.
        /// </summary>
        public const uint Station3_Temperature = 5030;
    }
}
