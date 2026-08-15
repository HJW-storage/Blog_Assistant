using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NaverBlogDraftAssistant.Models;

namespace NaverBlogDraftAssistant.Services
{
    /// <summary>
    /// 완성된 초안(텍스트)과 이미지 배치 정보를 합쳐서
    /// 네이버 블로그 에디터에 붙여넣기 좋은 HTML로 변환합니다.
    ///
    /// 중요: 이 앱은 네이버에 자동으로 "임시저장"하지 않습니다.
    /// (네이버는 외부 프로그램용 공식 글쓰기 API를 제공하지 않고,
    ///  매크로 방식으로 우회하면 이용약관 위반/계정 제재 위험이 있기 때문입니다.)
    /// 대신 붙여넣기 전용 HTML을 만들어 클립보드에 담아주고,
    /// 사용자가 네이버 에디터에 Ctrl+V로 붙여넣은 뒤 직접 '임시저장'을 누르는
    /// 반자동 방식을 사용합니다.
    /// </summary>
    public class DraftExporter
    {
        public string BuildHtml(string draftText, List<ImageItem> images)
        {
            var paragraphs = draftText
                .Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();

            // 문단 인덱스 -> 그 문단 뒤에 넣을 이미지 목록
            var imagesByParagraph = images
                .Where(i => i.RecommendedParagraphIndex >= 0)
                .GroupBy(i => i.RecommendedParagraphIndex)
                .ToDictionary(g => g.Key, g => g.ToList());

            var sb = new StringBuilder();
            sb.AppendLine("<div>");

            for (int i = 0; i < paragraphs.Count; i++)
            {
                var para = paragraphs[i];

                if (para.StartsWith("## "))
                {
                    sb.AppendLine($"<h3>{Escape(para[3..])}</h3>");
                }
                else
                {
                    sb.AppendLine($"<p>{Escape(para)}</p>");
                }

                if (imagesByParagraph.TryGetValue(i, out var imgs))
                {
                    foreach (var img in imgs)
                    {
                        // file:// 경로 사용. 네이버 에디터에 붙여넣으면
                        // 이미지가 자동으로 업로드되지 않는 에디터/브라우저 조합이 있을 수 있어,
                        // 안전하게는 '탐색기에서 사진 파일을 드래그&드롭'하는 것을 권장합니다.
                        var uri = new Uri(img.FilePath).AbsoluteUri;
                        sb.AppendLine($"<p><img src=\"{uri}\" alt=\"{Escape(img.Description ?? img.FileName)}\" /></p>");
                        sb.AppendLine($"<!-- 추천 위치 근거: {Escape(img.PlacementReason ?? "")} -->");
                    }
                }
            }

            sb.AppendLine("</div>");
            return sb.ToString();
        }

        private static string Escape(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
