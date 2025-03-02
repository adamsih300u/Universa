using System;

namespace Universa.Desktop.Models
{
    public class CodeBlock
    {
        private string _code;
        public string Code 
        { 
            get => _code;
            set
            {
                _code = value;
                NewText = value; // Keep backward compatibility
            }
        }
        public string Language { get; set; }
        public bool IsApplied { get; set; }

        // Backward compatibility properties
        public string OriginalText { get; set; }
        public string NewText 
        { 
            get => _code;
            set => _code = value;
        }
        public string FullMatch { get; set; }

        public CodeBlock(string code, string language = "")
        {
            Code = code;
            Language = language;
            IsApplied = false;
        }

        public CodeBlock()
        {
            IsApplied = false;
        }
    }
} 