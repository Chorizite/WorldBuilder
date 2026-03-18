# AC WorldBuilder

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/Chorizite/WorldBuilder)

AC WorldBuilder is a cross-platform, standalone desktop application designed for creating and editing dat content for Asheron's Call emulator servers / clients.

## Installation

- Grab the latest installer from the [Releases](https://github.com/Chorizite/WorldBuilder/releases) page.

## Development

### Platform-Specific Builds
The application supports multiple platforms:
- **Windows**: `dotnet run --project WorldBuilder.Windows`
- **Linux**: `dotnet run --project WorldBuilder.Linux`
- **Mac**: `dotnet run --project WorldBuilder.Mac`

### Testing

The project includes a comprehensive test suite:
- **Unit Tests**: Located in `WorldBuilder.Tests` and `WorldBuilder.Shared.Tests`
- **Running Tests**: Execute `dotnet test` from the root directory to run all tests


### Project Structure

- **WorldBuilder**: The main cross-platform application using Avalonia.
- **WorldBuilder.Windows**: Windows-specific application entry point.
- **WorldBuilder.Linux**: Linux-specific application entry point.
- **WorldBuilder.Mac**: Mac-specific application entry point.
- **WorldBuilder.Server**: Server components for collaborative features.
- **WorldBuilder.Shared**: Core data models, .dat parsers, rendering logic, and shared utilities.
- **WorldBuilder.Shared.Tests**: Unit tests for the shared library components.
- **Chorizite.OpenGLSDLBackend**: Low-level rendering backend implementation.

## Contributing

We welcome contributions! Please see our [Contributing Guidelines](https://github.com/Chorizite/WorldBuilder/wiki/Contributing) for details.

## MVP Specification

For our current roadmap and design goals, refer to the [MVP Specification](https://github.com/Chorizite/WorldBuilder/wiki/MVP).

## License

This project is licensed under the MIT License.
