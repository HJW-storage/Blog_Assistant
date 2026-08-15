using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace NaverBlogDraftAssistant.Services
{
    public class NaverBlogScraper
    {
        private readonly HttpClient _http;

        public NaverBlogScraper()
        {
            _http = new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(20);
            _http.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            _http.DefaultRequestHeaders.Add("Accept-Language", "ko-KR,ko;q=0.9");
            _http.DefaultRequestHeaders.Add("Referer", "https://blog.naver.com/");
        }

        public async Task<List<string>> FetchPostTextsAsync(
            string blogUrl,
            int maxPosts = 100,
            IProgress<(int done, int total, string message)>? progress = null)
        {
            var blogId = ExtractBlogId(blogUrl);
            progress?.Report((0, 0, $"블로그 ID [{blogId}] 확인. 게시글 목록 수집 중..."));

            var logNos = await CollectLogNosAsync(blogId, maxPosts, progress);
            if (logNos.Count == 0)
                throw new InvalidOperationException(
                    "게시글 목록을 가져오지 못했습니다.\n블로그 주소가 맞는지, 공개 블로그인지 확인해주세요.");

            progress?.Report((0, logNos.Count, $"게시글 {logNos.Count}개 발견. 본문 수집 시작..."));

            var texts = new List<string>();
            for (int i = 0; i < logNos.Count; i++)
            {
                try
                {
                    var text = await FetchPostTextAsync(blogId, logNos[i]);
                    if (!string.IsNullOrWhiteSpace(text) && text.Length > 30)
                        texts.Add(text);
                }
                catch { /* 개별 게시글 실패는 건너뜀 */ }

                progress?.Report((i + 1, logNos.Count, $"본문 수집 중... {i + 1}/{logNos.Count}"));
                await Task.Delay(400); // 서버 부담 최소화
            }

            if (texts.Count == 0)
                throw new InvalidOperationException("게시글 본문을 하나도 수집하지 못했습니다.");

            return texts;
        }

        private static string ExtractBlogId(string input)
        {
            input = input.Trim();
            if (!input.Contains("://"))
                input = "https://" + input;

            var uri = new Uri(input);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                throw new ArgumentException("URL에서 블로그 ID를 추출할 수 없습니다.");
            return segments[0];
        }

        private async Task<List<string>> CollectLogNosAsync(
            string blogId,
            int maxPosts,
            IProgress<(int, int, string)>? progress)
        {
            var logNos = new HashSet<string>();

            // 전략 1: RSS 피드 (최근 30개, 빠름)
            try
            {
                var rss = await _http.GetStringAsync($"https://rss.blog.naver.com/{blogId}.xml");
                foreach (Match m in Regex.Matches(rss, $@"blog\.naver\.com/{Regex.Escape(blogId)}/(\d{{7,12}})"))
                    logNos.Add(m.Groups[1].Value);
            }
            catch { }

            progress?.Report((0, 0, $"RSS에서 {logNos.Count}개 발견. 추가 수집 중..."));

            // 전략 2: PostList 페이지 순회 (30개씩, 페이지 단위)
            for (int page = 1; logNos.Count < maxPosts; page++)
            {
                try
                {
                    var url = $"https://blog.naver.com/PostList.naver?blogId={blogId}&currentPage={page}&countPerPage=30";
                    var html = await _http.GetStringAsync(url);
                    int before = logNos.Count;

                    // 게시글 링크에서 logNo 추출 (href 패턴 두 가지 모두 처리)
                    foreach (Match m in Regex.Matches(html,
                        $@"href=""(?:/{Regex.Escape(blogId)}/|PostView[^""]*logNo=)(\d{{7,12}})""",
                        RegexOptions.IgnoreCase))
                    {
                        logNos.Add(m.Groups[1].Value);
                    }

                    // data-logno 속성 패턴
                    foreach (Match m in Regex.Matches(html, @"data-logno=""(\d{7,12})"""))
                        logNos.Add(m.Groups[1].Value);

                    // 더 이상 새 글이 없으면 중단
                    if (logNos.Count == before) break;

                    progress?.Report((0, 0, $"목록 {page}페이지 수집... 현재 {logNos.Count}개"));
                    await Task.Delay(300);
                }
                catch { break; }
            }

            return logNos.Take(maxPosts).ToList();
        }

        private async Task<string> FetchPostTextAsync(string blogId, string logNo)
        {
            // 본문은 iframe 내 PostView URL에서 가져옴
            var url = $"https://blog.naver.com/PostView.nhn?blogId={blogId}&logNo={logNo}&redirect=Dlog&widgetTypeCall=true&noTrackingCode=true";
            string html;
            try
            {
                html = await _http.GetStringAsync(url);
            }
            catch
            {
                // 구 URL 실패 시 새 URL 시도
                url = $"https://blog.naver.com/PostView.naver?blogId={blogId}&logNo={logNo}";
                html = await _http.GetStringAsync(url);
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // SmartEditor 3 (최신 글)
            var node = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'se-main-container')]");

            // SmartEditor 2
            node ??= doc.DocumentNode.SelectSingleNode("//div[contains(@class,'se_component_wrap')]");

            // 구 에디터
            node ??= doc.DocumentNode.SelectSingleNode("//*[@id='postViewArea']");
            node ??= doc.DocumentNode.SelectSingleNode("//*[@id='post-view']");

            // 최후 폴백: body 전체
            node ??= doc.DocumentNode.SelectSingleNode("//body");

            if (node == null) return string.Empty;

            // script/style 제거 후 텍스트 추출
            foreach (var bad in node.SelectNodes(".//script|.//style") ?? Enumerable.Empty<HtmlNode>())
                bad.Remove();

            var raw = node.InnerText;
            raw = HtmlEntity.DeEntitize(raw);
            raw = Regex.Replace(raw, @"[ \t]+", " ");
            raw = Regex.Replace(raw, @"(\r?\n){3,}", "\n\n");
            return raw.Trim();
        }
    }
}
