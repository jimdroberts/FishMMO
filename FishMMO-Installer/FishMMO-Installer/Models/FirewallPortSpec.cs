using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FishMMO.Installer
{
    /// <summary>
    /// A single firewall rule: one port or an inclusive port range, on TCP or UDP.
    /// Deserializes from <c>install-config.json</c> as either a bare integer
    /// (<c>443</c>, TCP) or a string (<c>"7770-7999/udp"</c>, <c>"8080/tcp"</c>,
    /// <c>"7770-7999"</c>). Protocol defaults to TCP when omitted.
    /// </summary>
    [JsonConverter(typeof(FirewallPortSpecJsonConverter))]
    public readonly record struct FirewallPortSpec
    {
        public const string Tcp = "tcp";
        public const string Udp = "udp";

        /// <summary>First port in the range (inclusive).</summary>
        public int From { get; }

        /// <summary>Last port in the range (inclusive). Equals <see cref="From"/> for a single port.</summary>
        public int To { get; }

        /// <summary>Lower-case protocol: <c>tcp</c> or <c>udp</c>.</summary>
        public string Protocol { get; }

        public FirewallPortSpec(int from, int to, string protocol)
        {
            // Plain ArgumentException without a parameter name: the message is shown to
            // the user verbatim, and "(Parameter 'to')" would only confuse them.
            if (from < 1 || from > 65535)
                throw new ArgumentException($"Port {from} is out of range. Ports must be between 1 and 65535.");
            if (to < 1 || to > 65535)
                throw new ArgumentException($"Port {to} is out of range. Ports must be between 1 and 65535.");
            if (to < from)
                throw new ArgumentException($"Port range end ({to}) is below its start ({from}).");

            protocol = (protocol ?? Tcp).Trim().ToLowerInvariant();
            if (protocol != Tcp && protocol != Udp)
                throw new ArgumentException($"Unsupported protocol '{protocol}'. Use 'tcp' or 'udp'.");

            From = from;
            To = to;
            Protocol = protocol;
        }

        /// <summary>A single TCP port.</summary>
        public static FirewallPortSpec TcpPort(int port) => new(port, port, Tcp);

        /// <summary>True when this spec covers more than one port.</summary>
        public bool IsRange => From != To;

        /// <summary>Port text without protocol, hyphenated for ranges (<c>7770-7999</c>).</summary>
        public string PortText => IsRange
            ? string.Create(CultureInfo.InvariantCulture, $"{From}-{To}")
            : From.ToString(CultureInfo.InvariantCulture);

        /// <summary>Port text using the ufw range separator (<c>7770:7999</c>).</summary>
        public string UfwPortText => IsRange
            ? string.Create(CultureInfo.InvariantCulture, $"{From}:{To}")
            : From.ToString(CultureInfo.InvariantCulture);

        /// <summary>Canonical form: <c>443/tcp</c> or <c>7770-7999/udp</c>.</summary>
        public override string ToString() => $"{PortText}/{Protocol}";

        /// <summary>
        /// Parses <c>"443"</c>, <c>"443/tcp"</c>, <c>"7770-7999"</c>, <c>"7770-7999/udp"</c>
        /// or <c>"7770:7999/udp"</c>. Throws <see cref="FormatException"/> with a
        /// message that names the offending text.
        /// </summary>
        public static FirewallPortSpec Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new FormatException("Firewall port entry is empty.");

            string trimmed = text.Trim();
            string portPart = trimmed;
            string protocol = Tcp;

            int slash = trimmed.IndexOf('/');
            if (slash >= 0)
            {
                portPart = trimmed[..slash].Trim();
                protocol = trimmed[(slash + 1)..].Trim();
                if (protocol.Length == 0)
                    throw new FormatException($"Firewall port entry '{text}' has a '/' but no protocol. Use 'tcp' or 'udp'.");
            }

            // Accept both the firewalld/netsh hyphen and the ufw colon as range separators.
            int sep = portPart.IndexOfAny(new[] { '-', ':' });
            string fromText = sep >= 0 ? portPart[..sep].Trim() : portPart;
            string toText = sep >= 0 ? portPart[(sep + 1)..].Trim() : fromText;

            if (!int.TryParse(fromText, NumberStyles.None, CultureInfo.InvariantCulture, out int from) ||
                !int.TryParse(toText, NumberStyles.None, CultureInfo.InvariantCulture, out int to))
            {
                throw new FormatException(
                    $"Firewall port entry '{text}' is not a port or port range. " +
                    "Expected forms: 443, \"443/tcp\", \"7770-7999/udp\".");
            }

            try
            {
                return new FirewallPortSpec(from, to, protocol);
            }
            catch (ArgumentException ex)
            {
                throw new FormatException($"Firewall port entry '{text}' is invalid: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Reads a <see cref="FirewallPortSpec"/> from a JSON number (single TCP port)
    /// or a JSON string (port, range, and optional protocol). Writes the canonical string form.
    /// </summary>
    public sealed class FirewallPortSpecJsonConverter : JsonConverter<FirewallPortSpec>
    {
        public override FirewallPortSpec Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    if (!reader.TryGetInt32(out int port))
                        throw new JsonException("Firewall port must be a whole number between 1 and 65535.");
                    try
                    {
                        return FirewallPortSpec.TcpPort(port);
                    }
                    catch (ArgumentException ex)
                    {
                        throw new JsonException($"Firewall port {port} is invalid: {ex.Message}", ex);
                    }

                case JsonTokenType.String:
                    try
                    {
                        return FirewallPortSpec.Parse(reader.GetString() ?? string.Empty);
                    }
                    catch (FormatException ex)
                    {
                        throw new JsonException(ex.Message, ex);
                    }

                default:
                    throw new JsonException(
                        $"Firewall port entries must be a number (443) or a string (\"7770-7999/udp\"), not {reader.TokenType}.");
            }
        }

        public override void Write(Utf8JsonWriter writer, FirewallPortSpec value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
