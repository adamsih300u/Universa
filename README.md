# Universa

Universa is a comprehensive desktop application built with C# and WPF that combines document editing, project management, and media capabilities in a single integrated environment.

## Features

- **Markdown Editor**: Create and edit documents with full markdown support
- **Project Management**: Organize and track projects with tasks and dependencies
- **Export Capabilities**: Export documents to various formats including ePub
- **Media Integration**: Play and manage media files
- **Chat Integration**: Connect with Matrix and other chat services

## Getting Started

### Prerequisites

- Windows 10 or later
- .NET 8.0 or later
- Visual Studio 2022 or later (for development)

### Installation

1. Clone the repository
   ```
   git clone https://github.com/adamsih300u/Universa.git
   ```

2. Open the solution in Visual Studio
   ```
   cd Universa
   start Universa.sln
   ```

3. Build and run the application

## Development

The project is structured as follows:

- **Universa.Desktop**: Main WPF application
- **Universa.Web**: Web components (if applicable)
- **Docs**: Documentation

## Creating Releases

Universa uses GitHub Actions to automatically build and publish releases. To create a new release:

1. Ensure all changes are committed and pushed to the main branch
2. Create and push a new tag with a version number:
   ```
   git tag v1.0.0
   git push origin v1.0.0
   ```
3. GitHub Actions will automatically:
   - Build the application
   - Package it as a self-contained executable
   - Create a ZIP archive
   - Publish a new release on GitHub with the ZIP attached

The release will be available for download on the [Releases page](https://github.com/adamsih300u/Universa/releases).

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Acknowledgments

- All contributors to the project
- Open source libraries used in the project 