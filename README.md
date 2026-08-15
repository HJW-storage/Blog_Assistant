# 네이버 블로그 초안 도우미 (프로토타입)

C#/.NET 8 WPF로 만든 데스크톱(.exe) 프로토타입입니다.
요구하신 4가지 기능을 다음과 같은 방식으로 구현했습니다.

| 요구사항 | 구현 방식 |
|---|---|
| 기존 글과 비슷한 포맷의 초안 생성 | 기존 글(.txt) 100개를 규칙 기반으로 분석 → 스타일 요약 생성 → Anthropic API 호출 시 프롬프트에 포함 |
| .exe로 실행되는 UI | WPF (.NET 8) 데스크톱 앱, `dotnet build`로 .exe 생성 |
| 사진 자동 배치 | 업로드한 사진을 Claude의 이미지 인식 기능으로 분석 → 초안의 어느 문단과 어울리는지 추천 |
| 임시저장 | **반자동 방식**입니다. 아래 "왜 완전 자동화하지 않았나" 참고 |

## 실행 방법 (Windows, .NET 8 SDK 필요)

```bash
cd NaverBlogDraftAssistant
dotnet build
dotnet run --project NaverBlogDraftAssistant
```

Visual Studio에서는 `NaverBlogDraftAssistant.sln`을 열어서 F5로 실행하면 됩니다.
배포용 .exe가 필요하면:

```bash
dotnet publish NaverBlogDraftAssistant -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 사용 흐름

1. **탭 1**: 기존에 작성한 글을 미리 `.txt`로 저장해두고 (제목 없이 본문만) 여러 개 선택 → "스타일 분석" 클릭
2. **탭 2**: 제목과 대략적인 개요/키워드 입력 → "초안 생성" 클릭 → Claude API가 스타일을 반영한 초안 작성
3. **탭 3**: 사진 여러 장 추가 → "배치 추천 받기" 클릭 → 각 사진이 어느 문단과 어울리는지 AI가 분석
4. **탭 4**: "네이버용 서식으로 클립보드에 복사" → 네이버 블로그 글쓰기 페이지 열기 → 본문에 붙여넣기(Ctrl+V) → **사용자가 직접 '임시저장' 클릭**

상단의 API 키 입력란에 Anthropic API 키(`https://console.anthropic.com`에서 발급)를 넣어야 2, 3번 탭이 동작합니다.

## 왜 "임시저장"을 완전 자동화하지 않았나

네이버 블로그는 과거 외부 프로그램용 글쓰기 API(metaWeblog/XML-RPC)를 제공했지만
이용약관·게시물 운영정책 위반 소지로 이미 종료했고, 현재는 공식 "글쓰기/임시저장" API가 없습니다.

이 상태에서 완전 자동화를 하려면 실제 웹 에디터 화면을 프로그램이 대신 조작하는
매크로/브라우저 자동화 방식을 써야 하는데, 이는 네이버의 자동화 프로그램 사용 제한
정책과 충돌해 **계정 정지 위험**이 있습니다.

그래서 이 프로토타입은:
- 초안 작성 + 사진 배치까지는 완전 자동화하고,
- 마지막 "임시저장" 클릭 한 번만 사용자가 직접 하도록 설계했습니다.

이렇게 하면 번거로움은 거의 없이 계정 리스크 없는 형태로 요구사항을 최대한 충족할 수 있습니다.

## 참고: 만약 워드프레스를 함께 쓰신다면

워드프레스는 공식 REST API(`/wp-json/wp/v2/posts`, `status: draft`)를 제공해서
"임시저장까지 완전 자동화"가 이용약관 문제 없이 가능합니다. 필요하시면 이 부분만
추가하는 것도 어렵지 않습니다.

## 폴더 구조

```
NaverBlogDraftAssistant/
  NaverBlogDraftAssistant.sln
  NaverBlogDraftAssistant/
    MainWindow.xaml(.cs)      UI 및 전체 흐름 제어
    Models/
      StyleProfile.cs         스타일 분석 결과 모델
      ImageModels.cs          이미지/배치 추천 모델
    Services/
      StyleAnalyzer.cs        기존 글 텍스트 통계 분석
      ClaudeApiService.cs     Anthropic API 호출 (초안 생성 / 이미지 배치 추천)
      DraftExporter.cs        초안+이미지를 HTML로 합치기
      HtmlClipboardHelper.cs  서식 있는 붙여넣기용 클립보드 처리
```

## 현재 프로토타입의 한계 (실제 서비스 전 보완 필요)

- 스타일 분석은 정교한 NLP가 아니라 규칙 기반 통계입니다. 더 정확한 문체 반영을 원하면
  기존 글에서 더 많은 발췌를 few-shot으로 넣는 방식으로 고도화할 수 있습니다.
- 이미지 삽입은 `file://` 경로 기반이라, 네이버 에디터가 이를 인식 못하면 사용자가
  탐색기에서 사진을 직접 드래그&드롭해야 합니다.
- API 키는 세션 중 메모리에만 유지되며 저장하지 않습니다. 필요하다면 Windows
  자격 증명 관리자(Credential Manager) 연동을 추가할 수 있습니다.
