using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace junklite.Tests
{
    /// <summary>
    /// Air side attacks: each step of the weapon's air combo once per jump, in order, and landing
    /// gives the chain back. A one-step air combo is one air attack per jump.
    /// </summary>
    public sealed class AirComboBudgetTests
    {
        private readonly List<Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in owned) if (o != null) Object.DestroyImmediate(o);
            owned.Clear();
        }

        private MeleeWeaponData MakeWeapon(params string[] airSteps)
        {
            var weapon = ScriptableObject.CreateInstance<MeleeWeaponData>();
            owned.Add(weapon);
            weapon.sideCombo = new[] { Step("Ground_1"), Step("Ground_2") };
            weapon.airSideCombo = new MeleeWeaponData.MeleeComboStep[airSteps.Length];
            for (int i = 0; i < airSteps.Length; i++) weapon.airSideCombo[i] = Step(airSteps[i]);
            return weapon;
        }

        private static MeleeWeaponData.MeleeComboStep Step(string animation) =>
            new() { animationName = animation, damageMultiplier = 1f };

        /// <summary>Starts and finishes one attack; returns the animation, or null when refused.</summary>
        private static string Swing(CombatState combat, MeleeWeaponData weapon, AttackDirection direction, bool grounded)
        {
            if (!combat.TryBeginAttack(direction, grounded, weapon, out _, out string animation)) return null;
            combat.OnAttackComplete(direction, grounded, weapon);
            combat.Tick(1f); // past the cooldown; the combo window is long enough to stay open
            return animation;
        }

        [Test]
        public void AOneStepAirComboAllowsOneAirAttackPerJump()
        {
            MeleeWeaponData weapon = MakeWeapon("Air_1");
            var combat = new CombatState(weapon, 0f, 5f);

            Assert.That(Swing(combat, weapon, AttackDirection.Side, false), Is.EqualTo("Air_1"));
            Assert.That(Swing(combat, weapon, AttackDirection.Side, false), Is.Null, "no second air attack before landing");
            Assert.That(Swing(combat, weapon, AttackDirection.Side, false), Is.Null);

            combat.ResetAirChain(); // landed
            Assert.That(Swing(combat, weapon, AttackDirection.Side, false), Is.EqualTo("Air_1"), "landing gives the air attack back");
        }

        [Test]
        public void AThreeStepAirComboPlaysInOrderOncePerJump()
        {
            MeleeWeaponData weapon = MakeWeapon("Air_1", "Air_2", "Air_3");
            var combat = new CombatState(weapon, 0f, 5f);

            Assert.That(Swing(combat, weapon, AttackDirection.Side, false), Is.EqualTo("Air_1"));
            Assert.That(Swing(combat, weapon, AttackDirection.Side, false), Is.EqualTo("Air_2"));
            Assert.That(Swing(combat, weapon, AttackDirection.Side, false), Is.EqualTo("Air_3"));
            Assert.That(Swing(combat, weapon, AttackDirection.Side, false), Is.Null, "the chain does not loop in the air");
            Assert.That(combat.AirSideAttacksThisAirtime, Is.EqualTo(3));

            combat.ResetAirChain();
            Assert.That(Swing(combat, weapon, AttackDirection.Side, false), Is.EqualTo("Air_1"), "the next jump starts from step 1");
        }

        [Test]
        public void GroundAttacksAreNeverLimitedByTheAirBudget()
        {
            MeleeWeaponData weapon = MakeWeapon("Air_1");
            var combat = new CombatState(weapon, 0f, 5f);
            Assert.That(Swing(combat, weapon, AttackDirection.Side, false), Is.EqualTo("Air_1"));
            Assert.That(Swing(combat, weapon, AttackDirection.Side, true), Is.Not.Null);
            Assert.That(Swing(combat, weapon, AttackDirection.Side, true), Is.Not.Null);
            Assert.That(Swing(combat, weapon, AttackDirection.Side, true), Is.Not.Null, "the ground chain keeps looping");
        }

        [Test]
        public void ShippedBladesHaveOneAirAttackAndTheGruntBarelyHangs()
        {
            var blades = AssetDatabase.LoadAssetAtPath<MeleeWeaponData>("Assets/Game/Scripts/Weapons/Sword.asset");
            Assert.That(blades.airSideCombo.Length, Is.EqualTo(1));

            var grunt = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/ENEMIES/Grunt/Grunt Enemy.prefab");
            var launch = new SerializedObject(grunt.GetComponent<EnemyCharacter>()).FindProperty("launch");
            float hangSpeed = launch.FindPropertyRelative("apexHangSpeed").floatValue;
            float hangGravity = launch.FindPropertyRelative("apexHangGravityScale").floatValue;
            float gravity = launch.FindPropertyRelative("gravityScale").floatValue;
            float hangSeconds = 2f * hangSpeed / (Physics.gravity.magnitude * hangGravity);
            Assert.That(hangSeconds, Is.LessThan(0.3f), "the float at the top stays short");
            Assert.That(launch.FindPropertyRelative("spikeGravityScale").floatValue, Is.GreaterThan(gravity),
                "a spike must still fall faster than a normal launch");
        }
    }
}
