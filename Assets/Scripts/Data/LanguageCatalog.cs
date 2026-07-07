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
            new(Language.C, "C", "bare metal", ResolutionTrack.Native, "TODO: Native cards hit immediately and reward careful resource timing. Signature mechanic copy pending.", null, Distro("arch", "Arch"), Distro("templeos", "TempleOS")),
            new(Language.CPlusPlus, "C++", "template arsenal", ResolutionTrack.Native, "TODO: Native cards hit immediately with complex setup payoffs. Signature mechanic copy pending.", "unlock: upgrade a C card during a run"),
            new(Language.Rust, "Rust", "safe systems", ResolutionTrack.Native, "TODO: Rust resolves on the native track with reliable damage and defensive spillover. Signature mechanic copy pending.", null, Distro("arch", "Arch"), Distro("fedora", "Fedora")),
            new(Language.Python, "Python", "queued scripts", ResolutionTrack.InterpreterQueue, "TODO: Python queues flexible effects that resolve at end of turn. Signature mechanic copy pending.", null, Distro("ubuntu", "Ubuntu"), Distro("kali", "Kali"), Distro("mint", "Mint")),
            new(Language.JavaScript, "JavaScript", "runtime chaos", ResolutionTrack.Native, "TODO: JavaScript fires instantly with swingy side effects. Signature mechanic copy pending.", null, Distro("ubuntu", "Ubuntu"), Distro("endeavouros", "EndeavourOS"), Distro("mint", "Mint")),
            new(Language.TypeScript, "TypeScript", "typed runtime", ResolutionTrack.Native, "TODO: TypeScript stabilizes JavaScript-style tempo with stricter setup. Signature mechanic copy pending.", "unlock: upgrade a JavaScript card during a run"),
            new(Language.Haskell, "Haskell", "lazy proofs", ResolutionTrack.LazyStack, "TODO: Haskell stores delayed work on the lazy stack before resolving in bursts. Signature mechanic copy pending.", null, Distro("gentoo", "Gentoo"), Distro("nixos", "NixOS")),
            new(Language.Assembly, "Assembly", "raw opcodes", ResolutionTrack.Native, "TODO: Assembly is immediate, low-level, and high commitment. Signature mechanic copy pending.", null, Distro("gentoo", "Gentoo"), Distro("templeos", "TempleOS")),
            new(Language.Java, "Java", "warming vm", ResolutionTrack.Native, "TODO: Java starts expensive and gets cheaper as the combat warms up. Signature mechanic copy pending.", null, Distro("debian", "Debian"), Distro("fedora", "Fedora")),
            new(Language.Go, "Go", "small services", ResolutionTrack.Native, "TODO: Go uses direct native actions with simple scaling and concurrency hooks. Signature mechanic copy pending.", null, Distro("cachyos", "CachyOS"), Distro("nixos", "NixOS")),
            new(Language.Ruby, "Ruby", "sharp scripts", ResolutionTrack.InterpreterQueue, "TODO: Ruby queues expressive effects with combo-friendly timing. Signature mechanic copy pending.", null, Distro("kali", "Kali"), Distro("endeavouros", "EndeavourOS")),
            new(Language.Php, "PHP", "legacy glue", ResolutionTrack.InterpreterQueue, "TODO: PHP queues practical effects that improve when the board is messy. Signature mechanic copy pending.", null, Distro("debian", "Debian"), Distro("cachyos", "CachyOS")),
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
