/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Collections.Generic;
using Opc.Ua;

namespace Quickstarts.AliasNames.Server
{
    /// <summary>
    /// One entry of the tag list: the name people use, and the node it stands for.
    /// </summary>
    /// <param name="Alias">
    /// The tag name, in the DCS naming scheme of the plant. This is what an operator, a
    /// historian configuration or an MES recipe writes down.
    /// </param>
    /// <param name="Target">
    /// The node the tag stands for, as an <see cref="ExpandedNodeId"/> carrying the namespace
    /// URI of the model rather than an index. Part 17 §7.2 puts
    /// <c>ExpandedNodeId[]</c> on the wire, and a URI survives a server restart which
    /// renumbers its namespace table, so this is the form to keep an alias inventory in.
    /// </param>
    /// <param name="Unit">The engineering unit of the signal, for the sample's own display.</param>
    public sealed record PlantTag(string Alias, ExpandedNodeId Target, string Unit);

    /// <summary>
    /// The tag list of the sample plant: the mapping Part 17 exists to serve.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two halves of this sample are deliberately unrelated. <c>ModelDesign.xml</c> lays
    /// the plant out by structure - Plant/Reactor/TemperatureMeasurement - because that is how
    /// the engineering documentation describes it. This table names the very same signals the
    /// way the control system does - TIC101_PV - because that is what people type.
    /// </para>
    /// <para>
    /// Neither naming scheme is wrong and neither can be dropped, which is the whole problem:
    /// a client which only knows <c>TIC101_PV</c> cannot browse for it, and rewriting the
    /// address space to use tag names would break every client which addresses the plant by
    /// structure. Part 17 resolves it by putting the tag names in a separate, searchable
    /// index that points at the structural nodes.
    /// </para>
    /// <para>
    /// A real server reads this list out of the DCS configuration, an asset database or a
    /// CSV export. It lives in one static table here so a reader can see the entire mapping
    /// at once, and so the sample client can show the expected answers next to the ones the
    /// server returned.
    /// </para>
    /// </remarks>
    public static class PlantTags
    {
        /// <summary>
        /// The tags of the reactor unit.
        /// </summary>
        /// <remarks>
        /// The suffixes are the usual ones of a control system: <c>_PV</c> is the process
        /// value a sensor measures, <c>_SP</c> the set point an operator asks for, and
        /// <c>_CMD</c> a command output. The sample client searches for them by pattern.
        /// </remarks>
        public static IReadOnlyList<PlantTag> Reactor { get; } = new PlantTag[]
        {
            new PlantTag("TIC101_PV", VariableIds.Plant_Reactor_TemperatureMeasurement, "degC"),
            new PlantTag("TIC101_SP", VariableIds.Plant_Reactor_TemperatureSetpoint, "degC"),
            new PlantTag("PIC102_PV", VariableIds.Plant_Reactor_PressureMeasurement, "bar"),
            new PlantTag("SIC103_SP", VariableIds.Plant_Reactor_AgitatorSpeed, "rpm"),
        };

        /// <summary>
        /// The tags of the boiler unit.
        /// </summary>
        public static IReadOnlyList<PlantTag> Boiler { get; } = new PlantTag[]
        {
            new PlantTag("PIC201_PV", VariableIds.Plant_Boiler_SteamPressure, "bar"),
            new PlantTag("FIC202_PV", VariableIds.Plant_Boiler_FeedwaterFlow, "m3/h"),
            new PlantTag("HS203_CMD", VariableIds.Plant_Boiler_BurnerEnabled, string.Empty),
        };

        /// <summary>
        /// Every tag of the plant.
        /// </summary>
        public static IReadOnlyList<PlantTag> All { get; } = Concat(Reactor, Boiler);

        /// <summary>
        /// Joins two tag lists.
        /// </summary>
        private static PlantTag[] Concat(IReadOnlyList<PlantTag> first, IReadOnlyList<PlantTag> second)
        {
            var all = new List<PlantTag>(first.Count + second.Count);

            all.AddRange(first);
            all.AddRange(second);

            return all.ToArray();
        }
    }
}
