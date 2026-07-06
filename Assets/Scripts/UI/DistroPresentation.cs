using KernelPanic.Data;

namespace KernelPanic.UI
{
    /// <summary>
    /// Shared text formatting for distros, used by every screen that lists or details them.
    /// </summary>
    public static class DistroPresentation
    {
        public static string DisplayName(DistroDefinition unit)
        {
            return string.IsNullOrWhiteSpace(unit.DisplayName) ? unit.name : unit.DisplayName;
        }

        public static string FormatLanguages(DistroDefinition unit)
        {
            return $"{unit.PrimaryLanguage} / {unit.SecondaryLanguage}";
        }
    }
}
