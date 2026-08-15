using System.Collections.Generic;

namespace NaverBlogDraftAssistant.Models
{
    /// <summary>
    /// 기존 블로그 글을 분석해서 뽑아낸 "글쓰기 스타일 요약".
    /// AI에게 초안을 요청할 때 이 정보를 프롬프트에 함께 넣어서
    /// 기존 글과 비슷한 톤/구조로 글이 나오도록 유도합니다.
    /// </summary>
    public class StyleProfile
    {
        public int AnalyzedPostCount { get; set; }

        // 구조 통계
        public double AveragePostLength     { get; set; }
        public double AverageParagraphCount { get; set; }
        public double AverageSentenceLength { get; set; }
        public double AverageHeadingCount   { get; set; }

        // 이모지
        public double AverageEmojiCount { get; set; }
        public List<string> CommonEmojis { get; set; } = new();

        // 감성 표현 빈도 (글당 평균 횟수)
        public double ExclamationPerPost { get; set; }
        public double TildePerPost       { get; set; }

        /// <summary>0 = 완전 평어(~다), 1 = 완전 존댓말(~요/습니다)</summary>
        public double HonorificRatio { get; set; }

        public bool UsesListFormat { get; set; }

        /// <summary>AI가 생성한 자연어 문체 설명 (2단계 AI 심층 분석 시 채워짐)</summary>
        public string AiStyleDescription { get; set; } = string.Empty;

        /// <summary>실제 도입부/마무리 문장 예시 (하드코딩 키워드 → 실제 문장으로 개선)</summary>
        public List<string> CommonOpeningPhrases { get; set; } = new();
        public List<string> CommonClosingPhrases { get; set; } = new();

        /// <summary>글의 앞/중간/뒤에서 균등 샘플링한 대표 발췌 (few-shot용, 각 500자 이하)</summary>
        public List<string> RepresentativeExcerpts { get; set; } = new();

        public string ToPromptSummary()
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine($"- 분석한 기존 글 수: {AnalyzedPostCount}개");
            sb.AppendLine($"- 평균 글 길이: 약 {AveragePostLength:F0}자");
            sb.AppendLine($"- 평균 문단 수: 약 {AverageParagraphCount:F1}개");
            sb.AppendLine($"- 평균 소제목(헤딩) 수: 약 {AverageHeadingCount:F1}개");
            sb.AppendLine($"- 평균 문장 길이: 약 {AverageSentenceLength:F0}자");

            // 경어체 설명
            var toneLabel = HonorificRatio >= 0.7 ? "주로 존댓말(~요, ~습니다)" :
                            HonorificRatio <= 0.3 ? "주로 평어(~다, ~임)" :
                                                    "존댓말과 평어 혼용";
            sb.AppendLine($"- 문체: {toneLabel} (존댓말 비율 {HonorificRatio:P0})");

            // 감성 표현
            var emotionParts = new List<string>();
            if (ExclamationPerPost >= 0.5) emotionParts.Add($"느낌표(!) 글당 평균 {ExclamationPerPost:F1}회");
            if (TildePerPost       >= 0.5) emotionParts.Add($"물결(~) 글당 평균 {TildePerPost:F1}회");
            if (emotionParts.Count > 0)
                sb.AppendLine($"- 감성 표현: {string.Join(", ", emotionParts)} 사용");

            // 이모지
            if (AverageEmojiCount >= 0.5)
            {
                var emojiStr = CommonEmojis.Count > 0
                    ? $" (주요: {string.Join(" ", CommonEmojis)})"
                    : string.Empty;
                sb.AppendLine($"- 이모지: 글당 평균 {AverageEmojiCount:F1}개 사용{emojiStr}");
            }
            else
            {
                sb.AppendLine("- 이모지: 거의 사용하지 않음");
            }

            // 목록 형식
            sb.AppendLine($"- 목록 형식(-, •, 번호): {(UsesListFormat ? "자주 사용함" : "거의 사용하지 않음")}");

            // 도입부/마무리
            if (CommonOpeningPhrases.Count > 0)
                sb.AppendLine($"- 도입부 문장 예시: {string.Join(" / ", CommonOpeningPhrases.Select(p => $"\"{p}\""))}");

            if (CommonClosingPhrases.Count > 0)
                sb.AppendLine($"- 마무리 문장 예시: {string.Join(" / ", CommonClosingPhrases.Select(p => $"\"{p}\""))}");

            // 대표 발췌 (앞/중간/뒤 균등)
            if (RepresentativeExcerpts.Count > 0)
            {
                sb.AppendLine("- 대표 문체 예시 발췌 (글의 앞/중간/뒤에서 균등 샘플링):");
                foreach (var ex in RepresentativeExcerpts)
                    sb.AppendLine($"  \"{ex}\"");
            }

            // AI 심층 분석 결과 (있을 때만)
            if (!string.IsNullOrWhiteSpace(AiStyleDescription))
            {
                sb.AppendLine();
                sb.AppendLine("[AI 문체 심층 분석]");
                sb.AppendLine(AiStyleDescription);
            }

            return sb.ToString();
        }
    }
}
