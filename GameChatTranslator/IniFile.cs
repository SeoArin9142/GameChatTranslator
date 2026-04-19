using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GameTranslator
{
    // ==========================================
    // 📌 INI 환경 설정 파일 읽기/쓰기 유틸리티 클래스
    // C#에는 INI 파일을 다루는 내장 라이브러리가 없기 때문에,
    // Windows OS의 기본 API(kernel32.dll)를 직접 호출하여 빠르고 가볍게 처리합니다.
    // ==========================================
    public class IniFile
    {
        // INI 파일이 저장될 컴퓨터 내의 절대 경로 (예: C:\Games\config.ini)
        public string Path;

        // Windows API: INI 파일에 특정 키(Key)와 값(Value)을 저장(쓰기)하는 함수
        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern long WritePrivateProfileString(string Section, string Key, string Value, string FilePath);

        // Windows API: INI 파일에서 특정 키(Key)의 값을 불러오는(읽기) 함수
        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern int GetPrivateProfileString(string Section, string Key, string Default, StringBuilder RetVal, int Size, string FilePath);

        // ==========================================
        // 📌 1. 생성자
        // 객체를 생성할 때 파일 경로를 넘겨받아 안전한 절대 경로로 변환하여 저장해 둡니다.
        // ==========================================
        public IniFile(string iniPath)
        {
            Path = new FileInfo(iniPath).FullName;
        }

        // ==========================================
        // 📌 2. 설정 읽기 (Read)
        // INI 파일에서 원하는 설정값을 가져옵니다. 
        // Section 매개변수는 기본값으로 "Settings"가 지정되어 있습니다.
        // ==========================================
        public string Read(string Key, string Section = "Settings")
        {
            // Windows API가 읽어온 글자를 담아둘 255자 크기의 넉넉한 바구니(메모리 공간)를 준비합니다.
            var RetVal = new StringBuilder(255);

            // API를 호출하여 값을 찾습니다. 값이 없다면 null을 돌려 기본값 처리가 가능하게 합니다.
            int length = GetPrivateProfileString(Section, Key, "", RetVal, 255, Path);

            // 바구니에 담긴 텍스트를 C#에서 쓸 수 있는 String 형태로 바꿔서 반환합니다.
            return length == 0 ? null : RetVal.ToString();
        }

        // ==========================================
        // 📌 3. 설정 쓰기 (Write)
        // 변경된 설정값을 INI 파일에 기록합니다. 파일이 없으면 OS가 알아서 새로 만들어 줍니다.
        // ==========================================
        public void Write(string Key, string Value, string Section = "Settings")
        {
            // 지정된 섹션의 키 위치에 새로운 값을 덮어씁니다.
            WritePrivateProfileString(Section, Key, Value, Path);
        }
    }
}
