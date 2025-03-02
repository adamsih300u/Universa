using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    public class FileReferenceService
    {
        private readonly string _libraryPath;
        private string _currentFilePath;

        public FileReferenceService(string libraryPath)
        {
            _libraryPath = libraryPath ?? throw new ArgumentNullException(nameof(libraryPath));
        }

        public void SetCurrentFile(string filePath)
        {
            _currentFilePath = filePath;
        }

        /// <summary>
        /// Gets the content of a referenced file
        /// </summary>
        /// <param name="refPath">The reference path</param>
        /// <param name="currentFilePath">The current file path (optional)</param>
        /// <returns>The content of the referenced file</returns>
        public async Task<string> GetFileContent(string refPath, string currentFilePath = null)
        {
            if (string.IsNullOrEmpty(refPath))
                return null;
                
            try
            {
                string fullPath;
                if (Path.IsPathRooted(refPath))
                {
                    // If it's an absolute path, make sure it's within the library
                    var normalizedPath = Path.GetFullPath(refPath);
                    var normalizedLibraryPath = Path.GetFullPath(_libraryPath);
                    if (!normalizedPath.StartsWith(normalizedLibraryPath))
                    {
                        throw new InvalidOperationException("Referenced file must be within the library");
                    }
                    fullPath = normalizedPath;
                }
                else
                {
                    // For relative paths, try multiple resolution strategies
                    string currentDir = !string.IsNullOrEmpty(currentFilePath) 
                        ? Path.GetDirectoryName(currentFilePath) 
                        : (!string.IsNullOrEmpty(_currentFilePath) 
                            ? Path.GetDirectoryName(_currentFilePath) 
                            : _libraryPath);

                    // Try relative to current file first
                    fullPath = Path.GetFullPath(Path.Combine(currentDir, refPath));
                    
                    // If that doesn't exist, try relative to library root
                    if (!File.Exists(fullPath))
                    {
                        fullPath = Path.GetFullPath(Path.Combine(_libraryPath, refPath));
                    }
                    
                    // Verify the resolved path is still within the library
                    var normalizedLibraryPath = Path.GetFullPath(_libraryPath);
                    if (!fullPath.StartsWith(normalizedLibraryPath))
                    {
                        throw new InvalidOperationException("Referenced file must be within the library");
                    }
                }

                if (File.Exists(fullPath))
                {
                    string content = await File.ReadAllTextAsync(fullPath);
                    System.Diagnostics.Debug.WriteLine($"Successfully loaded reference file: {fullPath}");
                    return content;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Referenced file not found: {fullPath}");
                    System.Diagnostics.Debug.WriteLine($"Current file path: {currentFilePath ?? _currentFilePath}");
                    System.Diagnostics.Debug.WriteLine($"Library path: {_libraryPath}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading reference file {refPath}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        public async Task<List<FileReference>> LoadReferencesAsync(string content)
        {
            var references = new List<FileReference>();
            if (string.IsNullOrEmpty(content))
                return references;

            foreach (var line in content.Split('\n'))
            {
                var reference = FileReference.Parse(line.Trim());
                if (reference != null)
                {
                    try
                    {
                        string fullPath;
                        if (Path.IsPathRooted(reference.Path))
                        {
                            // If it's an absolute path, make sure it's within the library
                            var normalizedPath = Path.GetFullPath(reference.Path);
                            var normalizedLibraryPath = Path.GetFullPath(_libraryPath);
                            if (!normalizedPath.StartsWith(normalizedLibraryPath))
                            {
                                throw new InvalidOperationException("Referenced file must be within the library");
                            }
                            fullPath = normalizedPath;
                        }
                        else
                        {
                            // For relative paths, try multiple resolution strategies
                            string currentDir = !string.IsNullOrEmpty(_currentFilePath) 
                                ? Path.GetDirectoryName(_currentFilePath) 
                                : _libraryPath;

                            // Try relative to current file first
                            fullPath = Path.GetFullPath(Path.Combine(currentDir, reference.Path));
                            
                            // If that doesn't exist, try relative to library root
                            if (!File.Exists(fullPath))
                            {
                                fullPath = Path.GetFullPath(Path.Combine(_libraryPath, reference.Path));
                            }
                            
                            // Verify the resolved path is still within the library
                            var normalizedLibraryPath = Path.GetFullPath(_libraryPath);
                            if (!fullPath.StartsWith(normalizedLibraryPath))
                            {
                                throw new InvalidOperationException("Referenced file must be within the library");
                            }
                        }

                        if (File.Exists(fullPath))
                        {
                            reference.Content = await File.ReadAllTextAsync(fullPath);
                            references.Add(reference);
                            System.Diagnostics.Debug.WriteLine($"Successfully loaded reference file: {fullPath}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Referenced file not found: {fullPath}");
                            System.Diagnostics.Debug.WriteLine($"Current file path: {_currentFilePath}");
                            System.Diagnostics.Debug.WriteLine($"Library path: {_libraryPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading reference file {reference.Path}: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                    }
                }
            }

            return references;
        }
    }
} 