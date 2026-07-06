using System;
using KernelPanic.Core;
using KernelPanic.Data;

namespace KernelPanic.Meta
{
    /// <summary>
    /// Read-only unlock rule for language availability.
    /// </summary>
    public static class LanguageUnlock
    {
        public static bool IsUnlocked(Language language, PlayerCollection collection)
        {
            if (language == Language.CPlusPlus || language == Language.TypeScript)
            {
                // TODO: evolved-language unlock gate, separate from distro ownership.
                return false;
            }

            if (collection == null)
            {
                return false;
            }

            LanguageCatalogEntry entry = LanguageCatalog.Get(language);
            for (int unitIndex = 0; unitIndex < collection.OwnedUnits.Count; unitIndex++)
            {
                DistroDefinition unit = collection.OwnedUnits[unitIndex];
                if (unit == null)
                {
                    continue;
                }

                for (int distroIndex = 0; distroIndex < entry.SupportingDistros.Count; distroIndex++)
                {
                    if (string.Equals(unit.Id, entry.SupportingDistros[distroIndex].Id, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
