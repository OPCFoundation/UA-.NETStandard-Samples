/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Quickstarts.AliasNames.Server
{
    /// <summary>
    /// The node manager of the plant, which knows nothing about alias names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// That is the point worth noticing about it. Part 17 is an index laid over an address
    /// space, not a way of building one: the alias inventory lives in an
    /// <c>IAliasNameStore</c> that <see cref="AliasNamesServer"/> assembles, and the
    /// categories which serve it are node managers of their own. Nothing in this class, and
    /// nothing in <c>ModelDesign.xml</c>, has to change to publish a tag list, to rename a
    /// tag or to point one at a different node.
    /// </para>
    /// <para>
    /// So all this node manager does is what any node manager does: load the model and give
    /// its variables values. The values move, because the sample client resolves a tag name
    /// to a node id and then reads it, and a value which never changes makes a poor
    /// demonstration of having found the right node.
    /// </para>
    /// </remarks>
    [NodeManager]
    public partial class AliasNamesNodeManager
    {
        #region Configure
        /// <summary>
        /// Seeds the plant with values and starts the simulation.
        /// </summary>
        partial void Configure(IAliasNamesNodeManagerBuilder builder)
        {
            m_temperature = builder.Plant.Reactor.TemperatureMeasurement.Node;
            m_pressure = builder.Plant.Reactor.PressureMeasurement.Node;
            m_steamPressure = builder.Plant.Boiler.SteamPressure.Node;
            m_feedwaterFlow = builder.Plant.Boiler.FeedwaterFlow.Node;

            SetValue(m_temperature, Variant.From(84.2));
            SetValue(builder.Plant.Reactor.TemperatureSetpoint.Node, Variant.From(85.0));
            SetValue(m_pressure, Variant.From(2.4));
            SetValue(builder.Plant.Reactor.AgitatorSpeed.Node, Variant.From(120.0));

            SetValue(m_steamPressure, Variant.From(11.8));
            SetValue(m_feedwaterFlow, Variant.From(43.5));
            SetValue(builder.Plant.Boiler.BurnerEnabled.Node, Variant.From(true));

            // the clock of the server, so that the simulation and the timestamps it writes
            // run on the same time source as the rest of the server and a test can drive
            // them with a FakeTimeProvider. ITimeProviderProvider is the opt-in seam for
            // reaching it; an IServerInternal which does not implement it falls back to
            // the system clock.
            m_timeProvider = (Server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;

            // one timer for the whole plant: the measurements wander a little around their
            // seeded values so that a client which resolved a tag name can see the node it
            // landed on is live
            m_simulation = m_timeProvider.CreateTimer(
                OnSimulate,
                null,
                kSimulationPeriod,
                kSimulationPeriod);
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Stops the simulation when the node manager goes away.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_simulation?.Dispose();
                m_simulation = null;
            }

            base.Dispose(disposing);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Moves the measured values a little.
        /// </summary>
        private void OnSimulate(object state)
        {
            try
            {
                m_step++;

                Wander(m_temperature, 84.2, 0.8);
                Wander(m_pressure, 2.4, 0.05);
                Wander(m_steamPressure, 11.8, 0.3);
                Wander(m_feedwaterFlow, 43.5, 1.2);
            }
            catch (Exception exception)
            {
                m_logger.LogError(exception, "Unexpected error simulating the plant.");
            }
        }

        /// <summary>
        /// Puts a variable a little either side of its nominal value.
        /// </summary>
        /// <remarks>
        /// A sine rather than a random number, so that a reader watching two clients side by
        /// side sees the same curve in both and can tell a stale display from a moving value.
        /// </remarks>
        private void Wander(BaseVariableState node, double nominal, double amplitude)
        {
            double offset = amplitude * Math.Sin(m_step / 8.0);

            SetValue(node, Variant.From(Math.Round(nominal + offset, 3)));
        }

        /// <summary>
        /// Sets the value of a variable and stamps it with the current time.
        /// </summary>
        private void SetValue(BaseVariableState node, Variant value)
        {
            node.Value = value;
            node.Timestamp = new DateTimeUtc(m_timeProvider.GetUtcNow().UtcDateTime);
            node.ClearChangeMasks(SystemContext, false);
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// How often the measured values move.
        /// </summary>
        private static readonly TimeSpan kSimulationPeriod = TimeSpan.FromSeconds(1);

        private TimeProvider m_timeProvider = TimeProvider.System;
        private BaseVariableState m_temperature;
        private BaseVariableState m_pressure;
        private BaseVariableState m_steamPressure;
        private BaseVariableState m_feedwaterFlow;
        private ITimer m_simulation;
        private int m_step;
        #endregion
    }
}
