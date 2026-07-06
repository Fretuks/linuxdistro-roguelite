# Gacha Implementation Guide

This project currently has a working beginner banner and scaffolding for standard and limited banners.
The main files are:

- `Assets/Scripts/Meta/GachaService.cs`: pull costs, odds, pity, currency accounting, reward rolls.
- `Assets/Scripts/Meta/GachaBannerState.cs`: persistent per-banner counters.
- `Assets/Scripts/Meta/GachaCurrencyType.cs`: pull-token currency enum.
- `Assets/Scripts/Meta/SaveService.cs`: saved currency and banner progress fields.
- `Assets/Scripts/UI/GachaScreenController.cs`: banner list, banner names, pull buttons, pull prompts, result text.
- `Assets/Scripts/UI/MainMenuController.cs`: wallet/save refresh, root-credit exchange popup, gacha status readouts.
- `Assets/UI/MainMenu.uxml` and `Assets/UI/MainMenu.uss`: exchange popup and gacha/status styling.

## Adding A New Banner

1. Add a banner id in `GachaService.cs`.
   Example:

   ```csharp
   public const string EventBannerId = "event";
   ```

2. Add a persistent state field in `GachaService.cs`.
   Standard and limited banners should each have their own `GachaBannerState`, separate from `beginnerState`, so pity does not leak between banners.

3. Add save fields in `SaveData` inside `SaveService.cs`.
   Example:

   ```csharp
   public GachaBannerState eventBannerState = new(GachaService.EventBannerId);
   ```

   Then update `EnsureLists()`, `GachaService.LoadProgress()`, and `GachaService.WriteProgress()` to load and save that state.

4. Add the banner id to `bannerIds` in `GachaScreenController.cs`.

5. Add a title/status entry in `GetBannerTitle()` and `GetBannerStatus()` in `GachaScreenController.cs`.

6. Add rendering for the banner in `RenderSelectedBanner()`.
   Use `RenderBeginnerBanner()` as the template, but use the new banner state, pull costs, and currency.

7. Add pull request and execution methods in `GachaService.cs`.
   The beginner versions are:

   - `GetBeginnerPullCost`
   - `CanPullBeginner`
   - `PerformBeginnerPull`
   - `RollBeginnerReward`
   - `CanPullBeginnerWithEntropy`

   Copy this shape for the new banner, then change the currency, state, pity, and reward pool.

## Currency Rules

- `GachaCurrencyType.StandardPull` formats as `stable-pull-token`.
- `GachaCurrencyType.LimitedPull` formats as `feature-pull-token`.
- Beginner and standard banners should spend `StandardPull`.
- Limited and event banners should usually spend `LimitedPull`, unless the design says otherwise.
- `EntropyPerPullToken` controls the entropy fallback cost for each missing pull token.
- Root-credits convert to entropy through `GachaService.ConvertRootCreditsToEntropy()`.

To add a new pull currency:

1. Add a value to `GachaCurrencyType.cs`.
2. Initialize it in the `GachaService` constructor.
3. Add save fields in `SaveData`.
4. Load/save it in `GachaService.LoadProgress()` and `WriteProgress()`.
5. Add display text in `GachaService.FormatCurrencyName()`.
6. Update the gacha status readout in `MainMenuController.RefreshCurrencyReadouts()`.

## Adjusting Pull Costs

Beginner costs are constants in `GachaService.cs`:

```csharp
public const int BeginnerSinglePullCost = 1;
public const int BeginnerTenPullCost = 8;
```

For standard or limited banners, add equivalent constants:

```csharp
public const int StandardSinglePullCost = 1;
public const int StandardTenPullCost = 10;
public const int LimitedSinglePullCost = 1;
public const int LimitedTenPullCost = 10;
```

Then use those constants in the matching `Get...PullCost()` method and in the pull button labels in `GachaScreenController.cs`.

## Adjusting Odds

The current base roll order is:

1. Check for a 5-star result.
2. If no 5-star, check for a 4-star-or-better result.
3. If no 4-star-or-better result, award the base 3-star equipment cache.

The base non-pity tier rates are:

- 5-star item: `0.8%`
- 4-star item: `11.904%`
- 3-star equipment: `87.296%`

After a 4-star or 5-star item is rolled, the game then does a 50/50 split:

- equipment
- distro

The effective split rates are still useful for sanity checks, but they are not separate primary drop rates:

- `5-star equipment = 0.8% 5-star item * 50% equipment = 0.4%`
- `5-star distro = 0.8% 5-star item * 50% distro = 0.4%`
- `4-star item = 99.2% no 5-star * 12% 4-star item = 11.904%`
- `4-star equipment = 11.904% 4-star item * 50% equipment = 5.952%`
- `4-star distro = 11.904% 4-star item * 50% distro = 5.952%`
- `3-star equipment = 99.2% no 5-star * 88% no 4-star hit = 87.296%`

The current odds constants are in `GachaService.cs`:

```csharp
private const double FiveStarBaseChance = 0.008d;
private const double FourStarBaseChance = 0.12d;
private const double DistroChanceOnFeaturedTier = 0.5d;
```

`FiveStarBaseChance` is the total base 5-star chance.
`FourStarBaseChance` decides whether a non-5-star pull upgrades from the base 3-star equipment cache to a 4-star result.
`DistroChanceOnFeaturedTier` is the 50/50 distro-vs-equipment split after a 4-star or 5-star item has already been rolled.

## Five-Star Soft Pity

Five-star pity uses `fiveStarPityCounter` in `GachaBannerState`.
It is separate from the 4-star `pityCounter`.

Current 5-star constants:

```csharp
public const int FiveStarSoftPityStart = 66;
public const int FiveStarHardPity = 80;
private const double FiveStarBaseChance = 0.008d;
private const double FiveStarSoftPityStep = 0.07d;
```

The total 5-star chance table is:

- pity pulls 1-65: `0.8%`
- pity pull 66: `7.8%`
- pity pull 67: `14.8%`
- pity pull 68: `21.8%`
- pity pull 69: `28.8%`
- pity pull 70: `35.8%`
- pity pull 71: `42.8%`
- pity pull 72: `49.8%`
- pity pull 73: `56.8%`
- pity pull 74: `63.8%`
- pity pull 75: `70.8%`
- pity pull 76: `77.8%`
- pity pull 77: `84.8%`
- pity pull 78: `91.8%`
- pity pull 79: `98.8%`
- pity pull 80: guaranteed by hard pity

The raw formula reaches `105.8%` on pull 80, so `GetFiveStarChance()` clamps the effective chance to `100%`.

A random 5-star resets both:

- `fiveStarPityCounter`
- the 4-star `pityCounter`

The beginner banner's pull-50 capstone guarantee awards the placeholder 5-star but intentionally does not reset `fiveStarPityCounter`.

## Adjusting Pity

Current 4-star pity is shared as a constant:

```csharp
public const int FourStarHardPity = 10;
```

The pity check happens in `RollBeginnerReward()`:

```csharp
bool pityTriggered = beginnerState.pityCounter >= FourStarHardPity - 1;
```

When adding standard or limited banners:

1. Use that banner's own `GachaBannerState`.
2. Increment only that state's `pityCounter` after a non-upgraded result.
3. Reset only that state's `pityCounter` after a 4-star-or-better result.
4. Increment only that state's `fiveStarPityCounter` after a non-5-star result.
5. Reset only that state's `fiveStarPityCounter` after a random 5-star result.
6. Add separate constants if banners need different pity values.

Example:

```csharp
public const int LimitedFourStarHardPity = 10;
public const int LimitedFiveStarHardPity = 90;
```

Do not reuse `pityCounter` for 5-star pity. Use `fiveStarPityCounter`.

## Adjusting Rewards

Rewards are represented by `GachaReward` in `GachaService.cs`.
Current reward constructors are:

- `GachaReward.Equipment(starRating, displayName, pityTriggered, guaranteed)`
- `GachaReward.DistroReward(distro, starRating, pityTriggered, guaranteed, guaranteeReason)`
- `GachaReward.FutureStandardFiveStar(displayName, guaranteeReason)`

To add real equipment:

1. Create an equipment data type and database.
2. Add an equipment reference/id to `GachaReward`.
3. Add a reward constructor for the equipment item.
4. Update `GachaPullResult` and collection/inventory code to persist duplicates.
5. Update `GachaScreenController.PullSelectedBanner()` to add equipment to the player inventory and print the result.

To change possible distro rewards:

1. Change the pool selection in the banner's `Roll...Reward()` method.
2. For starter guarantees, update `SetBeginnerGuaranteedDistros()` and `RewardGuaranteedBeginnerDistro()`.
3. For standard/limited pools, prefer explicit pool methods over reusing the beginner `bannerPool` if the pools differ.

## Beginner Banner Guarantees

Beginner special cases currently live in `RollBeginnerReward()`:

- pull 20: guaranteed first unpicked starter distro
- pull 40: guaranteed second unpicked starter distro
- pull 50: placeholder future standard 5-star distro, without resetting 5-star pity

The guaranteed distro ids are stored in:

```csharp
beginnerState.guaranteedDistroIds
```

They are set from `MainMenuController.HandleStarterConfirmed()` through:

```csharp
gachaService.SetBeginnerGuaranteedDistros(remaining);
```

To change guarantee milestones, edit the pull-number checks in `RollBeginnerReward()` and update `FormatBeginnerGuarantees()` in `GachaScreenController.cs`.

## Limited Banner 5-Star Rule

Limited banners use a lenient featured guarantee:

- When a limited-banner 5-star item is rolled, first do the normal 50/50 equipment-vs-distro split.
- If the 50/50 lands on equipment, award 5-star equipment and do not touch the limited featured guarantee.
- If the 50/50 lands on a distro/character, there is a `1/3` chance it uses the standard 5-star pool.
- If that happens, `featuredFiveStarGuaranteed` is set on that banner's `GachaBannerState`.
- The next limited-banner 5-star distro/character ignores the `1/3` standard-pool chance and must use the limited featured pool.
- After the guaranteed featured 5-star is awarded, `featuredFiveStarGuaranteed` is cleared.

The helper for this is:

```csharp
ResolveLimitedFiveStarUsesStandardPool(limitedState)
```

It returns `true` when the 5-star should come from the standard pool and `false` when it should come from the featured limited pool.
Call it only after the pull has already rolled a 5-star on a limited banner and the 50/50 split landed on distro/character.

## Standard Banner 4-Star And 5-Star Rule

Standard banner 4-star and 5-star hits should split 50/50 between equipment and distros after the tier is rolled.
The helper for this is:

```csharp
ResolveFeaturedTierIsDistro()
```

It returns `true` for a distro and `false` for equipment.
Call it only after the banner has already rolled a 4-star or 5-star item.

## Banner Names And UI Text

Banner display names are in `GachaScreenController.GetBannerTitle()`.
Banner status text on the left list is in `GetBannerStatus()`.
Pull button labels are in the banner render method, currently `RenderBeginnerBanner()`.

Keep these in sync:

1. `GachaService` banner id constant.
2. `bannerIds` array in `GachaScreenController`.
3. `GetBannerTitle()`.
4. `GetBannerStatus()`.
5. Any result text in `PullSelectedBanner()`.

## Standard Banner Checklist

1. Add `standardState` in `GachaService`.
2. Save/load `standardBannerState` in `SaveData`.
3. Add `StandardSinglePullCost` and `StandardTenPullCost`.
4. Implement `GetStandardPullCost()`.
5. Implement `CanPullStandard()`.
6. Implement `PerformStandardPull()`.
7. Implement `RollStandardReward()`.
8. Use `GachaCurrencyType.StandardPull`.
9. On a 4-star or 5-star hit, call `ResolveFeaturedTierIsDistro()`.
10. If it returns `true`, pick from the matching standard distro pool.
11. If it returns `false`, award matching-rarity equipment.
12. Render real standard controls in `GachaScreenController` instead of `RenderFutureBanner()`.
13. Add result handling for standard rewards.

## Limited Banner Checklist

1. Add `limitedState` in `GachaService`.
2. Save/load `limitedBannerState` in `SaveData`.
3. Add `LimitedSinglePullCost` and `LimitedTenPullCost`.
4. Implement `GetLimitedPullCost()`.
5. Implement `CanPullLimited()`.
6. Implement `PerformLimitedPull()`.
7. Implement `RollLimitedReward()`.
8. Use `GachaCurrencyType.LimitedPull`.
9. On a 4-star or 5-star hit, call `ResolveFeaturedTierIsDistro()`.
10. If it returns `false`, award matching-rarity equipment.
11. If it returns `true` on a 4-star, pick from the limited 4-star distro pool.
12. If it returns `true` on a 5-star, call `ResolveLimitedFiveStarUsesStandardPool(limitedState)`.
13. If that returns `true`, pick from the standard 5-star pool.
14. If that returns `false`, pick from the featured limited pool.
15. Add featured-unit or rate-up pool configuration.
16. Render real limited controls in `GachaScreenController`.

## Testing Changes

After changing gacha code:

1. Compile the Unity runtime assembly.
2. Test single pull, ten pull, insufficient-token flow, entropy fallback, root-credit exchange, duplicate distro reward, pity reset, and saved progress reload.
3. For new banners, verify that pity counters are independent by pulling on one banner, switching banners, and checking the other banner state did not change.
