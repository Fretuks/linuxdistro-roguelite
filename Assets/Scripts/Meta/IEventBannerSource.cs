using System;

namespace KernelPanic.Meta
{
    /// <summary>
    /// Provides optional live-event banner copy for shell surfaces.
    /// </summary>
    public interface IEventBannerSource
    {
        EventBannerContent GetBanner();
    }

    /// <summary>
    /// Describes an optional event banner message and remaining duration.
    /// </summary>
    public sealed class EventBannerContent
    {
        public EventBannerContent(string text, TimeSpan? remainingTime)
        {
            Text = text;
            RemainingTime = remainingTime;
        }

        public string Text { get; }
        public TimeSpan? RemainingTime { get; }
    }
}
