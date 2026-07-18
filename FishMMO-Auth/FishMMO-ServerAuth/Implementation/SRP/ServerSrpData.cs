using System.Security;
using SecureRemotePassword;

namespace FishMMO.Auth.Implementation
{
	/// <summary>
	/// Holds SRP (Secure Remote Password) authentication data and logic for a server-side session.
	/// </summary>
	/// <remarks>
	/// <b>String zeroization limitation:</b> SRP values (username, salt, verifier, ephemeral,
	/// proof) are .NET immutable strings that cannot be deterministically zeroed. The
	/// SecureRemotePassword library and database boundary both require string parameters.
	/// <see cref="Clear"/> nulls all references so the GC can collect them, but the string
	/// contents remain in managed heap memory until garbage-collected. Intermediate decrypted
	/// <c>byte[]</c> buffers are zeroed via <c>CryptographicOperations.ZeroMemory</c> in the
	/// authenticator workers.
	/// </remarks>
	public class ServerSrpData
	{
		private const string SrpProofFailed = "Srp failed to generate proof.";

		/// <summary>
		/// Gets the username associated with the SRP session.
		/// </summary>
		public string? UserName { get; private set; }

		/// <summary>
		/// Gets the public ephemeral value sent by the client.
		/// </summary>
		public string? PublicClientEphemeral { get; private set; }

		/// <summary>
		/// Gets the SRP server instance handling the protocol.
		/// </summary>
		public SrpServer? SrpServer { get; private set; }

		/// <summary>
		/// Gets the salt used for the SRP session.
		/// </summary>
		public string? Salt { get; private set; }

		/// <summary>
		/// Gets the verifier used for the SRP session.
		/// </summary>
		public string? Verifier { get; private set; }

		/// <summary>
		/// Gets the server's ephemeral values for the SRP session.
		/// </summary>
		public SrpEphemeral? ServerEphemeral { get; private set; }

		/// <summary>
		/// Gets the SRP session object after proof verification.
		/// </summary>
		public SrpSession? Session { get; private set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="ServerSrpData"/> class.
		/// </summary>
		/// <param name="parameters">The SRP parameters to use.</param>
		/// <param name="username">The username for the session.</param>
		/// <param name="publicClientEphemeral">The public ephemeral value sent by the client.</param>
		/// <param name="salt">The salt for the session.</param>
		/// <param name="verifier">The verifier for the session.</param>
		public ServerSrpData(SrpParameters parameters, string username, string publicClientEphemeral, string salt, string verifier)
		{
			UserName = username;
			PublicClientEphemeral = publicClientEphemeral;
			SrpServer = new SrpServer(parameters);
			this.Salt = salt;
			this.Verifier = verifier;
			ServerEphemeral = SrpServer.GenerateEphemeral(this.Verifier);
		}

		/// <summary>
		/// Verifies the client's proof and derives the SRP session, returning the server's proof if successful.
		/// </summary>
		/// <param name="clientProof">The proof sent by the client.</param>
		/// <param name="serverProof">Output parameter that will contain the server's proof if verification is successful, or an error message if it fails.</param>
		/// <returns><c>true</c> if the proof is valid and the session is established; otherwise, <c>false</c>.</returns>
		public bool GetProof(string clientProof, out string serverProof)
		{
			try
			{
				Session = SrpServer!.DeriveSession(ServerEphemeral!.Secret,
												  PublicClientEphemeral,
												  Salt,
												  UserName,
												  Verifier,
												  clientProof);
				serverProof = Session.Proof;
				return true;
			}
			catch (SecurityException)
			{
				serverProof = SrpProofFailed;
				return false;
			}
		}

		/// <summary>
		/// Nulls all string and object references to allow GC collection of sensitive SRP material.
		/// Call after SRP success to minimize the window during which secrets reside in memory.
		/// </summary>
		/// <remarks>
		/// .NET strings are immutable and GC-managed — their contents cannot be deterministically
		/// zeroed. This method nulls all references so the GC can collect the backing memory at
		/// its next opportunity. For true defense-in-depth, ensure the process runs with locked
		/// pages or consider a native SRP library that operates on pinned byte arrays.
		/// </remarks>
		public void Clear()
		{
			UserName = null;
			PublicClientEphemeral = null;
			SrpServer = null;
			Salt = null;
			Verifier = null;
			ServerEphemeral = null;
			Session = null;
		}
	}
}