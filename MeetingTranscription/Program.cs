using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingTranscription;

// Models for OpenAI API responses and output
record TranscriptionResponse(
    [property: JsonPropertyName("text")] string Text
);

record ChatCompletionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] List<ChatMessage> Messages,
    [property: JsonPropertyName("response_format")] ResponseFormat? ResponseFormat = null
);

record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content
);

record ResponseFormat(
    [property: JsonPropertyName("type")] string Type
);

record ChatCompletionResponse(
    [property: JsonPropertyName("choices")] List<ChatChoice> Choices
);

record ChatChoice(
    [property: JsonPropertyName("message")] ChatMessage Message
);

record MeetingAnalysis(
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("key_points")] List<string> KeyPoints,
    [property: JsonPropertyName("technical_concepts")] List<TechnicalConcept> TechnicalConcepts,
    [property: JsonPropertyName("action_items")] List<ActionItem> ActionItems
);

record TechnicalConcept(
    [property: JsonPropertyName("term")] string Term,
    [property: JsonPropertyName("context")] string Context,
    [property: JsonPropertyName("mentioned_technologies")] List<string> MentionedTechnologies
);

record ActionItem(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("owner")] string Owner,
    [property: JsonPropertyName("due_date")] string DueDate,
    [property: JsonPropertyName("priority")] string Priority,
    [property: JsonPropertyName("source_times")] List<string> SourceTimes
);

class Program
{
    private static readonly HttpClient httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10) // Aumentar timeout para archivos grandes
    };
    private static string? apiKey;

    static async Task<int> Main(string[] args)
    {
        try
        {
            // Validate arguments
            if (args.Length == 0)
            {
                Console.WriteLine("Error: Debe proporcionar la ruta al archivo de audio.");
                Console.WriteLine("Uso: dotnet run -- archivo.mp3");
                return 1;
            }

            string audioFilePath = args[0];

            // Validate file exists
            if (!File.Exists(audioFilePath))
            {
                Console.WriteLine($"Error: El archivo '{audioFilePath}' no existe.");
                return 1;
            }

            // Get API key from environment
            apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                Console.WriteLine("Error: La variable de entorno OPENAI_API_KEY no está configurada.");
                return 1;
            }

            // Setup HttpClient
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            Console.WriteLine($"Procesando archivo: {audioFilePath}");

            // Step 1: Normalize audio if ffmpeg is available
            string audioToTranscribe = await NormalizeAudioIfPossible(audioFilePath);

            // Step 2: Transcribe audio
            Console.WriteLine("Transcribiendo audio...");
            string transcription = await TranscribeAudio(audioToTranscribe);
            Console.WriteLine($"Transcripción completada ({transcription.Length} caracteres)");

            // Step 3: Analyze transcription with GPT
            Console.WriteLine("Analizando transcripción...");
            MeetingAnalysis analysis = await AnalyzeTranscription(transcription);
            Console.WriteLine("Análisis completado");

            // Step 4: Save outputs
            string baseFileName = Path.GetFileNameWithoutExtension(audioFilePath);
            string outputDir = Path.GetDirectoryName(audioFilePath) ?? ".";

            string jsonOutputPath = Path.Combine(outputDir, $"{baseFileName}_analysis.json");
            string mdOutputPath = Path.Combine(outputDir, $"{baseFileName}_analysis.md");
            string transcriptPath = Path.Combine(outputDir, $"{baseFileName}_transcript.txt");

            await SaveJsonOutput(jsonOutputPath, analysis);
            await SaveMarkdownOutput(mdOutputPath, analysis);
            await SaveTranscript(transcriptPath, transcription);

            Console.WriteLine($"\nResultados guardados:");
            Console.WriteLine($"  - JSON: {jsonOutputPath}");
            Console.WriteLine($"  - Markdown: {mdOutputPath}");
            Console.WriteLine($"  - Transcripción: {transcriptPath}");

            // Cleanup temporary normalized file if created
            if (audioToTranscribe != audioFilePath && File.Exists(audioToTranscribe))
            {
                try
                {
                    File.Delete(audioToTranscribe);
                }
                catch { /* Ignore cleanup errors */ }
            }

            return 0;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Error de conexión con la API: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static async Task<string> NormalizeAudioIfPossible(string inputPath)
    {
        try
        {
            // Check if ffmpeg is available
            var ffmpegCheck = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(ffmpegCheck);
            if (process == null)
            {
                Console.WriteLine("ffmpeg no encontrado, usando archivo original");
                return inputPath;
            }

            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                Console.WriteLine("ffmpeg no disponible, usando archivo original");
                return inputPath;
            }

            // Normalize audio
            Console.WriteLine("Normalizando audio con ffmpeg...");
            string tempPath = Path.Combine(Path.GetTempPath(), $"normalized_{Guid.NewGuid()}.wav");

            var ffmpegNormalize = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{inputPath}\" -ar 16000 -ac 1 -y \"{tempPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var normalizeProcess = Process.Start(ffmpegNormalize);
            if (normalizeProcess == null)
            {
                return inputPath;
            }

            await normalizeProcess.WaitForExitAsync();

            if (normalizeProcess.ExitCode == 0 && File.Exists(tempPath))
            {
                Console.WriteLine("Audio normalizado correctamente");
                return tempPath;
            }
            else
            {
                Console.WriteLine("No se pudo normalizar el audio, usando archivo original");
                return inputPath;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al normalizar audio: {ex.Message}, usando archivo original");
            return inputPath;
        }
    }

    static async Task<string> TranscribeAudio(string audioPath)
    {
        using var form = new MultipartFormDataContent();

        // Add file
        var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(audioPath));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", Path.GetFileName(audioPath));

        // Add model parameter - try gpt-4o-mini-transcribe first, fallback to whisper-1
        form.Add(new StringContent("whisper-1"), "model");

        // Add response format
        form.Add(new StringContent("json"), "response_format");

        var response = await httpClient.PostAsync("https://api.openai.com/v1/audio/transcriptions", form);

        if (!response.IsSuccessStatusCode)
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Error en transcripción: {response.StatusCode} - {errorContent}");
        }

        string responseContent = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Respuesta de API (primeros 500 chars): {responseContent.Substring(0, Math.Min(500, responseContent.Length))}");

        var transcriptionResponse = JsonSerializer.Deserialize<TranscriptionResponse>(responseContent);

        if (transcriptionResponse == null || string.IsNullOrEmpty(transcriptionResponse.Text))
        {
            throw new Exception($"La transcripción está vacía. Respuesta completa: {responseContent}");
        }

        return transcriptionResponse.Text;
    }

    static async Task<MeetingAnalysis> AnalyzeTranscription(string transcription)
    {
        var systemPrompt = @"Eres un asistente que analiza transcripciones de reuniones y genera resúmenes estructurados en español.

Debes responder ÚNICAMENTE con un objeto JSON válido (sin markdown, sin bloques de código) con la siguiente estructura:
{
  ""summary"": ""Resumen conciso de la reunión en español"",
  ""key_points"": [""Punto clave 1"", ""Punto clave 2""],
  ""technical_concepts"": [
    {
      ""term"": ""Nombre del concepto técnico o tecnología"",
      ""context"": ""Breve explicación del contexto en que se mencionó en la reunión"",
      ""mentioned_technologies"": [""Tecnología 1"", ""Tecnología 2""]
    }
  ],
  ""action_items"": [
    {
      ""title"": ""Descripción de la tarea"",
      ""owner"": ""Persona responsable (o 'No especificado')"",
      ""due_date"": ""Fecha límite (o 'No especificado')"",
      ""priority"": ""Alta/Media/Baja"",
      ""source_times"": [""Contexto o momento de la reunión donde se mencionó""]
    }
  ]
}

IMPORTANTE para technical_concepts:
- Extrae TODOS los conceptos técnicos, tecnologías, frameworks, protocolos, APIs, servicios, arquitecturas, etc.
- Incluye términos específicos como nombres de servicios, bases de datos, herramientas de desarrollo
- Agrupa tecnologías relacionadas cuando sea apropiado
- Proporciona contexto útil para alguien nuevo que necesita investigar estos conceptos

Asegúrate de que el JSON sea válido y no incluyas ningún texto adicional.";

        var userPrompt = $"Analiza la siguiente transcripción de reunión y genera un resumen estructurado:\n\n{transcription}";

        var request = new ChatCompletionRequest(
            Model: "gpt-4o-mini",
            Messages: new List<ChatMessage>
            {
                new ChatMessage("system", systemPrompt),
                new ChatMessage("user", userPrompt)
            },
            ResponseFormat: new ResponseFormat("json_object")
        );

        var jsonContent = JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);

        if (!response.IsSuccessStatusCode)
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Error en análisis: {response.StatusCode} - {errorContent}");
        }

        string responseContent = await response.Content.ReadAsStringAsync();
        var chatResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseContent);

        if (chatResponse == null || chatResponse.Choices.Count == 0)
        {
            throw new Exception("No se recibió respuesta del modelo");
        }

        string analysisJson = chatResponse.Choices[0].Message.Content;

        var analysis = JsonSerializer.Deserialize<MeetingAnalysis>(analysisJson);
        if (analysis == null)
        {
            throw new Exception("No se pudo deserializar el análisis");
        }

        return analysis;
    }

    static async Task SaveJsonOutput(string path, MeetingAnalysis analysis)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        string json = JsonSerializer.Serialize(analysis, options);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
    }

    static async Task SaveMarkdownOutput(string path, MeetingAnalysis analysis)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Análisis de Reunión");
        sb.AppendLine();
        sb.AppendLine("## Resumen");
        sb.AppendLine();
        sb.AppendLine(analysis.Summary);
        sb.AppendLine();

        sb.AppendLine("## Puntos Clave");
        sb.AppendLine();
        foreach (var point in analysis.KeyPoints)
        {
            sb.AppendLine($"- {point}");
        }
        sb.AppendLine();

        sb.AppendLine("## Conceptos Técnicos Discutidos");
        sb.AppendLine();
        if (analysis.TechnicalConcepts.Count == 0)
        {
            sb.AppendLine("*No se identificaron conceptos técnicos específicos.*");
        }
        else
        {
            foreach (var concept in analysis.TechnicalConcepts)
            {
                sb.AppendLine($"### {concept.Term}");
                sb.AppendLine();
                sb.AppendLine($"**Contexto:** {concept.Context}");
                sb.AppendLine();
                if (concept.MentionedTechnologies.Count > 0)
                {
                    sb.AppendLine("**Tecnologías relacionadas:**");
                    foreach (var tech in concept.MentionedTechnologies)
                    {
                        sb.AppendLine($"- {tech}");
                    }
                    sb.AppendLine();
                }
            }
        }

        sb.AppendLine("## Tareas y Acciones");
        sb.AppendLine();

        if (analysis.ActionItems.Count == 0)
        {
            sb.AppendLine("*No se identificaron tareas específicas.*");
        }
        else
        {
            foreach (var item in analysis.ActionItems)
            {
                sb.AppendLine($"### {item.Title}");
                sb.AppendLine();
                sb.AppendLine($"- **Responsable:** {item.Owner}");
                sb.AppendLine($"- **Fecha límite:** {item.DueDate}");
                sb.AppendLine($"- **Prioridad:** {item.Priority}");

                if (item.SourceTimes.Count > 0)
                {
                    sb.AppendLine($"- **Contexto:** {string.Join(", ", item.SourceTimes)}");
                }

                sb.AppendLine();
            }
        }

        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
    }

    static async Task SaveTranscript(string path, string transcription)
    {
        await File.WriteAllTextAsync(path, transcription, Encoding.UTF8);
    }
}