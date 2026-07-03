namespace KernelPanic.UI
{
    /// <summary>
    /// Contains cosmetic terminal boot-log copy for the main menu shell.
    /// </summary>
    public static class BootLogCopy
    {
        public static readonly string[] Lines =
        {
            "[    0.000000] kernel-panic: booting userland shell",
            "[    0.071004] cpu: isolating run scheduler",
            "[    0.184331] entropy: wallet device mounted readonly",
            "[    0.264700] tty1: spawning command menu",
            "[    0.391226] units: probing installed distros",
            "[    0.447801] collection: no render pass requested",
            "[    0.521332] gacha: token api unavailable",
            "[    0.684012] events: banner source disconnected",
            "[    0.812445] ui: reduced motion preference loaded",
            "[    1.000000] shell: ready"
        };
    }
}
