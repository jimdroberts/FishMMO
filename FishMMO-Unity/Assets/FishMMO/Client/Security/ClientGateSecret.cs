namespace FishMMO.Client.Security
{
	/// <summary>
	/// IL-embedded client gate secret.
	///
	/// The default here is a sentinel, and this file is tracked so the project always
	/// compiles. The real value lives in the gitignored ClientApiSecret.generated.cs,
	/// written by FishMMO Dashboard > Game Settings (or by CI substituting
	/// FISHMMO_CLIENT_GATE_SECRET into GeneratedFileTemplates/ClientApiSecret.generated.cs.template),
	/// which implements <see cref="Configure"/> to overwrite it. See
	/// <see cref="FishMMO.Shared.HostConfig"/> for why the generated file no longer
	/// declares the whole class.
	/// </summary>
	/// <remarks>
	/// Must be <c>public</c> so the Editor assembly
	/// (<c>FishMMO.Client.Security.Editor</c> / <see cref="Editor.ClientSecurityBuildValidator"/>)
	/// can read the sentinel. <c>internal</c> is assembly-scoped only and caused CS0122.
	/// </remarks>
	public static partial class ClientGateSecret
	{
		/// <summary>
		/// Shared secret for X-FishMMO-Client HMAC header signing.
		/// The default is a sentinel — the dashboard or CI must replace it.
		/// </summary>
		public static readonly string Secret;

		static ClientGateSecret()
		{
			var values = new Values();
			Configure(values);

			Secret = values.Secret ?? "";
		}

		/// <summary>
		/// Implemented by ClientApiSecret.generated.cs.
		/// </summary>
		static partial void Configure(Values values);

		/// <summary>
		/// The value <see cref="Configure"/> may overwrite, initialised to a sentinel.
		/// </summary>
		private sealed class Values
		{
			public string Secret = CertificatePinSet.SentinelMarker + "_CLIENT_GATE_SECRET";
		}
	}
}
