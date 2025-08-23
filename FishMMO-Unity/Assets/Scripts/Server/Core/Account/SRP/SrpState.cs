namespace FishMMO.Server.Core.Account.SRP
{
	/// <summary>
	/// Represents the state of the SRP (Secure Remote Password) authentication process.
	/// </summary>
	public enum SrpState : byte
	{
		/// <summary>
		/// The server is verifying the client's ephemeral values.
		/// </summary>
		SrpVerify,

		/// <summary>
		/// The server is verifying the client's proof.
		/// </summary>
		SrpProof,

		/// <summary>
		/// The SRP authentication process has completed successfully.
		/// </summary>
		SrpSuccess,
	}
}