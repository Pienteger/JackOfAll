using System.Media;

namespace JackOfAll.Lib.Services;

public static class WellbeingService
{
    public static void PlayNotificationSound()
    {
        SystemSounds.Asterisk.Play();
    }
}
