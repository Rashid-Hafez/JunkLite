namespace Junklite.Shared
{
    /// <summary>
    /// A screen that wants first go at the back button (Esc). Return true if it used the press,
    /// for example to drop a picked-up item, so the pause menu doesn't open as well.
    /// </summary>
    public interface IBackHandler
    {
        bool HandleBack();
    }
}
