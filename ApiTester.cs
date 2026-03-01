using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Speakly.Config;

namespace Speakly.Services
{
    public static class ApiTester
    {
        private static readonly HttpClient _client = new HttpClient();

        public static async Task<string> TestDeepgramAsync(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return "FAIL: No API Key provided";
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.deepgram.com/v1/projects");
                request.Headers.Add("Authorization", $"Token {apiKey}");
                var response = await _client.SendAsync(request);
                return response.IsSuccessStatusCode ? "OK: Connection Successful" : $"FAIL: {response.StatusCode}";
            }
            catch (Exception ex) { return $"FAIL: {ex.Message}"; }
        }

        public static async Task<string> TestOpenAIAsync(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return "FAIL: No API Key provided";
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                var response = await _client.SendAsync(request);
                return response.IsSuccessStatusCode ? "OK: Connection Successful" : $"FAIL: {response.StatusCode}";
            }
            catch (Exception ex) { return $"FAIL: {ex.Message}"; }
        }

        public static async Task<string> TestCerebrasAsync(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return "FAIL: No API Key provided";
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.cerebras.ai/v1/models");
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                var response = await _client.SendAsync(request);
                return response.IsSuccessStatusCode ? "OK: Connection Successful" : $"FAIL: {response.StatusCode}";
            }
            catch (Exception ex) { return $"FAIL: {ex.Message}"; }
        }

        public static async Task<string> TestOpenRouterAsync(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return "FAIL: No API Key provided";
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/models");
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                var response = await _client.SendAsync(request);
                return response.IsSuccessStatusCode ? "OK: Connection Successful" : $"FAIL: {response.StatusCode}";
            }
            catch (Exception ex) { return $"FAIL: {ex.Message}"; }
        }
    }
}
