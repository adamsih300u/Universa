using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Universa.Desktop.Models;

namespace Universa.Desktop
{
    public class VerificationSession
    {
        public string UserId { get; set; }
        public string DeviceId { get; set; }
        public string TransactionId { get; set; }
        public VerificationState State { get; set; }
        public string Method { get; set; }
        public byte[] SharedSecret { get; set; }
        public string[] Emojis { get; set; }
        public string CancellationReason { get; set; }
        private Dictionary<string, string> _keysToBeSigned = new Dictionary<string, string>();

        public event EventHandler<VerificationState> StateChanged;

        public void UpdateState(VerificationState newState)
        {
            System.Diagnostics.Debug.WriteLine($"Verification session {TransactionId} state changing from {State} to {newState}");
            State = newState;
            StateChanged?.Invoke(this, newState);
        }

        public void Cancel(string reason)
        {
            CancellationReason = reason;
            UpdateState(VerificationState.Cancelled);
        }

        public bool CanTransitionTo(VerificationState newState)
        {
            return newState switch
            {
                VerificationState.Requested => State == VerificationState.None,
                VerificationState.Started => State == VerificationState.Requested,
                VerificationState.WaitingForKey => State == VerificationState.Started,
                VerificationState.KeysExchanged => State == VerificationState.WaitingForKey,
                VerificationState.KeysVerified => State == VerificationState.KeysExchanged,
                VerificationState.Completed => State == VerificationState.KeysVerified,
                VerificationState.Cancelled => true,
                _ => false
            };
        }

        public async Task HandleAccept(Dictionary<string, object> content)
        {
            System.Diagnostics.Debug.WriteLine($"Handling accept event for session {TransactionId}");
            
            if (!CanTransitionTo(VerificationState.Started))
            {
                throw new InvalidOperationException($"Cannot transition from {State} to Started");
            }

            try
            {
                // Verify the accept event contains compatible methods
                var method = content["method"]?.ToString();
                if (string.IsNullOrEmpty(method) || method != "m.sas.v1")
                {
                    throw new Exception("Unsupported verification method");
                }

                Method = method;
                UpdateState(VerificationState.Started);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling accept event: {ex.Message}");
                Cancel($"Failed to handle accept: {ex.Message}");
                throw;
            }
        }

        public async Task HandleKey(Dictionary<string, object> content)
        {
            System.Diagnostics.Debug.WriteLine($"Handling key event for session {TransactionId}");
            
            if (!CanTransitionTo(VerificationState.KeysExchanged))
            {
                throw new InvalidOperationException($"Cannot transition from {State} to KeysExchanged");
            }

            try
            {
                var key = content["key"]?.ToString();
                if (string.IsNullOrEmpty(key))
                {
                    throw new Exception("No key provided in key event");
                }

                // TODO: Implement actual key verification logic
                SharedSecret = System.Text.Encoding.UTF8.GetBytes(key);
                
                // Generate emoji indices
                using (var hmac = new System.Security.Cryptography.HMACSHA256(SharedSecret))
                {
                    var emojiBytes = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes("MATRIX_KEY_VERIFICATION_SAS"));
                    var emojiList = new List<string>();
                    
                    for (int i = 0; i < 7; i++)
                    {
                        var index = BitConverter.ToUInt16(emojiBytes, i * 2) % 64;
                        emojiList.Add(GetEmojiForIndex(index));
                    }
                    
                    Emojis = emojiList.ToArray();
                }

                UpdateState(VerificationState.KeysExchanged);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling key event: {ex.Message}");
                Cancel($"Failed to handle key: {ex.Message}");
                throw;
            }
        }

        public async Task HandleMac(Dictionary<string, object> content)
        {
            System.Diagnostics.Debug.WriteLine($"Handling MAC event for session {TransactionId}");
            
            if (!CanTransitionTo(VerificationState.Completed))
            {
                throw new InvalidOperationException($"Cannot transition from {State} to Completed");
            }

            try
            {
                var mac = content["mac"] as Dictionary<string, string>;
                var keys = content["keys"] as Dictionary<string, string>;

                if (mac == null || keys == null)
                {
                    throw new Exception("Invalid MAC or keys in MAC event");
                }

                // TODO: Implement actual MAC verification logic
                // For now, we'll just transition to completed state
                UpdateState(VerificationState.Completed);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling MAC event: {ex.Message}");
                Cancel($"Failed to handle MAC: {ex.Message}");
                throw;
            }
        }

        private string GetEmojiForIndex(int index)
        {
            // Standard Matrix emoji list
            var emojis = new[] {
                "👍", "👋", "🎉", "🎊", "🎈", "🎂", "🎁", "❤️", "😊", "🌟",
                "✨", "⭐", "🌈", "☀️", "⛅", "☁️", "🌧️", "⚡", "❄️", "🌊",
                "🎵", "🎶", "🎸", "🎹", "🎺", "🎻", "🥁", "🎤", "🎬", "🎨",
                "🎭", "🎪", "🎢", "🎡", "🎠", "🎮", "🕹", "🎲", "🎯", "🎳",
                "🎾", "⚽", "🏀", "🏈", "⚾", "🏉", "🎱", "🏓", "🏸", "🥊",
                "🥋", "🥅", "⛳", "⛸️", "🎣", "🎽", "🛹", "🛼", "🛶", "🎿",
                "🛷", "🥌", "🎯", "🪀"
            };

            return emojis[index % emojis.Length];
        }
    }
} 