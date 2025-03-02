using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Crypto.Signers;
using Universa.Desktop.Models;

namespace Universa.Desktop
{
    public class MatrixCrypto : IDisposable
    {
        private readonly string _userId;
        private readonly string _deviceId;
        private AsymmetricCipherKeyPair _identityKeyPair;
        private Dictionary<string, AsymmetricCipherKeyPair> _oneTimeKeys = new Dictionary<string, AsymmetricCipherKeyPair>();

        public MatrixCrypto(string userId, string deviceId)
        {
            _userId = userId;
            _deviceId = deviceId;
            System.Diagnostics.Debug.WriteLine($"MatrixCrypto initialized for user {userId} device {deviceId}");
        }

        public async Task<Dictionary<string, object>> GenerateDeviceKeys()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Generating device keys");
                var keyGen = new Ed25519KeyPairGenerator();
                keyGen.Init(new KeyGenerationParameters(new SecureRandom(), 256));
                _identityKeyPair = keyGen.GenerateKeyPair();

                var publicKey = ((Ed25519PublicKeyParameters)_identityKeyPair.Public).GetEncoded();
                var publicKeyBase64 = Convert.ToBase64String(publicKey);

                var deviceKeys = new Dictionary<string, object>
                {
                    ["user_id"] = _userId,
                    ["device_id"] = _deviceId,
                    ["algorithms"] = new[] { "m.olm.v1.curve25519-aes-sha2", "m.megolm.v1.aes-sha2" },
                    ["keys"] = new Dictionary<string, string>
                    {
                        [$"ed25519:{_deviceId}"] = publicKeyBase64,
                        [$"curve25519:{_deviceId}"] = publicKeyBase64
                    }
                };

                // Sign the device keys
                var signer = SignerUtilities.GetSigner("Ed25519");
                signer.Init(true, _identityKeyPair.Private);
                var message = Encoding.UTF8.GetBytes(publicKeyBase64);
                signer.BlockUpdate(message, 0, message.Length);
                var signature = signer.GenerateSignature();

                deviceKeys["signatures"] = new Dictionary<string, Dictionary<string, string>>
                {
                    [_userId] = new Dictionary<string, string>
                    {
                        [$"ed25519:{_deviceId}"] = Convert.ToBase64String(signature)
                    }
                };

                return deviceKeys;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating device keys: {ex.Message}");
                throw;
            }
        }

        public async Task<Dictionary<string, object>> GenerateOneTimeKeys(int count)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Generating {count} one-time keys");
                var oneTimeKeys = new Dictionary<string, string>();
                var keyGen = new Ed25519KeyPairGenerator();
                keyGen.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));

                for (int i = 0; i < count; i++)
                {
                    var keyId = $"signed_curve25519:{i}";
                    var keyPair = keyGen.GenerateKeyPair();
                    _oneTimeKeys[keyId] = keyPair;

                    var publicKey = ((Ed25519PublicKeyParameters)keyPair.Public).GetEncoded();
                    oneTimeKeys[keyId] = Convert.ToBase64String(publicKey);
                }

                return new Dictionary<string, object>
                {
                    ["one_time_keys"] = oneTimeKeys
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating one-time keys: {ex.Message}");
                throw;
            }
        }

        public void MarkKeysAsPublished()
        {
            System.Diagnostics.Debug.WriteLine("Keys marked as published");
        }

        private string SignJson(Dictionary<string, object> data)
        {
            try
            {
                var canonicalJson = JsonCanonicalizer.Canonicalize(data);
                var signature = Sign(Encoding.UTF8.GetBytes(canonicalJson));
                return Convert.ToBase64String(signature);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error signing JSON: {ex.Message}");
                throw;
            }
        }

        private byte[] Sign(byte[] message)
        {
            try
            {
                var signer = SignerUtilities.GetSigner("Ed25519");
                signer.Init(true, _identityKeyPair.Private);
                signer.BlockUpdate(message, 0, message.Length);
                return signer.GenerateSignature();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error signing message: {ex.Message}");
                throw;
            }
        }

        public void Dispose()
        {
            // Clear sensitive data
            _identityKeyPair = null;
            _oneTimeKeys.Clear();
        }

        public string DeviceId => _deviceId;
    }

    internal static class JsonCanonicalizer
    {
        public static string Canonicalize(Dictionary<string, object> data)
        {
            // Simple implementation - in practice, you'd want to use a proper JSON canonicalization library
            var sortedDict = new SortedDictionary<string, object>(data);
            return Newtonsoft.Json.JsonConvert.SerializeObject(sortedDict);
        }
    }
} 