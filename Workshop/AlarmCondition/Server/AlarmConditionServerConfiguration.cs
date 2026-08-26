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
using Opc.Ua;

namespace Quickstarts.AlarmConditionServer
{
    /// <summary>
    /// Stores the configuration the Alarm Condition server.
    /// </summary>
    [DataType(Namespace = "http://opcfoundation.org/Quickstarts/AlarmCondition")]
    public sealed partial class AlarmConditionServerConfiguration
    {

        #region Public Properties
        /// <summary>
        /// Gets or sets the list of top level Areas exposed by the server.
        /// </summary>
        [DataTypeField(Order = 1)]
        public ArrayOf<AreaConfiguration> Areas
        {
            get { return m_areas; }
            set { m_areas = value; }
        }
        #endregion

        #region Private Members
        private ArrayOf<AreaConfiguration> m_areas;
        #endregion
    }

    /// <summary>
    /// Stores the configuration for a Area within the Alarm Condition server.
    /// </summary>
    [DataType(Namespace = "http://opcfoundation.org/Quickstarts/AlarmCondition")]
    public sealed partial class AreaConfiguration
    {

        #region Public Properties
        /// <summary>
        /// The browse name for the instance.
        /// </summary>
        [DataTypeField(Order = 1)]
        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }

        /// <summary>
        /// Gets or set the list of sub-areas.
        /// </summary>
        [DataTypeField(Order = 2)]
        public ArrayOf<AreaConfiguration> SubAreas
        {
            get { return m_subAreas; }
            set { m_subAreas = value; }
        }

        /// <summary>
        /// Gets or set the list of sources.
        /// </summary>
        [DataTypeField(Order = 3)]
        public ArrayOf<string> SourcePaths
        {
            get { return m_sourcePaths; }
            set { m_sourcePaths = value; }
        }
        #endregion

        #region Private Members
        private string m_name;
        private ArrayOf<AreaConfiguration> m_subAreas;
        private ArrayOf<string> m_sourcePaths;
        #endregion
    }
}
