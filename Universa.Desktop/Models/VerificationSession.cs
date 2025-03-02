using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Diagnostics;
using Newtonsoft.Json;

namespace Universa.Desktop.Models
{
    public class VerificationSession : IDisposable
    {
        private readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();
        private byte[] _sasBytes;
        private readonly List<(string emoji, string description)> _emojis = new List<(string, string)>
        {
            ("🐶", "Dog"),
            ("🐱", "Cat"),
            ("🦁", "Lion"),
            ("🐎", "Horse"),
            ("🦄", "Unicorn"),
            ("🐷", "Pig"),
            ("🐘", "Elephant"),
            ("🐰", "Rabbit"),
            ("🐼", "Panda"),
            ("🐓", "Rooster"),
            ("🐧", "Penguin"),
            ("🐢", "Turtle"),
            ("🐟", "Fish"),
            ("🐙", "Octopus"),
            ("🦋", "Butterfly"),
            ("🌷", "Flower"),
            ("🌳", "Tree"),
            ("🌎", "Globe"),
            ("🌙", "Moon"),
            ("☁️", "Cloud"),
            ("⚡", "Lightning"),
            ("⭐", "Star"),
            ("☔", "Umbrella"),
            ("🎪", "Circus"),
            ("🎨", "Artist"),
            ("🎭", "Theater"),
            ("🎪", "Tent"),
            ("🎯", "Target"),
            ("🎵", "Music"),
            ("🎮", "Game"),
            ("🎲", "Dice"),
            ("📞", "Phone")
        };

        public string UserId { get; set; }
        public string DeviceId { get; set; }
        public string TransactionId { get; set; }
        public VerificationState State { get; private set; }
        public string CancellationReason { get; private set; }
        public List<(string emoji, string description)> Emojis { get; private set; }

        public event EventHandler<VerificationState> StateChanged;

        public async Task HandleAccept(Dictionary<string, object> content)
        {
            Debug.WriteLine($"Handling verification accept: {JsonConvert.SerializeObject(content)}");
            
            if (content.TryGetValue("transaction_id", out var txnId) && 
                txnId.ToString() == TransactionId)
            {
                // Generate 6 bytes of random data for SAS
                _sasBytes = new byte[6];
                _rng.GetBytes(_sasBytes);
                
                // Select 7 emojis based on the SAS bytes
                Emojis = new List<(string emoji, string description)>();
                for (int i = 0; i < 7; i++)
                {
                    var index = _sasBytes[i % 6] % _emojis.Count;
                    Emojis.Add(_emojis[index]);
                }

                UpdateState(VerificationState.WaitingForKey);
                Debug.WriteLine($"Generated emojis for verification: {string.Join(", ", Emojis.Select(e => e.emoji))}");
            }
            else
            {
                Debug.WriteLine("Transaction ID mismatch in accept message");
            }
        }

        public async Task HandleKey(Dictionary<string, object> content)
        {
            Debug.WriteLine($"Handling verification key: {JsonConvert.SerializeObject(content)}");
            
            if (content.TryGetValue("transaction_id", out var txnId) && 
                txnId.ToString() == TransactionId)
            {
                UpdateState(VerificationState.KeysExchanged);
            }
        }

        public async Task HandleMac(Dictionary<string, object> content)
        {
            Debug.WriteLine($"Handling verification MAC: {JsonConvert.SerializeObject(content)}");
            
            if (content.TryGetValue("transaction_id", out var txnId) && 
                txnId.ToString() == TransactionId)
            {
                UpdateState(VerificationState.KeysVerified);
            }
        }

        public void Cancel(string reason = null)
        {
            Debug.WriteLine($"Cancelling verification: {reason ?? "No reason provided"}");
            UpdateState(VerificationState.Cancelled);
        }

        public void UpdateState(VerificationState newState)
        {
            Debug.WriteLine($"Verification state changing from {State} to {newState}");
            State = newState;
            StateChanged?.Invoke(this, newState);
        }

        public void Dispose()
        {
            // Clear sensitive data
            if (_sasBytes != null)
            {
                Array.Clear(_sasBytes, 0, _sasBytes.Length);
            }
            _rng.Dispose();
        }
    }
} 