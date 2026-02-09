# AC WorldBuilder

AC WorldBuilder is a cross-platform, standalone desktop application designed for creating and editing dat content for Asheron's Call emulator servers / clients.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Installation

- Grab the latest installer from the [Releases](https://github.com/Chorizite/WorldBuilder/releases) page.

## Build and Run

### Desktop Application
To build and run the desktop application:

```bash
dotnet run --project WorldBuilder.Desktop
```

### Running Tests
To run the unit test suite:

```bash
dotnet test
```

## Project Structure

- **WorldBuilder.Desktop**: The main entry point for the desktop application using Avalonia.
- **WorldBuilder**: Contains the core application logic, UI views, and ViewModels.
- **WorldBuilder.Shared**: Core data models, .dat parsers, rendering logic, and shared utilities.
- **WorldBuilder.Shared.Tests**: Unit tests for the shared library components.
- **Chorizite.OpenGLSDLBackend**: Low-level rendering backend implementation.

## Contributing

We welcome contributions! Please see our [Contributing Guidelines](https://github.com/Chorizite/WorldBuilder/wiki/Contributing) for details.

## MVP Specification

For our current roadmap and design goals, refer to the [MVP Specification](https://github.com/Chorizite/WorldBuilder/wiki/MVP).
