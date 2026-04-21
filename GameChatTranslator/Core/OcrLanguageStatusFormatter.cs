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

            return string.Join(Environment.NewLine, items.Select(BuildLine));
        }

        public string BuildLine(OcrLanguageStatusEntry entry)
        {
            string marker = entry.EngineAvailable
                ? "OK"
                : entry.IsCapabilityInstalled ? "WARN" : "NO";

            string line = $"{marker}  {entry.Label} ({entry.AppLanguageTag}) : {(entry.EngineAvailable ? "사용 가능" : "미감지")}";
            if (entry.NeedsRebootHint)
            {
                line += " (capability 설치됨, 재부팅 필요 가능)";
            }

            return line;
        }
    }
}
