using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NaverBlogDraftAssistant.Models;

namespace NaverBlogDraftAssistant.Services
{
    /// <summary>
    /// Anthropic Messages API를 호출해서
    /// (1) 스타일에 맞는 초안 생성, (2) 이미지 배치 추천을 수행합니다.
    ///
    /// 주의: API 키는 사용자가 직접 발급받아 앱에 입력합니다.
    /// 이 서비스는 키를 코드에 저장하지 않고 매 호출 시 파라미터로만 사용합니다.
    /// </summary>
    public class ClaudeApiService
    {
        private const string ApiUrl = "https://api.anthropic.com/v1/messages";
        private const string ApiVersion = "2023-06-01";
        private const string Model = "claude-sonnet-4-6"; // 필요 시 최신 모델명으로 교체

        private readonly HttpClient _http = new();

        public async Task<string> GenerateDraftAsync(string apiKey, StyleProfile profile, string title, string outline)
        {
            var systemPrompt =
                "당신은 사용자의 네이버 블로그 글쓰기 스타일을 그대로 이어서 써주는 보조 작가입니다.\n" +
                "아래는 사용자의 기존 글 100개를 분석한 스타일 요약입니다. 이 스타일(문단 길이, 소제목 사용 방식, 도입/마무리 어투)을 최대한 반영해서 글을 작성하세요.\n\n" +
                profile.ToPromptSummary() +
                "\n\n작성 규칙:\n" +
                "- 문단은 빈 줄로 구분해주세요.\n" +
                "- 소제목이 필요하면 줄 앞에 '## '를 붙여주세요.\n" +
                "- 과장되거나 정보 없이 분량만 늘리는 문장은 피하고, 실제 정보 위주로 작성하세요.\n" +
                "- 결과는 블로그 본문 텍스트만 출력하세요. 다른 설명은 붙이지 마세요.";

            var userPrompt = $"제목: {title}\n\n다음 개요/키워드를 참고해서 본문 초안을 작성해주세요:\n{outline}";

            var requestBody = new
            {
                model = Model,
                max_tokens = 2000,
                system = systemPrompt,
                messages = new object[]
                {
                    new { role = "user", content = userPrompt }
                }
            };

            var responseJson = await SendRequestAsync(apiKey, requestBody);
            return ExtractTextFromResponse(responseJson);
        }

        public async Task RecommendImagePlacementsAsync(string apiKey, string draftText, List<ImageItem> images)
        {
            var paragraphs = draftText
                .Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();

            var numberedParagraphs = string.Join("\n", paragraphs.Select((p, i) => $"[{i}] {p}"));

            foreach (var image in images)
            {
                if (!File.Exists(image.FilePath)) continue;

                var (base64, mediaType) = EncodeImage(image.FilePath);

                var systemPrompt =
                    "당신은 블로그 글에 사진을 배치하는 편집자입니다. " +
                    "주어진 사진을 보고 짧게 설명한 뒤, 아래 번호가 붙은 문단들 중 이 사진과 가장 잘 어울리는 문단 번호를 하나 골라주세요.\n\n" +
                    "반드시 다음 JSON 형식으로만 답하세요 (다른 텍스트 금지):\n" +
                    "{\"description\": \"사진 설명\", \"paragraph_index\": 문단번호(정수), \"reason\": \"이 위치를 고른 이유\"}";

                var userContent = new object[]
                {
                    new
                    {
                        type = "image",
                        source = new { type = "base64", media_type = mediaType, data = base64 }
                    },
                    new
                    {
                        type = "text",
                        text = $"문단 목록:\n{numberedParagraphs}"
                    }
                };

                var requestBody = new
                {
                    model = Model,
                    max_tokens = 300,
                    system = systemPrompt,
                    messages = new object[]
                    {
                        new { role = "user", content = userContent }
                    }
                };

                try
                {
                    var responseJson = await SendRequestAsync(apiKey, requestBody);
                    var text = ExtractTextFromResponse(responseJson);
                    var parsed = JsonSerializer.Deserialize<PlacementResult>(text,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (parsed != null)
                    {
                        image.Description = parsed.Description;
                        image.RecommendedParagraphIndex = Math.Clamp(parsed.ParagraphIndex, 0, Math.Max(0, paragraphs.Count - 1));
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

        private async Task<string> SendRequestAsync(string apiKey, object requestBody)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", ApiVersion);
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"API 호출 실패 ({(int)response.StatusCode}): {body}");

            return body;
        }

        private static string ExtractTextFromResponse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var contentArray = doc.RootElement.GetProperty("content");
            var sb = new StringBuilder();
            foreach (var block in contentArray.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "text")
                {
                    sb.Append(block.GetProperty("text").GetString());
                }
            }
            return sb.ToString().Trim();
        }

        private static (string base64, string mediaType) EncodeImage(string filePath)
        {
            var bytes = File.ReadAllBytes(filePath);
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var mediaType = ext switch
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
