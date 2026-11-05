using Avalonia.Controls;

namespace CloudScope.Avalonia;

/// <summary>Projects one canonical work status into progress, status text and frame rate.</summary>
internal sealed class ViewerStatusPresenter(TextBlock message, TextBlock fps, ProgressBar progress)
{
    private bool _wasWorkingOrFailed;
    public void Update(ViewerStatusSnapshot status)
    {
        fps.Text = status.Fps > 0 ? $"{status.Fps:0} fps" : "";
        progress.IsVisible = status.WorkProgress >= 0;
        progress.Value = Math.Max(0, status.WorkProgress);
        if (status.WorkProgress >= 0) message.Text = $"{status.WorkName} {status.WorkProgress}%";
        else if (status.StreamingError != null) message.Text = "Streaming error: " + status.StreamingError;
        else if (_wasWorkingOrFailed) message.Text = "Ready";
        _wasWorkingOrFailed = status.WorkProgress >= 0 || status.StreamingError != null;
    }
}
