using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        // Windows API: INI 파일에 특정 키(Key)와 값(Value)을 저장(쓰기)하는 함수.
        // Section은 [Settings] 같은 INI 섹션명, Key는 설정명, Value는 저장할 값, FilePath는 config.ini 경로입니다.
        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern long WritePrivateProfileString(string Section, string Key, string Value, string FilePath);

        // Windows API: INI 파일에서 특정 키(Key)의 값을 불러오는(읽기) 함수.
        // Default는 키가 없을 때 반환할 기본 문자열, RetVal은 결과 버퍼, Size는 버퍼 길이입니다.
        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern int GetPrivateProfileString(string Section, string Key, string Default, StringBuilder RetVal, int Size, string FilePath);

        // ==========================================
        // 📌 1. 생성자
        // 객체를 생성할 때 파일 경로를 넘겨받아 안전한 절대 경로로 변환하여 저장해 둡니다.
        // ==========================================
        /// <summary>
        /// INI 파일 접근 객체를 생성합니다.
        /// <paramref name="iniPath"/>는 실행 폴더의 config.ini 같은 설정 파일 경로이며,
        /// 상대 경로가 들어와도 FileInfo를 통해 절대 경로로 정규화합니다.
        /// </summary>
        public IniFile(string iniPath)
        {
            Path = new FileInfo(iniPath).FullName;
        }

        // ==========================================
        // 📌 2. 설정 읽기 (Read)
        // INI 파일에서 원하는 설정값을 가져옵니다. 
        // Section 매개변수는 기본값으로 "Settings"가 지정되어 있습니다.
        // ==========================================
        /// <summary>
        /// INI 파일에서 문자열 설정값을 읽습니다.
        /// <paramref name="Key"/>는 읽을 설정 키 이름이고,
        /// <paramref name="Section"/>은 키가 속한 섹션명이며 기본값은 "Settings"입니다.
        /// 값이 없으면 null을 반환해 호출부의 ?? 기본값 처리가 동작하게 합니다.
        /// </summary>
        public string Read(string Key, string Section = "Settings")
        {
            if (!string.Equals(Section, SettingsService.LegacySettingsSectionName, StringComparison.OrdinalIgnoreCase))
            {
                return ReadExact(Key, Section);
            }

            string mappedSection = ResolveSettingsSection(Key);
            string mappedValue = ReadExact(Key, mappedSection);
            if (mappedValue != null)
            {
                return mappedValue;
            }

            return string.Equals(mappedSection, SettingsService.LegacySettingsSectionName, StringComparison.OrdinalIgnoreCase)
                ? null
                : ReadExact(Key, SettingsService.LegacySettingsSectionName);
        }

        // ==========================================
        // 📌 3. 설정 쓰기 (Write)
        // 변경된 설정값을 INI 파일에 기록합니다. 파일이 없으면 OS가 알아서 새로 만들어 줍니다.
        // ==========================================
        /// <summary>
        /// INI 파일에 문자열 설정값을 저장합니다.
        /// <paramref name="Key"/>는 저장할 설정 키 이름,
        /// <paramref name="Value"/>는 파일에 기록할 문자열 값,
        /// <paramref name="Section"/>은 값을 기록할 섹션명이며 기본값은 "Settings"입니다.
        /// </summary>
        public void Write(string Key, string Value, string Section = "Settings")
        {
            if (!string.Equals(Section, SettingsService.LegacySettingsSectionName, StringComparison.OrdinalIgnoreCase))
            {
                WritePrivateProfileString(Section, Key, Value, Path);
                return;
            }

            string mappedSection = ResolveSettingsSection(Key);
            WritePrivateProfileString(mappedSection, Key, Value, Path);

            if (!string.Equals(mappedSection, SettingsService.LegacySettingsSectionName, StringComparison.OrdinalIgnoreCase))
            {
                DeleteKey(SettingsService.LegacySettingsSectionName, Key);
            }
        }

        public void RewriteManagedSettingsSections()
        {
            if (!File.Exists(Path))
            {
                return;
            }

            string[] lines = File.ReadAllLines(Path);
            ParseSections(
                lines,
                out List<string> preambleLines,
                out List<IniSectionBlock> sectionBlocks);

            HashSet<string> managedSections = new HashSet<string>(
                SettingsService.ManagedSettingsSectionOrder.Append(SettingsService.LegacySettingsSectionName),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> managedKeys = new HashSet<string>(SettingsService.SettingsSectionKeyOrder, StringComparer.OrdinalIgnoreCase);

            Dictionary<string, string> legacyUnknownSettings = ReadUnknownLegacySettings(sectionBlocks, managedKeys);
            Dictionary<string, string> managedValues = ReadManagedSettingsValues(sectionBlocks);

            List<string> rewrittenLines = new List<string>();
            rewrittenLines.AddRange(preambleLines);

            AppendManagedSections(rewrittenLines, managedValues);
            AppendLegacyUnknownSettingsSection(rewrittenLines, legacyUnknownSettings);

            foreach (IniSectionBlock block in sectionBlocks)
            {
                if (managedSections.Contains(block.SectionName))
                {
                    continue;
                }

                if (rewrittenLines.Count > 0 && !string.IsNullOrWhiteSpace(rewrittenLines[^1]))
                {
                    rewrittenLines.Add("");
                }

                rewrittenLines.Add(block.HeaderLine);
                rewrittenLines.AddRange(block.ContentLines);
            }

            while (rewrittenLines.Count > 0 && string.IsNullOrWhiteSpace(rewrittenLines[^1]))
            {
                rewrittenLines.RemoveAt(rewrittenLines.Count - 1);
            }

            File.WriteAllLines(Path, rewrittenLines);
        }

        /// <summary>
        /// 지정한 섹션의 키 순서를 재정렬합니다.
        /// preferredKeyOrder에 포함된 키는 해당 순서대로 먼저 배치하고,
        /// 나머지 키는 섹션 끝에 알파벳순으로 이어 붙입니다.
        /// </summary>
        public void SortSectionKeys(string section = "Settings", IEnumerable<string> preferredKeyOrder = null)
        {
            if (!File.Exists(Path))
            {
                return;
            }

            string[] lines = File.ReadAllLines(Path);
            int sectionStartIndex = -1;
            int sectionEndIndex = lines.Length;

            for (int i = 0; i < lines.Length; i++)
            {
                if (!TryParseSectionHeader(lines[i], out string currentSection))
                {
                    continue;
                }

                if (sectionStartIndex < 0)
                {
                    if (string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase))
                    {
                        sectionStartIndex = i;
                    }

                    continue;
                }

                sectionEndIndex = i;
                break;
            }

            if (sectionStartIndex < 0)
            {
                return;
            }

            Dictionary<string, string> keyValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> originalKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = sectionStartIndex + 1; i < sectionEndIndex; i++)
            {
                if (!TryParseKeyValue(lines[i], out string key, out string value))
                {
                    continue;
                }

                keyValues[key] = value;
                originalKeys[key] = key;
            }

            if (keyValues.Count == 0)
            {
                return;
            }

            List<string> orderedKeys = new List<string>();
            HashSet<string> seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (preferredKeyOrder != null)
            {
                foreach (string preferredKey in preferredKeyOrder)
                {
                    if (string.IsNullOrWhiteSpace(preferredKey) ||
                        !keyValues.ContainsKey(preferredKey) ||
                        !seenKeys.Add(preferredKey))
                    {
                        continue;
                    }

                    orderedKeys.Add(preferredKey);
                }
            }

            orderedKeys.AddRange(
                keyValues.Keys
                    .Where(key => seenKeys.Add(key))
                    .OrderBy(key => key, StringComparer.OrdinalIgnoreCase));

            List<string> rewrittenLines = new List<string>();
            rewrittenLines.AddRange(lines.Take(sectionStartIndex));
            rewrittenLines.Add(lines[sectionStartIndex]);

            foreach (string key in orderedKeys)
            {
                rewrittenLines.Add($"{originalKeys[key]}={keyValues[key]}");
            }

            if (sectionEndIndex < lines.Length)
            {
                if (rewrittenLines.Count > 0 &&
                    !string.IsNullOrWhiteSpace(rewrittenLines[^1]) &&
                    !string.IsNullOrWhiteSpace(lines[sectionEndIndex]))
                {
                    rewrittenLines.Add("");
                }

                rewrittenLines.AddRange(lines.Skip(sectionEndIndex));
            }

            File.WriteAllLines(Path, rewrittenLines);
        }

        private static bool TryParseSectionHeader(string line, out string section)
        {
            section = null;
            if (line == null)
            {
                return false;
            }

            string trimmed = line.Trim();
            if (trimmed.Length < 3 || trimmed[0] != '[' || trimmed[^1] != ']')
            {
                return false;
            }

            section = trimmed.Substring(1, trimmed.Length - 2).Trim();
            return section.Length > 0;
        }

        private static bool TryParseKeyValue(string line, out string key, out string value)
        {
            key = null;
            value = null;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string trimmed = line.Trim();
            if (trimmed.StartsWith(";") || trimmed.StartsWith("#"))
            {
                return false;
            }

            int separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0)
            {
                return false;
            }

            key = trimmed.Substring(0, separatorIndex).Trim();
            if (key.Length == 0)
            {
                return false;
            }

            value = trimmed.Substring(separatorIndex + 1);
            return true;
        }

        private string ReadExact(string key, string section)
        {
            var retVal = new StringBuilder(255);
            int length = GetPrivateProfileString(section, key, "", retVal, 255, Path);
            return length == 0 ? null : retVal.ToString();
        }

        private static string ResolveSettingsSection(string key)
        {
            SettingsService settingsService = new SettingsService();
            return settingsService.TryGetSettingsSectionForKey(key, out string section)
                ? section
                : SettingsService.LegacySettingsSectionName;
        }

        private void DeleteKey(string section, string key)
        {
            WritePrivateProfileString(section, key, null, Path);
        }

        private Dictionary<string, string> ReadManagedSettingsValues(IEnumerable<IniSectionBlock> sectionBlocks)
        {
            Dictionary<string, Dictionary<string, string>> parsedSections = ParseSectionValues(sectionBlocks);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in SettingsService.SettingsSectionKeyOrder)
            {
                string mappedSection = ResolveSettingsSection(key);
                if (TryGetParsedValue(parsedSections, mappedSection, key, out string value) ||
                    (!string.Equals(mappedSection, SettingsService.LegacySettingsSectionName, StringComparison.OrdinalIgnoreCase) &&
                     TryGetParsedValue(parsedSections, SettingsService.LegacySettingsSectionName, key, out value)))
                {
                    values[key] = value;
                }
            }

            return values;
        }

        private static Dictionary<string, string> ReadUnknownLegacySettings(
            IEnumerable<IniSectionBlock> sectionBlocks,
            ISet<string> managedKeys)
        {
            var unknownSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IniSectionBlock settingsBlock = sectionBlocks.FirstOrDefault(block =>
                string.Equals(block.SectionName, SettingsService.LegacySettingsSectionName, StringComparison.OrdinalIgnoreCase));

            if (settingsBlock == null)
            {
                return unknownSettings;
            }

            foreach (string line in settingsBlock.ContentLines)
            {
                if (!TryParseKeyValue(line, out string key, out string value) || managedKeys.Contains(key))
                {
                    continue;
                }

                unknownSettings[key] = value;
            }

            return unknownSettings;
        }

        private static void AppendManagedSections(List<string> targetLines, IReadOnlyDictionary<string, string> managedValues)
        {
            foreach (string sectionName in SettingsService.ManagedSettingsSectionOrder)
            {
                if (!SettingsService.ManagedSettingsSections.TryGetValue(sectionName, out string[] keys))
                {
                    continue;
                }

                List<string> sectionLines = keys
                    .Where(key => managedValues.TryGetValue(key, out _))
                    .Select(key => $"{key}={managedValues[key]}")
                    .ToList();

                if (sectionLines.Count == 0)
                {
                    continue;
                }

                if (targetLines.Count > 0 && !string.IsNullOrWhiteSpace(targetLines[^1]))
                {
                    targetLines.Add("");
                }

                targetLines.Add($"[{sectionName}]");
                targetLines.AddRange(sectionLines);
            }
        }

        private static void AppendLegacyUnknownSettingsSection(List<string> targetLines, IReadOnlyDictionary<string, string> unknownSettings)
        {
            if (unknownSettings.Count == 0)
            {
                return;
            }

            if (targetLines.Count > 0 && !string.IsNullOrWhiteSpace(targetLines[^1]))
            {
                targetLines.Add("");
            }

            targetLines.Add($"[{SettingsService.LegacySettingsSectionName}]");
            foreach (KeyValuePair<string, string> entry in unknownSettings.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                targetLines.Add($"{entry.Key}={entry.Value}");
            }
        }

        private static Dictionary<string, Dictionary<string, string>> ParseSectionValues(IEnumerable<IniSectionBlock> sectionBlocks)
        {
            var parsedSections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            foreach (IniSectionBlock block in sectionBlocks ?? Enumerable.Empty<IniSectionBlock>())
            {
                if (!parsedSections.TryGetValue(block.SectionName, out Dictionary<string, string> keyValues))
                {
                    keyValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    parsedSections[block.SectionName] = keyValues;
                }

                foreach (string line in block.ContentLines)
                {
                    if (!TryParseKeyValue(line, out string key, out string value))
                    {
                        continue;
                    }

                    keyValues[key] = value;
                }
            }

            return parsedSections;
        }

        private static bool TryGetParsedValue(
            IReadOnlyDictionary<string, Dictionary<string, string>> parsedSections,
            string section,
            string key,
            out string value)
        {
            value = null;
            return parsedSections.TryGetValue(section, out Dictionary<string, string> keyValues) &&
                keyValues.TryGetValue(key, out value);
        }

        private static void ParseSections(
            IReadOnlyList<string> lines,
            out List<string> preambleLines,
            out List<IniSectionBlock> sectionBlocks)
        {
            preambleLines = new List<string>();
            sectionBlocks = new List<IniSectionBlock>();

            IniSectionBlock currentBlock = null;
            foreach (string line in lines ?? Array.Empty<string>())
            {
                if (TryParseSectionHeader(line, out string section))
                {
                    currentBlock = new IniSectionBlock(section, line);
                    sectionBlocks.Add(currentBlock);
                    continue;
                }

                if (currentBlock == null)
                {
                    preambleLines.Add(line);
                    continue;
                }

                currentBlock.ContentLines.Add(line);
            }
        }

        private sealed class IniSectionBlock
        {
            public IniSectionBlock(string sectionName, string headerLine)
            {
                SectionName = sectionName ?? "";
                HeaderLine = headerLine ?? "";
                ContentLines = new List<string>();
            }

            public string SectionName { get; }
            public string HeaderLine { get; }
            public List<string> ContentLines { get; }
        }
    }
}
