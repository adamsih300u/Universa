# Universa - Comprehensive User Guide

Universa is a comprehensive desktop application built with C# and WPF that combines document editing, project management, AI integration, media management, and chat capabilities in a single unified environment. This guide provides detailed information about all features and how to use them.

## Table of Contents

- [Download & Installation](#download--installation)
- [Core Features Overview](#core-features-overview)
- [Tab Types & UI Elements](#tab-types--ui-elements)
- [Configuration Options](#configuration-options)
- [AI Integration](#ai-integration)
- [Media Management](#media-management)
- [Chat Integration](#chat-integration)
- [Project Management](#project-management)
- [File Management & Sync](#file-management--sync)
- [Text-to-Speech (TTS)](#text-to-speech-tts)
- [Themes & Customization](#themes--customization)
- [Advanced Features](#advanced-features)
- [Troubleshooting](#troubleshooting)

## Download & Installation

### System Requirements
- Windows 10 (version 1903 or later) or Windows 11
- 4GB RAM minimum (8GB recommended)
- 500MB free disk space
- .NET 8.0 Runtime (included in portable version)

### Portable Edition
1. Download `Universa-Desktop-X.X.X.exe` from the [Releases](https://github.com/adamsih300u/Universa/releases) page
2. Save to any location and double-click to run
3. No installation required - fully self-contained

### Development Setup
1. Clone the repository: `git clone https://github.com/adamsih300u/Universa.git`
2. Open `Universa.sln` in Visual Studio
3. Build and run the application

## Core Features Overview

Universa provides multiple integrated modules:

- **Document Editing**: Full markdown support with live preview and formatting tools
- **Project Management**: GTD-based task management with dependencies and tracking
- **AI Integration**: Multiple AI providers for writing assistance and chat
- **Media Management**: Integration with Jellyfin, Subsonic, and Audiobookshelf
- **Chat Services**: Matrix client for secure messaging
- **RSS Reader**: Feed aggregation and reading
- **File Synchronization**: Cloud sync with Universa.Web backend
- **Text-to-Speech**: Convert text to audio with customizable voices

## Tab Types & UI Elements

### Main Interface Layout

The main window consists of:
- **Tab Bar**: Horizontal tabs at the top for different documents/features
- **Left Sidebar**: Expandable library navigator (when enabled)
- **Content Area**: Main workspace for the active tab
- **Right Sidebar**: Expandable chat panel (when enabled)
- **Status Bar**: Shows current file info, sync status, and theme

### Tab Types

#### 1. Overview Tab
**Purpose**: Central dashboard showing all projects and todos
**UI Elements**:
- Search box for filtering items
- Project list with status indicators
- Todo list with completion checkboxes
- AI Analysis button for project insights
- Quick navigation to project/todo files

#### 2. Markdown Tab
**Purpose**: Advanced markdown editor with live formatting
**UI Elements**:
- Rich text editor with syntax highlighting
- Formatting toolbar (Bold, Italic, Headers H1-H4)
- Font and font size selectors
- Search panel with regex support
- Chapter navigation for long documents
- TTS controls for audio playback
- Frontmatter editor for metadata
- Version history dropdown

**Features**:
- Real-time search with highlighting
- Chapter-based navigation
- Document metadata management
- File versioning support
- Export capabilities

#### 3. Project Tab
**Purpose**: GTD-based project management
**UI Elements**:
- Project title and description fields
- Goal definition area
- Task hierarchy with drag-and-drop reordering
- Dependency management (hard/soft dependencies)
- Reference file attachments
- Project log for tracking progress
- Status selector (Not Started, Started, Deferred, Completed)
- Category assignment

**Task Management**:
- Nested subtasks with unlimited depth
- Task dependencies between projects
- Completion tracking with automatic rollup
- Archive functionality for completed projects

#### 4. Chat Tab
**Purpose**: Matrix client integration for secure messaging
**UI Elements**:
- Room list with unread indicators
- Message history with rich content
- Text input with emoji support
- Room search and filtering
- User verification system
- End-to-end encryption support

#### 5. Media Tab
**Purpose**: Media library management and playback
**UI Elements**:
- Navigation tree for media sources
- Content grid with cover art
- Playback controls
- Media information panel
- Progress tracking for videos/audiobooks

**Supported Services**:
- **Jellyfin**: Personal media server
- **Subsonic**: Music streaming protocol
- **Audiobookshelf**: Audiobook and podcast server

#### 6. RSS Tab
**Purpose**: Feed aggregation and article reading
**UI Elements**:
- Feed tree with unread counts
- Article list view
- Reading view with HTML rendering
- Mark as read functionality
- Category organization

#### 7. Inbox Tab
**Purpose**: GTD inbox for quick capture and processing
**UI Elements**:
- Quick add text box
- Inbox items list
- Processing buttons (Convert to Project/Todo)
- Delete/archive options

#### 8. Editor Tab
**Purpose**: Plain text editor for any file type
**UI Elements**:
- Basic text editor
- Search functionality
- TTS support
- Save/Save As functionality

#### 9. Folder Tab
**Purpose**: File system browser
**UI Elements**:
- Directory tree view
- File list with icons
- Double-click to open files
- Refresh functionality

#### 10. Aggregated Todos Tab
**Purpose**: Unified view of all todo items across files
**UI Elements**:
- Combined todo list from all files
- Filtering and search
- Completion tracking
- Quick navigation to source files

## Configuration Options

Configuration is stored in `config.json` in the application directory. Key sections include:

### AI Provider Settings
```json
{
  "EnableOpenAI": false,
  "OpenAIApiKey": "",
  "EnableAnthropic": false,
  "AnthropicApiKey": "",
  "EnableXAI": false,
  "XAIApiKey": "",
  "EnableOllama": false,
  "OllamaUrl": "http://localhost:11434",
  "OllamaModel": "llama2",
  "DefaultAIProvider": 0,
  "UseBetaChains": false
}
```

### Theme Configuration
```json
{
  "CurrentTheme": "Default",
  "DarkModePlayingColor": "#FF4CAF50",
  "LightModePlayingColor": "#FF4CAF50",
  "DarkModeTextColor": "#FFFFFFFF",
  "LightModeTextColor": "#FF000000"
}
```

### Media Service Settings
```json
{
  "SubsonicUrl": null,
  "SubsonicUsername": null,
  "SubsonicPassword": null,
  "JellyfinUrl": null,
  "JellyfinUsername": null,
  "JellyfinPassword": null,
  "AudiobookshelfUrl": null,
  "AudiobookshelfUsername": null,
  "AudiobookshelfPassword": null
}
```

### Sync Configuration
```json
{
  "SyncServerUrl": "",
  "SyncUsername": "",
  "SyncPassword": "",
  "AutoSync": false,
  "SyncIntervalMinutes": 5
}
```

### TTS Settings
```json
{
  "EnableTTS": false,
  "TTSApiUrl": null,
  "TTSVoice": null,
  "TTSAvailableVoices": []
}
```

## AI Integration

Universa supports multiple AI providers for writing assistance and chat functionality:

### Supported Providers
1. **OpenAI**: GPT models for general assistance
2. **Anthropic**: Claude models for advanced reasoning
3. **xAI**: Grok models for creative tasks
4. **Ollama**: Local AI models for privacy

### AI Chat Sidebar
- Access via right sidebar toggle
- Context-aware conversations
- Document analysis capabilities
- Writing assistance and suggestions
- Code review and improvements

### Beta Chains Feature
Advanced AI workflows for specialized tasks:
- Document analysis chains
- Project planning assistance
- Writing improvement suggestions

## Media Management

### Jellyfin Integration
- Stream movies, TV shows, and music
- Resume playback across devices
- Metadata and artwork display
- User ratings and favorites

### Subsonic Protocol
- Compatible with Subsonic-API servers
- Music streaming and playlist management
- Album and artist browsing
- Cached metadata for offline browsing

### Audiobookshelf Support
- Audiobook and podcast streaming
- Progress tracking and bookmarks
- Series and collection management

### Media Controls
- System-wide media key integration
- Volume control and playback state
- Album art and metadata display

## Chat Integration

### Matrix Client
- End-to-end encrypted messaging
- Room management and discovery
- Device verification for security
- Emoji reactions and rich content
- File sharing capabilities

### Chat Features
- Real-time message synchronization
- Offline message queuing
- Multi-device support
- Push notifications

## Project Management

### GTD Methodology
Universa implements Getting Things Done principles:

1. **Capture**: Inbox for quick idea entry
2. **Clarify**: Process inbox items into projects/todos
3. **Organize**: Categorize and prioritize tasks
4. **Review**: Regular project and task review
5. **Engage**: Context-based task execution

### Project Structure
- **Goals**: Define project outcomes
- **Tasks**: Break down work into actionable items
- **Dependencies**: Track task relationships
- **References**: Attach relevant files
- **Logs**: Document progress and decisions

### Task Management Features
- Hierarchical task organization
- Completion tracking with rollup
- Due date management
- Priority indicators
- Drag-and-drop reordering

## File Management & Sync

### Local File Storage
- Documents stored in configurable library path
- Automatic file versioning
- Backup and recovery capabilities

### Universa.Web Sync
- Real-time file synchronization
- Conflict resolution
- Multi-device access
- Secure cloud storage

### File Versioning
- Automatic version creation on save
- Version history browsing
- Point-in-time recovery
- Storage optimization

## Text-to-Speech (TTS)

### Features
- Convert any text to speech
- Multiple voice options
- Sentence-by-sentence playback
- Pause and resume functionality
- Custom voice API integration

### Usage
- Click TTS button in any text editor
- Select text for partial reading
- Automatic sentence detection
- Audio progress tracking

## Themes & Customization

### Theme System
- Light and dark mode support
- Custom color schemes
- Per-element color customization
- Theme import/export

### Customizable Elements
- Tab colors (active/inactive)
- Text and background colors
- Media player indicators
- Button and control styling

### Theme Configuration
Themes are stored as JSON files with complete color definitions for all UI elements.

## Advanced Features

### File Search
- Global search across all documents
- Regex pattern support
- Context-aware results
- Quick navigation to matches

### Keyboard Shortcuts
- Ctrl+N: New tab
- Ctrl+O: Open file
- Ctrl+S: Save current file
- Ctrl+F: Find in document
- Ctrl+T: Toggle TTS

### Export Capabilities
- Markdown to HTML
- Project reports
- Todo lists
- Custom format support

### Plugin Architecture
- Custom tab implementations
- Service integrations
- Theme extensions

## Troubleshooting

### Common Issues

#### AI Integration Not Working
- Verify API keys in configuration
- Check network connectivity
- Ensure selected provider is enabled

#### Media Playback Issues
- Confirm server URLs and credentials
- Check firewall settings
- Verify media format support

#### Sync Problems
- Validate Universa.Web server connection
- Check authentication credentials
- Review conflict resolution settings

#### Performance Issues
- Clear cache directory
- Reduce sync frequency
- Optimize library organization

### Log Files
Application logs are stored in `%AppData%/Universa/Logs/` for debugging and support.

### Support
For issues and feature requests, use the GitHub repository's issue tracker.

## Development & Contribution

### Architecture
- **WPF Frontend**: C# with MVVM pattern
- **Go Backend**: Universa.Web sync server
- **SQLite**: Local data storage
- **JSON**: Configuration and document storage

### Building from Source
1. Install Visual Studio 2022
2. Clone repository
3. Restore NuGet packages
4. Build solution

### Contributing
1. Fork the repository
2. Create feature branch
3. Implement changes with tests
4. Submit pull request

## License

Universa is licensed under the MIT License. See LICENSE file for details.

---

*This documentation covers Universa version 1.0+. Features may vary by version.*
