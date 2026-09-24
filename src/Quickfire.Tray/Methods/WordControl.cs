using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Quickfire.Shared.Bridge;
using Word = Microsoft.Office.Interop.Word;

namespace Quickfire.Tray
{
    public static class WordControl
    {
        // Completion carries the original request, never a Windows user name or an uncorrelated command name.
        public static void PerformWordFunction(BridgeCommand command, Action<BridgeResponse> complete)
        {
            var response = new BridgeResponse { RequestId = command.RequestId, Succeeded = false, Data = new List<string>() };
            Word.Application application = null;
            Word.Document document = null;
            Word.Range range = null;
            try
            {
                if (command.Command != "GetWordDocContents") throw new NotSupportedException();
                application = (Word.Application)Marshal.GetActiveObject("Word.Application");
                document = application.ActiveDocument;
                range = document.Content;
                var text = range.Text ?? string.Empty;
                if (text.Length > 262144) response.Error = "The active document exceeds the bridge size limit.";
                else { response.Data.Add(text); response.Succeeded = true; }
            }
            catch { response.Error = "Unable to read the active Word document. Open a document in classic Word and try again."; }
            finally { Release(range); Release(document); Release(application); }
            complete(response);
        }

        private static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }
    }
}
