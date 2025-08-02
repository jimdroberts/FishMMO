using UnityEngine.Networking;

namespace FishMMO.Client
{
	/// <summary>
	/// Custom SSL certificate handler for UnityWebRequest on the client.
	/// Used to validate server SSL certificates during HTTPS requests.
	/// </summary>
	public class ClientSSLCertificateHandler : CertificateHandler
	{
		/// <summary>
		/// Validates the server SSL certificate.
		/// Always returns true, accepting all certificates.
		/// </summary>
		/// <param name="certificateData">The raw certificate data received from the server.</param>
		/// <returns>True to accept the certificate, false to reject.</returns>
		protected override bool ValidateCertificate(byte[] certificateData)
		{
			// Accepts all certificates. For production, implement proper validation logic.
			return true;
		}
	}
}