using System;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System.Linq;
using Universa.Desktop.Models;
using Universa.Desktop.Core;
using Universa.Desktop.Core.Configuration;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Service that integrates manuscript generation with the existing fiction, outline, and rules chains
    /// Handles the orchestration between UI events and AI generation services
    /// </summary>
    public class ManuscriptIntegrationService
    {
        /// <summary>
        /// Handles chapter generation requests from the UI and coordinates with AI services
        /// </summary>
        public async Task<string> HandleChapterGenerationRequest(ChapterGenerationRequestedEventArgs args, string currentFilePath, int? cursorPosition = null, AIProvider? customProvider = null, string customModel = null)
        {
            try
            {
                Debug.WriteLine($"[ManuscriptIntegration] Processing chapter generation request for Chapter {args.ChapterNumber}: {args.ChapterTitle}");

                // Get configuration from the config service
                var configService = ServiceLocator.Instance.GetService<Core.Configuration.IConfigurationService>();
                if (configService == null)
                {
                    throw new InvalidOperationException("Configuration service not available");
                }

                // Determine AI configuration - use custom settings if provided, otherwise use current config
                var configInstance = Models.Configuration.Instance;
                var provider = customProvider ?? configInstance.DefaultAIProvider;
                string apiKey = GetApiKeyForProvider(provider, configService.Provider);
                string model = customModel ?? configInstance.LastUsedModel ?? GetDefaultModelForProvider(provider);
                
                Debug.WriteLine($"[ManuscriptIntegration] Using provider: {provider}, model: {model}, custom override: {customProvider.HasValue}");
                
                if (string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException($"API key not configured for {provider}");
                }

                // Determine library path (directory of the current file)
                string libraryPath = !string.IsNullOrEmpty(currentFilePath) ? 
                    Path.GetDirectoryName(currentFilePath) : 
                    Environment.CurrentDirectory;

                Debug.WriteLine($"[ManuscriptIntegration] Creating FictionWritingBeta service with provider: {provider}, model: {model}");

                // Create FictionWritingBeta service using the existing architecture
                var fictionService = await FictionWritingBeta.GetInstance(apiKey, model, provider, currentFilePath, libraryPath);
                
                // Use file content directly to ensure frontmatter is included for individual chapter generation too
                string contentWithFrontmatter = args.ExistingContent;
                if (!string.IsNullOrEmpty(currentFilePath) && File.Exists(currentFilePath))
                {
                    contentWithFrontmatter = await File.ReadAllTextAsync(currentFilePath);
                    Debug.WriteLine($"[ManuscriptIntegration] Using file content for chapter generation: {contentWithFrontmatter.Length} chars");
                }
                
                // Update the service with content (this loads frontmatter references automatically)
                await fictionService.UpdateContentAndInitialize(contentWithFrontmatter);

                // CRITICAL: Set cursor position for proper context around generation point
                if (cursorPosition.HasValue)
                {
                    fictionService.UpdateCursorPosition(cursorPosition.Value);
                    Debug.WriteLine($"[ManuscriptIntegration] Set cursor position to {cursorPosition.Value} for context-aware generation");
                }
                else
                {
                    // For manuscript generation, estimate cursor position at end of content
                    var estimatedPosition = args.ExistingContent?.Length ?? 0;
                    fictionService.UpdateCursorPosition(estimatedPosition);
                    Debug.WriteLine($"[ManuscriptIntegration] Estimated cursor position at {estimatedPosition} for manuscript generation");
                }

                // Build the generation prompt
                var prompt = BuildChapterGenerationPrompt(args);

                Debug.WriteLine($"[ManuscriptIntegration] Sending generation request to Fiction service with cursor context");

                // Generate content using the fiction service (this will use cursor context automatically)
                var generatedContent = await fictionService.ProcessRequest(args.ExistingContent, prompt);

                Debug.WriteLine($"[ManuscriptIntegration] Generated {generatedContent.Length} characters for Chapter {args.ChapterNumber}");
                return generatedContent;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ManuscriptIntegration] Error processing chapter generation: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Builds a chapter generation prompt for the Fiction chain that emphasizes style guide compliance and full narrative prose
        /// </summary>
        private string BuildChapterGenerationPrompt(ChapterGenerationRequestedEventArgs args)
        {
            var promptBuilder = new System.Text.StringBuilder();

            promptBuilder.AppendLine($"GENERATE COMPLETE NARRATIVE CHAPTER: Chapter {args.ChapterNumber}: {args.ChapterTitle}");
            promptBuilder.AppendLine();
            
            promptBuilder.AppendLine("=== CRITICAL NARRATIVE GENERATION INSTRUCTIONS ===");
            promptBuilder.AppendLine("You are writing a complete, fully-fleshed chapter of a published novel. This is NOT outline expansion or summary development.");
            promptBuilder.AppendLine();
            
            promptBuilder.AppendLine("STYLE GUIDE COMPLIANCE:");
            promptBuilder.AppendLine("- FOLLOW YOUR STYLE GUIDE METICULOUSLY - this defines your voice, tone, and narrative approach");
            promptBuilder.AppendLine("- Use the established prose style, sentence structure, and descriptive techniques");
            promptBuilder.AppendLine("- Maintain the narrative voice and perspective defined in your style guide");
            promptBuilder.AppendLine("- Apply all vocabulary preferences, dialogue patterns, and descriptive guidelines");
            promptBuilder.AppendLine();
            
            if (!string.IsNullOrEmpty(args.ChapterSummary))
            {
                promptBuilder.AppendLine("CHAPTER OUTLINE OBJECTIVES (Transform into full scenes):");
                promptBuilder.AppendLine(args.ChapterSummary);
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("CRITICAL: The above are GOALS to achieve through original narrative prose.");
                promptBuilder.AppendLine("DO NOT copy, expand, or paraphrase outline text. CREATE fresh dialogue, actions, and descriptions that fulfill these objectives.");
                promptBuilder.AppendLine();
            }

            promptBuilder.AppendLine("FULL NARRATIVE REQUIREMENTS:");
            promptBuilder.AppendLine($"- Begin with: ## Chapter {args.ChapterNumber}: {args.ChapterTitle}");
            promptBuilder.AppendLine("- Write 1,500-3,000 words of complete narrative prose");
            promptBuilder.AppendLine("- Create vivid, cinematic scenes with rich sensory details");
            promptBuilder.AppendLine("- Develop authentic dialogue that reveals character and advances plot");
            promptBuilder.AppendLine("- Include character thoughts, emotions, and internal conflicts");
            promptBuilder.AppendLine("- Show character actions and reactions in specific, visual detail");
            promptBuilder.AppendLine("- Establish setting and atmosphere through descriptive prose");
            promptBuilder.AppendLine("- Maintain consistent pacing and tension throughout");
            promptBuilder.AppendLine("- Create smooth transitions between scenes and moments");
            promptBuilder.AppendLine("- End with a compelling conclusion that sets up the next chapter");
            promptBuilder.AppendLine();
            
            promptBuilder.AppendLine("CHARACTER & WORLD CONSISTENCY:");
            promptBuilder.AppendLine("- Use established character personalities, speech patterns, and motivations from your rules");
            promptBuilder.AppendLine("- Maintain world-building consistency and established story logic");
            promptBuilder.AppendLine("- Reference and build upon previous character relationships and conflicts");
            promptBuilder.AppendLine("- Honor the story's established tone, themes, and emotional landscape");
            promptBuilder.AppendLine();
            
            promptBuilder.AppendLine("MANUSCRIPT INTEGRATION:");
            promptBuilder.AppendLine("- Write as a seamless continuation of the existing narrative");
            promptBuilder.AppendLine("- Match the established writing quality and sophistication");
            promptBuilder.AppendLine("- Ensure this chapter reads like it was written by the same author as previous chapters");
            promptBuilder.AppendLine("- Create content worthy of publication in a professional novel");
            promptBuilder.AppendLine();
            
            promptBuilder.AppendLine("🎯 YOUR GOAL: Create a complete, publication-ready chapter that a reader would enjoy as part of a finished novel.");

            return promptBuilder.ToString();
        }

        /// <summary>
        /// Gets the API key for the specified provider
        /// </summary>
        private string GetApiKeyForProvider(AIProvider provider, dynamic config)
        {
            return provider switch
            {
                AIProvider.OpenAI => config.OpenAIApiKey,
                AIProvider.Anthropic => config.AnthropicApiKey,
                AIProvider.XAI => config.XAIApiKey,
                AIProvider.OpenRouter => config.OpenRouterApiKey,
                AIProvider.Ollama => null, // Ollama doesn't require an API key
                _ => throw new ArgumentException($"Unsupported provider: {provider}")
            };
        }

        /// <summary>
        /// Gets the default model for the specified provider
        /// </summary>
        private string GetDefaultModelForProvider(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.OpenAI => "gpt-4",
                AIProvider.Anthropic => "claude-3-5-sonnet-20241022",
                AIProvider.XAI => "grok-beta",
                AIProvider.OpenRouter => "anthropic/claude-3.5-sonnet",
                AIProvider.Ollama => "llama3.1",
                _ => throw new ArgumentException($"Unsupported provider: {provider}")
            };
        }

        /// <summary>
        /// Finds the outline file using frontmatter reference (ref outline:)
        /// </summary>
        private async Task<string> FindOutlineFileAsync(string currentFilePath)
        {
            try
            {
                if (string.IsNullOrEmpty(currentFilePath) || !File.Exists(currentFilePath))
                    return null;

                // Read the manuscript file and extract frontmatter
                var content = await File.ReadAllTextAsync(currentFilePath);
                var frontmatterProcessor = new Services.FrontmatterProcessor();
                var frontmatter = frontmatterProcessor.GetFrontmatterFromContent(content);

                // Look for "ref outline" in frontmatter
                var outlineRef = frontmatter.FirstOrDefault(kv => 
                    kv.Key.Equals("ref outline", StringComparison.OrdinalIgnoreCase) ||
                    kv.Key.Equals("outline", StringComparison.OrdinalIgnoreCase)).Value;

                if (!string.IsNullOrEmpty(outlineRef))
                {
                    var directory = Path.GetDirectoryName(currentFilePath);
                    var outlinePath = Path.Combine(directory, outlineRef);
                    
                    if (File.Exists(outlinePath))
                    {
                        Debug.WriteLine($"[ManuscriptIntegration] Found outline via frontmatter reference: {outlineRef}");
                        return outlinePath;
                    }
                    else
                    {
                        Debug.WriteLine($"[ManuscriptIntegration] Frontmatter references outline '{outlineRef}' but file not found at: {outlinePath}");
                    }
                }

                Debug.WriteLine($"[ManuscriptIntegration] No 'ref outline:' found in frontmatter of: {Path.GetFileName(currentFilePath)}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ManuscriptIntegration] Error finding outline file: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Generates a complete manuscript using the provided outline and settings
        /// Returns the updated content with generated chapters
        /// </summary>
        public async Task<(bool success, string updatedContent)> GenerateCompleteManuscriptAsync(
            string markdownContent, 
            string markdownFilePath, 
            string libraryPath,
            ManuscriptGenerationSettings settings = null,
            Func<string, Task> progressCallback = null)
        {
            try
            {
                progressCallback?.Invoke("Initializing manuscript generation...");

                // Use provided settings or create default
                settings ??= new ManuscriptGenerationSettings();

                // Get configuration from the config service
                var configService = ServiceLocator.Instance.GetService<Core.Configuration.IConfigurationService>();
                if (configService == null)
                {
                    throw new InvalidOperationException("Configuration service not available");
                }

                // Get AI configuration based on settings
                var configInstance = Models.Configuration.Instance;
                var configProvider = configService.Provider;
                
                string apiKey;
                string model;
                AIProvider provider;

                if (settings.UseCurrentChatSettings)
                {
                    // Use current chat settings
                    provider = configInstance.DefaultAIProvider;
                    apiKey = GetApiKeyForProvider(provider, configProvider);
                    model = configInstance.LastUsedModel ?? GetDefaultModelForProvider(provider);
                }
                else
                {
                    // Use custom settings
                    provider = settings.Provider;
                    apiKey = GetApiKeyForProvider(provider, configProvider);
                    model = settings.Model;
                }

                if (string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException($"API key not configured for {provider}");
                }

                // Create the manuscript generation service
                var manuscriptService = new ManuscriptGenerationService();
                
                // Find outline file using the same logic as MarkdownTab
                var outlineFilePath = await FindOutlineFileAsync(markdownFilePath);
                if (string.IsNullOrEmpty(outlineFilePath) || !File.Exists(outlineFilePath))
                {
                    throw new InvalidOperationException("Outline file not found. Please create an outline or add 'ref outline:' to frontmatter");
                }

                // Extract chapters from outline
                var outlineContent = await File.ReadAllTextAsync(outlineFilePath);
                var chapters = manuscriptService.ExtractChaptersFromOutline(outlineContent);
                
                if (chapters.Count == 0)
                {
                    throw new InvalidOperationException("No chapters found in outline");
                }

                progressCallback?.Invoke($"Found {chapters.Count} chapters to generate");

                // Create FictionWritingBeta service
                var fictionService = await FictionWritingBeta.GetInstance(apiKey, model, provider, markdownFilePath, libraryPath);
                
                // Use file content directly to ensure frontmatter is included
                string fileContentWithFrontmatter = markdownContent;
                if (!string.IsNullOrEmpty(markdownFilePath) && File.Exists(markdownFilePath))
                {
                    fileContentWithFrontmatter = await File.ReadAllTextAsync(markdownFilePath);
                    Debug.WriteLine($"[ManuscriptIntegration] Using file content with frontmatter: {fileContentWithFrontmatter.Length} chars");
                }
                else
                {
                    Debug.WriteLine($"[ManuscriptIntegration] Using passed content (may lack frontmatter): {markdownContent.Length} chars");
                }
                
                await fictionService.UpdateContentAndInitialize(fileContentWithFrontmatter);

                // Use the file content with frontmatter as starting point for generation
                string currentContent = fileContentWithFrontmatter;
                int totalChapters = chapters.Count;

                // Generate each chapter
                Debug.WriteLine($"[ManuscriptIntegration] Starting generation of {chapters.Count} chapters in order: [{string.Join(", ", chapters.Select(c => $"Ch{c.Item1}"))}]");
                
                for (int i = 0; i < chapters.Count; i++)
                {
                    var chapter = chapters[i];
                    var chapterNumber = chapter.Item1;
                    var chapterTitle = chapter.Item2;
                    var chapterSummary = chapter.Item3;

                    Debug.WriteLine($"[ManuscriptIntegration] === Processing Chapter {chapterNumber} ({i + 1}/{totalChapters}) ===");
                    Debug.WriteLine($"[ManuscriptIntegration] Current content length before generation: {currentContent.Length}");
                    
                    progressCallback?.Invoke($"Generating Chapter {chapterNumber}: {chapterTitle} ({i + 1}/{totalChapters})");

                    // Create chapter generation arguments
                    var args = new ChapterGenerationRequestedEventArgs
                    {
                        ChapterNumber = chapterNumber,
                        ChapterTitle = chapterTitle,
                        ChapterSummary = chapterSummary,
                        ExistingContent = currentContent
                    };

                    // For manuscript generation, set cursor at end of current content for proper context
                    var cursorPosition = currentContent?.Length ?? 0;

                    // Generate chapter content with cursor context, passing custom provider/model if specified
                    var chapterContent = await HandleChapterGenerationRequest(args, markdownFilePath, cursorPosition, 
                        settings.UseCurrentChatSettings ? null : provider, 
                        settings.UseCurrentChatSettings ? null : model);

                    Debug.WriteLine($"[ManuscriptIntegration] Generated content for Chapter {chapterNumber}: {chapterContent.Length} characters");

                    // Insert the chapter into the document
                    var insertionInfo = manuscriptService.DetermineInsertionPosition(currentContent, chapterNumber, chapterContent);
                    currentContent = manuscriptService.InsertContentAtPosition(currentContent, chapterContent, insertionInfo);

                    Debug.WriteLine($"[ManuscriptIntegration] Content length after inserting Chapter {chapterNumber}: {currentContent.Length}");

                    // Add delay between chapters if specified
                    if (settings.DelayBetweenChapters > 0 && i < chapters.Count - 1)
                    {
                        await Task.Delay(settings.DelayBetweenChapters);
                    }

                    // Update progress
                    var progress = (int)((i + 1) / (double)totalChapters * 100);
                    progressCallback?.Invoke($"Generated Chapter {chapterNumber} ({progress}% complete)");
                }

                progressCallback?.Invoke("Manuscript generation complete!");

                // Auto-save if requested
                if (settings.AutoSaveAfterGeneration && !string.IsNullOrEmpty(markdownFilePath))
                {
                    await File.WriteAllTextAsync(markdownFilePath, currentContent);
                    progressCallback?.Invoke("Manuscript saved to file");
                }

                return (true, currentContent);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ManuscriptIntegration] Error in complete manuscript generation: {ex.Message}");
                progressCallback?.Invoke($"Error: {ex.Message}");
                
                // Return file content with frontmatter if available, otherwise fallback to original
                string fallbackContent = markdownContent;
                if (!string.IsNullOrEmpty(markdownFilePath) && File.Exists(markdownFilePath))
                {
                    try
                    {
                        fallbackContent = await File.ReadAllTextAsync(markdownFilePath);
                    }
                    catch
                    {
                        // If file read fails, use original content
                    }
                }
                return (false, fallbackContent);
            }
        }
    }

    /// <summary>
    /// Extension methods for connecting manuscript generation to MarkdownTabAvalon
    /// </summary>
    public static class MarkdownTabManuscriptExtensions
    {
        /// <summary>
        /// Sets up manuscript generation integration for a MarkdownTabAvalon
        /// </summary>
        public static void SetupManuscriptGeneration(this Views.MarkdownTabAvalon tab)
        {
            var integrationService = new ManuscriptIntegrationService();

            // Subscribe to chapter generation requests
            tab.ChapterGenerationRequested += async (sender, args) =>
            {
                try
                {
                    // Get current cursor position for context-aware generation
                    var cursorPosition = tab.LastKnownCursorPosition;
                    
                    // Generate the content using the current file path and cursor context
                    var generatedContent = await integrationService.HandleChapterGenerationRequest(args, tab.FilePath, cursorPosition);

                    // Auto-insert the content at the optimal position (MarkdownTabAvalon would need this method implemented)
                    // For now, we'll append to the end - this may need to be implemented in MarkdownTabAvalon
                    var currentContent = tab.GetContent();
                    var newContent = currentContent + "\n\n" + generatedContent;
                    tab.MarkdownDocument.Text = newContent;

                    Debug.WriteLine($"[MarkdownTabAvalon] Successfully generated and inserted Chapter {args.ChapterNumber} with cursor context at position {cursorPosition}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MarkdownTabAvalon] Error in manuscript generation: {ex.Message}");
                    System.Windows.MessageBox.Show($"Error generating chapter: {ex.Message}", 
                        "Generation Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            };
        }
    }
} 