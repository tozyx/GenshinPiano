using GenshinPiano.Ocr.Engine;

namespace GenshinPiano.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Dispatch before creating WPF, audio, update checks or the single-instance lock.
        if (args.Length > 0 && args[0] == "--ocr-worker")
            return OcrWorkerHost.RunAsync(args[1..]).GetAwaiter().GetResult();

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
