using Microsoft.Extensions.Configuration;

namespace FishMMO.WebShared
{
    /// <summary>
    /// Reads list settings so that the configuration source that sets a list replaces it entirely.
    /// </summary>
    /// <remarks>
    /// <para>
    /// .NET configuration merges arrays index by index: a list is the keys <c>Name:0</c>,
    /// <c>Name:1</c>, ... and a later file only overrides the indices it names. Each web server's
    /// <c>appsettings.json</c> is the Development template and <c>appsettings.Production.json</c> the
    /// Production overrides, so a Production list shorter than the Development one kept the
    /// Development tail: one HTTPS connect origin over six loopback ones became the HTTPS origin plus
    /// five <c>http://localhost</c> origins, and the WebGL server refused to start in Production.
    /// </para>
    /// <para>
    /// Here the highest-priority source that sets the list (environment variables, then the
    /// environment's file, then <c>appsettings.json</c>, as registered) supplies all of it, the way an
    /// operator reading the files expects. An empty list (<c>[]</c>) in that source counts as setting
    /// it, to nothing.
    /// </para>
    /// </remarks>
    public static class ConfigurationLists
    {
        /// <summary>
        /// The list at <paramref name="key"/> from the highest-priority source that sets it, in index order;
        /// null when no source sets it.
        /// </summary>
        public static string[]? Get(IConfiguration? configuration, string key)
        {
            if (configuration == null) return null;
            if (configuration is not IConfigurationRoot root)
            {
                // A section or other view has no providers to choose between.
                return configuration.GetSection(key).Get<string[]>();
            }

            foreach (IConfigurationProvider provider in root.Providers.Reverse())
            {
                var indices = new SortedDictionary<int, string>();
                foreach (string child in provider.GetChildKeys(Enumerable.Empty<string>(), key).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (int.TryParse(child, out int index) && index >= 0 &&
                        provider.TryGet(key + ConfigurationPath.KeyDelimiter + child, out string? value) && value != null)
                    {
                        indices[index] = value;
                    }
                }
                if (indices.Count > 0) return indices.Values.ToArray();

                // An empty JSON array is stored as the key itself with no value and no children.
                if (provider.TryGet(key, out string? scalar) && string.IsNullOrEmpty(scalar))
                {
                    return Array.Empty<string>();
                }
            }
            return null;
        }
    }
}
