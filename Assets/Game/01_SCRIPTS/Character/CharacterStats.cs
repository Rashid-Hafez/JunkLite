using System.Collections.Generic;
using UnityEngine;
namespace junklite
{
    [CreateAssetMenu(fileName = "New Character Stats", menuName = "Junklite/Character Stats")]
    public class CharacterStats : ScriptableObject
    {
        [Header("Identity")]
        public string characterName = "Character";

        [Header("Combat")]
        public float damage = 10f;
        public float armor = 0f;

        [Header("Movement")]
        public float moveSpeed = 5f;
        public float jumpForce = 10f;

        [Header("Abilities")]
        public float dashForce = 20f;
        public float dashDuration = 0.2f;

        [Header("Attributes")]
        public List<Attribute> attributes = new List<Attribute>();

        /// <summary>Get attribute by name</summary>
        public Attribute GetAttribute(string attributeName)
        {
            return attributes.Find(attr =>
                attr.name.Equals(attributeName, System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Check if has attribute</summary>
        public bool HasAttribute(string attributeName)
        {
            return GetAttribute(attributeName) != null;
        }
    }
}
