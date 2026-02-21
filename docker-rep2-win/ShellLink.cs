using System;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.Shell;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.UI.Shell.PropertiesSystem;
using Windows.Win32.Foundation;
using Windows.Win32.System.Variant;

namespace docker_rep2_win
{
    public static class ShellLinkHelper
    {
        public static void CreateShortcut(string shortcutPath, string targetPath, string arguments, string description, string iconLocation, string? workingDir = null, string? appUserModelId = null)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

            // IShellLinkW インスタンスの作成
            // CsWin32 で生成された ShellLink クラスから GUID を取得
            Guid clsid = typeof(ShellLink).GUID;
            Guid iid = typeof(IShellLinkW).GUID;
            PInvoke.CoCreateInstance(in clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, in iid, out var obj).ThrowOnFailure();
            IShellLinkW link = (IShellLinkW)obj;
            
            link.SetPath(targetPath);
            link.SetArguments(arguments);
            link.SetDescription(description);
            link.SetWorkingDirectory(workingDir ?? Path.GetDirectoryName(targetPath) ?? string.Empty);

            if (!string.IsNullOrEmpty(iconLocation))
            {
                string iconPath = iconLocation;
                int iconIndex = 0;
                int lastComma = iconLocation.LastIndexOf(',');
                if (lastComma > 0)
                {
                    string indexPart = iconLocation.Substring(lastComma + 1).Trim();
                    if (int.TryParse(indexPart, out iconIndex))
                    {
                        iconPath = iconLocation.Substring(0, lastComma).Trim();
                    }
                }
                link.SetIconLocation(iconPath, iconIndex);
            }

            // AppUserModelID の設定
            // これにより Windows 11 のスタートメニューでショートカットが独立して表示されるようになる
            if (!string.IsNullOrEmpty(appUserModelId))
            {
                IPropertyStore store = (IPropertyStore)link;
                
                PROPVARIANT pv = default;
                // VARENUM が解決できれば vt へのアクセスも解決する可能性が高い
                // CsWin32 のバージョンによっては vt は Anonymous 直下にある
                pv.Anonymous.Anonymous.vt = VARENUM.VT_LPWSTR;
                
                unsafe
                {
                    pv.Anonymous.Anonymous.Anonymous.pwszVal = new PWSTR((char*)Marshal.StringToCoTaskMemUni(appUserModelId));
                }

                try
                {
                    store.SetValue(PInvoke.PKEY_AppUserModel_ID, in pv);
                    store.Commit();
                }
                finally
                {
                    PInvoke.PropVariantClear(ref pv);
                }
            }

            // ファイルへの保存
            // システム標準の IPersistFile インターフェースを使用
            if (link is IPersistFile persistFile)
            {
                persistFile.Save(shortcutPath, true);
            }
            else
            {
                throw new InvalidCastException("IShellLinkW インターフェースを IPersistFile にキャストできませんでした。");
            }
        }
    }
}
