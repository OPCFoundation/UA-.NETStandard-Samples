/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using Opc.Ua;

namespace Quickstarts.AliasNames.Client
{
    /// <summary>
    /// The alias name categories this client offers, and how it addresses each of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two kinds of category in the list differ in how much the client has to know in
    /// advance, which is the trade-off Part 17 §9 is about:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///   <b>Standard.</b> <c>TagVariables</c> is <c>i=23479</c> in every server that has one,
    ///   fixed by the specification, so a client addresses it with no prior knowledge at all.
    ///   That is what makes a generic tag browser possible.
    ///   </item>
    ///   <item>
    ///   <b>Application defined.</b> A category a server creates itself lives in a namespace
    ///   of that server's own, so a client has to be told the namespace uri and the identifier
    ///   - the constants below - or discover the category by browsing the standard
    ///   <c>Aliases</c> object it is organized under.
    ///   </item>
    /// </list>
    /// </remarks>
    public static class AliasCategories
    {
        /// <summary>
        /// The namespace the sample server puts its own alias categories in.
        /// </summary>
        /// <remarks>
        /// It matches <c>AliasNamesServer.CategoryNamespaceUri</c>. It is repeated here rather
        /// than shared, because a client does not reference the assembly of the server it
        /// talks to - knowing a uri and an identifier is exactly the prior knowledge an
        /// application defined category costs.
        /// </remarks>
        public const string NamespaceUri = "http://opcfoundation.org/Quickstarts/AliasNames/Categories";

        /// <summary>
        /// The root category of the plant.
        /// </summary>
        public const string PlantTags = "PlantTags";

        /// <summary>
        /// The sub-category holding the tags of the reactor.
        /// </summary>
        public const string Reactor = "Reactor";

        /// <summary>
        /// The sub-category holding the tags of the boiler.
        /// </summary>
        public const string Boiler = "Boiler";

        /// <summary>
        /// The NodeId of one of the server's own categories, in the session's namespace table.
        /// </summary>
        /// <remarks>
        /// The identifier is the category name, so the only thing which has to be looked up is
        /// the index the server gave the category namespace - which is why the id is built
        /// from an <see cref="ExpandedNodeId"/> carrying the uri instead of a hard coded index.
        /// </remarks>
        public static NodeId NodeIdOf(string categoryName, NamespaceTable namespaceUris)
        {
            return ExpandedNodeId.ToNodeId(
                new ExpandedNodeId(categoryName, 0, NamespaceUri, 0),
                namespaceUris);
        }
    }
}
