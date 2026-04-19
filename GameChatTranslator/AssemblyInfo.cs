using System.Windows;

// WPF 테마 리소스 검색 위치를 지정합니다.
// 이 프로젝트는 별도 테마별 ResourceDictionary를 사용하지 않으므로 None을 지정하고,
// 일반 리소스가 필요할 경우 현재 어셈블리(SourceAssembly)에서 찾도록 설정합니다.
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,            // 테마별 리소스 사전 위치입니다. 현재는 사용하지 않습니다.
    ResourceDictionaryLocation.SourceAssembly   // 공통 리소스 사전을 현재 실행 어셈블리에서 찾습니다.
)]
