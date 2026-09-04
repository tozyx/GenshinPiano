using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GenshinPiano.App.Controls;

internal static class SelectionIndicatorAnimator
{
    public static void Move(Border indicator, FrameworkElement target, FrameworkElement host, bool animate = true)
    {
        if (!target.IsLoaded || target.ActualWidth <= 0)
        {
            return;
        }

        var targetX = target.TranslatePoint(new Point(0, 0), host).X + 9;
        var targetWidth = Math.Max(18, target.ActualWidth - 18);
        if (indicator.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            indicator.RenderTransform = transform;
        }

        if (!animate)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            indicator.BeginAnimation(FrameworkElement.WidthProperty, null);
            transform.X = targetX;
            indicator.Width = targetWidth;
            return;
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var duration = TimeSpan.FromMilliseconds(300);
        transform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(targetX, duration) { EasingFunction = easing });

        var travel = Math.Abs(targetX - transform.X);
        var stretchedWidth = Math.Max(indicator.ActualWidth, targetWidth) + Math.Min(34, travel * .24);
        var widthAnimation = new DoubleAnimationUsingKeyFrames();
        widthAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(
            stretchedWidth, KeyTime.FromPercent(.48),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        widthAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(
            targetWidth, KeyTime.FromTimeSpan(duration),
            new CubicEase { EasingMode = EasingMode.EaseInOut }));
        indicator.BeginAnimation(FrameworkElement.WidthProperty, widthAnimation);
    }
}
