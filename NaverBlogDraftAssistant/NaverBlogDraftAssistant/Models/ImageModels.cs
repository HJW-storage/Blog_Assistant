namespace NaverBlogDraftAssistant.Models
{
    /// <summary>사용자가 업로드한 이미지 하나에 대한 정보</summary>
    public class ImageItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName => System.IO.Path.GetFileName(FilePath);

        /// <summary>AI가 이미지를 보고 생성한 짧은 설명 (배치 판단에 사용)</summary>
        public string? Description { get; set; }

        /// <summary>AI가 추천한 삽입 위치 (초안의 몇 번째 문단 뒤에 넣을지, 0-based)</summary>
        public int RecommendedParagraphIndex { get; set; } = -1;

        /// <summary>배치 추천 이유 (사용자가 검토할 수 있도록)</summary>
        public string? PlacementReason { get; set; }
    }
}
