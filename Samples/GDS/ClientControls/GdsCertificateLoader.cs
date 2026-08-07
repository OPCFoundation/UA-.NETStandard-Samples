using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Gds.Client.Controls
{
    internal static class GdsCertificateLoader
    {
        public static X509Certificate2 LoadCertificate(byte[] data) => X509CertificateLoader.LoadCertificate(data);

        public static X509Certificate2 LoadCertificateFromFile(string path) => X509CertificateLoader.LoadCertificateFromFile(path);
    }
}
