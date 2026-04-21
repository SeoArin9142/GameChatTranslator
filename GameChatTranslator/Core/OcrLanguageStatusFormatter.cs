using System;
using System.Collections.Generic;
using System.Linq;

namespace GameTranslator
{
    public sealed class OcrLanguageStatusEntry
    {
        public OcrLanguageStatusEntry(string label, string appLanguageTag, string capabilityLanguageTag, string capabilityState, bool engineAvailable)
        {
            Label = label ?? "";
            AppLanguageTag = appLanguageTag ?? "";
            CapabilityLanguageTag = capabilityLanguageTag ?? "";
            CapabilityState = string.IsNullOrWhiteSpace(capabilityState) ? "Unknown" : capabilityState.Trim();
            EngineAvailable = engineAvailable;
        }

        public string Label { get; }
        public string AppLanguageTag { get; }
        public string CapabilityLanguageTag { get; }
        public string CapabilityState { get; }
        public bool EngineAvailable { get; }

        public bool IsCapabilityInstalled =>
            string.Equals(CapabilityState, "Installed", StringComparison.OrdinalIgnoreCase);

        public bool NeedsRebootHint => IsCapabilityInstalled && !EngineAvailable;
    }

    public sealed class OcrLanguageStatusFormatter
    {
        public string GetCapabilityLanguageTag(string appLanguageTag)
        {
            return (appLanguageTag ?? "").Trim() switch
            {
                "ko" => "ko-KR",
                "en-US" => "en-US",
                "zh-Hans-CN" => "zh-CN",
                "ja" => "ja-JP",
                "ru" => "ru-RU",
                _ => (appLanguageTag ?? "").Trim()
            };
        }

        public OcrLanguageStatusEntry CreateEntry(string label, string appLanguageTag, string capabilityState, bool engineAvailable)
        {
            return new OcrLanguageStatusEntry(
                label,
                appLanguageTag,
                GetCapabilityLanguageTag(appLanguageTag),
                capabilityState,
                engineAvailable);
        }

        public string BuildDisplayText(IEnumerable<OcrLanguageStatusEntry> entries)
        {
            List<OcrLanguageStatusEntry> items = entries?.ToList() ?? new List<OcrLanguageStatusEntry>();
            if (items.Count == 0)
            {
                return "OCR 언어팩 상태 확인 실패";
            }

            List<string> lines = items.Select(BuildLine).ToList();
            if (items.Any(item => item.NeedsRebootHint))
            {
                lines.Add("");
                lines.Add("안내: capability는 설치됐지만 OCR 엔진이 아직 생성되지 않은 언어가 있습니다. 재부팅 후 다시 확인하세요.");
            }

            return string.Join(Environment.NewLine, lines);
        }

        public string BuildLine(OcrLanguageStatusEntry entry)
        {
            string marker = entry.EngineAvailable
                ? "OK"
                : entry.IsCapabilityInstalled ? "WARN" : "NO";

            string line = $"{marker}  {entry.Label} ({entry.AppLanguageTag}) - capability: {FormatCapabilityState(entry.CapabilityState)} / OCR 엔진: {(entry.EngineAvailable ? "사용 가능" : "미감지")}";
            if (entry.NeedsRebootHint)
            {
                line += " / 재부팅 필요 가능";
            }

            return line;
        }

        public string FormatCapabilityState(string capabilityState)
        {
            string state = string.IsNullOrWhiteSpace(capabilityState) ? "Unknown" : capabilityState.Trim();
            return state switch
            {
                "Installed" => "설치됨(Installed)",
                "NotPresent" => "미설치(NotPresent)",
                "Removed" => "미설치(Removed)",
                "InstallPending" => "설치 후 재시작 대기(InstallPending)",
                "Staged" => "설치 준비됨(Staged)",
                "Unknown" => "확인 실패",
                _ => state
            };
        }
    }
}
