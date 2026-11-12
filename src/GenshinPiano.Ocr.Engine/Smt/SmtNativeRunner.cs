using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace GenshinPiano.Ocr.Engine.Smt;

internal sealed record SmtNativeOutput(string Tokens,string Error,int ExitCode,bool TimedOut,double Seconds);

internal static class SmtNativeRunner
{
    public const string ModelName="smt-grandstaff-q8_0.gguf";
    public const string ModelSha256="f6612885a8d7015bef5ab7f045a37a0f667575f4b65b324a353cf348d6edc752";
    public static readonly string[] RuntimeFiles=["crispembed.exe","crispembed.dll","ggml.dll","ggml-base.dll","ggml-cpu.dll","msvcp140.dll","vcruntime140.dll","vcruntime140_1.dll",ModelName];

    public static async Task ValidateAsync(string root,CancellationToken cancellationToken)
    {
        foreach (var name in RuntimeFiles)
            if (!File.Exists(Path.Combine(root,name))) throw new FileNotFoundException($"SMT runtime file is missing: {name}");
        await using var model=File.OpenRead(Path.Combine(root,ModelName));
        var hash=await SHA256.HashDataAsync(model,cancellationToken);
        if (!Convert.ToHexString(hash).Equals(ModelSha256,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The SMT model failed integrity verification. Reinstall the OCR add-on.");
    }

    internal static async Task<SmtNativeOutput> RunAsync(string executable,string model,string image,CancellationToken cancellationToken,TimeSpan? limit=null)
    {
        var info=new ProcessStartInfo
        {
            FileName=executable,WorkingDirectory=Path.GetDirectoryName(executable)!,UseShellExecute=false,CreateNoWindow=true,
            WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,
            StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,
        };
        info.Environment["SMT_OCR_FORCE_CPU"]="1";
        foreach (var argument in new[]{"-m",model,"-t",Math.Min(4,Math.Max(1,Environment.ProcessorCount)).ToString(),"--ocr",image}) info.ArgumentList.Add(argument);
        using var process=new Process { StartInfo=info };
        var watch=Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();
        if (!process.Start()) throw new InvalidOperationException("Cannot start the SMT backend.");
        var stdout=process.StandardOutput.ReadToEndAsync();
        var stderr=process.StandardError.ReadToEndAsync();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit ?? TimeSpan.FromSeconds(60));
        var timedOut=false;
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            timedOut=!cancellationToken.IsCancellationRequested;
            try { if (!process.HasExited) process.Kill(entireProcessTree:true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout,stderr);
            cancellationToken.ThrowIfCancellationRequested();
        }
        watch.Stop();
        return new(await stdout,await stderr,process.ExitCode,timedOut,watch.Elapsed.TotalSeconds);
    }
}
