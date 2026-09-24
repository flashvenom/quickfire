using System;
using System.Collections.Generic;

namespace Quickfire.Shared.Bridge
{
    // Kept compatible with C# 7.3 for the .NET Framework Office tray host.
    public sealed class BridgeCommand
    {
        public string RequestId { get; set; } = string.Empty;
        public DateTimeOffset ExpiresUtc { get; set; }
        public string Command { get; set; } = string.Empty;
        public List<string> Parameters { get; set; } = new List<string>();
    }

    public sealed class BridgeResponse
    {
        public string RequestId { get; set; } = string.Empty;
        public bool Succeeded { get; set; }
        public string Error { get; set; } = string.Empty;
        public List<string> Data { get; set; } = new List<string>();
    }

    public static class BridgeCommands
    {
        private static readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "OutlookSearch_EmailStrictToFrom", "OutlookSearch_EmailBroad", "OutlookSearch_Policy",
            "OutlookSearch_SmartSearch", "OutlookSearch_Carrier", "OutlookEmail_CreateNew",
            "Windows_OpenFolder", "Windows_OpenFile", "Windows_ShowCallNotification",
            "GetWordDocContents", "ShowTrayNotification", "ShowStaffChat", "update_available", "update_prompt"
        };

        public static bool IsAllowed(string command)
        {
            return command != null && Allowed.Contains(command);
        }
    }
}
