using System.Text.Json;
using GenshinPiano.Application.Ocr;
using GenshinPiano.Core.Scores;
using GenshinPiano.Ocr.Engine.Smt;

namespace GenshinPiano.Ocr.Engine;

internal sealed class StaffNotationRecognizer
{
    private sealed record Candidate(int Attempt,int Spacing,int? WhiteAbove,string Tokens,SmtAssessment Quality,bool Rejected);
    private static bool Chinese(OcrAnalysisRequest request) => request.Language.StartsWith("zh",StringComparison.OrdinalIgnoreCase);
    private static OcrAnalysisResult Failure(string code,string message) => new(OcrProtocol.CurrentVersion,false,null,ErrorCode:code,ErrorMessage:message);

    public async Task<OcrAnalysisResult> RecognizeAsync(OcrAnalysisRequest request,CancellationToken cancellationToken)
    {
        if (!File.Exists(request.ImagePath)) return Failure("image_not_found","The source image does not exist.");
        var runtime=Path.Combine(AppContext.BaseDirectory,"staff-omr","smt");
        var work=Path.Combine(Path.GetTempPath(),"GenshinPiano-SMT-"+Guid.NewGuid().ToString("N"));
        string? diagnostics=null;
        Directory.CreateDirectory(work);
        var diagnosticRoot=Environment.GetEnvironmentVariable("GENSHINPIANO_SMT_DIAGNOSTICS");
        if (!string.IsNullOrWhiteSpace(diagnosticRoot))
        {
            diagnostics=Path.Combine(Path.GetFullPath(diagnosticRoot),Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(diagnostics);
        }
        try
        {
            await SmtNativeRunner.ValidateAsync(runtime,cancellationToken);
            OcrProgressReporter.Report(OcrProgressStage.Preparing,0.03);
            GrayImage image;
            SmtLayout layout;
            try
            {
                (image,layout)=await Task.Run(()=>
                {
                    var decoded=GrayImage.Load(request.ImagePath);
                    return (decoded,SmtLayoutDetector.Detect(decoded,cancellationToken));
                },cancellationToken);
            }
            catch (InvalidDataException)
            {
                return Failure("staff_scan_layout_unsupported",Chinese(request)
                    ? "无法完整定位钢琴上下谱表。请使用清晰、平直的扫描谱或电子谱截图；不支持拍照谱及自动照片矫正。"
                    : "Cannot locate the complete piano staves. Use a clear, flat scan or rendered score image; photographs and automatic rectification are unsupported.");
            }
            OcrProgressReporter.Report(OcrProgressStage.TextDetection,0.12);
            var documents=new List<SmtDocument>();
            var warnings=new List<string>();
            var selections=new List<object>();
            int? white=request.WatermarkMode switch { OcrWatermarkMode.Off=>null,OcrWatermarkMode.Strong=>200,_=>210 };
            var variants=new[]{(Spacing:8,White:white),(Spacing:9,White:white),(Spacing:8,White:(int?)null),(Spacing:7,White:white)}.Distinct().ToArray();
            for (var index=0;index<layout.Systems.Count;index++)
            {
                var system=layout.Systems[index];
                var candidates=new List<Candidate>();
                Candidate? selected=null;
                for (var attempt=0;attempt<variants.Length;attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (selected is not null && !selected.Quality.NeedsReview) break;
                    var (spacing,whiten)=variants[attempt];
                    var input=Path.Combine(work,$"system-{index+1:00}-candidate-{attempt}.png");
                    image.Render(system,input,spacing,whiten);
                    var native=await SmtNativeRunner.RunAsync(Path.Combine(runtime,"crispembed.exe"),Path.Combine(runtime,SmtNativeRunner.ModelName),input,cancellationToken);
                    var assessment=native.TimedOut || native.ExitCode!=0
                        ? new SmtAssessment(10000,null,null,[],[],["backend_failure"],native.TimedOut?"SMT timed out.":native.Error)
                        : SmtQuality.Assess(native.Tokens,system.ExpectedMeasures,candidates.FirstOrDefault()?.Quality);
                    var rejected=attempt>0 && assessment.Fatal is null && candidates[0].Quality.Fatal is null
                        && SmtQuality.ChangesUnflaggedMeasures(candidates[0].Quality,assessment);
                    var candidate=new Candidate(attempt,spacing,whiten,native.Tokens,assessment,rejected);
                    candidates.Add(candidate);
                    if (!rejected && (selected is null || assessment.Score<selected.Quality.Score)) selected=candidate;
                    if (diagnostics is not null)
                    {
                        var prefix=Path.Combine(diagnostics,$"system-{index+1:00}-candidate-{attempt}");
                        File.Copy(input,prefix+".png");
                        await File.WriteAllTextAsync(prefix+".tokens.txt",native.Tokens,cancellationToken);
                        await File.WriteAllTextAsync(prefix+".stderr.log",native.Error,cancellationToken);
                        await File.WriteAllTextAsync(prefix+".json",JsonSerializer.Serialize(new
                        {
                            attempt,spacing,whiteAbove=whiten,rejected,assessment.Score,assessment.Issues,assessment.Fatal,
                            budget=assessment.Budget?.ToString(),assessment.SuspectMeasures,
                            native.Seconds,native.ExitCode,native.TimedOut,system.ExpectedMeasures,
                        }),cancellationToken);
                    }
                    OcrProgressReporter.Report(OcrProgressStage.TextDetection,0.12+0.7*(index+(attempt+1d)/variants.Length)/layout.Systems.Count);
                }
                selected ??=candidates[0];
                selections.Add(new { system=index+1,selected.Attempt,selected.Spacing,selected.WhiteAbove,selected.Quality.Score,selected.Quality.Issues,selected.Quality.Fatal });
                if (selected.Quality.NeedsReview || selected.Quality.Document is null)
                {
                    if (diagnostics is not null) await File.WriteAllTextAsync(Path.Combine(diagnostics,"selection.json"),JsonSerializer.Serialize(selections),cancellationToken);
                    return Failure("staff_scan_needs_review",Chinese(request)
                        ? $"第 {index+1} 行在重识别后仍存在谱表、小节或节奏异常，未导入不完整结果。请换用更清晰、平直的谱图。"
                        : $"System {index+1} still has notation, measure or rhythm anomalies after retries. The incomplete result was not imported. Use a clearer, flat score image.");
                }
                documents.Add(selected.Quality.Document);
                if (diagnostics is not null) await File.WriteAllTextAsync(Path.Combine(diagnostics,$"system-{index+1:00}.selected.tokens.txt"),selected.Tokens,cancellationToken);
            }
            OcrProgressReporter.Report(OcrProgressStage.ScoreReconstruction,0.88);
            var score=SmtScoreBuilder.Build(documents,Path.GetFileNameWithoutExtension(request.ImagePath),request.IncludeAccompaniment,warnings);
            var validation=ScoreValidator.Validate(score);
            if (validation.Count>0) throw new InvalidDataException(string.Join("; ",validation));
            warnings.Add("Only clear, flat piano scans are supported. Review recognized notes, rhythm and ties before playback.");
            if (Chinese(request))
            {
                warnings=warnings.Select(w=>w switch
                {
                    "Tempo was not recognized; the initial tempo is 120 BPM. Adjust it after importing."=>"未识别速度，初始设置为 120 BPM，请导入后调整。",
                    "No initial time signature was recognized; 4/4 is used for display. Review the time signature."=>"未识别首个拍号，显示暂用 4/4，请核对拍号。",
                    "The image ends with open ties; review the ending durations."=>"图片末尾有延续到后续页面的连音，请核对结尾时值。",
                    "An incoming tie could not be matched; review tied notes."=>"部分连音起点位于图片之外，请核对连音。",
                    "Only clear, flat piano scans are supported. Review recognized notes, rhythm and ties before playback."=>"仅支持清晰平直的钢琴扫描谱，播放前请核对音符、节奏和连音。",
                    _=>w,
                }).ToList();
            }
            if (diagnostics is not null) await File.WriteAllTextAsync(Path.Combine(diagnostics,"selection.json"),JsonSerializer.Serialize(selections),cancellationToken);
            OcrProgressReporter.Report(OcrProgressStage.ScoreReconstruction,1);
            return new(OcrProtocol.CurrentVersion,true,score,0.8,warnings.Distinct().ToArray());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Failure("staff_smt_failure",Chinese(request)?"五线谱识别失败，请确认 OCR 附加包完整并使用清晰平直谱图。"+error.Message:error.Message);
        }
        finally
        {
            try { Directory.Delete(work,true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
