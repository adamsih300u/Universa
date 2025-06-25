using System;

namespace Universa.Desktop.Models
{
    /// <summary>
    /// Settings for manuscript generation including AI model selection
    /// </summary>
    public class ManuscriptGenerationSettings
    {
        public AIProvider Provider { get; set; }
        public string Model { get; set; }
        public bool UseCurrentChatSettings { get; set; } = true;
        public bool GenerateSequentially { get; set; } = true;
        public int DelayBetweenChapters { get; set; } = 1000; // ms
        public bool ShowProgressDialog { get; set; } = true;
        public bool AutoSaveAfterGeneration { get; set; } = false;
        
        /// <summary>
        /// Gets display name for the settings
        /// </summary>
        public string DisplayName => UseCurrentChatSettings ? 
            "Use Current Chat Settings" : 
            $"{Provider} - {Model}";
    }
} 