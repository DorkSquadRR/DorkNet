using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;

namespace DorkNet.Server.Services;

/// <summary>
/// Encrypts the data-protection key ring (the keys behind the sign-in
/// cookies) before it is stored in the <c>DataProtectionKeys</c> table:
/// AES-256-GCM under a key derived (HKDF-SHA256) from the server secret.
/// The secret comes from the environment, not the database, so a database
/// backup alone holds no usable keys. Changing the secret makes the stored
/// keys unreadable: the server then creates a new key and everyone signs in
/// again.
/// </summary>
public sealed class DataProtectionKeyEncryption : IXmlEncryptor
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] Info = Encoding.UTF8.GetBytes("DorkNet.DataProtectionKeys.v1");
    private static readonly XName Root = "dorknetEncryptedKey";
    private readonly byte[] _key;

    public DataProtectionKeyEncryption(string secret)
    {
        if (string.IsNullOrEmpty(secret))
            throw new InvalidOperationException("The server secret is required to encrypt the data-protection keys.");
        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret), 32, salt: null, info: Info);
    }

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        var payload = new byte[NonceSize + TagSize + plaintext.Length];
        var nonce = payload.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using (var aes = new AesGcm(_key, TagSize))
            aes.Encrypt(nonce, plaintext, payload.AsSpan(NonceSize + TagSize), payload.AsSpan(NonceSize, TagSize));
        return new EncryptedXmlInfo(
            new XElement(Root, new XAttribute("version", "1"), Convert.ToBase64String(payload)),
            typeof(Decryptor));
    }

    public XElement DecryptElement(XElement encrypted)
    {
        if (encrypted.Name != Root || (string?)encrypted.Attribute("version") != "1")
            throw new CryptographicException("Unrecognised encrypted data-protection key.");
        var payload = Convert.FromBase64String(encrypted.Value);
        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException("Encrypted data-protection key is truncated.");
        var plaintext = new byte[payload.Length - NonceSize - TagSize];
        using (var aes = new AesGcm(_key, TagSize))
            aes.Decrypt(payload.AsSpan(0, NonceSize), payload.AsSpan(NonceSize + TagSize),
                payload.AsSpan(NonceSize, TagSize), plaintext);
        return XElement.Parse(Encoding.UTF8.GetString(plaintext));
    }

    /// <summary>Created by the data-protection system from the stored type
    /// name, with the app's service provider.</summary>
    public sealed class Decryptor(IServiceProvider services) : IXmlDecryptor
    {
        public XElement Decrypt(XElement encryptedElement) =>
            services.GetRequiredService<DataProtectionKeyEncryption>().DecryptElement(encryptedElement);
    }
}
