/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A filter that can be applied to a list of certificates.
    /// </summary>
    public class CertificateListFilter
    {
        #region Public Properties
        /// <summary>
        /// Gets or sets the subject name filter.
        /// </summary>
        /// <value>The subject name filter.</value>
        public string SubjectName
        {
            get { return m_subjectName; }
            set { m_subjectName = value; }
        }

        /// <summary>
        /// Gets or sets the issuer name filter.
        /// </summary>
        /// <value>The issuer name filter.</value>
        public string IssuerName
        {
            get { return m_issuerName; }
            set { m_issuerName = value; }
        }

        /// <summary>
        /// Gets or sets the domain name filter.
        /// </summary>
        /// <value>The issuer domain filter.</value>
        public string Domain
        {
            get { return m_domain; }
            set { m_domain = value; }
        }

        /// <summary>
        /// Gets or sets the certificate type filter.
        /// </summary>
        /// <value>The issuer certificate type filter.</value>
        public CertificateListFilterType[] CertificateTypes
        {
            get { return m_certificateTypes; }
            set { m_certificateTypes = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the private key filter.
        /// </summary>
        /// <value><c>true</c> if the private key filter is turned on; otherwise, <c>false</c>.</value>
        public bool PrivateKey
        {
            get { return m_privateKey; }
            set { m_privateKey = value; }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Checks if the certicate meets the filter criteria.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <returns>True if it meets the criteria.</returns>
        public bool Match(X509Certificate2 certificate)
        {
            if (certificate == null)
            {
                return false;
            }

            try
            {
                if (!String.IsNullOrEmpty(m_subjectName))
                {
                    if (!Match(certificate.Subject, "CN*" + m_subjectName + ",*"))
                    {
                        return false;
                    }
                }

                if (!String.IsNullOrEmpty(m_issuerName))
                {
                    if (!Match(certificate.Issuer, "CN*" + m_issuerName + ",*"))
                    {
                        return false;
                    }
                }

                if (!String.IsNullOrEmpty(m_domain))
                {
                    IList<string> domains = X509Utils.GetDomainsFromCertificate(certificate);

                    bool found = false;

                    for (int ii = 0; ii < domains.Count; ii++)
                    {
                        if (Match(domains[ii], m_domain))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        return false;
                    }
                }

                // check for private key.
                if (m_privateKey)
                {
                    if (!certificate.HasPrivateKey)
                    {
                        return false;
                    }
                }

                if (m_certificateTypes != null)
                {
                    // determine if a CA certificate.
                    bool isCA = X509Utils.IsCertificateAuthority(certificate);

                    // determine if self-signed.
                    bool isSelfSigned = X509Utils.CompareDistinguishedName(certificate.Subject, certificate.Issuer);

                    // match if one or more of the criteria match.
                    bool found = false;

                    for (int ii = 0; ii < m_certificateTypes.Length; ii++)
                    {
                        switch (m_certificateTypes[ii])
                        {
                            case CertificateListFilterType.Application:
                            {
                                if (!isCA)
                                {
                                    found = true;
                                }

                                break;
                            }

                            case CertificateListFilterType.CA:
                            {
                                if (isCA)
                                {
                                    found = true;
                                }

                                break;
                            }

                            case CertificateListFilterType.SelfSigned:
                            {
                                if (isSelfSigned)
                                {
                                    found = true;
                                }

                                break;
                            }

                            case CertificateListFilterType.Issued:
                            {
                                if (!isSelfSigned)
                                {
                                    found = true;
                                }

                                break;
                            }
                        }
                    }

                    if (!found)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Returns true if the target string matches the UA pattern string.
        /// The pattern string may include UA wildcards %_\[]!
        /// </summary>
        /// <param name="target">String to check for a pattern match.</param>
        /// <param name="pattern">Pattern to match with the target string.</param>
        /// <returns>true if the target string matches the pattern, otherwise false.</returns>
        public static bool Match(string target, string pattern)
        {
            if (string.IsNullOrEmpty(target))
            {
                return false;
            }

            if (string.IsNullOrEmpty(pattern))
            {
                return true;
            }

            List<string> tokens = Parse(pattern);

            int targetIndex = 0;

            for (int ii = 0; ii < tokens.Count; ii++)
            {
                targetIndex = Match(target, targetIndex, tokens, ref ii);

                if (targetIndex < 0)
                {
                    return false;
                }
            }

            return targetIndex >= target.Length;
        }

        #endregion

        private static List<string> Parse(string pattern)
        {
            var tokens = new List<string>();

            int ii = 0;
            var buffer = new StringBuilder();

            while (ii < pattern.Length)
            {
                char ch = pattern[ii];

                if (ch == '\\')
                {
                    ii++;

                    if (ii >= pattern.Length)
                    {
                        break;
                    }

                    buffer.Append(pattern[ii]);
                    ii++;
                    continue;
                }

                if (ch == '_')
                {
                    if (buffer.Length > 0)
                    {
                        tokens.Add(buffer.ToString());
                        buffer.Length = 0;
                    }

                    tokens.Add("_");
                    ii++;
                    continue;
                }

                if (ch == '%')
                {
                    if (buffer.Length > 0)
                    {
                        tokens.Add(buffer.ToString());
                        buffer.Length = 0;
                    }

                    tokens.Add("%");
                    ii++;

                    while (ii < pattern.Length && pattern[ii] == '%')
                    {
                        ii++;
                    }

                    continue;
                }

                if (ch == '[')
                {
                    if (buffer.Length > 0)
                    {
                        tokens.Add(buffer.ToString());
                        buffer.Length = 0;
                    }

                    buffer.Append(ch);
                    ii++;
                    while (ii < pattern.Length && pattern[ii] != ']')
                    {
                        if (pattern[ii] == '-' && ii > 0 && ii < pattern.Length - 1)
                        {
                            int start = Convert.ToInt32(pattern[ii - 1]) + 1;
                            int end = Convert.ToInt32(pattern[ii + 1]);

                            while (start < end)
                            {
                                buffer.Append(Convert.ToChar(start));
                                start++;
                            }

                            buffer.Append(Convert.ToChar(end));
                            ii += 2;
                            continue;
                        }

                        buffer.Append(pattern[ii]);
                        ii++;
                    }

                    buffer.Append(']');
                    tokens.Add(buffer.ToString());
                    buffer.Length = 0;

                    ii++;
                    continue;
                }

                buffer.Append(ch);
                ii++;
            }

            if (buffer.Length > 0)
            {
                tokens.Add(buffer.ToString());
                buffer.Length = 0;
            }

            return tokens;
        }
        private static int Match(
            string target,
            int targetIndex,
            IList<string> tokens,
            ref int tokenIndex)
        {
            if (tokens == null || tokenIndex < 0 || tokenIndex >= tokens.Count)
            {
                return -1;
            }

            if (target == null || targetIndex < 0 || targetIndex >= target.Length)
            {
                if (tokens[tokenIndex] == "%" && tokenIndex == tokens.Count - 1)
                {
                    return targetIndex;
                }

                return -1;
            }

            string token = tokens[tokenIndex];

            if (token == "_")
            {
                if (targetIndex >= target.Length)
                {
                    return -1;
                }

                return targetIndex + 1;
            }

            if (token == "%")
            {
                return SkipToNext(target, targetIndex, tokens, ref tokenIndex);
            }

            if (token.StartsWith('['))
            {
                bool inverse = false;
                bool match = false;

                for (int ii = 1; ii < token.Length - 1; ii++)
                {
                    if (token[ii] == '^')
                    {
                        inverse = true;
                        continue;
                    }

                    if (!inverse && target[targetIndex] == token[ii])
                    {
                        return targetIndex + 1;
                    }

                    match |= inverse && target[targetIndex] == token[ii];
                }

                if (inverse && !match)
                {
                    return targetIndex + 1;
                }

                return -1;
            }

            if (target[targetIndex..].StartsWith(token, StringComparison.Ordinal))
            {
                return targetIndex + token.Length;
            }

            return -1;
        }

        private static int SkipToNext(
            string target,
            int targetIndex,
            IList<string> tokens,
            ref int tokenIndex)
        {
            if (targetIndex >= target.Length - 1)
            {
                return targetIndex + 1;
            }

            if (tokenIndex >= tokens.Count - 1)
            {
                return target.Length + 1;
            }

            if (!tokens[tokenIndex + 1].StartsWith("[^", StringComparison.Ordinal))
            {
                int nextTokenIndex = tokenIndex + 1;

                // skip over unmatched chars.
                while (targetIndex < target.Length &&
                    Match(target, targetIndex, tokens, ref nextTokenIndex) < 0)
                {
                    targetIndex++;
                    nextTokenIndex = tokenIndex + 1;
                }

                nextTokenIndex = tokenIndex + 1;

                // skip over duplicate matches.
                while (targetIndex < target.Length &&
                    Match(target, targetIndex, tokens, ref nextTokenIndex) >= 0)
                {
                    targetIndex++;
                    nextTokenIndex = tokenIndex + 1;
                }

                // return last match.
                if (targetIndex <= target.Length)
                {
                    return targetIndex - 1;
                }
            }
            else
            {
                int start = targetIndex;
                int nextTokenIndex = tokenIndex + 1;

                // skip over matches.
                while (targetIndex < target.Length &&
                    Match(target, targetIndex, tokens, ref nextTokenIndex) >= 0)
                {
                    targetIndex++;
                    nextTokenIndex = tokenIndex + 1;
                }

                // no match in string.
                if (targetIndex < target.Length)
                {
                    return -1;
                }

                // try the next token.
                if (tokenIndex >= tokens.Count - 2)
                {
                    return target.Length + 1;
                }

                tokenIndex++;

                return SkipToNext(target, start, tokens, ref tokenIndex);
            }

            return -1;
        }


        #region Private Fields
        private string m_subjectName;
        private string m_issuerName;
        private string m_domain;
        private CertificateListFilterType[] m_certificateTypes;
        private bool m_privateKey;
        #endregion
    }

    /// <summary>
    /// The available certificate filter types.
    /// </summary>
    public enum CertificateListFilterType
    {
        /// <summary>
        /// The certificate is an application instance certificate.
        /// </summary>
        Application,

        /// <summary>
        /// The certificate is an certificate authority certificate.
        /// </summary>
        CA,

        /// <summary>
        /// The certificate is self-signed.
        /// </summary>
        SelfSigned,

        /// <summary>
        /// The certificate was issued by a certificate authority.
        /// </summary>
        Issued
    }
}
