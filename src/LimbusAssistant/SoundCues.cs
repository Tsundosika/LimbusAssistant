using System.Media;

namespace Tsundosika.LimbusAssistant;

public sealed class SoundCues(bool enabled)
{
    const int MinIntervalMilliseconds = 700;

    long _lastPlayed;

    public void NewPlan() => Play(SystemSounds.Asterisk);

    public void CorrectPick() => Play(SystemSounds.Beep);

    public void AllDone() => Play(SystemSounds.Exclamation);

    void Play(SystemSound sound)
    {
        if (!enabled)
        {
            return;
        }
        var now = Environment.TickCount64;
        if (now - _lastPlayed < MinIntervalMilliseconds)
        {
            return;
        }
        _lastPlayed = now;
        sound.Play();
    }
}
