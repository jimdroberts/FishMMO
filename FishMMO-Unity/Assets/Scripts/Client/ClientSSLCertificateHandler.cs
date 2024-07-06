using UnityEngine.Networking;

namespace FishMMO.Client
{
	public class ClientSSLCertificateHandler : CertificateHandler
	{
		protected override bool ValidateCertificate(byte[] certificateData)
		{
			// Implement your custom certificate validation logic here
			// Return true to accept the certificate, or false to reject it
			return true;  // Example: Always accept the certificate
		}
	}
}