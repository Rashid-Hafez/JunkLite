using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace junklite.Tests
{
    public sealed class WeaponDurabilityUITests
    {
        private readonly List<Object> cleanup = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = cleanup.Count - 1; i >= 0; i--)
                if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
            cleanup.Clear();
        }

        [Test]
        public void ConsumedDurabilityShrinksHudAndRebindingRestoresFullWidthImmediately()
        {
            Image fill = CreateFill();
            WeaponSlotUI slot = fill.transform.parent.gameObject.AddComponent<WeaponSlotUI>();
            slot.Configure(null, fill, null);
            WeaponInstance weapon = CreateWeapon();
            slot.Bind(weapon);
            Assert.That(MeshWidth(fill), Is.EqualTo(100f).Within(0.01f));

            for (int i = 0; i < 10; i++) weapon.ConsumeDurability();
            typeof(WeaponSlotUI).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(slot, null);

            Assert.That(weapon.CurrentDurability, Is.EqualTo(90f));
            Assert.That(MeshWidth(fill), Is.EqualTo(90f).Within(0.01f));

            slot.Bind(CreateWeapon());
            Assert.That(MeshWidth(fill), Is.EqualTo(100f).Within(0.01f));
        }

        [TestCase(0f)]
        [TestCase(0.5f)]
        [TestCase(1f)]
        public void SpriteLessBarRendersTheRequestedFraction(float fraction)
        {
            Image fill = CreateFill();
            UIFillUtility.SetHorizontalFill(fill, fraction);
            Assert.That(MeshWidth(fill), Is.EqualTo(100f * fraction).Within(0.01f));
        }

        [Test]
        public void SpriteBackedBarKeepsItsRectangleAndUsesFilledGeometry()
        {
            Image fill = CreateFill();
            Texture2D texture = Texture2D.whiteTexture;
            fill.sprite = Track(Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f)));

            UIFillUtility.SetHorizontalFill(fill, 0.5f);

            Assert.That(fill.rectTransform.rect.width, Is.EqualTo(100f).Within(0.01f));
            Assert.That(MeshWidth(fill), Is.EqualTo(50f).Within(0.01f));
        }

        private Image CreateFill()
        {
            GameObject track = Track(new GameObject("Durability Track", typeof(RectTransform)));
            ((RectTransform)track.transform).sizeDelta = new Vector2(100f, 5f);
            var child = new GameObject("Durability Fill", typeof(RectTransform), typeof(Image));
            child.transform.SetParent(track.transform, false);
            Image image = child.GetComponent<Image>();
            image.rectTransform.anchorMin = Vector2.zero;
            image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = 0;
            return image;
        }

        private WeaponInstance CreateWeapon()
        {
            MeleeWeaponData data = Track(ScriptableObject.CreateInstance<MeleeWeaponData>());
            data.maxWeaponDurability = 100;
            data.durabilityPerHit = 1f;
            WeaponInstance weapon = Track(new GameObject("Sword")).AddComponent<WeaponInstance>();
            weapon.weaponData = data;
            return weapon;
        }

        private static float MeshWidth(Image image)
        {
            using var vertices = new VertexHelper();
            typeof(Image).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(VertexHelper) }, null).Invoke(image, new object[] { vertices });
            if (vertices.currentVertCount == 0) return 0f;

            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            UIVertex vertex = default;
            for (int i = 0; i < vertices.currentVertCount; i++)
            {
                vertices.PopulateUIVertex(ref vertex, i);
                min = Mathf.Min(min, vertex.position.x);
                max = Mathf.Max(max, vertex.position.x);
            }
            return max - min;
        }

        private T Track<T>(T value) where T : Object
        {
            cleanup.Add(value);
            return value;
        }
    }
}
