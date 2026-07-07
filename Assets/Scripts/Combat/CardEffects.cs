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

                int minAmount = UpgradeMath.ScaleAmount(_minAmount, context.Card);
                int maxAmount = UpgradeMath.ScaleAmount(_maxAmount, context.Card);
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
            int amount = UpgradeMath.ScaleShield(_amount, context.Card);
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
                int damageAmount = UpgradeMath.ScaleAmount(_damageAmount, context.Card);
                int amount = roll <= _successPercent ? damageAmount : 0;
                context.DamagePipeline.DealDamage(new DamageRequest(context.Source, target, amount, _language, false, false));
                context.CombatManager.ReportEffectResult(amount == 0 ? "typeof -> NaN (0)" : $"typeof -> {amount}");
            }
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
                context.StatusEffects.Apply(context.Source, _statusType, UpgradeMath.ScaleAmount(_stacks, context.Card), _duration, context.Source, _skipNextTick);
                return;
            }

            for (int i = 0; i < context.Targets.Count; i++)
            {
                context.StatusEffects.Apply(context.Targets[i], _statusType, UpgradeMath.ScaleAmount(_stacks, context.Card), _duration, context.Source, _skipNextTick);
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
            int room = context.HandController.RamCapacity - context.HandController.Cards.Count;
            if (room <= 0)
            {
                context.CombatManager.ReportEffectResult("hand full: drew 0");
                return;
            }

            // RAM is a hard hand cap. Draw effects only pull cards that can fit; blocked cards stay in the draw pile.
            int requested = UpgradeMath.ScaleAmount(_count, context.Card);
            int drawCount = UnityEngine.Mathf.Min(requested, room);
            IReadOnlyList<CardInstance> drawn = context.DeckController.Draw(drawCount);
            int added = 0;
            for (int i = 0; i < drawn.Count; i++)
            {
                if (context.HandController.Add(drawn[i]))
                {
                    added++;
                }
            }

            string capped = drawCount < requested ? " (hand full)" : string.Empty;
            context.CombatManager.ReportEffectResult($"drew {added}{capped}");
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
            int minCount = UpgradeMath.ScaleAmount(_minCount, context.Card);
            int maxCount = UpgradeMath.ScaleAmount(_maxCount, context.Card);
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
            if (context.HandController.Cards.Count >= context.HandController.RamCapacity)
            {
                context.CombatManager.ReportEffectResult("hand full: generated 0");
                return;
            }

            if (!context.CombatManager.TryCreateGeneratedCard(_language, _rarity, out CardInstance generatedCard))
            {
                context.CombatManager.ReportEffectResult($"no {_rarity.ToString().ToLowerInvariant()} {_language} card pool");
                return;
            }

            if (context.HandController.Add(generatedCard))
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

    public sealed class NoOpCardEffect : ICardEffect
    {
        private readonly string _todo;

        public NoOpCardEffect(string todo)
        {
            _todo = todo;
        }

        public void Execute(CombatContext context)
        {
            context.CombatManager.LogEffectTodo(context.Card, _todo);
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
                "shop_py_list_append" => One(new DealDamageEffect(4, 4, Language.Python)),
                "shop_py_async_def" => One(new DrawEffect(2)),
                "shop_py_zip" => Todo("TODO: zip() needs queue-effect-multiply support."),
                "shop_js_math_random" => One(new DealDamageEffect(2, 20, Language.JavaScript)),
                "shop_js_promise_all" => new ICardEffect[]
                {
                    new DealDamageEffect(4, 10, Language.JavaScript),
                    new DealDamageEffect(4, 10, Language.JavaScript),
                    new DealDamageEffect(4, 10, Language.JavaScript)
                },
                "shop_js_spread" => Todo("TODO: spread ... needs card-copy support."),
                "ubuntu_snap_install" => Todo("TODO: snap install needs token/junk insertion system."),
                "ubuntu_do_release_upgrade" => One(new UpgradeHandCardsEffect(1, 3)),
                "ubuntu_apt_install" => One(new GenerateCardEffect(Language.Python, Rarity.Common)),
                "ubuntu_pro_trial" => Todo("TODO: ubuntu pro trial needs unplayable token handling."),
                "mint_update_manager" => Todo("TODO: update manager needs delayed repeat system."),
                "mint_cinnamon" => Todo("TODO: cinnamon needs shield effect support."),
                "mint_nemo" => Todo("TODO: nemo needs conditional repeat based on shield."),
                "mint_timeshift" => Todo("TODO: timeshift snapshot needs delayed restore system."),
                "fedora_borrow_checker" => Todo("TODO: borrow checker needs shield and first-card tracking."),
                "fedora_cargo_build" => Todo("TODO: cargo build --release needs overkill-to-shield handling."),
                "fedora_dnf_update" => Todo("TODO: dnf update needs Java played count and cost mutation."),
                "fedora_rawhide" => Todo("TODO: rawhide needs next-card cost mutation and crash-risk hook."),
                "fedora_selinux" => Todo("TODO: SELinux enforcing needs enemy attack mitigation statuses."),
                _ => Todo("TODO: card effect is not implemented yet.")
            };
        }

        public static bool RequiresSingleTarget(CardDefinition definition)
        {
            return definition?.Id is "lang_js_console_log" or "lang_js_fetch" or "lang_js_typeof" or "mint_fix_broken"
                or "shop_py_list_append" or "shop_js_math_random" or "shop_js_promise_all";
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
                "shop_py_list_append" => $"queue: deal {UpgradeMath.ScaleAmount(4, card)}. TODO: grows when re-queued.{marker}",
                "shop_py_async_def" => $"queue: draw {UpgradeMath.ScaleAmount(2, card)}. TODO: gain 1 cycle next turn.{marker}",
                "shop_py_zip" => $"TODO: next 2 queued cards resolve twice.{marker}",
                "shop_js_math_random" => $"deal {UpgradeMath.ScaleAmount(2, card)}-{UpgradeMath.ScaleAmount(20, card)}.{marker}",
                "shop_js_promise_all" => $"deal {UpgradeMath.ScaleAmount(4, card)}-{UpgradeMath.ScaleAmount(10, card)} three times.{marker}",
                "shop_js_spread" => $"TODO: copy the lowest-cost card in hand.{marker}",
                "ubuntu_ask_ubuntu" => $"queue: draw {UpgradeMath.ScaleAmount(2, card)}.{marker}",
                "ubuntu_unattended_upgrades" => $"queue: gain {UpgradeMath.ScaleShield(4, card)} shield now and next 2 turns.{marker}",
                "ubuntu_do_release_upgrade" => $"upgrade {UpgradeMath.ScaleAmount(1, card)}-{UpgradeMath.ScaleAmount(3, card)} random hand cards.{marker}",
                "ubuntu_apt_install" => $"queue: generate a random common Python card at 0c.{marker}",
                "mint_fix_broken" => $"deal {UpgradeMath.ScaleAmount(8, card)}. cleanse harmful statuses.{marker}",
                _ => string.IsNullOrWhiteSpace(card.Definition.Description) ? "TODO: effect not implemented." : $"{card.Definition.Description}{marker}"
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

    }
}
