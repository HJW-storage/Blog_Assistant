using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NaverBlogDraftAssistant.Models;

namespace NaverBlogDraftAssistant.Services
{
    /// <summary>
    /// Groq API 호출 (OpenAI 호환 포맷)
    /// 무료 한도: 분당 30회, 일 500,000 토큰 (openai/gpt-oss-120b 기준)
    /// API 키 발급: https://console.groq.com
    /// </summary>
    public class GroqApiService
    {
        private const string ApiUrl = "https://api.groq.com/openai/v1/chat/completions";
        private const string ModelsUrl = "https://api.groq.com/openai/v1/models";

        /// <summary>
        /// 블로그 글쓰기(한국어 문체 모사, 지시사항 따르기)에 권장하는 모델.
        /// Groq의 production 등급 모델 중 텍스트 품질이 가장 좋아 기본 추천값으로 사용하고,
        /// 목록 조회 실패 시 최후 폴백으로도 사용합니다. 실제 목록에 없으면 자동으로 무시되므로
        /// 이 모델이 나중에 폐기되어도 앱이 깨지지 않습니다.
        /// </summary>
        public const string RecommendedModel = "openai/gpt-oss-120b";

        // 채팅/텍스트 생성 용도가 아닌 모델(음성 인식, 콘텐츠 검열 전용 등)은 목록에서 제외
        private static readonly string[] ExcludedModelKeywords = { "whisper", "tts", "guard", "prompt-guard" };

        private readonly HttpClient _http = new();

        /// <summary>
        /// Groq 계정에서 현재 사용 가능한 모델 ID 목록을 가져옵니다.
        /// 모델이 언제든 추가/폐기될 수 있으므로 하드코딩 대신 매번 최신 목록을 조회합니다.
        /// </summary>
        public async Task<List<string>> GetAvailableModelsAsync(string apiKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ModelsUrl);
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var response = await _http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"모델 목록 조회 실패 ({(int)response.StatusCode}): {body}");

            using var doc = JsonDocument.Parse(body);
            var ids = new List<string>();
            foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                var id = item.GetProperty("id").GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (ExcludedModelKeywords.Any(k => id.Contains(k, StringComparison.OrdinalIgnoreCase))) continue;
                ids.Add(id);
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }

        public async Task<string> GenerateDraftAsync(string apiKey, string model, StyleProfile profile, string title, string outline)
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
                model,
                max_tokens = 2000,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userPrompt }
                }
            };

            var responseJson = await SendRequestAsync(apiKey, requestBody);
            return ExtractText(responseJson);
        }

        private async Task<string> SendRequestAsync(string apiKey, object requestBody)
        {
            var json = JsonSerializer.Serialize(requestBody);

            // 429 재시도: 10s → 20s → 40s
            int[] retryDelaysMs = { 10_000, 20_000, 40_000 };

            for (int attempt = 0; attempt <= retryDelaysMs.Length; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _http.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                if ((int)response.StatusCode == 429)
                {
                    if (attempt < retryDelaysMs.Length)
                    {
                        await Task.Delay(retryDelaysMs[attempt]);
                        continue;
                    }
                    throw new Exception(
                        "Groq API 요청 한도를 초과했습니다.\n" +
                        "잠시 후 다시 시도해주세요.\n\n" +
                        "무료 한도: 분당 30회 / 일 500,000 토큰");
                }

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"API 호출 실패 ({(int)response.StatusCode}): {body}");

                return body;
            }

            throw new Exception("예상치 못한 오류입니다.");
        }

        /// <summary>
        /// 블로그 글 샘플을 AI에게 분석시켜 자연어 문체 지침을 생성합니다 (방법 2).
        /// 샘플 텍스트는 각 800자로 잘라 토큰 소모를 제한합니다.
        /// </summary>
        public async Task<string> AnalyzeStyleWithAiAsync(string apiKey, string model, List<string> sampleTexts)
        {
            var excerpts = string.Join("\n\n---\n\n", sampleTexts
                .Select((t, i) => $"[샘플 {i + 1}]\n{(t.Length > 800 ? t[..800] : t)}"));

            var systemPrompt =
                "당신은 글쓰기 스타일 분석 전문가입니다. " +
                "아래 블로그 글 샘플들을 읽고, 이 블로그의 문체적 특징을 파악해서 " +
                "새 글을 쓸 때 지켜야 할 핵심 지침을 간결하게 작성해주세요.";

            var userPrompt =
                $"다음은 분석 대상 블로그 글 샘플입니다:\n\n{excerpts}\n\n" +
                "이 글들의 문체 특징을 바탕으로, 비슷한 톤으로 새 글을 쓸 때 지켜야 할 지침을 " +
                "5개 이내 항목으로 작성해주세요. " +
                "어투(존댓말/반말), 감성 표현 방식, 구조 특징(소제목·목록 스타일), " +
                "자주 쓰는 어휘·표현 패턴을 반드시 포함해주세요. 총 300자 이내로 간결하게 작성해주세요.";

            var requestBody = new
            {
                model,
                max_tokens = 400,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userPrompt }
                }
            };

            var responseJson = await SendRequestAsync(apiKey, requestBody);
            return ExtractText(responseJson);
        }

        /// <summary>
        /// 블로그 제목을 입력받아 관련 키워드·주제·소제목 아이디어를 AI로 제안합니다.
        /// </summary>
        public async Task<string> SuggestKeywordsAsync(string apiKey, string model, string title)
        {
            var systemPrompt =
                "당신은 블로그 콘텐츠 기획 전문가입니다. " +
                "주어진 블로그 제목을 보고, 독자들이 이 주제에서 자주 찾는 키워드와 " +
                "궁금해하는 내용을 체계적으로 제안해주세요.";

            var userPrompt =
                $"블로그 제목: \"{title}\"\n\n" +
                "이 제목의 블로그 글에 포함하면 좋은 내용을 다음 형식으로 제안해주세요:\n\n" +
                "🔑 핵심 키워드: (5~8개, 쉼표 구분)\n" +
                "📌 독자들이 알고 싶어하는 핵심 주제: (3~5개 항목)\n" +
                "✍️ 소제목 아이디어: (3~5개)\n" +
                "💡 글의 가치를 높이는 추가 정보: (2~3가지)";

            var requestBody = new
            {
                model,
                max_tokens = 600,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userPrompt }
                }
            };

            var responseJson = await SendRequestAsync(apiKey, requestBody);
            return ExtractText(responseJson);
        }

        private static string ExtractText(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            return text?.Trim() ?? string.Empty;
        }
    }
}
