/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using log4net;
using Nini.Config;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text;

namespace OpenSim.Framework
{
    public static class NetworkXffUtil
    {
        // Logger
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static List<IPNetwork> m_TrustedProxies = null;
        private static object mainlock = new();
        public static void Initialize(IConfig netconfig)
        {
            if(netconfig == null)
                return;

            lock (mainlock)
            {
                if (m_TrustedProxies == null ) //only read once
                {
                    m_TrustedProxies = [];
                    string proxiesconfig = netconfig.GetString("XFFTrustedProxies", string.Empty);
                    // [Confluence] Unset means this machine (loopback): a reverse proxy on the same host, as Confluence
                    // trusted before this setting existed, so an existing front end keeps passing the visitor's address.
                    // "none" trusts no proxy.
                    if (proxiesconfig.Length == 0)
                        proxiesconfig = "127.0.0.0/8|::1/128";
                    else if (proxiesconfig.Equals("none", StringComparison.OrdinalIgnoreCase))
                        proxiesconfig = string.Empty;
                    if (proxiesconfig.Length > 0 )
                    {
                        string[] entries = proxiesconfig.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        foreach ( string entry in entries )
                            AddImpl(entry);
                    }
                }
                m_log.Info($"[NetworkXffUtil] added {m_TrustedProxies.Count} allow proxy entries");
            }
        }

        public static IPAddress GetIP(string address)
        {
            UriHostNameType type = Uri.CheckHostName(address);
            if (type == UriHostNameType.Unknown || type == UriHostNameType.Basic)
            {
                m_log.Error($"[NetworkXffUtil] failed to parse host entry {address}");
                return null;
            }
            IPAddress ip = Util.GetHostFromDNS(address);
            if (ip == null)
            {
                m_log.Error($"[NetworkXffUtil] failed to parse host entry {address}");
                return null;
            }
            if (ip.IsIPv4MappedToIPv6)
                ip = ip.MapToIPv4();
            return ip;
        }

        public static void AddImpl(string entry)
        {
            if (entry.Contains('/'))
            {
                if (IPNetwork.TryParse(entry, out IPNetwork entrynet))
                    m_TrustedProxies.Add(entrynet);
                else
                    m_log.Error($"[NetworkXffUtil] failed to parse CDIR entry {entry}");
            }
            else
            {
                IPAddress ip = GetIP(entry);
                if(ip != null)
                {
                    string ipstr = ip.ToString() + (ip.AddressFamily == AddressFamily.InterNetworkV6 ? "/128" : "/32");
                    if (IPNetwork.TryParse(ipstr, out IPNetwork entrynet))
                        m_TrustedProxies.Add(entrynet);
                    else
                        m_log.Error($"[NetworkXffUtil] failed to parse host entry {entry}");
                }
            }
        }

        public static void Add(string entry)
        {
            lock (mainlock)
            {
                if (m_TrustedProxies == null)
                    return;
                AddImpl(entry);
            }
        }

        public static bool Contains(string entry)
        {
            if(string.IsNullOrEmpty(entry))
                return false;

            lock (mainlock)
            {
                if (m_TrustedProxies == null || m_TrustedProxies.Count == 0)
                    return false;
            }

            IPAddress ip = GetIP(entry);
            if (ip == null)
                return false;

            lock (mainlock)
            {
                foreach(IPNetwork net in m_TrustedProxies)
                {
                    if (net.Contains(ip))
                        return true;
                }
            }
           return false;
        }

        public static bool Contains(IPAddress ip)
        {
            if (ip == null)
                return false;

            lock (mainlock)
            {
                if (m_TrustedProxies == null || m_TrustedProxies.Count == 0)
                    return false;
                foreach(IPNetwork net in m_TrustedProxies)
                {
                    if (net.Contains(ip))
                        return true;
                }
            }
           return false;
        }

        /// <summary>[Confluence] A further X-Forwarded-For header (or value) after the ones before it, as one chain.</summary>
        public static string AppendChain(string existing, string value)
        {
            value = value?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(existing))
                return value;
            if (value.Length == 0)
                return existing;
            return existing + ", " + value;
        }

        /// <summary>
        /// [Confluence] One X-Forwarded-For entry as an address: an IPv4 or IPv6 address, with a port or IPv6 brackets
        /// allowed. No DNS: the entries come from the client. Null when it is not an address.
        /// </summary>
        public static IPAddress ParseEntry(string entry)
        {
            if (string.IsNullOrWhiteSpace(entry))
                return null;
            string s = entry.Trim().Trim('"');
            if (s.StartsWith('['))
            {
                int close = s.IndexOf(']');
                if (close < 0)
                    return null;
                s = s.Substring(1, close - 1);
            }
            else if (s.Count(c => c == ':') == 1)
                s = s.Substring(0, s.IndexOf(':'));         // IPv4 with a port
            if (!IPAddress.TryParse(s, out IPAddress ip))
                return null;
            return ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
        }

        /// <summary>The address a request is taken to be from when its forwarding chain names no address that can be used.</summary>
        public static readonly IPAddress UnknownClient = IPAddress.None;

        /// <summary>
        /// The client address of a request that arrived from <paramref name="peer"/> with the X-Forwarded-For chain
        /// <paramref name="header"/>.
        /// </summary>
        /// <remarks>
        /// [Confluence] 2026-10-19. Only a peer that is a trusted proxy (XFFTrustedProxies) is asked; the chain is read
        /// from the right, past the trusted proxies, and the first address that is not one is the client. A trusted
        /// proxy appends the address it saw to the right, so nothing the client wrote to the left can be chosen. When
        /// an entry there is not an address the client is unknown (<see cref="UnknownClient"/>), never the proxy's own
        /// address: a loopback proxy's address is trusted, and an unreadable entry must not make a request trusted. A
        /// chain of trusted proxies only names the left-most of them. A private client address behind a public proxy
        /// is not believed (the request stays the proxy's).
        /// </remarks>
        public static bool CheckXFF(IPEndPoint peer, string header, out IPEndPoint xffremote, out string shortheader)
        {
            xffremote = null;
            shortheader = string.Empty;
            if (string.IsNullOrEmpty(header) || peer == null || !Contains(peer.Address))
                return false;

            string[] addrs = header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (addrs.Length == 0)
                return false;

            IPAddress lastproxy = peer.Address;
            for (int i = addrs.Length - 1; i >= 0; i--)
            {
                IPAddress addrip = ParseEntry(addrs[i]);
                if (addrip == null)
                {
                    m_log.Debug($"[NetworkXffUtil] unreadable X-Forwarded-For entry \"{addrs[i]}\" from {peer.Address}; client unknown");
                    xffremote = new IPEndPoint(UnknownClient, peer.Port);
                    shortheader = string.Empty;
                    return true;
                }

                if (Contains(addrip) && i > 0)
                {
                    lastproxy = addrip;
                    continue;
                }

                if (Util.IsPrivateNetwork(addrip) && !Util.IsPrivateNetwork(lastproxy))
                    return false;

                xffremote = new IPEndPoint(addrip, peer.Port);
                shortheader = addrs[i];
                return true;
            }
            return false;
        }
    }
}
