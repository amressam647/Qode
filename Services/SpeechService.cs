using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using NAudio.Wave;

namespace LocalCursor.Services
{
    public class SpeechService
    {
        private WaveInEvent _waveIn;
        private WaveFileWriter _writer;
        private string _tempFilePath;
        private bool _isRecording;

        public bool IsRecording => _isRecording;

        /// <summary>
        /// Starts recording audio from the default microphone.
        /// </summary>
        public void StartRecording()
        {
            if (_isRecording) return;

            try
            {
                _tempFilePath = Path.Combine(Path.GetTempPath(), $"voice_{DateTime.Now:yyyyMMddHHmmss}.wav");
                
                // Try 16kHz first (ideal for Whisper), fallback to 44.1kHz if needed
                int[] sampleRates = { 16000, 44100, 48000, 8000 };
                bool started = false;
                Exception lastEx = null;

                foreach (var rate in sampleRates)
                {
                    try
                    {
                        _waveIn = new WaveInEvent
                        {
                            WaveFormat = new WaveFormat(rate, 1),
                            DeviceNumber = 0 // Default
                        };
                        _writer = new WaveFileWriter(_tempFilePath, _waveIn.WaveFormat);
                        _waveIn.DataAvailable += (s, e) => _writer.Write(e.Buffer, 0, e.BytesRecorded);
                        _waveIn.StartRecording();
                        started = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        Cleanup();
                    }
                }

                if (!started) throw lastEx ?? new Exception("No audio devices found or all sample rates failed.");
                
                _isRecording = true;
            }
            catch (Exception ex)
            {
                _isRecording = false;
                Cleanup();
                throw new Exception($"Failed to start recording: {ex.Message}. Check your microphone settings.");
            }
        }

        private void Cleanup()
        {
            try { _writer?.Dispose(); } catch { }
            try { _waveIn?.Dispose(); } catch { }
            _writer = null;
            _waveIn = null;
        }

        /// <summary>
        /// Stops recording and returns the path to the audio file.
        /// </summary>
        public string StopRecording()
        {
            if (!_isRecording) return null;

            _waveIn.StopRecording();
            _writer.Dispose();
            _waveIn.Dispose();
            _isRecording = false;

            return _tempFilePath;
        }

        /// <summary>
        /// Transcribes audio using OpenAI Whisper API.
        /// </summary>
        public async Task<string> TranscribeWithWhisperAsync(string audioFilePath, string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
                return "[Voice input requires OpenAI API key for Whisper transcription]";

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            using var form = new MultipartFormDataContent();
            var audioBytes = File.ReadAllBytes(audioFilePath);
            var audioContent = new ByteArrayContent(audioBytes);
            audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
            form.Add(audioContent, "file", Path.GetFileName(audioFilePath));
            form.Add(new StringContent("whisper-1"), "model");

            var response = await httpClient.PostAsync("https://api.openai.com/v1/audio/transcriptions", form);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return $"Transcription error: {json}";

            // Parse simple JSON response
            // { "text": "..." }
            var startIndex = json.IndexOf("\"text\":\"") + 8;
            var endIndex = json.LastIndexOf("\"");
            if (startIndex > 7 && endIndex > startIndex)
            {
                return json.Substring(startIndex, endIndex - startIndex);
            }

            return json;
        }

        /// <summary>
        /// Transcribes using local Whisper via Ollama (if available).
        /// Falls back to returning audio path if no local transcription available.
        /// </summary>
        public async Task<string> TranscribeLocalAsync(string audioFilePath)
        {
            // For local transcription, you would need whisper.cpp or similar
            // This is a placeholder that returns the audio file path
            await Task.CompletedTask;
            return $"[Audio recorded: {audioFilePath}. Local transcription not yet implemented. Please use OpenAI Whisper API.]";
        }
    }
}
