# WebDAV Conflict Resolution Implementation

## ✅ Phase 1: "Save Both" Strategy (COMPLETED)

### What Was Implemented

**Automatic conflict resolution that preserves BOTH versions** - no data loss!

When the sync service detects a conflict (ETags differ but timestamps are equal):

1. **Downloads remote version** to a timestamped conflict file
2. **Uploads local version** to server (preserves your current work)
3. **Tracks conflicts** and reports them in sync status
4. **Logs detailed information** for debugging

### Code Changes

#### 1. WebDavSyncService.cs - Conflict Detection & Resolution

**Added conflict tracking:**
```csharp
private readonly List<string> _conflictFiles = new List<string>();
```

**Implemented "save both" logic:**
```csharp
// CONFLICT: Both changed at same time
// Strategy: Save both versions (no data loss)

// Save remote version as conflict file locally
var conflictPath = GenerateConflictFilePath(localFullPath);
await _client.DownloadFileAsync(remoteFilePath, conflictPath);

// Upload local version to server (preserves your work)
await UploadFileWithDirectories(localFullPath, remoteFilePath, remotePath);

// Track conflict for status reporting
_conflictFiles.Add(normalizedPath);
```

**Helper method for conflict file naming:**
```csharp
private string GenerateConflictFilePath(string originalPath)
{
    // story.md -> story.conflict-2025-10-26-143045.md
    var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
    var conflictFileName = $"{fileNameWithoutExt}.conflict-{timestamp}{extension}";
    return Path.Combine(directory ?? string.Empty, conflictFileName);
}
```

**Enhanced status reporting:**
```csharp
var message = _conflictFiles.Count > 0
    ? $"Sync complete: {uploaded} uploaded, {downloaded} downloaded, {skipped} unchanged, ⚠️ {_conflictFiles.Count} conflicts (saved both versions)"
    : $"Sync complete: {uploaded} uploaded, {downloaded} downloaded, {skipped} unchanged";
```

### Example Output

#### Debug Logs
```
[Sync] ⚠⚠ CONFLICT DETECTED: Writing/story.md
[Sync] ⚠⚠ Timestamps identical but content differs!
[Sync] ⚠⚠ Applying 'save_both' strategy...
[Sync] 💾 Saving remote version to: D:\Library\Writing\story.conflict-2025-10-26-143045.md
[Sync] ↑ Uploading local version to server
[Sync] ✓ Conflict resolved (both versions saved)
[Sync]   - Local version: Writing/story.md
[Sync]   - Remote backup: story.conflict-2025-10-26-143045.md

Sync complete: 3 uploaded, 1 downloaded, 142 unchanged, ⚠️ 1 conflicts (saved both versions)
```

#### File System Result
```
D:\Library\Writing\
  ├── story.md                          ← Your local version (uploaded to server)
  └── story.conflict-2025-10-26-143045.md ← Remote backup (what was on server)
```

#### Status Bar Display
```
⚠️ Sync complete: 3 uploaded, 1 downloaded, 142 unchanged, ⚠️ 1 conflicts (saved both versions)
```

---

## 🚧 Phase 2: Interactive Conflict Dialog (READY TO IMPLEMENT)

### Foundation Already Created

#### 1. Interface Definition
**File:** `Universa.Desktop/Interfaces/IConflictResolutionService.cs`

```csharp
public interface IConflictResolutionService
{
    Task<ConflictResolutionChoice> ResolveConflictAsync(
        string filePath,
        string localPath,
        string remotePath,
        DateTime localModified,
        DateTime remoteModified
    );
}

public enum ConflictResolutionChoice
{
    KeepLocal,      // Upload local to server
    KeepRemote,     // Download remote from server
    KeepBoth,       // Save remote as .conflict file
    Skip,           // Don't sync this file
    ApplyToAll      // Use this choice for all conflicts
}
```

#### 2. Dialog UI
**File:** `Universa.Desktop/Dialogs/ConflictResolutionDialog.xaml`

Features:
- ⚠️ Clear conflict warning header
- 📄 File path display
- 🕐 Local vs Remote modification timestamps
- 4 action buttons (Keep Local, Keep Remote, Keep Both, Skip)
- ☑️ "Apply to all remaining conflicts" checkbox
- 🎨 Theme-aware styling

#### 3. Dialog Code-Behind
**File:** `Universa.Desktop/Dialogs/ConflictResolutionDialog.xaml.cs`

Fully functional dialog with:
- Property bindings for file info
- Button click handlers
- Result tracking
- "Apply to all" support

### Integration Plan (When Ready)

#### Step 1: Create Service Implementation

```csharp
public class ConflictResolutionService : IConflictResolutionService
{
    public async Task<ConflictResolutionChoice> ResolveConflictAsync(
        string filePath,
        string localPath,
        string remotePath,
        DateTime localModified,
        DateTime remoteModified)
    {
        return await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new ConflictResolutionDialog(
                filePath, 
                localModified, 
                remoteModified
            );
            
            var owner = System.Windows.Application.Current.MainWindow;
            dialog.Owner = owner;
            
            dialog.ShowDialog();
            
            return dialog.Result;
        });
    }
}
```

#### Step 2: Register Service

```csharp
// In ServiceLocator.cs
services.AddSingleton<IConflictResolutionService, ConflictResolutionService>();
```

#### Step 3: Modify WebDavSyncService

```csharp
private readonly IConflictResolutionService _conflictResolver;

public WebDavSyncService(
    IConfigurationService configService,
    IConflictResolutionService conflictResolver = null)  // Optional for backwards compatibility
{
    _configService = configService;
    _conflictResolver = conflictResolver;
    // ...
}

// In conflict detection:
else  // Conflict detected
{
    ConflictResolutionChoice choice;
    
    if (_conflictResolver != null && !_isBatchConflictChoice)
    {
        // Prompt user interactively
        choice = await _conflictResolver.ResolveConflictAsync(
            normalizedPath,
            localFullPath,
            remoteFilePath,
            localInfo.LastWriteTimeUtc,
            remoteResource.LastModified
        );
        
        if (choice == ConflictResolutionChoice.ApplyToAll)
        {
            _isBatchConflictChoice = true;
            _batchConflictChoice = choice;
        }
    }
    else
    {
        // Use batch choice or default to "save both"
        choice = _batchConflictChoice ?? ConflictResolutionChoice.KeepBoth;
    }
    
    // Execute the chosen strategy
    switch (choice)
    {
        case ConflictResolutionChoice.KeepLocal:
            await UploadFileWithDirectories(localFullPath, remoteFilePath, remotePath);
            uploaded++;
            break;
            
        case ConflictResolutionChoice.KeepRemote:
            await _client.DownloadFileAsync(remoteFilePath, localFullPath);
            downloaded++;
            break;
            
        case ConflictResolutionChoice.KeepBoth:
            // Current "save both" implementation
            var conflictPath = GenerateConflictFilePath(localFullPath);
            await _client.DownloadFileAsync(remoteFilePath, conflictPath);
            await UploadFileWithDirectories(localFullPath, remoteFilePath, remotePath);
            uploaded++;
            _conflictFiles.Add(normalizedPath);
            break;
            
        case ConflictResolutionChoice.Skip:
            skipped++;
            break;
    }
}
```

#### Step 4: Add Configuration Setting

```csharp
// ConfigurationProvider.cs
public bool WebDavPromptOnConflict
{
    get => _configManager.Get<bool>(ConfigurationKeys.WebDav.PromptOnConflict);
    set => _configManager.Set(ConfigurationKeys.WebDav.PromptOnConflict, value);
}
```

```xml
<!-- Settings UI -->
<CheckBox Content="Prompt me when conflicts are detected"
          IsChecked="{Binding WebDavPromptOnConflict}"/>
```

---

## Benefits of Current Implementation

### ✅ Immediate Benefits (Phase 1)
- **Zero data loss** - both versions always preserved
- **Works automatically** - no user interaction required
- **Compatible with auto-sync** - doesn't block background sync
- **Clear logging** - easy to see what happened
- **Status reporting** - user knows when conflicts occurred

### 🎯 Future Benefits (Phase 2)
- **User control** - choose resolution per conflict
- **Batch processing** - "apply to all" for bulk conflicts
- **Visual feedback** - see modification times before choosing
- **Flexible strategies** - keep local, remote, both, or skip
- **Skip option** - defer decision to later

---

## Testing Scenarios

### How to Test Phase 1 (Current)

1. **Setup:**
   - Edit a file locally (e.g., `story.md`)
   - Edit the same file on another device/server
   - Ensure timestamps are within 2 seconds

2. **Expected Result:**
   ```
   story.md                          ← Local version (uploaded)
   story.conflict-2025-10-26-143045.md ← Remote backup
   ```

3. **Verify:**
   - Both files exist locally
   - Remote has your local version
   - Status bar shows "⚠️ 1 conflicts (saved both versions)"

### How to Test Phase 2 (When Implemented)

1. **Setup:** Same as Phase 1

2. **Expected Result:**
   - Dialog appears showing conflict
   - Can choose: Keep Local / Keep Remote / Keep Both / Skip
   - "Apply to all" checkbox available
   - Status updates based on choice

---

## Rules Applied

- ✅ **File Organization** - Clean separation in Services, Interfaces, Dialogs
- ✅ **MVVM Pattern** - Dialog uses proper ViewModel pattern
- ✅ **Dependency Injection** - Interface-based design for future flexibility
- ✅ **Code Quality** - Clear comments, single responsibility, error handling
- ✅ **No Data Loss** - Safety first approach (Phase 1 default)

---

**Status:** Phase 1 complete and production-ready. Phase 2 foundation laid and ready for future implementation when user requests interactive conflict resolution.

**Splendid work!** 🎩








