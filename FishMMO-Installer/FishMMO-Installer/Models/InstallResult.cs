namespace FishMMO.Installer
{
    /// <summary>
    /// Result of a single component installation operation.
    /// </summary>
    public sealed record InstallResult
    {
        /// <summary>True if the component installation succeeded.</summary>
        public bool Success { get; init; }

        /// <summary>Component name (e.g. "postgresql", "nginx").</summary>
        public string ComponentName { get; init; } = string.Empty;

        /// <summary>Error message when Success is false.</summary>
        public string? ErrorMessage { get; init; }

        /// <summary>Wall-clock duration of the installation.</summary>
        public TimeSpan Duration { get; init; }

        /// <summary>Creates a successful result.</summary>
        public static InstallResult Ok(string componentName, TimeSpan? duration = null)
            => new()
            {
                Success = true,
                ComponentName = componentName,
                Duration = duration ?? TimeSpan.Zero,
            };

        /// <summary>Creates a failure result.</summary>
        public static InstallResult Fail(string componentName, string? errorMessage = null, TimeSpan? duration = null)
            => new()
            {
                Success = false,
                ComponentName = componentName,
                ErrorMessage = errorMessage,
                Duration = duration ?? TimeSpan.Zero,
            };

        public override string ToString()
            => Success
                ? $"OK  {ComponentName} ({Duration.TotalSeconds:F1}s)"
                : $"FAIL {ComponentName}: {ErrorMessage ?? "unknown error"} ({Duration.TotalSeconds:F1}s)";
    }
}