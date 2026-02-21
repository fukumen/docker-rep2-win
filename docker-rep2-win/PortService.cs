using System.Net.NetworkInformation;

namespace docker_rep2_win
{
    public static class PortService
    {
        /// <summary>
        /// 指定されたポートが現在使用中（TCPリスナーとしてアクティブ）かどうかを判定します。
        /// </summary>
        /// <param name="port">確認するポート番号</param>
        /// <returns>使用中の場合は true、それ以外は false</returns>
        public static bool IsPortInUse(int port)
        {
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpListeners = ipGlobalProperties.GetActiveTcpListeners();

            foreach (var listener in tcpListeners)
            {
                if (listener.Port == port)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
