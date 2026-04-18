using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GameTranslator
{
    public class IniFile
    {
        public string Path;

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern long WritePrivateProfileString(string Section, string Key, string Value, string FilePath);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern int GetPrivateProfileString(string Section, string Key, string Default, StringBuilder RetVal, int Size, string FilePath);

        public IniFile(string iniPath)
        {
            Path = new FileInfo(iniPath).FullName;
        }

        public string Read(string Key, string Section = "Settings")
        {
            var RetVal = new StringBuilder(255);
            GetPrivateProfileString(Section, Key, "", RetVal, 255, Path);
            return RetVal.ToString();
        }

        public void Write(string Key, string Value, string Section = "Settings")
        {
            WritePrivateProfileString(Section, Key, Value, Path);
        }
    }
}