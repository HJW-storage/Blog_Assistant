using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NaverBlogDraftAssistant.Models;

namespace NaverBlogDraftAssistant.Services
{
    public class GeminiApiService
    {
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
        private const string Model = "gemini-2.0-flash";

        private readonly HttpClient _http = new();

        public async Task<string> GenerateDraftAsync(string apiKey, StyleProfile profile, string title, string outline)
        {
            var systemPrompt =
                "당신은 사용자의 네이버 블로그 글쓰기 스타일을 그대로 이어서 써주는 보조 작가입니다.\n" +
                "아래는 사용자의 기존 글을 분석한 스타일 요약입니다. 이 스타일(문단 길이, 소제목 사용 방식, 도입/마무리 어투)을 최대한 반영해서 글을 작성하세요.\n\n" +
                profile.ToPromptSummary() +
                "\n\n작성 규칙:\n" +
                "- 문단은 빈 줄로 구분해주세요.\n" +
                "- 소제목이 필요하면 줄 앞에 '## '를 붙여주세요.\n" +
                "- 과장되거나 정보 없이 분량만 늘리는 문장은 피하고, 실제 정보 위주로 작성하세요.\n" +
                "- 결과는 블로그 본문 텍스트만 출력하세요. 다른 설명은 붙이지 마세요.";

            var userPrompt = $"제목: {title}\n\n다음 개요/키워드를 참고해서 본문 초안을 작성해주세요:\n{outline}";

            var requestBody = new
            {
                system_instruction = new { parts = new[] { new { text = systemPrompt } } },
                contents = new[]
                {
                    new { role = "user", parts = new[] { new { text = userPrompt } } }
                },
                generationConfig = new { maxOutputTokens = 2000 }
            };

            var responseJson = await SendRequestAsync(apiKey, requestBody);
            return ExtractText(responseJson);
        }

        public async Task RecommendImagePlacementsAsync(string apiKey, string draftText, List<ImageItem> images)
        {
            var paragraphs = draftText
                .Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();

            var numberedParagraphs = string.Join("\n", paragraphs.Select((p, i) => $"[{i}] {p}"));

            var systemPrompt =
                "당신은 블로그 글에 사진을 배치하는 편집자입니다. " +
                "주어진 사진을 보고 짧게 설명한 뒤, 아래 번호가 붙은 문단들 중 이 사진과 가장 잘 어울리는 문단 번호를 하나 골라주세요.\n\n" +
                "반드시 다음 JSON 형식으로만 답하세요 (다른 텍스트 없이):\n" +
                "{\"description\": \"사진 설명\", \"paragraph_index\": 문단번호(정수), \"reason\": \"이 위치를 고른 이유\"}";

            bool firstImage = true;
            foreach (var image in images)
            {
                if (!File.Exists(image.FilePath)) continue;

                // 첫 번째 이미지 이후부터 4초 간격 (무료 티어 15 RPM 제한 대응)
                if (!firstImage) await Task.Delay(4000);
                firstImage = false;

                var (base64, mediaType) = EncodeImage(image.FilePath);

                var requestBody = new
                {
                    system_instruction = new { parts = new[] { new { text = systemPrompt } } },
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new object[]
                            {
                                new { inline_data = new { mime_type = mediaType, data = base64 } },
                                new { text = $"문단 목록:\n{numberedParagraphs}" }
                            }
                        }
                    },
                    generationConfig = new { maxOutputTokens = 400 }
                };

                try
                {
                    var responseJson = await SendRequestAsync(apiKey, requestBody);
                    var text = ExtractText(responseJson);

                    // Gemini가 ```json ... ``` 블록으로 감쌀 수 있으므로 JSON 객체만 추출
                    var jsonMatch = Regex.Match(text, @"\{[\s\S]*\}");
                    if (!jsonMatch.Success)
                        throw new Exception("JSON 형식 응답을 찾을 수 없습니다.");

                    var parsed = JsonSerializer.Deserialize<PlacementResult>(
                        jsonMatch.Value,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (parsed != null)
                    {
                        image.Description = parsed.Description;
                        image.RecommendedParagraphIndex = Math.Clamp(
                            parsed.ParagraphIndex, 0, Math.Max(0, paragraphs.Count - 1));
                        image.PlacementReason = parsed.Reason;
                    }
                }
                catch (Exception ex)
                {
                    image.Description = "(분석 실패)";
                    image.PlacementReason = ex.Message;
                    image.RecommendedParagraphIndex = 0;
                }
            }
        }

        // 429 재시도 대기 시간: 15s → 30s → 60s (Gemini 무료 티어는 분 단위로 리셋)
        private static readonly int[] RetryDelaysMs = { 15_000, 30_000, 60_000 };

        private async Task<string> SendRequestAsync(string apiKey, object requestBody)
        {
            var url = $"{BaseUrl}/{Model}:generateContent?key={apiKey}";
            var json = JsonSerializer.Serialize(requestBody);

            for (int attempt = 0; attempt <= RetryDelaysMs.Length; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _http.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                if ((int)response.StatusCode == 429)
                {
                    if (attempt < RetryDelaysMs.Length)
                    {
                        await Task.Delay(RetryDelaysMs[attempt]);
                        continue;
                    }
                    throw new Exception(
                        "Gemini API 분당 요청 한도(15회/분)를 초과했습니다.\n" +
                        "1~2분 기다린 뒤 다시 시도하거나, 이미지 수를 줄여보세요.");
                }

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"API 호출 실패 ({(int)response.StatusCode}): {body}");

                return body;
            }

            throw new Exception("예상치 못한 오류입니다.");
        }

        private static string ExtractText(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();
            return text?.Trim() ?? string.Empty;
        }

        private static (string base64, string mediaType) EncodeImage(string filePath)
        {
            var bytes = File.ReadAllBytes(filePath);
            var mediaType = Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
            return (Convert.ToBase64String(bytes), mediaType);
        }

        private class PlacementResult
        {
            public string Description { get; set; } = string.Empty;
            public int ParagraphIndex { get; set; }
            public string Reason { get; set; } = string.Empty;
        }
    }
}
