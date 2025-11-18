# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MeetingTranscription is a .NET 8.0 console application that transcribes audio recordings of meetings using OpenAI's Whisper API and analyzes them using GPT-4o-mini to extract summaries, key points, technical concepts, and action items.

## Development Commands

### Build
```bash
dotnet build
```

### Run with audio file
```bash
dotnet rñn --project path/to/audio.mp3
```

Supported audio formats: MP3, M4A, WAV, and any format supported by OpenAI Whisper API.

### Clean
```bash
dotnet clean
```

### Restore dependencies
```bash
dotnet restore
```

## Configuration

### Environment Variables
- **OPENAI_API_KEY** (required): OpenAI API key for Whisper transcription and GPT analysis

### Optional Dependencies
- **ffmpeg**: If available, the application will normalize audio to 16kHz mono WAV before transcription for better results

## Project Configuration

- **Target Framework**: .NET 8.0
- **SDK Version**: 8.0.0 (configured in global.json with latestMinor roll-forward)
- **Nullable Reference Types**: Enabled
- **Implicit Usings**: Enabled
- **Language**: Console output and analysis results are in Spanish

## Architecture

The application is structured as a single-file console application (Program.cs) that performs a pipeline of operations:

1. **Audio Normalization** (`NormalizeAudioIfPossible`): Optionally uses ffmpeg to normalize audio to 16kHz mono WAV format for improved transcription quality
2. **Transcription** (`TranscribeAudio`): Sends audio to OpenAI's Whisper API (whisper-1 model) to generate text transcription
3. **Analysis** (`AnalyzeTranscription`): Sends transcription to GPT-4o-mini with structured JSON output mode to extract:
   - Summary of the meeting
   - Key points
   - Technical concepts with context and related technologies
   - Action items with owners, due dates, priorities, and source context
4. **Output Generation**: Saves results in three formats in the same directory as the input audio file:
   - JSON: Structured data (`*_analysis.json`)
   - Markdown: Human-readable report (`*_analysis.md`)
   - Text: Raw transcription (`*_transcript.txt`)

### Data Models

The application uses C# records with JSON serialization attributes:
- `TranscriptionResponse`: Whisper API response
- `ChatCompletionRequest/Response`: GPT API request/response structures
- `MeetingAnalysis`: Top-level analysis output
- `TechnicalConcept`: Technical terms with context
- `ActionItem`: Tasks with ownership and priority

### Large File Handling

The application automatically handles large audio files (>20 MB) differently from small files:

- **Small files (<20 MB)**: Direct transcription via Whisper API
- **Large files (>20 MB)**: Automatic segmentation into 10-minute chunks using ffmpeg, transcribed separately, then concatenated
- **Segmentation**: Uses ffprobe to get duration, then ffmpeg to split into 600-second segments
- **Optimized format**: Segments are compressed as MP3 (64k bitrate) instead of WAV for smaller upload sizes
- **Streaming upload**: Uses file streaming instead of loading entire segments in memory
- **Temporary files**: Segments are created with unique GUID names and cleaned up after processing
- **Concatenation**: Segment transcriptions are joined with spaces to form the complete transcript

### API Interactions

- OpenAI Whisper API: `POST https://api.openai.com/v1/audio/transcriptions`
- OpenAI Chat Completions API: `POST https://api.openai.com/v1/chat/completions`
- HTTP timeout: 10 minutes (suitable for large files and segmented processing)
- **Retry Strategy**: 3 attempts with exponential backoff (5s base delay) for 5xx server errors

### Prompt Engineering

The GPT analysis uses a Spanish-language system prompt that instructs the model to:
- Extract ALL technical concepts (technologies, frameworks, APIs, services, architectures)
- Identify action items with context from the meeting
- Return structured JSON output (using `response_format: json_object`)

### Error Handling

The application includes comprehensive error handling for:
- Missing or invalid audio files
- Missing OPENAI_API_KEY environment variable
- API connection failures (HttpRequestException)
- Empty or invalid API responses
- ffmpeg availability check (falls back to original audio if unavailable)
- Temporary file cleanup on exit

All errors are returned with exit code 1 and Spanish error messages.

## Implementation Details

### Dependencies
- **Zero external NuGet packages**: Uses only built-in .NET 8.0 libraries (System.Net.Http, System.Text.Json, System.Diagnostics)
- **Optional external tools**: ffmpeg/ffprobe for audio processing (graceful fallback if unavailable)

### File Processing
- **Temporary file naming**: Uses GUID-based naming (`normalized_{guid}.wav`, `segment_{i}_{guid}.wav`) to avoid conflicts
- **Audio normalization**: Converts to 16kHz mono WAV using ffmpeg `-ar 16000 -ac 1` parameters
- **Cleanup strategy**: Attempts to delete temporary files on both successful completion and error conditions
- **Output encoding**: Uses `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` for JSON output to handle special characters

### Advanced Features
- **Automatic file size detection**: Checks file size before processing to determine transcription strategy
- **Concurrent segment processing**: Could be enhanced for parallel segment transcription (currently sequential)
- **Error differentiation**: Distinguishes between client errors (4xx) and server errors (5xx) for retry logic
- **Progress feedback**: Provides Spanish-language console output for each processing stage

### Memory Management
- **Streaming upload**: Uses `StreamContent` instead of `ByteArrayContent` to avoid loading entire files in memory
- **Compressed segments**: MP3 format with 64k bitrate reduces memory footprint and upload time
- **Temporary file lifecycle**: Creates, processes, and cleans up temporary files within each operation scope
- **HTTP client reuse**: Single HttpClient instance with 10-minute timeout for all API calls
- **Enhanced error handling**: Improved timeout detection and detailed error messages for debugging
