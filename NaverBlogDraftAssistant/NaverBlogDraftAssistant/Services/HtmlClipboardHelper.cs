using System.Text;
using System.Windows;

namespace NaverBlogDraftAssistant.Services
{
    /// <summary>
    /// 일반 Clipboard.SetText는 순수 텍스트만 복사되어
    /// 네이버 에디터에 붙여넣었을 때 줄바꿈/이미지 태그가 그대로 텍스트로 보일 수 있습니다.
    /// Windows의 CF_HTML 클립보드 포맷을 사용하면 "서식 있는 붙여넣기"가 가능해져,
    /// 에디터가 실제 문단/이미지로 인식할 확률이 높아집니다.
    /// </summary>
    public static class HtmlClipboardHelper
    {
        public static void CopyHtmlToClipboard(string htmlFragment)
        {
            var cfHtml = BuildCfHtml(htmlFragment);

            var dataObject = new DataObject();
            dataObject.SetData(DataFormats.Html, cfHtml);
            dataObject.SetData(DataFormats.Text, htmlFragment); // 서식 없는 붙여넣기용 대체 텍스트

            Clipboard.SetDataObject(dataObject, true);
        }

        private static string BuildCfHtml(string fragmentBody)
        {
            const string header =
                "Version:0.9\r\n" +
                "StartHTML:{0:000000}\r\n" +
                "EndHTML:{1:000000}\r\n" +
                "StartFragment:{2:000000}\r\n" +
                "EndFragment:{3:000000}\r\n";

            const string htmlPrefix = "<html><body><!--StartFragment-->";
            const string htmlSuffix = "<!--EndFragment--></body></html>";

            var headerLength = string.Format(header, 0, 0, 0, 0).Length;

            var startHtml = headerLength;
            var startFragment = startHtml + htmlPrefix.Length;
            var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragmentBody);
            var endHtml = endFragment + htmlSuffix.Length;

            var finalHeader = string.Format(header, startHtml, endHtml, startFragment, endFragment);
            return finalHeader + htmlPrefix + fragmentBody + htmlSuffix;
        }
    }
}
