using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NaverBlogDraftAssistant.Models;

namespace NaverBlogDraftAssistant.Services
{
    public class StyleAnalyzer
    {
        // BMP 심볼류(\p{So}) + 서로게이트 쌍(SMP 이모지)
        private static readonly Regex EmojiRegex = new(
            @"\p{So}|[\uD83C-\uDBFF][\uDC00-\uDFFF]",
            RegexOptions.Compiled);

        private static readonly Regex SentenceSplitter = new(
            @"(?<=[.!?요다임니까])\s+",
            RegexOptions.Compiled);

        private static readonly Regex HeadingRegex = new(
            @"^(#{1,3}\s|\[.+\]$|[▶■◆])",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex ListLineRegex = new(
            @"^[\s]*[-•*]\s|^[\s]*\d+\.\s",
            RegexOptions.Multiline | RegexOptions.Compiled);

        // 문장 끝 존댓말 어미(~요)
        private static readonly Regex HonorificEndRegex = new(
            @"요[.!?~]*\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        // 문장 끝 평어 어미(~다)
        private static readonly Regex PlainEndRegex = new(
            @"다[.!?~]*\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        public StyleProfile Analyze(IEnumerable<string> filePaths)
        {
            var texts = new List<string>();
            foreach (var path in filePaths)
            {
                try
                {
                    var content = File.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(content))
                        texts.Add(content);
                }
                catch { }
            }
            return AnalyzeTexts(texts);
        }

        public StyleProfile AnalyzeTexts(IEnumerable<string> postTexts)
        {
            var texts = postTexts.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            var profile = new StyleProfile { AnalyzedPostCount = texts.Count };
            if (texts.Count == 0) return profile;

            var paragraphCounts    = new List<int>();
            var headingCounts      = new List<int>();
            var sentenceLengths    = new List<double>();
            var postLengths        = new List<int>();
            var emojiCounts        = new List<int>();
            var emojiPool          = new List<string>();
            var exclamationCounts  = new List<int>();
            var tildeCounts        = new List<int>();
            var honorificCounts    = new List<int>();
            var plainCounts        = new List<int>();
            var listUsageFlags     = new List<bool>();
            var openingSentences   = new List<string>();
            var closingSentences   = new List<string>();
            // section: 0=앞, 1=중간, 2=뒤
            var excerptCandidates  = new List<(string text, int section)>();

            foreach (var text in texts)
            {
                var paragraphs = SplitParagraphs(text);
                paragraphCounts.Add(paragraphs.Count);
                postLengths.Add(text.Length);

                headingCounts.Add(HeadingRegex.Matches(text).Count);

                var sentences = SentenceSplitter.Split(text)
                    .Where(s => s.Trim().Length > 5)
                    .ToList();
                if (sentences.Count > 0)
                    sentenceLengths.Add(sentences.Average(s => s.Length));

                var emojis = EmojiRegex.Matches(text);
                emojiCounts.Add(emojis.Count);
                emojiPool.AddRange(emojis.Cast<Match>().Select(m => m.Value));

                exclamationCounts.Add(text.Count(c => c == '!'));
                tildeCounts.Add(text.Count(c => c == '~'));

                honorificCounts.Add(HonorificEndRegex.Matches(text).Count);
                plainCounts.Add(PlainEndRegex.Matches(text).Count);

                listUsageFlags.Add(ListLineRegex.IsMatch(text));

                if (paragraphs.Count > 0)
                {
                    var firstSentence = ExtractFirstSentence(paragraphs.First());
                    if (firstSentence.Length >= 8) openingSentences.Add(firstSentence);

                    var lastSentence = ExtractLastSentence(paragraphs.Last());
                    if (lastSentence.Length >= 8) closingSentences.Add(lastSentence);
                }

                CollectExcerpts(paragraphs, excerptCandidates);
            }

            // 기본 통계
            profile.AveragePostLength      = postLengths.Average();
            profile.AverageParagraphCount  = paragraphCounts.Average();
            profile.AverageHeadingCount    = headingCounts.Average();
            profile.AverageSentenceLength  = sentenceLengths.Count > 0 ? sentenceLengths.Average() : 0;

            // 이모지
            profile.AverageEmojiCount = emojiCounts.Average();
            profile.CommonEmojis = emojiPool
                .GroupBy(e => e)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Key)
                .ToList();

            // 감성 표현
            profile.ExclamationPerPost = exclamationCounts.Average();
            profile.TildePerPost       = tildeCounts.Average();

            // 경어체 비율
            var totalHonorific = honorificCounts.Sum();
            var totalPlain     = plainCounts.Sum();
            var totalVerbs     = totalHonorific + totalPlain;
            profile.HonorificRatio = totalVerbs > 0 ? (double)totalHonorific / totalVerbs : 0.5;

            // 목록 형식 사용 여부 (30% 이상의 글에서 사용하면 "사용함"으로 판단)
            profile.UsesListFormat = listUsageFlags.Count(f => f) > listUsageFlags.Count * 0.3;

            // 도입부/마무리 실제 문장 예시
            var rnd = new Random(42);
            profile.CommonOpeningPhrases = SelectPhrases(openingSentences, 3, rnd);
            profile.CommonClosingPhrases = SelectPhrases(closingSentences, 3, rnd);

            // 앞/중간/뒤 균등 샘플링 발췌 8개
            profile.RepresentativeExcerpts = SampleExcerpts(excerptCandidates, 8);

            return profile;
        }

        private static List<string> SplitParagraphs(string text) =>
            text.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 10)
                .ToList();

        // 글 1개에서 앞/중간/뒤 섹션별로 발췌 후보를 1개씩 수집
        private static void CollectExcerpts(List<string> paragraphs, List<(string text, int section)> candidates)
        {
            if (paragraphs.Count == 0) return;

            int count = paragraphs.Count;
            int third = Math.Max(1, count / 3);

            var sections = new[]
            {
                paragraphs.Take(third).ToList(),
                paragraphs.Skip(third).Take(third).ToList(),
                paragraphs.Skip(third * 2).ToList()
            };

            for (int i = 0; i < sections.Length; i++)
            {
                var pool = sections[i].Where(p => p.Length > 30).ToList();
                if (pool.Count == 0) continue;

                var chosen  = pool[Random.Shared.Next(pool.Count)];
                var excerpt = chosen.Length > 500 ? chosen[..500] : chosen;
                candidates.Add((excerpt, i));
            }
        }

        // 섹션별로 균등하게 최대 count개 선택
        private static List<string> SampleExcerpts(List<(string text, int section)> candidates, int count)
        {
            var rnd        = new Random(42);
            int perSection = Math.Max(1, count / 3);

            return candidates
                .GroupBy(c => c.section)
                .SelectMany(g => g.OrderBy(_ => rnd.Next()).Take(perSection + 1))
                .OrderBy(_ => rnd.Next())
                .Take(count)
                .Select(c => c.text)
                .ToList();
        }

        private static string ExtractFirstSentence(string paragraph)
        {
            var first = SentenceSplitter.Split(paragraph.Trim())
                .FirstOrDefault(s => s.Trim().Length > 5)?.Trim() ?? string.Empty;
            return first.Length > 80 ? first[..80] : first;
        }

        private static string ExtractLastSentence(string paragraph)
        {
            var last = SentenceSplitter.Split(paragraph.Trim())
                .LastOrDefault(s => s.Trim().Length > 5)?.Trim() ?? string.Empty;
            return last.Length > 80 ? last[..80] : last;
        }

        // 빈도 상위 후보 중 길이가 짧은 것 우선으로 count개 선택
        private static List<string> SelectPhrases(List<string> phrases, int count, Random rnd)
        {
            var candidates = phrases
                .Where(p => p.Length >= 8)
                .GroupBy(p => p)
                .OrderByDescending(g => g.Count())
                .Take(count * 5)
                .Select(g => g.Key)
                .OrderBy(p => p.Length)
                .ToList();

            return candidates.Take(count).ToList();
        }
    }
}
