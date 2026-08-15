using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using NaverBlogDraftAssistant.Models;
using NaverBlogDraftAssistant.Services;

namespace NaverBlogDraftAssistant
{
    public partial class MainWindow : Window
    {
        private readonly StyleAnalyzer _styleAnalyzer = new();
        private readonly GroqApiService _groqApi = new();
        private readonly DraftExporter _exporter = new();
        private readonly NaverBlogScraper _scraper = new();

        private List<string> _loadedPostFiles = new();
        private List<string> _scrapedPostTexts = new();
        private StyleProfile? _styleProfile;
        // private readonly List<ImageItem> _images = new(); // 이미지 분석 기능 비활성화
        private string _lastExportedHtml = string.Empty;

        public MainWindow()
        {
            InitializeComponent();
        }

        private string ApiKey => ApiKeyBox.Password;

        // ---------- 탭 1: 스타일 학습 ----------

        private async void FetchFromUrlButton_Click(object sender, RoutedEventArgs e)
        {
            var url = BlogUrlBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(url) || url == "https://blog.naver.com/")
            {
                MessageBox.Show("블로그 URL을 입력해주세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            FetchFromUrlButton.IsEnabled = false;
            FetchProgressBar.Value = 0;
            FetchStatusLabel.Text = "수집 준비 중...";
            _scrapedPostTexts = new List<string>();

            try
            {
                var progress = new Progress<(int done, int total, string message)>(p =>
                {
                    FetchStatusLabel.Text = p.message;
                    if (p.total > 0)
                        FetchProgressBar.Value = (double)p.done / p.total * 100;
                });

                _scrapedPostTexts = await _scraper.FetchPostTextsAsync(url, maxPosts: 100, progress);
                FetchProgressBar.Value = 100;
                FetchStatusLabel.Text = $"완료 — {_scrapedPostTexts.Count}개 게시글 수집됨. 이제 '스타일 분석하기'를 눌러주세요.";
                LoadedFilesLabel.Text = "선택된 파일: 0개";
                _loadedPostFiles.Clear();
            }
            catch (Exception ex)
            {
                FetchStatusLabel.Text = "수집 실패.";
                MessageBox.Show($"블로그 수집 중 오류:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                FetchFromUrlButton.IsEnabled = true;
            }
        }

        private void LoadPostsButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "텍스트 파일 (*.txt)|*.txt",
                Multiselect = true,
                Title = "기존 블로그 글 텍스트 파일들을 선택하세요 (최대 100개 권장)"
            };

            if (dlg.ShowDialog() == true)
            {
                _loadedPostFiles = dlg.FileNames.ToList();
                LoadedFilesLabel.Text = $"선택된 파일: {_loadedPostFiles.Count}개";
                _scrapedPostTexts.Clear();
                FetchStatusLabel.Text = "";
                FetchProgressBar.Value = 0;
            }
        }

        private void AnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_scrapedPostTexts.Count > 0)
            {
                _styleProfile = _styleAnalyzer.AnalyzeTexts(_scrapedPostTexts);
                StyleSummaryBox.Text = _styleProfile.ToPromptSummary();
                return;
            }

            if (_loadedPostFiles.Count == 0)
            {
                MessageBox.Show("블로그 URL에서 글을 가져오거나, .txt 파일을 선택해주세요.",
                    "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _styleProfile = _styleAnalyzer.Analyze(_loadedPostFiles);
            StyleSummaryBox.Text = _styleProfile.ToPromptSummary();
        }

        // ---------- 탭 2: 초안 생성 ----------

        private async void GenerateDraftButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                MessageBox.Show("API 키를 입력해주세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_styleProfile == null)
            {
                MessageBox.Show("먼저 [1. 기존 글 스타일 학습] 탭에서 스타일 분석을 실행해주세요.",
                    "안내", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                MessageBox.Show("제목을 입력해주세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            GenerateDraftButton.IsEnabled = false;
            DraftBox.Text = "생성 중입니다... 잠시만 기다려주세요.";

            try
            {
                var draft = await _groqApi.GenerateDraftAsync(ApiKey, _styleProfile, TitleBox.Text, OutlineBox.Text);
                DraftBox.Text = draft;
            }
            catch (Exception ex)
            {
                DraftBox.Text = string.Empty;
                MessageBox.Show($"초안 생성 중 오류가 발생했습니다:\n{ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                GenerateDraftButton.IsEnabled = true;
            }
        }

        // ---------- 탭 3: 사진 자동 배치 (이미지 분석 기능 비활성화) ----------
        /*
        private void AddImagesButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "이미지 파일 (*.jpg;*.jpeg;*.png;*.gif;*.webp)|*.jpg;*.jpeg;*.png;*.gif;*.webp",
                Multiselect = true,
                Title = "이 글에 사용할 사진들을 선택하세요"
            };

            if (dlg.ShowDialog() == true)
            {
                foreach (var file in dlg.FileNames)
                    _images.Add(new ImageItem { FilePath = file });

                RefreshImageList();
            }
        }

        private void RefreshImageList()
        {
            ImageListBox.ItemsSource = null;
            ImageListBox.ItemsSource = _images;
        }

        private async void RecommendPlacementButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                MessageBox.Show("API 키를 입력해주세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(DraftBox.Text))
            {
                MessageBox.Show("먼저 [2. 초안 생성] 탭에서 초안을 만들어주세요.", "안내",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_images.Count == 0)
            {
                MessageBox.Show("먼저 사진을 추가해주세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            RecommendPlacementButton.IsEnabled = false;
            PlacementResultBox.Text = "사진을 분석하고 배치를 추천하는 중입니다...";

            try
            {
                await _groqApi.RecommendImagePlacementsAsync(ApiKey, DraftBox.Text, _images);

                var sb = new System.Text.StringBuilder();
                foreach (var img in _images)
                {
                    sb.AppendLine($"[{img.FileName}]");
                    sb.AppendLine($"  설명: {img.Description}");
                    sb.AppendLine($"  추천 위치: {img.RecommendedParagraphIndex}번째 문단 뒤");
                    sb.AppendLine($"  이유: {img.PlacementReason}");
                    sb.AppendLine();
                }
                PlacementResultBox.Text = sb.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"배치 추천 중 오류가 발생했습니다:\n{ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RecommendPlacementButton.IsEnabled = true;
            }
        }
        */

        // ---------- AI 문체 심층 분석 (방법 2) ----------

        private async void AiAnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                MessageBox.Show("API 키를 입력해주세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_styleProfile == null)
            {
                MessageBox.Show("먼저 '스타일 분석하기'를 실행해주세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 스크래핑 또는 파일로 로드된 원문 중 최대 5개 샘플 사용
            List<string> rawTexts;
            if (_scrapedPostTexts.Count > 0)
            {
                rawTexts = _scrapedPostTexts;
            }
            else
            {
                rawTexts = _loadedPostFiles
                    .Select(f => { try { return File.ReadAllText(f); } catch { return string.Empty; } })
                    .ToList();
            }

            var sample = rawTexts.Where(t => t.Length > 100).Take(5).ToList();
            if (sample.Count == 0)
            {
                MessageBox.Show("분석할 텍스트가 없습니다. 먼저 블로그 글을 로드해주세요.",
                    "안내", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AiAnalyzeButton.IsEnabled = false;
            AiAnalyzeStatusLabel.Text = "AI가 문체를 분석하는 중...";

            try
            {
                var description = await _groqApi.AnalyzeStyleWithAiAsync(ApiKey, sample);
                _styleProfile.AiStyleDescription = description;
                // ToPromptSummary()가 AI 설명을 포함하므로 결과 박스 갱신
                StyleSummaryBox.Text = _styleProfile.ToPromptSummary();
                AiAnalyzeStatusLabel.Text = "완료 — 분석 결과가 초안 생성 프롬프트에 반영됩니다.";
            }
            catch (Exception ex)
            {
                AiAnalyzeStatusLabel.Text = "분석 실패.";
                MessageBox.Show($"AI 분석 중 오류:\n{ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AiAnalyzeButton.IsEnabled = true;
            }
        }

        // ---------- 키워드 제안 ----------

        private async void SuggestKeywordsButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                MessageBox.Show("API 키를 입력해주세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var title = TitleBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                MessageBox.Show("제목을 먼저 입력해주세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SuggestKeywordsButton.IsEnabled = false;
            KeywordSuggestBox.Text = "키워드 분석 중...";
            KeywordSuggestBorder.Visibility = Visibility.Visible;

            try
            {
                var suggestions = await _groqApi.SuggestKeywordsAsync(ApiKey, title);
                KeywordSuggestBox.Text = suggestions;
            }
            catch (Exception ex)
            {
                KeywordSuggestBox.Text = string.Empty;
                KeywordSuggestBorder.Visibility = Visibility.Collapsed;
                MessageBox.Show($"키워드 제안 중 오류:\n{ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SuggestKeywordsButton.IsEnabled = true;
            }
        }

        // ---------- 탭 4: 네이버로 내보내기 ----------

        private string BuildExportHtml()
        {
            if (string.IsNullOrWhiteSpace(DraftBox.Text))
                throw new InvalidOperationException("초안이 없습니다. 먼저 [2. 초안 생성] 탭에서 초안을 만들어주세요.");

            // 이미지 분석 비활성화로 빈 리스트 전달
            _lastExportedHtml = _exporter.BuildHtml(DraftBox.Text, new List<ImageItem>());
            ExportPreviewBox.Text = _lastExportedHtml;
            return _lastExportedHtml;
        }

        private void CopyToClipboardButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var html = BuildExportHtml();
                HtmlClipboardHelper.CopyHtmlToClipboard(html);
                MessageBox.Show(
                    "클립보드에 복사했습니다.\n\n" +
                    "이제 네이버 블로그 글쓰기 화면을 열고 본문 영역에 Ctrl+V로 붙여넣은 뒤,\n" +
                    "내용을 확인하고 직접 '임시저장' 버튼을 눌러주세요.",
                    "복사 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "안내", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenNaverWriteButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://blog.naver.com/GoBlogWrite.naver")
            {
                UseShellExecute = true
            });
        }

        private void SaveHtmlButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var html = BuildExportHtml();
                var dlg = new SaveFileDialog
                {
                    Filter = "HTML 파일 (*.html)|*.html",
                    FileName = (string.IsNullOrWhiteSpace(TitleBox.Text) ? "draft" : TitleBox.Text) + ".html"
                };

                if (dlg.ShowDialog() == true)
                {
                    File.WriteAllText(dlg.FileName, html, System.Text.Encoding.UTF8);
                    MessageBox.Show("저장했습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "안내", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
