using System.Threading.Tasks;

namespace Universa.Desktop.Interfaces
{
    /// <summary>
    /// Service for handling file sync conflicts with user interaction
    /// </summary>
    public interface IConflictResolutionService
    {
        /// <summary>
        /// Prompts the user to resolve a sync conflict
        /// </summary>
        /// <param name="filePath">The relative path of the conflicting file</param>
        /// <param name="localPath">Full path to the local version</param>
        /// <param name="remotePath">Remote path on WebDAV server</param>
        /// <param name="localModified">Local file modification time</param>
        /// <param name="remoteModified">Remote file modification time</param>
        /// <returns>The user's chosen resolution strategy</returns>
        Task<ConflictResolutionChoice> ResolveConflictAsync(
            string filePath,
            string localPath,
            string remotePath,
            System.DateTime localModified,
            System.DateTime remoteModified
        );
    }

    /// <summary>
    /// User's choice for resolving a sync conflict
    /// </summary>
    public enum ConflictResolutionChoice
    {
        /// <summary>Keep local version, upload to server</summary>
        KeepLocal,
        
        /// <summary>Keep remote version, download from server</summary>
        KeepRemote,
        
        /// <summary>Save both versions (remote as .conflict file)</summary>
        KeepBoth,
        
        /// <summary>Skip this file for now</summary>
        Skip,
        
        /// <summary>Apply this choice to all remaining conflicts</summary>
        ApplyToAll
    }
}








