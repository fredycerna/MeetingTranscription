# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MeetingTranscription is a .NET 8.0 console application. The project is currently a minimal template with placeholder code.

## Development Commands

### Build
```bash
dotnet build
```

### Run
```bash
dotnet run --project MeetingTranscription/MeetingTranscription.csproj
```

### Clean
```bash
dotnet clean
```

### Restore dependencies
```bash
dotnet restore
```

## Project Configuration

- **Target Framework**: .NET 8.0
- **SDK Version**: 8.0.0 (configured in global.json with latestMinor roll-forward)
- **Nullable Reference Types**: Enabled
- **Implicit Usings**: Enabled

## Architecture

The project currently consists of a single entry point (Program.cs) in a standard .NET console application structure. The codebase follows the minimal hosting model for console applications introduced in .NET 6+.
