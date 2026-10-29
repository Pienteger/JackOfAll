namespace JackOfAll.UserControls.Services.Hush;

/// <summary>
/// One audio engine shared by every open Hush tab, so two tabs never play two streams.
/// It starts with the first tab and stops when the last one closes.
/// </summary>
internal static class HushPlayer
{
    private static int _refs;

    public static List<Sound> Sounds { get; private set; } = [];
    public static Engine? Engine { get; private set; }

    public static Engine Acquire()
    {
        if (_refs++ == 0)
        {
            Sounds = Sound.CreateAll();
            Engine = new Engine(Sounds) { Playing = false };
            Engine.Start();
        }
        return Engine!;
    }

    public static void Release()
    {
        if (_refs == 0 || --_refs > 0) return;
        Engine?.Stop();
        Engine = null;
    }
}
