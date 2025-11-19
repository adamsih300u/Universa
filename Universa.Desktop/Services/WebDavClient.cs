using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Represents a file or directory on the WebDAV server
    /// </summary>
    public class WebDavResource
    {
        public string Path { get; set; }
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
        public string ETag { get; set; }
    }

    /// <summary>
    /// Low-level WebDAV client for communicating with WebDAV servers
    /// </summary>
    public class WebDavClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _username;
        private readonly string _password;
        private bool _disposed;

        public WebDavClient(string baseUrl, string username, string password)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentNullException(nameof(baseUrl));

            _baseUrl = baseUrl.TrimEnd('/');
            _username = username;
            _password = password;

            var handler = new HttpClientHandler
            {
                PreAuthenticate = true,
                Credentials = new NetworkCredential(username, password)
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(5)
            };

            // Set basic auth header
            if (!string.IsNullOrEmpty(username))
            {
                var authBytes = Encoding.ASCII.GetBytes($"{username}:{password}");
                var authHeader = Convert.ToBase64String(authBytes);
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);
            }
        }

        /// <summary>
        /// Tests if the WebDAV server is accessible
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Options, _baseUrl);
                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Lists files and directories at the specified path using PROPFIND (non-recursive, depth 1)
        /// </summary>
        public async Task<List<WebDavResource>> ListDirectoryAsync(string path)
        {
            var url = $"{_baseUrl}/{path.TrimStart('/')}";
            var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
            
            // Request properties we care about
            var propfindXml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<D:propfind xmlns:D=""DAV:"">
  <D:prop>
    <D:resourcetype/>
    <D:getcontentlength/>
    <D:getlastmodified/>
    <D:getetag/>
  </D:prop>
</D:propfind>";

            request.Content = new StringContent(propfindXml, Encoding.UTF8, "application/xml");
            request.Headers.Add("Depth", "1");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            return ParsePropfindResponse(responseContent, url);
        }

        /// <summary>
        /// Recursively lists all files in the given remote path and all subdirectories
        /// </summary>
        public async Task<List<WebDavResource>> ListDirectoryRecursiveAsync(string remotePath)
        {
            var allResources = new List<WebDavResource>();
            await ListDirectoryRecursiveInternalAsync(remotePath, allResources);
            return allResources;
        }

        private async Task ListDirectoryRecursiveInternalAsync(string remotePath, List<WebDavResource> allResources)
        {
            // Normalize the path to prevent double slashes and ensure consistency
            var normalizedPath = NormalizeRemotePath(remotePath);
            
            System.Diagnostics.Debug.WriteLine($"[WebDAV] Scanning directory: {normalizedPath}");
            
            // Get items in current directory
            var resources = await ListDirectoryAsync(normalizedPath);
            
            System.Diagnostics.Debug.WriteLine($"[WebDAV] Found {resources.Count} items in {normalizedPath}");
            
            foreach (var resource in resources)
            {
                System.Diagnostics.Debug.WriteLine($"[WebDAV]   - {(resource.IsDirectory ? "DIR " : "FILE")}: {resource.Path}");
                
                // Normalize resource path
                var normalizedResourcePath = NormalizeRemotePath(resource.Path);
                
                // Skip if this is the same as the directory we're scanning (parent directory)
                if (normalizedResourcePath == normalizedPath)
                {
                    System.Diagnostics.Debug.WriteLine($"[WebDAV]   SKIP: Parent directory detected");
                    continue;
                }
                
                if (resource.IsDirectory)
                {
                    // Recursively traverse subdirectories
                    await ListDirectoryRecursiveInternalAsync(normalizedResourcePath, allResources);
                }
                else
                {
                    // Add files to the result (with normalized path)
                    allResources.Add(new WebDavResource
                    {
                        Path = normalizedResourcePath,
                        IsDirectory = resource.IsDirectory,
                        Size = resource.Size,
                        LastModified = resource.LastModified,
                        ETag = resource.ETag
                    });
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"[WebDAV] Completed scanning: {normalizedPath}");
        }
        
        /// <summary>
        /// Normalizes a remote path by removing leading/trailing slashes and collapsing multiple slashes
        /// </summary>
        private string NormalizeRemotePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
                
            // Replace multiple slashes with single slash
            while (path.Contains("//"))
                path = path.Replace("//", "/");
                
            // Remove leading and trailing slashes
            return path.Trim('/');
        }

        /// <summary>
        /// Downloads a file from the WebDAV server
        /// </summary>
        public async Task<byte[]> DownloadFileAsync(string remotePath)
        {
            var url = $"{_baseUrl}/{remotePath.TrimStart('/')}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }

        /// <summary>
        /// Downloads a file directly to a local path
        /// </summary>
        public async Task DownloadFileAsync(string remotePath, string localPath)
        {
            var data = await DownloadFileAsync(remotePath);
            var directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            await File.WriteAllBytesAsync(localPath, data);
        }

        /// <summary>
        /// Uploads a file to the WebDAV server using PUT
        /// </summary>
        public async Task UploadFileAsync(string localPath, string remotePath)
        {
            if (!File.Exists(localPath))
                throw new FileNotFoundException("Local file not found", localPath);

            // URL encode each path segment separately to handle spaces, special chars, etc.
            var pathSegments = remotePath.TrimStart('/').Split('/');
            var encodedSegments = pathSegments.Select(s => Uri.EscapeDataString(s));
            var encodedPath = string.Join("/", encodedSegments);
            var url = $"{_baseUrl}/{encodedPath}";
            
            var fileInfo = new FileInfo(localPath);
            System.Diagnostics.Debug.WriteLine($"[WebDAV] Starting upload: {localPath} -> {remotePath} ({fileInfo.Length:N0} bytes)");
            System.Diagnostics.Debug.WriteLine($"[WebDAV] Encoded URL: {url}");
            
            try
            {
                using var fileStream = File.OpenRead(localPath);
                using var content = new StreamContent(fileStream);
                
                // Set content length explicitly to help with large files
                content.Headers.ContentLength = fileStream.Length;
                
                var request = new HttpRequestMessage(HttpMethod.Put, url)
                {
                    Content = content
                };

                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                
                System.Diagnostics.Debug.WriteLine($"[WebDAV] Upload complete: {remotePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebDAV] Upload error for {localPath}: {ex.GetType().Name} - {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Uploads file content from bytes
        /// </summary>
        public async Task UploadFileAsync(byte[] data, string remotePath)
        {
            var url = $"{_baseUrl}/{remotePath.TrimStart('/')}";
            using var content = new ByteArrayContent(data);
            
            var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = content
            };

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Creates a directory on the WebDAV server using MKCOL
        /// </summary>
        public async Task CreateDirectoryAsync(string remotePath)
        {
            // URL encode each path segment separately to handle spaces, special chars, etc.
            var pathSegments = remotePath.TrimStart('/').Split('/');
            var encodedSegments = pathSegments.Select(s => Uri.EscapeDataString(s));
            var encodedPath = string.Join("/", encodedSegments);
            var url = $"{_baseUrl}/{encodedPath}";
            
            System.Diagnostics.Debug.WriteLine($"[WebDAV] MKCOL: {remotePath} -> {url}");
            var request = new HttpRequestMessage(new HttpMethod("MKCOL"), url);
            
            var response = await _httpClient.SendAsync(request);
            
            // A 405 Method Not Allowed is the expected response if the directory already exists.
            // We can safely ignore it and consider the operation a success.
            if (response.StatusCode == HttpStatusCode.MethodNotAllowed)
            {
                System.Diagnostics.Debug.WriteLine($"[WebDAV] MKCOL 405: Directory already exists");
                return;
            }
                
            // For all other non-success codes, throw an exception.
            response.EnsureSuccessStatusCode();
            System.Diagnostics.Debug.WriteLine($"[WebDAV] MKCOL success: {response.StatusCode}");
        }

        /// <summary>
        /// Deletes a file or directory on the WebDAV server
        /// </summary>
        public async Task DeleteAsync(string remotePath)
        {
            var url = $"{_baseUrl}/{remotePath.TrimStart('/')}";
            var response = await _httpClient.DeleteAsync(url);
            
            // 404 means already deleted
            if (response.StatusCode == HttpStatusCode.NotFound)
                return;
                
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Moves/renames a file or directory using MOVE
        /// </summary>
        public async Task MoveAsync(string sourcePath, string destinationPath)
        {
            var sourceUrl = $"{_baseUrl}/{sourcePath.TrimStart('/')}";
            var destUrl = $"{_baseUrl}/{destinationPath.TrimStart('/')}";
            
            var request = new HttpRequestMessage(new HttpMethod("MOVE"), sourceUrl);
            request.Headers.Add("Destination", destUrl);
            request.Headers.Add("Overwrite", "T");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Gets properties of a specific resource
        /// </summary>
        public async Task<WebDavResource> GetResourceInfoAsync(string remotePath)
        {
            var url = $"{_baseUrl}/{remotePath.TrimStart('/')}";
            var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
            
            var propfindXml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<D:propfind xmlns:D=""DAV:"">
  <D:prop>
    <D:resourcetype/>
    <D:getcontentlength/>
    <D:getlastmodified/>
    <D:getetag/>
  </D:prop>
</D:propfind>";

            request.Content = new StringContent(propfindXml, Encoding.UTF8, "application/xml");
            request.Headers.Add("Depth", "0");

            var response = await _httpClient.SendAsync(request);
            
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
                
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var resources = ParsePropfindResponse(responseContent, url);
            return resources.FirstOrDefault();
        }

        /// <summary>
        /// Checks if a resource exists on the server
        /// </summary>
        public async Task<bool> ExistsAsync(string remotePath)
        {
            var resource = await GetResourceInfoAsync(remotePath);
            return resource != null;
        }

        private List<WebDavResource> ParsePropfindResponse(string xml, string requestUrl)
        {
            var resources = new List<WebDavResource>();
            
            try
            {
                XNamespace d = "DAV:";
                var doc = XDocument.Parse(xml);
                var responses = doc.Descendants(d + "response");

                foreach (var response in responses)
                {
                    var href = response.Element(d + "href")?.Value;
                    if (string.IsNullOrEmpty(href))
                        continue;

                    // Decode the href early
                    var decodedHref = Uri.UnescapeDataString(href);
                    
                    // Skip the parent directory in listings
                    // Compare paths directly instead of building full URLs
                    var baseUrlPath = new Uri(_baseUrl).AbsolutePath.TrimEnd('/');
                    var requestUrlPath = new Uri(requestUrl).AbsolutePath.TrimEnd('/');
                    var hrefPath = decodedHref.TrimEnd('/');
                    
                    // If href matches the request URL path, skip it (parent directory)
                    if (hrefPath == requestUrlPath || hrefPath == baseUrlPath)
                    {
                        System.Diagnostics.Debug.WriteLine($"[WebDAV] Skipping parent dir: {href}");
                        continue;
                    }

                    var propstat = response.Element(d + "propstat");
                    var prop = propstat?.Element(d + "prop");
                    
                    if (prop == null)
                        continue;

                    var resourceType = prop.Element(d + "resourcetype");
                    var isDirectory = resourceType?.Element(d + "collection") != null;

                    var contentLength = prop.Element(d + "getcontentlength")?.Value;
                    var lastModified = prop.Element(d + "getlastmodified")?.Value;
                    var etag = prop.Element(d + "getetag")?.Value;

                    // Make path relative to base URL
                    var relativePath = decodedHref;
                    if (decodedHref.StartsWith("/"))
                    {
                        var basePath = new Uri(_baseUrl).AbsolutePath.TrimEnd('/');
                        if (decodedHref.StartsWith(basePath + "/"))
                        {
                            relativePath = decodedHref.Substring(basePath.Length + 1);
                        }
                        else if (decodedHref == basePath)
                        {
                            relativePath = string.Empty;
                        }
                    }

                    resources.Add(new WebDavResource
                    {
                        Path = relativePath,
                        IsDirectory = isDirectory,
                        Size = long.TryParse(contentLength, out var size) ? size : 0,
                        LastModified = DateTime.TryParse(lastModified, out var dt) ? dt : DateTime.MinValue,
                        ETag = etag?.Trim('"')
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing PROPFIND response: {ex.Message}");
                throw;
            }

            return resources;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient?.Dispose();
                _disposed = true;
            }
        }
    }
}

