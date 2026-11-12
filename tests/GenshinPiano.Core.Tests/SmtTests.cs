using GenshinPiano.Core.Scores;
using GenshinPiano.Ocr.Engine.Smt;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class SmtTests
{
    private static string Score(string data,string meter="4/4") => $"**kern\t**kern\n*M{meter}\t*M{meter}\n=-\t=-\n{data}\n=\t=\n*-\t*-\n";

    [Fact]
    public void FractionsRemainExactAndDefaultIsZero()
    {
        Assert.Equal(Beat.Zero,default(Beat));
        Assert.Equal(new Beat(1,2),new Beat(2,4));
        Assert.Equal(480,(new Beat(4,12)*3).Ticks());
        Assert.Equal(160,new Beat(1,3).Ticks());
    }

    [Fact]
    public void SerializedChordsDotsAndOctavesBecomePlayableNotes()
    {
        var doc=SmtNotation.Parse("**ekern_1.0 <t> **ekern_1.0 <b> *M4/4 <t> *M4/4 <b> =- <t> =- <b> 1C <t> 2.c <s> 2.e <b> . <t> 4g <b> = <t> = <b> *- <t> *- <b>");
        var warnings=new List<string>();
        var score=SmtScoreBuilder.Build([doc],"test",true,warnings);
        Assert.Empty(ScoreValidator.Validate(score));
        var upper=score.Tracks[0].Notes;
        Assert.Equal(new[]{60,64,67},upper.Select(n=>n.Pitch));
        Assert.Equal(1440,upper[0].DurationTick);
        Assert.Equal(1440,upper[2].StartTick);
        Assert.Equal(48,Assert.Single(score.Tracks[1].Notes).Pitch);
    }

    [Fact]
    public void SplitVoicesUseSharedTimeAndPreserveSimultaneousNotes()
    {
        var doc=SmtNotation.Parse("**kern\t**kern\n*\t*^\n=-\t=-\t=-\n1C\t2c\t1e\n.\t2d\t.\n=\t=\t=\n*\t*v\t*v\n*-\t*-\n");
        Assert.Equal(3,doc.Events.Count(e=>e.Staff==1));
        Assert.Equal((Beat)2,doc.Events.Single(e=>e.Pitch==62).Start);
        Assert.Equal((Beat)4,doc.Measures[0].Upper);
    }

    [Fact]
    public void TiesAcrossSystemsMergeAndAccompanimentCanBeOmitted()
    {
        var first=SmtNotation.Parse(Score("1C\t1c["));
        var second=SmtNotation.Parse(Score("1D\t1c]"));
        var score=SmtScoreBuilder.Build([first,second],"tie",false,[]);
        var note=Assert.Single(Assert.Single(score.Tracks).Notes);
        Assert.Equal(3840,note.DurationTick);
        Assert.Equal(0,note.StartTick);
    }

    [Fact]
    public void TripletsAndMeterChangesDoNotAccumulateRounding()
    {
        var doc=SmtNotation.Parse(Score("4C\t12c\n.\t12d\n.\t12e"));
        var score=SmtScoreBuilder.Build([doc],"triplets",true,[]);
        Assert.Equal(new long[]{0,160,320},score.Tracks[0].Notes.Select(n=>n.StartTick));
        var next=SmtNotation.Parse(Score("2.D\t2.d","3/4"));
        var combined=SmtScoreBuilder.Build([doc,next],"meters",true,[]);
        Assert.Equal(480,combined.Timing.TimeSignatures[1].Tick);
        Assert.Equal(3,combined.Timing.TimeSignatures[1].Numerator);
    }

    [Theory]
    [InlineData("**kern\t**kern\n1C\t1c\n")]
    [InlineData("**kern\t**kern\n1C\t1c\n=\t=\n*-\t*-\n1D\t1d\n")]
    [InlineData("**kern\t**kern\n*v\t*\n")]
    public void InvalidOrTruncatedNotationCannotBecomeAScore(string text) => Assert.Throws<InvalidDataException>(()=>SmtNotation.Parse(text));

    [Fact]
    public void CandidateCannotChangeCleanMeasuresOrEvadeBaselineBudget()
    {
        var baseline=SmtQuality.Assess(Score("1C\t1c\n=\t=\n2.C\t1c\n=\t=\n1C\t1c"),3);
        var candidate=SmtQuality.Assess(Score("1D\t1c\n=\t=\n1C\t1c\n=\t=\n1C\t1c"),3,baseline);
        Assert.True(SmtQuality.ChangesUnflaggedMeasures(baseline,candidate));
        var shortened=SmtQuality.Assess(Score("2.C\t1c\n=\t=\n1C\t2.c\n=\t=\n2.C\t2.c"),3,baseline);
        Assert.Contains("staff_duration",shortened.Issues);
    }

    [Fact]
    public void ThreeFourAndWeakPickupAreNotForcedToFourBeats()
    {
        Assert.False(SmtQuality.Assess(Score("2.C\t2.c","3/4"),1).NeedsReview);
        Assert.False(SmtQuality.Assess(Score("4C\t4c"),1).NeedsReview);
    }

    [Fact]
    public void LayoutFindsShortSystemsAndMissingLinesButRejectsOmittedSkewedSystem()
    {
        static GrayImage Make(bool tilted)
        {
            const int width=500,height=520;
            var pixels=Enumerable.Repeat((byte)255,width*height).ToArray();
            foreach (var top in new[]{50,150,300,400})
                for (var x=30;x<(top>=300?240:450);x++)
                    for (var line=0;line<5;line++)
                    {
                        if (top==50 && line==2) continue;
                        var shift=tilted && top>=300?(int)Math.Round(x*0.02):0;
                        pixels[(top+line*10+shift)*width+x]=0;
                    }
            foreach (var top in new[]{50,300})
                foreach (var x in new[]{30,140,top==50?449:239})
                {
                    var shift=tilted && top>=300?(int)Math.Round(x*0.02):0;
                    for (var y=top+shift;y<=top+140+shift;y++) pixels[y*width+x]=0;
                }
            for (var x=5;x<490;x++) pixels[250*width+x]=0;
            return new(width,height,pixels);
        }
        var layout=SmtLayoutDetector.Detect(Make(false));
        Assert.Equal(2,layout.Systems.Count);
        Assert.Equal(1,layout.InferredLines);
        Assert.Throws<InvalidDataException>(()=>SmtLayoutDetector.Detect(Make(true)));
    }
}
