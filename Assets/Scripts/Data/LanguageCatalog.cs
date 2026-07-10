using System;
using System.Collections.Generic;
using KernelPanic.Core;

namespace KernelPanic.Data
{
    /// <summary>
    /// UI-facing language reference copy. This is not card data and does not unlock content by itself.
    /// </summary>
    public static class LanguageCatalog
    {
        private static readonly LanguageCatalogEntry[] Entries =
        {
            new(Language.C, "C", "bare metal", ResolutionTrack.Native, "C is immediate, high-risk burst damage. C damage rolls a 50% crit chance instead of the normal 25%, spills overkill into other enemies, and C cards can segfault: hurting you and corrupting a hand card.", null, Distro("arch", "Arch"), Distro("templeos", "TempleOS")),
            new(Language.CPlusPlus, "C++", "template arsenal", ResolutionTrack.Native, "C++ is planned as a Native setup/payoff language built from upgraded C cards. It should keep C's direct-hit pressure while adding heavier combo requirements.", "unlock: upgrade a C card during a run"),
            new(Language.Rust, "Rust", "safe systems", ResolutionTrack.Native, "Rust is immediate, reliable damage with defensive payoff. Rust overkill becomes Shield, and Rust damage has a tiny 0.001% chance to overflow into the unsigned 64-bit limit.", null, Distro("arch", "Arch"), Distro("fedora", "Fedora")),
            new(Language.Python, "Python", "queued scripts", ResolutionTrack.InterpreterQueue, "Python cards enter the interpreter queue instead of resolving immediately. They are slower, but let you stage delayed damage, draw, and repeat effects around the exchange structure.", null, Distro("ubuntu", "Ubuntu"), Distro("kali", "Kali"), Distro("mint", "Mint")),
            new(Language.JavaScript, "JavaScript", "runtime chaos", ResolutionTrack.Native, "JavaScript resolves immediately and is intentionally swingy: wide damage ranges, chance-based outcomes, and conditional repeat effects. It rewards embracing variance.", null, Distro("ubuntu", "Ubuntu"), Distro("endeavouros", "EndeavourOS"), Distro("mint", "Mint")),
            new(Language.TypeScript, "TypeScript", "typed runtime", ResolutionTrack.Native, "TypeScript is planned as the steadier JavaScript branch: Native timing with less chaos and more controlled setup/payoff patterns.", "unlock: upgrade a JavaScript card during a run"),
            new(Language.Haskell, "Haskell", "lazy proofs", ResolutionTrack.LazyStack, "Haskell uses the lazy stack: effects are stored for later instead of firing now, then resolve in last-in-first-out order. It is for delayed burst turns and precise sequencing.", null, Distro("gentoo", "Gentoo"), Distro("nixos", "NixOS")),
            new(Language.Assembly, "Assembly", "raw opcodes", ResolutionTrack.Native, "Assembly is planned as the most direct Native language: low-level, high-commitment effects that should trade flexibility for extreme precision or shield-bypass pressure.", null, Distro("gentoo", "Gentoo"), Distro("templeos", "TempleOS")),
            new(Language.Java, "Java", "warming vm", ResolutionTrack.Native, "Java cards are heavy but scale over the run: each Java card uses 2 RAM, Java damage spills overkill into other enemies, and JIT makes Java cards cheaper as you keep playing them. The discount cools after each wave.", null, Distro("debian", "Debian"), Distro("fedora", "Fedora")),
            new(Language.Go, "Go", "small services", ResolutionTrack.Native, "Go is planned as a straightforward Native language for small, dependable actions. Its identity should be simple scaling, services, and concurrency-style utility without much randomness.", null, Distro("cachyos", "CachyOS"), Distro("nixos", "NixOS")),
            new(Language.Ruby, "Ruby", "sharp scripts", ResolutionTrack.InterpreterQueue, "Ruby is planned as an interpreter-queue combo language: delayed scripting with expressive effects that should get better when sequenced with other queued cards.", null, Distro("kali", "Kali"), Distro("endeavouros", "EndeavourOS")),
            new(Language.Php, "PHP", "legacy glue", ResolutionTrack.InterpreterQueue, "PHP is planned as a queued utility language that benefits from messy boards and legacy leftovers: practical, delayed effects that turn clutter into value.", null, Distro("debian", "Debian"), Distro("cachyos", "CachyOS")),
        };

        public static IReadOnlyList<LanguageCatalogEntry> All => Entries;

        public static LanguageCatalogEntry Get(Language language)
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Language == language)
                {
                    return Entries[i];
                }
            }

            throw new ArgumentOutOfRangeException(nameof(language), language, "Language is not registered in the catalog.");
        }

        private static LanguageDistroReference Distro(string id, string displayName)
        {
            return new LanguageDistroReference(id, displayName);
        }
    }

    public readonly struct LanguageCatalogEntry
    {
        public LanguageCatalogEntry(Language language, string displayName, string identityTag, ResolutionTrack resolutionTrack, string howItWorks, string unlockHint, params LanguageDistroReference[] supportingDistros)
        {
            Language = language;
            DisplayName = displayName;
            IdentityTag = identityTag;
            ResolutionTrack = resolutionTrack;
            HowItWorks = howItWorks;
            UnlockHint = unlockHint;
            SupportingDistros = supportingDistros ?? Array.Empty<LanguageDistroReference>();
        }

        public Language Language { get; }
        public string DisplayName { get; }
        public string IdentityTag { get; }
        public ResolutionTrack ResolutionTrack { get; }
        public string HowItWorks { get; }
        public string UnlockHint { get; }
        public IReadOnlyList<LanguageDistroReference> SupportingDistros { get; }
    }

    public readonly struct LanguageDistroReference
    {
        public LanguageDistroReference(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public string Id { get; }
        public string DisplayName { get; }
    }
}
