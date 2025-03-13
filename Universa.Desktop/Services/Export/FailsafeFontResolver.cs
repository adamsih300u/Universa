using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using PdfSharp.Fonts;

namespace PdfSharp.Snippets.Font
{
    /// <summary>
    /// A simple font resolver that uses the system's installed fonts.
    /// </summary>
    public class FailsafeFontResolver : IFontResolver
    {
        // Dictionary to cache font data
        private static readonly Dictionary<string, byte[]> _fontCache = new Dictionary<string, byte[]>();
        
        // Dictionary to map font family names to file paths
        private static readonly Dictionary<string, string> _fontPathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        // Initialize the font path map
        static FailsafeFontResolver()
        {
            try
            {
                InitializeFontMap();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing font map: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Initializes the font path map by scanning the system fonts folder
        /// </summary>
        private static void InitializeFontMap()
        {
            string fontsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts));
            if (!Directory.Exists(fontsFolder))
            {
                Debug.WriteLine($"Fonts folder not found: {fontsFolder}");
                return;
            }
            
            // Add common fonts with their exact file names
            _fontPathMap["arial"] = Path.Combine(fontsFolder, "arial.ttf");
            _fontPathMap["arial bold"] = Path.Combine(fontsFolder, "arialbd.ttf");
            _fontPathMap["arial italic"] = Path.Combine(fontsFolder, "ariali.ttf");
            _fontPathMap["arial bold italic"] = Path.Combine(fontsFolder, "arialbi.ttf");
            
            _fontPathMap["times new roman"] = Path.Combine(fontsFolder, "times.ttf");
            _fontPathMap["times new roman bold"] = Path.Combine(fontsFolder, "timesbd.ttf");
            _fontPathMap["times new roman italic"] = Path.Combine(fontsFolder, "timesi.ttf");
            _fontPathMap["times new roman bold italic"] = Path.Combine(fontsFolder, "timesbi.ttf");
            
            _fontPathMap["courier new"] = Path.Combine(fontsFolder, "cour.ttf");
            _fontPathMap["courier new bold"] = Path.Combine(fontsFolder, "courbd.ttf");
            _fontPathMap["courier new italic"] = Path.Combine(fontsFolder, "couri.ttf");
            _fontPathMap["courier new bold italic"] = Path.Combine(fontsFolder, "courbi.ttf");
            
            _fontPathMap["verdana"] = Path.Combine(fontsFolder, "verdana.ttf");
            _fontPathMap["verdana bold"] = Path.Combine(fontsFolder, "verdanab.ttf");
            _fontPathMap["verdana italic"] = Path.Combine(fontsFolder, "verdanai.ttf");
            _fontPathMap["verdana bold italic"] = Path.Combine(fontsFolder, "verdanaz.ttf");
            
            _fontPathMap["calibri"] = Path.Combine(fontsFolder, "calibri.ttf");
            _fontPathMap["calibri bold"] = Path.Combine(fontsFolder, "calibrib.ttf");
            _fontPathMap["calibri italic"] = Path.Combine(fontsFolder, "calibrii.ttf");
            _fontPathMap["calibri bold italic"] = Path.Combine(fontsFolder, "calibriz.ttf");
            
            _fontPathMap["segoe ui"] = Path.Combine(fontsFolder, "segoeui.ttf");
            _fontPathMap["segoe ui bold"] = Path.Combine(fontsFolder, "segoeuib.ttf");
            _fontPathMap["segoe ui italic"] = Path.Combine(fontsFolder, "segoeuii.ttf");
            _fontPathMap["segoe ui bold italic"] = Path.Combine(fontsFolder, "segoeuiz.ttf");
            
            _fontPathMap["tahoma"] = Path.Combine(fontsFolder, "tahoma.ttf");
            _fontPathMap["tahoma bold"] = Path.Combine(fontsFolder, "tahomabd.ttf");
            
            _fontPathMap["consolas"] = Path.Combine(fontsFolder, "consola.ttf");
            _fontPathMap["consolas bold"] = Path.Combine(fontsFolder, "consolab.ttf");
            _fontPathMap["consolas italic"] = Path.Combine(fontsFolder, "consolai.ttf");
            _fontPathMap["consolas bold italic"] = Path.Combine(fontsFolder, "consolaz.ttf");
            
            // Scan all font files and add them to the map
            try
            {
                string[] fontFiles = Directory.GetFiles(fontsFolder, "*.ttf")
                    .Concat(Directory.GetFiles(fontsFolder, "*.otf"))
                    .Concat(Directory.GetFiles(fontsFolder, "*.ttc"))
                    .ToArray();
                
                foreach (string fontFile in fontFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(fontFile).ToLower();
                    if (!_fontPathMap.ContainsKey(fileName))
                    {
                        _fontPathMap[fileName] = fontFile;
                    }
                }
                
                Debug.WriteLine($"Initialized font map with {_fontPathMap.Count} entries");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error scanning font files: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves the font name and returns the font data.
        /// </summary>
        public byte[] GetFont(string faceName)
        {
            try
            {
                // Check if font is already in cache
                if (_fontCache.TryGetValue(faceName, out byte[] fontData))
                {
                    return fontData;
                }

                // Try to find the font path
                string fontPath = GetFontPath(faceName);
                if (File.Exists(fontPath))
                {
                    fontData = File.ReadAllBytes(fontPath);
                    _fontCache[faceName] = fontData;
                    Debug.WriteLine($"Font loaded: {faceName} from {fontPath}");
                    return fontData;
                }

                // Fallback to Arial if the font is not found
                Debug.WriteLine($"Font not found: {faceName}, falling back to Arial");
                fontPath = GetFontPath("Arial");
                if (File.Exists(fontPath))
                {
                    fontData = File.ReadAllBytes(fontPath);
                    _fontCache[faceName] = fontData;
                    return fontData;
                }

                // If Arial is not found, try to find any font
                Debug.WriteLine("Arial not found, searching for any available font");
                string fontsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts));
                if (Directory.Exists(fontsFolder))
                {
                    string[] fontFiles = Directory.GetFiles(fontsFolder, "*.ttf");
                    if (fontFiles.Length > 0)
                    {
                        fontData = File.ReadAllBytes(fontFiles[0]);
                        _fontCache[faceName] = fontData;
                        Debug.WriteLine($"Using fallback font: {fontFiles[0]}");
                        return fontData;
                    }
                }

                // If no font is found, throw an exception
                throw new FileNotFoundException($"Font '{faceName}' not found and no fallback font is available.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting font {faceName}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Gets the path to the font file.
        /// </summary>
        private string GetFontPath(string faceName)
        {
            // Try to get the font path from the map
            string normalizedName = faceName.ToLower();
            if (_fontPathMap.TryGetValue(normalizedName, out string fontPath))
            {
                return fontPath;
            }
            
            // Try to find a partial match
            foreach (var entry in _fontPathMap)
            {
                if (entry.Key.Contains(normalizedName) || normalizedName.Contains(entry.Key))
                {
                    Debug.WriteLine($"Found partial match for {faceName}: {entry.Key}");
                    return entry.Value;
                }
            }
            
            // If not found, return Arial as fallback
            Debug.WriteLine($"No match found for {faceName}, using Arial");
            return _fontPathMap.TryGetValue("arial", out string arialPath) 
                ? arialPath 
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        }

        /// <summary>
        /// Resolves the font name and returns font information.
        /// </summary>
        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            try
            {
                // Normalize the family name
                string normalizedName = familyName.ToLower();
                
                // Try to find the font with style
                string styleSuffix = "";
                if (isBold && isItalic)
                    styleSuffix = " bold italic";
                else if (isBold)
                    styleSuffix = " bold";
                else if (isItalic)
                    styleSuffix = " italic";
                
                string fullName = normalizedName + styleSuffix;
                
                // Check if we have this exact font
                if (_fontPathMap.ContainsKey(fullName))
                {
                    Debug.WriteLine($"Resolved {familyName} (Bold={isBold}, Italic={isItalic}) to {fullName}");
                    return new FontResolverInfo(fullName);
                }
                
                // If not, use the base name and let PdfSharp handle the styling
                Debug.WriteLine($"Using base font for {familyName} (Bold={isBold}, Italic={isItalic})");
                string faceName = familyName;
                
                // Add style information to the face name for PdfSharp
                if (isBold && isItalic)
                    faceName += "-BoldItalic";
                else if (isBold)
                    faceName += "-Bold";
                else if (isItalic)
                    faceName += "-Italic";
                
                return new FontResolverInfo(faceName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error resolving typeface {familyName}: {ex.Message}");
                return new FontResolverInfo(familyName);
            }
        }
    }
} 