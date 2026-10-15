using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Cataholic;

// cataholic:// links (Cataholic.swift CataholicLinks) — any app, script or CI job can talk to your cat:
//   cataholic://sign?count=3&note=Grafterr   → holds up a sign with 3 (and whispers the note when it goes up)
//   cataholic://sign?count=0                 → puts the sign down
//   cataholic://zoomies                      → zoomies
// On the Mac the system hands the URL to the running app. Windows starts a new process for it (HKCU\Software\Classes\
// cataholic → `"<exe>" --url "%1"`), so that process hands the URL to the running cat over a named pipe and exits at
// once. One cat per user and session: a named mutex. A double-click on the exe is the same thing without a URL.
static class CatLinks
{
    const string Scheme = "cataholic://", MutexName = @"Local\Cataholic";
    static Mutex? single;                                   // held for the life of the process
    static string PipeName => "Cataholic.Link." + System.Diagnostics.Process.GetCurrentProcess().SessionId;

    /// Am I the cat? True: registered the link scheme, listening for links, and `url` (this launch's own) will be handled once
    /// the cat is up. False: another cat runs; `url` was passed to it, and this process should exit.
    public static bool Own(string? url)
    {
        single = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            Console.Error.WriteLine("another Cataholic is running" + (url != null ? "; passing it the link" : ""));
            if (url != null) Forward(url);
            return false;
        }
        Register();
        // Links queue on the UI thread at Background priority: after the startup handler (Normal), when the cat exists.
        var ui = Dispatcher.CurrentDispatcher;
        _ = Serve(ui);
        if (url != null) ui.BeginInvoke(DispatcherPriority.Background, new System.Action(() => Handle(url)));
        return true;
    }

    /// The scheme, per user, at every start (idempotent): no installer, no admin. Left in place on quit, as the Mac keeps its.
    static void Register()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (exe == null || Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) return;   // `dotnet run`: not the cat
            using var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\cataholic");
            k.SetValue("", "URL:Cataholic");
            k.SetValue("URL Protocol", "");
            using (var i = k.CreateSubKey("DefaultIcon")) i.SetValue("", $"\"{exe}\",0");
            using (var c = k.CreateSubKey(@"shell\open\command")) c.SetValue("", $"\"{exe}\" --url \"%1\"");
        }
        catch { /* a locked-down profile: links just don't open the cat */ }
    }

    static void Forward(string url)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            c.Connect(1500);                                // waits for the first cat to start listening
            c.Write(Encoding.UTF8.GetBytes(url + "\n"));
            c.Flush();
        }
        catch (Exception e) { Console.Error.WriteLine("could not pass the link on: " + e.Message); }
    }

    /// One link per connection. Asynchronous waiting: nothing runs (no timer, no thread spinning) until a link arrives.
    static async Task Serve(Dispatcher ui)
    {
        while (true)
        {
            try
            {
                using var s = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                                                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await s.WaitForConnectionAsync();
                using var cts = new CancellationTokenSource(2000);        // a client that connects and says nothing must not hold the pipe
                using var r = new StreamReader(s, Encoding.UTF8);
                string? line = await r.ReadLineAsync(cts.Token);
                if (line != null) _ = ui.BeginInvoke(DispatcherPriority.Background, new System.Action(() => Handle(line)));
            }
            catch (Exception e) when (e is OperationCanceledException or IOException) { }
            catch { await Task.Delay(1000); }                             // the pipe could not be made: try again, don't spin
        }
    }

    /// Run one link. Anything that is not `cataholic://sign` or `cataholic://zoomies` does nothing.
    public static void Handle(string? url)
    {
        if (url == null || url.Length > 2048 || !url.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)) return;
        string rest = url[Scheme.Length..];
        int hash = rest.IndexOf('#'); if (hash >= 0) rest = rest[..hash];
        string query = "";
        int q = rest.IndexOf('?'); if (q >= 0) { query = rest[(q + 1)..]; rest = rest[..q]; }
        string host = rest.Split('/')[0];
        // like URLComponents.queryItems: percent-decoded, "+" stays a plus, the first item with the name wins
        string? V(string key)
        {
            foreach (var item in query.Split('&'))
            {
                int e = item.IndexOf('=');
                if (Uri.UnescapeDataString(e < 0 ? item : item[..e]) == key) return e < 0 ? null : Uri.UnescapeDataString(item[(e + 1)..]);
            }
            return null;
        }
        switch (host)
        {
            case "sign":
                // Int("3") — anything that is not a whole number puts the sign down
                long.TryParse(V("count"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n);
                MailStore.SetSign(n, V("note") ?? "");
                break;
            case "zoomies": CatPanel.StopLife(); CatPanel.Zoomies(); break;
        }
    }
}
