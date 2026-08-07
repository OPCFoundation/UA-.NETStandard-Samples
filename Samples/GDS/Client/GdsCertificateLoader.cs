using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Gds.Client
{
    internal static class GdsCertificateLoader
    {
        public static X509Certificate2 LoadCertificate(byte[] data) => X509CertificateLoader.LoadCertificate(data);

        public static X509Certificate2 LoadCertificateFromFile(string path) => X509CertificateLoader.LoadCertificateFromFile(path);

        public static X509Certificate2 LoadPkcs12(byte[] data, string password, X509KeyStorageFlags keyStorageFlags) =>
            X509CertificateLoader.LoadPkcs12(data, password, keyStorageFlags);
    }
}
