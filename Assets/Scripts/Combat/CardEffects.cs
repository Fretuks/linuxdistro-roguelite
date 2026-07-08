using System.Collections.Generic;
using System.Linq;
using KernelPanic.Core;
using KernelPanic.Data;

namespace KernelPanic.Combat
{
    public sealed class DealDamageEffect : ICardEffect
    {
        private readonly int _minAmount;
        private readonly int _maxAmount;
        private readonly Language _language;
        private readonly bool _trueDamage;
        private readonly bool _canCrit;
        private readonly bool _allEnemies;

        public DealDamageEffect(int minAmount, int maxAmount, Language language, bool trueDamage = false, bool canCrit = false, bool allEnemies = false)
        {
            _minAmount = minAmount;
            _maxAmount = maxAmount;
            _language = language;
            _trueDamage = trueDamage;
            _canCrit = canCrit;
            _allEnemies = allEnemies;
        }

        public void Execute(CombatContext context)
        {
            IReadOnlyList<CombatantState> targets = _allEnemies
                ? context.Enemies.Where(enemy => enemy.State != null && !enemy.State.IsDefeated).Select(enemy => enemy.State).ToList()
                : context.Targets;

            if (StatusEffectController.Has(context.Source, StatusType.RaceCondition) && targets.Count > 1)
            {
                targets = ShuffleTargets(targets, context.Source);
            }

            for (int i = 0; i < targets.Count; i++)
            {
                CombatantState target = targets[i];
                if (target == null || target.IsDefeated)
                {
                    continue;
                }

                int minAmount = UpgradeMath.ScaleAmount(UpgradeMath.ApplySourceFlatBonus(_minAmount, context), context.Card);
                int maxAmount = UpgradeMath.ScaleAmount(UpgradeMath.ApplySourceFlatBonus(_maxAmount, context), context.Card);
                int amount = minAmount == maxAmount
                    ? minAmount
                    : RandomRoll.RollRange(minAmount, maxAmount, new RollContext(context.Source));

                context.DamagePipeline.DealDamage(new DamageRequest(context.Source, target, amount, _language, _trueDamage, _canCrit));
            }
        }

        private static IReadOnlyList<CombatantState> ShuffleTargets(IReadOnlyList<CombatantState> targets, CombatantState source)
        {
            List<CombatantState> shuffled = targets.ToList();
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int swapIndex = RandomRoll.RollRange(0, i, new RollContext(source));
                (shuffled[i], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[i]);
            }

            return shuffled;
        }
    }

    public sealed class ShieldEffect : ICardEffect
    {
        private readonly int _amount;

        public ShieldEffect(int amount)
        {
            _amount = amount;
        }

        public void Execute(CombatContext context)
        {
            int amount = UpgradeMath.ScaleShield(UpgradeMath.ApplySourceFlatBonus(_amount, context), context.Card);
            context.Source.Shield += amount;
            context.CombatManager.ReportEffectResult($"gained {amount} shield");
        }
    }

    public sealed class FirstCardShieldEffect : ICardEffect
    {
        private readonly int _baseAmount;
        private readonly int _firstCardBonus;

        public FirstCardShieldEffect(int baseAmount, int firstCardBonus)
        {
            _baseAmount = baseAmount;
            _firstCardBonus = firstCardBonus;
        }

        public void Execute(CombatContext context)
        {
            int amount = _baseAmount + (context.Card != null && context.Card.WasFirstCardThisTurn ? _firstCardBonus : 0);
            amount = UpgradeMath.ScaleShield(UpgradeMath.ApplySourceFlatBonus(amount, context), context.Card);
            context.Source.Shield += amount;
            context.CombatManager.ReportEffectResult($"gained {amount} shield");
        }
    }

    public sealed class ChanceDamageEffect : ICardEffect
    {
        private readonly int _damageAmount;
        private readonly int _successPercent;
        private readonly Language _language;

        public ChanceDamageEffect(int damageAmount, int successPercent, Language language)
        {
            _damageAmount = damageAmount;
            _successPercent = successPercent;
            _language = language;
        }

        public void Execute(CombatContext context)
        {
            for (int i = 0; i < context.Targets.Count; i++)
            {
                CombatantState target = context.Targets[i];
                if (target == null || target.IsDefeated)
                {
                    continue;
                }

                int roll = RandomRoll.RollRange(1, 100, new RollContext(context.Source));
                int damageAmount = UpgradeMath.ScaleAmount(UpgradeMath.ApplySourceFlatBonus(_damageAmount, context), context.Card);
                int amount = roll <= _successPercent ? damageAmount : 0;
                context.DamagePipeline.DealDamage(new DamageRequest(context.Source, target, amount, _language, false, false));
                context.CombatManager.ReportEffectResult(amount == 0 ? "typeof -> NaN (0)" : $"typeof -> {amount}");
            }
        }
    }

    public sealed class QueuedGrowthDamageEffect : ICardEffect
    {
        private readonly int _baseAmount;
        private readonly Language _language;

        public QueuedGrowthDamageEffect(int baseAmount, Language language)
        {
            _baseAmount = baseAmount;
            _language = language;
        }

        public void Execute(CombatContext context)
        {
            int queueGrowth = UnityEngine.Mathf.Max(0, (context.Card?.QueuePlayCount ?? 1) - 1);
            int amount = _baseAmount + queueGrowth;
            new DealDamageEffect(amount, amount, _language).Execute(context);
        }
    }


    public sealed class ApplyStatusEffect : ICardEffect
    {
        private readonly StatusType _statusType;
        private readonly int _stacks;
        private readonly int _duration;
        private readonly bool _targetSelf;
        private readonly bool _skipNextTick;

        public ApplyStatusEffect(StatusType statusType, int stacks, int duration, bool targetSelf, bool skipNextTick = false)
        {
            _statusType = statusType;
            _stacks = stacks;
            _duration = duration;
            _targetSelf = targetSelf;
            _skipNextTick = skipNextTick;
        }

        public void Execute(CombatContext context)
        {
            if (_targetSelf)
            {
                context.StatusEffects.Apply(context.Source, _statusType, UpgradeMath.ScaleAmount(UpgradeMath.ApplySourceFlatBonus(_stacks, context), context.Card), _duration, context.Source, _skipNextTick);
                return;
            }

            for (int i = 0; i < context.Targets.Count; i++)
            {
                context.StatusEffects.Apply(context.Targets[i], _statusType, UpgradeMath.ScaleAmount(UpgradeMath.ApplySourceFlatBonus(_stacks, context), context.Card), _duration, context.Source, _skipNextTick);
            }
        }
    }

    public sealed class CleanseEffect : ICardEffect
    {
        private readonly bool _harmfulOnly;

        public CleanseEffect(bool harmfulOnly)
        {
            _harmfulOnly = harmfulOnly;
        }

        public void Execute(CombatContext context)
        {
            if (_harmfulOnly)
            {
                context.StatusEffects.CleanseHarmful(context.Source);
            }
        }
    }

    public sealed class DrawEffect : ICardEffect
    {
        private readonly int _count;

        public DrawEffect(int count)
        {
            _count = count;
        }

        public void Execute(CombatContext context)
        {
            int room = context.HandController.RemainingRam;
            if (room <= 0)
            {
                context.CombatManager.ReportEffectResult("hand full: drew 0");
                return;
            }

            // RAM is a hard hand cap. Draw effects only pull cards that can fit; blocked cards stay in the draw pile.
            int baseCount = _count;
            if (context.Card?.Definition?.Id == "ubuntu_ask_ubuntu"
                && string.Equals(context.CombatManager.RunConfig?.Distro?.Id, "ubuntu", System.StringComparison.OrdinalIgnoreCase)
                && context.CombatManager.RunConfig.DistroVersion >= 3)
            {
                baseCount = 3;
            }

            int requested = UpgradeMath.ScaleAmount(UpgradeMath.ApplySourceFlatBonus(baseCount, context), context.Card);
            int drawCount = UnityEngine.Mathf.Min(requested, room);
            IReadOnlyList<CardInstance> drawn = context.DeckController.Draw(drawCount);
            int added = 0;
            for (int i = 0; i < drawn.Count; i++)
            {
                if (context.HandController.Add(drawn[i]))
                {
                    added++;
                }
                else
                {
                    context.DeckController.AddToDrawPile(drawn[i], shuffle: false);
                }
            }

            string capped = drawCount < requested ? " (hand full)" : string.Empty;
            context.CombatManager.ReportEffectResult($"drew {added}{capped}");
        }
    }

    public sealed class NextTurnCycleEffect : ICardEffect
    {
        private readonly int _amount;

        public NextTurnCycleEffect(int amount)
        {
            _amount = amount;
        }

        public void Execute(CombatContext context)
        {
            int amount = UpgradeMath.ScaleAmount(_amount, context.Card);
            context.CombatManager.AddNextTurnCycleBonus(amount);
            context.CombatManager.ReportEffectResult($"next turn +{amount} cycle");
        }
    }

    public sealed class QueueRepeatEffect : ICardEffect
    {
        private readonly int _count;

        public QueueRepeatEffect(int count)
        {
            _count = count;
        }

        public void Execute(CombatContext context)
        {
            context.CombatManager.AddQueuedRepeatCharges(UpgradeMath.ScaleAmount(_count, context.Card));
        }
    }

    public sealed class CopyLowestCostHandCardEffect : ICardEffect
    {
        public void Execute(CombatContext context)
        {
            if (context.HandController.RemainingRam <= 0)
            {
                context.CombatManager.ReportEffectResult("hand full: copied 0");
                return;
            }

            CardInstance selected = null;
            int selectedCost = int.MaxValue;
            for (int i = 0; i < context.HandController.Cards.Count; i++)
            {
                CardInstance candidate = context.HandController.Cards[i];
                if (candidate == null || candidate.Definition == null)
                {
                    continue;
                }

                int cost = context.CombatManager.GetEffectiveCardCost(candidate);
                if (cost < selectedCost)
                {
                    selected = candidate;
                    selectedCost = cost;
                }
            }

            if (selected == null)
            {
                context.CombatManager.ReportEffectResult("no card to copy");
                return;
            }

            CardInstance copy = selected.CopyForCombat();
            if (!context.HandController.CanAdd(copy))
            {
                context.CombatManager.ReportEffectResult("not enough RAM to copy");
                return;
            }

            if (context.HandController.Add(copy))
            {
                string name = string.IsNullOrWhiteSpace(copy.Definition.DisplayName) ? copy.Definition.Id : copy.Definition.DisplayName;
                context.CombatManager.ReportEffectResult($"copied {name}");
            }
        }
    }

    public sealed class UpgradeHandCardsEffect : ICardEffect
    {
        private readonly int _minCount;
        private readonly int _maxCount;

        public UpgradeHandCardsEffect(int minCount, int maxCount)
        {
            _minCount = minCount;
            _maxCount = maxCount;
        }

        public void Execute(CombatContext context)
        {
            int minCount = UpgradeMath.ScaleAmount(UpgradeMath.ApplySourceFlatBonus(_minCount, context), context.Card);
            int maxCount = UpgradeMath.ScaleAmount(UpgradeMath.ApplySourceFlatBonus(_maxCount, context), context.Card);
            int targetCount = minCount == maxCount
                ? minCount
                : RandomRoll.RollRange(minCount, maxCount, new RollContext(context.Source));

            List<CardInstance> candidates = context.HandController.Cards
                .Where(card => card != null && card.CanUpgrade)
                .ToList();
            int upgraded = 0;
            while (upgraded < targetCount && candidates.Count > 0)
            {
                int index = RandomRoll.RollRange(0, candidates.Count - 1, new RollContext(context.Source));
                CardInstance card = candidates[index];
                candidates.RemoveAt(index);
                if (card.Upgrade())
                {
                    upgraded++;
                }
            }

            context.CombatManager.ReportEffectResult(upgraded == 0 ? "no cards upgraded" : $"upgraded {upgraded} card(s)");
        }
    }

    public sealed class ShuffleTokenIntoDrawPileEffect : ICardEffect
    {
        private readonly string _tokenId;

        public ShuffleTokenIntoDrawPileEffect(string tokenId)
        {
            _tokenId = tokenId;
        }

        public void Execute(CombatContext context)
        {
            if (context.CombatManager.TryCreateGeneratedCardById(_tokenId, out CardInstance token))
            {
                context.DeckController.AddToDrawPile(token, shuffle: true);
                context.CombatManager.ReportEffectResult($"shuffled {GetCardName(token)} into draw pile");
            }
            else
            {
                context.CombatManager.ReportEffectResult($"token {_tokenId} unavailable");
            }
        }

        private static string GetCardName(CardInstance card)
        {
            return string.IsNullOrWhiteSpace(card?.Definition?.DisplayName) ? card?.Definition?.Id ?? "--" : card.Definition.DisplayName;
        }
    }

    public sealed class GenerateCardEffect : ICardEffect
    {
        private readonly Language _language;
        private readonly Rarity _rarity;

        public GenerateCardEffect(Language language, Rarity rarity)
        {
            _language = language;
            _rarity = rarity;
        }

        public void Execute(CombatContext context)
        {
            if (context.HandController.RemainingRam <= 0)
            {
                context.CombatManager.ReportEffectResult("hand full: generated 0");
                return;
            }

            if (!context.CombatManager.TryCreateGeneratedCard(_language, _rarity, out CardInstance generatedCard))
            {
                context.CombatManager.ReportEffectResult($"no {_rarity.ToString().ToLowerInvariant()} {_language} card pool");
                return;
            }

            if (!context.HandController.CanAdd(generatedCard))
            {
                context.CombatManager.ReportEffectResult("not enough RAM: generated 0");
            }
            else if (context.HandController.Add(generatedCard))
            {
                context.CombatManager.ReportEffectResult($"generated {GetCardName(generatedCard)} at 0c");
            }
            else
            {
                context.CombatManager.ReportEffectResult("hand full: generated 0");
            }
        }

        private static string GetCardName(CardInstance card)
        {
            if (card?.Definition == null)
            {
                return "--";
            }

            return string.IsNullOrWhiteSpace(card.Definition.DisplayName) ? card.Definition.Id : card.Definition.DisplayName;
        }
    }

    public sealed class OverkillToShieldDamageEffect : ICardEffect
    {
        private readonly int _amount;
        private readonly Language _language;

        public OverkillToShieldDamageEffect(int amount, Language language)
        {
            _amount = amount;
            _language = language;
        }

        public void Execute(CombatContext context)
        {
            for (int i = 0; i < context.Targets.Count; i++)
            {
                CombatantState target = context.Targets[i];
                if (target == null || target.IsDefeated)
                {
                    continue;
                }

                int amount = UpgradeMath.ScaleAmount(UpgradeMath.ApplySourceFlatBonus(_amount, context), context.Card);
                int survivable = target.CurrentUptime + target.Shield;
                context.DamagePipeline.DealDamage(new DamageRequest(context.Source, target, amount, _language, false, false));
                int overkill = UnityEngine.Mathf.Max(0, amount - survivable);
                if (overkill > 0)
                {
                    context.Source.Shield += overkill;
                    context.CombatManager.ReportEffectResult($"overkill -> {overkill} shield");
                }
            }
        }
    }

    public sealed class ConditionalShieldRepeatDamageEffect : ICardEffect
    {
        private readonly int _amount;
        private readonly Language _language;

        public ConditionalShieldRepeatDamageEffect(int amount, Language language)
        {
            _amount = amount;
            _language = language;
        }

        public void Execute(CombatContext context)
        {
            int repeats = context.Source.Shield > 0 ? 2 : 1;
            for (int repeat = 0; repeat < repeats; repeat++)
            {
                new DealDamageEffect(_amount, _amount, _language).Execute(context);
            }
        }
    }

    public sealed class FirstToPackageEffect : ICardEffect
    {
        private readonly int _amount;

        public FirstToPackageEffect(int amount)
        {
            _amount = amount;
        }

        public void Execute(CombatContext context)
        {
            int amount = UpgradeMath.ScaleAmount(UpgradeMath.ApplySourceFlatBonus(_amount, context), context.Card);
            new DealDamageEffect(amount, amount, Language.Java).Execute(context);
            context.CombatManager.AddJavaCostDiscountThisTurn(1);
        }
    }

    public sealed class DnfAutoremoveEffect : ICardEffect
    {
        private readonly int _damageAmount;
        private readonly int _cycleGain;

        public DnfAutoremoveEffect(int damageAmount, int cycleGain)
        {
            _damageAmount = damageAmount;
            _cycleGain = cycleGain;
        }

        public void Execute(CombatContext context)
        {
            CardInstance junk = FindJunkCard(context);
            if (junk == null)
            {
                context.CombatManager.ReportEffectResult("dnf autoremove: no orphaned package");
                return;
            }

            int damageAmount = UpgradeMath.ScaleAmount(UpgradeMath.ApplySourceFlatBonus(_damageAmount, context), context.Card);
            new DealDamageEffect(damageAmount, damageAmount, Language.Rust).Execute(context);
            if (context.HandController.Remove(junk))
            {
                context.DeckController.Exhaust(junk);
                int cycleGain = UpgradeMath.ScaleAmount(_cycleGain, context.Card);
                context.Source.Cycles += cycleGain;
                context.CombatManager.ReportEffectResult($"autoremove exhausted {GetCardName(junk)}: +{cycleGain} cycle");
            }
        }

        private static CardInstance FindJunkCard(CombatContext context)
        {
            for (int i = 0; i < context.HandController.Cards.Count; i++)
            {
                CardInstance card = context.HandController.Cards[i];
                if (IsJunk(card))
                {
                    return card;
                }
            }

            return null;
        }

        private static bool IsJunk(CardInstance card)
        {
            if (card == null)
            {
                return false;
            }

            string id = card.Definition?.Id ?? string.Empty;
            string name = card.Definition?.DisplayName ?? string.Empty;
            return card.Definition != null && card.Definition.IsToken
                || card.IsBroken
                || id.IndexOf("junk", System.StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("nop", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("junk", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("NOP", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("broken", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetCardName(CardInstance card)
        {
            return string.IsNullOrWhiteSpace(card?.Definition?.DisplayName) ? card?.Definition?.Id ?? "--" : card.Definition.DisplayName;
        }
    }

    public sealed class UpdateManagerEffect : ICardEffect
    {
        private readonly int _amount;

        public UpdateManagerEffect(int amount)
        {
            _amount = amount;
        }

        public void Execute(CombatContext context)
        {
            int amount = UpgradeMath.ScaleAmount(UpgradeMath.ApplySourceFlatBonus(_amount, context), context.Card);
            new DealDamageEffect(amount, amount, Language.Python, allEnemies: true).Execute(context);
            context.CombatManager.ScheduleUpdateManagerRepeat(amount);
        }
    }

    public sealed class TimeshiftSnapshotEffect : ICardEffect
    {
        public void Execute(CombatContext context)
        {
            context.CombatManager.ScheduleTimeshiftRestore(context.Source.CurrentUptime);
        }
    }

    public sealed class RawhideEffect : ICardEffect
    {
        public void Execute(CombatContext context)
        {
            new DrawEffect(2).Execute(context);
            context.CombatManager.GrantRawhideBonus(1);
            context.CombatManager.ReportEffectResult("rawhide armed next card");
        }
    }

    public sealed class IncomingAttackHalveEffect : ICardEffect
    {
        private readonly int _charges;

        public IncomingAttackHalveEffect(int charges)
        {
            _charges = charges;
        }

        public void Execute(CombatContext context)
        {
            context.CombatManager.AddIncomingAttackHalfCharges(UpgradeMath.ScaleAmount(_charges, context.Card));
        }
    }

    public sealed class NoOpCardEffect : ICardEffect
    {
        private readonly string _message;

        public NoOpCardEffect(string message)
        {
            _message = message;
        }

        public void Execute(CombatContext context)
        {
            context.CombatManager.LogEffectTodo(context.Card, _message);
        }
    }

    public static class CardEffectFactory
    {
        public static IReadOnlyList<ICardEffect> CreateEffects(CardDefinition definition)
        {
            List<ICardEffect> serializedEffects = CreateSerializedEffects(definition);
            if (serializedEffects.Count > 0)
            {
                return serializedEffects;
            }

            return definition?.Id switch
            {
                "lang_py_print" => One(new DealDamageEffect(3, 3, Language.Python)),
                "lang_py_import_antigravity" => One(new DrawEffect(2)),
                "lang_py_for_loop" => One(new DealDamageEffect(2, 2, Language.Python, allEnemies: true)),
                "lang_js_console_log" => One(new DealDamageEffect(3, 9, Language.JavaScript)),
                "lang_js_fetch" => One(new DealDamageEffect(5, 13, Language.JavaScript)),
                "lang_rs_fn_main" => One(new OverkillToShieldDamageEffect(6, Language.Rust)),
                "lang_rs_unwrap" => new ICardEffect[]
                {
                    new DealDamageEffect(9, 9, Language.Rust),
                    new DrawEffect(1)
                },
                "lang_rs_borrow" => One(new ShieldEffect(5)),
                "lang_java_public_static_main" => One(new DealDamageEffect(7, 7, Language.Java)),
                "lang_java_system_out_println" => One(new DealDamageEffect(5, 5, Language.Java)),
                "lang_java_new_object" => One(new DealDamageEffect(3, 3, Language.Java)),
                "ubuntu_ask_ubuntu" => One(new DrawEffect(2)),
                "ubuntu_unattended_upgrades" => new ICardEffect[]
                {
                    new ShieldEffect(4),
                    new ApplyStatusEffect(StatusType.UnattendedUpgrades, 4, 2, targetSelf: true, skipNextTick: true)
                },
                "mint_fix_broken" => new ICardEffect[]
                {
                    new DealDamageEffect(8, 8, Language.JavaScript),
                    new CleanseEffect(harmfulOnly: true)
                },
                "lang_js_typeof" => One(new ChanceDamageEffect(8, 75, Language.JavaScript)),
                "shop_py_list_append" => One(new QueuedGrowthDamageEffect(4, Language.Python)),
                "shop_py_async_def" => new ICardEffect[]
                {
                    new DrawEffect(2),
                    new NextTurnCycleEffect(1)
                },
                "shop_py_zip" => One(new QueueRepeatEffect(2)),
                "shop_js_math_random" => One(new DealDamageEffect(2, 20, Language.JavaScript)),
                "shop_js_promise_all" => new ICardEffect[]
                {
                    new DealDamageEffect(4, 10, Language.JavaScript),
                    new DealDamageEffect(4, 10, Language.JavaScript),
                    new DealDamageEffect(4, 10, Language.JavaScript)
                },
                "shop_js_spread" => One(new CopyLowestCostHandCardEffect()),
                "ubuntu_snap_install" => new ICardEffect[]
                {
                    new DealDamageEffect(6, 14, Language.JavaScript),
                    new ShuffleTokenIntoDrawPileEffect("ubuntu_pro_trial")
                },
                "ubuntu_do_release_upgrade" => One(new UpgradeHandCardsEffect(1, 3)),
                "ubuntu_apt_install" => One(new GenerateCardEffect(Language.Python, Rarity.Common)),
                "ubuntu_pro_trial" => One(new NoOpCardEffect("ubuntu pro trial is unplayable")),
                "mint_update_manager" => One(new UpdateManagerEffect(5)),
                "mint_cinnamon" => new ICardEffect[]
                {
                    new ShieldEffect(3),
                    new DrawEffect(1)
                },
                "mint_nemo" => One(new ConditionalShieldRepeatDamageEffect(6, Language.JavaScript)),
                "mint_timeshift" => One(new TimeshiftSnapshotEffect()),
                "fedora_dnf_autoremove" => One(new DnfAutoremoveEffect(7, 1)),
                "fedora_first_to_package" => One(new FirstToPackageEffect(7)),
                "fedora_dnf_update" => One(new DealDamageEffect(9, 9, Language.Java)),
                "fedora_rawhide" => One(new RawhideEffect()),
                "fedora_selinux" => One(new IncomingAttackHalveEffect(2)),
                _ => Todo("card effect is not authored")
            };
        }

        public static bool RequiresSingleTarget(CardDefinition definition)
        {
            return definition?.Id is "lang_js_console_log" or "lang_js_fetch" or "lang_js_typeof" or "mint_fix_broken"
                or "lang_rs_fn_main" or "lang_rs_unwrap" or "lang_java_public_static_main"
                or "lang_java_system_out_println" or "lang_java_new_object"
                or "shop_py_list_append" or "shop_js_math_random" or "shop_js_promise_all" or "ubuntu_snap_install"
                or "mint_nemo" or "fedora_dnf_autoremove" or "fedora_first_to_package" or "fedora_dnf_update";
        }

        public static bool TargetsAllEnemies(CardDefinition definition)
        {
            return definition?.Id is "lang_py_for_loop";
        }

        private static List<ICardEffect> CreateSerializedEffects(CardDefinition definition)
        {
            List<ICardEffect> effects = new();
            if (definition?.Effects == null)
            {
                return effects;
            }

            for (int i = 0; i < definition.Effects.Count; i++)
            {
                ICardEffect effect = definition.Effects[i] == null ? null : definition.Effects[i].CreateRuntimeEffect();
                if (effect != null)
                {
                    effects.Add(effect);
                }
            }

            return effects;
        }

        private static IReadOnlyList<ICardEffect> One(ICardEffect effect)
        {
            return new[] { effect };
        }

        private static IReadOnlyList<ICardEffect> Todo(string message)
        {
            return One(new NoOpCardEffect(message));
        }

        public static string GetRulesText(CardInstance card)
        {
            if (card?.Definition == null)
            {
                return "--";
            }

            int level = card.UpgradeLevel;
            string marker = level > 0 ? " [upgraded]" : string.Empty;
            return card.Definition.Id switch
            {
                "lang_py_print" => $"queue: deal {UpgradeMath.ScaleAmount(3, card)}.{marker}",
                "lang_py_import_antigravity" => $"queue: draw {UpgradeMath.ScaleAmount(2, card)}.{marker}",
                "lang_py_for_loop" => $"queue: deal {UpgradeMath.ScaleAmount(2, card)} to all enemies.{marker}",
                "lang_js_console_log" => $"deal {UpgradeMath.ScaleAmount(3, card)}-{UpgradeMath.ScaleAmount(9, card)}.{marker}",
                "lang_js_fetch" => $"deal {UpgradeMath.ScaleAmount(5, card)}-{UpgradeMath.ScaleAmount(13, card)}.{marker}",
                "lang_js_typeof" => $"75%: deal {UpgradeMath.ScaleAmount(8, card)}. 25%: deal 0.{marker}",
                "lang_rs_fn_main" => $"deal {UpgradeMath.ScaleAmount(6, card)}. overkill becomes shield.{marker}",
                "lang_rs_unwrap" => $"deal {UpgradeMath.ScaleAmount(9, card)}. draw {UpgradeMath.ScaleAmount(1, card)}.{marker}",
                "lang_rs_borrow" => $"gain {UpgradeMath.ScaleShield(5, card)} shield.{marker}",
                "lang_java_public_static_main" => $"uses 2 RAM. deal {UpgradeMath.ScaleAmount(7, card)}. JIT: Java cards cost 1 less this combat each time you play a Java card; discount decays by 1 after each wave.{marker}",
                "lang_java_system_out_println" => $"uses 2 RAM. deal {UpgradeMath.ScaleAmount(5, card)}. JIT applies; discount decays by 1 after each wave.{marker}",
                "lang_java_new_object" => $"uses 2 RAM. deal {UpgradeMath.ScaleAmount(3, card)}. JIT applies; discount decays by 1 after each wave.{marker}",
                "shop_py_list_append" => $"queue: deal {UpgradeMath.ScaleAmount(4 + UnityEngine.Mathf.Max(0, card.QueuePlayCount - 1), card)}; +1 each time re-queued.{marker}",
                "shop_py_async_def" => $"queue: draw {UpgradeMath.ScaleAmount(2, card)}. gain {UpgradeMath.ScaleAmount(1, card)} cycle next turn.{marker}",
                "shop_py_zip" => $"next {UpgradeMath.ScaleAmount(2, card)} queued cards resolve twice.{marker}",
                "shop_js_math_random" => $"deal {UpgradeMath.ScaleAmount(2, card)}-{UpgradeMath.ScaleAmount(20, card)}.{marker}",
                "shop_js_promise_all" => $"deal {UpgradeMath.ScaleAmount(4, card)}-{UpgradeMath.ScaleAmount(10, card)} three times.{marker}",
                "shop_js_spread" => $"copy the lowest-cost card in hand.{marker}",
                "ubuntu_ask_ubuntu" => $"queue: draw {UpgradeMath.ScaleAmount(2, card)}.{marker}",
                "ubuntu_unattended_upgrades" => $"queue: gain {UpgradeMath.ScaleShield(4, card)} shield now and next 2 turns.{marker}",
                "ubuntu_snap_install" => $"deal {UpgradeMath.ScaleAmount(6, card)}-{UpgradeMath.ScaleAmount(14, card)}. shuffle an ubuntu pro trial into your draw pile.{marker}",
                "ubuntu_do_release_upgrade" => $"upgrade {UpgradeMath.ScaleAmount(1, card)}-{UpgradeMath.ScaleAmount(3, card)} random hand cards.{marker}",
                "ubuntu_apt_install" => $"queue: generate a random common Python card at 0c.{marker}",
                "mint_fix_broken" => $"deal {UpgradeMath.ScaleAmount(8, card)}. cleanse harmful statuses.{marker}",
                "mint_update_manager" => $"queue: deal {UpgradeMath.ScaleAmount(5, card)} to all enemies. repeats at the end of your next turn.{marker}",
                "mint_cinnamon" => $"queue: gain {UpgradeMath.ScaleShield(3, card)} shield. draw {UpgradeMath.ScaleAmount(1, card)}.{marker}",
                "mint_nemo" => $"deal {UpgradeMath.ScaleAmount(6, card)}. if you have shield, deal it again.{marker}",
                "mint_timeshift" => $"queue: record uptime now. at end of your next turn, restore to it if lower.{marker}",
                "fedora_dnf_autoremove" => $"deal {UpgradeMath.ScaleAmount(7, card)}. exhaust a junk/NOP/broken card from hand; if you do, gain {UpgradeMath.ScaleAmount(1, card)} cycle. if not, do nothing.{marker}",
                "fedora_first_to_package" => $"deal {UpgradeMath.ScaleAmount(7, card)}. Java cards cost 1 less this turn.{marker}",
                "fedora_dnf_update" => $"deal {UpgradeMath.ScaleAmount(9, card)}. costs 1 less per Java card played this combat.{marker}",
                "fedora_rawhide" => $"draw {UpgradeMath.ScaleAmount(2, card)}. next card this turn gains bleeding edge.{marker}",
                "fedora_selinux" => $"next {UpgradeMath.ScaleAmount(2, card)} enemy attacks deal half damage.{marker}",
                _ => string.IsNullOrWhiteSpace(card.Definition.Description) ? "effect not authored." : $"{card.Definition.Description}{marker}"
            };
        }

    }

    public static class UpgradeMath
    {
        public static int ScaleAmount(int baseAmount, CardInstance card)
        {
            return baseAmount + UnityEngine.Mathf.Max(0, card?.MagnitudeBonus ?? 0);
        }

        public static int ScaleShield(int baseAmount, CardInstance card)
        {
            return baseAmount + UnityEngine.Mathf.Max(0, card?.MagnitudeBonus ?? 0);
        }

        public static int ApplySourceFlatBonus(int amount, CombatContext context)
        {
            return amount + UnityEngine.Mathf.Max(0, context?.Source?.FlatEffectBonus ?? 0);
        }
    }
}
