# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MeetingTranscription is a .NET 8.0 console application that transcribes audio recordings using OpenAI's Whisper API (`whisper-1`) and analyzes them using `gpt-4o-mini` to extract summaries, key points, technical concepts, and action items. All output is in Spanish.

## Development Commands

```bash
# Build
dotnet build

# Run with audio file (from repo root)
dotnet run --project MeetingTranscription -- path/to/audio.mp3

# Run from project directory
cd MeetingTranscription && dotnet run -- path/to/audio.mp3

# Clean
dotnet clean
```

Supported audio formats: MP3, M4A, WAV, and any format supported by OpenAI Whisper API.

## Configuration

- **OPENAI_API_KEY** (required): Environment variable for OpenAI API access
- **ffmpeg** (optional): If available, normalizes audio to 16kHz mono WAV for better transcription
- **ffmpeg/ffprobe** (required for large files): Needed to segment files >20 MB into chunks

## Output Files

For each processed audio file, three output files are generated in the same directory as the input:
- `{filename}_analysis.json` - Structured JSON with full analysis
- `{filename}_analysis.md` - Human-readable Markdown summary
- `{filename}_transcript.txt` - Raw transcription text

## Architecture

Single-file console application (`MeetingTranscription/Program.cs`) with a linear processing pipeline:

```
Main → TestConnectivity → NormalizeAudioIfPossible → TranscribeAudio → AnalyzeTranscription → Save outputs
```

### Processing Pipeline

1. **Pre-flight Checks**: Validates connectivity before processing
   - `TestConnectivity`: OpenAI API access via /v1/models
   - `TestMultipartUpload`: Multipart form upload to httpbin.org
   - `TestWhisperSmallFile`: Whisper API test with `/tmp/test_small.wav` (optional test file)
2. **Audio Normalization** (`NormalizeAudioIfPossible`): ffmpeg converts to 16kHz mono WAV
3. **Transcription** (`TranscribeAudio`): Routes to `TranscribeSingleFile` or `TranscribeLargeFile` based on 20 MB threshold
4. **Analysis** (`AnalyzeTranscription`): GPT-4o-mini with structured JSON response format
5. **Output Generation**: Saves JSON, Markdown, and plain text files

### Large File Handling

Files >20 MB (after WAV normalization) are automatically segmented and transcribed in chunks:
- `TranscribeLargeFile`: Uses ffprobe for duration, ffmpeg for 5-minute (300s) segment extraction
- 10-second pause between segments to avoid API throttling
- 3 retry attempts with exponential backoff (5s × attempt number) for transient failures

### Data Models

C# records at the top of `Program.cs`:
- `MeetingAnalysis`: Main output structure (summary, key_points, technical_concepts, action_items)
- `TechnicalConcept`: Term with context and related technologies
- `ActionItem`: Task with owner, due_date, priority, source_times
- OpenAI API types: `TranscriptionResponse`, `ChatCompletionRequest/Response`, `ChatMessage`, `ResponseFormat`

### Key Implementation Constraints

- **Zero external NuGet packages**: Uses only built-in .NET 8.0 libraries (`System.Text.Json`, `System.Net.Http`, `System.Diagnostics`)
- **HTTP clients**: Static `HttpClient` for general API calls; `TranscribeSingleFile` creates a fresh `HttpClient` per upload to avoid connection pooling issues
- **HTTP timeout**: 10 minutes for large audio uploads
- **JSON encoding**: `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` for Spanish characters
- **Locale handling**: `CultureInfo.InvariantCulture` for parsing ffprobe duration output
- **Exit codes**: 0 success, 1 error (with Spanish error messages)
- **All user-facing output is in Spanish**
