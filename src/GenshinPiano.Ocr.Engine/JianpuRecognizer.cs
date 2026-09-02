using GenshinPiano.Application.Ocr;
using GenshinPiano.Core.Scores;
using RapidOcrNet;
using SkiaSharp;

namespace GenshinPiano.Ocr.Engine;

internal sealed class JianpuRecognizer
{
    private const int DefaultPpq = 480;
    private const int TileSize = 1536;
    private const int TileOverlap = 192;
    private static readonly IReadOnlyDictionary<char, int> ScalePitch = new Dictionary<char, int>
    {
        ['1'] = 60, ['2'] = 62, ['3'] = 64, ['4'] = 65,
        ['5'] = 67, ['6'] = 69, ['7'] = 71,
    };

    public async Task<OcrAnalysisResult> RecognizeAsync(
        OcrAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        OcrProgressReporter.Report(OcrProgressStage.Preparing, 0.02);
        if (request.NotationHint == OcrNotationHint.Staff)
        {
            return await new StaffNotationRecognizer().RecognizeAsync(request, cancellationToken);
        }

        if (!File.Exists(request.ImagePath))
        {
            return Failure("image_not_found", "The source image does not exist.");
        }

        using var decoded = SKBitmap.Decode(request.ImagePath);
        if (decoded is null)
        {
            return Failure("image_decode_failed", "The source image could not be decoded.");
        }

        SKBitmap? suppressedImage = null;
        SuppressionStatistics? suppression = null;
        if (request.WatermarkMode != OcrWatermarkMode.Off)
        {
            OcrProgressReporter.Report(OcrProgressStage.WatermarkSuppression, 0.08);
            suppressedImage = WatermarkSuppressor.Create(
                decoded,
                request.WatermarkMode,
                out var createdSuppression);
            suppression = createdSuppression;
            OcrProgressReporter.Report(OcrProgressStage.WatermarkSuppression, 0.18);
        }

        using var suppressed = suppressedImage;
        if (suppressed is not null && DiagnosticsEnabled())
        {
            Console.Error.WriteLine(
                $"OCR watermark suppression: mode={request.WatermarkMode}, " +
                $"background={suppression!.EstimatedBackground}, " +
                $"preserve<={suppression.PreserveBelow}, erase>={suppression.EraseAbove}, " +
                $"changed={suppression.ChangedPercent:F2}%");
        }

        // RapidOcrNet 2.0.0 throws from Dispose when its optional classifier
        // model is not initialized. This one-request worker is reclaimed on exit.
        var ocr = new RapidOcr();
        ocr.InitModels();
        var options = RapidOcrOptions.Default with
        {
            // Preserve small notation glyphs instead of shrinking a whole long
            // page to the package default of 1024 pixels.
            ImgResize = TileSize,
            DoAngle = false,
            ReturnWordBox = true,
            ReturnSingleCharBox = true,
            TextScore = 0.30f,
        };

        // The original branch remains authoritative. Faint accompaniment notes
        // can have the same luminance as a watermark, so the suppressed branch
        // is only invoked as a recovery path when OCR has clearly struggled.
        OcrProgressReporter.Report(OcrProgressStage.TextDetection, 0.24);
        var originalCandidates = await DetectCandidatesAsync(
            ocr,
            decoded,
            options,
            cancellationToken);
        var originalAverageScore = originalCandidates.Count == 0
            ? 0
            : originalCandidates.Average(candidate => candidate.Score);
        var hintCandidates = originalCandidates;
        if (suppressed is not null &&
            (originalCandidates.Count < 8 || originalAverageScore < 0.55f))
        {
            var suppressedCandidates = await DetectCandidatesAsync(
                ocr,
                suppressed,
                options,
                cancellationToken);
            if (suppressedCandidates.Count > originalCandidates.Count ||
                (suppressedCandidates.Count > 0 && originalCandidates.Count == 0))
            {
                hintCandidates = suppressedCandidates;
            }

            if (DiagnosticsEnabled())
            {
                Console.Error.WriteLine(
                    $"OCR watermark recovery: original={originalCandidates.Count} " +
                    $"({originalAverageScore:F3}), suppressed={suppressedCandidates.Count}, " +
                    $"selected={(ReferenceEquals(hintCandidates, suppressedCandidates) ? "suppressed" : "original")}");
            }
        }

        OcrProgressReporter.Report(OcrProgressStage.TextDetection, 0.46);
        hintCandidates = AnalyzeGeometry(decoded, hintCandidates);
        var ocrSeedRows = ClusterRows(hintCandidates);
        var seedRows = DiscoverProjectionSeedRows(decoded, ocrSeedRows);
        OcrProgressReporter.Report(OcrProgressStage.SuperResolution, 0.52);
        var projectedCandidates = DetectProjectionCandidates(
            decoded,
            suppressed,
            request.WatermarkMode,
            seedRows);
        List<GlyphCandidate> candidates;
        if (projectedCandidates.Count >= originalCandidates.Count * 0.70f)
        {
            candidates = projectedCandidates;
        }
        else if (DiagnosticsEnabled())
        {
            candidates = AnalyzeGeometry(decoded, originalCandidates);
            Console.Error.WriteLine(
                $"OCR projection fallback: projected={projectedCandidates.Count}, " +
                $"ocr={originalCandidates.Count}");
        }
        else
        {
            candidates = AnalyzeGeometry(decoded, originalCandidates);
        }

        OcrProgressReporter.Report(OcrProgressStage.SuperResolution, 0.78);
        var rows = FilterNotationRows(ClusterRows(candidates), decoded.Width);
        rows = MarkTies(suppressedImage ?? decoded, rows);
        if (rows.Count == 0)
        {
            return Failure("no_notes_detected", "No numbered-note rows were detected in the image.");
        }

        var systems = GroupSystems(rows);
        WriteLayoutDiagnostics(rows, systems);
        OcrProgressReporter.Report(OcrProgressStage.ScoreReconstruction, 0.86);
        var tracks = BuildTracks(systems, request.IncludeAccompaniment);
        if (tracks.Count == 0)
        {
            return Failure("no_notes_detected", "No numbered notes from 1 to 7 were detected in the image.");
        }

        var score = ScoreDocument.CreateEmpty(Path.GetFileNameWithoutExtension(request.ImagePath)) with
        {
            Tracks = tracks,
            Playback = new PlaybackSettings
            {
                Mapping = "full-pitch",
                OutOfRangePolicy = OutOfRangePolicy.Drop,
            },
        };
        var confidence = candidates.Count == 0
            ? 0
            : candidates.Average(candidate => (double)candidate.Score);
        return new OcrAnalysisResult(
            OcrProtocol.CurrentVersion,
            Success: true,
            score,
            Math.Clamp(confidence, 0, 1),
            Warnings:
            [
                !request.IncludeAccompaniment && systems.Any(system => system.Rows.Count > 1)
                    ? "Accompaniment voices were detected but excluded from the imported score."
                    : tracks.Count > 1
                        ? "Multiple numbered-notation voices were inferred from the page layout. Review their alignment."
                        : "A single numbered-notation voice was inferred from the page layout.",
                "Octave dots, rhythm underlines and augmentation dots were inferred geometrically. Review low-confidence symbols before playback.",
                "Recognized accidentals and extension dashes were preserved. Review curved ties and other low-confidence notation marks.",
            ]);
    }

    private static async Task<List<GlyphCandidate>> DetectCandidatesAsync(
        RapidOcr ocr,
        SKBitmap source,
        RapidOcrOptions options,
        CancellationToken cancellationToken)
    {
        var detected = new List<GlyphCandidate>();
        var step = TileSize - TileOverlap;
        for (var y = 0; y < source.Height; y += step)
        {
            for (var x = 0; x < source.Width; x += step)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var width = Math.Min(TileSize, source.Width - x);
                var height = Math.Min(TileSize, source.Height - y);
                using var tile = new SKBitmap(width, height, source.ColorType, source.AlphaType);
                using (var canvas = new SKCanvas(tile))
                {
                    canvas.Clear(SKColors.White);
                    canvas.DrawBitmap(
                        source,
                        new SKRect(x, y, x + width, y + height),
                        new SKRect(0, 0, width, height));
                }

                var result = await Task.Run(() => ocr.Detect(tile, options), cancellationToken);
                AddCandidates(result, x, y, detected);
            }
        }

        return Deduplicate(detected);
    }

    private static void AddCandidates(
        OcrResult result,
        int offsetX,
        int offsetY,
        ICollection<GlyphCandidate> destination)
    {
        foreach (var block in result.TextBlocks)
        {
            if (block.WordResults is { Length: > 0 })
            {
                foreach (var word in block.WordResults)
                {
                    if (word.Text.Length == 1 && IsCandidateCharacter(word.Text[0]))
                    {
                        destination.Add(new GlyphCandidate(
                            NormalizeCandidateCharacter(word.Text[0]),
                            word.Score,
                            Bounds(word.BoxPoints, offsetX, offsetY)));
                    }
                }

                continue;
            }

            var notationCharacters = block.Text.Where(IsCandidateCharacter).ToArray();
            if (notationCharacters.Length == 0)
            {
                continue;
            }

            var box = Bounds(block.BoxPoints, offsetX, offsetY);
            var charWidth = box.Width / notationCharacters.Length;
            for (var index = 0; index < notationCharacters.Length; index++)
            {
                destination.Add(new GlyphCandidate(
                    NormalizeCandidateCharacter(notationCharacters[index]),
                    block.CharScores is { Length: > 0 }
                        ? block.CharScores[Math.Min(index, block.CharScores.Length - 1)]
                        : block.BoxScore,
                    new SKRect(
                        box.Left + (charWidth * index), box.Top,
                        box.Left + (charWidth * (index + 1)), box.Bottom)));
            }
        }
    }

    private static SKRect Bounds(SKPointI[] points, int offsetX, int offsetY) => new(
        points.Min(point => point.X) + offsetX,
        points.Min(point => point.Y) + offsetY,
        points.Max(point => point.X) + offsetX,
        points.Max(point => point.Y) + offsetY);

    private static bool IsCandidateCharacter(char character) =>
        character is >= '0' and <= '7' or '#' or 'b' or '-' or '♯' or '♭';

    private static char NormalizeCandidateCharacter(char character) => character switch
    {
        '♯' => '#',
        '♭' => 'b',
        _ => character,
    };

    private static List<GlyphCandidate> RefineCandidates(
        SKBitmap source,
        List<GlyphCandidate> candidates)
    {
        var modelPath = Path.Combine(
            AppContext.BaseDirectory,
            "models",
            "orpheusnet",
            "orpheusnet-middle.onnx");
        if (!File.Exists(modelPath))
        {
            return candidates;
        }

        using var classifier = new OrpheusNetClassifier(modelPath);
        var refined = new List<GlyphCandidate>(candidates.Count);
        var agreements = 0;
        var disagreements = 0;
        foreach (var candidate in candidates)
        {
            if (candidate.Bounds.Width < 1 || candidate.Bounds.Height < 1)
            {
                refined.Add(candidate);
                continue;
            }

            var classificationBounds = candidate.Features.DigitBounds ?? candidate.Bounds;
            var result = classifier.Classify(source, classificationBounds);
            var value = result.Label switch
            {
                { Length: 1 } label when label[0] is >= '0' and <= '7' => label[0],
                _ => candidate.Value,
            };
            var modelProducedNotation = value is >= '0' and <= '7';
            var agrees = value == candidate.Value;
            if (!agrees && modelProducedNotation && DiagnosticsEnabled())
            {
                Console.Error.WriteLine(
                    $"OCR disagreement: ocr={candidate.Value}, cnn={value}, " +
                    $"confidence={result.Confidence:F3}, margin={result.Margin:F3}");
            }
            if (agrees)
            {
                agreements++;
            }
            else
            {
                disagreements++;
            }

            // The upstream model is over-confident on OCR-reconstructed crops.
            // It may confirm an OCR digit, but must not replace a disagreement.
            refined.Add(agrees && modelProducedNotation
                ? candidate with
                {
                    Score = (float)((candidate.Score + result.Confidence) / 2d),
                }
                : candidate);
        }

        if (DiagnosticsEnabled())
        {
            Console.Error.WriteLine(
                $"OCR classifier: input={candidates.Count}, agreements={agreements}, " +
                $"disagreements={disagreements}");
        }

        return Deduplicate(refined);
    }

    private static List<GlyphCandidate> AnalyzeGeometry(
        SKBitmap source,
        List<GlyphCandidate> candidates) =>
        candidates
            .Select(candidate => candidate.Value is not '#' and not 'b'
                ? candidate with
                {
                    Geometry = NotationGeometryAnalyzer.Analyze(source, candidate.Bounds),
                }
                : candidate)
            .ToList();

    private static List<GlyphCandidate> DetectProjectionCandidates(
        SKBitmap source,
        SKBitmap? suppressed,
        OcrWatermarkMode watermarkMode,
        IReadOnlyList<NotationRow> seedRows)
    {
        var modelPath = Path.Combine(
            AppContext.BaseDirectory,
            "models",
            "orpheusnet",
            "orpheusnet-middle.onnx");
        if (!File.Exists(modelPath) || seedRows.Count == 0)
        {
            return [];
        }

        using var classifier = new OrpheusNetClassifier(modelPath);
        var result = new List<GlyphCandidate>();
        foreach (var row in seedRows)
        {
            var digitBounds = row.Glyphs
                .Select(glyph => glyph.Features.DigitBounds ?? glyph.Bounds)
                .Where(bounds => bounds.Width >= 2 && bounds.Height >= 3)
                .ToArray();
            if (digitBounds.Length == 0)
            {
                continue;
            }

            var bodyHeight = Median(digitBounds.Select(bounds => bounds.Height));
            var bodyCenter = Median(digitBounds.Select(bounds => bounds.MidY));
            var bandTop = Math.Clamp(
                (int)Math.Floor(bodyCenter - (bodyHeight * 0.58f)),
                0,
                source.Height - 1);
            var bandBottom = Math.Clamp(
                (int)Math.Ceiling(bodyCenter + (bodyHeight * 0.58f)),
                bandTop + 1,
                source.Height);
            var runs = FindProjectionRuns(source, bandTop, bandBottom);
            foreach (var run in runs)
            {
                var bounds = FindInkBounds(source, run.Left, run.Right, bandTop, bandBottom);
                if (bounds is null ||
                    IsLikelyBarline(bounds.Value, bodyHeight) ||
                    IsAccessorySymbol(bounds.Value, bodyHeight))
                {
                    continue;
                }

                var classifiedBounds = bounds.Value;
                char? connectedAccidental = null;
                // A leading accidental is sometimes connected to the following
                // digit by anti-aliasing, so the complete run may be confidently
                // misclassified as another digit. Split a sufficiently wide run
                // at its internal whitespace before classifying it. Accidentals
                // are deliberately discarded because the 21-key target has no
                // chromatic pitches; preserving the main numbered note is more
                // important than retaining the unsupported modifier.
                var accidentalDigit = TryExtractDigitAfterAccidental(
                    classifier,
                    source,
                    classifiedBounds,
                    bodyHeight);
                if (accidentalDigit is not null)
                {
                    connectedAccidental = accidentalDigit.Accidental;
                    classifiedBounds = accidentalDigit.Bounds;
                }

                var classification = ClassifyWithWatermarkSuppression(
                    classifier,
                    source,
                    suppressed,
                    classifiedBounds,
                    watermarkMode);
                if (classification.Label.Length != 1 && DiagnosticsEnabled())
                {
                    Console.Error.WriteLine(
                        $"OCR non-digit candidate: label={classification.Label}, " +
                        $"x={classifiedBounds.Left:F0}, w={classifiedBounds.Width:F0}, " +
                        $"h={classifiedBounds.Height:F0}, bodyHeight={bodyHeight:F1}");
                }
                if (classification.Label is "sharp" or "flat")
                {
                    connectedAccidental = classification.Label == "sharp" ? '#' : 'b';
                    var recovered = TryRecoverDigitAfterAccidental(
                        classifier,
                        source,
                        classifiedBounds,
                        bodyHeight);
                    if (recovered is null)
                    {
                        continue;
                    }

                    classifiedBounds = recovered.Bounds;
                    classification = recovered.Classification;
                }

                if (classification.Label.Length != 1 ||
                    classification.Label[0] is < '0' or > '7' ||
                    classification.Confidence < 0.55)
                {
                    continue;
                }

                if (classification.Label[0] == '0' &&
                    IsClassifiedBarline(classifiedBounds, bodyHeight))
                {
                    continue;
                }
                var geometry = NotationGeometryAnalyzer.Analyze(source, classifiedBounds);
                if (connectedAccidental is { } accidental)
                {
                    result.Add(new GlyphCandidate(
                        accidental,
                        (float)classification.Confidence,
                        new SKRect(bounds.Value.Left, bounds.Value.Top, classifiedBounds.Left, bounds.Value.Bottom)));
                }
                result.Add(new GlyphCandidate(
                    classification.Label[0],
                    (float)classification.Confidence,
                    classifiedBounds,
                    geometry));
            }
        }

        if (DiagnosticsEnabled())
        {
            Console.Error.WriteLine(
                $"OCR projection: seedRows={seedRows.Count}, candidates={result.Count}");
        }

        return Deduplicate(RemoveRepeatedLeftMarginArtifacts(
            result,
            seedRows.Count,
            source.Width));
    }

    private static List<GlyphCandidate> RemoveRepeatedLeftMarginArtifacts(
        List<GlyphCandidate> candidates,
        int rowCount,
        int pageWidth)
    {
        if (rowCount < 6 || candidates.Count == 0)
        {
            return candidates;
        }

        var bucketWidth = Math.Max(4, pageWidth / 260);
        var minimumRepeats = Math.Max(5, rowCount / 3);
        var artifactKeys = candidates
            .Where(candidate =>
                candidate.Bounds.MidX < pageWidth * 0.12f &&
                candidate.Features.UnderlineCount == 0 &&
                candidate.Features.OctaveShift == 0)
            .GroupBy(candidate => (
                candidate.Value,
                X: (int)Math.Round(candidate.Bounds.MidX / bucketWidth)))
            .Where(group => group.Count() >= minimumRepeats)
            .Select(group => group.Key)
            .ToHashSet();
        if (artifactKeys.Count == 0)
        {
            return candidates;
        }

        var filtered = candidates
            .Where(candidate => !artifactKeys.Contains((
                candidate.Value,
                X: (int)Math.Round(candidate.Bounds.MidX / bucketWidth))))
            .ToList();
        if (DiagnosticsEnabled())
        {
            Console.Error.WriteLine(
                $"OCR left-margin artifacts: removed={candidates.Count - filtered.Count}, " +
                $"patterns={string.Join(',', artifactKeys.Select(key => $"{key.Value}@{key.X * bucketWidth}"))}");
        }

        return filtered;
    }

    private static OrpheusNetClassifier.ClassificationResult ClassifyWithWatermarkSuppression(
        OrpheusNetClassifier classifier,
        SKBitmap original,
        SKBitmap? suppressed,
        SKRect bounds,
        OcrWatermarkMode mode)
    {
        var primary = classifier.Classify(original, bounds);
        if (suppressed is null || mode == OcrWatermarkMode.Off)
        {
            return primary;
        }

        var primaryIsDigit = IsNumberedNoteLabel(primary.Label);
        var shouldInspectSuppressed = mode == OcrWatermarkMode.Strong ||
                                      !primaryIsDigit ||
                                      primary.Confidence < 0.82 ||
                                      primary.Margin < 0.24;
        if (!shouldInspectSuppressed)
        {
            return primary;
        }

        // The suppressed crop has already gone through contrast normalization;
        // running SRCNN again adds substantial latency and can amplify the hard
        // threshold edge. Use the base Orpheus classifier for this branch.
        var cleaned = classifier.ClassifyWithoutEnhancement(suppressed, bounds);
        if (cleaned.Label == primary.Label)
        {
            return cleaned.Confidence > primary.Confidence ? cleaned : primary;
        }

        var cleanedIsDigit = IsNumberedNoteLabel(cleaned.Label);
        var adopt = mode switch
        {
            OcrWatermarkMode.Auto =>
                !primaryIsDigit && cleanedIsDigit &&
                cleaned.Confidence >= 0.88 && cleaned.Margin >= 0.28,
            OcrWatermarkMode.Strong =>
                cleanedIsDigit && cleaned.Confidence >= 0.90 &&
                cleaned.Margin >= 0.35 &&
                (!primaryIsDigit || cleaned.Confidence >= primary.Confidence + 0.05),
            _ => false,
        };
        if (cleaned.Label != primary.Label && DiagnosticsEnabled())
        {
            Console.Error.WriteLine(
                $"OCR watermark {(adopt ? "replacement" : "disagreement")}: mode={mode}, " +
                $"original={primary.Label} ({primary.Confidence:F3}/{primary.Margin:F3}), " +
                $"cleaned={cleaned.Label} ({cleaned.Confidence:F3}/{cleaned.Margin:F3}), " +
                $"x={bounds.Left:F0}, y={bounds.Top:F0}");
        }

        return adopt ? cleaned : primary;
    }

    private static bool IsNumberedNoteLabel(string label) =>
        label.Length == 1 && label[0] is >= '0' and <= '7';

    private static List<NotationRow> DiscoverProjectionSeedRows(
        SKBitmap source,
        IReadOnlyList<NotationRow> ocrRows)
    {
        var estimatedBodyHeight = ocrRows.Count > 0
            ? Median(ocrRows
                .SelectMany(row => row.Glyphs)
                .Select(glyph => (glyph.Features.DigitBounds ?? glyph.Bounds).Height)
                .Where(height => height >= 3))
            : EstimateBodyHeightFromHorizontalProjection(source);
        if (estimatedBodyHeight < 3)
        {
            return ocrRows.ToList();
        }

        var inkCounts = CountHorizontalInk(source);
        var minimumInk = Math.Max(3, source.Width / 420);
        var maximumGap = Math.Max(1, (int)Math.Round(estimatedBodyHeight * 0.18f));
        var ranges = FindActiveRowRanges(inkCounts, minimumInk, maximumGap);
        var discovered = new List<NotationRow>();
        foreach (var (top, bottom) in ranges)
        {
            var height = bottom - top;
            if (height < estimatedBodyHeight * 0.48f ||
                height > estimatedBodyHeight * 1.65f)
            {
                continue;
            }

            var center = (top + bottom) / 2d;
            if (discovered.Any(row =>
                    Math.Abs(row.CenterY - center) < estimatedBodyHeight * 0.55f))
            {
                continue;
            }

            var dummyBounds = new SKRect(
                0,
                (float)(center - (estimatedBodyHeight / 2f)),
                Math.Max(3, estimatedBodyHeight * 0.55f),
                (float)(center + (estimatedBodyHeight / 2f)));
            discovered.Add(new NotationRow(
                [new GlyphCandidate('1', 0.5f, dummyBounds)],
                center));
        }

        // OCR rows remain useful hints for extremely sparse lines, but no
        // longer decide whether a line exists. Projection-discovered rows take
        // priority and OCR only fills vertical gaps that projection missed.
        foreach (var row in ocrRows)
        {
            if (discovered.All(candidate =>
                    Math.Abs(candidate.CenterY - row.CenterY) >= estimatedBodyHeight * 0.55f))
            {
                discovered.Add(row);
            }
        }

        var ordered = discovered.OrderBy(row => row.CenterY).ToList();
        if (DiagnosticsEnabled())
        {
            Console.Error.WriteLine(
                $"OCR row discovery: bodyHeight={estimatedBodyHeight:F1}, " +
                $"projectionRows={ranges.Count}, accepted={ordered.Count}, " +
                $"ocrHints={ocrRows.Count}");
        }

        return ordered;
    }

    private static int[] CountHorizontalInk(SKBitmap source)
    {
        const int inkThreshold = 135;
        var counts = new int[source.Height];
        for (var y = 0; y < source.Height; y++)
        {
            var count = 0;
            for (var x = 0; x < source.Width; x++)
            {
                var color = source.GetPixel(x, y);
                var luminance = ((77 * color.Red) + (150 * color.Green) + (29 * color.Blue)) >> 8;
                if (luminance < inkThreshold)
                {
                    count++;
                }
            }

            counts[y] = count;
        }

        return counts;
    }

    private static List<(int Top, int Bottom)> FindActiveRowRanges(
        IReadOnlyList<int> inkCounts,
        int minimumInk,
        int maximumGap)
    {
        var ranges = new List<(int Top, int Bottom)>();
        var start = -1;
        var lastActive = -1;
        for (var y = 0; y < inkCounts.Count; y++)
        {
            if (inkCounts[y] >= minimumInk)
            {
                start = start < 0 ? y : start;
                lastActive = y;
                continue;
            }

            if (start >= 0 && y - lastActive > maximumGap)
            {
                ranges.Add((start, lastActive + 1));
                start = -1;
                lastActive = -1;
            }
        }

        if (start >= 0)
        {
            ranges.Add((start, lastActive + 1));
        }

        return ranges;
    }

    private static float EstimateBodyHeightFromHorizontalProjection(SKBitmap source)
    {
        var counts = CountHorizontalInk(source);
        var ranges = FindActiveRowRanges(
            counts,
            Math.Max(3, source.Width / 420),
            maximumGap: 1);
        var maximumHeight = Math.Max(12, source.Height / 30f);
        var heights = ranges
            .Select(range => range.Bottom - range.Top)
            .Where(height => height >= 5 && height <= maximumHeight)
            .ToArray();
        if (heights.Length == 0)
        {
            return 0;
        }

        var mode = heights
            .GroupBy(height => (int)Math.Round(height / 2d) * 2)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key)
            .First()
            .Key;
        var neighborhood = heights
            .Where(height => Math.Abs(height - mode) <= Math.Max(2, mode * 0.30f))
            .Select(height => (float)height);
        return Median(neighborhood);
    }

    private static List<ProjectionRun> FindProjectionRuns(
        SKBitmap source,
        int top,
        int bottom)
    {
        const int inkThreshold = 145;
        var hasInk = new bool[source.Width];
        for (var x = 0; x < source.Width; x++)
        {
            for (var y = top; y < bottom; y++)
            {
                var color = source.GetPixel(x, y);
                var luminance = ((77 * color.Red) + (150 * color.Green) + (29 * color.Blue)) >> 8;
                if (luminance < inkThreshold)
                {
                    hasInk[x] = true;
                    break;
                }
            }
        }

        // Close a one-pixel gap caused by anti-aliasing, but keep the visible
        // whitespace between adjacent numbered notes.
        for (var x = 1; x < hasInk.Length - 1; x++)
        {
            if (!hasInk[x] && hasInk[x - 1] && hasInk[x + 1])
            {
                hasInk[x] = true;
            }
        }

        var runs = new List<ProjectionRun>();
        var start = -1;
        for (var x = 0; x <= hasInk.Length; x++)
        {
            var ink = x < hasInk.Length && hasInk[x];
            if (ink && start < 0)
            {
                start = x;
            }
            else if (!ink && start >= 0)
            {
                if (x - start >= 2)
                {
                    runs.Add(new ProjectionRun(start, x));
                }

                start = -1;
            }
        }

        return runs;
    }

    private static SKRect? FindInkBounds(
        SKBitmap source,
        int left,
        int right,
        int top,
        int bottom)
    {
        const int inkThreshold = 145;
        var minX = right;
        var minY = bottom;
        var maxX = left;
        var maxY = top;
        var found = false;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var color = source.GetPixel(x, y);
                var luminance = ((77 * color.Red) + (150 * color.Green) + (29 * color.Blue)) >> 8;
                if (luminance >= inkThreshold)
                {
                    continue;
                }

                found = true;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return found
            ? new SKRect(minX, minY, maxX + 1, maxY + 1)
            : null;
    }

    private static bool IsLikelyBarline(SKRect bounds, float bodyHeight)
    {
        var aspect = bounds.Width / Math.Max(1, bounds.Height);
        return bounds.Height >= bodyHeight * 0.85f && aspect < 0.14f;
    }

    private static bool IsClassifiedBarline(SKRect bounds, float bodyHeight)
    {
        var aspect = bounds.Width / Math.Max(1, bounds.Height);
        return bounds.Height >= bodyHeight * 0.80f && aspect < 0.35f;
    }

    private static bool IsAccessorySymbol(SKRect bounds, float bodyHeight) =>
        bounds.Height < bodyHeight * 0.50f &&
        bounds.Width < bodyHeight * 0.50f;

    private static RecoveredDigit? TryRecoverDigitAfterAccidental(
        OrpheusNetClassifier classifier,
        SKBitmap source,
        SKRect combinedBounds,
        float bodyHeight)
    {
        if (combinedBounds.Width < bodyHeight * 0.70f)
        {
            return null;
        }

        var left = (int)Math.Floor(combinedBounds.Left);
        var right = (int)Math.Ceiling(combinedBounds.Right);
        var top = (int)Math.Floor(combinedBounds.Top);
        var bottom = (int)Math.Ceiling(combinedBounds.Bottom);
        var searchLeft = left + Math.Max(1, (int)(combinedBounds.Width * 0.20f));
        var searchRight = right - Math.Max(2, (int)(combinedBounds.Width * 0.32f));
        var bestX = -1;
        var lowestInk = int.MaxValue;
        for (var x = searchLeft; x <= searchRight; x++)
        {
            var ink = 0;
            for (var y = top; y < bottom; y++)
            {
                var color = source.GetPixel(x, y);
                var luminance = ((77 * color.Red) + (150 * color.Green) + (29 * color.Blue)) >> 8;
                if (luminance < 145)
                {
                    ink++;
                }
            }

            if (ink < lowestInk)
            {
                lowestInk = ink;
                bestX = x;
            }
        }

        if (bestX < 0 || lowestInk > Math.Max(2, combinedBounds.Height * 0.16f))
        {
            return null;
        }

        var digitBounds = FindInkBounds(source, bestX + 1, right, top, bottom);
        if (digitBounds is null || digitBounds.Value.Height < bodyHeight * 0.65f)
        {
            return null;
        }

        var classification = classifier.Classify(source, digitBounds.Value);
        var accidentalBounds = FindInkBounds(source, left, bestX, top, bottom);
        var accidental = accidentalBounds is null
            ? null
            : classifier.Classify(source, accidentalBounds.Value).Label switch
            {
                "sharp" => (char?)'#',
                "flat" => 'b',
                _ => null,
            };
        return classification.Label.Length == 1 &&
               classification.Label[0] is >= '0' and <= '7' &&
               classification.Confidence >= 0.70
            ? new RecoveredDigit(digitBounds.Value, classification, accidental)
            : null;
    }

    private static RecoveredDigit? TryExtractDigitAfterAccidental(
        OrpheusNetClassifier classifier,
        SKBitmap source,
        SKRect combinedBounds,
        float bodyHeight)
    {
        // Normal Jianpu digits are substantially narrower than a connected
        // accidental-plus-digit pair. Keep this conservative to avoid splitting
        // ordinary wide glyphs such as 4 and 5.
        if (combinedBounds.Width < bodyHeight * 0.90f ||
            combinedBounds.Height < bodyHeight * 0.65f)
        {
            return null;
        }

        var left = Math.Clamp((int)Math.Floor(combinedBounds.Left), 0, source.Width - 1);
        var right = Math.Clamp((int)Math.Ceiling(combinedBounds.Right), left + 1, source.Width);
        var top = Math.Clamp((int)Math.Floor(combinedBounds.Top), 0, source.Height - 1);
        var bottom = Math.Clamp((int)Math.Ceiling(combinedBounds.Bottom), top + 1, source.Height);
        var width = right - left;

        // Search around the middle of the combined glyph. The separator may
        // contain a few antialiased pixels, therefore choose the lowest-density
        // column instead of requiring a completely empty one.
        var searchLeft = left + Math.Max(2, (int)Math.Round(width * 0.28f));
        var searchRight = right - Math.Max(2, (int)Math.Round(width * 0.30f));
        if (searchLeft >= searchRight)
        {
            return null;
        }

        var bestX = -1;
        var lowestInk = int.MaxValue;
        for (var x = searchLeft; x <= searchRight; x++)
        {
            var ink = 0;
            for (var y = top; y < bottom; y++)
            {
                var color = source.GetPixel(x, y);
                var luminance = ((77 * color.Red) + (150 * color.Green) + (29 * color.Blue)) >> 8;
                if (luminance < 145)
                {
                    ink++;
                }
            }

            if (ink < lowestInk)
            {
                lowestInk = ink;
                bestX = x;
            }
        }

        if (bestX < 0 || lowestInk > Math.Max(2, combinedBounds.Height * 0.18f))
        {
            return null;
        }

        var digitBounds = FindInkBounds(source, bestX + 1, right, top, bottom);
        if (digitBounds is null ||
            digitBounds.Value.Height < bodyHeight * 0.65f ||
            digitBounds.Value.Width < bodyHeight * 0.18f)
        {
            return null;
        }

        var leftPartWidth = bestX - left + 1;
        var gapToDigit = digitBounds.Value.Left - bestX;
        if (leftPartWidth < bodyHeight * 0.18f || gapToDigit > bodyHeight * 0.30f)
        {
            return null;
        }

        var classification = classifier.Classify(source, digitBounds.Value);
        if (classification.Label.Length != 1 ||
            classification.Label[0] is < '0' or > '7' ||
            classification.Confidence < 0.70)
        {
            return null;
        }

        if (DiagnosticsEnabled())
        {
            Console.Error.WriteLine(
                $"OCR accidental ignored: digit={classification.Label[0]}, " +
                $"combinedX={combinedBounds.Left:F0}, combinedW={combinedBounds.Width:F0}, " +
                $"digitX={digitBounds.Value.Left:F0}, digitW={digitBounds.Value.Width:F0}");
        }

        return new RecoveredDigit(digitBounds.Value, classification);
    }

    private static List<GlyphCandidate> Deduplicate(List<GlyphCandidate> candidates)
    {
        var accepted = new List<GlyphCandidate>();
        foreach (var candidate in candidates.OrderByDescending(item => item.Score))
        {
            var duplicate = accepted.Any(existing =>
                IntersectionOverUnion(existing.Bounds, candidate.Bounds) >= 0.35f);
            if (!duplicate)
            {
                accepted.Add(candidate);
            }
        }

        return accepted;
    }

    private static float IntersectionOverUnion(SKRect left, SKRect right)
    {
        var intersection = SKRect.Intersect(left, right);
        if (intersection.IsEmpty)
        {
            return 0;
        }

        var intersectionArea = intersection.Width * intersection.Height;
        var unionArea = (left.Width * left.Height) + (right.Width * right.Height) - intersectionArea;
        return unionArea <= 0 ? 0 : intersectionArea / unionArea;
    }

    private static List<NotationRow> ClusterRows(List<GlyphCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var medianHeight = Median(candidates.Select(item => item.Bounds.Height));
        var centerTolerance = Math.Max(8, medianHeight * 0.75f);
        var rows = new List<List<GlyphCandidate>>();
        foreach (var candidate in candidates.OrderBy(item => item.CenterY))
        {
            var row = rows
                .Where(items => Math.Abs(items.Average(item => item.CenterY) - candidate.CenterY) <= centerTolerance)
                .OrderBy(items => Math.Abs(items.Average(item => item.CenterY) - candidate.CenterY))
                .FirstOrDefault();
            if (row is null)
            {
                rows.Add([candidate]);
            }
            else
            {
                row.Add(candidate);
            }
        }

        // Metadata normally contributes only one or two digits; phrases have
        // enough horizontally separated symbols to be useful as music rows.
        return rows
            .Where(items => items.Count >= 3)
            .Select(items => new NotationRow(
                items.OrderBy(item => item.Bounds.Left).ToList(),
                items.Average(item => item.CenterY)))
            .OrderBy(row => row.CenterY)
            .ToList();
    }

    private static List<NotationRow> FilterNotationRows(
        List<NotationRow> rows,
        int pageWidth)
    {
        if (rows.Count == 0 || pageWidth <= 0)
        {
            return rows;
        }

        var firstStrongRow = rows.FindIndex(row =>
        {
            var glyphs = row.Glyphs.OrderBy(glyph => glyph.Bounds.Left).ToArray();
            var coverage = (glyphs[^1].Bounds.Right - glyphs[0].Bounds.Left) / pageWidth;
            return (glyphs.Length >= 4 && coverage >= 0.24f) ||
                   (glyphs.Length >= 8 && coverage >= 0.14f);
        });
        if (firstStrongRow < 0)
        {
            return rows;
        }

        // Metadata such as 1=C, 4/4 and BPM values often produces a handful of
        // valid digit classifications in a small corner before the first real
        // music row. Once music has started, retain every row so sparse endings
        // are not deleted by a page-wide coverage rule.
        var notationRows = rows.Skip(firstStrongRow).ToList();
        while (notationRows.Count >= 4)
        {
            var gaps = notationRows
                .Zip(notationRows.Skip(1), (top, bottom) => bottom.CenterY - top.CenterY)
                .ToArray();
            var firstGap = gaps[0];
            var laterMedian = Median(gaps.Skip(1).Select(gap => (float)gap));
            var similarFirstGaps = gaps.Count(gap =>
                Math.Abs(gap - firstGap) <= Math.Max(12, firstGap * 0.18));
            if (firstGap >= laterMedian * 0.80 || similarFirstGaps > 2)
            {
                break;
            }

            if (DiagnosticsEnabled())
            {
                Console.Error.WriteLine(
                    $"OCR pre-score row removed: y={notationRows[0].CenterY:F1}, " +
                    $"glyphs={notationRows[0].Glyphs.Count}, gap={firstGap:F1}, " +
                    $"laterMedian={laterMedian:F1}");
            }

            notationRows.RemoveAt(0);
        }

        return notationRows;
    }

    private static List<NotationSystem> GroupSystems(List<NotationRow> rows)
    {
        if (rows.Count < 2)
        {
            return rows.Select(row => new NotationSystem([row])).ToList();
        }

        var gaps = rows.Zip(rows.Skip(1), (top, bottom) => (float)(bottom.CenterY - top.CenterY)).ToArray();
        var (smallGapCenter, largeGapCenter) = ClusterGapCenters(gaps);
        if (smallGapCenter <= 0 || largeGapCenter / smallGapCenter < 1.45f)
        {
            return rows.Select(row => new NotationSystem([row])).ToList();
        }

        var pairThreshold = (smallGapCenter + largeGapCenter) / 2f;
        var systems = new List<NotationSystem>();
        for (var index = 0; index < rows.Count;)
        {
            if (index + 1 < rows.Count && rows[index + 1].CenterY - rows[index].CenterY <= pairThreshold)
            {
                systems.Add(new NotationSystem([rows[index], rows[index + 1]]));
                index += 2;
            }
            else
            {
                systems.Add(new NotationSystem([rows[index]]));
                index++;
            }
        }

        return systems;
    }

    private static (float Small, float Large) ClusterGapCenters(float[] gaps)
    {
        var small = gaps.Min();
        var large = gaps.Max();
        if (Math.Abs(large - small) < 1)
        {
            return (small, large);
        }

        for (var iteration = 0; iteration < 20; iteration++)
        {
            var smallValues = gaps.Where(gap => Math.Abs(gap - small) <= Math.Abs(gap - large)).ToArray();
            var largeValues = gaps.Where(gap => Math.Abs(gap - small) > Math.Abs(gap - large)).ToArray();
            if (smallValues.Length == 0 || largeValues.Length == 0)
            {
                break;
            }

            var nextSmall = smallValues.Average();
            var nextLarge = largeValues.Average();
            if (Math.Abs(nextSmall - small) < 0.1f && Math.Abs(nextLarge - large) < 0.1f)
            {
                small = nextSmall;
                large = nextLarge;
                break;
            }

            small = nextSmall;
            large = nextLarge;
        }

        return small <= large ? (small, large) : (large, small);
    }

    private static List<ScoreTrack> BuildTracks(
        List<NotationSystem> systems,
        bool includeAccompaniment)
    {
        var voiceCount = includeAccompaniment
            ? Math.Max(1, systems.Max(system => system.Rows.Count))
            : 1;
        var trackNotes = Enumerable.Range(0, voiceCount).Select(_ => new List<NoteEvent>()).ToArray();
        long systemStartTick = 0;
        for (var systemIndex = 0; systemIndex < systems.Count; systemIndex++)
        {
            var system = systems[systemIndex];
            var primaryAnchors = new List<TimelineAnchor>();
            long primaryTick = 0;
            var primaryAlteration = 0;
            var tieFromPrevious = false;
            foreach (var glyph in system.Rows[0].Glyphs)
            {
                if (glyph.Value is '#' or 'b')
                {
                    primaryAlteration = glyph.Value == '#' ? 1 : -1;
                    continue;
                }

                if (glyph.Value == '-' && trackNotes[0].Count > 0)
                {
                    ExtendPreviousNote(trackNotes[0], DefaultPpq);
                    primaryTick += DefaultPpq;
                    continue;
                }

                var rhythmTick = RhythmTick(glyph);
                if (glyph.Value == '0')
                {
                    primaryAnchors.Add(new TimelineAnchor(glyph.Bounds.MidX, primaryTick));
                    primaryTick += rhythmTick;
                    primaryAlteration = 0;
                    tieFromPrevious = false;
                }
                else if (ScalePitch.TryGetValue(glyph.Value, out var pitch))
                {
                    primaryAnchors.Add(new TimelineAnchor(glyph.Bounds.MidX, primaryTick));
                    var resolvedPitch = pitch + primaryAlteration + (glyph.Features.OctaveShift * 12);
                    if (tieFromPrevious && trackNotes[0].Count > 0 && trackNotes[0][^1].Pitch == resolvedPitch)
                    {
                        ExtendPreviousNote(trackNotes[0], rhythmTick);
                    }
                    else
                    {
                        trackNotes[0].Add(CreateNote(resolvedPitch, systemStartTick + primaryTick, rhythmTick));
                    }
                    primaryTick += rhythmTick;
                    primaryAlteration = 0;
                    tieFromPrevious = glyph.TieToNext;
                }
            }

            long systemTicks = Math.Max(DefaultPpq, primaryTick);
            for (var voice = 1; voice < Math.Min(system.Rows.Count, voiceCount); voice++)
            {
                var pendingAlteration = 0;
                var firstVoiceTick = -1L;
                var tieFromPreviousVoice = false;
                foreach (var glyph in system.Rows[voice].Glyphs)
                {
                    if (glyph.Value is '#' or 'b')
                    {
                        pendingAlteration = glyph.Value == '#' ? 1 : -1;
                        continue;
                    }

                    var relativeTick = MapHorizontalPositionToTick(
                        glyph.Bounds.MidX,
                        primaryAnchors,
                        primaryTick);

                    if (glyph.Value == '-' && trackNotes[voice].Count > 0)
                    {
                        ExtendPreviousNote(trackNotes[voice], DefaultPpq);
                        systemTicks = Math.Max(systemTicks, relativeTick + DefaultPpq);
                        continue;
                    }

                    if (glyph.Value == '0')
                    {
                        pendingAlteration = 0;
                        tieFromPreviousVoice = false;
                        continue;
                    }

                    if (!ScalePitch.TryGetValue(glyph.Value, out var pitch))
                    {
                        continue;
                    }

                    var rhythmTick = RhythmTick(glyph);
                    firstVoiceTick = firstVoiceTick < 0 ? relativeTick : firstVoiceTick;
                    var resolvedPitch = pitch + pendingAlteration + (glyph.Features.OctaveShift * 12);
                    if (tieFromPreviousVoice && trackNotes[voice].Count > 0 && trackNotes[voice][^1].Pitch == resolvedPitch)
                    {
                        ExtendPreviousNote(trackNotes[voice], rhythmTick);
                    }
                    else
                    {
                        trackNotes[voice].Add(CreateNote(resolvedPitch, systemStartTick + relativeTick, rhythmTick));
                    }
                    systemTicks = Math.Max(systemTicks, relativeTick + rhythmTick);
                    pendingAlteration = 0;
                    tieFromPreviousVoice = glyph.TieToNext;
                }

                if (DiagnosticsEnabled() && firstVoiceTick >= 0)
                {
                    Console.Error.WriteLine(
                        $"OCR voice alignment: system={systemIndex}, voice={voice + 1}, " +
                        $"leadingOffset={firstVoiceTick}");
                }
            }

            systemStartTick += systemTicks;
        }

        return trackNotes
            .Select((notes, index) => new ScoreTrack
            {
                Id = $"ocr-voice-{index + 1}",
                Name = voiceCount == 1 ? "OCR" : $"OCR Voice {index + 1}",
                Notes = notes,
            })
            .Where(track => track.Notes.Count > 0)
            .ToList();
    }

    private static int RhythmTick(GlyphCandidate glyph)
    {
        var rhythmTick = DefaultPpq >> glyph.Features.UnderlineCount;
        return glyph.Features.IsDotted ? (rhythmTick * 3) / 2 : rhythmTick;
    }

    private static NoteEvent CreateNote(int pitch, long startTick, int rhythmTick) => new()
    {
        Pitch = pitch,
        StartTick = startTick,
        RhythmTick = rhythmTick,
        DurationTick = Math.Max(1, (rhythmTick * 4) / 5),
        DurationMode = DurationMode.Auto,
        Articulation = NoteArticulation.Natural,
    };

    private static void ExtendPreviousNote(List<NoteEvent> notes, int extensionTick)
    {
        var previous = notes[^1];
        var rhythmTick = Math.Max(1, previous.RhythmTick ?? previous.DurationTick) + extensionTick;
        notes[^1] = previous with
        {
            RhythmTick = rhythmTick,
            DurationTick = Math.Max(1, (rhythmTick * 4) / 5),
        };
    }

    private static long MapHorizontalPositionToTick(
        float x,
        IReadOnlyList<TimelineAnchor> anchors,
        long primaryDuration)
    {
        if (anchors.Count == 0)
        {
            return 0;
        }

        if (x <= anchors[0].X)
        {
            return anchors[0].Tick;
        }

        for (var index = 1; index < anchors.Count; index++)
        {
            var right = anchors[index];
            if (x > right.X)
            {
                continue;
            }

            var left = anchors[index - 1];
            var width = Math.Max(1, right.X - left.X);
            var progress = Math.Clamp((x - left.X) / width, 0, 1);
            var interpolated = left.Tick + ((right.Tick - left.Tick) * progress);
            const int minimumGrid = DefaultPpq / 8;
            return Math.Max(0, (long)Math.Round(interpolated / minimumGrid) * minimumGrid);
        }

        return Math.Max(0, primaryDuration);
    }

    private static float Median(IEnumerable<float> values)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2f
            : sorted[middle];
    }

    private static List<NotationRow> MarkTies(SKBitmap source, List<NotationRow> rows)
    {
        return rows.Select(row =>
        {
            var glyphs = row.Glyphs.OrderBy(glyph => glyph.Bounds.Left).ToList();
            for (var index = 0; index < glyphs.Count; index++)
            {
                var left = glyphs[index];
                if (!ScalePitch.ContainsKey(left.Value)) continue;
                var nextIndex = index + 1;
                while (nextIndex < glyphs.Count && glyphs[nextIndex].Value is '#' or 'b') nextIndex++;
                if (nextIndex >= glyphs.Count) continue;
                var right = glyphs[nextIndex];
                if (right.Value != left.Value ||
                    right.Features.OctaveShift != left.Features.OctaveShift ||
                    !NotationGeometryAnalyzer.HasTieArc(source, left.Bounds, right.Bounds)) continue;
                glyphs[index] = left with { TieToNext = true };
            }
            return new NotationRow(glyphs, row.CenterY);
        }).ToList();
    }

    private static void WriteLayoutDiagnostics(
        IReadOnlyList<NotationRow> rows,
        IReadOnlyList<NotationSystem> systems)
    {
        if (!DiagnosticsEnabled())
        {
            return;
        }

        Console.Error.WriteLine(
            $"OCR layout: {rows.Count} rows, {systems.Count} systems, " +
            $"voices={systems.Max(system => system.Rows.Count)}");
        for (var index = 0; index < rows.Count; index++)
        {
            var gap = index == 0 ? 0 : rows[index].CenterY - rows[index - 1].CenterY;
            Console.Error.WriteLine(
                $"  row {index}: y={rows[index].CenterY:F1}, gap={gap:F1}, " +
                $"glyphs={rows[index].Glyphs.Count}, " +
                $"text={new string(rows[index].Glyphs.Select(glyph => glyph.Value).ToArray())}, " +
                $"underlines={string.Join(',', rows[index].Glyphs.Select(glyph => glyph.Features.UnderlineCount))}, " +
                $"dotted={string.Join(',', rows[index].Glyphs.Select(glyph => glyph.Features.IsDotted ? 1 : 0))}");
        }
    }

    private static bool DiagnosticsEnabled() =>
        string.Equals(
            Environment.GetEnvironmentVariable("GENSHINPIANO_OCR_DIAGNOSTICS"),
            "1",
            StringComparison.Ordinal);

    private static OcrAnalysisResult Failure(string code, string message) => new(
        OcrProtocol.CurrentVersion,
        Success: false,
        Score: null,
        ErrorCode: code,
        ErrorMessage: message);

    private sealed record GlyphCandidate(
        char Value,
        float Score,
        SKRect Bounds,
        GeometryFeatures? Geometry = null,
        bool TieToNext = false)
    {
        public float CenterY => (Bounds.Top + Bounds.Bottom) / 2f;
        public GeometryFeatures Features => Geometry ?? GeometryFeatures.Empty;
    }

    private sealed record NotationRow(List<GlyphCandidate> Glyphs, double CenterY);
    private sealed record NotationSystem(List<NotationRow> Rows);

    private sealed record TimelineAnchor(float X, long Tick);
    private sealed record ProjectionRun(int Left, int Right);
    private sealed record RecoveredDigit(
        SKRect Bounds,
        OrpheusNetClassifier.ClassificationResult Classification,
        char? Accidental = null);
}
