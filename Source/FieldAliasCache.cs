using System;

namespace BetterLoading
{
    struct FieldAliasCache : IEquatable<FieldAliasCache>
    {
        public Type type;
        public string fieldName;

        public FieldAliasCache(Type type, string fieldName)
        {
            this.type = type;
            this.fieldName = fieldName.ToLower();
        }

        public bool Equals(FieldAliasCache other) => type == other.type && string.Equals(fieldName, other.fieldName);
    }
}