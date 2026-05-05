using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace JamesFrowen.SimpleWeb
{
    /// <summary>
    /// Parses PROXY protocol v1 (text) and v2 (binary) headers sent by a
    /// reverse proxy such as NGINX when <c>proxy_protocol on;</c> is enabled.
    /// <para>
    /// Spec references:
    ///   v1 – https://www.haproxy.org/download/1.8/doc/proxy-protocol.txt §2.1
    ///   v2 – https://www.haproxy.org/download/1.8/doc/proxy-protocol.txt §2.2
    /// </para>
    /// </summary>
    internal static class ProxyProtocol
    {
        // ── v2 constants ────────────────────────────────────────────────
        static readonly byte[] V2Signature = new byte[]
        {
            0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51,
            0x55, 0x49, 0x54, 0x0A
        };
        const int V2HeaderLength = 16; // 12-byte sig + ver/cmd + fam + 2-byte len
        // The v2 'len' field includes address data AND TLV extensions.
        // Spec: entire header fits in one MSS (536 bytes) → max len = 536 − 16 = 520.
        // NGINX v2 may include TLVs (PP2_TYPE_AUTHORITY, PP2_TYPE_SSL, etc.).
        const int V2MaxPayloadLength = 520;

        // ── v1 constants ────────────────────────────────────────────────
        static readonly byte[] V1Prefix = Encoding.ASCII.GetBytes("PROXY ");
        const int V1MaxLineLength = 108; // spec limit including CRLF

        /// <summary>
        /// Attempts to read a PROXY protocol header (v1 or v2) from
        /// <paramref name="stream"/> and returns the source IP address.
        /// </summary>
        /// <param name="stream">Raw TCP stream (before SSL/TLS).</param>
        /// <param name="sourceAddress">Parsed source IP on success; null on failure.</param>
        /// <returns><c>true</c> when a valid header was parsed.</returns>
        public static bool TryParse(Stream stream, out string sourceAddress)
        {
            sourceAddress = null;

            // Peek at the first bytes to distinguish v1 ("PROXY ") from v2 (binary sig).
            // We need at least 16 bytes to identify v2 and enough for v1's prefix.
            byte[] peek = new byte[V2HeaderLength];
            if (!TryReadExact(stream, peek, 0, V2HeaderLength))
                return false;

            if (MatchesV2Signature(peek))
                return TryParseV2(stream, peek, out sourceAddress);

            if (MatchesV1Prefix(peek))
                return TryParseV1(stream, peek, out sourceAddress);

            return false;
        }

        // ── v1 parsing ─────────────────────────────────────────────────

        static bool MatchesV1Prefix(byte[] peek)
        {
            for (int i = 0; i < V1Prefix.Length; i++)
            {
                if (peek[i] != V1Prefix[i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Parses a v1 text header. <paramref name="peek"/> already contains the
        /// first 16 bytes which include "PROXY " and part of the rest of the line.
        /// </summary>
        static bool TryParseV1(Stream stream, byte[] peek, out string sourceAddress)
        {
            sourceAddress = null;

            // Read the rest of the line (up to V1MaxLineLength total, terminated by \r\n).
            byte[] lineBuf = new byte[V1MaxLineLength];
            Buffer.BlockCopy(peek, 0, lineBuf, 0, peek.Length);
            int pos = peek.Length;

            // Continue reading one byte at a time until \r\n or limit.
            while (pos < V1MaxLineLength)
            {
                int b = stream.ReadByte();
                if (b < 0)
                    return false;

                lineBuf[pos++] = (byte)b;

                // Check for \r\n terminator.
                if (pos >= 2 && lineBuf[pos - 2] == (byte)'\r' && lineBuf[pos - 1] == (byte)'\n')
                {
                    // Exclude \r\n from the parsed string.
                    string line = Encoding.ASCII.GetString(lineBuf, 0, pos - 2);
                    return ParseV1Line(line, out sourceAddress);
                }
            }

            return false; // line too long
        }

        /// <summary>
        /// Parses a complete v1 header line (without CRLF).
        /// Format: "PROXY TCP4 srcIP dstIP srcPort dstPort"
        ///     or: "PROXY UNKNOWN ..."
        /// </summary>
        static bool ParseV1Line(string line, out string sourceAddress)
        {
            sourceAddress = null;

            // "PROXY UNKNOWN\r\n" is valid and means no address info.
            if (line.StartsWith("PROXY UNKNOWN", StringComparison.Ordinal))
                return true; // success but no address

            // Split: ["PROXY", "TCP4"/"TCP6", srcIP, dstIP, srcPort, dstPort]
            string[] parts = line.Split(' ');
            if (parts.Length != 6)
                return false;

            string family = parts[1];
            if (family != "TCP4" && family != "TCP6")
                return false;

            string srcIp = parts[2];
            string dstIp = parts[3];

            // Validate both IPs to reject malformed/spoofed headers.
            if (!IPAddress.TryParse(srcIp, out _))
                return false;
            if (!IPAddress.TryParse(dstIp, out _))
                return false;

            // Validate ports: must be numeric integers in [0..65535].
            if (!TryParsePort(parts[4]) || !TryParsePort(parts[5]))
                return false;

            sourceAddress = srcIp;
            return true;
        }

        // ── v2 parsing ─────────────────────────────────────────────────

        static bool MatchesV2Signature(byte[] peek)
        {
            for (int i = 0; i < V2Signature.Length; i++)
            {
                if (peek[i] != V2Signature[i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Parses a v2 binary header. <paramref name="header"/> already contains
        /// the 16-byte fixed header.
        /// </summary>
        static bool TryParseV2(Stream stream, byte[] header, out string sourceAddress)
        {
            sourceAddress = null;

            byte verCmd = header[12];
            int version = (verCmd >> 4) & 0x0F;
            int command = verCmd & 0x0F;

            if (version != 2)
                return false;

            // command: 0 = LOCAL (health check), 1 = PROXY
            byte familyProtocol = header[13];
            int family = (familyProtocol >> 4) & 0x0F;

            int addrLen = (header[14] << 8) | header[15];

            // Sanity-check the payload length (addresses + optional TLV data).
            if (addrLen < 0 || addrLen > V2MaxPayloadLength)
                return false;

            // Read the address block (may be 0 for LOCAL command).
            byte[] addrBlock = new byte[addrLen];
            if (addrLen > 0 && !TryReadExact(stream, addrBlock, 0, addrLen))
                return false;

            // LOCAL command: no address info (e.g. health check).
            if (command == 0)
                return true;

            if (command != 1)
                return false;

            // family: 1 = AF_INET, 2 = AF_INET6
            switch (family)
            {
                case 1: // AF_INET — 4+4+2+2 = 12 bytes
                    if (addrLen < 12)
                        return false;
                    byte[] ipv4 = new byte[4];
                    Buffer.BlockCopy(addrBlock, 0, ipv4, 0, 4);
                    sourceAddress = new IPAddress(ipv4).ToString();
                    return true;

                case 2: // AF_INET6 — 16+16+2+2 = 36 bytes
                    if (addrLen < 36)
                        return false;
                    byte[] ipv6 = new byte[16];
                    Buffer.BlockCopy(addrBlock, 0, ipv6, 0, 16);
                    sourceAddress = new IPAddress(ipv6).ToString();
                    return true;

                default:
                    // UNIX sockets or unknown — consume data but return no IP.
                    return true;
            }
        }

        // ── helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Validates a port string per the spec: decimal integer [0..65535], no leading zeroes.
        /// </summary>
        static bool TryParsePort(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 5)
                return false;

            // Leading zeroes are forbidden per spec (avoid octal confusion).
            if (s.Length > 1 && s[0] == '0')
                return false;

            if (!int.TryParse(s, out int port))
                return false;

            return port >= 0 && port <= 65535;
        }

        static bool TryReadExact(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (read <= 0)
                    return false;
                totalRead += read;
            }
            return true;
        }
    }
}
