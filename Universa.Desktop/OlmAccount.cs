using System;
using System.Collections.Generic;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Crypto.Signers;

namespace Universa
{
    public class OlmAccount : IDisposable
    {
        private readonly Ed25519KeyPairGenerator _keyGenerator;
        private readonly AsymmetricCipherKeyPair _identityKeyPair;
        private readonly List<OneTimeKey> _oneTimeKeys;
        private readonly SecureRandom _random;

        public OlmAccount(Ed25519KeyPairGenerator keyGenerator)
        {
            _keyGenerator = keyGenerator;
            _random = new SecureRandom();
            _oneTimeKeys = new List<OneTimeKey>();

            // Generate identity keys
            _identityKeyPair = _keyGenerator.GenerateKeyPair();
            System.Diagnostics.Debug.WriteLine("OlmAccount: Generated identity keys");
        }

        public void GenerateOneTimeKeys(int count)
        {
            try
            {
                _oneTimeKeys.Clear();
                for (int i = 0; i < count; i++)
                {
                    var keyPair = _keyGenerator.GenerateKeyPair();
                    var publicKey = ((Ed25519PublicKeyParameters)keyPair.Public).GetEncoded();
                    var privateKey = ((Ed25519PrivateKeyParameters)keyPair.Private).GetEncoded();

                    _oneTimeKeys.Add(new OneTimeKey
                    {
                        KeyId = i.ToString(),
                        Value = Convert.ToBase64String(publicKey),
                        PrivateKey = Convert.ToBase64String(privateKey)
                    });
                }
                System.Diagnostics.Debug.WriteLine($"OlmAccount: Generated {count} one-time keys");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OlmAccount: Failed to generate one-time keys - {ex.Message}");
                throw;
            }
        }

        public IReadOnlyList<OneTimeKey> OneTimeKeys => _oneTimeKeys;

        public (string Curve25519, string Ed25519) IdentityKeys
        {
            get
            {
                try
                {
                    var publicKey = ((Ed25519PublicKeyParameters)_identityKeyPair.Public).GetEncoded();
                    // For Curve25519, we'll derive it from Ed25519 (this is a simplification)
                    var curve25519Key = new byte[32];
                    Array.Copy(publicKey, curve25519Key, 32);

                    return (
                        Convert.ToBase64String(curve25519Key),
                        Convert.ToBase64String(publicKey)
                    );
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"OlmAccount: Failed to get identity keys - {ex.Message}");
                    throw;
                }
            }
        }

        public string Sign(string message)
        {
            try
            {
                var signer = SignerUtilities.GetSigner("Ed25519");
                signer.Init(true, _identityKeyPair.Private);

                var messageBytes = Encoding.UTF8.GetBytes(message);
                signer.BlockUpdate(messageBytes, 0, messageBytes.Length);

                var signature = signer.GenerateSignature();
                return Convert.ToBase64String(signature);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OlmAccount: Failed to sign message - {ex.Message}");
                throw;
            }
        }

        public bool VerifySignature(string message, string signature, byte[] publicKey)
        {
            try
            {
                var signer = SignerUtilities.GetSigner("Ed25519");
                signer.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));

                var messageBytes = Encoding.UTF8.GetBytes(message);
                signer.BlockUpdate(messageBytes, 0, messageBytes.Length);

                var signatureBytes = Convert.FromBase64String(signature);
                return signer.VerifySignature(signatureBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OlmAccount: Failed to verify signature - {ex.Message}");
                return false;
            }
        }

        public void MarkKeysAsPublished()
        {
            _oneTimeKeys.Clear();
            System.Diagnostics.Debug.WriteLine("OlmAccount: Marked keys as published");
        }

        public void Dispose()
        {
            // Clear sensitive data
            _oneTimeKeys.Clear();
        }
    }

    public class OneTimeKey
    {
        public string KeyId { get; set; }
        public string Value { get; set; }
        public string PrivateKey { get; set; }
    }
} 