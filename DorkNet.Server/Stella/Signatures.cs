using System.Security.Cryptography;
using System.Text;

namespace Stella
{
    public class Signatures
    {
        private static RSA? _rsa;

        public static void Init()
        {
            try
            {
                string pem = File.ReadAllText(Path.Combine("TransientData", "PrivateKey.pem"));

                _rsa = RSA.Create();
                _rsa.ImportFromPem(pem.ToCharArray());
            }
            catch (Exception ex)
            {
                throw new Exception("Error setting up Signatures: " + ex.Message);
            }
        }

        public static string Sign(byte[] data)
        {
            if (_rsa == null)
                throw new InvalidOperationException("Signatures not initialized. Call Init() first.");

            byte[] signatureBytes = _rsa.SignData(data, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);

            return Convert.ToBase64String(signatureBytes);
        }

        public static string Sign(dynamic data)
        {
            if (_rsa == null)
                return "never called init";

            string json = System.Text.Json.JsonSerializer.Serialize(data);
            byte[] dataBytes = Encoding.UTF8.GetBytes(json);

            byte[] signature = _rsa.SignData(dataBytes, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);

            return Convert.ToBase64String(signature);
        }

        public static string Sign(string data)
        {
            if (_rsa == null)
                return "never called init";

            string json = System.Text.Json.JsonSerializer.Serialize(data);
            byte[] dataBytes = Encoding.UTF8.GetBytes(json);

            byte[] signature = _rsa.SignData(dataBytes, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);

            return Convert.ToBase64String(signature);
        }
    }
}