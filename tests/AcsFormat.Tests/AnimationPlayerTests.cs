using OfficeAssistant.Core.Animation;

namespace AcsFormat.Tests;

public class AnimationPlayerTests
{
    [Fact]
    public void NewAnimationReplacesTheRunningOneImmediately()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
        try
        {
            var clippit = AcsCharacter.Load(TestFiles.Assistant("CLIPPIT.ACS"));
            var presented = new List<uint[]>();
            var printCompleted = false;
            using var player = new AnimationPlayer(clippit, frame => presented.Add((uint[])frame.Clone()), null);

            player.Play("Print", () => printCompleted = true);
            Assert.Equal("Print", player.CurrentAnimation);

            presented.Clear();
            player.Play("Wave");

            // Switched without waiting for Print's exit path, and Print never "completes".
            Assert.Equal("Wave", player.CurrentAnimation);
            var expected = new AcsFrameRenderer(clippit).Render(clippit.FindAnimation("Wave")!.Frames[0]);
            Assert.Equal(expected, presented.Single());
            Assert.False(printCompleted);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
